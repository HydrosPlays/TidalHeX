using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PKHeX.Core;
using static PKHeX.Core.MessageStrings;

namespace PKHeX.WinForms.Tidal.Web;

// plugins.*: PKHeX plugins from the plugins folder, loaded like the classic window does (Main.AttachPlugins).
// Plugins add their commands to a hidden copy of PKHeX's menu bar; the page lists those commands under Save Tools.
internal sealed partial class WebApi
{
    private static readonly List<IPlugin> Plugins = [];
    private MenuStrip? PluginMenu;
    private readonly Dictionary<string, ToolStripItem> PluginItems = [];
    private readonly Dictionary<ToolStripItem, string> PluginItemIds = [];

    private void RegisterPlugins()
    {
        Bridge.Register("plugins.list", _ => GetPluginMenu());
        Bridge.Register("plugins.run", c => RunPluginCommand(c.Get<IdArgs>().Id));
    }

    /// <summary> Main.AttachPlugins, with web stand-ins for the save view, the Pokémon editor and the menu bar. </summary>
    public void AttachPlugins()
    {
        if (!Settings.Startup.PluginLoadEnable || Plugins.Count != 0)
            return;
        var folder = Settings.LocalResources.GetPluginPath();
        if (!Directory.Exists(folder))
            return;

        try
        {
            PluginLoader.LoadPlugins(folder, Plugins, Settings.Startup.PluginLoadMerged);
        }
        catch (InvalidCastException c)
        {
            WinFormsUtil.Error(MsgPluginFailLoad, c);
            return;
        }
        if (Plugins.Count == 0)
            return;

        LoadPKHeXAssemblies();
        PluginMenu = CreatePluginMenu();
        var saveView = new PluginSaveView(this);
        var editor = new WebPKMView(this);
        foreach (var p in Plugins.OrderBy(z => z.Priority).ToList())
        {
            try
            {
                // Exactly the classic window's arguments: PluginPile, for one, parents its windows to a Form if it is given one.
                p.Initialize(saveView, editor, PluginMenu, Program.CurrentVersion);
            }
            catch (Exception ex)
            {
                WinFormsUtil.Error(MsgPluginFailLoad, ex);
                Plugins.Remove(p);
            }
        }
    }

    /// <summary>
    /// Plugins find PKHeX's libraries among the loaded assemblies (PluginPile's DrawingUtil does, in a static constructor).
    /// The classic window loads them all at startup; the web UI only when it first draws with them.
    /// </summary>
    private static void LoadPKHeXAssemblies()
    {
        foreach (var name in typeof(WebApi).Assembly.GetReferencedAssemblies())
        {
            if (name.Name?.StartsWith("PKHeX", StringComparison.Ordinal) != true)
                continue;
            try { System.Reflection.Assembly.Load(name); }
            catch (Exception ex) { Debug.WriteLine($"Unable to load {name.Name}: {ex.Message}"); }
        }
    }

    /// <summary> The classic menu bar's top-level menus, which plugins look up by name to add their commands. </summary>
    private MenuStrip CreatePluginMenu()
    {
        var strip = new MenuStrip { Name = "menuStrip1", Visible = false };
        strip.Items.AddRange(
        [
            new ToolStripMenuItem("File") { Name = "Menu_File" },
            new ToolStripMenuItem("Tools") { Name = "Menu_Tools" },
            new ToolStripMenuItem("Options") { Name = "Menu_Options" },
        ]);
        Host.Controls.Add(strip); // gives plugin windows the main window as their owner (FindForm)
        return strip;
    }

    private static void NotifyPluginsSaveLoaded()
    {
        foreach (var p in Plugins)
        {
            try { p.NotifySaveLoaded(); }
            catch (Exception ex) { Debug.WriteLine($"Plugin {p.Name} failed on save load: {ex.Message}"); }
        }
    }

    private static void NotifyPluginsLanguageChanged(string lang)
    {
        foreach (var p in Plugins)
        {
            try { p.NotifyDisplayLanguageChanged(lang); }
            catch (Exception ex) { Debug.WriteLine($"Plugin {p.Name} failed on language change: {ex.Message}"); }
        }
    }

    /// <summary> Main.OpenFromPath: a plugin may take over opening a file. </summary>
    private static bool TryPluginLoadFile(string path)
    {
        foreach (var p in Plugins)
        {
            try
            {
                if (p.TryLoadFile(path))
                    return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Plugin {p.Name} failed to open {path}: {ex.Message}");
            }
        }
        return false;
    }

    private PluginMenuDto GetPluginMenu()
    {
        if (PluginMenu is null)
            return new PluginMenuDto([], []);

        var items = new List<PluginItemDto>();
        foreach (var top in PluginMenu.Items.OfType<ToolStripMenuItem>())
        {
            foreach (ToolStripItem item in top.DropDownItems)
            {
                if (GetPluginItem(item) is { } dto)
                    items.Add(dto);
            }
        }
        return new PluginMenuDto(Plugins.Select(z => z.Name).Distinct().ToList(), items);
    }

    /// <summary> A plugin's menu entry; submenus become groups. Hidden entries (plugins hide those that don't fit the save) are left out. </summary>
    private PluginItemDto? GetPluginItem(ToolStripItem item)
    {
        if (item is ToolStripSeparator || !item.Available)
            return null;

        List<PluginItemDto> children = [];
        if (item is ToolStripDropDownItem { HasDropDownItems: true } menu)
        {
            foreach (ToolStripItem child in menu.DropDownItems)
            {
                if (GetPluginItem(child) is { } dto)
                    children.Add(dto);
            }
            if (children.Count == 0)
                return null;
        }

        var text = CleanMenuText(item.Text);
        if (text.Length == 0)
            return null;
        return new PluginItemDto(GetPluginItemId(item), text, item.ToolTipText ?? string.Empty, item.Enabled, item.Image is not null, children);
    }

    private string GetPluginItemId(ToolStripItem item)
    {
        if (PluginItemIds.TryGetValue(item, out var id))
            return id;
        id = $"p{PluginItemIds.Count + 1}";
        PluginItemIds[item] = id;
        PluginItems[id] = item;
        return id;
    }

    /// <summary> Menu text without mnemonics ("&amp;Import" → "Import", "&amp;&amp;" → "&amp;"). </summary>
    private static string CleanMenuText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        return text.Replace("&&", "\0").Replace("&", "").Replace("\0", "&").Trim().TrimEnd('.').Trim();
    }

    /// <summary> Clicks the plugin's menu entry, as choosing it in the classic Tools menu would. </summary>
    private bool RunPluginCommand(string id)
    {
        if (!PluginItems.TryGetValue(id, out var item) || !item.Available)
            throw new ArgumentException("That plugin command isn't available; reopen Save Tools.");
        if (!item.Enabled)
            throw new InvalidOperationException("That plugin command isn't available for this save.");
        try
        {
            item.PerformClick();
        }
        finally
        {
            // Plugins write straight into the save and the editor; refresh like after a classic tool.
            RefreshAllSlots();
        }
        return true;
    }

    /// <summary> A plugin entry's own icon, if it has one. </summary>
    internal Image? GetPluginIcon(string id)
    {
        if (!PluginItems.TryGetValue(id, out var item) || item.Image is not { } image)
            return null;
        return new Bitmap(image); // the menu keeps its image; the route disposes what it gets
    }

    /// <summary> <see cref="ISaveFileProvider.ReloadSlots"/>: plugins ask for a reload after writing slots. </summary>
    internal void ReloadSlotsForPlugin()
    {
        if (Host.InvokeRequired)
        {
            Host.BeginInvoke(ReloadSlotsForPlugin);
            return;
        }
        SAV.State.Edited = true;
        RefreshAllSlots();
    }
}
