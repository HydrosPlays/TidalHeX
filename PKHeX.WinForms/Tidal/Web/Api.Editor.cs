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
    }

    internal EditorState GetEditorState() => EditorSvc.GetState($"/sprite/editor?v={Session.EditorStamp}");

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
            default:
                Toast("info", "That editor isn't available for this Pokémon's format.");
                return GetEditorState();
        }
        if (!pk.Data.SequenceEqual(before))
        {
            Session.EditorDirty = true;
            Session.TouchEditor();
        }
        return GetEditorState();
    }
}
