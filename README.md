# Lucid Cats Flashlight Mod 🔦🐾

A fan-made mod for [Lucid Cats](https://store.steampowered.com/app/4778540/Lucid_Cats/) that adds a **flashlight** to the game.

Press **F** in-game to toggle a warm flashlight beam from your paw — volumetric cone included — and everyone else in your lobby sees it too, synced through the game's own networking.

## Features

- **F to toggle** your flashlight (only during real gameplay — no accidental toggles while typing in the laptop)
- **Visible to everyone** in the lobby, synced via Netcode for GameObjects custom messages (host relays, late joiners get the current state)
- **Volumetric light cone** using the game's own Volumetric Light Beam asset, so it fits the game's look
- **Held-in-paw placement** — the beam originates beside your view like a carried torch, not a headlamp
- **Tunable via config file** — brightness, range, cone width, shadows, beam opacity, offsets; no rebuild needed
- Handles the game's scene changes: players despawning/respawning between the lobby and dream levels keep their flashlight state

## Install

> Requirements: Windows, Lucid Cats (Steam), **the game must be closed while installing.**

### Option A — from this repository

1. Clone or download this repository (green **Code** button → *Download ZIP* → extract)
2. Double-click **`package\install.bat`**
3. The installer finds your Steam library automatically (or asks for the game folder)
4. Launch Lucid Cats through Steam as usual — press **F** in-game

### Option B — portable zip

Grab `LucidCatsFlashlight.zip` from the [Releases](https://github.com/ElayjHQ/Lucid-Cats-Flashlight-Mod/releases) page (if available), extract it anywhere, and run `install.bat` from inside. Same result.

### Who needs the mod?

| Situation | Requirement |
|---|---|
| You want to **use** a flashlight (F key) | You need the mod |
| You want to **see** other players' flashlights | You need the mod |
| Someone joins without the mod | They just see no beams — nothing breaks |
| The **host** doesn't have the mod | No sync happens (host relays the messages) |

## Controls

| Key | Action |
|---|---|
| `F` | Toggle your flashlight |

## Configuration

After your first launch, edit `BepInEx\config\lucidcats.flashlight.cfg` (with the game closed):

| Setting | Default | What it does |
|---|---|---|
| `Intensity` | `10` | Spotlight brightness |
| `Range` | `16` | How far the light reaches (meters) |
| `Angle` | `38` | Cone width in degrees |
| `Shadows` | `true` | Flashlight shadow casting |
| `Beam` | `true` | Show the visible volumetric cone (`false` = plain spotlight) |
| `BeamIntensity` | `0.35` | Cone opacity (0 = invisible, 1 = solid) |
| `LocalRight` | `0.35` | Beam origin offset to your right (the "held in paw" feel) |
| `LocalDown` | `0.12` | Beam origin offset below your camera |
| `LocalForward` | `0.28` | Beam origin offset in front of your camera |
| `RemoteHeight` | `0.4` | Other players' beam origin height on their model |
| `RemoteForward` | `0.35` | Other players' beam origin, forward offset |
| `RemoteYaw` | `0` | Other players' beam yaw (try `180` if a beam points backwards) |
| `RemotePitch` | `-5` | Other players' beam pitch (negative = slightly down) |

## Uninstall

Double-click **`package\uninstall.bat`** — it removes only the mod files (`BepInEx\`, `winhttp.dll`, `doorstop_config.ini`) and your game returns to vanilla. Steam file validation also restores everything cleanly.

## Build from source

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and a Lucid Cats install with the mod's BepInEx already set up (run `install.bat` once).

```powershell
$env:LUCID_CATS_DIR = "C:\Path\To\Lucid Cats"   # skip if you edit the csproj default
dotnet build src\LucidCatsFlashlight\LucidCatsFlashlight.csproj -c Release
```

Building automatically installs the DLL into the game's `BepInEx\plugins\` and refreshes `package\LucidCatsFlashlightMod\BepInEx\plugins\` so the installer stays current.

The game ships on **Unity 6000.3.11f1 (Mono)** — this mod uses **BepInEx 6.0.0-be.793** (BepInEx 5 is incompatible with this Unity version and crashes on launch).

## Troubleshooting

- **Game won't start** — run `uninstall.bat`, then check `BepInEx\LogOutput.log` in the game folder for the failing plugin
- **F does nothing** — it only works during gameplay (cursor locked); check the log for `Flashlight ON/OFF` lines
- **Beam points the wrong way for a friend** — set `RemoteYaw = 180` in the config
- **A game update broke it** — mod code may need updating after game patches; check the log for assembly errors

## Disclaimer

Fan-made, unofficial mod. Not affiliated with Kolide Studio. Use of mods in online games is at your own discretion.
