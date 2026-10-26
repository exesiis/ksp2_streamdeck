using System;
using KSP;
using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
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
				}

				if (!view.TryGetActiveSimVessel(out VesselComponent v) || v == null) { _log(cmd + ": pas de vaisseau actif"); return; }

				switch (cmd)
				{
					case "stage": v.ActivateNextStage(); Log(cmd, o); break;
					case "toggle": Toggle(v, (string)o["name"]); break;
					case "throttle": Throttle(view, v, o); break;
					case "launch": Launch(v); break;
					default: _log("commande inconnue ignoree: " + cmd); break;
				}
			}
			catch (Exception e) { _log(cmd + ": echec " + e.GetType().Name + " " + e.Message); }
		}

		private void Log(string cmd, JObject o) { _log("commande " + o.ToString(Newtonsoft.Json.Formatting.None)); }

		/// <summary>Bascule explicite (on -> off, off -> on) pour que le jeu et l'affichage restent d'accord.</summary>
		private void Toggle(VesselComponent v, string name)
		{
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
