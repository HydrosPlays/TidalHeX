using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PKHeX.WinForms.Controls;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Applies the Tidal look to WinForms controls.
/// </summary>
/// <remarks>
/// Only colors that are still system defaults are replaced, so PKHeX's own semantic colors
/// (legality warnings, stat coloring, slot highlights, etc.) are preserved.
/// </remarks>
public static class TidalTheme
{
    /// <summary> True once <see cref="Initialize"/> has been called. </summary>
    public static bool Enabled { get; private set; }

    private static readonly HashSet<Form> ThemedForms = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<Control> Themed = new(ReferenceEqualityComparer.Instance);
    private static readonly MethodInfo? SetStyleMethod = typeof(Control).GetMethod("SetStyle", BindingFlags.NonPublic | BindingFlags.Instance);

    public static void Initialize()
    {
        Enabled = true;
        ToolStripManager.Renderer = new TidalMenuRenderer();
        TidalWindowHook.Install();
    }

    /// <summary>
    /// Themes a form and all of its current and future child controls. Safe to call multiple times.
    /// </summary>
    /// <param name="form">Form to theme.</param>
    /// <param name="force">Theme even when the global theme is disabled (used by the Tidal-native windows).</param>
    public static void Apply(Form form, bool force = false)
    {
        if (!Enabled && !force)
            return;
        lock (ThemedForms)
        {
            if (!ThemedForms.Add(form))
                return;
        }
        form.Disposed += (_, _) => { lock (ThemedForms) ThemedForms.Remove(form); };

        form.SuspendLayout();
        try
        {
            if (form is not PokePreview)
                AttachSurface(form, TidalPaint.Surface.Backdrop);
            if (form.ShowIcon)
                form.Icon = TidalAssets.AppIcon;
            SetFore(form, TidalPalette.Text);
            ApplyTitleBar(form);
            form.HandleCreated += (_, _) => ApplyTitleBar(form);
            foreach (Control c in form.Controls)
                ApplyControl(c);
            form.ControlAdded += (_, e) => { if (e.Control is { } c) ApplyControl(c); };
        }
        finally
        {
            form.ResumeLayout(true);
        }
    }

    /// <summary>
    /// Themes a single control subtree (used for controls added after the form was themed).
    /// </summary>
    public static void ApplyControl(Control c)
    {
        lock (Themed)
        {
            if (!Themed.Add(c))
                return;
        }
        c.Disposed += (_, _) => { lock (Themed) Themed.Remove(c); };

        try
        {
            ApplySingle(c);
        }
        catch
        {
            // A control that rejects a color should never take down the program.
        }

        if (!ShouldRecurse(c))
            return;
        foreach (Control child in c.Controls)
            ApplyControl(child);
        c.ControlAdded += (_, e) => { if (e.Control is { } x) ApplyControl(x); };
    }

    private static bool ShouldRecurse(Control c) => c is not (PropertyGrid or DataGridView or ComboBox or TextBoxBase or ListView or TreeView or UpDownBase);

    private static void ApplySingle(Control c)
    {
        if (c.GetType().Namespace?.StartsWith(typeof(TidalTheme).Namespace!, StringComparison.Ordinal) == true)
            return; // Tidal controls style themselves.

        switch (c)
        {
            case PictureBox:
            case PokeGrid:
                return; // sprites and box wallpapers keep their own look
            case Form f:
                Apply(f);
                return;
            case VerticalTabControl vtc:
                AttachSmoothSwitch(vtc);
                vtc.Invalidate();
                return;
            case TabControl tc:
                AttachTabPainter(tc);
                AttachSmoothSwitch(tc);
                return;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                SetStyleMethod?.Invoke(page, [ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true]);
                // No debounce: a page often gets its real size as it is first shown, and a delayed re-render made the
                // background visibly jump right after the tab appeared. Same-size pages share one cached bitmap anyway.
                AttachSurface(page, TidalPaint.Surface.Panel, debounce: false);
                SetFore(page, TidalPalette.Text);
                return;
            case PropertyGrid pg:
                ApplyPropertyGrid(pg);
                return;
            case DataGridView dgv:
                ApplyDataGrid(dgv);
                return;
            case ToolStrip ts:
                ts.RenderMode = ToolStripRenderMode.ManagerRenderMode;
                if (ts.BackColor.IsSystemColor)
                    ts.BackColor = TidalPalette.Strip;
                ts.ForeColor = TidalPalette.Text;
                return;
            case CheckBox { Appearance: Appearance.Button } or RadioButton { Appearance: Appearance.Button }:
                ApplyButton((ButtonBase)c); // toggle buttons: FlatAppearance is honored for these
                return;
            case Button b:
                if (!HasCustomBack(b)) // buttons whose color is state (e.g. Frontier symbols) keep the stock look
                    TidalSkin.SkinButton(b);
                return;
            case LinkLabel ll:
                ll.LinkColor = TidalPalette.AccentSoft;
                ll.ActiveLinkColor = Color.White;
                ll.VisitedLinkColor = TidalPalette.AccentSoft;
                MakeTransparent(ll);
                return;
            case CheckBox or RadioButton:
                MakeTransparent(c);
                SetFore(c, TidalPalette.Text);
                TidalSkin.SkinCheck((ButtonBase)c);
                return;
            case GroupBox gb:
                MakeTransparent(gb);
                SetFore(gb, TidalPalette.Text);
                TidalSkin.SkinGroupBox(gb);
                return;
            case Label:
                MakeTransparent(c);
                SetFore(c, TidalPalette.Text);
                return;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Popup; // same value WinFormsTranslator.ReformatDark uses (both are the flat adapter)
                ApplyField(cb);
                TidalSkin.SkinCombo(cb);
                return;
            case TextBoxBase tb:
                ApplyField(tb);
                TidalSkin.SkinTextBox(tb);
                return;
            case NumericUpDown nud:
                ApplyField(nud);
                TidalSkin.SkinNumeric(nud);
                return;
            case ListBox or ListView or TreeView or UpDownBase:
                ApplyField(c);
                return;
            case ScrollBar or ProgressBar or TrackBar or DateTimePicker or WebBrowserBase:
                return;
            default:
                // Generic containers (Panel, SplitContainer, FlowLayoutPanel, TableLayoutPanel, UserControls).
                if (c.BackgroundImage is null)
                    MakeTransparent(c);
                SetFore(c, TidalPalette.Text);
                return;
        }
    }

    #region Helpers

    /// <summary>
    /// True if the property holds its own value rather than inheriting (ambient) or defaulting.
    /// BackColor/ForeColor are ambient in WinForms, so a child's reported color is often just its parent's.
    /// </summary>
    private static bool IsExplicit(Control c, string property)
    {
        try { return TypeDescriptor.GetProperties(c)[property]?.ShouldSerializeValue(c) ?? false; }
        catch { return true; } // be conservative: assume it was set on purpose
    }

    /// <summary>
    /// A background color PKHeX (or its designer files) chose deliberately, which the theme must keep.
    /// This includes an explicit <see cref="Color.Transparent"/>: some editors store state in button colors
    /// (e.g. Gen 3 Battle Frontier symbols read Transparent/Silver/Gold back when saving), so recoloring would corrupt saves.
    /// </summary>
    private static bool HasCustomBack(Control c) => IsExplicit(c, nameof(Control.BackColor)) && !c.BackColor.IsSystemColor;

    /// <summary> A text color PKHeX chose deliberately (warnings, stat colors, etc.), which the theme must keep. </summary>
    private static bool HasCustomFore(Control c) => IsExplicit(c, nameof(Control.ForeColor)) && !c.ForeColor.IsSystemColor;

    private static void MakeTransparent(Control c)
    {
        if (HasCustomBack(c))
            return; // includes controls that are already explicitly transparent
        try { c.BackColor = Color.Transparent; }
        catch (ArgumentException) { /* control does not support transparency */ }
    }

    private static void SetFore(Control c, Color color)
    {
        if (!HasCustomFore(c))
            c.ForeColor = color;
    }

    private static void ApplyField(Control c)
    {
        if (!HasCustomBack(c))
            c.BackColor = TidalPalette.Field;
        if (!HasCustomFore(c))
            c.ForeColor = TidalPalette.Text;

        // PKHeX resets colors after validation (ResetBackColor); put the field color back when that happens.
        c.BackColorChanged += (_, _) =>
        {
            if (!HasCustomBack(c) && c.BackColor != TidalPalette.Field)
                c.BackColor = TidalPalette.Field;
        };
        c.ForeColorChanged += (_, _) =>
        {
            if (!HasCustomFore(c) && c.ForeColor != TidalPalette.Text)
                c.ForeColor = TidalPalette.Text;
        };
    }

    private static void ApplyButton(ButtonBase b)
    {
        if (HasCustomBack(b))
            return;
        b.UseVisualStyleBackColor = false;
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = TidalPalette.Button;
        b.ForeColor = TidalPalette.Text;
        b.FlatAppearance.BorderColor = TidalPalette.ButtonBorder;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = TidalPalette.ButtonHover;
        b.FlatAppearance.MouseDownBackColor = TidalPalette.ButtonPressed;
        b.FlatAppearance.CheckedBackColor = TidalPalette.MenuHover;
    }

    private static void ApplyPropertyGrid(PropertyGrid pg)
    {
        pg.BackColor = TidalPalette.Strip;
        pg.ForeColor = TidalPalette.Text;
        pg.ViewBackColor = TidalPalette.Field;
        pg.ViewForeColor = TidalPalette.Text;
        pg.ViewBorderColor = TidalPalette.FieldBorder;
        pg.LineColor = TidalPalette.FieldAlt;
        pg.CategoryForeColor = TidalPalette.AccentSoft;
        pg.CategorySplitterColor = TidalPalette.FieldAlt;
        pg.HelpBackColor = TidalPalette.Strip;
        pg.HelpForeColor = TidalPalette.Text;
        pg.HelpBorderColor = TidalPalette.FieldBorder;
        pg.CommandsBackColor = TidalPalette.Strip;
        pg.CommandsForeColor = TidalPalette.Text;
        pg.SelectedItemWithFocusBackColor = TidalPalette.MenuHover;
        pg.SelectedItemWithFocusForeColor = TidalPalette.Text;
        pg.DisabledItemForeColor = TidalPalette.TextMuted;
        foreach (var strip in pg.Controls.OfType<ToolStrip>())
            strip.Renderer = new TidalMenuRenderer(); // PropertyGrid has no public ToolStripRenderer property
    }

    private static void ApplyDataGrid(DataGridView dgv)
    {
        dgv.EnableHeadersVisualStyles = false;
        dgv.BackgroundColor = TidalPalette.Strip;
        dgv.GridColor = TidalPalette.FieldAlt;
        dgv.BorderStyle = BorderStyle.None;

        var cell = dgv.DefaultCellStyle;
        cell.BackColor = TidalPalette.Field;
        cell.ForeColor = TidalPalette.Text;
        cell.SelectionBackColor = TidalPalette.MenuHover;
        cell.SelectionForeColor = TidalPalette.Text;

        // DoubleBufferedDataGridView sets RowsDefaultCellStyle in dark mode, which wins over DefaultCellStyle.
        var rowsStyle = dgv.RowsDefaultCellStyle;
        rowsStyle.BackColor = TidalPalette.Field;
        rowsStyle.ForeColor = TidalPalette.Text;
        rowsStyle.SelectionBackColor = TidalPalette.MenuHover;
        rowsStyle.SelectionForeColor = TidalPalette.Text;

        dgv.AlternatingRowsDefaultCellStyle.BackColor = TidalPalette.FieldAlt;
        dgv.AlternatingRowsDefaultCellStyle.ForeColor = TidalPalette.Text;

        var header = dgv.ColumnHeadersDefaultCellStyle;
        header.BackColor = TidalPalette.Strip;
        header.ForeColor = TidalPalette.AccentSoft;
        header.SelectionBackColor = TidalPalette.Strip;

        var rows = dgv.RowHeadersDefaultCellStyle;
        rows.BackColor = TidalPalette.Strip;
        rows.ForeColor = TidalPalette.AccentSoft;
        rows.SelectionBackColor = TidalPalette.MenuHover;
    }

    #endregion

    #region Surfaces

    private sealed class SurfaceEntry(Bitmap bitmap)
    {
        public Bitmap Bitmap { get; } = bitmap;
        public int References;
    }

    // Controls of the same size share one bitmap; each is released (and disposed at zero) when no control shows it.
    private static readonly Dictionary<(int W, int H, TidalPaint.Surface Kind), SurfaceEntry> SurfaceCache = [];

    private static Bitmap AcquireSurface(Size size, TidalPaint.Surface kind)
    {
        var key = (size.Width, size.Height, kind);
        lock (SurfaceCache)
        {
            if (!SurfaceCache.TryGetValue(key, out var entry))
                SurfaceCache[key] = entry = new SurfaceEntry(TidalPaint.RenderSurface(size, kind));
            entry.References++;
            return entry.Bitmap;
        }
    }

    private static void ReleaseSurface(Image? image, TidalPaint.Surface kind)
    {
        if (image is null)
            return;
        var key = (image.Width, image.Height, kind);
        lock (SurfaceCache)
        {
            if (!SurfaceCache.TryGetValue(key, out var entry) || !ReferenceEquals(entry.Bitmap, image))
                return;
            if (--entry.References > 0)
                return;
            SurfaceCache.Remove(key);
            entry.Bitmap.Dispose();
        }
    }

    /// <summary>
    /// Gives a control a painted Tidal background that tracks its size.
    /// Regeneration is debounced so live window resizing doesn't render a bitmap per intermediate size.
    /// </summary>
    public static void AttachSurface(Control c, TidalPaint.Surface kind, bool debounce = true)
    {
        c.BackColor = kind == TidalPaint.Surface.Backdrop ? TidalPalette.BackdropBottom : TidalPalette.PanelBottom;
        c.BackgroundImageLayout = ImageLayout.None;
        Image? current = null;
        var timer = new System.Windows.Forms.Timer { Interval = 120 };
        timer.Tick += (_, _) => { timer.Stop(); Update(); };
        Update();
        c.SizeChanged += (_, _) =>
        {
            if (current is null || !debounce)
            {
                Update(); // first real size (or no debounce): render right away
                return;
            }
            timer.Stop();
            timer.Start();
        };
        c.Disposed += (_, _) =>
        {
            timer.Dispose();
            ReleaseSurface(current, kind);
            current = null;
        };
        return;

        void Update()
        {
            if (c.IsDisposed)
                return;
            var size = c.ClientSize;
            if (size.Width <= 0 || size.Height <= 0)
                return;
            if (current is not null && current.Size == size)
                return;
            var next = AcquireSurface(size, kind);
            c.BackgroundImage = next;
            ReleaseSurface(current, kind);
            current = next;
        }
    }

    #endregion

    #region Tabs

    private static void AttachTabPainter(TabControl tc)
    {
        SetStyleMethod?.Invoke(tc, [ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true]);
        tc.Paint += (_, e) => PaintTabStrip(tc, e.Graphics);
        tc.SelectedIndexChanged += (_, _) => tc.Invalidate();
        tc.ControlAdded += (_, _) => tc.Invalidate();
        tc.ControlRemoved += (_, _) => tc.Invalidate();
        tc.Invalidate();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);

    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_COMPOSITED = 0x02000000;

    /// <summary>
    /// Shows a newly selected tab in one step. A tab page is made of dozens of (mostly transparent) child windows that
    /// paint one after another, so the screen showed the old page half-erased with new controls popping in for 60-80 ms.
    /// With WS_EX_COMPOSITED, Windows paints the tab control and all of its children off-screen and presents the result at once.
    /// </summary>
    private static void AttachSmoothSwitch(TabControl tc)
    {
        if (tc.IsHandleCreated)
            SetComposited(tc);
        tc.HandleCreated += (_, _) => SetComposited(tc);
    }

    private static void SetComposited(Control c)
    {
        var style = GetWindowLongPtr(c.Handle, GWL_EXSTYLE);
        if ((style & WS_EX_COMPOSITED) == 0)
            SetWindowLongPtr(c.Handle, GWL_EXSTYLE, style | WS_EX_COMPOSITED);
    }

    private static void PaintTabStrip(TabControl tc, Graphics g)
    {
        g.Clear(TidalPalette.Strip);
        for (int i = 0; i < tc.TabCount; i++)
        {
            Rectangle bounds;
            try { bounds = tc.GetTabRect(i); }
            catch { continue; }
            DrawTab(g, tc, tc.TabPages[i], bounds, i == tc.SelectedIndex, tc.Alignment);
        }
    }

    /// <summary>
    /// Draws a single tab header. Shared with <see cref="VerticalTabControl"/>.
    /// </summary>
    public static void DrawTab(Graphics g, TabControl tc, TabPage page, Rectangle bounds, bool selected, TabAlignment alignment, Color? pip = null)
    {
        var r = bounds;
        r.Inflate(-1, -1);
        if (selected)
        {
            TidalPaint.FillRounded(g, r, 5, TidalPalette.Selection);
            TidalPaint.DrawSelectionRing(g, r, 5);
        }
        else
        {
            TidalPaint.FillRounded(g, r, 5, Color.FromArgb(120, TidalPalette.Field));
        }

        var textRect = r;
        if (pip is { } color && selected)
        {
            // Accent bar on the leading edge, like a console settings menu.
            var bar = alignment is TabAlignment.Left or TabAlignment.Right
                ? new Rectangle(r.X + 3, r.Y + 4, 3, r.Height - 8)
                : new Rectangle(r.X + 4, r.Bottom - 5, r.Width - 8, 3);
            using var b = new SolidBrush(color);
            g.FillRectangle(b, bar);
            if (alignment is TabAlignment.Left or TabAlignment.Right)
                textRect = r with { X = r.X + 6, Width = r.Width - 6 };
        }

        if (page.ImageIndex >= 0 && tc.ImageList is { } list && page.ImageIndex < list.Images.Count)
        {
            // ImageList.Draw avoids the per-call Bitmap copy that list.Images[i] makes.
            var size = list.ImageSize;
            list.Draw(g, textRect.X + 4, textRect.Y + ((textRect.Height - size.Height) / 2), page.ImageIndex);
            textRect = textRect with { X = textRect.X + size.Width + 4, Width = textRect.Width - size.Width - 4 };
        }

        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(g, page.Text, tc.Font, textRect, selected ? TidalPalette.Accent : TidalPalette.TextSoft, flags);
    }

    #endregion

    #region Title bar

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    /// <summary>
    /// Asks Windows 11 to round the corners of a (borderless) window. No effect on older Windows.
    /// </summary>
    public static void RoundCorners(Form form)
    {
        if (!form.IsHandleCreated)
            return;
        try
        {
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        }
        catch
        {
            // Not supported.
        }
    }

    private static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated)
            return;
        try
        {
            var handle = form.Handle;
            int dark = 1;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int caption = ToColorRef(TidalPalette.Strip);
            DwmSetWindowAttribute(handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            int text = ToColorRef(TidalPalette.Text);
            DwmSetWindowAttribute(handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            int border = ToColorRef(TidalPalette.Accent);
            DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch
        {
            // Older Windows versions don't support these attributes.
        }
    }

    #endregion
}
