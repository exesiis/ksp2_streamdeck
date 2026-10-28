using System;
using System.Collections.Generic;
using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;

namespace Ksp2StreamDeck
{
	/// <summary>
	/// Seul endroit qui parle a l'API interne du jeu. Si une mise a jour de Redux la change,
	/// c'est ici qu'il faut corriger. Chaque champ est lu isolement: une lecture qui echoue
	/// donne la valeur par defaut et n'est journalisee qu'une fois.
	/// </summary>
	public sealed class GameBridge
	{
		private readonly Action<string> _log;
		private readonly HashSet<string> _failed = new HashSet<string>();

		public GameBridge(Action<string> log) { _log = log; }

		private static GameInstance Game => GameManager.Instance?.Game;

		/// <summary>Etat du vaisseau actif au format de PROTOCOL.md, ou null s'il n'y en a pas.</summary>
		public Dictionary<string, object> ReadTelemetry()
		{
			var game = Game;
			if (game == null || game.ViewController == null) return null;
			if (!game.ViewController.TryGetActiveSimVessel(out VesselComponent v) || v == null) return null;

			var ecId = Safe("ecId", () => game.ResourceDefinitionDatabase.GetResourceIDFromName("ElectricCharge"), default(ResourceDefinitionID));
			var mpId = Safe("mpId", () => game.ResourceDefinitionDatabase.GetResourceIDFromName("MonoPropellant"), default(ResourceDefinitionID));
			ResourceContainerGroup group = Safe<ResourceContainerGroup>("containers", () => v.SimulationObject.PartOwner.ContainerGroup, null);

			double Stored(ResourceDefinitionID id, string name) => group == null ? 0 : Safe(name + "Stored", () => group.GetResourceStoredUnits(id), 0.0);
			double Capacity(ResourceDefinitionID id, string name) => group == null ? 0 : Safe(name + "Cap", () => group.GetResourceCapacityUnits(id), 0.0);

			// AP/PE n'ont de sens qu'une fois decolle: ni sur le pas de tir, ni pose, ni a la mer
			bool hasOrbit = Safe("hasOrbit", () =>
			{
				var s = v.Situation;
				return s != VesselSituations.PreLaunch && s != VesselSituations.Landed && s != VesselSituations.Splashed;
			}, false);

			// Etagement: meme source et meme regle que le HUD du jeu. Un seul etage restant n'est "pret" que
			// s'il n'est pas deja actif (voir StagingDataProvider.GetHasReadyStage).
			var staging = Safe<StagingComponent>("staging", () => v.SimulationObject.FindComponent<StagingComponent>(), null);
			int stageCount = staging == null ? 0 : Safe("stageCount", () => staging.StageCount, 0);
			bool stageReady = staging != null && Safe("stageReady", () => stageCount > 1 || (stageCount == 1 && !staging.AvailableStages[0].IsActive), false);

			bool On(KSPActionGroup g) => Safe("group" + g, () => v.GetActionGroupState(g) == KSPActionGroupState.True, false);

			var t = new Dictionary<string, object>
			{
				["vessel"] = Safe("name", () => v.Name, ""),
				["situation"] = Safe("situation", () => v.Situation.ToString(), ""),
				["altitude"] = Safe("altSea", () => v.AltitudeFromSeaLevel, 0.0),
				["altitudeSurface"] = Safe("altSurf", () => v.AltitudeFromSurface, 0.0),
				["altitudeSeaLevel"] = Safe("altSea", () => v.AltitudeFromSeaLevel, 0.0),
				// Le jeu bascule seul entre sol et niveau de la mer selon l'astre (voir docs/api-jeu.md)
				["altitudeMode"] = Safe("altMode", () => v.altimeterMode == AltimeterDisplayMode.GroundLevel ? "surface" : "orbit", "surface"),
				["onPad"] = Safe("landed", () => v.LandedOrSplashed, false),
				["paused"] = Safe("paused", () => game.ViewController.IsPaused, false),
				["speedSurface"] = Safe("srfSpeed", () => v.SrfSpeedMagnitude, 0.0),
				// Vitesse affichee: le jeu choisit seul entre surface, orbite et cible (voir docs/api-jeu.md)
				["heading"] = Safe("heading", () => v.Heading, 0.0),
				["stageCount"] = stageCount,
				["stageReady"] = stageReady,
				["speedMode"] = Safe("speedMode", () => v.speedMode.ToString().ToLowerInvariant(), "surface"),
				["speed"] = Safe("speed", () => v.speedMode == SpeedDisplayMode.Surface ? v.SrfSpeedMagnitude : v.speedMode == SpeedDisplayMode.Target ? v.TargetSpeed : v.OrbitalSpeed, 0.0),
				["speedOrbital"] = Safe("orbSpeed", () => v.OrbitalSpeed, 0.0),
				// Sans orbite utile tant que le vaisseau est au sol: null -> le Stream Deck affiche "--"
				["apoapsis"] = hasOrbit ? Safe<object>("ap", () => v.Orbit.ApoapsisArl, null) : null,
				["periapsis"] = hasOrbit ? Safe<object>("pe", () => v.Orbit.PeriapsisArl, null) : null,
				["timeToApoapsis"] = Safe("tAp", () => ((IPatchedOrbit)v.Orbiter.PatchedConicsOrbit).TimeToAp, 0.0),
				["deltaV"] = Safe("dv", () => v.VesselDeltaV.TotalDeltaVActual, 0.0),
				["fuelPct"] = Safe("fuel", () => v.FuelPercentage, 0.0),
				["throttle"] = Safe("throttle", () => (double)v.flightCtrlState.mainThrottle, 0.0),
				["ec"] = Stored(ecId, "ec"),
				["ecMax"] = Capacity(ecId, "ec"),
				["mp"] = Stored(mpId, "mp"),
				["mpMax"] = Capacity(mpId, "mp"),
				// Etat reel du pilote automatique (et non le groupe d'action): c'est ce que le jeu affiche
				["sas"] = Safe("sas", () => v.Autopilot.Enabled, false),
				["sasMode"] = Safe("sasMode", () => v.Autopilot.AutopilotMode.ToString(), ""),
				// Modes que le jeu accepterait maintenant (CanSetMode): les touches Manoeuvre s'estompent sinon
				["sasAvailable"] = Safe("sasAvailable", () =>
				{
					var ok = new List<string>();
					foreach (string name in GameCommands.SasModes)
						if (Enum.TryParse(name, out AutopilotMode mode) && v.Autopilot.CanSetMode(mode)) ok.Add(name);
					return ok.ToArray();
				}, new string[0]),
				["rcs"] = On(KSPActionGroup.RCS),
				["gear"] = On(KSPActionGroup.Gear),
				["lights"] = On(KSPActionGroup.Lights),
				["timeWarp"] = Safe("warp", () => Math.Round((double)game.ViewController.TimeWarp.CurrentRate), 1.0),
				// Numero du niveau de warp (0 = x1) et nombre de niveaux: pour la rangee de chevrons de la molette
				["timeWarpIndex"] = Safe("warpIndex", () => game.ViewController.TimeWarp.CurrentRateIndex, 0),
				["timeWarpLevels"] = Safe("warpLevels", () => PhysicsSettings.TimeWarpLevels.Length, 11),
				["node"] = null, // phase 4+ (hors perimetre de la v1)
			};
			return t;
		}

		private T Safe<T>(string field, Func<T> read, T fallback)
		{
			try { return read(); }
			catch (Exception e)
			{
				if (_failed.Add(field)) _log("lecture '" + field + "' impossible: " + e.GetType().Name + " " + e.Message);
				return fallback;
			}
		}
	}
}
