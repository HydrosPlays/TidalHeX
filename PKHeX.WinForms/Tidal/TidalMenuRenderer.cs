using System.Drawing;
using System.Windows.Forms;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Menu/ToolStrip renderer for the Tidal theme. Installed globally via <see cref="ToolStripManager.Renderer"/>.
/// </summary>
public sealed class TidalMenuRenderer() : ToolStripProfessionalRenderer(new TidalColorTable())
{
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is MenuStrip or StatusStrip)
        {
            // Top-level bars blend with the window backdrop.
            using var b = new SolidBrush(Color.FromArgb(90, TidalPalette.Strip));
            e.Graphics.FillRectangle(b, e.AffectedBounds);
            return;
        }
        base.OnRenderToolStripBackground(e);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? TidalPalette.Text : TidalPalette.TextMuted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = TidalPalette.AccentSoft;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        if (!item.Selected && !(item is ToolStripMenuItem { DropDown.Visible: true }))
            return;
        var r = new Rectangle(2, 1, item.Width - 4, item.Height - 2);
        TidalPaint.FillRounded(e.Graphics, r, 4, TidalPalette.MenuHover);
        TidalPaint.StrokeRounded(e.Graphics, r, 4, TidalPalette.Accent);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(Color.FromArgb(90, TidalPalette.Accent));
        e.Graphics.DrawLine(pen, 28, y, e.Item.Width - 4, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        r.Inflate(2, 2);
        TidalPaint.FillRounded(e.Graphics, r, 3, TidalPalette.Selection);
        TidalPaint.StrokeRounded(e.Graphics, r, 3, TidalPalette.Accent);
        base.OnRenderItemCheck(e);
    }
}

internal sealed class TidalColorTable : ProfessionalColorTable
{
    public TidalColorTable() => UseSystemColors = false;

    public override Color ToolStripDropDownBackground => TidalPalette.Menu;
    public override Color ImageMarginGradientBegin => TidalPalette.Menu;
    public override Color ImageMarginGradientMiddle => TidalPalette.Menu;
    public override Color ImageMarginGradientEnd => TidalPalette.Menu;
    public override Color MenuBorder => TidalPalette.MenuBorder;
    public override Color MenuItemBorder => TidalPalette.Accent;
    public override Color MenuItemSelected => TidalPalette.MenuHover;
    public override Color MenuItemSelectedGradientBegin => TidalPalette.MenuHover;
    public override Color MenuItemSelectedGradientEnd => TidalPalette.MenuHover;
    public override Color MenuItemPressedGradientBegin => TidalPalette.Selection;
    public override Color MenuItemPressedGradientMiddle => TidalPalette.Selection;
    public override Color MenuItemPressedGradientEnd => TidalPalette.Selection;
    public override Color MenuStripGradientBegin => TidalPalette.Strip;
    public override Color MenuStripGradientEnd => TidalPalette.Strip;
    public override Color ToolStripGradientBegin => TidalPalette.Strip;
    public override Color ToolStripGradientMiddle => TidalPalette.Strip;
    public override Color ToolStripGradientEnd => TidalPalette.Strip;
    public override Color ToolStripBorder => TidalPalette.Strip;
    public override Color StatusStripGradientBegin => TidalPalette.Strip;
    public override Color StatusStripGradientEnd => TidalPalette.Strip;
    public override Color SeparatorDark => TidalPalette.MenuBorder;
    public override Color SeparatorLight => TidalPalette.Menu;
    public override Color CheckBackground => TidalPalette.Selection;
    public override Color CheckSelectedBackground => TidalPalette.MenuHover;
    public override Color CheckPressedBackground => TidalPalette.Selection;
    public override Color ButtonSelectedHighlight => TidalPalette.MenuHover;
    public override Color ButtonSelectedGradientBegin => TidalPalette.MenuHover;
    public override Color ButtonSelectedGradientEnd => TidalPalette.MenuHover;
    public override Color ButtonSelectedBorder => TidalPalette.Accent;
}
