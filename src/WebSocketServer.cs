using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Ksp2StreamDeck
{
	/// <summary>
	/// Serveur WebSocket (RFC 6455) minimal: texte uniquement, ecoute sur 127.0.0.1.
	/// Ecrit a la main pour ne dependre d'aucune API reseau du jeu.
	/// </summary>
	public sealed class WebSocketServer : IDisposable
	{
		private const string Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

		private readonly TcpListener _listener;
		private readonly List<Client> _clients = new List<Client>();
		private readonly object _lock = new object();
		private volatile bool _running;

		/// <summary>Appele depuis un thread reseau a chaque message texte recu.</summary>
		public event Action<string> MessageReceived;
		public event Action<string> Log;

		public WebSocketServer(int port) { _listener = new TcpListener(IPAddress.Loopback, port); }

		public int ClientCount { get { lock (_lock) return _clients.Count; } }

		public void Start()
		{
			_running = true;
			_listener.Start();
			new Thread(AcceptLoop) { IsBackground = true, Name = "StreamDeck-Accept" }.Start();
		}

		public void Dispose()
		{
			_running = false;
			try { _listener.Stop(); } catch { /* deja arrete */ }
			lock (_lock)
			{
				foreach (var c in _clients) c.Close();
				_clients.Clear();
			}
		}

		/// <summary>Envoie un texte a tous les clients; retire ceux qui ne repondent plus.</summary>
		public void Broadcast(string text)
		{
			byte[] frame = Frame(0x1, Encoding.UTF8.GetBytes(text));
			List<Client> dead = null;
			lock (_lock)
			{
				foreach (var c in _clients)
					if (!c.TrySend(frame)) (dead ??= new List<Client>()).Add(c);
				if (dead != null) foreach (var c in dead) { _clients.Remove(c); c.Close(); }
			}
		}

		private void AcceptLoop()
		{
			while (_running)
			{
				TcpClient tcp;
				try { tcp = _listener.AcceptTcpClient(); }
				catch { break; }
				new Thread(() => Serve(tcp)) { IsBackground = true, Name = "StreamDeck-Client" }.Start();
			}
		}

		private void Serve(TcpClient tcp)
		{
			Client client = null;
			try
			{
				tcp.NoDelay = true;
				var stream = tcp.GetStream();
				stream.WriteTimeout = 250; // un client bloque ne doit pas figer le jeu
				if (!Handshake(stream)) { tcp.Close(); return; }

				client = new Client(tcp, stream);
				lock (_lock) _clients.Add(client);
				Log?.Invoke("client connecte (" + ClientCount + ")");
				ReadLoop(client);
			}
			catch (Exception e) { Log?.Invoke("client: " + e.Message); }
			finally
			{
				if (client != null) { lock (_lock) _clients.Remove(client); client.Close(); }
				else tcp.Close();
				Log?.Invoke("client deconnecte (" + ClientCount + ")");
			}
		}

		/// <summary>Poignee de main HTTP -> WebSocket. Refuse toute requete venant d'un navigateur (en-tete Origin).</summary>
		private static bool Handshake(NetworkStream stream)
		{
			var sb = new StringBuilder();
			var buf = new byte[1];
			while (sb.Length < 8192 && !sb.ToString().EndsWith("\r\n\r\n"))
			{
				if (stream.Read(buf, 0, 1) <= 0) return false;
				sb.Append((char)buf[0]);
			}

			string key = null;
			foreach (var line in sb.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
			{
				int i = line.IndexOf(':');
				if (i < 0) continue;
				string name = line.Substring(0, i).Trim().ToLowerInvariant();
				string value = line.Substring(i + 1).Trim();
				if (name == "origin") return Reject(stream, "403 Forbidden"); // page web: interdit
				if (name == "sec-websocket-key") key = value;
			}
			if (key == null) return Reject(stream, "400 Bad Request");

			string accept = Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(key + Guid)));
			byte[] response = Encoding.ASCII.GetBytes(
				"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
			stream.Write(response, 0, response.Length);
			return true;
		}

		private static bool Reject(NetworkStream stream, string status)
		{
			byte[] r = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
			try { stream.Write(r, 0, r.Length); } catch { /* ignore */ }
			return false;
		}

		private void ReadLoop(Client client)
		{
			var stream = client.Stream;
			stream.ReadTimeout = Timeout.Infinite;
			var message = new MemoryStream();
			while (_running)
			{
				int b0 = stream.ReadByte(), b1 = stream.ReadByte();
				if (b0 < 0 || b1 < 0) return;
				int opcode = b0 & 0x0F;
				bool fin = (b0 & 0x80) != 0, masked = (b1 & 0x80) != 0;
				long len = b1 & 0x7F;
				if (len == 126) { var l = ReadExact(stream, 2); len = (l[0] << 8) | l[1]; }
				else if (len == 127) { var e = ReadExact(stream, 8); len = 0; foreach (var x in e) len = (len << 8) | x; }
				if (len > 1 << 20) return; // message trop gros: on coupe
				byte[] mask = masked ? ReadExact(stream, 4) : null;
				byte[] payload = ReadExact(stream, (int)len);
				if (mask != null) for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];

				switch (opcode)
				{
					case 0x8: client.TrySend(Frame(0x8, payload)); return; // fermeture
					case 0x9: client.TrySend(Frame(0xA, payload)); break;  // ping -> pong
					case 0x0: case 0x1:
						message.Write(payload, 0, payload.Length);
						if (fin)
						{
							string text = Encoding.UTF8.GetString(message.ToArray());
							message.SetLength(0);
							try { MessageReceived?.Invoke(text); } catch (Exception e) { Log?.Invoke("commande: " + e.Message); }
						}
						break;
				}
			}
		}

		private static byte[] ReadExact(Stream s, int count)
		{
			var data = new byte[count];
			int got = 0;
			while (got < count)
			{
				int n = s.Read(data, got, count - got);
				if (n <= 0) throw new EndOfStreamException();
				got += n;
			}
			return data;
		}

		private static byte[] Frame(int opcode, byte[] payload)
		{
			var ms = new MemoryStream();
			ms.WriteByte((byte)(0x80 | opcode));
			if (payload.Length < 126) ms.WriteByte((byte)payload.Length);
			else if (payload.Length <= ushort.MaxValue) { ms.WriteByte(126); ms.WriteByte((byte)(payload.Length >> 8)); ms.WriteByte((byte)payload.Length); }
			else
			{
				ms.WriteByte(127);
				for (int shift = 56; shift >= 0; shift -= 8) ms.WriteByte((byte)((long)payload.Length >> shift));
			}
			ms.Write(payload, 0, payload.Length);
			return ms.ToArray();
		}

		private sealed class Client
		{
			private readonly TcpClient _tcp;
			public readonly NetworkStream Stream;
			private readonly object _writeLock = new object();

			public Client(TcpClient tcp, NetworkStream stream) { _tcp = tcp; Stream = stream; }

			public bool TrySend(byte[] frame)
			{
				try { lock (_writeLock) Stream.Write(frame, 0, frame.Length); return true; }
				catch { return false; }
			}

			public void Close() { try { _tcp.Close(); } catch { /* ignore */ } }
		}
	}
}
