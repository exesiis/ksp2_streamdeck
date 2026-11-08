# Stream Deck Bridge for KSP2 Redux

A mod for **Kerbal Space Program 2** with **Redux** that sends live flight data to a local WebSocket server and accepts commands from it. It is the game-side half of the **Flight Program** plugin for Elgato Stream Deck (Stream Deck + and 15-key models), which turns the data into keys and dials: altitude, speed, stage, SAS modes, maneuver nodes, action groups and more.

Any other WebSocket client can use it too: the protocol is described in [PROTOCOL.md](PROTOCOL.md).

```
KSP2 Redux (this mod)  <-- JSON over WebSocket, 127.0.0.1 -->  Flight Program (Stream Deck plugin)
```

> Unofficial. Not affiliated with, endorsed by or sponsored by Intercept Games, Take-Two Interactive or Elgato.

## Requirements

- Kerbal Space Program 2 with **Redux** installed
- To use a Stream Deck: the Stream Deck app (6.9 or later) and the **Flight Program** plugin, installed from the Elgato Marketplace

## Which download do I need?

Redux comes in two layouts, and the mod must match yours. Open the `KSP2_x64_Data\Managed` folder in the game folder:

| If this file exists | You have | Download |
|---|---|---|
| `Ksp2Redux.dll` | Redux **snapshot** (for example 0.2.9.0 "26w44a") | the **Redux snapshot** zip |
| it does not exist | Redux **stable** (for example 0.2.8.5) | the **Redux stable** zip |

With the wrong one, the mod does not load and the game misbehaves. If that happens, use the other zip.

## Installation

1. Close the game.
2. Copy the `ksp2_streamdeck` folder (it contains `Ksp2StreamDeck.dll` and `swinfo.json`) into the game's `mods` folder, next to `KSP2_x64.exe`.
3. Start the game, load a save and take control of a vessel.
4. Install the Flight Program plugin from the Elgato Marketplace. It installs its profiles for the Stream Deck models you have connected.

No port setting is needed: the mod and the plugin find each other on their own.

To check that the mod is running, the game log (`Player.log`, in `C:\Users\<name>\AppData\LocalLow\Intercept Games\Kerbal Space Program 2\`) should contain:

```
[ksp2_streamdeck] [StreamDeck] serveur WebSocket sur ws://127.0.0.1:8765
[ksp2_streamdeck] [StreamDeck] client connecte (1)
```

(The log messages are in French.)

## Troubleshooting

| Symptom | What to check |
|---|---|
| No `ksp2_streamdeck` line in the log | The mod is not loaded. Check that `mods\ksp2_streamdeck` contains `swinfo.json` and `Ksp2StreamDeck.dll`, and that the DLL matches your Redux version (see above). |
| `Could not load file or assembly 'Ksp2Redux'` or `'ReduxLib'` | Wrong DLL for this Redux version. Use the other zip. |
| `port 8765 indisponible` (port unavailable) | Normal if Windows or another program holds that port: the mod takes the next one. Only `aucun port disponible` (no port available) is a real failure. |
| A value stays at `0` or is wrong | Look for `lecture '...' impossible` in the log: the mod isolates each read and reports the ones that fail. |
| A Redux update broke a reading | All game reads are in `src/GameBridge.cs`, all commands in `src/GameCommands.cs`. |
| A SAS mode key does nothing | Look for `etat sas=` and `sas:` in the log. |

## Building from source

Requirements: the .NET 8 SDK and a game installation with Redux.

```
powershell -ExecutionPolicy Bypass -File deploy.ps1
```

The script builds the mod against your game installation and copies it to `mods\ksp2_streamdeck\`. It looks for the game in the default Steam folder, `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program 2`. For another location, use `-GameDir "C:\path\to\the\game"`. It refuses to run while the game is open.

The Redux layout is detected automatically (or force it with `-p:ReduxLayout=old|new` when using `dotnet build`). To ship both variants, build once for each Redux version.

| Redux | Detected by | References |
|---|---|---|
| snapshot (new layout) | `Ksp2Redux.dll` exists | `Ksp2Redux` |
| stable (old layout) | it does not | `Assembly-CSharp`, `ReduxLib`, `SpaceWarp2` |

## The port

The server listens on `127.0.0.1` only. It tries port **8765**, then its nine neighbours (up to 8774), then a free port picked by Windows (Docker or Hyper-V can reserve port ranges). The port in use is written to `ksp2_streamdeck\port.txt` in `%LOCALAPPDATA%`, which is deleted when the mod stops. A client reads that file, then tries the range 8765-8774.

## Security

- The server refuses any request carrying an `Origin` header, so a web page open in a browser cannot send commands to it.
- Only whitelisted commands are executed (stage, lights, gear, RCS, SAS and its modes, throttle, warp, pause, launch, speed mode, action groups, views, maneuver nodes, app bar windows). Everything else is ignored and logged.

## Contents

```
src/GameBridge.cs         reads the game (the only place that talks to the internal API)
src/GameCommands.cs       runs commands, whitelist
src/StreamDeckMod.cs      entry point, 10 Hz broadcast, port selection
src/WebSocketServer.cs    hand-written WebSocket server (RFC 6455)
docs/api-jeu.md           game calls used and what was validated in game (in French)
PROTOCOL.md               contract with clients
```

## License

[MIT](LICENSE). Contributions and issues are welcome.
