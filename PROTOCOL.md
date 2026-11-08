# Protocol: KSP2 mod <-> Stream Deck plugin

Local WebSocket: `ws://127.0.0.1:<port>` (the mod is the server). The mod takes the first free port between **8765 and 8774** (Windows or another program, Docker for example, may refuse one); if all are refused, it asks Windows for a free port (port 0). It writes the open port to `%LOCALAPPDATA%\ksp2_streamdeck\port.txt` (deleted when the mod stops). The client reads that file, then tries the range 8765-8774, and only keeps a port once it has received telemetry. Messages are JSON.

## Mod -> client (10 Hz)
```json
{"type":"telemetry","data":{
  "vessel":"Jeb 1","situation":"Flying",
  "altitude":1234.5,"altitudeSurface":1234.5,"altitudeSeaLevel":2400,"altitudeMode":"surface","onPad":false,"paused":false,"speedSurface":210.2,"speedOrbital":230.0,"speed":210.2,"speedMode":"surface","heading":272.4,"stageCount":3,"stageReady":true,
  "apoapsis":80000,"periapsis":-500000,"timeToApoapsis":120,
  "deltaV":3200,"fuelPct":0.82,"throttle":0.5,"ec":410,"ecMax":500,"mp":30,"mpMax":60,
  "sas":true,"sasMode":"Prograde","sasAvailable":"StabilityAssist,Prograde","rcs":false,"gear":false,"lights":false,
  "timeWarp":1,"timeWarpIndex":0,"timeWarpLevels":11
}}
```
`apoapsis` and `periapsis` are `null` while the vessel is on the ground. `sasAvailable` lists, comma-separated, the SAS modes the game would accept right now (a string: the game's serializer tags arrays with `$type`/`$values`). Altitudes are in m, speeds in m/s, times in s.

## Client -> mod
| Message | Effect |
|---|---|
| `{"cmd":"stage"}` | Next stage |
| `{"cmd":"toggle","name":"sas|rcs|gear|lights"}` | Toggle |
| `{"cmd":"actionGroup","index":1}` | Action group 1..10 |
| `{"cmd":"throttle","value":0.5}` or `{"cmd":"throttle","delta":0.05}` | Throttle 0..1 |
| `{"cmd":"sas","mode":"Prograde"}` | Picks the SAS mode and turns the autopilot on if it is off. Modes: `StabilityAssist` (SAS lock), `Prograde`, `Retrograde`, `Normal`, `Antinormal`, `RadialIn`, `RadialOut` |
| `{"cmd":"sas","value":false}` | Turns the autopilot off (the game goes back to `StabilityAssist`) |
| `{"cmd":"warp","delta":1}` / `{"cmd":"warp","stop":true}` | Time warp |
| `{"cmd":"warp","play":true}` / `{"cmd":"pause","value":true}` | Back to x1 / pause or resume the game |
| `{"cmd":"launch"}` | First stage, only if `onPad`; the throttle is not changed |
| `{"cmd":"node","op":"warpTo"}` | Warp to the node |
| `{"cmd":"node","op":"adjust","axis":"prograde","delta":5}` | Adjusts the node's dV (axes: prograde, normal, radial) |

## Additions (v0.3)
Telemetry: `timeToPeriapsis`, `hasTarget`, `view` (`flight`|`map`), `groups` (action groups that are on, for example `"1,3"`), `timeWarpMax` (highest warp level allowed right now), `apps` (app bar windows that are open: `research`, `missions`, `resources`, for example `"missions"`), `hasNode`, `nodeTime` (seconds left), `nodeDv` (these replace `node`).

| Message | Effect |
|---|---|
| `{"cmd":"speedMode","mode":"orbit|surface|target"}` | Speed display mode (target only when a target exists) |
| `{"cmd":"actionGroup","index":1}` | Custom action group 1..10 (toggle) |
| `{"cmd":"view","name":"flight|map"}` | Flight view or map view |
| `{"cmd":"app","name":"research|missions|resources"}` | Opens or closes the window (research inventory, mission tracker, resource manager) |
| `{"cmd":"node","op":"warpTo|delete"}` | Warp to the next node / delete the nodes |
