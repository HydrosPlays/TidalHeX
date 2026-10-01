using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using PKHeX.Core;

namespace PKHeX.WinForms.Tidal.Web;

// The PKMEditor fields that only some formats have (Pokérus, form argument, markings, size, contest stats, Gen 4/5/LGPE
// extras, handler language, HOME tracker, region, extra bytes...). Each is described for the page (FieldDto) and written
// with the same rules as the classic editor's controls; editor.set uses the key "x.<key>".
internal sealed partial class EditorService
{
    private const string SecMain = "overview", SecMet = "met", SecStats = "stats", SecMoves = "moves", SecExtras = "extras", SecTrainer = "trainer";

    private List<FieldDto> GetFields(PKM pk, SaveFile sav)
    {
        var list = new List<FieldDto>();
        if (pk.Species == 0)
            return list;
        var strings = GameInfo.Strings;
        var format = pk.Format;
        var source = GameInfo.FilteredSources;

        // Main: catch rate, Pokérus, form argument
        if (pk is PK1 pk1)
            list.Add(new FieldDto("catchRate", SecMain, "", "Catch rate", "number", (int)pk1.CatchRate) { Max = 255, Suggest = true, Hint = "Gen 2 trades use this as the held item" });
        if (format >= 2)
        {
            var state = pk.IsPokerusCured ? 2 : pk.IsPokerusInfected ? 1 : 0;
            list.Add(new FieldDto("pkrs.state", SecMain, "Pokérus", "Pokérus", "select", state) { Options = [new(0, "Never infected"), new(1, "Infected"), new(2, "Cured")] });
            if (state != 0)
                list.Add(new FieldDto("pkrs.strain", SecMain, "Pokérus", "Strain", "number", pk.PokerusStrain) { Min = 1, Max = 15 });
            if (state == 1)
                list.Add(new FieldDto("pkrs.days", SecMain, "Pokérus", "Days left", "number", pk.PokerusDays) { Min = 1, Max = Pokerus.GetMaxDuration(pk.PokerusStrain) });
        }
        if (format >= 6 && pk is IFormArgument fa)
            AddFormArgument(list, pk, fa);

        // Met
        if (pk is IBattleVersion bv)
            list.Add(new FieldDto("battleVersion", SecMet, "", "Battle version", "select", (int)bv.BattleVersion) { Options = WithNone(source.Games), Hint = "Lets it battle in another game's ranked rules" });
        if (format is 4 or 5 or 6 && pk is IGroundTile gt && pk.Gen4)
            list.Add(new FieldDto("groundTile", SecMet, "", "Ground tile", "select", (int)gt.GroundTile) { Options = ToOpts(source.G4GroundTiles) });
        if (format == 2 && pk is ICaughtData2 c2)
            list.Add(new FieldDto("metTimeOfDay", SecMet, "", "Met time of day", "select", c2.MetTimeOfDay) { Options = [new(0, "(None)"), new(1, "Morning"), new(2, "Day"), new(3, "Night")], Hint = "Only Crystal records it" });
        if (pk is IObedienceLevel ob)
            list.Add(new FieldDto("obedience", SecMet, "", "Obedience level", "number", (int)ob.ObedienceLevel) { Max = 100, Suggest = true });
        if (pk is PB7 pb7)
            list.Add(new FieldDto("received", SecMet, "", "Received", "datetime", FormatReceived(pb7)));

        // Stats
        if (format >= 3 && pk.Characteristic is var ch and > -1 && ch < strings.characteristics.Length)
            list.Add(new FieldDto("characteristic", SecStats, "", "Characteristic", "info", strings.characteristics[ch]));
        if (format <= 7 || pk is PB8)
            list.Add(new FieldDto("hpType", SecStats, "", "Hidden Power", "select", pk.HPType) { Options = GetHiddenPowerTypes(), Hint = "Changing it adjusts the IVs" });
        if (pk is ITeraType tera)
        {
            var options = GetTeraTypes(sav.Generation);
            list.Add(new FieldDto("teraOriginal", SecStats, "Tera Type", "Original", "select", (int)tera.TeraTypeOriginal) { Options = options[1..], Suggest = true, SuggestTip = pk.SV ? "Use the species' type (again for its other type)" : "HOME's Tera Type for this species" });
            list.Add(new FieldDto("teraOverride", SecStats, "Tera Type", "Override", "select", (int)tera.TeraTypeOverride) { Options = options, Suggest = true, SuggestTip = pk.SV ? "Clear the override" : "Match the original" });

            // StatEditor's gem: the type it Terastallizes into (the override if set).
            var active = (byte)tera.GetTeraType();
            if (IsTeraTypeShown(active))
                list.Add(new FieldDto("teraGem", SecStats, "Tera Type", "Tera Type", "image", $"/sprite/gem/{active}") { Hint = $"Terastallizes into {GetTeraTypeName(active)}" });
        }
        if (pk is IAlpha alpha)
            list.Add(new FieldDto("alpha", SecStats, "Legends", "Alpha", "bool", alpha.IsAlpha));
        if (pk is INoble noble)
            list.Add(new FieldDto("noble", SecStats, "Legends", "Noble", "bool", noble.IsNoble));
        if (format == 8 && pk is IDynamaxLevel dmax)
            list.Add(new FieldDto("dynamaxLevel", SecStats, "Dynamax", "Dynamax level", "number", (int)dmax.DynamaxLevel) { Max = 10 });
        if (format == 8 && pk is IGigantamax gmax)
            list.Add(new FieldDto("gigantamax", SecStats, "Dynamax", "Gigantamax", "bool", gmax.CanGigantamax));

        // Moves
        if (pk is PA8 pa8)
            list.Add(new FieldDto("alphaMove", SecMoves, "", "Alpha mastered move", "move", (int)pa8.AlphaMove));

        // Extras: markings, size, contest stats, game-specific
        if (format >= 3)
            list.Add(new FieldDto("marks", SecExtras, "Markings", "Markings", "marks", GetMarkings(pk)) { Max = pk is IAppliedMarkings<MarkingColor> ? 2 : 1 });
        if (pk is IFavorite fav)
            list.Add(new FieldDto("favorite", SecExtras, "Markings", "Favorite", "bool", fav.IsFavorite));
        AddSize(list, pk);
        if (pk is IContestStats cs)
        {
            var smart = pk.Context.IsEraPre3DS ? "Smart" : "Clever";
            list.Add(new FieldDto("contest.cool", SecExtras, "Contest stats", "Cool", "number", (int)cs.ContestCool) { Max = 255 });
            list.Add(new FieldDto("contest.beauty", SecExtras, "Contest stats", "Beauty", "number", (int)cs.ContestBeauty) { Max = 255 });
            list.Add(new FieldDto("contest.cute", SecExtras, "Contest stats", "Cute", "number", (int)cs.ContestCute) { Max = 255 });
            list.Add(new FieldDto("contest.smart", SecExtras, "Contest stats", smart, "number", (int)cs.ContestSmart) { Max = 255 });
            list.Add(new FieldDto("contest.tough", SecExtras, "Contest stats", "Tough", "number", (int)cs.ContestTough) { Max = 255 });
            list.Add(new FieldDto("contest.sheen", SecExtras, "Contest stats", "Sheen", "number", (int)cs.ContestSheen) { Max = 255 });
        }
        if (format == 4 && pk is G4PKM g4)
        {
            list.Add(new FieldDto("shinyLeaf", SecExtras, "HeartGold / SoulSilver", "Shiny leaves", "flags", g4.ShinyLeaf)
                { Options = [new(1, "Leaf 1"), new(2, "Leaf 2"), new(4, "Leaf 3"), new(8, "Leaf 4"), new(16, "Leaf 5"), new(32, "Crown")], Hint = "The crown needs all five leaves" });
            list.Add(new FieldDto("walkingMood", SecExtras, "HeartGold / SoulSilver", "Walking mood", "number", (int)g4.WalkingMood) { Min = -127, Max = 127 });
        }
        if (pk is PK5 pk5)
        {
            list.Add(new FieldDto("nsparkle", SecExtras, "Black / White", "N's sparkle", "bool", pk5.NSparkle));
            list.Add(new FieldDto("pokeStarFame", SecExtras, "Black / White", "Pokéstar fame", "number", (int)pk5.PokeStarFame) { Max = 255 });
        }
        if (pk is PB7 lgpe)
        {
            list.Add(new FieldDto("spirit", SecExtras, "Let's Go", "Spirit", "number", (int)lgpe.Spirit) { Max = 255 });
            list.Add(new FieldDto("mood", SecExtras, "Let's Go", "Mood", "number", (int)lgpe.Mood) { Max = 255 });
        }
        if (pk is IShadowCapture shadow)
        {
            list.Add(new FieldDto("shadow.id", SecExtras, "Shadow", "Shadow ID", "number", (int)shadow.ShadowID) { Max = 127 });
            if (shadow.ShadowID > 0)
            {
                list.Add(new FieldDto("shadow.purification", SecExtras, "Shadow", "Heart gauge", "number", shadow.Purification) { Min = -100, Max = 100_000 });
                list.Add(new FieldDto("shadow.state", SecExtras, "Shadow", "State", "info", shadow.IsShadow ? "Shadow" : "Purified"));
            }
        }

        // OT / Misc
        if (format >= 6 && pk.HandlingTrainerName.Length != 0)
            list.Add(new FieldDto("currentHandler", SecTrainer, "", "Current handler", "select", (int)pk.CurrentHandler) { Options = [new(0, "Original trainer"), new(1, "Latest handler")] });
        if (format >= 8 && pk is IHandlerLanguage hl)
            list.Add(new FieldDto("htLanguage", SecTrainer, "", "Handler language", "select", (int)hl.HandlingTrainerLanguage) { Options = WithNone(source.Languages) });
        if (pk is IRegionOrigin ro)
        {
            var lang = GameInfo.CurrentLanguage;
            list.Add(new FieldDto("country", SecTrainer, "Region", "Country", "select", (int)ro.Country) { Options = ToOpts(Util.GetCountryRegionList("countries", lang)) });
            if (ro.Country > 0)
                list.Add(new FieldDto("region", SecTrainer, "Region", "Sub-region", "select", (int)ro.Region) { Options = ToOpts(Util.GetCountryRegionList($"sr_{ro.Country:000}", lang)) });
            list.Add(new FieldDto("consoleRegion", SecTrainer, "Region", "3DS region", "select", (int)ro.ConsoleRegion) { Options = ToOpts(source.ConsoleRegions) });
        }
        if (format >= 8 && pk is IHomeTrack home && !Program.Settings.Privacy.HideSecretDetails)
            list.Add(new FieldDto("homeTracker", SecTrainer, "", "HOME tracker", "hex", home.Tracker.ToString("X16")) { Max = 16 });
        if (format >= 3 && pk.ExtraBytes.Length != 0)
            list.Add(new FieldDto("extra", SecTrainer, "", "Extra bytes", "bytes", pk.ExtraBytes.ToArray().Select(o => new[] { (int)o, pk.Data[o] }).ToArray()) { Max = 255, Hint = "Unused bytes; most should stay 0" });
        return list;
    }

    private static void AddFormArgument(List<FieldDto> list, PKM pk, IFormArgument f)
    {
        var mode = FormArgumentUtil.GetType(pk.Species, pk.Form, pk.Context);
        const string group = "Form value";
        switch (mode)
        {
            case FormArgumentType.Named:
            {
                var names = FormConverter.GetFormArgumentStrings(pk.Species);
                list.Add(new FieldDto("formArg", SecMain, group, "Form value", "select", (int)Math.Min(f.FormArgument, (uint)Math.Max(0, names.Length - 1)))
                    { Options = names.Select((t, i) => new Opt(i, t)).ToList() });
                break;
            }
            case FormArgumentType.Raw:
            {
                var max = (int)FormArgumentUtil.GetFormArgumentMaxEdge(pk.Species, pk.Form, pk.Context);
                list.Add(new FieldDto("formArg", SecMain, group, "Form value", "number", (int)Math.Min(f.FormArgument, (uint)max)) { Max = max });
                break;
            }
            case FormArgumentType.Triple:
                list.Add(new FieldDto("formArg.max", SecMain, group, "Maximum", "number", (int)f.FormArgumentMaximum) { Max = 255 });
                list.Add(new FieldDto("formArg.elapsed", SecMain, group, "Elapsed", "number", (int)f.FormArgumentElapsed) { Max = 255 });
                list.Add(new FieldDto("formArg.remain", SecMain, group, "Remaining", "number", (int)f.FormArgumentRemain) { Max = 255 });
                break;
            case FormArgumentType.TripleParty:
                list.Add(new FieldDto("formArg.remain", SecMain, group, "Days remaining", "number", (int)f.FormArgumentRemain) { Max = 255 });
                list.Add(new FieldDto("formArg.elapsed", SecMain, group, "Days elapsed", "number", (int)f.FormArgumentElapsed) { Max = 255 });
                break;
        }
    }

    /// <summary> SizeCP: height/weight scalars, scale, LGPE absolute height/weight and CP. </summary>
    private static void AddSize(List<FieldDto> list, PKM pk)
    {
        const string group = "Size";
        var hasScale = pk is IScaledSize3;
        if (pk is IScaledSize ss)
        {
            list.Add(new FieldDto("height", SecExtras, group, "Height", "number", (int)ss.HeightScalar) { Max = 255, Hint = hasScale ? null : $"Size: {PokeSizeUtil.GetSizeRating(ss.HeightScalar)}" });
            list.Add(new FieldDto("weight", SecExtras, group, "Weight", "number", (int)ss.WeightScalar) { Max = 255, Hint = hasScale ? null : $"Size: {PokeSizeUtil.GetSizeRating(ss.WeightScalar)}" });
        }
        if (pk is IScaledSize3 s3)
        {
            var rating = "Size: " + (pk is PK9 ? PokeSizeDetailedUtil.GetSizeRating(s3.Scale).ToString() : PokeSizeUtil.GetSizeRating(s3.Scale).ToString());
            if (s3.Scale is 0 or 255)
                rating += pk is PK9 ? " · Mini/Jumbo mark possible" : "";
            list.Add(new FieldDto("scale", SecExtras, group, "Scale", "number", (int)s3.Scale) { Max = 255, Hint = rating });
        }
        if (pk is IScaledSizeValue sv)
        {
            list.Add(new FieldDto("heightAbs", SecExtras, group, "Height (absolute)", "info", sv.HeightAbsolute.ToString("R", CultureInfo.InvariantCulture)));
            list.Add(new FieldDto("weightAbs", SecExtras, group, "Weight (absolute)", "info", sv.WeightAbsolute.ToString("R", CultureInfo.InvariantCulture)));
        }
        if (pk is ICombatPower cp)
            list.Add(new FieldDto("cp", SecExtras, group, "CP", "number", Math.Min(65535, cp.Stat_CP)) { Max = 65535, Suggest = true });
    }

    private static int[] GetMarkings(PKM pk)
    {
        if (pk is IAppliedMarkings<MarkingColor> c)
            return Enumerable.Range(0, c.MarkingCount).Select(i => (int)c.GetMarking(i)).ToArray();
        if (pk is IAppliedMarkings<bool> b)
            return Enumerable.Range(0, b.MarkingCount).Select(i => b.GetMarking(i) ? 1 : 0).ToArray();
        return [];
    }

    private static string FormatReceived(PB7 pk)
    {
        try
        {
            if (pk is { ReceivedDate: { } d, ReceivedTime: { } t })
                return new DateTime(d, t).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException) { /* garbage date, shown empty */ }
        return string.Empty;
    }

    private static List<Opt> GetHiddenPowerTypes()
    {
        var types = GameInfo.Strings.types;
        return Enumerable.Range(0, HiddenPower.TypeCount).Select(i => new Opt(i, types[i + 1])).ToList();
    }

    /// <summary> StatEditor's Tera list: (none), the 18 types, Stellar. Index 0 is the override's "none". </summary>
    private static List<Opt> GetTeraTypes(byte generation)
    {
        var types = GameInfo.Strings.types;
        var list = new List<Opt> { new(TeraTypeUtil.OverrideNone, "(None)") };
        for (int i = 0; i < TeraTypeUtil.StellarTypeDisplayStringIndex; i++)
            list.Add(new Opt(i, types[i]) { Img = $"/sprite/type/{i}?v={generation}" });
        list.Add(new Opt(TeraTypeUtil.Stellar, types[TeraTypeUtil.StellarTypeDisplayStringIndex]) { Img = $"/sprite/type/{TeraTypeUtil.Stellar}?v={generation}" });
        return list;
    }

    private static bool IsTeraTypeShown(byte type) => type < TeraTypeUtil.StellarTypeDisplayStringIndex || type == TeraTypeUtil.Stellar;

    private static string GetTeraTypeName(byte type) => GameInfo.Strings.types[type == TeraTypeUtil.Stellar ? TeraTypeUtil.StellarTypeDisplayStringIndex : type];

    private static List<Opt> ToOpts(IEnumerable<ComboItem> items) => items.Select(z => new Opt(z.Value, z.Text)).ToList();

    private static List<Opt> WithNone(IEnumerable<ComboItem> items)
    {
        var list = ToOpts(items);
        if (!list.Any(z => z.V == 0))
            list.Insert(0, new Opt(0, "(None)"));
        return list;
    }

    /// <summary> editor.set "x.&lt;key&gt;". </summary>
    private void SetField(PKM pk, string key, JsonElement value)
    {
        switch (key)
        {
            case "catchRate" when pk is PK1 pk1: pk1.CatchRate = (byte)Math.Clamp(value.GetInt32(), 0, 255); break;
            case "pkrs.state": SetPokerusState(pk, value.GetInt32()); break;
            case "pkrs.strain":
            {
                var cured = pk.IsPokerusCured;
                pk.PokerusStrain = Math.Clamp(value.GetInt32(), 1, 15);
                pk.PokerusDays = cured ? 0 : Math.Clamp(pk.PokerusDays, 1, Pokerus.GetMaxDuration(pk.PokerusStrain));
                break;
            }
            case "pkrs.days" when pk.IsPokerusInfected && !pk.IsPokerusCured:
                pk.PokerusDays = Math.Clamp(value.GetInt32(), 1, Pokerus.GetMaxDuration(pk.PokerusStrain));
                break;
            case "formArg" or "formArg.max" or "formArg.elapsed" or "formArg.remain" when pk is IFormArgument f:
                SetFormArgument(pk, f, key, value.GetInt32());
                break;
            case "battleVersion" when pk is IBattleVersion bv: bv.BattleVersion = (GameVersion)value.GetInt32(); break;
            case "groundTile" when pk is IGroundTile gt: gt.GroundTile = (GroundTileType)value.GetInt32(); break;
            case "metTimeOfDay" when pk is ICaughtData2 c2: c2.MetTimeOfDay = Math.Clamp(value.GetInt32(), 0, 3); break;
            case "obedience" when pk is IObedienceLevel ob: ob.ObedienceLevel = (byte)Math.Clamp(value.GetInt32(), 0, 100); break;
            case "received" when pk is PB7 pb7 && DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date):
                pb7.ReceivedYear = (byte)Math.Clamp(date.Year - 2000, 0, 255);
                pb7.ReceivedMonth = (byte)date.Month;
                pb7.ReceivedDay = (byte)date.Day;
                pb7.ReceivedHour = (byte)date.Hour;
                pb7.ReceivedMinute = (byte)date.Minute;
                pb7.ReceivedSecond = (byte)date.Second;
                break;
            case "hpType":
            {
                // StatEditor: the IVs change to give the chosen Hidden Power type.
                Span<int> ivs = stackalloc int[6];
                pk.GetIVs(ivs);
                if (Program.Settings.EntityEditor.HiddenPowerOnChangeMaxPower)
                    ivs.Fill(pk.MaxIV);
                HiddenPower.SetIVs(Math.Clamp(value.GetInt32(), 0, HiddenPower.TypeCount - 1), ivs, pk.Context);
                pk.SetIVs(ivs);
                break;
            }
            case "teraOriginal" when pk is ITeraType t: t.TeraTypeOriginal = (MoveType)value.GetInt32(); break;
            case "teraOverride" when pk is ITeraType t: t.TeraTypeOverride = (MoveType)value.GetInt32(); break;
            case "alpha" when pk is IAlpha alpha: alpha.IsAlpha = value.GetBoolean(); break;
            case "noble" when pk is INoble noble: noble.IsNoble = value.GetBoolean(); break;
            case "dynamaxLevel" when pk is IDynamaxLevel dmax: dmax.DynamaxLevel = (byte)Math.Clamp(value.GetInt32(), 0, 10); break;
            case "gigantamax" when pk is IGigantamax gmax: gmax.CanGigantamax = value.GetBoolean(); break;
            case "alphaMove" when pk is PA8 pa8: pa8.AlphaMove = (ushort)Math.Clamp(value.GetInt32(), 0, pk.MaxMoveID); break;
            case "marks": SetMarkings(pk, value); break;
            case "favorite" when pk is IFavorite fav: fav.IsFavorite = value.GetBoolean(); break;
            case "height" when pk is IScaledSize ss:
                ss.HeightScalar = (byte)Math.Clamp(value.GetInt32(), 0, 255);
                if (pk is PA8 h8) // SizeCP: height is copied to scale
                    h8.Scale = ss.HeightScalar;
                RecalculateSize(pk);
                break;
            case "weight" when pk is IScaledSize ss:
                ss.WeightScalar = (byte)Math.Clamp(value.GetInt32(), 0, 255);
                RecalculateSize(pk);
                break;
            case "scale" when pk is IScaledSize3 s3:
                s3.Scale = (byte)Math.Clamp(value.GetInt32(), 0, 255);
                if (pk is PA8 { } s8)
                    s8.HeightScalar = s3.Scale;
                break;
            case "cp" when pk is ICombatPower cp: cp.Stat_CP = Math.Clamp(value.GetInt32(), 0, 65535); break;
            case "contest.cool" when pk is IContestStats cs: cs.ContestCool = ToByte(value); break;
            case "contest.beauty" when pk is IContestStats cs: cs.ContestBeauty = ToByte(value); break;
            case "contest.cute" when pk is IContestStats cs: cs.ContestCute = ToByte(value); break;
            case "contest.smart" when pk is IContestStats cs: cs.ContestSmart = ToByte(value); break;
            case "contest.tough" when pk is IContestStats cs: cs.ContestTough = ToByte(value); break;
            case "contest.sheen" when pk is IContestStats cs: cs.ContestSheen = ToByte(value); break;
            case "shinyLeaf" when pk is G4PKM g4: g4.ShinyLeaf = value.GetInt32() & 0b11_1111; break;
            case "walkingMood" when pk is G4PKM g4: g4.WalkingMood = (sbyte)Math.Clamp(value.GetInt32(), -127, 127); break;
            case "nsparkle" when pk is PK5 pk5: pk5.NSparkle = value.GetBoolean(); break;
            case "pokeStarFame" when pk is PK5 pk5: pk5.PokeStarFame = ToByte(value); break;
            case "spirit" when pk is PB7 pb7: pb7.Spirit = ToByte(value); break;
            case "mood" when pk is PB7 pb7: pb7.Mood = ToByte(value); break;
            case "shadow.id" when pk is IShadowCapture sc: sc.ShadowID = (ushort)Math.Clamp(value.GetInt32(), 0, 127); break;
            case "shadow.purification" when pk is IShadowCapture { ShadowID: > 0 } sc: sc.Purification = Math.Max(-100, value.GetInt32()); break;
            case "currentHandler": pk.CurrentHandler = (byte)(value.GetInt32() & 1); break;
            case "htLanguage" when pk is IHandlerLanguage hl: hl.HandlingTrainerLanguage = (byte)value.GetInt32(); break;
            case "country" when pk is IRegionOrigin ro:
                ro.Country = (byte)value.GetInt32();
                ro.Region = 0; // the sub-region list belongs to the country
                break;
            case "region" when pk is IRegionOrigin ro: ro.Region = (byte)value.GetInt32(); break;
            case "consoleRegion" when pk is IRegionOrigin ro: ro.ConsoleRegion = (byte)value.GetInt32(); break;
            case "homeTracker" when pk is IHomeTrack home: home.Tracker = Util.GetHexValue64(value.GetString() ?? string.Empty); break;
            default:
                if (key.StartsWith("extra.", StringComparison.Ordinal) && int.TryParse(key[6..], out var offset) && pk.ExtraBytes.Contains((ushort)offset))
                {
                    pk.Data[offset] = ToByte(value);
                    break;
                }
                throw new ArgumentException($"'{key}' can't be set for this Pokémon.");
        }
    }

    private static byte ToByte(JsonElement value) => (byte)Math.Clamp(value.GetInt32(), 0, 255);

    /// <summary> PKMEditor's Infected / Cured checkboxes: a cured Pokémon keeps its strain with 0 days left. </summary>
    private static void SetPokerusState(PKM pk, int state)
    {
        switch (state)
        {
            case 0:
                pk.PokerusStrain = 0;
                pk.PokerusDays = 0;
                break;
            case 1:
                if (pk.PokerusStrain == 0)
                    pk.PokerusStrain = 1;
                pk.PokerusDays = Math.Clamp(pk.PokerusDays, 1, Pokerus.GetMaxDuration(pk.PokerusStrain));
                break;
            default:
                if (pk.PokerusStrain == 0)
                    pk.PokerusStrain = 1;
                pk.PokerusDays = 0;
                break;
        }
    }

    /// <summary> FormArgumentEditor.SaveArgument </summary>
    private static void SetFormArgument(PKM pk, IFormArgument f, string key, int value)
    {
        var mode = FormArgumentUtil.GetType(pk.Species, pk.Form, pk.Context);
        var b = (byte)Math.Clamp(value, 0, 255);
        switch (mode)
        {
            case FormArgumentType.Named:
                f.FormArgument = (uint)Math.Clamp(value, 0, Math.Max(0, FormConverter.GetFormArgumentStrings(pk.Species).Length - 1));
                break;
            case FormArgumentType.Raw:
                f.FormArgument = (uint)Math.Clamp(value, 0, (int)FormArgumentUtil.GetFormArgumentMaxEdge(pk.Species, pk.Form, pk.Context));
                break;
            case FormArgumentType.Triple:
                if (key == "formArg.max") f.FormArgumentMaximum = b;
                else if (key == "formArg.elapsed") f.FormArgumentElapsed = b;
                else f.FormArgumentRemain = b;
                break;
            case FormArgumentType.TripleParty:
                if (key == "formArg.remain")
                {
                    f.FormArgumentRemain = b;
                }
                else // only Furfrou keeps a streak; it is stored as both maximum and elapsed
                {
                    var streak = pk.Species == (ushort)Species.Furfrou ? b : (byte)0;
                    f.FormArgumentMaximum = streak;
                    f.FormArgumentElapsed = streak;
                }
                break;
        }
    }

    private static void SetMarkings(PKM pk, JsonElement value)
    {
        var marks = value.EnumerateArray().Select(z => z.GetInt32()).ToArray();
        if (pk is IAppliedMarkings<MarkingColor> c)
        {
            for (int i = 0; i < Math.Min(c.MarkingCount, marks.Length); i++)
                c.SetMarking(i, (MarkingColor)Math.Clamp(marks[i], 0, 2));
        }
        else if (pk is IAppliedMarkings<bool> b)
        {
            for (int i = 0; i < Math.Min(b.MarkingCount, marks.Length); i++)
                b.SetMarking(i, marks[i] != 0);
        }
    }

    /// <summary> SizeCP with "auto" on: LGPE's absolute height/weight follow the scalars. </summary>
    private static void RecalculateSize(PKM pk)
    {
        if (pk is not IScaledSizeValue sv)
            return;
        sv.ResetHeight();
        sv.ResetWeight();
    }

    /// <summary> Suggest buttons next to some fields (catch rate, obedience level, CP). </summary>
    private string? SuggestField(PKM pk, string key)
    {
        switch (key)
        {
            case "catchRate" when pk is PK1 pk1:
                pk1.CatchRate = (byte)CatchRateApplicator.GetSuggestedCatchRate(pk1, Sav);
                return null;
            case "obedience" when pk is IObedienceLevel ob:
                ob.ObedienceLevel = ob.GetSuggestedObedienceLevel(pk, pk.MetLevel);
                return null;
            case "cp" when pk is ICombatPower cp:
                cp.ResetCP();
                return null;
            case "teraOriginal" when pk is ITeraType t:
            {
                // StatEditor.L_TeraTypeOriginal_Click: SV natives alternate between the species' types, others get HOME's pick.
                var pi = pk.PersonalInfo;
                t.TeraTypeOriginal = !pk.SV
                    ? TeraTypeUtil.GetTeraTypeImport(pi.Type1, pi.Type2)
                    : (MoveType)((byte)t.TeraTypeOriginal == pi.Type1 ? pi.Type2 : pi.Type1);
                t.TeraTypeOverride = (MoveType)TeraTypeUtil.OverrideNone;
                return null;
            }
            case "teraOverride" when pk is ITeraType t:
                // StatEditor.L_TeraTypeOverride_Click
                t.TeraTypeOverride = pk.SV ? (MoveType)TeraTypeUtil.OverrideNone : t.TeraTypeOriginal;
                return null;
            default:
                return "No suggestion is available for that.";
        }
    }
}
