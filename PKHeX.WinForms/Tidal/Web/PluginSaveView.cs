using System.Windows.Forms;
using PKHeX.Core;

namespace PKHeX.WinForms.Tidal.Web;

/// <summary>
/// The save view plugins receive (SAVEditor in the classic window).
/// </summary>
/// <remarks>
/// Public, with SAVEditor's member names, because plugins reach past <see cref="ISaveFileProvider"/> with <c>dynamic</c>:
/// PluginPile reads <c>SortMenu</c> and <c>menu.mnuVSD</c> to add entries to the box and slot right-click menus.
/// </remarks>
public sealed class PluginSaveView : ISaveFileProvider
{
    private readonly WebApi Api;

    internal PluginSaveView(WebApi api) => Api = api;

    public SaveFile SAV => Api.Session.SAV;
    public int CurrentBox => Api.Session.CurrentBox;
    public void ReloadSlots() => Api.ReloadSlotsForPlugin();

    /// <summary> SAVEditor.SortMenu: the box tab's right-click menu. </summary>
    public PluginBoxMenu SortMenu { get; } = new();

#pragma warning disable IDE1006 // SAVEditor's field name
    /// <summary> SAVEditor.menu: the slot right-click menu. </summary>
    public PluginSlotMenu menu { get; } = new();
#pragma warning restore IDE1006
}

/// <summary> Stand-in for BoxMenuStrip. Plugins find other PKHeX.WinForms types through its assembly. </summary>
public sealed class PluginBoxMenu : ContextMenuStrip;

/// <summary> Stand-in for ContextMenuSAV. </summary>
public sealed class PluginSlotMenu
{
#pragma warning disable IDE1006 // ContextMenuSAV's field name
    public ContextMenuStrip mnuVSD { get; } = new();
#pragma warning restore IDE1006
}
