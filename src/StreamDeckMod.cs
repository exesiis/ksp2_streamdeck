using System.Collections.Concurrent;
using Newtonsoft.Json;
using Redux.ExtraModTypes;
using UnityEngine;

namespace Ksp2StreamDeck
{
	/// <summary>
	/// Point d'entree du mod. Diffuse la telemetrie a 10 Hz sur ws://127.0.0.1:8765 et
	/// execute les commandes recues (mises en file, puis jouees sur le thread principal).
	/// </summary>
	public class StreamDeckMod : KerbalMod
	{
		// Port prefere, puis les suivants si Windows ou un autre programme refuse celui-ci (le plugin cherche dans la meme plage)
		private const int Port = 8765;
		private const int PortCount = 10;
		// Le port ouvert est ecrit ici pour que le plugin le retrouve meme si Windows l'a choisi (port 0)
		private static readonly string PortFile = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "ksp2_streamdeck", "port.txt");
		private const float Period = 0.1f;

		private WebSocketServer _server;
		private GameBridge _bridge;
		private GameCommands _commandRunner;
		private readonly ConcurrentQueue<string> _commands = new ConcurrentQueue<string>();
		private float _next;
		private bool _readFailed;

		private void Info(string message) { SWLogger?.LogInfo("[StreamDeck] " + message); }

		public override void OnInitialized()
		{
			_bridge = new GameBridge(m => SWLogger?.LogWarning("[StreamDeck] " + m));
			_commandRunner = new GameCommands(Info);
			// 8765 puis ses 9 voisins, puis (port 0) un port libre choisi par Windows, qui evite les plages reservees (Docker, Hyper-V)
			for (int i = 0; i <= PortCount; i++)
			{
				int port = i < PortCount ? Port + i : 0;
				var server = new WebSocketServer(port);
				server.Log += Info;
				server.MessageReceived += _commands.Enqueue;
				try
				{
					server.Start();
					_server = server;
					Info("serveur WebSocket sur ws://127.0.0.1:" + server.Port);
					WritePortFile(server.Port);
					return;
				}
				catch (System.Exception e)
				{
					server.Dispose();
					SWLogger?.LogWarning("[StreamDeck] port " + port + " indisponible: " + e.Message);
				}
			}
			SWLogger?.LogError("[StreamDeck] aucun port disponible (ni " + Port + " a " + (Port + PortCount - 1) + ", ni un port choisi par Windows)");
		}

		private void WritePortFile(int port)
		{
			try
			{
				System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PortFile));
				System.IO.File.WriteAllText(PortFile, port.ToString());
			}
			catch (System.Exception e) { SWLogger?.LogWarning("[StreamDeck] impossible d'ecrire " + PortFile + ": " + e.Message); }
		}

		private void Update()
		{
			if (_server == null) return;

			// Les commandes arrivent sur un thread reseau; elles s'executent ici, sur le thread principal d'Unity.
			while (_commands.TryDequeue(out string cmd)) _commandRunner.Execute(cmd);

			if (Time.unscaledTime < _next) return;
			_next = Time.unscaledTime + Period;
			if (_server.ClientCount == 0) return; // personne n'ecoute: on ne lit pas le jeu pour rien

			// Pendant un changement de scene (menu, chargement) le jeu peut lever des exceptions:
			// on envoie "pas de vaisseau" (data = null) au lieu de planter, avec une seule alerte dans le journal.
			System.Collections.Generic.Dictionary<string, object> data = null;
			try { data = _bridge.ReadTelemetry(); }
			catch (System.Exception e)
			{
				if (!_readFailed) { _readFailed = true; Info("lecture de la telemetrie impossible (changement de scene ?): " + e.GetType().Name + " " + e.Message); }
			}
			_server.Broadcast(JsonConvert.SerializeObject(new { type = "telemetry", data }));
		}

		private void OnDestroy()
		{
			_server?.Dispose();
			_server = null;
			try { System.IO.File.Delete(PortFile); } catch { /* deja absent */ }
		}
	}
}
