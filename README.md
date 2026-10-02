<p align="center">
  <img src="TidalHeX_Logo.png" width="350" alt="TidalHeX logo">
</p>
<p align="center">
  A modern, console-style interface for <a href="https://github.com/kwsch/PKHeX">PKHeX</a>, the Pokémon core series save editor.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/License-GPLv3-green?style=flat" alt="License: GPLv3">
  <img src="https://img.shields.io/badge/Based%20On-PKHeX-red?style=flat" alt="Based on PKHeX">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue?style=flat" alt="Platform: Windows 10 | 11">
  <img src="https://img.shields.io/github/v/release/HydrosPlays/TidalHeX?include_prereleases&style=flat&label=Version&color=orange" alt="Latest version">
  <a href="https://github.com/HydrosPlays/TidalHeX/releases"><img src="https://img.shields.io/github/downloads/HydrosPlays/TidalHeX/total?style=flat&label=Downloads&color=purple" alt="Total downloads"></a>
</p>

![TidalHeX home screen](.github/screenshots/home.webp)

TidalHeX is a fork of PKHeX that replaces the classic Windows Forms window with a new animated interface, styled after a game console's home menu and the Rotom Dex. Underneath, it is still PKHeX: saves are read and written by PKHeX's own engine (`PKHeX.Core`, unchanged), with the same legality checker and the same encounter and Mystery Gift data. TidalHeX edits your saves exactly the way PKHeX does.

A built-in **Save Manager** lists every save you keep in the `saves` folder next to the exe, raw files or `.zip` backups, with each game's box art, so switching games is a double-click. Five themes change the whole look, from the default ocean blue to a Game Boy Advance–style pixel menu. PKHeX plugins work too: drop them in the `plugins` folder and their commands appear under Save Tools. The classic PKHeX window is still one click away for anything the new interface doesn't cover yet.

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

- **Home screen** with your party along the top, large tiles for Pokémon, Boxes, Encounters, Mystery Gifts, Save Tools, Open File and your recent saves, and a dock for quick actions (open, Save Manager, export, check every Pokémon, settings, classic mode).
- **Boxes**: a large box grid with your party and a details panel. Drag to move, Shift+drag to clone, Alt+drag to overwrite, with undo and redo. Drop a Pokémon or Mystery Gift file onto a slot to place it straight into that slot.
- **Pokémon editor** split into Overview, Met, Stats, Moves, Trainer and Extras, with a large sprite, a live legality card, a stat chart and one-click actions (max IVs, suggested moves, suggested met location and more). Ribbons, memories and medals open PKHeX's own editors.
- **PKHeX's editor shortcuts kept**: Alt+click the shiny star to keep the PID and change the SID (so PID/IV-correlated Pokémon stay legal), Shift+click for a square shiny, Ctrl+click for a star shiny.
- **Forms**: the form picker shows each form's sprite, gender forms (Meowstic, Indeedee and others) stay in step with the gender, and Gen 3 Deoxys explains that its forme comes from the game it's in.
- **In-app messages**: PKHeX's questions and errors appear inside the interface instead of Windows message boxes.
- **Keyboard friendly**: Q/E switch pages (the L/R buttons), Esc goes back, Ctrl+1–6 jump to a page, Ctrl+O opens a file and Ctrl+E exports the save. Each screen lists its actions along the bottom.
- **Themes**: Tidal Light, Tidal Dark, Tidal PSS, Tidal ZA and Tidal Pixel (see [Themes](#themes)).
- **Updates**: at startup TidalHeX checks this repository's releases. When a newer version is out it shows the release notes, and **Update now** downloads it, swaps it in and restarts. Turn the check off or run it by hand in Settings → Updates.
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

### Save Manager

Keep all your saves next to TidalHeX and switch between them with a double-click.

![Save Manager](.github/screenshots/savemanager.webp)

**How to use it**

1. Make a folder named `saves` next to `TidalHeX.exe`, or open the Save Manager and click **Open folder** (it creates the folder for you).
2. Put your saves in it: raw save files (`main`, `*.sav`, `*.dsv`, `*.bin`, …) or `.zip` backups, such as the ones JKSV makes for Switch games.
3. Optionally, sort them into folders. Each folder becomes a group, and common console names get a proper title (`gb`, `gbc`, `gba`, `gc`, `ds`, `3ds`, `switch`). Any other folder name is shown as it is. Folders inside a group, like the folder Checkpoint makes for each backup, stay in that group.

   ```
   TidalHeX.exe
   saves/
   ├── gb/
   │   └── blue-main/
   │       └── sav.dat
   ├── gba/
   │   └── Emerald.sav
   ├── ds/
   │   └── Black 2.sav
   └── switch/
       ├── shield.zip
       └── legends za.zip
   ```

4. Open the Save Manager from the home screen dock or the **Save Manager** tab at the top.
5. **Double-click** a save to load it and jump straight to its boxes. You can also point at a save and press A or Enter, or move with the arrow keys.

**What each save shows**

- The game's box art. Saves that don't record which game of a pair they're from (like Ruby/Sapphire) show both.
- Trainer name and gender, TID and SID. The SID is hidden when PKHeX's "Hide secret details" privacy setting is on.
- Play time, the date the adventure started, money and the number of Pokémon caught. The start date shows for Gen 4 onward; Gen 1–3, Let's Go and Legends: Z-A don't have one.
- Language, generation and ZIP badges, plus **Open now** on the save that's loaded.
- The party, and when the file was last saved.

**Red or Blue? Naming Gen 1–3 saves**

Saves from the first three generations don't record which game of a pair they came from (Red or Blue, Gold or Silver, Ruby or Sapphire, FireRed or LeafGreen), and most don't record their language either. Like PKHeX, TidalHeX works both out from the name: it checks the save's file name first, then the `.zip` it's in, then its folders. A Checkpoint backup in `blue-main/sav.dat` is read as Blue. Put the game's name in the save's own language somewhere in the file or folder name:

| Game | English | French | German | Italian | Spanish | Japanese |
|---|---|---|---|---|---|---|
| Red | `red` | `rouge` | `rot` | `rosso` | `rojo` | `aka` / 赤 |
| Blue | `blue` | `bleu` | `blau` | `blu` | `azul` | `ao` / 青 |
| Green (Japan only) | | | | | | `midori` / 緑 |
| Yellow | `yellow` | `jaune` | `gelb` | `giallo` | `amarillo` | `pika` / 黄 |

Later games work the same way with their English names (`gold`, `silver`, `crystal`, `ruby`, `sapphire`, `firered`, `leafgreen`) or translated ones (`rubin`, `saphir`, `feuer`, `blatt`, …). Yellow, Crystal and Emerald are also recognized from the save itself, so for those the name only adds the language. A save whose name doesn't say uses PKHeX's default from **Settings → Save Language** (Red/Blue, English, unless you change it). Keep names simple: a folder called `transferred` contains "red".

**Good to know**

- Groups go from the oldest console to the newest, and saves in each group are sorted by generation. Use the search box or the Gen buttons to filter.
- The folder is read when TidalHeX starts, so the list is ready right away. It refreshes when you switch back to TidalHeX or press **Refresh** (Y or F5), so new files show up without restarting.
- A save inside a `.zip` is read straight from the zip, and the zip is never changed. When you export, TidalHeX asks where to save it. Plain save files work exactly like opening them with Open File, including PKHeX's automatic backups and the recent files list.
- Files that aren't saves are skipped, and the page tells you how many there were.

### Themes

TidalHeX comes with five themes. They change the colors, panels, backgrounds and animations of the whole interface; everything works the same in each.

**How to change the theme**

1. Open **Settings**: the gear button in the home screen's dock, the **+ Options** hint at the bottom of the home screen, or the + key.
2. Under **Theme**, click the one you want. The interface switches right away, and TidalHeX remembers your choice the next time it starts.

![Theme picker in Settings](.github/screenshots/themes/settings-themes.webp)

The theme is also a PKHeX setting (**Startup → TidalUITheme**), so it can be changed from PKHeX's settings window too. PKHeX's classic windows (Ribbons, Memories and the other editors that open in their own window) keep the ocean theme in every theme. **Reduce motion** turns off each theme's background animation.

#### Tidal Light (Default)

The original look: a bright cyan-to-blue sea with a grid, rings and rising bubbles, frosted-glass panels, and a white-and-cyan selection ring.

| Home | Boxes |
|:---:|:---:|
| ![Home](.github/screenshots/themes/light-home.webp) | ![Boxes](.github/screenshots/themes/light-boxes.webp) |
| **Pokémon editor** | **Save Manager** |
| ![Pokémon editor](.github/screenshots/themes/light-editor.webp) | ![Save Manager](.github/screenshots/themes/light-saves.webp) |

#### Tidal Dark

The same design on a deep night-time sea: darker panels, dimmed glows and box wallpapers, and the same cyan accents and colorful home tiles.

| Home | Boxes |
|:---:|:---:|
| ![Home](.github/screenshots/themes/dark-home.webp) | ![Boxes](.github/screenshots/themes/dark-boxes.webp) |
| **Pokémon editor** | **Save Manager** |
| ![Pokémon editor](.github/screenshots/themes/dark-editor.webp) | ![Save Manager](.github/screenshots/themes/dark-saves.webp) |

#### Tidal PSS

Based on the Player Search System of Pokémon X/Y and Omega Ruby/Alpha Sapphire: a sunny yellow background with diagonal stripes, burnt-orange panels, navy bars across the top and bottom, and navy buttons and labels.

| Home | Boxes |
|:---:|:---:|
| ![Home](.github/screenshots/themes/pss-home.webp) | ![Boxes](.github/screenshots/themes/pss-boxes.webp) |
| **Pokémon editor** | **Save Manager** |
| ![Pokémon editor](.github/screenshots/themes/pss-editor.webp) | ![Save Manager](.github/screenshots/themes/pss-saves.webp) |

#### Tidal ZA

Based on Pokémon Legends: Z-A. The background is the game's title screen: near-black with a teal and maroon glow, falling green data dots and floating squares (animated on the home screen). Menus are frosted slate panels with condensed white titles, and items turn white with a lime marker when you point at them, like the game's menus.

| Home | Boxes |
|:---:|:---:|
| ![Home](.github/screenshots/themes/za-home.webp) | ![Boxes](.github/screenshots/themes/za-boxes.webp) |
| **Pokémon editor** | **Save Manager** |
| ![Pokémon editor](.github/screenshots/themes/za-editor.webp) | ![Save Manager](.github/screenshots/themes/za-saves.webp) |

#### Tidal Pixel

Based on the Game Boy and Game Boy Advance games (Gold/Silver, Ruby/Sapphire/Emerald, FireRed/LeafGreen): a light-blue striped background that scrolls on the home screen, flat dark-blue boxes with pixel frames and hard shadows, white boxes with a ▶ cursor for the item you point at, and pixel bubbles. Titles, buttons and labels use **Tidal Pixel**, a pixel font made for TidalHeX (its glyphs are drawn in `PKHeX.WinForms/Tidal/Assets/PixelFont/make_tidal_pixel.py`, which builds the font file).

| Home | Boxes |
|:---:|:---:|
| ![Home](.github/screenshots/themes/pixel-home.webp) | ![Boxes](.github/screenshots/themes/pixel-boxes.webp) |
| **Pokémon editor** | **Save Manager** |
| ![Pokémon editor](.github/screenshots/themes/pixel-editor.webp) | ![Save Manager](.github/screenshots/themes/pixel-saves.webp) |

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

### Publishing a release

The in-app updater compares its own version with the tags of this repository's releases (pre-releases included):

1. Set the new version in `PKHeX.WinForms/Tidal/TidalVersion.cs` (for example `0.6.0-beta`) and build.
2. Tag the release with the same version (`v0.6.0-beta`; the `v` is optional).
3. Attach the exe named exactly `TidalHeX.exe`.

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

Pokémon and all related names, sprites, game art and data are © Nintendo, Game Freak and The Pokémon Company. TidalHeX is a fan project and is not affiliated with or endorsed by them.
