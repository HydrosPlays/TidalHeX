<p align="center">
  <img src="PKHeX.WinForms/Tidal/Assets/logo.png" width="150" alt="TidalHeX logo">
</p>

<h1 align="center">TidalHeX</h1>

<p align="center">
  A modern, console-style interface for <a href="https://github.com/kwsch/PKHeX">PKHeX</a>, the Pokémon core series save editor.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/License-GPLv3-blue.svg" alt="License: GPLv3">
  <img src="https://img.shields.io/badge/based%20on-PKHeX-1a8fe6.svg" alt="Based on PKHeX">
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078d4.svg" alt="Platform: Windows 10 | 11">
</p>

![TidalHeX home screen](.github/screenshots/home.webp)

TidalHeX is a fork of PKHeX that replaces the classic Windows Forms window with a new animated interface, styled after a game console's home menu and the Rotom Dex. Underneath, it is still PKHeX: saves are read and written by PKHeX's own engine (`PKHeX.Core`, unchanged), with the same legality checker and the same encounter and Mystery Gift data. TidalHeX edits your saves exactly the way PKHeX does.

PKHeX plugins work too: drop them in the `plugins` folder next to the exe and their commands appear under Save Tools. The classic PKHeX window is still one click away for anything the new interface doesn't cover yet.

**We do not support or condone cheating at the expense of others. Do not use significantly hacked Pokémon in battle or in trades with those who are unaware hacked Pokémon are in use.**

## Screenshots

| Boxes | Pokémon editor |
|:---:|:---:|
| ![Boxes](.github/screenshots/boxes.webp) | ![Pokémon editor](.github/screenshots/editor.webp) |
| **Stats** | **Encounter database** |
| ![Stats](.github/screenshots/editor-stats.webp) | ![Encounter database](.github/screenshots/encounters.webp) |
| **Mystery Gift database** | **Save tools** |
| ![Mystery Gift database](.github/screenshots/gifts.webp) | ![Save tools](.github/screenshots/tools.webp) |
| **Trainer Info** | **Pokédex** |
| ![Trainer Info](.github/screenshots/trainer.webp) | ![Pokédex](.github/screenshots/pokedex.webp) |
| **Items** | |
| ![Items](.github/screenshots/items.webp) | |

### Classic mode

The classic PKHeX window is still there, restyled with the TidalHeX ocean theme.

| Classic window | Stats and party |
|:---:|:---:|
| ![Classic PKHeX window with the TidalHeX theme](.github/screenshots/classic-main.webp) | ![Classic window: stats and party](.github/screenshots/classic-stats.webp) |

## What TidalHeX changes

### A new interface

- **Home screen** with your party along the top, large tiles for Pokémon, Boxes, Encounters, Mystery Gifts, Save Tools, Open File and your recent saves, and a dock for quick actions (open, export, check every Pokémon, settings, classic mode).
- **Boxes**: a large box grid with your party and a details panel. Drag to move, Shift+drag to clone, Alt+drag to overwrite, with undo and redo. Drop a Pokémon or Mystery Gift file onto a slot to place it straight into that slot.
- **Pokémon editor** split into Overview, Met, Stats, Moves, Trainer and Extras, with a large sprite, a live legality card, a stat chart and one-click actions (max IVs, suggested moves, suggested met location and more). Ribbons, memories and medals open PKHeX's own editors.
- **PKHeX's editor shortcuts kept**: Alt+click the shiny star to keep the PID and change the SID (so PID/IV-correlated Pokémon stay legal), Shift+click for a square shiny, Ctrl+click for a star shiny.
- **Forms**: the form picker shows each form's sprite, gender forms (Meowstic, Indeedee and others) stay in step with the gender, and Gen 3 Deoxys explains that its forme comes from the game it's in.
- **In-app messages**: PKHeX's questions and errors appear inside the interface instead of Windows message boxes.
- **Keyboard friendly**: Q/E switch pages (the L/R buttons), Esc goes back, Ctrl+1–5 jump to a page, Ctrl+O opens a file and Ctrl+E exports the save. Each screen lists its actions along the bottom.
- **Light on your PC**: the background animation only runs on the home screen while the window is focused, and pauses when minimized. A "Reduce motion" setting turns it off entirely.

### Easier Encounter and Mystery Gift databases

- Same data and the same filters as PKHeX, laid out as cards with the encounter type, level, location and the games it comes from.
- A details panel with the facts that matter: moves, guaranteed IVs, ability, ball, shiny lock and more.
- Gifts claimed through Pokémon HOME are labeled as HOME gifts, and forms are named and drawn correctly (Alolan Vulpix, Deoxys-Speed, Vivillon patterns, …).
- An **Only Pokémon in this game** switch, which is PKHeX's "filter unavailable species" setting.
- Opening an encounter always gives you the Pokémon as the database generates it. PKHeX's "Use tabs as criteria" setting, which copies the shiny state, nature and IVs of the Pokémon you're editing, only applies in classic mode.

### Save tools

- **Items**, **Trainer Info**, **Box Layout** and **Pokédex** are rebuilt inside the interface, with the same rules as PKHeX's editors. Nothing is written until you press Save, and leaving with unsaved changes asks first.
- Every other save editor opens PKHeX's own window, restyled to match. Each rebuilt tool also has a **More options** button that opens the classic editor for game-specific details.
- **Plugins**: PKHeX plugins in the `plugins` folder next to the exe load at startup, just like in PKHeX. Everything they add to PKHeX's Tools menu shows up in a **Plugins** section of Save Tools, with their submenus as groups, and commands that don't apply to the loaded game hidden. Plugin windows get the TidalHeX theme. Startup setting `PluginLoadEnable` turns plugins off.

### Classic mode

- Settings → **Switch to classic PKHeX** restarts in the classic window. Click **TidalHeX view** in its menu bar to come back.
- The classic window and all of its editors use the TidalHeX ocean theme, with smoother tab switching.
- Two startup settings control this: `TidalUI` (new interface or classic window) and `TidalTheme` (the ocean theme for classic windows).

### What stays the same

- `PKHeX.Core` is untouched: the save formats, conversions, legality checks and encounter data are exactly PKHeX's.
- The same files are supported:
  * Save files ("main", \*.sav, \*.dsv, \*.dat, \*.gci, \*.bin)
  * GameCube Memory Card files (\*.raw, \*.bin) containing GC Pokémon savegames
  * Individual Pokémon entity files (.pk\*, \*.ck3, \*.xk3, \*.pb7, \*.sk2, \*.bk4, \*.rk4)
  * Mystery Gift files (\*.pgt, \*.pcd, \*.pgf, .wc\*) including conversion to .pk\*
- PKHeX's settings and backups work as before, and Pokémon, move and item names follow PKHeX's language setting. The new interface itself is in English.

Like PKHeX, TidalHeX expects save files that are not encrypted with console-specific keys. Use a savedata manager to import and export savedata from the console ([Checkpoint](https://github.com/FlagBrew/Checkpoint), save_manager, [JKSM](https://github.com/J-D-K/JKSM), or SaveDataFiler).

## Requirements

- Windows 10 or 11 (64-bit)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (already installed on Windows 11 and most Windows 10 PCs)

## Building

TidalHeX builds the same way as PKHeX, with the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```
dotnet publish PKHeX.WinForms/PKHeX.WinForms.csproj -c Release -o build
```

This produces a single `PKHeX.exe` in `build`. The project can also be opened in [Visual Studio](https://visualstudio.microsoft.com/downloads/) through the .sln or .csproj file.

### Where things are

| Path | What it is |
|---|---|
| `PKHeX.WinForms/Tidal/Web/wwwroot` | The interface: plain HTML, CSS and JavaScript (Vue 3 and anime.js), no build step. It is embedded into the executable. |
| `PKHeX.WinForms/Tidal/Web` | The WebView2 host and the bridge between the page and PKHeX (`API.md` documents the calls). |
| `PKHeX.WinForms/Tidal` | The ocean theme for PKHeX's classic windows, plus the TidalHeX logo and icon. |

### Staying up to date with PKHeX

This repository shares PKHeX's history, so new PKHeX releases (new games, events and legality fixes) can be merged in:

```
git fetch upstream
git merge upstream/master
```

## Credits

TidalHeX is built on [PKHeX](https://github.com/kwsch/PKHeX) by Kaphotics and its contributors, and is licensed under the same [GPLv3 license](LICENSE).

- [Vue.js](https://vuejs.org/) and [anime.js](https://animejs.com/) power the interface (both MIT licensed).
- [Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) hosts it.
- From PKHeX: QR code generation from [QRCoder](https://github.com/codebude/QRCoder) ([MIT](https://github.com/codebude/QRCoder/blob/master/LICENSE.txt)), the shiny sprite collection from [pokesprite](https://github.com/msikma/pokesprite) ([MIT](https://github.com/msikma/pokesprite/blob/master/LICENSE)), and the Pokémon Legends: Arceus sprite collection from the [National Pokédex - Icon Dex](https://www.deviantart.com/pikafan2000/art/National-Pokedex-Version-Delta-Icon-Dex-824897934) project and its contributors.

Pokémon and all related names, sprites and data are © Nintendo, Game Freak and The Pokémon Company. TidalHeX is a fan project and is not affiliated with or endorsed by them.
