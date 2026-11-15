# Lucid Cats Flashlight Mod 🔦🐾

A fan-made mod for [Lucid Cats](https://store.steampowered.com/app/4778540/Lucid_Cats/) that adds a **flashlight** to the game.

Press your configured key or mouse button in-game to summon a warm flashlight that floats beside you — volumetric cone included — and everyone else in your lobby sees it too, synced through the game's own networking.

## Features

- **Rebindable flashlight toggle** — any keyboard key or mouse button; hold the current bind for 2 s in-game, click the key box in Settings → FLASHLIGHT, or edit `ToggleKey` in the config. It only toggles during gameplay, not while in menus or the laptop.
- **Visible to everyone** in the lobby, synced via Netcode for GameObjects custom messages (host relays, late joiners get the current state)
- **Volumetric light cone** using the game's own Volumetric Light Beam asset, so it fits the game's look
- **Floating dream-artifact styling** — the torch hovers beside you with a gentle bob, a pulsing glow and motes falling away beneath it; it pops into place when toggled on and dissolves away when toggled off
- **Battery system** — 40 seconds of continuous light, then it dims and flickers as it runs dry and dies with a click; it recharges in 60 seconds while off. While lit, a resizable bar with charge percentage sits at the bottom of the screen (optional via config).
- **Tunable via config file** — brightness, range, cone width, shadows, beam opacity, offsets; no rebuild needed
- Handles the game's scene changes: players despawning/respawning between the lobby and dream levels keep their flashlight state

## Install

> Requirements: Windows, Lucid Cats (Steam), **the game must be closed while installing.**

### Option A — from this repository

1. Clone or download this repository (green **Code** button → *Download ZIP* → extract)
2. Double-click **`package\install.bat`**
3. The installer finds your Steam library automatically (or asks for the game folder)
4. Launch Lucid Cats through Steam as usual — press **F** in-game (or your configured bind)

### Option B — portable zip

Grab `LucidCatsFlashlight.zip` from the [Releases](https://github.com/ElayjHQ/Lucid-Cats-Flashlight-Mod/releases) page (if available), extract it anywhere, and run `install.bat` from inside. Same result.

### Who needs the mod?

| Situation | Requirement |
|---|---|
| You want to **use** a flashlight (F key) | You need the mod |
| You want to **see** other players' flashlights | You need the mod |
| Someone joins without the mod | They just see no beams — nothing breaks |
| The **host** doesn't have the mod | No sync happens (host relays the messages) |

> **Keep everyone on the same mod version.** v1.2.0 adds battery data to flashlight sync messages; mixed versions may not correctly show late-joiner or battery state.

## Controls

| Key | Action |
|---|---|
| `ToggleKey` (default `F`) | Toggle your flashlight |

### Rebinding the flashlight key

- **In the game's Settings menu:** open Settings → **FLASHLIGHT** tab, click the **TOGGLE KEY** box, then press a keyboard key or mouse button (Esc cancels). The tab also has sound, click volume, HUD size, glow intensity and battery HUD controls; changes apply immediately.
- **In-game:** hold the current flashlight key or mouse button for **2 seconds** — the prompt appears at the centre of the screen; press the new key or click the new mouse button (or **Esc** to cancel). Saved immediately, no restart.
- **Config file:** `Controls > ToggleKey` accepts any Unity keyboard key name (`F`, `G`, `LeftShift`, `Digit3`, `Space`, `Tab`, `Numpad0`, `CapsLock`, ...) or a mouse button: `LeftMouse`, `RightMouse`, `MiddleMouse`, `Mouse4`, `Mouse5`
- Pick a bind the game doesn't already use. Left/right mouse buttons can conflict with the game's own actions; middle or side buttons may be more convenient.

## Configuration

After your first launch, edit `BepInEx\config\lucidcats.flashlight.cfg` (with the game closed):

| Setting | Default | What it does |
|---|---|---|
| `ToggleKey` | `F` | Flashlight toggle key or mouse button (`LeftMouse`, `MiddleMouse`, `Mouse4`, `Mouse5`, ...) — hold it in-game 2 s to rebind |
| `Intensity` | `10` | Spotlight brightness |
| `Range` | `16` | How far the light reaches (meters) |
| `Angle` | `38` | Cone width in degrees |
| `Shadows` | `true` | Flashlight shadow casting |
| `Beam` | `true` | Show the visible volumetric cone (`false` = plain spotlight) |
| `BeamIntensity` | `0.35` | Cone opacity (0 = invisible, 1 = solid) |
| `Effects` | `true` | Floating-flashlight effects: hover, glow, motes |
| `GlowIntensity` | `1.2` | Strength of the pulsing glow (0 = off) |
| `Battery` | `true` | Battery system: drains while lit, recharges while off |
| `BatteryLife` | `40` | Seconds of continuous light on a full battery |
| `RechargeTime` | `60` | Seconds to fully recharge while the light is off |
| `LowThreshold` | `0.25` | Charge level where the light starts dimming (0-1) |
| `ShowBatteryHud` | `true` | Small battery bar at the bottom of the screen |
| `HudScale` | `1` | Battery HUD bar size multiplier (1 = default size) |
| `LowStatic` | `true` | Quiet static crackle while a low battery drains |
| `LucidLock` | `0.30` | Flashlight locks off while dreaming and lucidity is at/below this (0-1, `0` = off); unlocks when you wake up |
| `Sound` | `true` | Click sound on toggle |
| `Volume` | `0.45` | Click volume (0-1) |
| `ShowModel` | `true` | Show the 3D floating flashlight |
| `ModelScale` | `1` | Flashlight size multiplier |
| `LocalRight` | `0.28` | Floating flashlight offset to your right |
| `LocalDown` | `0.16` | Floating flashlight offset below your camera (higher = lower in view) |
| `LocalForward` | `0.42` | Floating flashlight offset in front of your camera |
| `LocalYaw` | `-6` | Floating flashlight aim in degrees (negative = toward the screen centre) |
| `RemoteRight` | `0` | Other players' flashlight: sideways nudge from their right hand |
| `RemoteForward` | `0.05` | Other players' flashlight: forward nudge from their right hand |
| `RemoteYaw` | `0` | Other players' flashlight yaw (try `180` if it points backwards) |
| `RemotePitch` | `-5` | Other players' flashlight pitch (negative = down) |

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
