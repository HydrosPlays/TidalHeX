using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using PKHeX.Core;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// Headless Pokémon editor for the web UI. Edits a live <see cref="PKM"/> exactly the way PKHeX's
/// WinForms <c>PKMEditor</c> does (same side effects per format), and finalizes it like <c>PreparePKM</c>.
/// </summary>
internal sealed partial class EditorService
{
    private readonly WebSession Session;
    private SaveFile Sav => Session.SAV;
    private static bool HaX => Program.HaX;

    /// <summary> Live working copy (not finalized); owned by the session so box writes, sprites and exports share it. </summary>
    public PKM Entity => Session.Editor;

    private bool _metAsEgg;
    private LegalityAnalysis? _legality;
    private static readonly GameStrings English = GameInfo.GetStrings(GameLanguage.DefaultLanguage);

    public EditorService(WebSession session)
    {
        Session = session;
        session.EditorReplaced += OnReplaced;
        OnReplaced();
    }

    /// <summary> A new Pokémon was loaded into the editor (slot, file, encounter, gift, save). </summary>
    private void OnReplaced()
    {
        var pk = Session.Editor;
        if (!HaX && pk.Species != 0 && pk.CurrentLevel == 100)
            pk.CurrentLevel = 100; // clamp EXP to the level-100 value, as PKHeX's editor does on load
        _metAsEgg = pk.Format >= 4 && pk.Species != 0 && EncounterStateUtil.IsMetAsEgg(pk);
        _legality = null;
    }

    private void Changed()
    {
        _legality = null;
        Session.EditorDirty = true;
        Session.TouchEditor();
    }

    /// <summary> A classic sub-editor changed the working copy directly. </summary>
    public void ChangedExternally() => Changed();

    /// <summary>
    /// Returns a finalized clone (what PKHeX's PreparePKM produces): recalculated/healed stats, compacted moves,
    /// transfer EC fix-up, met/egg date rules, memories fixed, checksum refreshed. The working copy is untouched.
    /// </summary>
    public PKM Prepare()
    {
        var pk = Entity.Clone();
        var sav = Sav;

        if (pk.Format >= 4)
        {
            var none = LocationEdits.GetNoneLocation(pk);
            if (!_metAsEgg)
            {
                pk.EggMetDate = null;
                pk.EggLocation = none;
            }
            else
            {
                pk.EggMetDate ??= new DateOnly(2000, 1, 1);
            }
            if (pk.IsEgg && pk.MetLocation == none)
                pk.MetDate = null;
            else
                pk.MetDate ??= new DateOnly(2000, 1, 1);
        }

        if (pk.Format >= 6 && PIDVerifier.GetTransferEC(pk, out var ec))
            pk.EncryptionConstant = ec;

        RecalcStats(pk, sav);
        if (!HaX)
            pk.Stat_Level = pk.CurrentLevel;
        if (pk is PK6 or PK7)
            pk.Data[0xFE..].Clear();
        if (pk is PB7 { Stat_CP: 0 } b7)
            b7.ResetCP();

        pk.FixMoves();
        FixRelearn(pk);
        if (Program.Settings.SlotWrite.SetUpdatePKM)
            FixMemories(pk);
        pk.RefreshChecksum();
        return pk;
    }

    private static void RecalcStats(PKM pk, SaveFile sav)
    {
        Span<ushort> stats = stackalloc ushort[6];
        pk.LoadStats(sav.Personal.GetFormEntry(pk.Species, pk.Form), stats);
        pk.SetStats(stats);
    }

    private static void FixRelearn(PKM pk)
    {
        switch (pk)
        {
            case G6PKM g6: g6.FixRelearn(); break;
            case G8PKM g8: g8.FixRelearn(); break;
            case PA8 a8: a8.FixRelearn(); break;
            case PK9 k9: k9.FixRelearn(); break;
            case PA9 a9: a9.FixRelearn(); break;
        }
    }

    private static void FixMemories(PKM pk)
    {
        switch (pk)
        {
            case PK6 p: p.FixMemories(); break;
            case PK7 p: p.FixMemories(); break;
            case PB7 p: p.FixMemories(); break;
            case PK8 p: p.FixMemories(); break;
            case PB8 p: p.FixMemories(); break;
            case PA8 p: p.FixMemories(); break;
            case PK9 p: p.FixMemories(); break;
            case PA9 p: p.FixMemories(); break;
        }
    }

    public LegalityAnalysis Legality => _legality ??= new LegalityAnalysis(Prepare(), Sav.Personal);

    public (bool Valid, string Report) GetLegalityReport()
    {
        var la = Legality;
        var ctx = LegalityLocalizationContext.Create(la, GameInfo.CurrentLanguage);
        return (la.Valid, ctx.Report(true));
    }

    // ------------------------------------------------------------------ state

    public EditorState GetState(string spriteUrl)
    {
        try
        {
            return BuildState(spriteUrl);
        }
        catch (Exception ex)
        {
            // Bad/corrupt data (e.g. a bad egg with garbage move IDs): show what we can instead of breaking the editor.
            var pk = Entity;
            var names = GameInfo.Strings.specieslist;
            return new EditorState
            {
                Empty = false,
                Format = pk.Format,
                Sprite = spriteUrl,
                Species = pk.Species,
                SpeciesName = pk.Species < names.Length ? names[pk.Species] : $"#{pk.Species}",
                Nickname = SafeNickname(pk),
                Ot = new TrainerDto(string.Empty, 0, string.Empty, null, 0),
                Ht = new HandlerDto(false, string.Empty, 0, 0),
                Legality = new LegalityDto(false, "Unreadable data", [$"This Pokémon's data couldn't be read: {ex.Message}"]),
            };
        }
    }

    private static string SafeNickname(PKM pk)
    {
        try { return pk.Nickname; }
        catch { return string.Empty; }
    }

    private EditorState BuildState(string spriteUrl)
    {
        var pk = Entity;
        var sav = Sav;
        var strings = GameInfo.Strings;
        var format = pk.Format;
        var pi = sav.Personal.GetFormEntry(pk.Species, pk.Form);
        var empty = pk.Species == 0;

        // Forms
        var forms = new List<Opt>();
        var basePi = sav.Personal[pk.Species];
        if (FormInfo.HasFormSelection(basePi, pk.Species, format) || HaX)
        {
            var list = GetFormNames(pk);
            for (int i = 0; i < list.Length; i++)
                forms.Add(new Opt(i, string.IsNullOrWhiteSpace(list[i]) ? $"Form {i}" : list[i]));
        }

        // Abilities
        var (abilities, ability) = GetAbilityState(pk);

        // Stats (display order HP, Atk, Def, SpA, SpD, Spe; PKM index order HP, Atk, Def, Spe, SpA, SpD)
        Span<ushort> values = stackalloc ushort[6];
        pk.LoadStats(pi, values);
        var natureForStats = format >= 8 ? pk.StatAlignment : pk.Nature;
        var (up, down) = natureForStats.GetNatureModification();
        var neutral = natureForStats.IsNeutralOrInvalid(up, down);
        var stats = new List<StatDto>(6);
        var hyper = pk as IHyperTrain;
        var ganbaru = pk as IGanbaru; // Legends: Arceus effort levels
        var awakened = pk as IAwakened; // Let's Go awakening values
        foreach (var (key, name, index, amp) in StatOrder)
        {
            int mod = neutral || amp < 0 ? 0 : amp == up ? 1 : amp == down ? -1 : 0;
            stats.Add(new StatDto(key, name, pi.GetBaseStatValue(index), pk.GetIV(index), pk.GetEV(index), values[index],
                hyper?.IsHyperTrained(index) ?? false, mod)
            {
                Gv = ganbaru?.GetGV(index),
                GvMax = ganbaru is null ? null : pk.GetMaxGanbaru(index),
                Av = awakened?.GetAV(index),
            });
        }

        // Moves
        var la = empty ? null : Legality;
        var moves = new List<MoveDto>(4);
        for (int i = 0; i < 4; i++)
        {
            var id = pk.GetMove(i);
            var type = id == 0 ? null : TypeName(MoveInfo.GetType(id, pk.Context));
            bool? legal = la is { Parsed: true } && !HaX && id != 0 ? la.Info.Moves[i].Valid : null;
            moves.Add(new MoveDto(id, id < strings.movelist.Length ? strings.movelist[id] : $"#{id}", id == 0 ? 0 : GetPP(pk, i), id == 0 ? 0 : GetPPUps(pk, i), id == 0 ? 0 : pk.GetMovePP(id, GetPPUps(pk, i)), type, MoveCategory.GetName(id, pk.Context), legal));
        }
        var learnable = la is { Parsed: true } && !HaX ? GetLearnableMoves(la, pk.MaxMoveID) : null;
        var relearn = new[] { pk.RelearnMove1, pk.RelearnMove2, pk.RelearnMove3, pk.RelearnMove4 };

        // Met
        MetDto? met = null;
        if (format >= 2 && pk is not SK2 || format >= 3)
        {
            met = new MetDto(
                (int)pk.Version, pk.MetLocation, pk.MetLevel,
                format >= 4 ? FormatDate(pk.MetDate) : null,
                pk.EggLocation, format >= 4 ? FormatDate(pk.EggMetDate) : null,
                pk.FatefulEncounter, format >= 4, _metAsEgg);
        }

        // Trainer
        var idFormat = pk.TrainerIDDisplayFormat;
        var tid = idFormat == TrainerIDFormat.SixDigit ? pk.DisplayTID.ToString("D6") : pk.DisplayTID.ToString("D5");
        string? sid = idFormat switch
        {
            TrainerIDFormat.SixteenBitSingle => null,
            TrainerIDFormat.SixDigit => pk.DisplaySID.ToString("D4"),
            _ => pk.DisplaySID.ToString("D5"),
        };
        var ot = new TrainerDto(pk.OriginalTrainerName, pk.OriginalTrainerGender & 1, tid, sid, pk.Language);
        var ht = new HandlerDto(format >= 6, format >= 6 ? pk.HandlingTrainerName : string.Empty, format >= 6 ? pk.HandlingTrainerGender & 1 : 0, format >= 6 ? pk.HandlingTrainerFriendship : 0);

        // Tera
        int? tera = null;
        if (pk is ITeraType t)
        {
            var effective = (byte)t.GetTeraType();
            tera = effective == TeraTypeUtil.Stellar ? 18 : effective;
        }

        // Legality summary
        LegalityDto? legality = null;
        if (!empty && !HaX && la is not null)
        {
            var issues = new List<string>();
            if (!la.Valid)
            {
                var ctx = LegalityLocalizationContext.Create(la, GameInfo.CurrentLanguage);
                var simple = ctx.Report(false);
                issues.AddRange(simple.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(z => z.Length != 0).Skip(1).Take(6));
            }
            legality = new LegalityDto(la.Valid, la.Valid ? "Legal" : "Legality issues", issues);
        }

        int ribbons = 0;
        if (format >= 3)
        {
            try { ribbons = RibbonInfo.GetRibbonInfo(pk).Count(r => r.HasRibbon || r.RibbonCount > 0); }
            catch { ribbons = 0; }
        }

        var shiny = pk.IsShiny ? (ShinyExtensions.GetType(pk) == Shiny.AlwaysSquare ? "square" : "star") : "none";
        var (t1, t2) = format <= 2 ? (ModernTypeGB(pi.Type1), ModernTypeGB(pi.Type2)) : (pi.Type1, pi.Type2);
        string LocalType(byte t) => t < strings.types.Length ? strings.types[t] : t.ToString();
        var types = new List<TypeDto> { new(TypeName(t1), LocalType(t1)) };
        if (t2 != t1)
            types.Add(new TypeDto(TypeName(t2), LocalType(t2)));

        string? hiddenPower = format <= 7 || pk is PB8 ? strings.types[pk.HPType + 1] : null;

        return new EditorState
        {
            Empty = empty,
            Format = format,
            Generation = pk.Generation,
            Sprite = spriteUrl,
            Species = pk.Species,
            SpeciesName = pk.Species < strings.specieslist.Length ? strings.specieslist[pk.Species] : pk.Species.ToString(),
            Form = pk.Form,
            Forms = forms,
            FormNote = forms.Count == 0 ? GetFormNote(pk, sav) : null,
            Nickname = pk.Nickname,
            IsNicknamed = pk.IsNicknamed,
            NicknameMax = pk.MaxStringLengthNickname,
            Level = pk.CurrentLevel,
            Exp = pk.EXP,
            Nature = format >= 3 ? (int)pk.Nature : null,
            StatNature = (int)pk.StatAlignment,
            HasStatNature = format >= 8,
            Ability = ability,
            Abilities = abilities,
            AbilityNote = GetAbilityNote(pk),
            HeldItem = pk.HeldItem,
            HasHeldItem = format >= 2 && (HaX || sav is not (SAV7b or SAV8LA)),
            Gender = pk.Gender,
            GenderLocked = !basePi.IsDualGender,
            Shiny = shiny,
            IsEgg = pk.IsEgg,
            Pid = format >= 3 ? pk.PID.ToString("X8") : string.Empty,
            Ec = format >= 6 ? pk.EncryptionConstant.ToString("X8") : string.Empty,
            HasEC = format >= 6,
            Language = format >= 3 ? pk.Language : null,
            Friendship = format >= 2 ? pk.OriginalTrainerFriendship : null,
            Ball = pk.Ball,
            Types = types,
            Met = met,
            Ot = ot,
            OtMax = pk.MaxStringLengthTrainer,
            Ht = ht,
            Stats = stats,
            IvMax = pk.MaxIV,
            EvMax = pk.MaxEV,
            EvTotalMax = format >= 3 ? 510 : 0,
            HasHyperTraining = pk is IHyperTrain,
            Fields = GetFields(pk, sav),
            TeraType = tera,
            HasTera = pk is ITeraType,
            HiddenPower = hiddenPower,
            Moves = moves,
            Learnable = learnable,
            MoveFlags = GetMoveFlagEditors(pk),
            Editors = GetDetailEditors(pk),
            Relearn = relearn.Select(z => (int)z).ToArray(),
            HasRelearn = format >= 6,
            RibbonCount = ribbons,
            Legality = legality,
        };
    }

    private static readonly (string Key, string Name, int Index, int Amp)[] StatOrder =
    [
        ("hp", "HP", 0, -1), ("atk", "Attack", 1, 0), ("def", "Defense", 2, 1),
        ("spa", "Sp. Atk", 4, 3), ("spd", "Sp. Def", 5, 4), ("spe", "Speed", 3, 2),
    ];

    private static int StatIndex(string key) => key switch
    {
        "hp" => 0, "atk" => 1, "def" => 2, "spe" => 3, "spa" => 4, "spd" => 5,
        _ => throw new ArgumentException($"Unknown stat '{key}'."),
    };

    /// <summary>
    /// Game Boy personal data stores types in the original games' internal numbering (e.g. Fire = 20);
    /// map them to the modern indexes used by <see cref="GameStrings.types"/>.
    /// </summary>
    private static byte ModernTypeGB(byte type) => type switch
    {
        <= 5 => type,  // Normal, Fighting, Flying, Poison, Ground, Rock
        7 => 6,        // Bug
        8 => 7,        // Ghost
        9 => 8,        // Steel
        20 => 9, 21 => 10, 22 => 11, 23 => 12, 24 => 13, 25 => 14, 26 => 15, // Fire .. Dragon
        27 => 16,      // Dark
        _ => 0,
    };

    private static string TypeName(int type) => (uint)type < English.types.Length ? English.types[type].ToLowerInvariant() : "normal";
    private static string? FormatDate(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static int GetPP(PKM pk, int i) => i switch { 0 => pk.Move1_PP, 1 => pk.Move2_PP, 2 => pk.Move3_PP, _ => pk.Move4_PP };
    private static int GetPPUps(PKM pk, int i) => i switch { 0 => pk.Move1_PPUps, 1 => pk.Move2_PPUps, 2 => pk.Move3_PPUps, _ => pk.Move4_PPUps };

    private static void SetPP(PKM pk, int i, int v)
    {
        switch (i) { case 0: pk.Move1_PP = v; break; case 1: pk.Move2_PP = v; break; case 2: pk.Move3_PP = v; break; default: pk.Move4_PP = v; break; }
    }

    private static void SetPPUps(PKM pk, int i, int v)
    {
        switch (i) { case 0: pk.Move1_PPUps = v; break; case 1: pk.Move2_PPUps = v; break; case 2: pk.Move3_PPUps = v; break; default: pk.Move4_PPUps = v; break; }
    }

    // Ability: "index" mode (list index = slot) for normal editing, "raw" (ability id) for PA9 / HaX.
    private static bool RawAbility(PKM pk) => HaX;

    /// <summary> Changing species or form refreshes the ability for that slot, except in Z-A, where the birth ability stays. </summary>
    private static bool AbilityFollowsSpecies(PKM pk) => pk.Format >= 3 && !RawAbility(pk) && pk is not PA9;

    /// <summary>
    /// Ability slots to choose from. Z-A keeps the ability a Pokémon was born with when it evolves, so its slots come from
    /// the species it was born as (AbilityVerifier.VerifyBirthAbility); a HOME-tracked one is realigned to its own species.
    /// </summary>
    private IPersonalAbility12 GetAbilitySlots(PKM pk)
    {
        var (species, form) = GetBirthSpecies(pk);
        return pk is PA9 ? PersonalTable.ZA[species, form] : (IPersonalAbility12)pk.PersonalInfo;
    }

    /// <summary> Z-A: the species the Pokémon was born as. Other games (and HOME-tracked Z-A Pokémon): its current species. </summary>
    private (ushort Species, byte Form) GetBirthSpecies(PKM pk)
    {
        if (pk is not PA9 || pk is IHomeTrack { HasTracker: true } || pk.Species == 0)
            return (pk.Species, pk.Form);

        // The encounter the legality check matched, if it found one.
        if (Legality is { Parsed: true } la && la.EncounterMatch is { Species: not 0 } enc and not EncounterInvalid)
            return (enc.Species, enc.Form);

        // Otherwise the closest species in its line whose slot holds the stored ability (e.g. evolved in the editor
        // below the evolution level, where no encounter matches).
        var slot = pk.AbilityNumber is 1 or 2 or 4 ? pk.AbilityNumber >> 1 : 0;
        if (PersonalTable.ZA[pk.Species, pk.Form].GetAbilityAtIndex(slot) == pk.Ability)
            return (pk.Species, pk.Form);
        var line = EvolutionTree.GetEvolutionTree(EntityContext.Gen9a).Reverse.GetPreEvolutions(pk.Species, pk.Form).Reverse();
        foreach (var (s, f) in line)
        {
            if (PersonalTable.ZA[s, f].GetAbilityAtIndex(slot) == pk.Ability)
                return (s, f);
        }
        return (pk.Species, pk.Form);
    }

    /// <summary> Legends: Arceus and Z-A have no abilities in battle; the stored one applies once the Pokémon moves on through HOME. </summary>
    private string? GetAbilityNote(PKM pk)
    {
        if (pk is not (PA8 or PA9) || pk.Species == 0)
            return null;
        var game = pk is PA9 ? "Z-A" : "Legends: Arceus";
        var note = $"Not used in {game} battles. It's the ability it will have in other games through Pokémon HOME.";
        var (species, form) = GetBirthSpecies(pk);
        if (pk is PA9 && !RawAbility(pk) && (species != pk.Species || form != pk.Form))
        {
            var name = species < GameInfo.Strings.specieslist.Length ? GameInfo.Strings.specieslist[species] : $"#{species}";
            note += $" Z-A keeps the ability it was born with, so these are {name}'s.";
        }
        return note;
    }

    private (List<Opt> Options, int Selected) GetAbilityState(PKM pk)
    {
        if (pk.Format < 3)
            return ([], 0);
        if (RawAbility(pk))
            return ([.. GameInfo.FilteredSources.Abilities.Select(z => new Opt(z.Value, z.Text))], pk.Ability);

        // PKMEditor.SetAbilityList: the species' slots as "Name (1)", "Name (2)", "Name (H)".
        var slots = GetAbilitySlots(pk);
        var list = GameInfo.FilteredSources.GetAbilityList((IPersonalAbility)slots);
        var options = list.Select((z, i) => new Opt(i, z.Text)).ToList();
        if (pk is { Context: EntityContext.Gen5, Species: (ushort)Species.Basculin, Form: 1 })
            options.Add(new Opt(BasculinReckless, FilteredGameDataSource.GetAbilityItem(GameInfo.Strings.abilitylist, (int)PKHeX.Core.Ability.Reckless, '*').Text));
        if (pk is PK5 { Species: (ushort)Species.Basculin, Form: 1, Ability: (int)PKHeX.Core.Ability.Reckless })
            return (options, BasculinReckless);
        return (options, Math.Clamp(GetAbilityIndex(pk, list.Count), 0, Math.Max(0, list.Count - 1)));
    }

    /// <summary> Gen 5 Blue-Striped Basculin may have Reckless, which isn't one of its slots (PKMEditor.SetAbilityList adds it). </summary>
    private const int BasculinReckless = 3;

    private static int GetAbilityIndex(PKM pk, int count)
    {
        switch (pk)
        {
            case G3PKM g3:
                return g3.AbilityBit && count > 1 ? 1 : 0;
            case PK5 pk5:
                return pk5.HiddenAbility ? 2 : GetAbilityIndex4(pk5);
            default:
                if (pk.Format <= 4)
                    return GetAbilityIndex4(pk);
                var n = pk.AbilityNumber;
                return n is 1 or 2 or 4 ? n >> 1 : 0;
        }
    }

    private static int GetAbilityIndex4(PKM pk)
    {
        var pi = pk.PersonalInfo;
        int i = pi.GetIndexOfAbility(pk.Ability);
        if (i >= 2)
            return 2;
        if (i < 0)
            return 0;
        if (pi is IPersonalAbility12 { IsAbility12Same: true })
            return Math.Max(0, pk.PIDAbility);
        return i;
    }

    // ------------------------------------------------------------------ edits

    public void Set(string field, JsonElement value)
    {
        var pk = Entity;
        var sav = Sav;
        switch (field)
        {
            case "species": SetSpecies(pk, (ushort)value.GetInt32()); break;
            case "form": SetForm(pk, (byte)value.GetInt32()); break;
            case "nickname": SetNicknameText(pk, value.GetString() ?? string.Empty); break;
            case "isNicknamed":
                pk.IsNicknamed = value.GetBoolean();
                if (!pk.IsNicknamed)
                    ResetNickname(pk);
                break;
            case "level":
                pk.EXP = Experience.GetEXP((byte)Math.Clamp(value.GetInt32(), Experience.MinLevel, Experience.MaxLevel), pk.PersonalInfo.EXPGrowth);
                pk.Stat_Level = pk.CurrentLevel;
                break;
            case "exp":
            {
                pk.EXP = (uint)Math.Max(0, value.GetInt64());
                if (!HaX && pk.CurrentLevel == 100)
                    pk.EXP = Experience.GetEXP(100, pk.PersonalInfo.EXPGrowth);
                pk.Stat_Level = pk.CurrentLevel;
                break;
            }
            case "nature": SetNature(pk, (Nature)value.GetInt32()); break;
            case "statNature":
                if (pk.Format >= 8 && (uint)value.GetInt32() < (uint)Nature.Random)
                    pk.StatAlignment = (Nature)value.GetInt32();
                break;
            case "ability": SetAbility(pk, value.GetInt32()); break;
            case "heldItem": pk.HeldItem = value.GetInt32(); break;
            case "gender": SetGender(pk, (byte)value.GetInt32()); break;
            case "shiny": SetShiny(pk, value.GetString() ?? "none", keepPID: false); break;
            case "shinySID": SetShiny(pk, value.GetString() ?? "none", keepPID: pk.Format >= 3); break;
            case "isEgg": SetEgg(pk, sav, value.GetBoolean()); break;
            case "pid": if (pk.Format >= 3) pk.PID = Util.GetHexValue(value.GetString() ?? string.Empty); break;
            case "ec": if (pk.Format >= 6) pk.EncryptionConstant = Util.GetHexValue(value.GetString() ?? string.Empty); break;
            case "language":
                if (pk.Format >= 3)
                {
                    pk.Language = value.GetInt32();
                    ResetNickname(pk);
                }
                break;
            case "friendship": pk.OriginalTrainerFriendship = (byte)Math.Clamp(value.GetInt32(), 0, 255); break;
            case "ball": pk.Ball = (byte)value.GetInt32(); break;
            case "teraType": SetTera(pk, value.GetInt32()); break;
            default:
                if (field.StartsWith("met.", StringComparison.Ordinal)) SetMet(pk, sav, field[4..], value);
                else if (field.StartsWith("ot.", StringComparison.Ordinal)) SetOT(pk, field[3..], value);
                else if (field.StartsWith("ht.", StringComparison.Ordinal)) SetHT(pk, field[3..], value);
                else if (field.StartsWith("stats.", StringComparison.Ordinal)) SetStat(pk, field[6..], value);
                else if (field.StartsWith("moves.", StringComparison.Ordinal)) SetMove(pk, field[6..], value);
                else if (field.StartsWith("x.", StringComparison.Ordinal)) SetField(pk, field[2..], value);
                else if (field.StartsWith("relearn.", StringComparison.Ordinal)) { if (pk.Format >= 6) pk.SetRelearnMove(int.Parse(field[8..], CultureInfo.InvariantCulture), (ushort)value.GetInt32()); }
                else throw new ArgumentException($"Unknown field '{field}'.");
                break;
        }
        Changed();
    }

    private void SetSpecies(PKM pk, ushort species)
    {
        if (species > pk.MaxSpeciesID)
            return;
        var abilityIndex = AbilityFollowsSpecies(pk) ? GetAbilityIndex(pk, pk.PersonalInfo.AbilityCount) : -1;
        pk.Species = species;
        if (!HaX)
            pk.Form = 0;
        pk.EXP = Experience.GetEXP(pk.CurrentLevel, pk.PersonalInfo.EXPGrowth);
        if (abilityIndex >= 0)
            StoreAbilityAtIndex(pk, Math.Clamp(abilityIndex, 0, pk.PersonalInfo.AbilityCount - 1));
        if (pk.Format >= 3)
            pk.Gender = pk.GetSaneGender();
        if (pk is IFormArgument fa && FormArgumentUtil.GetType(pk.Species, pk.Form, pk.Context) == FormArgumentType.None)
            fa.FormArgument = 0;
        ResetNickname(pk);
    }

    /// <summary>
    /// Gen 3 Deoxys: its forme isn't stored in the Pokémon, the game it's in decides it (the sprite engine draws it the same way).
    /// </summary>
    private static FormNoteDto? GetFormNote(PKM pk, SaveFile sav)
    {
        if (pk.Format != 3 || pk.Species != (ushort)Species.Deoxys || sav.Generation != 3)
            return null;
        var game = sav switch
        {
            SAV3FRLG f => ReferenceEquals(f.Personal, PersonalTable.FR) ? GameVersion.FR : GameVersion.LG,
            SAV3E => GameVersion.E,
            _ => GameVersion.RS,
        };
        byte form = game switch { GameVersion.FR => 1, GameVersion.LG => 2, GameVersion.E => 3, _ => 0 };
        var names = GetFormNames(pk);
        var name = form < names.Length ? names[form] : "Normal";
        var gameName = game == GameVersion.RS ? "Ruby/Sapphire" : GameInfo.Strings.gamelist[(int)game];
        return new FormNoteDto($"{name} ({gameName})",
            "In Gen 3, Deoxys takes the forme of the game it's in: Normal in Ruby/Sapphire, Attack in FireRed, Defense in LeafGreen, Speed in Emerald.");
    }

    /// <summary> PKMEditor's Ribbons / Memories / Medals buttons (BTN_Ribbons, BTN_History, BTN_Medals visibility). </summary>
    private static List<string> GetDetailEditors(PKM pk)
    {
        var list = new List<string>(3);
        if (pk.Format >= 3)
            list.Add("ribbons");
        if (pk.Format >= 6 && pk is not PB7)
            list.Add("memories");
        if (pk.Format is 6 or 7 && pk is not PB7 && pk is ISuperTrainRegimen)
            list.Add("medals");
        return list;
    }

    /// <summary> PKMEditor's move flag buttons this Pokémon has (Relearn Flags, Move Shop, Plus Flags). </summary>
    private static List<string> GetMoveFlagEditors(PKM pk)
    {
        var list = new List<string>(3);
        if (pk is ITechRecord)
            list.Add("records");
        if (pk is IMoveShop8Mastery)
            list.Add("moveshop");
        if (pk is IPlusRecord && pk.PersonalInfo is IPermitPlus)
            list.Add("plus");
        return list;
    }

    private readonly LegalMoveInfo LearnInfo = new();

    /// <summary> Moves the Pokémon can legally know: PKMEditor's move list shows these first, highlighted (LegalMoveSource). </summary>
    private List<int> GetLearnableMoves(LegalityAnalysis la, int maxMove)
    {
        LearnInfo.ReloadMoves(la);
        var list = new List<int>();
        for (ushort move = 1; move <= maxMove; move++)
        {
            if (LearnInfo.CanLearn(move))
                list.Add(move);
        }
        return list;
    }

    /// <summary> The form names PKMEditor's form list shows (also valid for forms the personal data doesn't count, e.g. Unown). </summary>
    private static string[] GetFormNames(PKM pk)
    {
        var strings = GameInfo.Strings;
        return FormConverter.GetFormList(pk.Species, strings.types, strings.forms, GameInfo.GenderSymbolUnicode, pk.Context);
    }

    /// <summary> Species whose two forms are its genders (Meowstic, Indeedee, ...): PKMEditor keeps form and gender in step. </summary>
    private static bool HasGenderForms(PKM pk, string[] names)
        => names.Length == 2 && EntityGender.GetFromString(names[0]) < 2 && EntityGender.GetFromString(names[1]) < 2;

    /// <summary> PKMEditor.UpdateForm </summary>
    private void SetForm(PKM pk, byte form)
    {
        var names = GetFormNames(pk);
        if (!HaX && form >= Math.Max(1, names.Length))
            throw new ArgumentOutOfRangeException(nameof(form), $"Form {form} doesn't exist for this species.");
        var abilityIndex = AbilityFollowsSpecies(pk) ? GetAbilityIndex(pk, pk.PersonalInfo.AbilityCount) : -1;
        if (pk.Format == 3 && pk.Species == (ushort)Species.Unown && form < names.Length)
            pk.SetPIDUnown3(form); // the PID search can only find Unown's 28 letters (HaX may go past them)
        pk.Form = form; // Gen 1/2 Unown: rerolls the DVs until they spell the letter
        pk.EXP = Experience.GetEXP(pk.CurrentLevel, pk.PersonalInfo.EXPGrowth);
        if (abilityIndex >= 0)
            StoreAbilityAtIndex(pk, Math.Clamp(abilityIndex, 0, pk.PersonalInfo.AbilityCount - 1));
        if (pk.Format >= 3 && HasGenderForms(pk, names))
            pk.Gender = form;
        if (pk.Format >= 3)
            pk.Gender = pk.GetSaneGender();
        if (pk is IFormArgument fa && FormArgumentUtil.GetType(pk.Species, pk.Form, pk.Context) == FormArgumentType.None)
            fa.FormArgument = 0;
    }

    /// <summary> Stores the ability for a list index without rerolling the PID (species/form changes). </summary>
    private static void StoreAbilityAtIndex(PKM pk, int index)
    {
        var pi = pk.PersonalInfo;
        switch (pk)
        {
            case G3PKM g3: g3.AbilityBit = index != 0 && pi.AbilityCount > 1; break;
            case PK5 pk5: pk5.Ability = pi.GetAbilityAtIndex(index); pk5.HiddenAbility = index == 2; break;
            default:
                if (pk.Format <= 4)
                    pk.Ability = pi.GetAbilityAtIndex(index);
                else
                    pk.RefreshAbility(index);
                break;
        }
    }

    private void SetAbility(PKM pk, int value)
    {
        if (pk.Format < 3)
            return;
        if (RawAbility(pk))
        {
            pk.Ability = value;
            return;
        }
        if (value == BasculinReckless && pk is PK5 { Species: (ushort)Species.Basculin, Form: 1 } basculin)
        {
            basculin.Ability = (int)PKHeX.Core.Ability.Reckless;
            basculin.HiddenAbility = false;
            return;
        }
        if (pk is PA9 za)
        {
            // Z-A: the slot's ability comes from the birth species (see GetAbilitySlots).
            var slots = GetAbilitySlots(pk);
            var slot = Math.Clamp(value, 0, 2);
            za.AbilityNumber = 1 << slot;
            za.Ability = slots.GetAbilityAtIndex(slot);
            return;
        }
        var index = Math.Clamp(value, 0, pk.PersonalInfo.AbilityCount - 1);
        // Gen3-5: picking slot 1/2 rerolls the PID so the ability bit matches (keeps gender and nature).
        if (pk.Format <= 5 && index < 2 && pk.PIDAbility > -1 && index != pk.PIDAbility)
            pk.SetAbilityIndex(index);
        StoreAbilityAtIndex(pk, index);
    }

    private static void SetNature(PKM pk, Nature nature)
    {
        if (pk.Format < 3)
            return;
        if (nature >= Nature.Random) // IsFixed only rejects 25; anything >= 25 would make SetPIDNature search forever
            throw new ArgumentOutOfRangeException(nameof(nature), "Unknown nature.");
        if (pk.Format <= 4 && pk.Nature != nature)
            pk.SetPIDNature(nature); // Gen3/4 nature comes from the PID
        pk.Nature = nature;
    }

    /// <summary> PKMEditor.ClickGender </summary>
    private void SetGender(PKM pk, byte gender)
    {
        var pi = pk.PersonalInfo;
        if (!pi.IsDualGender)
            return;
        if (gender > 1)
            gender = 0;
        if (pk.Format <= 2)
            pk.SetAttackIVFromGender(gender);
        else if (pk.Format <= 4)
            pk.SetPIDGender(gender);
        pk.Gender = gender;

        // Gendered forms follow the gender.
        if (pk.Format >= 3 && pk.Form != gender && HasGenderForms(pk, GetFormNames(pk)))
            SetForm(pk, gender);
    }

    /// <summary>
    /// PKMEditor.UpdateShiny: a new shiny PID, or with <paramref name="keepPID"/> (Alt+click in PKHeX) a new SID instead,
    /// so the PID and its PID/IV correlation stay as they are.
    /// </summary>
    private static void SetShiny(PKM pk, string type, bool keepPID)
    {
        if (pk is GBPKM gb)
        {
            if (type == "none")
            {
                if (gb.IsShiny)
                    gb.IV_SPE = gb.IV_SPE == 10 ? 11 : gb.IV_SPE; // any DV change breaks the shiny pattern
            }
            else if (!gb.IsShiny)
            {
                gb.SetShiny();
            }
            return;
        }
        if (type == "none")
        {
            if (!pk.IsShiny)
                return;
            if (!keepPID)
            {
                pk.SetUnshiny();
                return;
            }
            var rnd = Util.Rand;
            while (pk.IsShiny)
                pk.SID16 = (ushort)rnd.Next(ushort.MaxValue + 1);
            return;
        }

        var shiny = type switch
        {
            "square" => Shiny.AlwaysSquare,
            "star" => Shiny.AlwaysStar,
            _ => Shiny.Random,
        };
        if (keepPID)
            pk.SetShinySID(shiny);
        else
            pk.SetShiny(shiny);
    }

    private void SetEgg(PKM pk, SaveFile sav, bool egg)
    {
        if (egg == pk.IsEgg || pk.Format < 2)
            return; // Gen1 has no eggs (PK1.IsEgg can't be set)
        if (egg)
        {
            if (pk.Format == 3)
                pk.OriginalTrainerName = pk.OriginalTrainerName; // PK3 remaps OT/language when becoming an egg
            pk.IsEgg = true;
            pk.OriginalTrainerFriendship = (byte)EggStateLegality.GetMinimumEggHatchCycles(pk);
            if (pk.Format >= 4)
            {
                if (!_metAsEgg)
                {
                    // Same as ticking "met as egg": today's date and the suggested egg location.
                    pk.EggMetDate = DateOnly.FromDateTime(DateTime.Today);
                    pk.EggLocation = EncounterSuggestion.GetSuggestedEncounterEggLocationEgg(pk, pk.Version != sav.Version);
                }
                pk.MetDate = new DateOnly(2000, 1, 1);
            }
            _metAsEgg = true;
            if (pk.Format >= 4)
            {
                bool traded = sav.OT != pk.OriginalTrainerName || sav.TID16 != pk.TID16 || sav.SID16 != pk.SID16;
                pk.MetLocation = traded ? Locations.TradedEggLocation(sav.Generation, sav.Version) : LocationEdits.GetNoneLocation(pk);
            }
            pk.IsNicknamed = EggStateLegality.IsNicknameFlagSet(pk);
            pk.Nickname = SpeciesName.GetEggName(pk.Language, pk.Format);
            if (pk.Format >= 6 && Program.Settings.SlotWrite.SetUpdatePKM)
                pk.ClearMemories();
            if (pk is PK9)
                pk.Version = 0;
            return;
        }

        // Hatch
        var eggName = SpeciesName.GetEggName(pk.Language, pk.Format);
        pk.IsEgg = false;
        pk.OriginalTrainerFriendship = pk.PersonalInfo.BaseFriendship; // egg cycles become friendship again
        if (pk.Format >= 4)
        {
            if (pk.EggLocation == LocationEdits.GetNoneLocation(pk))
            {
                pk.MetDate = DateOnly.FromDateTime(DateTime.Today);
                pk.EggMetDate = null;
                _metAsEgg = false;
            }
            else
            {
                pk.MetDate = pk.EggMetDate;
                pk.MetLocation = EncounterSuggestion.GetSuggestedEggMetLocation(pk);
            }
        }
        if (pk.Nickname == eggName)
        {
            pk.IsNicknamed = false;
            ResetNickname(pk);
        }
    }

    private static void SetTera(PKM pk, int value)
    {
        if (pk is not ITeraType t)
            return;
        var type = value == 18 ? (MoveType)TeraTypeUtil.Stellar : (MoveType)value;
        t.TeraTypeOverride = type == t.TeraTypeOriginal ? (MoveType)TeraTypeUtil.OverrideNone : type;
    }

    private void SetMet(PKM pk, SaveFile sav, string part, JsonElement value)
    {
        switch (part)
        {
            case "version":
            {
                var oldGroup = GameUtil.GetMetLocationVersionGroup(pk.Version);
                pk.Version = (GameVersion)value.GetInt32();
                if (GameUtil.GetMetLocationVersionGroup(pk.Version) != oldGroup)
                {
                    pk.MetLocation = EncounterSuggestion.TryGetSuggestedTransferLocation(pk); // 0 (none) when there is no transfer location
                    if (pk.Format >= 4)
                        pk.EggLocation = _metAsEgg ? EncounterSuggestion.GetSuggestedEncounterEggLocationEgg(pk, pk.IsEgg && pk.Version != sav.Version) : LocationEdits.GetNoneLocation(pk);
                }
                break;
            }
            case "location": pk.MetLocation = (ushort)value.GetInt32(); break;
            case "level": pk.MetLevel = (byte)Math.Clamp(value.GetInt32(), 0, 100); break;
            case "date": pk.MetDate = ParseDate(value); break;
            case "eggLocation":
                pk.EggLocation = (ushort)value.GetInt32();
                _metAsEgg = pk.EggLocation != LocationEdits.GetNoneLocation(pk);
                break;
            case "eggDate":
                pk.EggMetDate = ParseDate(value);
                if (pk.EggMetDate is not null)
                    _metAsEgg = true;
                break;
            case "fateful": pk.FatefulEncounter = value.GetBoolean(); break;
            default: throw new ArgumentException($"Unknown met field '{part}'.");
        }
    }

    private static DateOnly? ParseDate(JsonElement value)
    {
        var s = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        return DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static void SetOT(PKM pk, string part, JsonElement value)
    {
        switch (part)
        {
            case "name": pk.OriginalTrainerName = value.GetString() ?? string.Empty; break;
            case "gender": if (pk.Format >= 2) pk.OriginalTrainerGender = (byte)(value.GetInt32() & 1); break;
            case "tid": SetTrainerId(pk, value.GetString(), isTid: true); break;
            case "sid": SetTrainerId(pk, value.GetString(), isTid: false); break;
            default: throw new ArgumentException($"Unknown OT field '{part}'.");
        }
    }

    private static void SetTrainerId(PKM pk, string? text, bool isTid)
    {
        if (!uint.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return;
        switch (pk.TrainerIDDisplayFormat)
        {
            case TrainerIDFormat.SixDigit:
                if (isTid) pk.SetTrainerTID7(Math.Min(v, 999_999));
                else
                {
                    var sid = Math.Min(v, 4294u);
                    if (!pk.IsValidTrainerID7(sid, pk.GetTrainerTID7()))
                        sid = 4293u; // TID7+SID7 must fit in 32 bits
                    pk.SetTrainerSID7(sid);
                }
                break;
            case TrainerIDFormat.SixteenBitSingle:
                if (isTid) pk.TID16 = (ushort)Math.Min(v, ushort.MaxValue);
                break;
            default:
                if (isTid) pk.TID16 = (ushort)Math.Min(v, ushort.MaxValue);
                else pk.SID16 = (ushort)Math.Min(v, ushort.MaxValue);
                break;
        }
    }

    private static void SetHT(PKM pk, string part, JsonElement value)
    {
        if (pk.Format < 6)
            return;
        switch (part)
        {
            case "name":
                pk.HandlingTrainerName = value.GetString() ?? string.Empty;
                if (pk.HandlingTrainerName.Length == 0)
                {
                    pk.CurrentHandler = 0;
                    pk.HandlingTrainerGender = 0;
                }
                break;
            case "gender": pk.HandlingTrainerGender = (byte)(value.GetInt32() & 1); break;
            case "friendship": pk.HandlingTrainerFriendship = (byte)Math.Clamp(value.GetInt32(), 0, 255); break;
            default: throw new ArgumentException($"Unknown handler field '{part}'.");
        }
    }

    private static void SetStat(PKM pk, string path, JsonElement value)
    {
        var parts = path.Split('.');
        if (parts.Length != 2)
            throw new ArgumentException($"Bad stat field '{path}'.");
        var index = StatIndex(parts[0]);
        switch (parts[1])
        {
            case "iv": pk.SetIV(index, Math.Clamp(value.GetInt32(), 0, pk.MaxIV)); break;
            case "ev": pk.SetEV(index, Math.Clamp(value.GetInt32(), 0, pk.MaxEV)); break;
            case "ht":
                if (pk is IHyperTrain h && h.IsHyperTrained(index) != value.GetBoolean())
                    h.HyperTrainInvert(index);
                break;
            case "gv" when pk is IGanbaru g: g.SetGV(index, (byte)Math.Clamp(value.GetInt32(), 0, GanbaruExtensions.TrueMax)); break;
            case "av" when pk is IAwakened a: a.SetAV(index, (byte)Math.Clamp(value.GetInt32(), 0, AwakeningUtil.AwakeningMax)); break;
            default: throw new ArgumentException($"Bad stat field '{path}'.");
        }
    }

    private static void SetMove(PKM pk, string path, JsonElement value)
    {
        var parts = path.Split('.');
        var i = int.Parse(parts[0], CultureInfo.InvariantCulture);
        if ((uint)i >= 4 || parts.Length != 2)
            throw new ArgumentException($"Bad move field '{path}'.");
        var move = pk.GetMove(i);
        switch (parts[1])
        {
            case "id":
                move = (ushort)Math.Clamp(value.GetInt32(), 0, pk.MaxMoveID);
                pk.SetMove(i, move);
                if (move == 0 || !Legal.IsPPUpAvailable(pk) || !Legal.IsPPUpAvailable(move))
                    SetPPUps(pk, i, 0);
                SetPP(pk, i, move == 0 ? 0 : pk.GetMovePP(move, GetPPUps(pk, i)));
                break;
            case "ppUps":
                var ups = move != 0 && Legal.IsPPUpAvailable(pk) && Legal.IsPPUpAvailable(move) ? Math.Clamp(value.GetInt32(), 0, 3) : 0;
                SetPPUps(pk, i, ups);
                SetPP(pk, i, move == 0 ? 0 : pk.GetMovePP(move, ups));
                break;
            case "pp":
                SetPP(pk, i, move == 0 ? 0 : Math.Clamp(value.GetInt32(), 0, pk.GetMovePP(move, GetPPUps(pk, i))));
                break;
            default: throw new ArgumentException($"Bad move field '{path}'.");
        }
    }

    // ------------------------------------------------------------------ nickname helpers (mirrors PKMEditor)

    private static bool IsPossibleNotNicknamed(PKM pk, ReadOnlySpan<char> current)
    {
        var species = pk.IsEgg ? (ushort)0 : pk.Species;
        var ctx = pk.Context;
        if (!SpeciesName.IsNicknamedAnyLanguage(species, current, ctx))
            return true;
        if (pk.IsEgg)
            return false;
        if (ctx != EntityContext.Gen5 || species > 493 || pk.Gen5)
            return false;
        return !SpeciesName.IsNicknamedAnyLanguage(species, current, EntityContext.Gen4);
    }

    private static void SetNicknameText(PKM pk, string text)
    {
        pk.Nickname = text;
        if (!pk.IsNicknamed && pk.Species is > 0 && pk.Species <= pk.MaxSpeciesID && !IsPossibleNotNicknamed(pk, text))
            pk.IsNicknamed = true;
    }

    private static void ResetNickname(PKM pk)
    {
        if (pk.IsNicknamed)
            return;
        if (pk.Species == 0 || pk.Species > pk.MaxSpeciesID)
        {
            pk.Nickname = string.Empty;
            return;
        }
        // PKHeX keeps a name that is already a species name in some language; we re-localize like "Clear nickname".
        var lang = pk.Language;
        pk.Nickname = pk.IsEgg
            ? SpeciesName.GetEggName(lang, pk.Format)
            : SpeciesName.GetSpeciesNameGeneration(pk.Species, lang, pk.Format);
        if (pk is GBPKM gb)
            gb.SetNotNicknamed(lang);
    }

    // ------------------------------------------------------------------ suggestions

    public string? Suggest(string what)
    {
        var pk = Entity;
        string? message = null;
        switch (what)
        {
            case "moves":
            {
                Span<ushort> m = stackalloc ushort[4];
                pk.GetMoveSet(m);
                if (m[0] == 0)
                    return "No legal move suggestion is available.";
                pk.SetMoves(m);
                if (pk is ITechRecord tr)
                {
                    tr.ClearRecordFlags();
                    tr.SetRecordFlags(m, new LegalityAnalysis(pk).Info.EvoChainsAllGens.Get(pk.Context));
                }
                pk.HealPP();
                break;
            }
            case "relearn":
            {
                if (pk.Format < 6)
                    return "Relearn moves don't exist in this format.";
                Span<ushort> m = stackalloc ushort[4];
                new LegalityAnalysis(pk, Sav.Personal).GetSuggestedRelearnMoves(m);
                pk.SetRelearnMoves(m);
                break;
            }
            case "met":
                message = SuggestMet(pk);
                break;
            case "maxIVs":
            {
                Span<int> ivs = stackalloc int[6];
                ivs.Fill(pk.MaxIV);
                pk.SetIVs(ivs);
                if (pk is IGanbaru g)
                    g.SetSuggestedGanbaruValues(pk);
                break;
            }
            case "randomIVs":
            {
                Span<int> ivs = stackalloc int[6];
                var enc = new LegalityAnalysis(pk).EncounterMatch;
                if (enc is IFlawlessIVCount { FlawlessIVCount: not 0 } fc)
                    pk.SetRandomIVs(ivs, fc.FlawlessIVCount);
                else if (enc is IFixedIVSet { IVs: { IsSpecified: true } iv })
                    pk.SetRandomIVs(ivs, iv);
                else
                    pk.SetRandomIVs(ivs);
                break;
            }
            case "clearEVs":
            {
                Span<int> evs = stackalloc int[6];
                pk.SetEVs(evs);
                break;
            }
            case "suggestEVs":
            {
                Span<int> evs = stackalloc int[6];
                EffortValues.SetMax(evs, pk);
                pk.SetEVs(evs);
                break;
            }
            case "rerollPID":
                if (pk.Format < 3)
                    return "Game Boy Pokémon have no PID.";
                pk.SetPIDGender(pk.Gender);
                if (pk.Format >= 6 && pk.Generation is 3 or 4 or 5)
                    pk.EncryptionConstant = pk.PID;
                break;
            case "rerollEC":
                if (pk.Format >= 6)
                    pk.SetRandomEC();
                break;
            case var f when f.StartsWith("field:", StringComparison.Ordinal):
                message = SuggestField(pk, f[6..]);
                if (message is not null)
                    return message;
                break;
            // Shift+click on PKMEditor's move flag buttons: the flags the Pokémon can legally have.
            case "legalRecords":
                if (pk is not ITechRecord records)
                    return "This Pokémon has no relearn flags.";
                records.SetRecordFlags(pk, TechnicalRecordApplicatorOption.LegalCurrent);
                break;
            case "legalMoveShop":
            {
                if (pk is not IMoveShop8Mastery shop)
                    return "This Pokémon has no Move Shop flags.";
                shop.ClearMoveShopFlags();
                var enc = Legality.EncounterMatch;
                if (enc is IMasteryInitialMoveShop8 initial)
                    initial.SetInitialMastery(pk, enc);
                shop.SetMoveShopFlags(pk);
                break;
            }
            case "legalPlus":
                if (pk is not IPlusRecord plus || pk.PersonalInfo is not IPermitPlus permit)
                    return "This Pokémon has no Plus flags.";
                plus.SetPlusFlags(pk, permit, PlusRecordApplicatorOption.LegalCurrent);
                break;
            default:
                throw new ArgumentException($"Unknown suggestion '{what}'.");
        }
        Changed();
        return message;
    }

    private string? SuggestMet(PKM pk)
    {
        if (HaX)
            return null;
        if (new LegalityAnalysis(Prepare(), Sav.Personal).Valid)
            return "Already legal — no met data change needed.";
        var enc = EncounterSuggestion.GetSuggestedMetInfo(pk);
        if (enc is null || (pk.Format >= 3 && enc.Location == 0))
            return "No met location suggestion is available.";
        var level = enc.LevelMin;
        var minLevel = EncounterSuggestion.GetLowestLevel(pk, level);
        if (minLevel == 0)
            minLevel = level;
        pk.MetLocation = enc.Location;
        pk.MetLevel = enc.GetSuggestedMetLevel(pk);
        if (pk is IGroundTile gt && enc.HasGroundTile(pk.Format))
            gt.GroundTile = enc.GetSuggestedGroundTile();
        if (pk.CurrentLevel < minLevel)
            pk.CurrentLevel = (byte)minLevel;
        return null;
    }
}
