using System.Collections.Generic;

namespace PKHeX.WinForms.Tidal.Web;

// DTOs for the in-page save tools (see API.md, "Save tools").

/// <summary> items.get: every pouch of the bag, plus what the game's item rows support. </summary>
public sealed record BagState
{
    public IReadOnlyList<BagPouchDto> Pouches { get; init; } = [];

    /// <summary> Item names by item ID. </summary>
    public IReadOnlyList<string> ItemNames { get; init; } = [];
    public BagColumns Columns { get; init; } = new(false, false, false, false, false, false);

    /// <summary> Scarlet/Violet and Z-A: each row belongs to one item; only counts and flags change. </summary>
    public bool ItemsFixed { get; init; }
    public bool Hax { get; init; }
    public int MaxQuantityHaX { get; init; }
}

/// <summary> Which per-item flags the game stores. </summary>
public sealed record BagColumns(bool Favorite, bool New, bool FreeSpace, bool FreeSpaceIndex, bool NewShop, bool Held);

public sealed record BagPouchDto
{
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary> Number of item slots in the pouch. </summary>
    public int Capacity { get; init; }
    public int MaxCount { get; init; }
    public int DefaultCount { get; init; }
    public bool CanBulkGive { get; init; }

    /// <summary> Not every item fits ("give all" then asks whether to pick at random). </summary>
    public bool Cramped { get; init; }

    /// <summary> Item IDs this pouch can hold; empty means any (HaX). </summary>
    public IReadOnlyList<int> Allowed { get; init; } = [];
    public IReadOnlyList<BagItemDto> Items { get; init; } = [];
}

public sealed record BagItemDto
{
    public int Id { get; init; }
    public int Count { get; init; }
    public bool Favorite { get; init; }
    public bool New { get; init; }
    public bool Free { get; init; }
    public uint FreeIndex { get; init; }
    public bool Shop { get; init; }
    public bool Held { get; init; }
}

public sealed record BagPouchRows(string Type, IReadOnlyList<BagItemDto> Items);
public sealed record BagSaveArgs(IReadOnlyList<BagPouchRows> Pouches);
public sealed record BagActionArgs(string Pouch, string Action, int Count, IReadOnlyList<BagItemDto> Items);

/// <summary> trainer.get: the trainer fields shared by every game, plus coins and badges where the save has them. </summary>
public sealed record TrainerStateDto
{
    public string Game { get; init; } = string.Empty;
    public string Ot { get; init; } = string.Empty;
    public int OtMaxLength { get; init; }

    /// <summary> 0 = male, 1 = female; null for Gen 1 (no gender choice). </summary>
    public byte? Gender { get; init; }

    /// <summary> "six" (Gen 7+: 6-digit TID, 4-digit SID), "sixteen" (5-digit TID/SID) or "single" (Gen 1/2: TID only). </summary>
    public string IdFormat { get; init; } = "sixteen";
    public uint Tid { get; init; }
    public uint Sid { get; init; }
    public uint Money { get; init; }
    public int MaxMoney { get; init; }
    public int Hours { get; init; }
    public int Minutes { get; init; }
    public int Seconds { get; init; }
    public int MaxHours { get; init; }

    /// <summary> Game Corner coins (Gen 1/2); null elsewhere. </summary>
    public int? Coins { get; init; }
    public int MaxCoins { get; init; }
    public IReadOnlyList<BadgeGroupDto> BadgeGroups { get; init; } = [];

    /// <summary> Badge flags in the order of <see cref="BadgeGroups"/>. </summary>
    public IReadOnlyList<bool> Badges { get; init; } = [];
}

public sealed record BadgeGroupDto(string Region, IReadOnlyList<string> Names);

public sealed record TrainerSaveArgs(string? Ot, int? Gender, uint Tid, uint Sid, uint Money, uint Hours, uint Minutes, uint Seconds, uint? Coins, IReadOnlyList<bool>? Badges);

/// <summary> boxlayout.get: every box's name and wallpaper, plus the save's unlocked count and flags. </summary>
public sealed record BoxLayoutDto
{
    public IReadOnlyList<BoxLayoutBoxDto> Boxes { get; init; } = [];
    public bool CanRename { get; init; }
    public int NameMaxLength { get; init; }

    /// <summary> Wallpaper choices (index = value); empty if the game has none. </summary>
    public IReadOnlyList<string> Wallpapers { get; init; } = [];

    /// <summary> Legends: Arceus / Z-A show one scene for every box (the wallpaper value still exists). </summary>
    public bool FixedWallpaper { get; init; }

    /// <summary>
    /// Wallpaper value that shows the save's own picture (Box R/S "My Wallpaper"), if the game has one. It is drawn as the
    /// left or right half of the picture by box position, so its preview takes <c>?box=</c>.
    /// </summary>
    public int? PictureWallpaper { get; init; }

    /// <summary> Number of unlocked boxes, if the game tracks it. </summary>
    public int? Unlocked { get; init; }

    /// <summary> Raw box flag bytes (advanced), if the game has them. </summary>
    public IReadOnlyList<int> Flags { get; init; } = [];
    public int SlotsPerBox { get; init; }
}

/// <summary> One box: its index in the save, name, wallpaper (-1 if none), Pokémon count and whether it can move. </summary>
public sealed record BoxLayoutBoxDto(int Index, string Name, int Wallpaper, int Count, bool Locked);

/// <summary> Order: the original box index for each position. Names/Wallpapers are in the new order. </summary>
public sealed record BoxLayoutSaveArgs(IReadOnlyList<int> Order, IReadOnlyList<string?> Names, IReadOnlyList<int> Wallpapers, int? Unlocked, IReadOnlyList<int>? Flags);

/// <summary> dex.get: seen/caught for every species the game's Pokédex has. </summary>
public sealed record DexStateDto
{
    public string Game { get; init; } = string.Empty;
    public IReadOnlyList<DexEntryDto> Entries { get; init; } = [];

    /// <summary> Gen 4+: "caught" completes the entry (forms, genders, languages); Gen 1-3 only have the two flags. </summary>
    public bool Detailed { get; init; }

    /// <summary> A species can be set to "seen" without being caught (false for Scarlet/Violet and Z-A: whole entries only). </summary>
    public bool SeenOnly { get; init; } = true;
}

public sealed record DexEntryDto(int Species, string Name, bool Seen, bool Caught);

/// <summary> One action: seenAll / caughtAll / completeAll / clearAll (Shiny = include shiny forms), or set (Species + State none|seen|caught). </summary>
public sealed record DexOp(string Op, bool Shiny, int Species, string? State);
public sealed record DexSaveArgs(IReadOnlyList<DexOp> Ops);

/// <param name="Plugins">Names of the loaded plugins.</param>
/// <param name="Items">The commands they added to the menu bar (submenus as groups).</param>
public sealed record PluginMenuDto(List<string> Plugins, List<PluginItemDto> Items);

public sealed record PluginItemDto(string Id, string Text, string Tip, bool Enabled, bool HasIcon, List<PluginItemDto> Children);
