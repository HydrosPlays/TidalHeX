using System.Collections.Generic;

namespace PKHeX.WinForms.Tidal.Web;

// DTOs for the Pokémon editor (editor.get / editor.set / editorLoaded). Serialized camelCase; see API.md.

internal sealed record Opt(int V, string T);
internal sealed record TypeDto(string Id, string Name);
internal sealed record StatDto(string Key, string Name, int Base, int Iv, int Ev, int Value, bool Ht, int NatureMod);
internal sealed record MoveDto(int Id, string Name, int Pp, int PpUps, int MaxPp, string? Type, string? Category, bool? Legal);
internal sealed record MetDto(int Version, int Location, int Level, string? Date, int EggLocation, string? EggDate, bool Fateful, bool HasEggMet, bool MetAsEgg);
internal sealed record TrainerDto(string Name, int Gender, string Tid, string? Sid, int Language);
internal sealed record HandlerDto(bool Has, string Name, int Gender, int Friendship);
internal sealed record LegalityDto(bool Valid, string Summary, List<string> Issues);

internal sealed class EditorState
{
    public bool Empty { get; init; }
    public int Format { get; init; }
    public int Generation { get; init; }
    public string Sprite { get; init; } = string.Empty;
    public int Species { get; init; }
    public string SpeciesName { get; init; } = string.Empty;
    public int Form { get; init; }
    public List<Opt> Forms { get; init; } = [];
    /// <summary> Read-only form shown when the form isn't stored (Gen 3 Deoxys takes the forme of the game it's in). </summary>
    public FormNoteDto? FormNote { get; init; }
    public string Nickname { get; init; } = string.Empty;
    public bool IsNicknamed { get; init; }
    public int NicknameMax { get; init; }
    public int Level { get; init; }
    public uint Exp { get; init; }
    public int? Nature { get; init; }
    public int StatNature { get; init; }
    public bool HasStatNature { get; init; }
    public int Ability { get; init; }
    public List<Opt> Abilities { get; init; } = [];
    public int HeldItem { get; init; }
    public bool HasHeldItem { get; init; }
    public int Gender { get; init; }
    public bool GenderLocked { get; init; }
    public string Shiny { get; init; } = "none";
    public bool IsEgg { get; init; }
    public string Pid { get; init; } = string.Empty;
    public string Ec { get; init; } = string.Empty;
    public bool HasEC { get; init; }
    public int? Language { get; init; }
    public int? Friendship { get; init; }
    public int Ball { get; init; }
    public List<TypeDto> Types { get; init; } = [];
    public MetDto? Met { get; init; }
    public TrainerDto Ot { get; init; } = null!;
    public int OtMax { get; init; }
    public HandlerDto Ht { get; init; } = null!;
    public List<StatDto> Stats { get; init; } = [];
    public int IvMax { get; init; }
    public int EvMax { get; init; }
    public int EvTotalMax { get; init; }
    public bool HasHyperTraining { get; init; }
    public int? TeraType { get; init; }
    public bool HasTera { get; init; }
    public string? HiddenPower { get; init; }
    public List<MoveDto> Moves { get; init; } = [];
    public int[] Relearn { get; init; } = [];
    public bool HasRelearn { get; init; }
    public int RibbonCount { get; init; }
    public LegalityDto? Legality { get; init; }
}

/// <param name="Name">Form as the loaded game shows it, e.g. "Attack Forme (FireRed)".</param>
/// <param name="Hint">Why it can't be changed here.</param>
public sealed record FormNoteDto(string Name, string Hint);
