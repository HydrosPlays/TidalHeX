using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PKHeX.Core;
using PKHeX.Core.Searching;
using static PKHeX.Core.MessageStrings;

namespace PKHeX.WinForms.Tidal.Web;

// enc.* and gift.*: the search engines of EncounterBrowser / MysteryGiftBrowser (SAV_Encounters / SAV_MysteryGiftDB).
internal sealed partial class WebApi
{
    private static string TrainerPath => Settings.LocalResources.GetTrainerPath();

    private static readonly EncounterTypeGroup[] AllTypeGroups =
    [
        EncounterTypeGroup.Egg,
        EncounterTypeGroup.Mystery,
        EncounterTypeGroup.Static,
        EncounterTypeGroup.Trade,
        EncounterTypeGroup.Slot,
    ];

    private CancellationTokenSource? SearchTokenSource;
    private Task? SearchTask;
    private TrainerDatabase Trainers = new();
    private Task? TrainerTask;

    private void RegisterEncounters()
    {
        Bridge.RegisterAsync("enc.search", c => SearchEncounters(c.Get<EncounterSearchArgs>()));
        Bridge.Register("enc.load", c => LoadEncounter(c.Get<TokenArgs>().Value));
        Bridge.RegisterAsync("gift.all", _ => GetGifts());
        Bridge.Register("gift.load", c => LoadGift(c.Get<TokenArgs>().Value));
        Bridge.Register("gift.saveFile", c => SaveGiftFile(c.Get<TokenArgs>().Value));
    }

    private void CancelSearch() => SearchTokenSource?.Cancel();

    #region Trainers

    /// <summary>
    /// Trainer files (trainers folder) used as the OT of generated encounters, loaded in the background like Main.Menu_EncDatabase_Click.
    /// </summary>
    private void ResetTrainerDatabase()
    {
        var db = Trainers = new TrainerDatabase();
        var context = SAV.Context;
        TrainerTask = Task.Run(() =>
        {
            var dir = TrainerPath;
            if (!Directory.Exists(dir))
                return;
            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories);
            var pk = BoxUtil.GetPKMsFromPaths(files, context);
            foreach (var f in pk)
                db.RegisterCopy(f);
        });
    }

    private TrainerDatabase GetTrainers()
    {
        try
        {
            TrainerTask?.Wait(TimeSpan.FromSeconds(10)); // background file reads only; no UI dependency
        }
        catch
        {
            // Unreadable trainer files: fall back to the save's trainer.
        }
        return Trainers;
    }

    #endregion

    #region Encounter search

    private async Task<object?> SearchEncounters(EncounterSearchArgs args)
    {
        // One search at a time: the generator's type priority list is global.
        CancelSearch();
        if (SearchTask is { } previous)
        {
            try { await previous.ConfigureAwait(true); }
            catch { /* cancelled or failed; superseded anyway */ }
        }

        var sav = SAV;
        var types = ParseTypes(args.Types);
        var settings = GetSearchSettings(sav, args);
        if (!IsSearchAllowed(settings, types))
        {
            Session.SetEncounters([], -1);
            return Array.Empty<EncounterResult>();
        }

        var cts = SearchTokenSource = new CancellationTokenSource();
        var token = cts.Token;
        var filterUnavailable = Settings.EncounterDb.FilterUnavailableSpecies;
        var strings = GameInfo.Strings;

        EncounterMovesetGenerator.PriorityList = types;
        var listId = Session.NextListId();
        var task = Task.Run(() =>
        {
            var results = SearchDatabase(sav, settings, filterUnavailable, token).ToList();
            token.ThrowIfCancellationRequested();
            var dtos = new List<EncounterResult>(results.Count);
            for (int i = 0; i < results.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                dtos.Add(GetEncounterResult(results[i], WebSession.GetEncounterToken(listId, i), strings));
            }
            return (results, dtos);
        }, token);
        SearchTask = task;

        try
        {
            var (results, dtos) = await task.ConfigureAwait(true);
            if (token.IsCancellationRequested || !ReferenceEquals(sav, SAV))
                throw new OperationCanceledException(token);
            Session.SetEncounters(results, listId);
            return dtos;
        }
        finally
        {
            if (ReferenceEquals(SearchTask, task))
            {
                EncounterMovesetGenerator.ResetFilters();
                SearchTask = null;
            }
        }
    }

    private static EncounterTypeGroup[] ParseTypes(string[]? types)
    {
        if (types is null)
            return AllTypeGroups;

        var result = new HashSet<EncounterTypeGroup>();
        foreach (var type in types)
        {
            var group = type?.Trim().ToLowerInvariant() switch
            {
                "slot" or "slots" or "wild" => EncounterTypeGroup.Slot,
                "static" or "statics" => EncounterTypeGroup.Static,
                "trade" or "trades" => EncounterTypeGroup.Trade,
                "egg" or "eggs" => EncounterTypeGroup.Egg,
                "mystery" or "gift" or "gifts" or "event" or "events" or "mysterygift" or "mysterygifts" => EncounterTypeGroup.Mystery,
                _ => EncounterTypeGroup.None,
            };
            if (group != EncounterTypeGroup.None)
                result.Add(group);
        }
        return [.. result.OrderBy(z => z)];
    }

    private static SearchSettings GetSearchSettings(SaveFile sav, EncounterSearchArgs args)
    {
        var settings = new SearchSettings
        {
            Context = sav.Context,
            Generation = sav.Generation,
            Species = (ushort)Math.Clamp(args.Species, 0, ushort.MaxValue),
            Version = (GameVersion)Math.Clamp(args.Version, 0, byte.MaxValue),
            SearchEgg = args.Egg,
            SearchShiny = args.Shiny,
        };
        foreach (var move in args.Moves ?? [])
        {
            if (move is > 0 and <= ushort.MaxValue)
                settings.AddMove((ushort)move);
        }
        return settings;
    }

    private static bool IsSearchAllowed(SearchSettings settings, EncounterTypeGroup[] types)
    {
        if (types.Length == 0)
            return false;
        if (settings is { Species: 0, Moves.Count: 0 } && Settings.EncounterDb.ReturnNoneIfEmptySearch)
            return false;
        return true;
    }

    private static IEnumerable<IEncounterInfo> SearchDatabase(SaveFile sav, SearchSettings settings, bool filterUnavailable, CancellationToken token)
    {
        var pk = sav.BlankPKM;

        var moves = settings.Moves.ToArray();
        var versions = settings.GetVersions(sav);
        var species = settings.Species == 0 ? GetFullRange(sav.MaxSpeciesID) : [settings.Species];
        var results = GetAllSpeciesFormEncounters(sav, species, sav.Personal, versions, moves, pk, filterUnavailable, token);
        if (settings.SearchEgg is { } egg)
            results = results.Where(z => z.IsEgg == egg);
        if (settings.SearchShiny is { } shiny)
            results = results.Where(z => z.IsShiny == shiny);

        results = results.Distinct(new ReferenceComparer<IEncounterInfo>());

        if (filterUnavailable)
        {
            var filter = EntityPresenceFilters.GetFilterGeneric<IEncounterInfo>(sav.Context);
            if (filter != null)
                results = results.Where(filter);
        }
        return results;
    }

    private static IEnumerable<IEncounterInfo> GetAllSpeciesFormEncounters(SaveFile sav, IEnumerable<ushort> species, IPersonalTable pt,
        ReadOnlyMemory<GameVersion> versions, ReadOnlyMemory<ushort> moves, PKM pk, bool filterUnavailable, CancellationToken token)
    {
        foreach (var s in species)
        {
            if (token.IsCancellationRequested)
                break;

            var pi = pt.GetFormEntry(s, 0);
            var fc = pi.FormCount;
            if (fc == 0 && !filterUnavailable)
            {
                pi = PersonalTable.USUM.GetFormEntry(s, 0);
                fc = pi.FormCount;
            }
            for (byte f = 0; f < fc; f++)
            {
                if (FormInfo.IsBattleOnlyForm(s, f, pk.Format))
                    continue;
                pk.Species = s;
                pk.Form = f;
                pk.SetGender(pk.GetSaneGender());
                EncounterMovesetGenerator.OptimizeCriteria(pk, sav);
                foreach (var enc in EncounterMovesetGenerator.GenerateEncounters(pk, moves, versions))
                    yield return enc;
            }
        }
    }

    private static IEnumerable<ushort> GetFullRange(int max)
    {
        for (ushort i = 1; i <= max; i++)
            yield return i;
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private static EncounterResult GetEncounterResult(IEncounterInfo enc, string token, GameStrings strings)
    {
        var (badge, _) = EncounterBrowser.GetTypeBadge(enc);
        return new EncounterResult
        {
            Token = token,
            Species = enc.Species,
            Form = enc.Form,
            Name = GetSpeciesDisplay(enc, strings),
            Version = (int)enc.Version,
            VersionName = GetVersionName(enc.Version, strings),
            Location = enc.GetEncounterLocation() ?? string.Empty,
            LevelMin = enc.LevelMin,
            LevelMax = enc.LevelMax,
            Type = GetTypeGroup(enc).ToString().ToLowerInvariant(),
            TypeLabel = badge,
            Generation = enc.Generation,
            Shiny = enc.Shiny == Shiny.Never ? "never" : enc.IsShiny ? "always" : "random",
            Ball = (int)enc.FixedBall,
            Alpha = enc is IAlphaReadOnly { IsAlpha: true },
            Gmax = enc is IGigantamaxReadOnly { CanGigantamax: true },
            Facts = GetFacts(enc, strings),
            Games = GetGames(enc, strings),
            Source = IsHomeGift(enc) ? HomeName : string.Empty,
        };
    }

    private const string HomeName = "Pokémon HOME";

    /// <summary>
    /// Gifts claimed in Pokémon HOME rather than in a game. PKHeX numbers these cards 9000 and up (see WC8.IsHOMEGift).
    /// </summary>
    private static bool IsHomeGift(IEncounterTemplate enc)
        => enc is MysteryGift { CardID: >= 9000 } and (WC8 or WB8 or WA8 or WC9 or WA9 or WB7);

    /// <summary>
    /// The individual games an encounter or gift is from. PKHeX often reports a group (USUM, RBY, Gen7...), which is
    /// expanded to its games; gift types already fold their version restrictions into <see cref="IVersion.Version"/>.
    /// </summary>
    private static List<GameDto> GetGames(IEncounterTemplate enc, GameStrings strings)
    {
        // Z-A cards without an origin game report Scarlet/Violet (WA9.Version), but they are received in Z-A.
        var version = enc is WA9 { OriginGame: 0 } ? GameVersion.ZA : enc.Version;
        var games = new List<GameVersion>();
        if (IsHomeGift(enc))
            version = GameVersion.Any; // HOME records the pair's first game (Sword, Brilliant Diamond, Scarlet) as origin; it can go to either
        if (version.IsValidSavedVersion())
            games.Add(version);
        else if (version != GameVersion.Any)
            games.AddRange(GameUtil.GameVersions.Where(v => version.Contains(v)));
        if (games.Count == 0) // "any game": every game of the encounter's generation
            games.AddRange(GameUtil.GameVersions.Where(v => v != GameVersion.GO && v.Context == enc.Context));

        return games
            .OrderBy(v => v.Generation)
            .ThenBy(GameOrder)
            .Select(v => new GameDto((int)v, GetVersionName(v, strings)))
            .ToList();

        // Release order, pairs the way players say them (Ruby/Sapphire, Black/White, Omega Ruby/Alpha Sapphire).
        static double GameOrder(GameVersion v) => v switch
        {
            GameVersion.R => 0.5,
            GameVersion.HG or GameVersion.SS => (int)v + 10, // after Platinum
            GameVersion.B => 19.5,
            GameVersion.B2 => 21.5,
            GameVersion.OR => 25.5,
            _ => (int)v,
        };
    }

    /// <summary> What's fixed about an encounter, beyond what the card already shows (location, game, level, type). </summary>
    private static List<FactDto> GetFacts(IEncounterInfo enc, GameStrings strings)
    {
        var facts = new List<FactDto>();
        try
        {
            if (enc is IMoveset { Moves.HasMoves: true } m)
                facts.Add(new("Moves", m.Moves.GetMovesetLine(strings.movelist)));
            if (enc is IFixedBall { FixedBall: not Ball.None and var ball } && (int)ball < strings.balllist.Length)
                facts.Add(new("Ball", strings.balllist[(int)ball]));
            if (enc is IFixedNature { Nature: < Nature.Random and var nature })
                facts.Add(new("Nature", strings.natures[(int)nature]));
            if (enc is IFixedGender { IsFixedGender: true } g)
                facts.Add(new("Gender", g.Gender switch { 0 => "Male ♂", 1 => "Female ♀", _ => "Genderless" }));
            if (enc.Generation >= 3 && enc is IFixedAbilityNumber { Ability: not AbilityPermission.Any12 and var ability })
                facts.Add(new("Ability", GetAbilityText(enc, ability, strings)));
            if (enc is IFixedIVSet { IVs.IsSpecified: true } fixedIVs)
                facts.Add(new("IVs", GetIVText(fixedIVs.IVs)));
            else if (enc is IFlawlessIVCount { FlawlessIVCount: > 0 and var flawless })
                facts.Add(new("IVs", $"At least {flawless} perfect"));
            if (enc is IFixedTrainer { IsFixedTrainer: true })
                facts.Add(new("Trainer", "Fixed in-game OT"));
            if (enc is IFixedNickname { IsFixedNickname: true })
                facts.Add(new("Nickname", "Fixed"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Tidal: encounter facts failed for {enc}: {ex.Message}");
        }
        return facts;
    }

    private static string GetAbilityText(IEncounterTemplate enc, AbilityPermission ability, GameStrings strings)
    {
        if (ability == AbilityPermission.Any12H)
            return "Any, including hidden";
        int index = ability.GetSingleValue();
        try
        {
            var pi = GameData.GetPersonal(enc.Version).GetFormEntry(enc.Species, enc.Form);
            var id = pi.GetAbilityAtIndex(index);
            if (id > 0 && id < strings.abilitylist.Length)
                return index == 2 ? $"{strings.abilitylist[id]} (hidden)" : strings.abilitylist[id];
        }
        catch (Exception)
        {
            // Unknown game/personal data: fall back to the slot description.
        }
        return index == 2 ? "Hidden ability" : $"Ability {index + 1}";
    }

    private static string GetIVText(IndividualValueSet iv)
    {
        static string V(sbyte v) => v < 0 ? "–" : v.ToString(CultureInfo.InvariantCulture);
        return $"{V(iv.HP)} / {V(iv.ATK)} / {V(iv.DEF)} / {V(iv.SPA)} / {V(iv.SPD)} / {V(iv.SPE)}";
    }

    /// <summary> Encounter type group, matching the search filter names. </summary>
    private static EncounterTypeGroup GetTypeGroup(IEncounterTemplate enc)
    {
        if (enc is MysteryGift)
            return EncounterTypeGroup.Mystery;
        if (enc is IEncounterEgg)
            return EncounterTypeGroup.Egg;
        var type = enc.GetType().Name;
        if (type.Contains("Trade", StringComparison.Ordinal))
            return EncounterTypeGroup.Trade;
        if (type.Contains("Slot", StringComparison.Ordinal))
            return EncounterTypeGroup.Slot;
        return EncounterTypeGroup.Static;
    }

    private static string GetSpeciesDisplay(IEncounterTemplate enc, GameStrings strings)
    {
        var name = enc.Species < strings.Species.Count ? strings.Species[enc.Species] : enc.Species.ToString();
        if (enc.Form == 0)
            return name;
        var form = FormConverter.GetStringFromForm(enc.Species, enc.Form, strings, enc.Context);
        return string.IsNullOrWhiteSpace(form) ? name : $"{name}-{form}";
    }

    private static string GetVersionName(GameVersion version, GameStrings strings) => version.IsValidSavedVersion() && (int)version < strings.gamelist.Length
        ? strings.gamelist[(int)version]
        : version.ToString();

    #endregion

    #region Encounter load

    private EditorState LoadEncounter(string token)
    {
        if (Session.GetEncounter(token) is not { } enc)
            throw new ArgumentException("Unknown encounter; search again.");

        var pk = CreateFromEncounter(enc);
        LoadEditor(pk);
        return GetEditorState();
    }

    /// <summary>
    /// EncounterBrowser.TryCreate: generate with a matching trainer, convert and adapt to the save.
    /// </summary>
    /// <remarks>
    /// TidalHeX always generates the encounter as the database has it (random nature, IVs, shininess within its rules).
    /// PKHeX's "Use tabs as criteria" copied the editor's Pokémon (shiny, nature, IVs, ...) into it; that setting now
    /// only affects the classic Encounter Database window.
    /// </remarks>
    private PKM CreateFromEncounter(IEncounterInfo enc)
    {
        var sav = SAV;
        var trainer = GetTrainers().GetTrainer(enc.Version, enc.Generation <= 2 ? (LanguageID)sav.Language : null) ?? sav;
        var temp = enc.ConvertToPKM(trainer, EncounterCriteria.Unrestricted);
        var converted = EntityConverter.ConvertToType(temp, sav.PKMType, out var c);
        if (converted is null)
            throw new InvalidOperationException(c.GetDisplayString(temp, sav.PKMType));
        sav.AdaptToSaveFile(converted);
        converted.RefreshChecksum();
        return converted;
    }

    #endregion

    #region Gifts

    private async Task<object?> GetGifts()
    {
        if (Session.GiftResults is { } cached)
            return cached;

        var sav = SAV;
        var context = sav.Context;
        var generation = sav.Generation;
        var filterUnavailable = Settings.MysteryDb.FilterUnavailableSpecies;
        var lang = GameInfo.CurrentLanguage;
        var strings = GameInfo.Strings;

        var listId = Session.NextListId();
        var (gifts, dtos) = await Task.Run(() =>
        {
            var list = LoadGiftDatabase(context, generation, filterUnavailable);
            var results = new List<GiftResult>(list.Count);
            for (int i = 0; i < list.Count; i++)
                results.Add(GetGiftResult(list[i], WebSession.GetGiftToken(listId, i), strings, lang));
            return (list, results);
        }).ConfigureAwait(true);

        if (!ReferenceEquals(sav, SAV))
            throw new OperationCanceledException("The save file changed while loading gifts.");
        Session.SetGifts(gifts, dtos, listId);
        return dtos;
    }

    /// <summary> MysteryGiftBrowser.LoadDatabase </summary>
    private static List<MysteryGift> LoadGiftDatabase(EntityContext context, byte generation, bool filterUnavailable)
    {
        var db = EncounterEvent.GetAllEvents();
        if (filterUnavailable)
        {
            var filter = EntityPresenceFilters.GetFilterGift<MysteryGift>(context, generation);
            if (filter != null)
                db = db.Where(filter);
        }
        var list = db.ToList();
        foreach (var mg in list)
            mg.GiftUsed = false;
        return list;
    }

    private static string CleanTitle(MysteryGift g) => g.CardTitle.Replace('　', ' ').Trim();

    private static GiftResult GetGiftResult(MysteryGift g, string token, GameStrings strings, string lang)
    {
        var title = CleanTitle(g);
        string name;
        if (g.IsEntity && g.Species != 0)
            name = GetSpeciesDisplay(g, strings); // with the form, like the encounter list
        else if (g.IsItem && (uint)g.ItemID < strings.itemlist.Length)
            name = strings.itemlist[g.ItemID];
        else
            name = title.Length != 0 ? title : g.Type;

        var moves = g.Moves;
        return new GiftResult
        {
            Token = token,
            Species = g.Species,
            Form = g.Form,
            Name = name,
            Title = title,
            CardId = g.CardID,
            Type = g.Type,
            Generation = g.Generation,
            Level = g.Level,
            Shiny = g.IsShiny,
            Egg = g.IsEgg,
            Item = g.IsItem ? GetItemName(g.ItemID, strings) : string.Empty,
            Ot = g.OriginalTrainerName,
            FileName = g.FileName,
            IsItem = g.IsItem,
            HeldItem = GetItemName(g.HeldItem, strings),
            Moves = [.. new[] { moves.Move1, moves.Move2, moves.Move3, moves.Move4 }.Where(z => z != 0).Select(z => (int)z)],
            Details = GetDetails(g, lang),
            Games = GetGames(g, strings),
            Source = IsHomeGift(g) ? HomeName : string.Empty,
        };
    }

    /// <summary> PKHeX's card summary lines (card #, OT, level, moves...), minus the header line. </summary>
    private static string[] GetDetails(MysteryGift g, string lang)
    {
        try { return [.. g.GetTextLines(false, lang).Skip(1)]; }
        catch { return []; }
    }

    private MysteryGift GetGiftOrThrow(string token) => Session.GetGift(token) ?? throw new ArgumentException("Unknown gift; reload the gift list.");

    /// <summary> MysteryGiftBrowser.LoadSelected </summary>
    private EditorState LoadGift(string token)
    {
        var gift = GetGiftOrThrow(token);
        if (!gift.IsEntity)
            throw new InvalidOperationException(MsgPKMMysteryGiftFail);

        var sav = SAV;
        var temp = gift.ConvertToPKM(sav, EncounterCriteria.Unrestricted);
        var pk = EntityConverter.ConvertToType(temp, sav.PKMType, out var c);
        if (pk is null)
            throw new InvalidOperationException(c.GetDisplayString(temp, sav.PKMType));
        sav.AdaptToSaveFile(pk);
        pk.RefreshChecksum();
        LoadEditor(pk);
        return GetEditorState();
    }

    /// <summary> MysteryGiftBrowser.SaveGift </summary>
    private bool SaveGiftFile(string token)
    {
        var gift = GetGiftOrThrow(token);
        if (gift is not DataMysteryGift g) // e.g. WC3
        {
            WinFormsUtil.Alert(MsgExportWC3DataFail);
            return false;
        }
        return WinFormsUtil.ExportMGDialog(g);
    }

    #endregion
}
