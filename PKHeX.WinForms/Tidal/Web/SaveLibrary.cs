using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using PKHeX.Core;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// The Save Manager's library: saves kept in the "saves" folder next to the exe, as raw files or .zip archives
/// (e.g. JKSV backups), grouped by sub-folder. Scans run in the background and reuse what they read for unchanged files.
/// </summary>
internal sealed class SaveLibrary
{
    public const string FolderName = "saves";

    /// <summary> Separates a .zip file from the save inside it in an <see cref="LibraryItem.Id"/>. </summary>
    public const string EntrySeparator = "::";

    /// <summary> Stop listing a folder that holds far more files than any save collection would. </summary>
    private const int MaxFiles = 5000;

    public static string Root => Path.Combine(Program.WorkingDirectory, FolderName);

    private readonly Dictionary<string, CachedFile> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object Gate = new();
    private Task<LibraryScan>? Current;

    private sealed record CachedFile(long Length, DateTime Modified, List<LibraryItem> Items);

    /// <summary> Scans the folder (or joins the scan already running). </summary>
    public Task<LibraryScan> Scan()
    {
        lock (Gate)
        {
            if (Current is { IsCompleted: false } running)
                return running;
            return Current = Task.Run(ScanFolder);
        }
    }

    private LibraryScan ScanFolder()
    {
        var root = Root;
        var items = new List<LibraryItem>();
        var skipped = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
        {
            Cache.Clear();
            return new LibraryScan(items, skipped);
        }

        foreach (var path in EnumerateFiles(root).Take(MaxFiles))
        {
            seen.Add(path);
            FileInfo fi;
            try { fi = new FileInfo(path); }
            catch { continue; }

            if (!Cache.TryGetValue(path, out var cached) || cached.Length != fi.Length || cached.Modified != fi.LastWriteTimeUtc)
                Cache[path] = cached = new CachedFile(fi.Length, fi.LastWriteTimeUtc, ReadFile(fi, GetRelativePath(root, path)));

            if (cached.Items.Count == 0)
                skipped.Add(GetRelativePath(root, path));
            items.AddRange(cached.Items);
        }

        foreach (var gone in Cache.Keys.Where(k => !seen.Contains(k)).ToList())
            Cache.Remove(gone);
        return new LibraryScan(items, skipped);
    }

    /// <summary> Every file below the folder, skipping hidden ones (".nx_save_meta.bin", ".DS_Store", ".git"...). </summary>
    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        return Directory.EnumerateFiles(root, "*", options)
            .Where(p => !GetRelativePath(root, p).Split('/').Any(part => part.StartsWith('.')))
            .Order(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static List<LibraryItem> ReadFile(FileInfo fi, string relative)
    {
        try
        {
            if (fi.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return ReadArchive(fi, relative);
            if (!SaveUtil.IsSizeValid(fi.Length))
                return [];

            var data = File.ReadAllBytes(fi.FullName);
            if (TryRead(data, fi.FullName, out var sav, out var gameCount))
                return [Describe(sav, relative, fi, null)];
            if (gameCount > 1) // a memory card with several games: PKHeX asks which one when it opens
                return [new LibraryItem(relative, fi.FullName, null, fi.Name, fi.Length, fi.LastWriteTime) { Version = GameVersion.CXD, Generation = 3, Note = $"GameCube memory card · {gameCount} games" }];
        }
        catch
        {
            // Unreadable or not a save; listed as skipped.
        }
        return [];
    }

    private static List<LibraryItem> ReadArchive(FileInfo fi, string relative)
    {
        using var zip = ZipFile.OpenRead(fi.FullName);
        var found = new List<(ZipArchiveEntry Entry, SaveFile Sav)>();
        foreach (var entry in zip.Entries)
        {
            if (!IsCandidate(entry))
                continue;
            try
            {
                var data = ReadEntry(entry);
                if (TryRead(data, Path.Combine(fi.FullName, entry.FullName), out var sav, out _))
                    found.Add((entry, sav));
            }
            catch
            {
                // Skip entries that can't be read.
            }
        }

        // Switch backups hold the game's "main" save and its "backup" copy: list the main one.
        found.RemoveAll(f => IsName(f.Entry, "backup") && found.Any(o => IsName(o.Entry, "main") && GetFolder(o.Entry) == GetFolder(f.Entry)));
        return found.ConvertAll(f => Describe(f.Sav, relative + EntrySeparator + f.Entry.FullName, fi, f.Entry.FullName));

        static bool IsName(ZipArchiveEntry e, string name) => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        static string GetFolder(ZipArchiveEntry e) => e.FullName[..^e.Name.Length];
    }

    private static bool IsCandidate(ZipArchiveEntry entry)
        => entry.Name.Length != 0 && !entry.FullName.Split('/').Any(part => part.StartsWith('.')) && SaveUtil.IsSizeValid(entry.Length);

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        var data = new byte[entry.Length];
        using var stream = entry.Open();
        stream.ReadExactly(data);
        return data;
    }

    /// <summary> Detects a save like PKHeX's file loading does, including GameCube memory cards that hold one game. </summary>
    /// <param name="data">File contents.</param>
    /// <param name="path">Where the data came from (some formats look at the file name).</param>
    /// <param name="sav">Detected save.</param>
    /// <param name="gameCount">For memory cards: how many Pokémon games are on it.</param>
    public static bool TryRead(Memory<byte> data, string? path, [NotNullWhen(true)] out SaveFile? sav, out int gameCount)
    {
        gameCount = 0;
        if (SaveUtil.TryGetSaveFile(data, out sav, path))
            return true;
        if (!FileUtil.TryGetMemoryCard(data, out var card))
            return false;

        var game = card.GetMemoryCardState() switch
        {
            MemoryCardSaveStatus.SaveGameCOLO => SaveFileType.Colosseum,
            MemoryCardSaveStatus.SaveGameXD => SaveFileType.XD,
            MemoryCardSaveStatus.SaveGameRSBOX => SaveFileType.RSBox,
            _ => SaveFileType.None,
        };
        gameCount = card.SaveGameCount;
        if (game == SaveFileType.None)
            return false;
        card.SelectSaveGame(game);
        return SaveUtil.TryGetSaveFile(card, out sav);
    }

    private static LibraryItem Describe(SaveFile sav, string id, FileInfo fi, string? entry) => new(id, fi.FullName, entry, fi.Name, fi.Length, fi.LastWriteTime)
    {
        Version = sav.Version,
        Generation = sav.Generation,
        Ot = sav.OT,
        Tid = WebApi.FormatTrainerId(sav, secret: false),
        Sid = WebApi.FormatTrainerId(sav, secret: true),
        PlayTime = GetPlayTime(sav),
        Language = sav.Language,
        Started = GetStartDate(sav),
        Gender = HasTrainerGender(sav) ? sav.Gender : -1,
        Money = sav.Money,
        DexCaught = sav.HasPokeDex ? WebApi.GetDexCaught(sav) : -1,
        Party = GetParty(sav),
    };

    private static string GetPlayTime(SaveFile sav)
    {
        try { return $"{sav.PlayedHours}h {sav.PlayedMinutes:00}m {sav.PlayedSeconds:00}s"; }
        catch { return string.Empty; }
    }

    /// <summary> Gen 1 and Gold/Silver have no trainer gender (Crystal added the girl). </summary>
    private static bool HasTrainerGender(SaveFile sav) => sav.Generation >= 3 || sav.Version == GameVersion.C;

    /// <summary>
    /// When the adventure started, read like each game's Trainer Info editor does. Gen 1-3, Let's Go and Z-A don't store
    /// one (or PKHeX doesn't read it yet).
    /// </summary>
    private static DateTime? GetStartDate(SaveFile sav)
    {
        try
        {
            DateTime date;
            switch (sav)
            {
                case SAV8SWSH swsh:
                    var card = swsh.TrainerCard;
                    date = new DateTime(card.StartedYear, card.StartedMonth, card.StartedDay);
                    break;
                case SAV8LA la:
                    date = la.AdventureStart.Timestamp;
                    break;
                case SAV8BS bs:
                    date = bs.System.LocalTimestampStart;
                    break;
                case SAV9SV sv:
                    date = sv.EnrollmentDate.Timestamp;
                    break;
                case SAV9ZA:
                    return null;
                default:
                    if (sav.SecondsToStart == 0)
                        return null;
                    DateUtil.GetDateTime2000(sav.SecondsToStart, out date, out _);
                    break;
            }
            // Blank or garbage values: years the games can't have been played in.
            return date.Year >= 2000 && date <= DateTime.Now.AddYears(1) ? date.Date : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<LibraryMon> GetParty(SaveFile sav)
    {
        if (!sav.HasParty)
            return [];
        try
        {
            return sav.PartyData
                .Where(pk => pk.Species != 0)
                .Take(6)
                .Select(pk => new LibraryMon(pk.Species, pk.Form, pk.Gender, pk.IsShiny, pk.IsEgg))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Finds a listed save again. False if the id points outside the folder or the file is gone.
    /// </summary>
    /// <param name="id">The save's <see cref="LibraryItem.Id"/>.</param>
    /// <param name="path">Full path of the file (the .zip for archived saves).</param>
    /// <param name="entry">The save inside the .zip, or null for a plain file.</param>
    public static bool TryResolve(string id, [NotNullWhen(true)] out string? path, out string? entry)
    {
        path = null;
        entry = null;
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var split = id.IndexOf(EntrySeparator, StringComparison.Ordinal);
        var relative = split < 0 ? id : id[..split];
        entry = split < 0 ? null : id[(split + EntrySeparator.Length)..];

        var root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            return false;
        path = full;
        return true;
    }

    /// <summary> Reads one save out of a .zip. </summary>
    public static byte[] ReadArchivedSave(string zipPath, string entryName)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(entryName);
        if (entry is null || !IsCandidate(entry))
            throw new FileNotFoundException($"{entryName} is no longer in {Path.GetFileName(zipPath)}.");
        return ReadEntry(entry);
    }

    /// <summary> The path PKHeX gets for a save inside a .zip: it doesn't exist, so exporting always asks where to write. </summary>
    public static string GetArchivedPath(string zipPath, string entryName) => Path.Combine(zipPath, entryName);
}

/// <summary> Result of a scan: every listed save, and the files that weren't recognized. </summary>
internal sealed record LibraryScan(List<LibraryItem> Items, List<string> Skipped);

/// <summary> A save found in the library (raw details; names and icons are resolved when listed). </summary>
/// <param name="Id">Path relative to the folder, plus "::entry" for a save inside a .zip.</param>
/// <param name="FullPath">The file on disk (the .zip for archived saves).</param>
/// <param name="Entry">The save's name inside the .zip.</param>
/// <param name="FileName">The file's name.</param>
/// <param name="Size">File size in bytes.</param>
/// <param name="Modified">Last write time (local).</param>
internal sealed record LibraryItem(string Id, string FullPath, string? Entry, string FileName, long Size, DateTime Modified)
{
    public GameVersion Version { get; init; }
    public byte Generation { get; init; }
    public string Ot { get; init; } = string.Empty;
    public string Tid { get; init; } = string.Empty;
    public string Sid { get; init; } = string.Empty;
    public string PlayTime { get; init; } = string.Empty;
    public int Language { get; init; }
    public DateTime? Started { get; init; }
    /// <summary> Trainer gender (0 male, 1 female), -1 for games without one. </summary>
    public int Gender { get; init; } = -1;
    public uint Money { get; init; }
    /// <summary> Species caught in the Pokédex, -1 without a Pokédex. </summary>
    public int DexCaught { get; init; } = -1;
    public List<LibraryMon> Party { get; init; } = [];
    public string? Note { get; init; }

    /// <summary> Sub-folder path relative to the library ("" for saves directly in it). </summary>
    public string Group
    {
        get
        {
            var path = Entry is null ? Id : Id[..Id.IndexOf(SaveLibrary.EntrySeparator, StringComparison.Ordinal)];
            var slash = path.LastIndexOf('/');
            return slash < 0 ? string.Empty : path[..slash];
        }
    }
}
