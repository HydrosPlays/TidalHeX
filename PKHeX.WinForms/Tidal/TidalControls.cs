using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Transparent container that draws a rounded, translucent "card" behind its children.
/// </summary>
public class TidalCard : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 10;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Color.FromArgb(150, TidalPalette.Field);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Outline { get; set; } = true;

    public TidalCard()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        ForeColor = TidalPalette.Text;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        TidalPaint.FillRounded(g, r, Radius, Fill);
        if (Outline)
            TidalPaint.StrokeRounded(g, r, Radius, Color.FromArgb(110, TidalPalette.PanelEdge));
    }
}

/// <summary>
/// Title bar for a Tidal window: icon glyph + title on the left, optional status text on the right, underline.
/// </summary>
public sealed class TidalHeader : Control
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Status { get; set { field = value; Invalidate(); } } = string.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? Icon { get; set { field = value; Invalidate(); } }

    public TidalHeader()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        ForeColor = TidalPalette.Text;
        Height = 48;
        Dock = DockStyle.Top;
        Font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Point);
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int x = 16;
        int mid = Height / 2;
        if (Icon is { } icon)
        {
            g.DrawImage(icon, x, mid - (icon.Height / 2), icon.Width, icon.Height);
            x += icon.Width + 10;
        }
        else
        {
            // Four-square "all software" glyph.
            using var pen = new Pen(TidalPalette.Text, 1.6f);
            const int s = 8;
            g.DrawRectangle(pen, x, mid - s - 1, s, s);
            g.DrawRectangle(pen, x + s + 3, mid - s - 1, s, s);
            g.DrawRectangle(pen, x, mid + 2, s, s);
            g.DrawRectangle(pen, x + s + 3, mid + 2, s, s);
            x += (s * 2) + 14;
        }

        var textRect = new Rectangle(x, 0, Width - x - 16, Height);
        TextRenderer.DrawText(g, Text, Font, textRect, TidalPalette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        if (Status.Length != 0)
        {
            using var small = new Font("Segoe UI", 10f);
            TextRenderer.DrawText(g, Status, small, textRect, TidalPalette.TextSoft, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);
        }

        using var line = new Pen(Color.FromArgb(160, TidalPalette.PanelEdge), 1f);
        g.DrawLine(line, 12, Height - 2, Width - 12, Height - 2);
    }
}

/// <summary>
/// A controller-style hint bar: glyphs with labels, clickable, docked at the bottom of a window.
/// </summary>
public sealed class TidalFooter : Control
{
    public sealed record Hint(string Glyph, string Label, Action Action);

    private readonly List<Hint> _hints = [];
    private readonly List<(Rectangle Bounds, Hint Hint)> _hitRects = [];
    private int _hover = -1;
    private readonly Font _glyphFont = new("Segoe UI", 8f, FontStyle.Bold);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string LeftText { get; set { field = value; Invalidate(); } } = string.Empty;

    public TidalFooter()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        ForeColor = TidalPalette.Text;
        Height = 36;
        Dock = DockStyle.Bottom;
        Font = new Font("Segoe UI", 10f);
    }

    public void SetHints(params Hint[] hints)
    {
        _hints.Clear();
        _hints.AddRange(hints);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var line = new Pen(Color.FromArgb(160, TidalPalette.PanelEdge), 1f))
            g.DrawLine(line, 12, 1, Width - 12, 1);

        // Console glyph on the left.
        var mid = Height / 2;
        var body = new Rectangle(16, mid - 8, 34, 16);
        TidalPaint.StrokeRounded(g, body, 4, TidalPalette.Text, 1.6f);
        using (var b = new SolidBrush(TidalPalette.Text))
        {
            g.FillRectangle(b, body.X + 3, body.Y + 3, 5, body.Height - 6);
            g.FillRectangle(b, body.Right - 8, body.Y + 3, 5, body.Height - 6);
        }
        // Hints, right-aligned; any that would collide with the console glyph are skipped.
        _hitRects.Clear();
        const int leftEdge = 60;
        int x = Width - 16;
        for (int i = _hints.Count - 1; i >= 0; i--)
        {
            var h = _hints[i];
            var labelSize = TextRenderer.MeasureText(h.Label, Font);
            var glyphWidth = Math.Max(18, TextRenderer.MeasureText(h.Glyph, _glyphFont).Width + 6);
            var total = glyphWidth + 6 + labelSize.Width;
            if (x - total < leftEdge)
                break;
            x -= total;
            var bounds = new Rectangle(x - 4, 4, total + 8, Height - 8);
            if (i == _hover)
                TidalPaint.FillRounded(g, bounds, 6, Color.FromArgb(70, TidalPalette.Accent));
            TidalPaint.DrawButtonGlyph(g, h.Glyph, new Point(x, mid - 9), 18, _glyphFont);
            TextRenderer.DrawText(g, h.Label, Font, new Point(x + glyphWidth + 6, mid - (labelSize.Height / 2)), TidalPalette.Text);
            _hitRects.Add((bounds, h));
            x -= 18;
        }

        // Left text gets whatever room the hints leave.
        var room = x - leftEdge - 12;
        if (LeftText.Length != 0 && room > 40)
            TextRenderer.DrawText(g, LeftText, Font, new Rectangle(leftEdge, 0, room, Height), TidalPalette.TextSoft, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        for (int i = 0; i < _hitRects.Count; i++)
        {
            if (!_hitRects[i].Bounds.Contains(e.Location))
                continue;
            hover = _hints.IndexOf(_hitRects[i].Hint);
            break;
        }
        if (hover == _hover)
            return;
        _hover = hover;
        Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        foreach (var (bounds, hint) in _hitRects)
        {
            if (!bounds.Contains(e.Location))
                continue;
            hint.Action();
            return;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _glyphFont.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Rounded button. <see cref="Primary"/> buttons are filled with the accent color.
/// </summary>
public sealed class TidalButton : Button
{
    private bool _hover;
    private bool _down;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set { field = value; Invalidate(); } }

    public TidalButton()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        ForeColor = TidalPalette.Text;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Height = 34;
        Font = new Font("Segoe UI", 10f, FontStyle.Regular);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // Paint the parent behind us so the rounded corners are see-through.
        InvokePaintBackground(this, e);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(1, 1, Width - 3, Height - 3);
        var radius = Height / 2f - 1;

        Color fill, text;
        if (!Enabled)
        {
            fill = Color.FromArgb(120, TidalPalette.ButtonPressed);
            text = TidalPalette.TextMuted;
        }
        else if (Primary)
        {
            fill = _down ? TidalPalette.AccentSoft : _hover ? Color.White : TidalPalette.Accent;
            text = TidalPalette.BackdropBottom;
        }
        else
        {
            fill = _down ? TidalPalette.ButtonPressed : _hover ? TidalPalette.ButtonHover : TidalPalette.Button;
            text = TidalPalette.Text;
        }

        TidalPaint.FillRounded(g, r, radius, fill);
        if (Enabled && !Primary)
            TidalPaint.StrokeRounded(g, r, radius, _hover ? TidalPalette.Accent : Color.FromArgb(150, TidalPalette.ButtonBorder));
        if (Focused && ShowFocusCues)
            TidalPaint.DrawSelectionRing(g, r, radius);

        TextRenderer.DrawText(g, Text, Font, r, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>
/// Pill-shaped toggle used for filter chips.
/// </summary>
public sealed class TidalChip : CheckBox
{
    private bool _hover;

    public TidalChip()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        ForeColor = TidalPalette.Text;
        Appearance = Appearance.Button;
        FlatStyle = FlatStyle.Flat; // Standard is system-drawn in dark mode, which would bypass OnPaint.
        FlatAppearance.BorderSize = 0;
        AutoSize = false;
        Height = 28;
        Font = new Font("Segoe UI", 9f);
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 6, 6);
    }

    public void FitToText() => Width = TextRenderer.MeasureText(Text, Font).Width + 26;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        InvokePaintBackground(this, e);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(1, 1, Width - 3, Height - 3);
        var radius = Height / 2f - 1;
        if (Checked)
        {
            TidalPaint.FillRounded(g, r, radius, TidalPalette.Selection);
            TidalPaint.StrokeRounded(g, r, radius, TidalPalette.Accent, 2f);
        }
        else
        {
            TidalPaint.FillRounded(g, r, radius, Color.FromArgb(_hover ? 150 : 90, TidalPalette.Field));
            TidalPaint.StrokeRounded(g, r, radius, Color.FromArgb(_hover ? 200 : 90, TidalPalette.ButtonBorder));
        }
        var color = Checked ? TidalPalette.Accent : TidalPalette.TextSoft;
        TextRenderer.DrawText(g, Text, Font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

/// <summary>
/// "Any / Yes / No" segmented selector.
/// </summary>
public sealed class TidalTriState : Control
{
    private static readonly string[] Labels = ["Any", "Yes", "No"];
    private int _index;
    private int _hover = -1;

    public event EventHandler? ValueChanged;

    /// <summary> null = Any, true = Yes, false = No. </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool? Value
    {
        get => _index switch { 1 => true, 2 => false, _ => null };
        set
        {
            var index = value switch { true => 1, false => 2, _ => 0 };
            if (index == _index)
                return;
            _index = index;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public TidalTriState()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        Height = 28;
        Font = new Font("Segoe UI", 9f);
        Cursor = Cursors.Hand;
    }

    private Rectangle Segment(int i)
    {
        var w = (Width - 2) / Labels.Length;
        return new Rectangle(1 + (i * w), 1, w - 2, Height - 3);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = new Rectangle(0, 0, Width - 1, Height - 1);
        TidalPaint.FillRounded(g, outer, Height / 2f, Color.FromArgb(110, TidalPalette.Field));
        for (int i = 0; i < Labels.Length; i++)
        {
            var r = Segment(i);
            if (i == _index)
            {
                TidalPaint.FillRounded(g, r, r.Height / 2f, TidalPalette.Selection);
                TidalPaint.StrokeRounded(g, r, r.Height / 2f, TidalPalette.Accent, 2f);
            }
            else if (i == _hover)
            {
                TidalPaint.FillRounded(g, r, r.Height / 2f, Color.FromArgb(60, TidalPalette.Accent));
            }
            TextRenderer.DrawText(g, Labels[i], Font, r, i == _index ? TidalPalette.Accent : TidalPalette.TextSoft, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        for (int i = 0; i < Labels.Length; i++)
        {
            if (Segment(i).Contains(e.Location))
                hover = i;
        }
        if (hover == _hover)
            return;
        _hover = hover;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        for (int i = 0; i < Labels.Length; i++)
        {
            if (!Segment(i).Contains(e.Location))
                continue;
            Value = i switch { 1 => true, 2 => false, _ => null };
            return;
        }
    }
}

/// <summary>
/// Small caption label used above filter inputs.
/// </summary>
public sealed class TidalCaption : Label
{
    public TidalCaption(string text)
    {
        Text = text;
        AutoSize = false;
        Height = 20;
        Dock = DockStyle.Top;
        BackColor = Color.Transparent;
        ForeColor = TidalPalette.TextSoft;
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        TextAlign = ContentAlignment.BottomLeft;
    }
}
