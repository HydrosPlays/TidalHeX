using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PKHeX.Core;
using PKHeX.Drawing.PokeSprite;
using PKHeX.WinForms.Controls;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// Host side of the web UI contract (API.md). Each <c>Api.*.cs</c> file registers one group of methods.
/// </summary>
/// <remarks>
/// Replicates what the classic <see cref="Main"/> window does for the engine (settings, language, save loading),
/// without creating the classic editor controls. All members run on the UI thread.
/// </remarks>
internal sealed partial class WebApi
{
    private readonly TidalWebHost Host;
    private readonly WebBridge Bridge;

    public WebSession Session { get; } = new();

    /// <summary> Headless Pokémon editor operating on <see cref="WebSession.Editor"/>. </summary>
    private readonly EditorService EditorSvc;

    private static PKHeXSettings Settings => Program.Settings;
    private static bool HaX => Program.HaX;
    private SaveFile SAV => Session.SAV;

    /// <summary> Slot legality indication, as the classic box view does it (<c>C_SAV.FlagIllegal</c>). </summary>
    public bool FlagIllegal => Settings.Display.FlagIllegal && !HaX;

    public WebApi(TidalWebHost host, WebBridge bridge)
    {
        Host = host;
        Bridge = bridge;
        EditorSvc = new EditorService(Session);

        RegisterFiles();
        RegisterBox();
        RegisterLists();
        RegisterEncounters();
        RegisterTools();
        RegisterItemsTool();
        RegisterTrainerTool();
        RegisterBoxLayoutTool();
        RegisterPokedexTool();
        RegisterPlugins();
        RegisterEditor();
    }

    #region Global initialization (mirrors Main.FormInitializeSecond / ReloadProgramSettings / ApplyMainLanguage)

    /// <summary>
    /// One-time engine setup done by the classic main window's constructor. <see cref="StartupUtil.ReloadSettings"/> already ran in <see cref="Program"/>.
    /// </summary>
    public void InitializeGlobals()
    {
        ReloadProgramSettings(skipCore: true);
        ApplyLanguage(GameLanguage.GetLanguageIndex(Settings.Startup.Language));
    }

    // Main.Unicode has a private setter; subforms (trainer editors, Hall of Fame...) read it for fonts and gender symbols.
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "set_Unicode")]
    private static extern void SetMainUnicode(Main? target, bool value);

    private void ReloadProgramSettings(bool skipCore = false)
    {
        var settings = Settings;
        if (!skipCore)
            StartupUtil.ReloadSettings(settings);

        SetMainUnicode(null, settings.Display.Unicode);
        SpriteName.AllowShinySprite = settings.Sprite.ShinySprites;
        SpriteBuilderUtil.SpriterPreference = settings.Sprite.SpritePreference;

        WinFormsUtil.DetectSaveFileOnFileOpen = settings.Startup.TryDetectRecentSave;
        SelectablePictureBox.FocusBorderDeflate = GenderToggle.FocusBorderDeflate = settings.Display.FocusBorderDeflate;

        if (HaX)
            EntityConverter.AllowIncompatibleConversion = EntityCompatibilitySetting.AllowIncompatibleAll;
        SpriteBuilder.LoadSettings(settings.Sprite);
        WinFormsUtil.AddSaveFileExtensions(settings.Backup.OtherSaveFileExtensions);
        WinFormsUtil.Quiet = !settings.Sounds.PlaySoundOther;
        if (SAV is not FakeSaveFile)
            SpriteUtil.Initialize(SAV); // sprite preference may have changed
    }

    private void ApplyLanguage(int index)
    {
        if ((uint)index < GameLanguage.LanguageCount)
            GameInfo.CurrentLanguage = GameLanguage.LanguageCode(index);

        var lang = GameInfo.CurrentLanguage;
        Settings.Startup.Language = lang;
        WinFormsUtil.SetCultureLanguage(lang);

        LocalizeUtil.InitializeStrings(lang, SAV, HaX);
        LocalizedDescriptionAttribute.Localizer = WinFormsTranslator.GetDictionary(lang);
        SizeCP.ResetSizeLocalizations(lang);
        NotifyPluginsLanguageChanged(lang);
    }

    #endregion

    #region Events

    private void EmitSaveLoaded() => Bridge.Emit("saveLoaded", GetSaveSummary());
    private void EmitSaveChanged() => Bridge.Emit("saveChanged", GetSaveSummary());
    private void EmitBoxChanged(int box) => Bridge.Emit("boxChanged", new { box });
    private void EmitEditorLoaded() => Bridge.Emit("editorLoaded", GetEditorState());

    /// <summary> Shows a transient message in the page. </summary>
    public void Toast(string kind, string text) => Bridge.Emit("toast", new { kind, text });

    private void Warn(string text)
    {
        WinFormsUtil.Exclamation();
        Toast("warn", text);
    }

    /// <summary>
    /// Invalidates all slot images and tells the page to refetch the party and the current box.
    /// </summary>
    private void RefreshAllSlots()
    {
        // Callers rewrote storage wholesale (imports, export compaction, tools, box layout):
        // positional undo entries could now restore an old Pokémon over a different one.
        Session.ResetHistory();
        Session.TouchAll();
        EmitSaveChanged();
        if (SAV.HasParty)
            EmitBoxChanged(SlotRef.Party);
        if (SAV.HasBox)
            EmitBoxChanged(Session.CurrentBox);
    }

    #endregion

    #region Editor

    /// <summary>
    /// Loads a Pokémon into the editor, like <see cref="PKMEditor.PopulateFields"/> (converting it to the save's format).
    /// </summary>
    /// <returns>False if the Pokémon cannot be converted to the save's format.</returns>
    internal bool LoadEditor(PKM pk, bool skipConversionCheck = false)
    {
        var input = pk;
        if (!skipConversionCheck && !EntityConverter.TryMakePKMCompatible(pk, Session.Editor, out var c, out pk))
        {
            WinFormsUtil.Alert(c.GetDisplayString(input, Session.Editor.GetType()));
            return false;
        }

        Session.SetEditor(pk);
        EmitEditorLoaded();
        return true;
    }

    /// <summary> Mirrors <see cref="PKMEditor.EditsComplete"/>: an empty editor can't be written unless HaX. </summary>
    private static bool IsEditorComplete(PKM pk) => pk.Species != 0 || HaX;

    private bool? GetEditorLegality(PKM pk)
    {
        if (HaX || pk.Species == 0)
            return null;
        try
        {
            return new LegalityAnalysis(pk, SAV.Personal).Valid;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Lets classic sub-editors that talk to the PKM editor (Stadium teams, Battle Passes) use the web editor instead.
    /// </summary>
    private sealed class WebPKMView(WebApi api) : IPKMView
    {
        public PKM Data => api.Session.Editor;
        public bool Unicode => Settings.Display.Unicode;
        public bool HaX => Program.HaX;
        public bool ChangingFields { get; set; }
        public bool EditsComplete => IsEditorComplete(Data);
        public PKM PreparePKM(bool click = true) => api.EditorSvc.Prepare();
        public void PopulateFields(PKM pk, bool focus = true, bool skipConversionCheck = false) => api.LoadEditor(pk, skipConversionCheck);
        public void NotifyWasExported(PKM pk) => api.Session.EditorDirty = false;
    }

    #endregion

    #region Slots

    private static string GetSpeciesName(ushort species)
    {
        var list = GameInfo.Strings.Species;
        return species < list.Count ? list[species] : species.ToString();
    }

    private static string GetItemName(int item, GameStrings strings)
    {
        if (item <= 0)
            return string.Empty;
        var list = strings.itemlist;
        return item < list.Length ? list[item] : $"#{item}";
    }

    /// <summary>
    /// Validates a slot reference against the loaded save and returns PKHeX's slot descriptor for it.
    /// </summary>
    private ISlotInfo GetSlotInfo(SlotRef? slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        var sav = SAV;
        if (slot.IsParty)
        {
            if (!sav.HasParty)
                throw new InvalidOperationException("This save file has no party.");
            if ((uint)slot.Slot >= WebSession.PartySize)
                throw new ArgumentException($"Party slot {slot.Slot} does not exist.");
            return new SlotInfoParty(slot.Slot);
        }

        if (!sav.HasBox)
            throw new InvalidOperationException(MessageStrings.MsgSaveBoxFailNone);
        if ((uint)slot.Box >= sav.BoxCount)
            throw new ArgumentException($"Box {slot.Box} does not exist.");
        if (!IsBoxSlotPresent(slot.Box, slot.Slot))
            throw new ArgumentException($"Box {slot.Box + 1} has no slot {slot.Slot + 1}.");
        return new SlotInfoBox(slot.Box, slot.Slot, sav);
    }

    /// <summary> Some saves have a partial last box (e.g. Let's Go: 1000 slots in boxes of 30). </summary>
    private bool IsBoxSlotPresent(int box, int slot) => (uint)slot < SAV.BoxSlotCount && (box * SAV.BoxSlotCount) + slot < SAV.SlotCount;

    /// <summary>
    /// Reads a slot. Party slots past the party count are treated as empty (the game ignores them).
    /// </summary>
    private PKM ReadSlot(ISlotInfo info)
    {
        if (info is SlotInfoParty p && p.Slot >= Math.Min(SAV.PartyCount, WebSession.PartySize))
            return SAV.BlankPKM;
        return info.Read(SAV);
    }

    private static SlotRef ToRef(ISlotInfo info) => info switch
    {
        SlotInfoBox b => new SlotRef(b.Box, b.Slot),
        SlotInfoParty p => new SlotRef(SlotRef.Party, p.Slot),
        _ => new SlotRef(int.MinValue, info.Slot),
    };

    private string GetSlotSpriteUrl(int box, int slot) => $"/sprite/slot/{box}/{slot}?v={Session.GetSlotStamp(box, slot)}";

    private SlotDto GetBoxSlotDto(int box, int slot)
    {
        if (!IsBoxSlotPresent(box, slot))
            return new SlotDto { Box = box, Slot = slot, Empty = true, Locked = true, Sprite = GetSlotSpriteUrl(box, slot) };
        var info = new SlotInfoBox(box, slot, SAV);
        return GetSlotDto(box, slot, info.Read(SAV), info.Type, SAV.IsBoxSlotLocked(box, slot));
    }

    private SlotDto[] GetPartySlots()
    {
        var result = new SlotDto[SAV.HasParty ? WebSession.PartySize : 0];
        for (int i = 0; i < result.Length; i++)
        {
            var pk = ReadSlot(new SlotInfoParty(i));
            result[i] = GetSlotDto(SlotRef.Party, i, pk, StorageSlotType.Party, false);
        }
        return result;
    }

    private SlotDto GetSlotDto(int box, int slot, PKM pk, StorageSlotType type, bool locked)
    {
        var sprite = GetSlotSpriteUrl(box, slot);
        if (pk.Species == 0)
            return new SlotDto { Box = box, Slot = slot, Empty = true, Locked = locked, Sprite = sprite };

        try
        {
            return new SlotDto
            {
                Box = box,
                Slot = slot,
                Species = pk.Species,
                Form = pk.Form,
                Name = GetSpeciesName(pk.Species),
                Nickname = pk.Nickname,
                Level = pk.CurrentLevel,
                Gender = pk.Gender,
                Shiny = pk.IsShiny,
                Egg = pk.IsEgg,
                Legal = GetSlotLegality(box, slot, pk, type),
                HeldItem = GetItemName(pk.HeldItem, GameInfo.Strings),
                Sprite = sprite,
                Locked = locked,
            };
        }
        catch
        {
            // Corrupt data: show the slot as occupied and illegal rather than failing the whole box.
            return new SlotDto { Box = box, Slot = slot, Species = pk.Species, Name = "???", Legal = false, Sprite = sprite, Locked = locked };
        }
    }

    private bool? GetSlotLegality(int box, int slot, PKM pk, StorageSlotType type)
    {
        if (!FlagIllegal)
            return null;
        if (Session.TryGetLegality(box, slot, out var cached))
            return cached;

        bool valid;
        if (!pk.Valid)
        {
            valid = false; // bad egg / checksum failure
        }
        else
        {
            // Same analysis the slot sprite uses for its warning overlay.
            var la = pk.GetType() == SAV.PKMType
                ? new LegalityAnalysis(pk, SAV.Personal, type)
                : new LegalityAnalysis(pk, pk.PersonalInfo, type);
            valid = la.Valid;
        }
        Session.SetLegality(box, slot, valid);
        return valid;
    }

    #endregion

    #region Summary

    public SaveSummary GetSaveSummary()
    {
        var sav = SAV;
        if (sav is FakeSaveFile)
            return new SaveSummary();

        return new SaveSummary
        {
            Loaded = true,
            Blank = !sav.State.Exportable,
            Game = GameInfo.GetVersionName(sav.Version),
            Version = (int)sav.Version,
            Generation = sav.Generation,
            Context = sav.Context.ToString(),
            Ot = sav.OT,
            Tid = FormatTrainerId(sav, secret: false),
            Sid = FormatTrainerId(sav, secret: true),
            PlayTime = GetPlayTime(sav),
            FileName = sav.Metadata.FileName ?? string.Empty,
            FilePath = sav.Metadata.FilePath ?? string.Empty,
            BoxCount = sav.HasBox ? sav.BoxCount : 0,
            SlotsPerBox = sav.HasBox ? sav.BoxSlotCount : 0,
            PokemonCount = CountPokemon(sav),
            SlotCount = sav.HasBox ? sav.SlotCount : 0,
            DexCaught = GetDexCaught(sav),
            DexTotal = sav.HasPokeDex ? sav.MaxSpeciesID : 0,
            HasParty = sav.HasParty,
            HasBox = sav.HasBox,
            Exportable = sav.State.Exportable,
            Edited = sav.State.Edited,
            Party = GetPartySlots(),
        };
    }

    /// <summary> TID/SID as the games display them (TrainerIDFormat). </summary>
    private static string FormatTrainerId(SaveFile sav, bool secret) => sav.TrainerIDDisplayFormat switch
    {
        TrainerIDFormat.SixDigit => secret ? sav.DisplaySID.ToString("D4") : sav.DisplayTID.ToString("D6"),
        TrainerIDFormat.SixteenBitSingle => secret ? string.Empty : sav.DisplayTID.ToString("D5"),
        _ => (secret ? sav.DisplaySID : sav.DisplayTID).ToString("D5"),
    };

    private static string GetPlayTime(SaveFile sav)
    {
        try { return sav.PlayTimeString.Replace('ː', ':'); }
        catch { return string.Empty; }
    }

    private static int CountPokemon(SaveFile sav)
    {
        int count = 0;
        try
        {
            if (sav.HasBox)
            {
                for (int i = 0; i < sav.SlotCount; i++)
                {
                    if (sav.GetBoxSlotAtIndex(i).Species != 0)
                        count++;
                }
            }
            if (sav.HasParty)
                count += sav.PartyData.Count(z => z.Species != 0);
        }
        catch
        {
            // Partial counts are fine for a summary.
        }
        return count;
    }

    private static int GetDexCaught(SaveFile sav)
    {
        if (!sav.HasPokeDex)
            return 0;
        try { return sav.CaughtCount; }
        catch { return 0; }
    }

    #endregion

    #region Settings

    public static UiSettings GetUiSettings() => new()
    {
        ReducedMotion = Settings.Startup.TidalReduceMotion,
        HideSecrets = Settings.Privacy.HideSecretDetails,
        EncountersInGameOnly = Settings.EncounterDb.FilterUnavailableSpecies,
        GiftsInGameOnly = Settings.MysteryDb.FilterUnavailableSpecies,
    };

    private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, [MarshalAs(UnmanagedType.Bool)] out bool pvParam, uint fWinIni);

    /// <summary> Windows "Animation effects" setting (Accessibility → Visual effects). </summary>
    internal static bool IsSystemReducedMotion()
    {
        try
        {
            if (SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out var animate, 0))
                return !animate;
        }
        catch
        {
            // Fall through to the older setting.
        }
        return !SystemInformation.UIEffectsEnabled;
    }

    #endregion

    /// <summary>
    /// Stops background work when the window closes.
    /// </summary>
    public void Shutdown() => CancelSearch();
}
