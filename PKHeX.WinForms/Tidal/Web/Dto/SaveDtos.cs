using System.Collections.Generic;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// Summary of the loaded save file (<c>saveLoaded</c> / <c>saveChanged</c> events, <c>app.init</c>).
/// </summary>
public sealed record SaveSummary
{
    public bool Loaded { get; init; }
    public bool Blank { get; init; }
    public string Game { get; init; } = string.Empty;
    /// <summary> Box art (img/games/...), as in the Save Manager: two for saves that can't tell a pair apart. </summary>
    public List<string> Icons { get; init; } = [];
    public int Version { get; init; }
    public int Generation { get; init; }
    public string Context { get; init; } = string.Empty;
    public string Ot { get; init; } = string.Empty;
    /// <summary> Trainer ID as displayed in game (6 digits from Gen 7, else 5). </summary>
    public string Tid { get; init; } = string.Empty;

    /// <summary> Secret ID as displayed; empty for Gen 1-2. </summary>
    public string Sid { get; init; } = string.Empty;
    public string PlayTime { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int BoxCount { get; init; }
    public int SlotsPerBox { get; init; }
    public int PokemonCount { get; init; }
    public int SlotCount { get; init; }
    public int DexCaught { get; init; }
    public int DexTotal { get; init; }
    public bool HasParty { get; init; }
    public bool HasBox { get; init; }
    public bool Exportable { get; init; }
    public bool Edited { get; init; }
    public IReadOnlyList<SlotDto> Party { get; init; } = [];
}

/// <summary>
/// A stored Pokémon slot as shown in the box grid or the party.
/// </summary>
public sealed record SlotDto
{
    /// <summary> Box index; -1 is the party. </summary>
    public int Box { get; init; }
    public int Slot { get; init; }
    public bool Empty { get; init; }
    public int Species { get; init; }
    public int Form { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;
    public int Level { get; init; }
    public int Gender { get; init; }
    public bool Shiny { get; init; }
    public bool Egg { get; init; }

    /// <summary> Legality flag; null when the "flag illegal" display setting is off (or HaX). </summary>
    public bool? Legal { get; init; }
    /// <summary> Held item name; empty if none. </summary>
    public string HeldItem { get; init; } = string.Empty;
    public string Sprite { get; init; } = string.Empty;
    public bool Locked { get; init; }
}

public sealed record BoxData
{
    public int Box { get; init; }
    public string Name { get; init; } = string.Empty;
    public int BoxCount { get; init; }
    public string Wallpaper { get; init; } = string.Empty;
    public IReadOnlyList<SlotDto> Slots { get; init; } = [];
}

public sealed record PartyData
{
    public IReadOnlyList<SlotDto> Slots { get; init; } = [];
}

public sealed record RecentFile
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Folder { get; init; } = string.Empty;
    public bool Exists { get; init; }
}

public sealed record UiSettings
{
    public bool ReducedMotion { get; init; }

    /// <summary> Look for a new TidalHeX release at startup (Startup.TidalCheckForUpdates). </summary>
    public bool CheckForUpdates { get; init; }

    /// <summary> "light" (Tidal Light, default), "dark" (Tidal Dark), "pss" (Tidal PSS), "za" (Tidal ZA) or "pixel" (Tidal Pixel): Startup.TidalUITheme. </summary>
    public string Theme { get; init; } = "light";
    public bool HideSecrets { get; init; }

    /// <summary> Encounter database: only Pokémon that exist in the loaded game (PKHeX setting EncounterDb.FilterUnavailableSpecies). </summary>
    public bool EncountersInGameOnly { get; init; }

    /// <summary> Mystery Gift database: only gifts the loaded game can hold (PKHeX setting MysteryDb.FilterUnavailableSpecies). </summary>
    public bool GiftsInGameOnly { get; init; }
}

/// <summary>
/// Result of <c>app.init</c>.
/// </summary>
public sealed record InitResult
{
    /// <summary> PKHeX's version (yyyyMMdd). </summary>
    public string Version { get; init; } = string.Empty;
    /// <summary> TidalHeX's version, e.g. "0.5.0 Beta". </summary>
    public string TidalVersion { get; init; } = string.Empty;
    public bool Hax { get; init; }
    public SaveSummary Save { get; init; } = new();
    public IReadOnlyList<RecentFile> Recent { get; init; } = [];
    public UiSettings Settings { get; init; } = new();
}

public sealed record LegalityReport
{
    public bool Valid { get; init; }
    public string Report { get; init; } = string.Empty;
}

public sealed record ToolInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary> In-page editor for this tool (js/tools/{web}.js), or null to open PKHeX's classic window. </summary>
    public string? Web { get; init; }
}

/// <summary>
/// Combo list entry (<c>list.get</c>).
/// </summary>
public sealed record ListItem(int V, string T);

/// <summary> Save Manager: the saves in the "saves" folder, one group per sub-folder. </summary>
public sealed record LibraryResult
{
    /// <summary> Full path of the folder. </summary>
    public string Folder { get; init; } = string.Empty;
    public List<LibraryGroup> Groups { get; init; } = [];
    /// <summary> Files that aren't saves PKHeX recognizes (relative paths). </summary>
    public List<string> Skipped { get; init; } = [];
}

public sealed record LibraryGroup
{
    /// <summary> Top-level folder inside the saves folder (its sub-folders belong to it too), "" for saves directly in it. </summary>
    public string Key { get; init; } = string.Empty;
    /// <summary> Display name, e.g. "Nintendo Switch" for a "switch" folder. </summary>
    public string Name { get; init; } = string.Empty;
    public List<LibrarySave> Saves { get; init; } = [];
}

public sealed record LibrarySave
{
    public string Id { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    /// <summary> Folders between the group's folder and the file (e.g. a Checkpoint backup's folder), "" if none. </summary>
    public string Folder { get; init; } = string.Empty;
    /// <summary> The save's name inside a .zip; null for plain files. </summary>
    public string? Entry { get; init; }
    public int Version { get; init; }
    public string Game { get; init; } = string.Empty;
    public int Generation { get; init; }
    public string Ot { get; init; } = string.Empty;
    public string Tid { get; init; } = string.Empty;
    /// <summary> Empty when PKHeX's "hide secret details" privacy setting is on, or the game has no SID. </summary>
    public string Sid { get; init; } = string.Empty;
    /// <summary> "38h 13m 47s". </summary>
    public string PlayTime { get; init; } = string.Empty;
    /// <summary> Badge text, e.g. "ENG". </summary>
    public string Language { get; init; } = string.Empty;
    public string LanguageName { get; init; } = string.Empty;
    /// <summary> Adventure start date (yyyy-MM-dd), empty for games that don't store one. </summary>
    public string Started { get; init; } = string.Empty;
    /// <summary> Trainer gender: 0 male, 1 female, -1 none (Gen 1, Gold/Silver). </summary>
    public int Gender { get; init; } = -1;
    public uint Money { get; init; }
    /// <summary> Pokédex caught count, -1 without a Pokédex. </summary>
    public int DexCaught { get; init; } = -1;
    public long Size { get; init; }
    /// <summary> Last write time, ISO 8601 local time. </summary>
    public string Modified { get; init; } = string.Empty;
    /// <summary> Box art icons (img/games/...): one per game, two for saves shared by a pair (e.g. Ruby/Sapphire). </summary>
    public List<string> Icons { get; init; } = [];
    public List<LibraryMon> Party { get; init; } = [];
    /// <summary> The save currently open. </summary>
    public bool Loaded { get; init; }
    public string? Note { get; init; }
}

public sealed record LibraryMon(ushort Species, byte Form, byte Gender, bool Shiny, bool Egg);

/// <summary> Result of update.check (and the updateAvailable event). </summary>
public sealed record UpdateState
{
    /// <summary> This version, e.g. "0.5.0-beta". </summary>
    public string Current { get; init; } = string.Empty;
    public string CurrentDisplay { get; init; } = string.Empty;
    /// <summary> GitHub answered. </summary>
    public bool Checked { get; init; }
    /// <summary> A newer release is out. </summary>
    public bool Available { get; init; }
    /// <summary> The newest release with an exe, if any. </summary>
    public UpdateRelease? Latest { get; init; }
    public string? Error { get; init; }
}

public sealed record UpdateRelease
{
    /// <summary> The release's tag, e.g. "v0.5.1-beta". </summary>
    public string Version { get; init; } = string.Empty;
    /// <summary> "0.5.1 Beta". </summary>
    public string Display { get; init; } = string.Empty;
    /// <summary> The release title. </summary>
    public string Name { get; init; } = string.Empty;
    /// <summary> The release description (Markdown). </summary>
    public string Notes { get; init; } = string.Empty;
    /// <summary> Download size in bytes. </summary>
    public long Size { get; init; }
    public string Published { get; init; } = string.Empty;
}
