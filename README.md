# Dimraeth Minimap

**English** | [한국어](README.ko.md)

An **unofficial** BepInEx plugin that adds a minimap to [Dimraeth](https://store.steampowered.com/app/2402680/Dimraeth/).

> **Notice**
> This is an unofficial fan-made mod, not affiliated with or endorsed by Mudtek. No game files or game-derived data are included in this repository or its releases. **It will be taken down immediately at the developer's request** — please open an issue. Use at your own risk.

![The minimap in the top-left corner showing the surrounding map, fog of war and place icons, with the F7 settings panel open below it](docs/screenshot.webp)

*The minimap (top left) with the surrounding map, fog of war and place icons, and the F7 settings panel open below it.*

## Features

- F7 offers independent **Show stamina / Show mana** switches. Hide mana to keep health and stamina, or hide both to keep health only. Hidden rows take no space.
- **Character-following bars (v0.8.0)**: health (red), stamina (green), and concentration (blue) above your own character. F6 toggles them; F7 adjusts size, vertical offset, opacity, and optional numbers. Independent of minimap visibility.
- A round (or square) minimap in a screen corner, with zoom
- **In-game settings panel (F7)**: minimap size, visible range, icon size, position, margins, opacity and shape, applied live. Korean and English
- It crops the game's own world map instead of rendering the world a second time, so it costs **next to no frame time**
- **Fog of war stays.** Undiscovered regions are covered exactly as on the map screen
- The same icons as the map screen: discovered places (waypoints, shops, camps...), quest / conversation NPCs, map fragments (collected ones greyed out), reward spots, and beacons placed by you or your party
- Targets of tracked quests (diamond + quest area) and the game's **dashed guidance routes**. Far targets and beacons stick to the rim as a direction hint. A quest area stays visible while you are inside it
- Only what the game already shows you on its map screen or HUD. No monsters, no hidden chests, no undiscovered places
- Other players in the same region as dots (on the rim when out of range)
- **Client-side only**: it draws on your screen and never touches game state or networking. You can play with people who do not have the mod
- If a game update changes something the mod relies on, that feature switches off; the game keeps running

## Install (players)

1. Download `DimraethMinimap-vX.Y.Z.zip` from [Releases](../../releases/latest) and extract it anywhere.
2. Close the game and double-click `install.bat`. It finds the game through Steam (any drive) and installs the mod. If it cannot find the game it asks for the folder.
3. Start the game. The first launch needs an internet connection and may take several minutes while the mod loader prepares its files.

**First-launch messages (normal setup)**

The BepInEx console may show the following messages (the version and paths depend on your installation):

```text
[Message:InteropManager] Downloading unity base libraries from https://unity.bepinex.dev/libraries/6000.0.61.zip
[Message:InteropManager] Extracting unity base libraries from ...
[Message:InteropManager] Running Cpp2IL to generate dummy assemblies from .../global-metadata.dat
```

These messages are not errors. BepInEx downloads and extracts libraries matching the game's Unity version, then generates assemblies that let plugins access the game. Leave the game running until setup finishes; the time depends on your PC and connection. Later launches normally reuse the generated files. This preparation can run again after a game update or if BepInEx and its generated files were removed and reinstalled.

If startup fails or errors appear afterward, include `BepInEx/LogOutput.log` when reporting the problem.

Updating is the same steps with the new zip; your settings are kept. To uninstall, run `uninstall-minimap.bat` in the game folder. For a manual install, copy the contents of the zip's `payload` folder into the game folder.

If Windows shows "Windows protected your PC" for `install.bat`, choose **More info > Run anyway**. `install.bat` and `install.ps1` are plain text; open them in Notepad to see what they do.

| Key | Action |
|---|---|
| F6 | Show / hide health, stamina, and concentration above your character |
| F7 | Open / close the **settings panel**. Keyboard (↑↓ select, ←→ adjust) or mouse (click `<` `>`); saved when closed |
| F8 | Show / hide the minimap |
| PageUp / PageDown | Zoom in / out |
| F9 | Write diagnostics to `BepInEx/LogOutput.log` (for bug reports) |

Config file: `BepInEx/config/com.yhj.dimraeth.minimap.cfg`. Most settings are in the F7 panel; the file additionally has the key bindings and, under `[Markers]`, a switch per icon type.

## Build (developers)

You need the .NET SDK 8 or newer and a copy of the game with BepInEx 6 (IL2CPP) installed and **launched once**.

```powershell
dotnet build src\DimraethMinimap -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\Dimraeth"
powershell -File tools\package.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Dimraeth"
```

The project references `BepInEx\interop\*.dll` straight from the game folder. Those assemblies are generated from game code, so they are **never committed or shipped** (`.gitignore` and a check in `package.ps1` enforce this). For the same reason there is no CI build: releases are built locally.

## Layout

| File | Role |
|---|---|
| `Plugin.cs` | Entry point, config entries |
| `MinimapController.cs` | The minimap UI. Map mode (default) and camera mode (fallback) |
| `GameAccess.cs` | Player positions, through Netcode lookups only. Never search the scene: the game keeps its whole world loaded and one search stalls it for 50-300 ms |
| `GameMap.cs` | The game's map data: world-to-map transform, discovered regions |
| `GameMarkers.cs` | Icons, quest targets and guidance routes, copied from lists the game already keeps |
| `SettingsPanel.cs` | The F7 settings panel (keyboard and mouse, Korean / English) |
| `Diagnostics.cs`, `FrameStats.cs` | F9 diagnostics, frame hitch counters |

`VitalBarsAccess.cs` reads the local player's current and maximum vitals; `VitalBarsController.cs` renders the bars and follows the player's screen position. `VitalBarsBehaviour.cs` and `VitalBarsConfig.cs` provide the independent lifecycle and configuration. Values refresh at 10 Hz and position updates every frame. The `[VitalBars]` config section also exposes these settings.

Game data access is read-only. The bars do not modify stats, items, saves, or network values. When a game update breaks a feature, that feature switches off.

## License

MIT. Release zips include an unmodified copy of [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1).
