using System;
using System.Globalization;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// Editing state of the web UI: the loaded save, the editor Pokémon, slot undo/redo history, image versions and search results.
/// </summary>
internal sealed class WebSession
{
    public const int PartySize = 6;

    public SaveFile SAV { get; private set; } = FakeSaveFile.Default;

    /// <summary>
    /// Slot writes with PKHeX's undo/redo changelog (one entry per operation; party changes snapshot the whole party).
    /// </summary>
    public SlotEditor<object> Slots { get; private set; } = new(FakeSaveFile.Default);

    /// <summary> Pokémon in the editor; always of the save's entity type. </summary>
    public PKM Editor { get; private set; } = FakeSaveFile.Default.BlankPKM;

    /// <summary> Editor has changes that were not written to a slot or file (set by the future editor API). </summary>
    public bool EditorDirty { get; set; }

    /// <summary> Box last shown by the page; used as "current box" for imports, box layout and export. </summary>
    public int CurrentBox { get; set; }

    public List<IEncounterInfo> Encounters { get; private set; } = [];
    public List<MysteryGift>? Gifts { get; private set; }
    public List<GiftResult>? GiftResults { get; private set; }

    // Result lists get never-reused ids that are part of their tokens: the page caches images by URL
    // (/sprite/enc/{token}), so a token must never point at a different encounter than it did before.
    private long ListIds;
    private long EncounterListId = -1;
    private long GiftListId = -1;

    public long NextListId() => ++ListIds;

    public void SetEncounters(List<IEncounterInfo> list, long id)
    {
        Encounters = list;
        EncounterListId = id;
    }

    /// <summary> Drops the cached gift list so the next <c>gift.all</c> reloads it (e.g. after the gift filter setting changed). </summary>
    public void ResetGifts()
    {
        Gifts = null;
        GiftResults = null;
        GiftListId = -1;
    }

    public void SetGifts(List<MysteryGift> gifts, List<GiftResult> results, long id)
    {
        Gifts = gifts;
        GiftResults = results;
        GiftListId = id;
    }

    #region Image versions
    // Every change gets a new, never reused stamp so a URL with ?v=stamp can be cached forever.
    private long Stamp;
    private long BaseStamp;
    private readonly Dictionary<(int Box, int Slot), long> SlotStamps = [];
    private readonly Dictionary<(int Box, int Slot), (long Stamp, bool Valid)> LegalityCache = [];

    /// <summary> Increases whenever anything visible changes; used to detect whether an operation loaded anything. </summary>
    public long Revision => Stamp;
    public long EditorStamp { get; private set; }
    public long WallpaperStamp { get; private set; }

    private long Next() => ++Stamp;

    public long GetSlotStamp(int box, int slot) => SlotStamps.TryGetValue((box, slot), out var s) ? s : BaseStamp;

    public void TouchSlot(int box, int slot) => SlotStamps[(box, slot)] = Next();

    public void TouchParty()
    {
        for (int i = 0; i < PartySize; i++)
            TouchSlot(SlotRef.Party, i);
    }

    /// <summary> Invalidates every slot and wallpaper image (bulk edits, tools, settings changes). </summary>
    public void TouchAll()
    {
        SlotStamps.Clear();
        LegalityCache.Clear();
        BaseStamp = WallpaperStamp = Next();
    }

    public bool TryGetLegality(int box, int slot, out bool valid)
    {
        valid = false;
        if (!LegalityCache.TryGetValue((box, slot), out var entry) || entry.Stamp != GetSlotStamp(box, slot))
            return false;
        valid = entry.Valid;
        return true;
    }

    public void SetLegality(int box, int slot, bool valid) => LegalityCache[(box, slot)] = (GetSlotStamp(box, slot), valid);
    #endregion

    /// <summary>
    /// Starts a new editing session for <paramref name="sav"/> with <paramref name="editor"/> loaded in the editor.
    /// </summary>
    public void SetSave(SaveFile sav, PKM editor)
    {
        SAV = sav;
        Slots = new SlotEditor<object>(sav);
        var box = sav.HasBox ? sav.CurrentBox : 0;
        CurrentBox = (uint)box >= sav.BoxCount ? 0 : box;
        SetEncounters([], -1);
        ResetGifts();
        TouchAll();
        SetEditor(editor);
    }

    /// <summary> Clears undo/redo; slot history is positional and becomes invalid after bulk storage changes. </summary>
    public void ResetHistory() => Slots = new SlotEditor<object>(SAV);

    /// <summary> Raised after a different Pokémon is loaded into the editor. </summary>
    public event Action? EditorReplaced;

    public void SetEditor(PKM pk)
    {
        Editor = pk.Clone();
        EditorDirty = false;
        TouchEditor();
        EditorReplaced?.Invoke();
    }

    /// <summary> Invalidates the editor sprite (e.g. after sprite settings change). </summary>
    public void TouchEditor() => EditorStamp = Next();

    // Search result tokens: "e{list}-{index}" for encounters, "g{list}-{index}" for gifts (one /sprite/enc/{token} namespace).
    public static string GetEncounterToken(long list, int index) => $"e{list}-{index}";
    public static string GetGiftToken(long list, int index) => $"g{list}-{index}";

    /// <summary> Resolves a search result token (encounter or gift). </summary>
    public IEncounterInfo? GetResult(string token) => GetEncounter(token) ?? GetGift(token);

    public IEncounterInfo? GetEncounter(string token)
        => TryParseToken(token, 'e', EncounterListId, out var index) && index < Encounters.Count ? Encounters[index] : null;

    public MysteryGift? GetGift(string token)
        => TryParseToken(token, 'g', GiftListId, out var index) && Gifts is { } list && index < list.Count ? list[index] : null;

    /// <summary> Parses "{prefix}{list}-{index}"; tokens from an older list are rejected. </summary>
    private static bool TryParseToken(string token, char prefix, long list, out int index)
    {
        index = -1;
        if (token.Length < 4 || char.ToLowerInvariant(token[0]) != prefix)
            return false;
        var span = token.AsSpan(1);
        var dash = span.IndexOf('-');
        return dash > 0
            && long.TryParse(span[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id == list
            && int.TryParse(span[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
}
