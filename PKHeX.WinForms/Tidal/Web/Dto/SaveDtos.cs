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
    public string Version { get; init; } = string.Empty;
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
