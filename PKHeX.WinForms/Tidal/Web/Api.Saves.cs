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
            Entry = z.Entry,
            Version = (int)z.Version,
            Game = z.Note is null ? GameInfo.GetVersionName(z.Version) : "Colosseum / XD",
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
            Icons = GetGameIcons(z.Version).Select(n => $"img/games/pokemon-{n}.png").ToList(),
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
        if (entry is null)
            return OpenTracked(path);

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

    /// <summary> "switch" → "Nintendo Switch"; nested folders keep their path ("Nintendo Switch › Scarlet"). </summary>
    private static string GetGroupName(string key)
    {
        if (key.Length == 0)
            return "Saves";
        var parts = key.Split('/');
        parts[0] = PlatformNames.GetValueOrDefault(parts[0], parts[0]);
        return string.Join(" › ", parts);
    }

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

    /// <summary> Box art in wwwroot/img/games (pokemon-{name}.png); saves that can't tell a pair apart get both. </summary>
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
        _ => [], // no box art (e.g. Moon, Colosseum, XD, Stadium): the page draws a placeholder
    };

    /// <summary> Order within a generation (GameVersion values aren't in release order for older games). </summary>
    private static int GetReleaseOrder(GameVersion version) => version switch
    {
        RD or GN or RB => 0, BU => 1, YW or RBY => 2,
        GD or GS => 0, SI => 1, C or GSC => 2,
        R or RS => 0, S => 1, E or RSE => 2, FR or FRLG => 3, LG => 4, COLO or CXD => 5, XD => 6, RSBOX => 7,
        D or DP => 0, P => 1, Pt or DPPt => 2, HG or HGSS => 3, SS => 4, BATREV => 5,
        _ => (int)version,
    };
}
