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
		private const int Port = 8765;
		private const float Period = 0.1f;

		private WebSocketServer _server;
		private GameBridge _bridge;
		private GameCommands _commandRunner;
		private readonly ConcurrentQueue<string> _commands = new ConcurrentQueue<string>();
		private float _next;

		private void Info(string message) { SWLogger?.LogInfo("[StreamDeck] " + message); }

		public override void OnInitialized()
		{
			_bridge = new GameBridge(m => SWLogger?.LogWarning("[StreamDeck] " + m));
			_commandRunner = new GameCommands(Info);
			_server = new WebSocketServer(Port);
			_server.Log += Info;
			_server.MessageReceived += _commands.Enqueue;
			try
			{
				_server.Start();
				Info("serveur WebSocket sur ws://127.0.0.1:" + Port);
			}
			catch (System.Exception e)
			{
				SWLogger?.LogError("[StreamDeck] impossible d'ouvrir le port " + Port + ": " + e.Message);
			}
		}

		private void Update()
		{
			if (_server == null) return;

			// Les commandes arrivent sur un thread reseau; elles s'executent ici, sur le thread principal d'Unity.
			while (_commands.TryDequeue(out string cmd)) _commandRunner.Execute(cmd);

			if (Time.unscaledTime < _next) return;
			_next = Time.unscaledTime + Period;
			if (_server.ClientCount == 0) return; // personne n'ecoute: on ne lit pas le jeu pour rien

			var data = _bridge.ReadTelemetry();
			_server.Broadcast(JsonConvert.SerializeObject(new { type = "telemetry", data }));
		}

		private void OnDestroy()
		{
			_server?.Dispose();
			_server = null;
		}
	}
}
