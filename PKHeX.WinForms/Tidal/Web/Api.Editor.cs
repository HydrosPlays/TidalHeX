using System;
using PKHeX.Core;

namespace PKHeX.WinForms.Tidal.Web;

// editor.*: the Pokémon editor (EditorService does the PKHeX-accurate edits).
internal sealed partial class WebApi
{
    private void RegisterEditor()
    {
        Bridge.Register("editor.get", _ => GetEditorState());
        Bridge.Register("editor.set", c =>
        {
            var a = c.Get<FieldArgs>();
            EditorSvc.Set(a.Field, a.Value);
            return GetEditorState();
        });
        Bridge.Register("editor.suggest", c =>
        {
            var message = EditorSvc.Suggest(c.Get<WhatArgs>().What);
            if (message is not null)
                Toast("info", message);
            return GetEditorState();
        });
        Bridge.Register("editor.legality", _ =>
        {
            var (valid, report) = EditorSvc.GetLegalityReport();
            return new { valid, report };
        });
        Bridge.Register("editor.exportFile", _ => ExportEditorFile());
        Bridge.Register("editor.classic", c => OpenClassicEditor(c.Get<WhatArgs>().What));
        Bridge.Register("editor.moveData", _ => GetMoveData());
    }

    internal EditorState GetEditorState() => EditorSvc.GetState($"/sprite/editor?v={Session.EditorStamp}");

    /// <summary> Every move's type and category in the editor's game, for the move pickers (PKMEditor.ValidateMovePaint draws the type). </summary>
    private MoveDataDto GetMoveData()
    {
        var pk = Session.Editor;
        var context = pk.Context;
        var count = Math.Min(GameInfo.Strings.movelist.Length, pk.MaxMoveID + 1);
        var types = new int[count];
        var categories = new char[count];
        types[0] = -1;
        categories[0] = ' ';
        for (ushort move = 1; move < count; move++)
        {
            types[move] = MoveInfo.GetType(move, context);
            categories[move] = MoveCategory.Get(move, context);
        }
        return new MoveDataDto(types, new string(categories));
    }

    /// <summary> Main.MainMenuSave: exports the finalized editor Pokémon to a file. </summary>
    private bool ExportEditorFile()
    {
        if (!IsEditorComplete(Session.Editor))
        {
            Warn("The editor is empty.");
            return false;
        }
        var pk = EditorSvc.Prepare();
        if (!WinFormsUtil.SavePKMDialog(pk))
            return false;
        Session.EditorDirty = false;
        return true;
    }

    /// <summary> Opens one of PKHeX's detailed sub-editors on the editor Pokémon. </summary>
    private EditorState OpenClassicEditor(string what)
    {
        var pk = Session.Editor;
        var before = pk.Data.ToArray();
        switch (what)
        {
            case "ribbons" when pk.Format >= 3:
            {
                using var form = new RibbonEditor(pk);
                form.ShowDialog(Host);
                break;
            }
            case "memories" when pk.Format >= 6 && pk is not PB7:
            {
                using var form = new MemoryAmie(pk);
                form.ShowDialog(Host);
                break;
            }
            case "medals" when pk is not PB7 && pk is ISuperTrainRegimen st:
            {
                using var form = new SuperTrainingEditor(st);
                form.ShowDialog(Host);
                break;
            }
            // PKMEditor's move flag buttons (B_Records_Click, B_MoveShop_Click, B_PlusRecord_Click)
            case "records" when pk is ITechRecord records:
            {
                using var form = new TechRecordEditor(records, pk);
                form.ShowDialog(Host);
                break;
            }
            case "moveshop" when pk is IMoveShop8Mastery shop:
            {
                using var form = new MoveShopEditor(shop, shop, pk);
                form.ShowDialog(Host);
                break;
            }
            case "plus" when pk is IPlusRecord plus && pk.PersonalInfo is IPermitPlus permit:
            {
                using var form = new PlusRecordEditor(plus, permit, pk);
                form.ShowDialog(Host);
                break;
            }
            default:
                Toast("info", "That editor isn't available for this Pokémon's format.");
                return GetEditorState();
        }
        if (!pk.Data.SequenceEqual(before))
            EditorSvc.ChangedExternally(); // also rechecks legality
        return GetEditorState();
    }
}
