using System.Collections.Concurrent;
using Newtonsoft.Json;
using Redux.ExtraModTypes;
using UnityEngine;

namespace Ksp2StreamDeck
{
	/// <summary>
	/// Point d'entree du mod. Phase 3: lecture seule. Diffuse la telemetrie a 10 Hz sur
	/// ws://127.0.0.1:8765. Les commandes recues sont mises en file puis journalisees
	/// (l'execution arrive en phase 4).
	/// </summary>
	public class StreamDeckMod : KerbalMod
	{
		private const int Port = 8765;
		private const float Period = 0.1f;

		private WebSocketServer _server;
		private GameBridge _bridge;
		private readonly ConcurrentQueue<string> _commands = new ConcurrentQueue<string>();
		private float _next;

		private void Info(string message) { SWLogger?.LogInfo("[StreamDeck] " + message); }

		public override void OnInitialized()
		{
			_bridge = new GameBridge(m => SWLogger?.LogWarning("[StreamDeck] " + m));
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

			// Phase 3: on vide la file pour qu'elle ne grossisse pas, sans rien executer.
			while (_commands.TryDequeue(out string cmd)) Info("commande recue (non executee en lecture seule): " + cmd);

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
