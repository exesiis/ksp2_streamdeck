# Game API notes

Notes on the game calls this mod uses. The "Status" column says what was found and what was later validated in game; the sections at the end record what was validated in game.

## Basic access
| Need | Call found |
|---|---|
| Game instance | `GameManager.Instance.Game` |
| Active vessel | `Game.ViewController.TryGetActiveSimVessel(out VesselComponent v)` (or `GetActiveSimVessel()`) |
| Mod class | `Redux.ExtraModTypes.KerbalMod` (`OnPreInitialized`, `OnInitialized`, `OnPostInitialized`, `OnCampaignLoaded`) |

## Telemetry (read)
| Protocol value | Source (`VesselComponent v`) | Status |
|---|---|---|
| `altitudeSeaLevel`, `altitudeSurface` | `v.AltitudeFromSeaLevel`, `v.AltitudeFromSurface` | found |
| `stageCount` | `v.SimulationObject.FindComponent<StagingComponent>().StageCount` (same source as the HUD, `StagingDataProvider.GetStageCount`) | found |
| `stageReady` | `StageCount > 1`, or `StageCount == 1` and `!AvailableStages[0].IsActive` (rule of `StagingDataProvider.GetHasReadyStage`) | found |
| `heading` | `v.Heading` (degrees; same source as the HUD, `TelemetryDataProvider.GetVesselHeading`) | found |
| `altitudeMode` | `v.altimeterMode` (`GroundLevel` = surface, `SeaLevel` = orbit); the game switches it through `mainBody.navballSwitchAltitudeHigh` / `Low` (default 15000 / 13000 m, per body) in `OnUpdateAltimeterMode` | found |
| `speedSurface` | `v.SrfSpeedMagnitude` | found |
| `speedOrbital` | `v.OrbitalSpeed` | found |
| `apoapsis`, `periapsis` | `v.Orbit.ApoapsisArl`, `v.Orbit.PeriapsisArl` (`Arl` = above ground level; without `Arl` = from the body's center) | found |
| `timeToApoapsis` | `IPatchedOrbit.TimeToAp` (via `v.Orbiter.PatchedConicsOrbit`) | found, check outside orbit |
| `deltaV` | `v.VesselDeltaV.TotalDeltaVActual` (also `TotalDeltaVVac`) | found |
| `fuelPct` | `v.FuelPercentage` (also `StageFuelPercentage`) | found |
| `throttle` | `v.GetVehicle()`/`VesselVehicle.mainThrottle` | found, exact path to confirm |
| `sas`, `sasMode` | `v.Autopilot.Enabled` (real on/off state) and `v.Autopilot.AutopilotMode` (`AutopilotMode`: StabilityAssist, Prograde, Retrograde, Normal, Antinormal, RadialIn, RadialOut, Target, AntiTarget, Maneuver, Navigation, Autopilot). When off, the mode falls back to StabilityAssist (`Deactivate`). | validated in game |
| `rcs` | `v.IsRCSEnabled` | found |
| `gear`, `lights`, `brakes` | `VesselVehicle.brakes`, `.headlight`, `.gearUp/.gearDown`; or the action group states (`ActionGroups`) | found |
| `timeWarp` | `TimeWarp.CurrentRate` / `CurrentRateIndex` | class found |
| node | `v.SimulationObject.ManeuverPlan.GetNodes()` -> `ManeuverNodeData` (`Time`, `BurnRequiredDV`, `BurnVector`, `BurnDuration`) | found; `Time` is an absolute time (UT): the time left is computed with the universe time |

## Commands (write)
| Protocol command | Call found | Status |
|---|---|---|
| `stage` | `v.ActivateNextStage()` | found |
| `toggle sas` | `v.SetAutopilotEnableDisable(bool)` | found |
| `sas` (mode) | `v.SetAutopilotMode(mode)` (restarts the autopilot if it is on, otherwise remembers the mode), then `v.SetAutopilotEnableDisable(true)`; returns `false` if `CanSetMode` refuses (SAS unavailable, speed < 1 m/s for the vector modes, no target or node) | written; validated in game |
| `toggle rcs` | `v.IsRCSEnabled = ...` | found |
| `toggle gear` | `VesselVehicle.SetGearState(bool up)` | found |
| `toggle lights` | `VesselVehicle.SetHeadlight(bool)` | found |
| `toggle brakes` | `v.TriggerActionGroup(KSPActionGroup.Brakes)` (toggle) | found |
| `actionGroup n` | `v.TriggerActionGroup(KSPActionGroup.CustomNN)` (`Custom01` = 0x10000, `Custom02` = 0x20000...) | found |
| `throttle` | `VesselVehicle.SetMainThrottle(float)`; check `v.HasControlForThrottleMinMax()` | found |
| `warp` | `TimeWarp.IncreaseTimeWarp()`, `DecreaseTimeWarp()`, `StopTimeWarp()` | found |
| `node warpTo` | `ManeuverPlanComponent.WarpToNode(ManeuverNodeData)` | found |
| `node adjust` | `ManeuverPlanComponent.UpdateChangeOnNode(node, Vector3 change)` (change = delta on the radial/normal/prograde axes, order to confirm) | found, axis order to confirm |

## Additions
| Need | Call found | Status |
|---|---|---|
| EC, MP | `ResourceContainerGroup.GetResourceStoredUnits(id)` / `GetResourceCapacityUnits(id)` with the `ElectricCharge` and `MonoPropellant` resources | found |
| Pause / resume | `ViewController.SetPause(bool)`, read `ViewController.IsPaused` | found |
| Launch from the pad | 100 % throttle + `ActivateNextStage()`; a landed vessel: `LandedOrSplashed` | found |
| Launch from the VAB | only the internal message `VABLaunchButtonPressedMessage` | out of scope |

## SAS: surface and orbit (validated in game, 2026-10-28)
The `Normal`, `Antinormal`, `RadialIn`, `RadialOut` values of `AutopilotMode` aim at different directions depending on `v.speedMode` (`VesselAutopilot.SetAutopilot`): on the surface they are respectively North, South, Down and Up of the horizon (`HorizonNorth/South/Down/Up`). The wheel in the interface has six slots that do not sit in the same place in the two modes. `Autopilot.Enabled` is the state of the round SAS button; when it is off, the mode falls back to `StabilityAssist` and the game greys out all modes (`CanSetMode` does not know about it). The game's serializer tags arrays (`$type`/`$values`): the mod sends strings.

## App bar windows (version 0.3, validated in game 2026-11-04)
The game's app bar (`KSP.UI.Flight.AppBar`) binds each icon to a boolean property of `ViewController.DataProvider` (`ViewDataProvider`). The mod reads and writes the same properties.

| Window | Read | Write | Notes |
|---|---|---|---|
| Research inventory | `GetResearchManagerVisible()` | `SetResearchManagerVisible(bool)` (`Game.ResearchInventoryManager.IsVisible`) | refused outside flight, and in sandbox (the game mode's `ScienceExperiments`) |
| Mission tracker | `GetMissionTrackerVisible()` | `SetMissionTrackerVisible(bool)` (`Game.MissionControlManager.MissionTracker.SetVisible`) | |
| Resource manager | `GetResourceManagerVisible()` | `SetResourceManagerVisible(bool)` (`Game.ResourceManager.IsVisible`) | flight view or colony |

Protocol command: `{"cmd":"app","name":"research|missions|resources"}` (toggle). Telemetry: `apps`, the list of open windows.
