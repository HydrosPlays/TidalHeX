using System.Collections.Generic;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// One encounter from <c>enc.search</c>. <see cref="Token"/> (<c>e{list}-{index}</c>, unique per search) identifies it for <c>enc.load</c> and <c>/sprite/enc/{token}</c>.
/// </summary>
public sealed record EncounterResult
{
    public string Token { get; init; } = string.Empty;
    public int Species { get; init; }
    public int Form { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public string VersionName { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public int LevelMin { get; init; }
    public int LevelMax { get; init; }

    /// <summary> Encounter type group, matching the <c>types</c> search filter: slot, static, trade, egg or mystery. </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary> Display badge: Wild, Static, Trade, Egg, Raid, Outbreak, Gift or Event. </summary>
    public string TypeLabel { get; init; } = string.Empty;
    public int Generation { get; init; }

    /// <summary> never | always | random </summary>
    public string Shiny { get; init; } = "random";
    public int Ball { get; init; }
    public bool Alpha { get; init; }
    public bool Gmax { get; init; }

    /// <summary> What's fixed about the encounter (moves, ball, nature, gender, ability, IVs, trainer); may be empty. </summary>
    public IReadOnlyList<FactDto> Facts { get; init; } = [];

    /// <summary> The individual games it's from (groups like USUM or RBY expanded), oldest first. </summary>
    public IReadOnlyList<GameDto> Games { get; init; } = [];

    /// <summary> "Pokémon HOME" for HOME gifts (then <see cref="Games"/> are the games it can be moved to); otherwise empty. </summary>
    public string Source { get; init; } = string.Empty;
}

/// <summary> One game: its <c>GameVersion</c> id and localized name. </summary>
public sealed record GameDto(int Id, string Name);

/// <summary> A labelled detail line, e.g. <c>{ label: "Nature", value: "Adamant" }</c>. </summary>
public sealed record FactDto(string Label, string Value);

/// <summary>
/// One event gift from <c>gift.all</c>. <see cref="Token"/> (<c>g{list}-{index}</c>, unique per gift list) identifies it for <c>gift.load</c>, <c>gift.saveFile</c> and <c>/sprite/enc/{token}</c>.
/// </summary>
public sealed record GiftResult
{
    public string Token { get; init; } = string.Empty;
    public int Species { get; init; }
    public int Form { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int CardId { get; init; }
    public string Type { get; init; } = string.Empty;
    public int Generation { get; init; }
    public int Level { get; init; }
    public bool Shiny { get; init; }
    public bool Egg { get; init; }

    /// <summary> Item name for item gifts (see <see cref="IsItem"/>), otherwise empty. </summary>
    public string Item { get; init; } = string.Empty;
    public string Ot { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public bool IsItem { get; init; }

    /// <summary> Held item name; empty if none. </summary>
    public string HeldItem { get; init; } = string.Empty;
    public IReadOnlyList<int> Moves { get; init; } = [];
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary> The games that can receive the gift, oldest first (for HOME gifts: the games it can be moved to). </summary>
    public IReadOnlyList<GameDto> Games { get; init; } = [];

    /// <summary> "Pokémon HOME" for gifts claimed in HOME rather than in a game; otherwise empty. </summary>
    public string Source { get; init; } = string.Empty;
}
