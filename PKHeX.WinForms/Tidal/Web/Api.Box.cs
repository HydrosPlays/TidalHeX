using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Windows.Forms;
using PKHeX.Core;
using static PKHeX.Core.MessageStrings;

namespace PKHeX.WinForms.Tidal.Web;

// box.* : storage slots, with the semantics of SlotChangeManager (drag & drop) and ContextMenuSAV (view / set / delete).
internal sealed partial class WebApi
{
    private enum MoveMode { Move, Clone, Overwrite }

    private void RegisterBox()
    {
        Bridge.Register("box.get", c => GetBox(c.Get<BoxArgs>().Box));
        Bridge.Register("box.party", _ => new PartyData { Slots = GetPartySlots() });
        Bridge.Register("box.move", c =>
        {
            var args = c.Get<MoveArgs>();
            return MoveSlot(args.From, args.To, ParseMoveMode(args.Mode));
        });
        Bridge.Register("box.delete", c => DeleteSlot(c.Get<SlotArgs>().Slot));
        Bridge.Register("box.view", c => ViewSlot(c.Get<SlotArgs>().Slot));
        Bridge.Register("box.set", c => SetSlotFromEditor(c.Get<SlotArgs>().Slot));
        Bridge.Register("box.dropFile", c => DropFileOnSlot(c.Get<SlotArgs>().Slot, c.Files));
#if DEBUG
        // Development only: the slot-drop logic with a path, since tests can't produce real dropped files.
        Bridge.Register("dev.dropPath", c => { var a = c.Get<DevDropArgs>(); return DropFileOnSlot(a.Slot, [a.Path]); });
#endif
        Bridge.Register("box.legality", c => GetSlotLegalityReport(c.Get<SlotArgs>().Slot));
        Bridge.Register("box.exportFile", c => ExportSlotFile(c.Get<SlotArgs>().Slot));
        Bridge.Register("box.undo", _ => UndoRedo(undo: true));
        Bridge.Register("box.redo", _ => UndoRedo(undo: false));
    }

    private static MoveMode ParseMoveMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        null or "" or "move" => MoveMode.Move,
        "clone" => MoveMode.Clone,
        "overwrite" => MoveMode.Overwrite,
        _ => throw new ArgumentException($"Unknown move mode '{mode}'."),
    };

    private BoxData GetBox(int box)
    {
        var sav = SAV;
        if (!sav.HasBox)
            throw new InvalidOperationException(MsgSaveBoxFailNone);
        if ((uint)box >= sav.BoxCount)
            throw new ArgumentException($"Box {box} does not exist.");

        Session.CurrentBox = box;
        var slots = new SlotDto[sav.BoxSlotCount];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = GetBoxSlotDto(box, i);

        return new BoxData
        {
            Box = box,
            Name = GetBoxName(box),
            BoxCount = sav.BoxCount,
            Wallpaper = $"/wallpaper/{box}?v={Session.WallpaperStamp}",
            Slots = slots,
        };
    }

    /// <summary>
    /// True if removing this party member would leave the party empty or with only eggs.
    /// </summary>
    private bool IsLastPartyMember(ISlotInfo info) => info is SlotInfoParty p && SAV.IsPartyAllEggs(p.Slot);

    /// <summary>
    /// Drag and drop between slots (SlotChangeManager.TrySetPKMDestination / TrySetPKMSource).
    /// </summary>
    /// <remarks>
    /// Move swaps with an occupied target; Overwrite replaces the target and empties the source; Clone copies.
    /// Like classic internal drags, the data is written as-is (no trade/dex update). Each call is one undo step.
    /// </remarks>
    private bool MoveSlot(SlotRef from, SlotRef to, MoveMode mode)
    {
        var source = GetSlotInfo(from);
        var dest = GetSlotInfo(to);
        if (from == to)
            return true; // dropped onto itself

        var sav = SAV;
        if (!source.CanWriteTo(sav) || !dest.CanWriteTo(sav))
        {
            Warn(MsgSaveSlotLocked);
            return false;
        }

        var pk = ReadSlot(source);
        if (pk.Species == 0)
        {
            WinFormsUtil.Asterisk(); // nothing to move
            return false;
        }
        var destPk = ReadSlot(dest);
        bool destEmpty = destPk.Species == 0;

        if (dest.CanWriteTo(sav, pk) != WriteBlockedMessage.None)
        {
            Warn(MsgSaveSlotEmpty);
            return false;
        }

        bool swap = mode == MoveMode.Move && !destEmpty;
        bool removeSource = !swap && mode != MoveMode.Clone;
        if (swap && source.CanWriteTo(sav, destPk) != WriteBlockedMessage.None)
        {
            Warn(MsgSaveSlotEmpty); // the party would end up with only eggs
            return false;
        }
        if (removeSource && dest is not SlotInfoParty && IsLastPartyMember(source))
        {
            Warn(MsgSaveSlotEmpty);
            return false;
        }

        var slots = Session.Slots;
        bool ok;
        if (mode == MoveMode.Clone)
        {
            ok = slots.Set(dest, pk, SlotTouchType.Swap) == SlotTouchResult.Success;
        }
        else if (swap)
        {
            ok = slots.Swap(source, dest) == SlotTouchResult.Success;
        }
        else
        {
            // Target first, then empty the source: emptying a party source slides the party down, which would
            // otherwise shift the target index and overwrite/keep the wrong Pokémon. One undo step.
            ok = slots.Batch([source, dest], _ =>
            {
                dest.WriteTo(sav, pk, EntityImportSettings.None);
                source.WriteTo(sav, sav.BlankPKM, EntityImportSettings.None);
            });
        }

        if (!ok)
        {
            Warn(MsgSaveSlotBadData);
            return false;
        }
        AfterSlotWrite(from, to);
        return true;
    }

    /// <summary>
    /// ContextMenuSAV.ClickDelete.
    /// </summary>
    private bool DeleteSlot(SlotRef slot)
    {
        var info = GetSlotInfo(slot);
        var sav = SAV;
        if (ReadSlot(info).Species == 0)
        {
            WinFormsUtil.Asterisk();
            return false;
        }
        if (!CheckDestination(info, sav.BlankPKM))
            return false;
        if (IsLastPartyMember(info))
        {
            Warn(MsgSaveSlotEmpty);
            return false;
        }

        if (Session.Slots.Delete(info) != SlotTouchResult.Success)
        {
            Warn(MsgSaveSlotBadData);
            return false;
        }
        AfterSlotWrite(slot);
        return true;
    }

    /// <summary>
    /// ContextMenuSAV.ClickView: loads the slot into the editor.
    /// </summary>
    private EditorState ViewSlot(SlotRef slot)
    {
        var info = GetSlotInfo(slot);
        var pk = ReadSlot(info);
        if (pk.Species == 0 && !HaX)
        {
            WinFormsUtil.Asterisk();
            throw new InvalidOperationException("The slot is empty.");
        }

        LoadEditor(pk, skipConversionCheck: true);
        return GetEditorState();
    }

    /// <summary>
    /// ContextMenuSAV.ClickSet: writes the editor Pokémon into the slot (with the save's trade/dex update settings).
    /// </summary>
    private bool SetSlotFromEditor(SlotRef slot)
    {
        var pk = EditorSvc.Prepare(); // finalized like PKHeX's PreparePKM (stats, moves, EC, dates, checksum)
        if (!IsEditorComplete(pk))
        {
            Warn("The editor is empty.");
            return false;
        }

        var info = GetSlotInfo(slot);
        var sav = SAV;
        if (pk.GetType() != sav.PKMType)
        {
            var converted = EntityConverter.ConvertToType(pk, sav.PKMType, out var c);
            if (converted is null)
            {
                Warn(c.GetDisplayString(pk, sav.PKMType));
                return false;
            }
            pk = converted;
        }

        if (!CheckDestination(info, pk))
            return false;
        if (pk.Species == 0 && IsLastPartyMember(info)) // HaX: an empty editor deletes the slot
        {
            Warn(MsgSaveSlotEmpty);
            return false;
        }

        var errata = sav.EvaluateCompatibility(pk);
        if (errata.Count != 0)
        {
            var msg = string.Join(Environment.NewLine, errata);
            var prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, msg, MsgContinue);
            if (prompt != DialogResult.Yes)
                return false;
        }

        if (Session.Slots.Set(info, pk) != SlotTouchResult.Success)
        {
            Warn(MsgSaveSlotBadData);
            return false;
        }
        Session.EditorDirty = false; // NotifyWasExported
        AfterSlotWrite(slot);
        return true;
    }

    /// <summary>
    /// SlotChangeManager.HandleDropPKM / TryLoadFiles: a Pokémon (or gift) file dropped onto a slot is converted and
    /// written into that slot. Anything else (a save, a folder, box data) is opened like a normal drop, as in classic.
    /// </summary>
    private SlotDropResult DropFileOnSlot(SlotRef slot, IReadOnlyList<string> files)
    {
        if (files.Count == 0)
            return new SlotDropResult(false, null);

        var path = files[0]; // first file only, like classic
        var sav = SAV;
        PKM? temp;
        try
        {
            temp = File.Exists(path) ? FileUtil.GetSingleFromPath(path, sav) : null;
        }
        catch (Exception ex)
        {
            Warn($"Couldn't read {Path.GetFileName(path)}: {ex.Message}");
            return new SlotDropResult(false, null);
        }
        if (temp is null) // not a single Pokémon: pass through to the normal open
            return new SlotDropResult(false, OpenTracked(path));

        var pk = EntityConverter.ConvertToType(temp, sav.PKMType, out var result);
        if (pk is null)
        {
            Warn(result.GetDisplayString(temp, sav.PKMType));
            return new SlotDropResult(false, null);
        }
        if (sav is ILangDeviantSave il && !EntityConverter.IsCompatibleGB(temp, il.Japanese, pk.Japanese))
        {
            Warn(EntityConverterResult.IncompatibleLanguageGB.GetIncompatibleGBMessage(pk, il.Japanese));
            return new SlotDropResult(false, null);
        }

        var info = GetSlotInfo(slot);
        if (!CheckDestination(info, pk)) // locked slots, eggs/blanks where the party can't take them
            return new SlotDropResult(false, null);

        var errata = sav.EvaluateCompatibility(pk);
        if (errata.Count != 0)
        {
            var prompt = WinFormsUtil.Prompt(MessageBoxButtons.YesNo, string.Join(Environment.NewLine, errata), MsgContinue);
            if (prompt != DialogResult.Yes)
                return new SlotDropResult(false, null);
        }

        if (Session.Slots.Set(info, pk) != SlotTouchResult.Success)
        {
            Warn(MsgSaveSlotBadData);
            return new SlotDropResult(false, null);
        }
        AfterSlotWrite(slot);

        var names = GameInfo.Strings.specieslist;
        var name = pk.Species < names.Length ? names[pk.Species] : $"#{pk.Species}";
        var where = slot.IsParty ? $"party slot {slot.Slot + 1}" : $"{GetBoxName(slot.Box)}, slot {slot.Slot + 1}";
        Toast("success", $"Placed {name} in {where}");
        return new SlotDropResult(true, null);
    }

    /// <summary>
    /// ContextMenuSAV.CheckDest.
    /// </summary>
    private bool CheckDestination(ISlotInfo info, PKM pk)
    {
        if (!info.CanWriteTo(SAV))
        {
            Warn(MsgSaveSlotLocked);
            return false;
        }

        var msg = info.CanWriteTo(SAV, pk);
        switch (msg)
        {
            case WriteBlockedMessage.None:
                return true;
            case WriteBlockedMessage.InvalidPartyConfiguration:
                Warn(MsgSaveSlotEmpty);
                return false;
            case WriteBlockedMessage.InvalidDestination:
                Warn(MsgSaveSlotLocked);
                return false;
            default: // IncompatibleFormat
                WinFormsUtil.Exclamation();
                return false;
        }
    }

    private LegalityReport GetSlotLegalityReport(SlotRef slot)
    {
        var info = GetSlotInfo(slot);
        var pk = ReadSlot(info);
        if (pk.Species == 0)
            throw new InvalidOperationException("The slot is empty.");

        var la = new LegalityAnalysis(pk, SAV.Personal, info.Type);
        if (Settings.Sounds.PlaySoundLegalityCheck)
            SystemSounds.Asterisk.Play();
        return new LegalityReport
        {
            Valid = la.Valid,
            Report = la.Report(GameInfo.CurrentLanguage, verbose: true).ReplaceLineEndings("\n"),
        };
    }

    private bool ExportSlotFile(SlotRef slot)
    {
        var pk = ReadSlot(GetSlotInfo(slot));
        if (pk.Species == 0)
        {
            WinFormsUtil.Asterisk();
            return false;
        }
        pk = pk.Clone();
        pk.ForcePartyData(); // stored slots have no party stats; exports are party-sized (classic drag-out does the same)
        return WinFormsUtil.SavePKMDialog(pk);
    }

    private bool UndoRedo(bool undo)
    {
        var changelog = Session.Slots.Changelog;
        if (undo ? !changelog.CanUndo : !changelog.CanRedo)
            return false;

        var slots = undo ? changelog.Undo() : changelog.Redo();
        var refs = new List<SlotRef>(slots.Count);
        foreach (var slot in slots)
            refs.Add(ToRef(slot));
        AfterSlotWrite([.. refs]);
        return true;
    }

    /// <summary>
    /// Marks the save edited, refreshes the touched slot images and notifies the page.
    /// </summary>
    private void AfterSlotWrite(params ReadOnlySpan<SlotRef> changed)
    {
        SAV.State.Edited = true;

        var boxes = new HashSet<int>();
        bool party = SAV is SAV7b; // Let's Go stores the party inside the boxes
        foreach (var r in changed)
        {
            if (r.IsParty)
            {
                party = true;
                continue;
            }
            Session.TouchSlot(r.Box, r.Slot);
            boxes.Add(r.Box);
        }
        if (party)
            Session.TouchParty();

        EmitSaveChanged();
        if (party && SAV.HasParty)
            EmitBoxChanged(SlotRef.Party);
        foreach (var box in boxes)
            EmitBoxChanged(box);
    }
}
