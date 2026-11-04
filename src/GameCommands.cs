using System;
using System.Collections.Generic;
using KSP;
using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.Maneuver;
using Newtonsoft.Json.Linq;

namespace Ksp2StreamDeck
{
	/// <summary>
	/// Execute les commandes du Stream Deck. A appeler depuis le thread principal d'Unity.
	/// Liste blanche stricte: toute commande inconnue ou mal formee est ignoree et journalisee.
	/// </summary>
	public sealed class GameCommands
	{
		private readonly Action<string> _log;

		public GameCommands(Action<string> log) { _log = log; }

		public void Execute(string json)
		{
			JObject o;
			try { o = JObject.Parse(json); }
			catch { _log("commande illisible ignoree"); return; }

			string cmd = (string)o["cmd"];
			var game = GameManager.Instance?.Game;
			if (cmd == null || game?.ViewController == null) return;
			var view = game.ViewController;

			try
			{
				// Commandes qui ne demandent pas de vaisseau
				switch (cmd)
				{
					case "pause": view.SetPause((bool)o["value"]); Log(cmd, o); return;
					case "warp": Warp(view, o); return;
					case "view": View(game, o); return;
					case "app": App(game, o); return;
				}

				if (!view.TryGetActiveSimVessel(out VesselComponent v) || v == null) { _log(cmd + ": pas de vaisseau actif"); return; }

				switch (cmd)
				{
					case "stage": v.ActivateNextStage(); Log(cmd, o); break;
					case "toggle": Toggle(v, (string)o["name"]); break;
					case "throttle": Throttle(view, v, o); break;
					case "launch": Launch(v); break;
					case "sas": Sas(v, o); break;
					case "speedMode": SpeedMode(view, o); break;
					case "actionGroup": ActionGroup(v, o); break;
					case "node": Node(game, v, o); break;
					default: _log("commande inconnue ignoree: " + cmd); break;
				}
			}
			catch (Exception e) { _log(cmd + ": echec " + e.GetType().Name + " " + e.Message); }
		}

		private void Log(string cmd, JObject o) { _log("commande " + o.ToString(Newtonsoft.Json.Formatting.None)); }

		/// <summary>Bascule explicite (on -> off, off -> on) pour que le jeu et l'affichage restent d'accord.</summary>
		private void Toggle(VesselComponent v, string name)
		{
			if (name == "rcs")
			{
				v.IsRCSEnabled = !v.IsRCSEnabled;
				_log("commande toggle rcs -> " + (v.IsRCSEnabled ? "on" : "off"));
				return;
			}

			KSPActionGroup group;
			switch (name)
			{
				case "lights": group = KSPActionGroup.Lights; break;
				case "gear": group = KSPActionGroup.Gear; break;
				default: _log("toggle: '" + name + "' non pris en charge"); return;
			}
			bool on = v.GetActionGroupState(group) == KSPActionGroupState.True;
			v.SetActionGroup(group, !on);
			_log("commande toggle " + name + " -> " + (!on ? "on" : "off"));
		}

		// Modes SAS acceptes (liste blanche): ceux de l'interface du jeu (Target et AntiTarget demandent une cible selectionnee, Maneuver un plan de manoeuvre: sinon le jeu refuse)
		internal static readonly HashSet<string> SasModes = new HashSet<string>
		{
			"StabilityAssist", "Prograde", "Retrograde", "Normal", "Antinormal", "RadialIn", "RadialOut", "Target", "AntiTarget", "Maneuver",
		};

		/// <summary>
		/// {"cmd":"sas","mode":"Prograde"}: choisit le mode et allume le pilote automatique s'il est eteint.
		/// {"cmd":"sas","value":false}: eteint le pilote automatique.
		/// Le jeu refuse certains modes (par exemple verrouillage sur la vitesse a l'arret): journalise.
		/// </summary>
		private void Sas(VesselComponent v, JObject o)
		{
			if (v.Autopilot == null) { _log("sas: pas de pilote automatique"); return; }

			if (o["mode"] != null)
			{
				string name = (string)o["mode"];
				AutopilotMode mode;
				if (!SasModes.Contains(name) || !Enum.TryParse(name, out mode)) { _log("sas: mode '" + name + "' non pris en charge"); return; }
				// SetMode relance le pilote avec ce mode s'il est deja allume; sinon il le memorise et on l'allume ensuite
				if (!v.SetAutopilotMode(mode)) { _log("sas: mode " + name + " impossible dans cet etat"); return; }
				if (!v.Autopilot.Enabled && !v.SetAutopilotEnableDisable(true)) { _log("sas: allumage impossible"); return; }
				_log("commande sas " + name);
			}
			else if (o["value"] != null)
			{
				bool on = (bool)o["value"];
				if (v.Autopilot.Enabled == on) return;
				if (!v.SetAutopilotEnableDisable(on)) { _log("sas: bascule impossible"); return; }
				_log("commande sas " + (on ? "on" : "off"));
			}
			else _log("sas: ni mode ni value");
		}

		/// <summary>{"cmd":"speedMode","mode":"orbit|surface|target"}: affichage de vitesse du jeu (target seulement avec une cible).</summary>
		private void SpeedMode(ViewController view, JObject o)
		{
			SpeedDisplayMode mode;
			if (!Enum.TryParse((string)o["mode"], true, out mode)) { _log("speedMode: mode illisible"); return; }
			if (!view.TryGetActiveVehicle(out IVehicle vehicle)) { _log("speedMode: pas de vehicule actif"); return; }
			vehicle.SetSpeedDisplayMode(mode);
			Log("speedMode", o);
		}

		/// <summary>{"cmd":"actionGroup","index":1..10}: bascule le groupe d'action personnalise.</summary>
		private void ActionGroup(VesselComponent v, JObject o)
		{
			int i = o["index"] != null ? (int)o["index"] : 0;
			if (i < 1 || i > 10) { _log("actionGroup: index hors 1..10"); return; }
			var group = (KSPActionGroup)(0x10000u << (i - 1));
			bool on = v.GetActionGroupState(group) == KSPActionGroupState.True;
			v.SetActionGroup(group, !on);
			_log("commande actionGroup " + i + " -> " + (!on ? "on" : "off"));
		}

		/// <summary>{"cmd":"view","name":"flight|map"}: vue vol ou carte (pas la station de controle: on perdrait la telemetrie).</summary>
		private void View(GameInstance game, JObject o)
		{
			string name = (string)o["name"];
			bool isMap = game.GlobalGameState.GetGameState().GameState == GameState.Map3DView;
			// Memes appels que ViewDataProvider.SetMapEnabled
			if (name == "map" && !isMap) { game.GlobalGameState.SetState(GameState.Map3DView); game.Map.EnableFlightMap(); }
			else if (name == "flight" && isMap) { game.GlobalGameState.BackToPreviousState(); game.Map.DisableMap(); }
			else return;
			Log("view", o);
		}

		/// <summary>
		/// {"cmd":"app","name":"research|missions|resources"}: ouvre la fenetre si elle est fermee, la ferme sinon
		/// (memes proprietes que les icones de la barre d'application du jeu; le jeu refuse l'ouverture hors des vues permises).
		/// </summary>
		private void App(GameInstance game, JObject o)
		{
			var dp = game.ViewController.DataProvider;
			if (dp == null) { _log("app: fournisseur de donnees indisponible"); return; }
			switch ((string)o["name"])
			{
				case "research":
					if (game.ResearchInventoryManager == null) { _log("app: inventaire de recherche indisponible ici"); return; }
					dp.SetResearchManagerVisible(!dp.GetResearchManagerVisible()); break;
				case "missions":
					if (game.MissionControlManager?.MissionTracker == null) { _log("app: suivi de mission indisponible ici"); return; }
					dp.SetMissionTrackerVisible(!dp.GetMissionTrackerVisible()); break;
				case "resources":
					if (game.ResourceManager == null) { _log("app: gestionnaire de ressources indisponible ici"); return; }
					dp.SetResourceManagerVisible(!dp.GetResourceManagerVisible()); break;
				default: _log("app: fenetre inconnue"); return;
			}
			Log("app", o);
		}

		/// <summary>{"cmd":"node","op":"warpTo|delete"}: agit sur le prochain noeud de manoeuvre.</summary>
		private void Node(GameInstance game, VesselComponent v, JObject o)
		{
			var plan = v.SimulationObject.ManeuverPlan;
			var nodes = plan?.GetNodes();
			if (nodes == null || nodes.Count == 0) { _log("node: pas de noeud"); return; }
			var first = nodes[0];
			foreach (var n in nodes) if (n.Time < first.Time) first = n;
			switch ((string)o["op"])
			{
				case "warpTo": plan.WarpToNode(first); break;
				// Meme chemin que la corbeille de la carte (qui retire aussi les noeuds suivants)
				case "delete": game.SpaceSimulation.Maneuvers.RemoveNodesFromVessel(v.GlobalId, new List<ManeuverNodeData>(nodes)); break;
				default: _log("node: operation inconnue"); return;
			}
			Log("node", o);
		}

		private void Throttle(ViewController view, VesselComponent v, JObject o)
		{
			float target;
			if (o["value"] != null) target = (float)o["value"];
			else if (o["delta"] != null) target = v.flightCtrlState.mainThrottle + (float)o["delta"];
			else { _log("throttle: ni value ni delta"); return; }
			SetThrottle(view, Math.Max(0f, Math.Min(1f, target)));
		}

		private bool SetThrottle(ViewController view, float value)
		{
			if (!view.TryGetActiveVehicle(out IVehicle vehicle) || !(vehicle is VesselVehicle vv)) { _log("poussee: pas de vehicule actif"); return false; }
			vv.SetMainThrottle(value);
			return true;
		}

		/// <summary>
		/// Sur le pas de tir (vaisseau pose): active seulement le premier etage. La poussee reste celle
		/// que l'utilisateur a reglee (molette). Ignore en vol.
		/// </summary>
		private void Launch(VesselComponent v)
		{
			if (!v.LandedOrSplashed) { _log("launch ignore: le vaisseau n'est pas pose"); return; }
			v.ActivateNextStage();
			_log("commande launch");
		}

		private void Warp(ViewController view, JObject o)
		{
			TimeWarp tw = view.TimeWarp;
			if (tw == null) { _log("warp: indisponible"); return; }

			if (o["play"] != null && (bool)o["play"]) { tw.StopTimeWarp(); Log("warp", o); return; }
			if (o["stop"] != null && (bool)o["stop"]) { tw.StopTimeWarp(); Log("warp", o); return; }

			int delta = o["delta"] != null ? Math.Sign((int)o["delta"]) : 0;
			if (delta > 0) tw.IncreaseTimeWarp(); // en pause, reprend simplement le jeu
			// Ne descend jamais sous x1: le jeu mettrait en pause (reserve a l'appui sur la molette)
			else if (delta < 0 && tw.CurrentRateIndex > 0) tw.DecreaseTimeWarp();
			if (delta != 0) Log("warp", o);
		}
	}
}
