using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows.Forms;
using PKHeX.Core;
using PKHeX.Drawing.PokeSprite;
using PKHeX.WinForms.Controls;
using static PKHeX.Core.MessageStrings;

namespace PKHeX.WinForms.Tidal.Web;

// app.init, file.* and openDropped: a port of Main's file loading (OpenFromPath → OpenFile → LoadFile → OpenSAV/OpenPKM/...).
internal sealed partial class WebApi
{
    private static string TemplatePath => Settings.LocalResources.GetTemplatePath();
    private static string BackupPath => Settings.LocalResources.GetBackupPath();

    private void RegisterFiles()
    {
        Bridge.Register("app.init", _ => GetInit());
        Bridge.Register("app.ready", _ => OnPageReady()); // notification: the page finished booting
        Bridge.Register("file.open", _ => OpenWithDialog());
        Bridge.Register("file.openPath", c => OpenTracked(c.Get<PathArgs>().Path));
        Bridge.Register("openDropped", c => c.Files.Count == 0 ? null : OpenTracked(c.Files[0])); // like Main_DragDrop: first file only
        Bridge.Register("file.exportSave", _ => ExportSaveFile());
        Bridge.Register("file.recent", _ => GetRecent());
#if DEBUG
        // Development only: load a blank save of any game, e.g. { version: "E" } or { version: 2 }.
        Bridge.Register("dev.blank", c =>
        {
            var v = c.Get<DevBlankArgs>().Version;
            var version = int.TryParse(v, out var n) ? (GameVersion)n : Enum.Parse<GameVersion>(v, true);
            SAV.State.Edited = false; // tests switch saves freely; skip the unsaved-changes prompt
            Session.EditorDirty = false;
            LoadBlankSaveFile(version);
            return GetSaveSummary();
        });
        // Development only: list the save editors for blank saves too (PKHeX hides them because blanks can't be exported).
        Bridge.Register("dev.showAllTools", _ => DevShowAllTools = true);
        // Development only: write the current save to a path (test data for screenshots and classic-mode checks).
        Bridge.Register("dev.saveTo", c =>
        {
            File.WriteAllBytes(c.Get<DevPathArgs>().Path, SAV.Write().ToArray());
            return true;
        });
#endif
    }

    private InitResult GetInit()
    {
        Host.NotifyPageReady();
        Host.Dialogs.SetPageAlive(true);
        Host.ScheduleStartupActions();
        return new InitResult
        {
            Version = GetVersionString(),
            TidalVersion = Tidal.TidalVersion.Display(Tidal.TidalVersion.Current),
            Hax = HaX,
            Save = GetSaveSummary(),
            Recent = GetRecent(),
            Settings = GetUiSettings(),
        };
    }

    private object? OnPageReady()
    {
        Host.NotifyPageReady();
        Host.Dialogs.SetPageAlive(true);
        Host.ScheduleStartupActions();
        return null;
    }

    private static string GetVersionString()
    {
        var v = Program.CurrentVersion;
        return $"{2000 + v.Major:00}{v.Minor:00}{v.Build:00}";
    }

    private static List<RecentFile> GetRecent() => Settings.Startup.RecentlyLoaded
        .Where(z => !string.IsNullOrWhiteSpace(z))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(z => new RecentFile
        {
            Path = z,
            Name = Path.GetFileName(z),
            Folder = Path.GetDirectoryName(z) ?? string.Empty,
            Exists = File.Exists(z),
        })
        .ToList();

    private SaveSummary? OpenWithDialog()
    {
        if (!WinFormsUtil.OpenSAVPKMDialog(SAV.PKMExtensions, out var path))
            return null;
        return OpenTracked(path);
    }

    /// <summary>
    /// Opens any supported file. Returns the save summary if anything was loaded (save, Pokémon, boxes), otherwise null.
    /// </summary>
    private SaveSummary? OpenTracked(string path)
    {
        var before = Session.Revision;
        OpenFromPath(path);
        return Session.Revision != before ? GetSaveSummary() : null;
    }

    /// <summary>
    /// Startup: the save and Pokémon picked by <see cref="StartupUtil.GetStartup"/> (command line, detected save or blank save).
    /// </summary>
    public void LoadInitialFiles(StartupArguments args)
    {
        var sav = args.SAV!;
        var path = sav.Metadata.FilePath ?? string.Empty;
        OpenSAV(sav, path);

        var pk = args.Entity!;
        OpenPKM(pk);

        if (args.Error is { } ex)
            ErrorWindow.ShowErrorDialog(MsgFileLoadFailAuto, ex, true);
    }

    private void LoadBlankSaveFile(GameVersion version)
    {
        if (!version.IsValidSavedVersion())
            version = Latest.Version;
        var current = SAV;
        var sav = BlankSaveFile.Get(version, current);
        OpenSAV(sav, string.Empty);
        SAV.State.Edited = false; // Prevents form close warning from showing until changes are made
        EmitSaveChanged();
    }

    internal void OpenFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (TryPluginLoadFile(path))
            return; // handled by a plugin

        // detect if it is a folder (load into boxes or not)
        if (Directory.Exists(path))
        {
            LoadBoxesFromFolder(out _, path);
            return;
        }

        var fi = new FileInfo(path);
        if (!fi.Exists)
        {
            Warn($"File not found: {path}");
            return;
        }

        if (FileUtil.IsFileTooBig(fi.Length))
        {
            WinFormsUtil.Error(MsgFileSizeLarge + Environment.NewLine + string.Format(MsgFileSize, fi.Length), path);
            return;
        }
        if (FileUtil.IsFileTooSmall(fi.Length))
        {
            WinFormsUtil.Error(MsgFileSizeSmall + Environment.NewLine + string.Format(MsgFileSize, fi.Length), path);
            return;
        }
        byte[] input; try { input = File.ReadAllBytes(path); }
        catch (Exception e) { WinFormsUtil.Error(MsgFileInUse + path, e); return; }

        string ext = fi.Extension;
#if DEBUG
        OpenFile(input, path, ext);
#else
        try { OpenFile(input, path, ext); }
        catch (Exception e) { WinFormsUtil.Error(MsgFileLoadFail + "\nPath: " + path, e); }
#endif
    }

    private void OpenFile(Memory<byte> input, string path, string ext)
    {
        var obj = FileUtil.GetSupportedFile(input, ext, SAV);
        if (obj is not null && LoadFile(obj, path))
            return;

        WinFormsUtil.Error(GetHintInvalidFile(input.Span, path),
            $"{MsgFileLoad}{Environment.NewLine}{path}",
            $"{string.Format(MsgFileSize, input.Length)}{Environment.NewLine}{input.Length} bytes (0x{input.Length:X4})");
    }

    private static string GetHintInvalidFile(ReadOnlySpan<byte> input, string path)
    {
        bool isSAV = WinFormsUtil.IsFileExtensionSAV(path);
        if (!isSAV)
            return MsgPKMUnsupported;

        // Include a hint for the user to check if the file is all 00 or all FF
        bool allZero = !input.ContainsAnyExcept<byte>(0x00);
        if (allZero)
            return MsgFileLoadAllZero;
        bool allFF = !input.ContainsAnyExcept<byte>(0xFF);
        if (allFF)
            return MsgFileLoadAllFFFF;

        return MsgFileUnsupported;
    }

    private bool LoadFile(object? input, string path)
    {
        if (input is null)
            return false;

        switch (input)
        {
            case PKM pk: return OpenPKM(pk);
            case SaveFile s: return OpenSAV(s, path);
            case IPokeGroup b: return OpenGroup(b);
            case MysteryGift g: return OpenMysteryGift(g, path);
            case ConcatenatedEntitySet pkms: return OpenPCBoxBin(pkms);
            case IEncounterConvertible enc: return OpenPKM(enc.ConvertToPKM(SAV));

            case SAV3GCMemoryCard gc:
                if (!CheckGCMemoryCard(gc, path))
                    return true;
                if (!SaveUtil.TryGetSaveFile(gc, out var mcsav))
                    return false;
                mcsav.Metadata.SetExtraInfo(path);
                return OpenSAV(mcsav, path);
        }
        return false;
    }

    private bool OpenPKM(PKM pk)
    {
        var sav = SAV;
        var destType = sav.PKMType;
        var tmp = EntityConverter.ConvertToType(pk, destType, out var c);
        Debug.WriteLine(c.GetDisplayString(pk, destType));
        if (tmp is null)
            return false;

        var unconverted = ReferenceEquals(pk, tmp);
        if (unconverted && sav is { State.Exportable: true })
            sav.AdaptToSaveFile(tmp);
        LoadEditor(tmp);
        return true;
    }

    private bool OpenGroup(IPokeGroup b)
    {
        bool result = OpenGroupIntoBox(b, out var msg);
        if (!string.IsNullOrWhiteSpace(msg))
            WinFormsUtil.Alert(msg);
        Debug.WriteLine(msg);
        return result;
    }

    private bool OpenMysteryGift(MysteryGift tg, string path)
    {
        if (!tg.IsEntity)
        {
            WinFormsUtil.Alert(MsgPKMMysteryGiftFail, path);
            return true;
        }

        var temp = tg.ConvertToPKM(SAV);
        var destType = SAV.PKMType;
        var pk = EntityConverter.ConvertToType(temp, destType, out var c);

        if (pk is null)
        {
            WinFormsUtil.Alert(c.GetDisplayString(temp, destType));
            return true;
        }

        SAV.AdaptToSaveFile(pk);
        LoadEditor(pk);
        Debug.WriteLine(c);
        return true;
    }

    private bool OpenPCBoxBin(ConcatenatedEntitySet pkms)
    {
        if (!OpenPCBoxBin(pkms.Data.Span, out var msg))
        {
            WinFormsUtil.Alert(MsgFileLoadIncompatible, msg);
            return true;
        }

        WinFormsUtil.Alert(msg);
        return true;
    }

    private SaveFileType SelectMemoryCardSaveGame(SAV3GCMemoryCard memCard)
    {
        if (memCard.SaveGameCount == 1)
            return memCard.SelectedGameVersion;

        string[] games =
        [
            MsgGameColosseum,
            MsgGameXD,
            MsgGameRSBOX,
        ];

        if (!Host.TrySelectIndex(MsgFileLoadSaveMultiple, MsgFileLoadSaveSelectGame, games, out var index))
            return SaveFileType.None;
        return index switch
        {
            0 => SaveFileType.Colosseum,
            1 => SaveFileType.XD,
            2 => SaveFileType.RSBox,
            _ => SaveFileType.None,
        };
    }

    private bool CheckGCMemoryCard(SAV3GCMemoryCard memCard, string path)
    {
        var state = memCard.GetMemoryCardState();
        switch (state)
        {
            case MemoryCardSaveStatus.NoPkmSaveGame:
                WinFormsUtil.Error(MsgFileGameCubeNoGames, path);
                return false;

            case MemoryCardSaveStatus.DuplicateCOLO:
            case MemoryCardSaveStatus.DuplicateXD:
            case MemoryCardSaveStatus.DuplicateRSBOX:
                WinFormsUtil.Error(MsgFileGameCubeDuplicate, path);
                return false;

            case MemoryCardSaveStatus.MultipleSaveGame:
                var game = SelectMemoryCardSaveGame(memCard);
                if (game == 0) // Cancel
                    return false;
                memCard.SelectSaveGame(game);
                break;

            case MemoryCardSaveStatus.SaveGameCOLO: memCard.SelectSaveGame(SaveFileType.Colosseum); break;
            case MemoryCardSaveStatus.SaveGameXD: memCard.SelectSaveGame(SaveFileType.XD); break;
            case MemoryCardSaveStatus.SaveGameRSBOX: memCard.SelectSaveGame(SaveFileType.RSBox); break;

            default:
                WinFormsUtil.Error(!SAV3GCMemoryCard.IsMemoryCardSize(memCard.Data.Length) ? MsgFileGameCubeBad : GetHintInvalidFile(memCard.Data, path), path);
                return false;
        }
        return true;
    }

    private static void StoreLegalSaveGameData(SaveFile sav)
    {
        if (sav is SAV3 sav3)
            EReaderBerrySettings.LoadFrom(sav3);
    }

    /// <param name="sav">Save to load.</param>
    /// <param name="path">Where it was loaded from.</param>
    /// <param name="forceOpen">Load even if the version is invalid.</param>
    /// <param name="archived">
    /// Loaded from inside a .zip (Save Manager): <paramref name="path"/> doesn't exist on disk, so there is nothing to
    /// back up or check for write access, and it isn't added to the recent files (startup can't reopen it).
    /// </param>
    internal bool OpenSAV(SaveFile sav, string path, bool forceOpen = false, bool archived = false)
    {
        if (Control.ModifierKeys == Keys.Alt)
        {
            SaveTypeInfo other = default;
            if (SaveUtil.TryOverride(sav, other, out var replace))
                sav = replace;
        }
        if (!sav.IsVersionValid() && !forceOpen)
        {
            WinFormsUtil.Error(MsgFileLoadSaveLoadFail, path);
            return true;
        }

        sav.Metadata.SetExtraInfo(path);
        if (!SanityCheckSAV(ref sav))
            return true;

        if (SAV.State.Edited && Settings.SlotWrite.ModifyUnset)
        {
            var prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, MsgProgramCloseUnsaved, MsgProgramSaveFileConfirm);
            if (prompt != DialogResult.Yes)
                return true;
        }

        ClosePopups();
        CancelSearch();

        StoreLegalSaveGameData(sav);
        ParseSettings.InitFromSaveFileData(sav); // physical GB, no longer used in logic
        RecentTrainerCache.SetRecentTrainer(sav);
        SpriteUtil.Initialize(sav); // refresh sprite generator

        GameInfo.FilteredSources = new FilteredGameDataSource(sav, GameInfo.Sources, HaX);
        ResetSAVPKMEditors(sav);

        if (!archived)
        {
            TryBackupExportCheck(sav, path);
            CheckLoadPath(path);
            Settings.Startup.LoadSaveFile(path);
        }
        if (Settings.Sounds.PlaySoundSAVLoad)
            SystemSounds.Asterisk.Play();

        NotifyPluginsSaveLoaded();
        EmitSaveLoaded(); // the editor was reset to the template; the page fetches it with editor.get
        return true;
    }

    private void ResetSAVPKMEditors(SaveFile sav)
    {
        var pk = sav.LoadTemplate(TemplatePath);
        Session.SetSave(sav, pk);
        ResetTrainerDatabase();
        sav.State.Edited = false;
    }

    /// <summary>
    /// Closes stray non-modal windows that belong to the previous save, like Main.ClosePopups.
    /// </summary>
    private void ClosePopups()
    {
        var forms = Application.OpenForms;
        for (int i = forms.Count - 1; i >= 0; i--)
        {
            var f = forms[i];
            if (f is null || ReferenceEquals(f, Host) || f is SplashScreen or PokePreview)
                continue;
            if (f.InvokeRequired)
                continue; // from another thread, not our scope.
            f.Close();
        }
    }

    private static bool TryBackupExportCheck(SaveFile sav, string path)
    {
        // If backup folder exists, save a backup.
        if (string.IsNullOrWhiteSpace(path))
            return false; // not actual save
        if (!Settings.Backup.BAKEnabled)
            return false;
        if (!sav.State.Exportable)
            return false; // not actual save
        var dir = BackupPath;
        if (!Directory.Exists(dir))
            return false;

        var meta = sav.Metadata;
        var backupName = meta.GetBackupFileName(dir);
        if (File.Exists(backupName))
            return false; // Already backed up.

        // Ensure the file we are copying exists.
        var src = meta.FilePath;
        if (src is null || !File.Exists(src))
            return false;

        try
        {
            // Don't need to force overwrite, but on the off-chance it was written externally, we force ours.
            File.Copy(src, backupName, true);
            return true;
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error(MsgBackupUnable, ex);
            return false;
        }
    }

    private static bool CheckLoadPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false; // not actual save
        if (!FileUtil.IsFileLocked(path))
            return true;

        WinFormsUtil.Alert(MsgFileWriteProtected + Environment.NewLine + path, MsgFileWriteProtectedAdvice);
        return false;
    }

    private bool SanityCheckSAV(ref SaveFile sav)
    {
        if (sav.Generation <= 3)
        {
            if (LibraryHints is { } hints)
                SaveLibrary.ApplyGameHints(sav, hints); // the file name, then its .zip and folders (Save Manager)
            else
                SaveLanguage.TryRevise(sav);
        }

        if (sav.State.Exportable && sav is SAV3 s3)
        {
            if (Control.ModifierKeys == Keys.Control || s3.IsCorruptPokedexFF())
            {
                ReadOnlySpan<GameVersion> choices = [GameVersion.R, GameVersion.S, GameVersion.E, GameVersion.FR, GameVersion.LG];
                var options = new string[choices.Length];
                for (int i = 0; i < options.Length; i++)
                    options[i] = GameInfo.Strings.gamelist[(int)choices[i]];

                var msg = string.Format(MsgFileLoadVersionDetect, $"3 ({s3.Version})");
                var text = MsgFileLoadSaveSelectVersion;
                if (sav.Metadata.FileName is { } fn)
                    text += Environment.NewLine + fn;
                if (!Host.TrySelectIndex(msg, text, options, out var index, choices.IndexOf(s3.Version)))
                    return false;

                var game = choices[index];
                var s = s3.ForceLoad(game);
                if (s is SAV3FRLG frlg)
                {
                    // Try to give the correct Deoxys form stats (different in R/S, E, FR and LG)
                    bool result = frlg.ResetPersonal(game);
                    if (!result)
                        return false;
                }
                var origin = sav.Metadata.FilePath;
                if (origin is not null)
                    s.Metadata.SetExtraInfo(origin);
                sav = s;
            }
            else if (s3 is SAV3FRLG frlg && !frlg.Version.IsValidSavedVersion()) // IndeterminateSubVersion
            {
                ReadOnlySpan<GameVersion> choices = [GameVersion.FR, GameVersion.LG];
                var options = new string[choices.Length];
                for (int i = 0; i < options.Length; i++)
                    options[i] = GameInfo.Strings.gamelist[(int)choices[i]];

                string dual = "{1}/{2} " + MsgFileLoadVersionDetect;
                var msg = string.Format(dual, "3", options[0], options[1]);
                if (!Host.TrySelectIndex(msg, MsgFileLoadSaveSelectVersion, options, out var index))
                    return false;

                var game = choices[index];
                bool result = frlg.ResetPersonal(game);
                if (!result)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Main.ClickExportSAV + SAVEditor.ExportSaveFile.
    /// </summary>
    private bool ExportSaveFile()
    {
        var sav = SAV;
        if (!sav.State.Exportable)
            return false; // hot-keys can't cheat the system!

        if (Settings.Advanced.SaveExportCheckUnsavedEntity && Session.EditorDirty)
        {
            var prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, MsgProgramSaveUnsaved, MsgContinue);
            if (prompt != DialogResult.Yes)
                return false;
        }

        bool reload = sav is IStorageCleanup b && b.FixStoragePreWrite();
        if (reload)
            RefreshAllSlots();

        // The classic box view keeps SAV.CurrentBox in sync with the displayed box; do the same before writing.
        var box = Session.CurrentBox;
        if (sav.HasBox && (uint)box < sav.BoxCount && sav.CurrentBox != box)
            sav.CurrentBox = box;

        bool forceSaveAs = Settings.Advanced.SaveExportForceSaveAs;
        var result = WinFormsUtil.ExportSAVDialog(Host, sav, box, forceSaveAs);
        EmitSaveChanged();
        return result;
    }

    #region Box imports (SAVEditor.OpenGroup / OpenPCBoxBin / LoadBoxes)

    private string GetBoxName(int box)
    {
        var names = BoxUtil.GetBoxNames(SAV);
        return (uint)box < names.Length ? names[box] : $"Box {box + 1}";
    }

    private bool OpenGroupIntoBox(IPokeGroup b, out string c)
    {
        if (!SAV.HasBox)
        {
            c = MsgSaveBoxFailNone;
            return true;
        }

        var box = Session.CurrentBox;
        var msg = string.Format(MsgSaveBoxImportGroup, GetBoxName(box));
        var prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, msg, MsgSaveBoxImportOverwrite);
        if (prompt != DialogResult.Yes)
        {
            c = string.Empty;
            return true; // declined: handled (don't report the file as unsupported)
        }

        var settings = GetImportSettingsOverride();
        var slotSkipped = ImportGroup(b.Contents, SAV, box, settings);
        SAV.State.Edited = true;
        RefreshAllSlots();

        c = slotSkipped > 0 ? string.Format(MsgSaveBoxImportSkippedLocked, slotSkipped) : MsgSaveBoxImportGroupSuccess;
        return true;
    }

    private static int ImportGroup(IEnumerable<PKM> data, SaveFile sav, int box, EntityImportSettings settings)
    {
        var type = sav.PKMType;
        int slotSkipped = 0;
        int index = 0;
        foreach (var x in data)
        {
            var i = index++;
            if (i >= sav.BoxSlotCount || (box * sav.BoxSlotCount) + i >= sav.SlotCount)
            {
                slotSkipped++; // never spill past the target box
                continue;
            }
            if (sav.IsBoxSlotOverwriteProtected(box, i))
            {
                slotSkipped++;
                continue;
            }

            var convert = EntityConverter.ConvertToType(x, type, out _);
            if (convert?.GetType() != type)
            {
                slotSkipped++;
                continue;
            }
            sav.SetBoxSlotAtIndex(convert, box, i, settings); // write the converted entity
        }

        return slotSkipped;
    }

    private bool OpenPCBoxBin(ReadOnlySpan<byte> input, out string c)
    {
        var sav = SAV;
        if (!sav.HasBox)
        {
            c = MsgSaveBoxFailNone;
            return false;
        }

        var box = Session.CurrentBox;
        if (sav.GetPCBinary().Length == input.Length)
        {
            if (sav.IsAnySlotLockedInBox(0, sav.BoxCount - 1))
            { c = MsgSaveBoxImportPCFailBattle; return false; }
            if (!sav.SetPCBinary(input))
            { c = string.Format(MsgSaveCurrentGeneration, sav.Generation); return false; }

            c = MsgSaveBoxImportPCBinary;
        }
        else if (sav.GetBoxBinary(box).Length == input.Length)
        {
            if (sav.IsAnySlotLockedInBox(box, box))
            { c = MsgSaveBoxImportBoxFailBattle; return false; }
            if (!sav.SetBoxBinary(input, box))
            { c = string.Format(MsgSaveCurrentGeneration, sav.Generation); return false; }

            c = MsgSaveBoxImportBoxBinary;
        }
        else
        {
            c = string.Format(MsgSaveCurrentGeneration, sav.Generation);
            return false;
        }
        sav.State.Edited = true;
        RefreshAllSlots();
        return true;
    }

    private bool LoadBoxesFromFolder(out string result, string path)
    {
        result = string.Empty;
        if (!SAV.HasBox)
            return false;
        if (!Directory.Exists(path))
            return false;

        if (!GetBulkImportSettings(out bool clearAll, out var overwrite, out var settings))
            return false;

        SAV.LoadBoxes(path, out result, Session.CurrentBox, clearAll, overwrite, settings);
        SAV.State.Edited = true;
        RefreshAllSlots();
        return true;
    }

    private static bool GetBulkImportSettings(out bool clearAll, out bool overwrite, out EntityImportSettings settings)
    {
        clearAll = false; settings = default; overwrite = false;
        var dr = WinFormsUtil.Prompt(MessageBoxButtons.YesNoCancel, MsgSaveBoxImportClear, MsgSaveBoxImportClearNo);
        if (dr == DialogResult.Cancel)
            return false;

        clearAll = dr == DialogResult.Yes;
        settings = GetImportSettingsOverride();
        return true;
    }

    private static EntityImportSettings GetImportSettingsOverride()
    {
        var choice = WinFormsUtil.Prompt(MessageBoxButtons.YesNoCancel,
            MsgSaveBoxImportModifyIntro,
            MsgSaveBoxImportModifyYes + Environment.NewLine +
            MsgSaveBoxImportModifyNo + Environment.NewLine +
            string.Format(MsgSaveBoxImportModifyCurrent, SaveFile.SetUpdateSettings));
        return choice switch
        {
            DialogResult.Yes => EntityImportSettings.All,
            DialogResult.No => EntityImportSettings.None,
            _ => default,
        };
    }

    #endregion
}
