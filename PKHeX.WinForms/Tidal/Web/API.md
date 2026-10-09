# TidalHeX web UI ↔ host contract

The UI is a single page (`wwwroot/index.html`) running in WebView2, served from the virtual origin `https://tidal.local/`.
All data comes from the C# host through a JSON RPC bridge. Everything is camelCase JSON.

## Transport

**Call (JS → host):** `window.chrome.webview.postMessage({ id: number, method: string, args: object | null })`

**Reply (host → JS):** `CoreWebView2.PostWebMessageAsJson({ id, ok: true, result })` or `{ id, ok: false, error: string }`.

**Events (host → JS):** `{ event: string, data: any }` (no `id`). Events:
- `saveLoaded` → `SaveSummary`: a save was opened (from any path: dialog, drop, recent, command line).
- `saveChanged` → `SaveSummary`: summary values changed (edited flag, counts).
- `boxChanged` → `{ box: number }` (`-1` = party): slots changed; the UI refetches.
- `editorLoaded` → `EditorState`: a Pokémon was loaded into the editor (from a slot, file, encounter, gift).
- `toast` → `{ kind: 'info'|'success'|'warn'|'error', text: string }`.
- `updateAvailable` → `UpdateState`: the startup check found a newer TidalHeX release (not the skipped one).
- `updateProgress` → `{ received, total, done? }`: download progress of `update.install`.

**File drop:** the page intercepts drag/drop and calls
`chrome.webview.postMessageWithAdditionalObjects({ id, method: 'openDropped', args: null }, fileList)`;
the host reads file paths from `CoreWebView2WebMessageReceivedEventArgs.AdditionalObjects` (`CoreWebView2File.Path`).

## Static and image URLs (served by the host)

| URL | Content |
|---|---|
| `/…` | embedded `wwwroot` files |
| `/sprite/slot/{box}/{slot}?v={n}` | PNG of a stored Pokémon. `box = -1` is the party. Uses PKHeX sprite settings (shiny, egg, held item, legality flag when enabled). |
| `/sprite/editor?v={n}` | PNG of the Pokémon currently in the editor |
| `/sprite/species/{species}/{form}?shiny=0|1&gender=0|1|2&egg=0|1&crop=0|1` | PNG for a species/form (`crop=1`: trimmed, no shiny sparkle) |
| `/sprite/enc/{token}` | PNG for an encounter or gift search result (`token` from the search result) |
| `/sprite/ball/{ball}` | ball icon |
| `/sprite/item/{item}` | held-item icon |
| `/sprite/type/{type}?v={generation}` | small type icon (move and Tera pickers) |
| `/sprite/gem/{type}` | Tera Type gem (`99` = Stellar) |
| `/sprite/egg/{species}` | the plain egg sprite in the current sprite style (`490` = Manaphy's egg) |
| `/wallpaper/{box}?v={n}` | box wallpaper image |
| `/wallpaper/choice/{value}?box={position}` | a wallpaper by its value, for the Box Layout tool's picker. `box` only matters for Box R/S's "My Wallpaper" (`BoxLayoutDto.pictureWallpaper`), which shows the left or right half of the save's picture by box position. |

Responses carry `Cache-Control: max-age=31536000` when the URL has `v`; the UI bumps `v` to refresh.

## Methods

### App / files
| Method | Args | Result |
|---|---|---|
| `app.init` | – | `{ version: string, save: SaveSummary, recent: RecentFile[], settings: UiSettings }` |
| `file.open` | – | `SaveSummary \| null` (shows the open dialog; opens a save, a Pokémon file, a gift, a box dump…) |
| `file.openPath` | `{ path }` | same as `file.open` |
| `openDropped` | (additional objects) | same as `file.open` |
| `file.exportSave` | – | `bool` (export dialog; honors PKHeX backup/export settings) |
| `file.recent` | – | `RecentFile[]` |

### Boxes / party
| Method | Args | Result |
|---|---|---|
| `box.get` | `{ box }` | `BoxData` |
| `box.party` | – | `PartyData` |
| `box.move` | `{ from: SlotRef, to: SlotRef, mode: 'move'|'clone'|'overwrite' }` | `bool` (`move` swaps when the target is occupied) |
| `box.delete` | `{ slot: SlotRef }` | `bool` |
| `box.view` | `{ slot: SlotRef }` | `EditorState` (loads the slot into the editor) |
| `box.dropFile` | `{ slot }` + dropped files (postMessageWithAdditionalObjects) | `{ placed, save }` — a Pokémon/gift file is written into the slot (classic slot drop); anything else is opened like `openDropped` |
| `box.set` | `{ slot: SlotRef }` | `bool` (writes the editor Pokémon into the slot) |
| `box.legality` | `{ slot: SlotRef }` | `{ valid: bool, report: string }` |
| `box.exportFile` | `{ slot: SlotRef }` | `bool` (save-as dialog) |
| `box.undo` / `box.redo` | – | `bool` |

`SlotRef = { box: number, slot: number }` (`box = -1` → party).

### Lists
| Method | Args | Result |
|---|---|---|
| `list.get` | `{ name: 'species'|'moves'|'items'|'natures'|'abilities'|'balls'|'languages'|'games'|'types' }` | `{ v: number, t: string }[]` filtered for the loaded save |

### Encounters / gifts
| Method | Args | Result |
|---|---|---|
| `enc.search` | `{ species, version, moves: number[], shiny: bool|null, egg: bool|null, types: string[] }` | `EncounterResult[]` |
| `enc.load` | `{ token }` | `EditorState` |
| `gift.all` | – | `GiftResult[]` (every gift available for the save; filtering is done in the UI) |
| `gift.load` | `{ token }` | `EditorState` |
| `gift.saveFile` | `{ token }` | `bool` |

### Save tools
| Method | Args | Result |
|---|---|---|
| `tools.list` | – | `ToolInfo[]` (sub-editors available for this save) |
| `tools.open` | `{ id }` | `bool` (opens the themed classic editor window modally, then emits `saveChanged`/`boxChanged`) |
| `plugins.list` | – | `{ plugins: string[], items: PluginItem[] }`: commands the plugins in the `plugins` folder added to the Tools menu (hidden ones left out) |
| `plugins.run` | `{ id }` | `bool` (clicks the plugin's menu entry, then refreshes the slots) |
| `app.settings` | – | opens the classic settings dialog |
| `app.setOption` | `{ name: 'encountersInGameOnly' \| 'giftsInGameOnly' \| 'reducedMotion', value: bool }` | `UiSettings`; PKHeX's "filter unavailable species" for the encounter / gift databases, or Startup.TidalReduceMotion (saved on exit) |
| `app.setTheme` | `{ theme: 'light' \| 'dark' \| 'pss' \| 'za' \| 'pixel' \| 'arceus' }` | `UiSettings`; Tidal Light / Dark / PSS / ZA / Pixel / Arceus (Startup.TidalUITheme, saved on exit). The host opens the page with `?theme=` so the first frame uses it |
| `app.classic` | – | restarts in classic PKHeX mode |
| `update.check` | – | `UpdateState`: the newest GitHub release (pre-releases included) that has `TidalHeX.exe` attached, compared with this version (`Tidal/TidalVersion.cs`) |
| `update.install` | – | `bool`; downloads the release from the last check (`updateProgress` events), swaps it in for the running exe (kept as `*.old.exe`, deleted next start) and restarts. Asks first about unsaved changes |
| `update.skip` | – | `bool`; no startup reminder for that release (Startup.TidalSkippedUpdate) |
| `update.openPage` | – | opens the release page in the browser |
| `saves.list` | `{ refresh?: bool }` | `LibraryResult`: the Save Manager's saves (the `saves` folder next to the exe, scanned at startup; `refresh` rescans, reusing unchanged files) |
| `saves.open` | `{ id }` | `SaveSummary \| null`; a plain file opens like `file.openPath`; a save inside a .zip loads from memory (its path points inside the .zip, so export always asks where to write, and it isn't added to the recent files) |
| `saves.openFolder` | – | opens the `saves` folder in Explorer (creates it if needed) |

### Editor (implemented in `Api.Editor`)
`editor.get`, `editor.set { field, value }`, `editor.legality`, `editor.exportFile`, `editor.suggest { what }`. `EditorState` is defined in `Dto/EditorState.cs`.

## DTOs

```ts
SaveSummary { loaded, blank, game, icons, version, generation, context, ot, tid, sid, playTime, fileName, filePath,
              boxCount, slotsPerBox, pokemonCount, slotCount, dexCaught, dexTotal, hasParty, hasBox, exportable, edited,
              party: SlotDto[] }
RecentFile  { path, name, folder, exists }
UiSettings  { reducedMotion: bool, checkForUpdates: bool, hideSecrets: bool, theme: 'light' | 'dark' | 'pss' | 'za' | 'pixel' | 'arceus', encountersInGameOnly: bool, giftsInGameOnly: bool }
BoxData     { box, name, boxCount, wallpaper, slots: SlotDto[] }
PartyData   { slots: SlotDto[] }
SlotDto     { box, slot, empty, species, form, name, nickname, level, gender, shiny, egg, legal: bool|null,
              heldItem, sprite, locked: bool }
EncounterResult { token, species, form, name, version, versionName, location, levelMin, levelMax, type, typeLabel,
                  generation, shiny: 'never'|'always'|'random', ball, alpha, gmax, facts: {label, value}[], games: {id, name}[], source: '' | 'Pokémon HOME' }
GiftResult  { token, species, form, name, title, cardId, type, generation, level, shiny, egg, item, ot, fileName,
              isItem, heldItem, moves: number[], details: string[], games: {id, name}[], source: '' | 'Pokémon HOME' }
ToolInfo    { id, name, category, description }
```

## Additions used by the UI

| Method | Args | Result |
|---|---|---|
| `app.ready` | – | (no reply needed) page finished booting; host may close the splash |
| `list.get` | `{ name: 'metLocations' \| 'eggLocations', version }` | locations valid for that origin game |

### Editor
| Method | Args | Result |
|---|---|---|
| `editor.get` | – | `EditorState` |
| `editor.set` | `{ field, value }` | `EditorState` after applying PKHeX's own side effects |
| `editor.legality` | – | `{ valid, report }` |
| `editor.exportFile` | – | `bool` (save-as dialog) |
| `editor.suggest` | `{ what: 'moves' \| 'relearn' \| 'met' \| 'maxIVs' \| 'randomIVs' \| 'clearEVs' \| 'suggestEVs' \| 'rerollPID' \| 'rerollEC' }` | `EditorState` |
| `editor.classic` | `{ what: 'ribbons' \| 'memories' \| 'medals' }` | `EditorState` (opens the classic sub-editor) |

`editor.set` fields: `species`, `form`, `nickname`, `isNicknamed`, `level`, `exp`, `nature`, `statNature`, `ability`, `heldItem`, `gender`,
`shiny` (`'none'|'random'|'star'|'square'`: new PID), `shinySID` (same values; keeps the PID and changes the SID, PKHeX's Alt+click), `isEgg`, `pid` (hex), `ec` (hex), `language`, `friendship`, `ball`, `teraType`,
`met.version|location|level|date|eggLocation|eggDate|fateful`, `ot.name|gender|tid|sid`, `ht.name|gender|friendship`,
`stats.{hp|atk|def|spa|spd|spe}.{iv|ev|ht|gv|av}`, `x.<key>` (format-specific fields listed in `EditorState.fields`), `moves.{0-3}.{id|pp|ppUps}`, `relearn.{0-3}`.

```ts
EditorState {
  empty, format, generation, sprite, species, speciesName, form, forms: Opt[], formNote: { name, hint } | null (read-only form, e.g. Gen 3 Deoxys), nickname, isNicknamed, nicknameMax,
  level, exp, nature, statNature, hasStatNature, ability, abilityNumber, abilities: Opt[], heldItem, hasHeldItem,
  gender, genderLocked, shiny: 'none'|'star'|'square', isEgg, pid, ec, hasEC, language, friendship, ball,
  types: { id, name }[],
  met: { version, location, level, date: string|null, eggLocation, eggDate: string|null, fateful, hasEggMet } | null,
  ot: { name, gender, tid, sid: string|null, language }, otMax, ht: { has, name, gender, friendship },
  stats: { key, name, base, iv, ev, value, ht, natureMod }[], ivMax, evMax, evTotalMax, hasHyperTraining,
  teraType, hasTera, hiddenPower: string|null,
  moves: { id, name, pp, ppUps, maxPp, type, category, legal: bool|null }[], relearn: number[], hasRelearn,
  ribbonCount, legality: { valid, summary, issues: string[] } | null
}
Opt = { v, t }
```

```ts
PluginItem { id, text, tip, enabled, hasIcon, children: PluginItem[] }   // icon: /sprite/plugin/{id}
```

```ts
// Save Manager. Groups = top-level folders ("" = the folder itself; deeper folders stay in their group and show in
// `folder`), oldest generation first; saves by generation, then game.
LibraryResult { folder, groups: LibraryGroup[], skipped: string[] }   // skipped: files that aren't recognized saves
LibraryGroup  { key, name, saves: LibrarySave[] }                      // name: "switch" → "Nintendo Switch"
LibrarySave   { id, fileName, folder, entry?, version, game, generation, ot, tid, sid, playTime, language, languageName, started,
                gender, money, dexCaught, size, modified, icons: string[],
                party: { species, form, gender, shiny, egg }[], loaded, note? }   // icons: img/games/pokemon-*.png
```

```ts
// EditorState.fields: what PKMEditor shows only for some formats (Pokérus, form argument, markings, size, contest stats,
// battle version, ground tile, alpha/noble, dynamax, tera, hidden power type, region, HOME tracker, extra bytes...).
FieldDto { key, section: 'overview'|'met'|'stats'|'moves'|'extras'|'trainer', group, label,
           kind: 'bool'|'number'|'select'|'hex'|'info'|'datetime'|'flags'|'marks'|'bytes'|'move'|'image',
           value, min, max, options?: Opt[], hint?, suggest, suggestTip? }   // suggest: editor.suggest { what: 'field:<key>' }
Opt { v, t, img? }   // img: option icon (Tera types); an 'image' field's value is an image URL shown beside its group
```
