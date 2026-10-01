using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;
using static PKHeX.Core.GameVersion;

namespace PKHeX.WinForms.Tidal.Web;

// saves.*: the Save Manager (saves kept in the "saves" folder next to the exe).
internal sealed partial class WebApi
{
    private readonly SaveLibrary Library = new();
    private LibraryScan? LastScan;

    /// <summary> While the Save Manager opens a save: names that may tell a Gen 1-3 save's game and language. </summary>
    private IReadOnlyList<string>? LibraryHints;

    private void RegisterSaves()
    {
        Bridge.RegisterAsync("saves.list", c => ListLibrary(c.Get<LibraryArgs>()));
        Bridge.Register("saves.open", c => OpenFromLibrary(c.Get<LibraryArgs>().Id ?? string.Empty));
        Bridge.Register("saves.openFolder", _ => OpenLibraryFolder());
    }

    /// <summary> Startup: read the folder in the background, so the Save Manager opens with the list ready. </summary>
    public void StartLibraryScan() => Library.Scan().ContinueWith(t => LastScan = t.Result, TaskContinuationOptions.OnlyOnRanToCompletion);

    private async Task<object?> ListLibrary(LibraryArgs args)
    {
        if (args.Refresh || LastScan is null)
            LastScan = await Library.Scan().ConfigureAwait(true);
        return GetLibrary(LastScan);
    }

    private LibraryResult GetLibrary(LibraryScan scan)
    {
        var current = SAV.Metadata.FilePath;
        var groups = scan.Items
            .GroupBy(z => z.Group, StringComparer.OrdinalIgnoreCase)
            .Select(g => new LibraryGroup
            {
                Key = g.Key,
                Name = GetGroupName(g.Key),
                Saves = g.OrderBy(z => z.Generation).ThenBy(z => GetReleaseOrder(z.Version)).ThenBy(z => z.FileName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(z => ToLibrarySave(z, current))
                    .ToList(),
            })
            // Oldest games first: gb, gbc, gba, ds, 3ds, switch folders line up by console.
            .OrderBy(g => g.Saves.Min(z => z.Generation))
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new LibraryResult { Folder = SaveLibrary.Root, Groups = groups, Skipped = scan.Skipped };
    }

    private static LibrarySave ToLibrarySave(LibraryItem z, string? current)
    {
        var path = z.Entry is null ? z.FullPath : SaveLibrary.GetArchivedPath(z.FullPath, z.Entry);
        return new LibrarySave
        {
            Id = z.Id,
            FileName = z.FileName,
            Folder = z.SubFolder,
            Entry = z.Entry,
            Version = (int)z.Version,
            Game = z.Note is null ? GetGameName(z.Version, z.Language) : "Colosseum / XD",
            Generation = z.Generation,
            Ot = z.Ot,
            Tid = z.Tid,
            Sid = Settings.Privacy.HideSecretDetails ? string.Empty : z.Sid,
            PlayTime = z.PlayTime,
            Language = GetLanguageCode(z.Language),
            LanguageName = GetLanguageName(z.Language),
            Started = z.Started?.ToString("yyyy-MM-dd") ?? string.Empty,
            Gender = z.Gender,
            Money = z.Money,
            DexCaught = z.DexCaught,
            Size = z.Size,
            Modified = z.Modified.ToString("s"),
            Icons = GetGameIcons(z.Version, z.Language).Select(n => $"img/games/pokemon-{n}.png").ToList(),
            Party = z.Party,
            Loaded = string.Equals(path, current, StringComparison.OrdinalIgnoreCase),
            Note = z.Note,
        };
    }

    /// <summary>
    /// Opens a save from the library. Plain files load like any opened file; a save inside a .zip loads from memory and
    /// the .zip is never written (export asks where to save, and it stays out of the recent files).
    /// </summary>
    private SaveSummary? OpenFromLibrary(string id)
    {
        if (!SaveLibrary.TryResolve(id, out var path, out var entry))
        {
            Warn("That save isn't in the saves folder anymore. Refresh the list to see what's there now.");
            return null;
        }

        LibraryHints = SaveLibrary.GetHintNames(id);
        try
        {
            return entry is null ? OpenTracked(path) : OpenArchived(path, entry);
        }
        finally
        {
            LibraryHints = null;
        }
    }

    private SaveSummary? OpenArchived(string path, string entry)
    {
        var before = Session.Revision;
        var virtualPath = SaveLibrary.GetArchivedPath(path, entry);
        try
        {
            var data = SaveLibrary.ReadArchivedSave(path, entry);
            if (!SaveLibrary.TryRead(data, virtualPath, out var sav, out _))
            {
                WinFormsUtil.Error(MessageStrings.MsgFileUnsupported, virtualPath);
                return null;
            }
            OpenSAV(sav, virtualPath, archived: true);
        }
        catch (Exception e)
        {
            WinFormsUtil.Error(MessageStrings.MsgFileLoadFail + "\nPath: " + virtualPath, e);
            return null;
        }
        return Session.Revision != before ? GetSaveSummary() : null;
    }

    private static bool OpenLibraryFolder()
    {
        var root = SaveLibrary.Root;
        try
        {
            Directory.CreateDirectory(root);
            Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
            return true;
        }
        catch (Exception e)
        {
            WinFormsUtil.Error($"Couldn't open {root}", e);
            return false;
        }
    }

    /// <summary> Short language tag for the card's badge. </summary>
    private static string GetLanguageCode(int language) => (LanguageID)language switch
    {
        LanguageID.Japanese => "JPN",
        LanguageID.English => "ENG",
        LanguageID.French => "FRE",
        LanguageID.Italian => "ITA",
        LanguageID.German => "GER",
        LanguageID.Spanish => "SPA",
        LanguageID.SpanishL => "SPA-LA",
        LanguageID.Korean => "KOR",
        LanguageID.ChineseS => "CHS",
        LanguageID.ChineseT => "CHT",
        _ => string.Empty,
    };

    private static string GetLanguageName(int language) => (LanguageID)language switch
    {
        LanguageID.Japanese => "Japanese",
        LanguageID.English => "English",
        LanguageID.French => "French",
        LanguageID.Italian => "Italian",
        LanguageID.German => "German",
        LanguageID.Spanish => "Spanish (Spain)",
        LanguageID.SpanishL => "Spanish (Latin America)",
        LanguageID.Korean => "Korean",
        LanguageID.ChineseS => "Chinese (Simplified)",
        LanguageID.ChineseT => "Chinese (Traditional)",
        _ => string.Empty,
    };

    /// <summary> "switch" → "Nintendo Switch"; other folder names are shown as they are. </summary>
    private static string GetGroupName(string key) => key.Length == 0 ? "Saves" : PlatformNames.GetValueOrDefault(key, key);

    private static readonly Dictionary<string, string> PlatformNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gb"] = "Game Boy",
        ["gbc"] = "Game Boy Color",
        ["gba"] = "Game Boy Advance",
        ["gc"] = "GameCube",
        ["gcn"] = "GameCube",
        ["ngc"] = "GameCube",
        ["gamecube"] = "GameCube",
        ["ds"] = "Nintendo DS",
        ["nds"] = "Nintendo DS",
        ["3ds"] = "Nintendo 3DS",
        ["switch"] = "Nintendo Switch",
        ["ns"] = "Nintendo Switch",
        ["nx"] = "Nintendo Switch",
        ["switch2"] = "Nintendo Switch 2",
        ["n64"] = "Nintendo 64",
        ["wii"] = "Wii",
    };

    /// <summary>
    /// The game's name. Saves that can't tell a pair apart (international Red/Blue, Ruby/Sapphire...) report the pair,
    /// which PKHeX only names by its code ("RB"): spell out both games instead.
    /// </summary>
    internal static string GetGameName(GameVersion version, int language)
    {
        if (GamePairs.TryGetValue(version, out var games))
            return string.Join(" / ", games.Select(g => GetGameName(g, language)));

        var name = GameInfo.GetVersionName(version);
        // "Blue [INT]/Green [JP]": the same version, sold as Blue outside Japan and as Green in Japan.
        var regions = name.Split('/');
        if (regions.Length > 1 && name.Contains('['))
            name = regions.FirstOrDefault(r => r.Contains(language == (int)LanguageID.Japanese ? "[JP]" : "[INT]")) ?? regions[0];
        return TrimRegion(name);
    }

    /// <summary> "Blue [JP]" → "Blue" (the badge shows the language). </summary>
    private static string TrimRegion(string name)
    {
        var bracket = name.LastIndexOfAny(['[', '(']);
        return bracket > 0 && name.EndsWith(name[bracket] == '[' ? ']' : ')') ? name[..bracket].TrimEnd() : name;
    }

    private static readonly Dictionary<GameVersion, GameVersion[]> GamePairs = new()
    {
        [RB] = [RD, BU],
        [RBY] = [RD, BU, YW],
        [GS] = [GD, SI],
        [GSC] = [GD, SI, C],
        [RS] = [R, S],
        [RSE] = [R, S, E],
        [FRLG] = [FR, LG],
        [DP] = [D, P],
        [DPPt] = [D, P, Pt],
        [HGSS] = [HG, SS],
        [BW] = [B, W],
        [B2W2] = [B2, W2],
        [XY] = [X, Y],
        [ORAS] = [OR, AS],
        [SM] = [SN, MN],
        [USUM] = [US, UM],
        [GG] = [GP, GE],
        [SWSH] = [SW, SH],
        [BDSP] = [BD, SP],
        [SV] = [SL, VL],
        [CXD] = [COLO, XD],
    };

    /// <summary> Box art in wwwroot/img/games (pokemon-{name}.png); saves that can't tell a pair apart get both. </summary>
    private static string[] GetGameIcons(GameVersion version, int language) => version switch
    {
        GN when language != (int)LanguageID.Japanese => ["blue"], // international Blue shares Japanese Green's version
        _ => GetGameIcons(version),
    };

    private static string[] GetGameIcons(GameVersion version) => version switch
    {
        RD => ["red"],
        GN => ["green"],
        BU => ["blue"],
        YW => ["yellow"],
        RB or RBY => ["red", "blue"],
        GD => ["gold"],
        SI => ["silver"],
        C => ["crystal"],
        GS or GSC => ["gold", "silver"],
        R => ["ruby"],
        S => ["sapphire"],
        E => ["emerald"],
        RS or RSE => ["ruby", "sapphire"],
        FR => ["firered"],
        LG => ["leafgreen"],
        FRLG => ["firered", "leafgreen"],
        COLO => ["colosseum"],
        XD => ["xdgaleofdarkness"],
        CXD => ["colosseum", "xdgaleofdarkness"], // a memory card holding both
        RSBOX => ["boxrubysapphire"],
        Stadium or StadiumJ => ["stadium"],
        Stadium2 => ["stadium2"],
        D => ["diamond"],
        P => ["pearl"],
        Pt => ["platinum"],
        DP or DPPt => ["diamond", "pearl"],
        HG => ["heartgold"],
        SS => ["soulsilver"],
        HGSS => ["heartgold", "soulsilver"],
        B => ["black"],
        W => ["white"],
        BW => ["black", "white"],
        B2 => ["black2"],
        W2 => ["white2"],
        B2W2 => ["black2", "white2"],
        X => ["x"],
        Y => ["y"],
        XY => ["x", "y"],
        OR => ["omegaruby"],
        AS => ["alphasapphire"],
        ORAS or ORASDEMO => ["omegaruby", "alphasapphire"],
        SN => ["sun"],
        MN => ["moon"],
        SM => ["sun", "moon"],
        US => ["ultrasun"],
        UM => ["ultramoon"],
        USUM => ["ultrasun", "ultramoon"],
        GP => ["letsgopikachu"],
        GE => ["letsgoeevee"],
        GG => ["letsgopikachu", "letsgoeevee"],
        SW => ["sword"],
        SH => ["shield"],
        SWSH => ["sword", "shield"],
        BD => ["brilliantdiamond"],
        SP => ["shiningpearl"],
        BDSP => ["brilliantdiamond", "shiningpearl"],
        PLA => ["legendsarceus"],
        SL => ["scarlet"],
        VL => ["violet"],
        SV => ["scarlet", "violet"],
        ZA => ["legendsza"],
        GO => ["go"],
        _ => [], // no box art: the page draws a placeholder
    };

    /// <summary> Order within a generation (GameVersion values aren't in release order for older games). </summary>
    private static int GetReleaseOrder(GameVersion version) => version switch
    {
        RD or RB => 0, GN => 1, BU => 2, YW or RBY => 3, StadiumJ or Stadium => 4, // GN: Green, and Blue outside Japan
        GD or GS => 0, SI => 1, C or GSC => 2, Stadium2 => 3,
        R or RS => 0, S => 1, E or RSE => 2, FR or FRLG => 3, LG => 4, COLO or CXD => 5, XD => 6, RSBOX => 7,
        D or DP => 0, P => 1, Pt or DPPt => 2, HG or HGSS => 3, SS => 4, BATREV => 5,
        _ => (int)version,
    };
}
