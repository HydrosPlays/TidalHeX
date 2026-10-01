using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Modern look for stock WinForms controls, applied at runtime without changing layout or any property PKHeX reads back.
/// </summary>
/// <remarks>
/// Everything here is an overlay drawn after the control's own painting (Paint events, or a NativeWindow after WM_PAINT/WM_NCPAINT).
/// Nothing changes sizes, fonts, BorderStyle or DropDownStyle (those recreate handles and shift layouts), and semantic
/// colors (validation, stat coloring, state stored in colors) are always honored because the overlays paint with the control's own colors.
/// </remarks>
internal static class TidalSkin
{
    private static float Scale(Control c) => c.DeviceDpi / 96f;

    #region Buttons

    /// <summary>
    /// Rounded button drawn over the stock (dark-mode) button, with hover/pressed/focus states.
    /// </summary>
    public static void SkinButton(Button b)
    {
        var hover = false;
        var down = false;
        b.MouseEnter += (_, _) => { hover = true; b.Invalidate(); };
        b.MouseLeave += (_, _) => { hover = down = false; b.Invalidate(); };
        b.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { down = true; b.Invalidate(); } };
        b.MouseUp += (_, _) => { down = false; b.Invalidate(); };
        b.EnabledChanged += (_, _) => b.Invalidate();
        b.Paint += (_, e) => PaintButton(b, e.Graphics, hover, down);
        b.Invalidate();
    }

    private static void PaintButton(Button b, Graphics g, bool hover, bool down)
    {
        var bounds = b.ClientRectangle;
        if (bounds.Width <= 2 || bounds.Height <= 2)
            return;
        ButtonRenderer.DrawParentBackground(g, bounds, b);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var s = Scale(b);
        var r = Rectangle.Inflate(bounds, -1, -1);
        r.Width -= 1;
        r.Height -= 1;
        var radius = Math.Min(8 * s, r.Height / 2f);

        Color fill, border;
        if (!b.Enabled)
        {
            fill = Color.FromArgb(110, TidalPalette.ButtonPressed);
            border = Color.FromArgb(60, TidalPalette.ButtonBorder);
        }
        else if (down)
        {
            fill = TidalPalette.ButtonPressed;
            border = TidalPalette.Accent;
        }
        else if (hover)
        {
            fill = TidalPalette.ButtonHover;
            border = TidalPalette.Accent;
        }
        else
        {
            fill = TidalPalette.Button;
            border = Color.FromArgb(140, TidalPalette.ButtonBorder);
        }
        TidalPaint.FillRounded(g, r, radius, fill);
        TidalPaint.StrokeRounded(g, r, radius, border);
        if (b.Focused)
            TidalPaint.StrokeRounded(g, Rectangle.Inflate(r, -2, -2), Math.Max(1, radius - 2), Color.FromArgb(180, TidalPalette.Accent));

        // Small icon-sized buttons (e.g. the shiny star) get almost no padding so their glyph isn't clipped.
        var padX = r.Width < 40 * s ? 0 : (int)(4 * s);
        var padY = r.Height < 24 * s ? 0 : (int)(2 * s);
        DrawButtonContent(g, b, Rectangle.Inflate(r, -padX, -padY), b.Enabled ? b.ForeColor : TidalPalette.TextMuted);
    }

    private static void DrawButtonContent(Graphics g, ButtonBase b, Rectangle area, Color textColor)
    {
        var img = b.Image;
        var text = b.Text;
        var flags = ToFlags(b.TextAlign) | (area.Width < 40 ? TextFormatFlags.NoClipping | TextFormatFlags.SingleLine : TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        flags |= TextFormatFlags.HidePrefix; // mnemonics stay functional; underline hidden like modern UIs
        if (!b.UseMnemonic)
            flags |= TextFormatFlags.NoPrefix;

        if (img is null)
        {
            area = FitTextArea(g, b, area, ref flags);
            TextRenderer.DrawText(g, text, b.Font, area, textColor, flags);
            return;
        }

        if (!b.Enabled)
        {
            // Draw a faded image for disabled buttons.
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.4f };
            attributes.SetColorMatrix(matrix);
            var ir = AlignImage(area, img.Size, text.Length == 0 ? ContentAlignment.MiddleCenter : b.ImageAlign, b, out var textArea);
            g.DrawImage(img, ir, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attributes);
            if (text.Length != 0)
                TextRenderer.DrawText(g, text, b.Font, textArea, textColor, flags);
            return;
        }

        var imageRect = AlignImage(area, img.Size, text.Length == 0 ? ContentAlignment.MiddleCenter : b.ImageAlign, b, out var remaining);
        g.DrawImage(img, imageRect);
        if (text.Length != 0)
            TextRenderer.DrawText(g, text, b.Font, remaining, textColor, flags);
    }

    /// <summary>
    /// Keeps a button's text on one line like the stock button does: the padding shrinks when the text is tight, and it
    /// only wraps when the button is tall enough for two lines (a one-line button would cut the second line off).
    /// </summary>
    private static Rectangle FitTextArea(Graphics g, ButtonBase b, Rectangle area, ref TextFormatFlags flags)
    {
        if ((flags & TextFormatFlags.WordBreak) == 0 || b.Text.Length == 0)
            return area;
        var measure = TextFormatFlags.SingleLine | TextFormatFlags.HidePrefix;
        var size = TextRenderer.MeasureText(g, b.Text, b.Font, new Size(int.MaxValue, int.MaxValue), measure);
        if (size.Width <= area.Width)
            return area; // fits as is
        var wide = Rectangle.Inflate(b.ClientRectangle, -2, 0) with { Y = area.Y, Height = area.Height };
        if (size.Width <= wide.Width || area.Height < size.Height * 2)
        {
            flags = (flags & ~(TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis)) | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping;
            return wide;
        }
        return area; // tall button: wrap
    }

    /// <summary> Places the image per <see cref="ButtonBase.TextImageRelation"/>, returning the area left for text. </summary>
    private static Rectangle AlignImage(Rectangle area, Size img, ContentAlignment align, ButtonBase b, out Rectangle textArea)
    {
        textArea = area;
        if (b.Text.Length != 0)
        {
            switch (b.TextImageRelation)
            {
                case TextImageRelation.ImageBeforeText:
                {
                    var textWidth = TextRenderer.MeasureText(b.Text, b.Font).Width;
                    var total = img.Width + 4 + textWidth;
                    var x = area.X + Math.Max(0, (area.Width - total) / 2);
                    var rImg = new Rectangle(x, area.Y + ((area.Height - img.Height) / 2), img.Width, img.Height);
                    textArea = new Rectangle(rImg.Right + 4, area.Y, Math.Max(1, area.Right - rImg.Right - 4), area.Height);
                    return rImg;
                }
                case TextImageRelation.ImageAboveText:
                {
                    var rImg = new Rectangle(area.X + ((area.Width - img.Width) / 2), area.Y + 2, img.Width, img.Height);
                    textArea = new Rectangle(area.X, rImg.Bottom + 2, area.Width, Math.Max(1, area.Bottom - rImg.Bottom - 2));
                    return rImg;
                }
            }
        }

        // Overlay (default): image at its own alignment, text across the whole area.
        int ix = align switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => area.X,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => area.Right - img.Width,
            _ => area.X + ((area.Width - img.Width) / 2),
        };
        int iy = align switch
        {
            ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => area.Y,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => area.Bottom - img.Height,
            _ => area.Y + ((area.Height - img.Height) / 2),
        };
        return new Rectangle(ix, iy, img.Width, img.Height);
    }

    private static TextFormatFlags ToFlags(ContentAlignment a)
    {
        var h = a switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft => TextFormatFlags.Left,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter,
        };
        var v = a switch
        {
            ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight => TextFormatFlags.Top,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
            _ => TextFormatFlags.VerticalCenter,
        };
        return h | v;
    }

    #endregion

    #region CheckBox / RadioButton

    /// <summary>
    /// Replaces the check/radio glyph with a rounded accent glyph. Text and layout are untouched.
    /// </summary>
    public static void SkinCheck(ButtonBase c)
    {
        if (c.Image is not null || c.FlatStyle == FlatStyle.System)
            return; // image checkboxes (ShinyLeaf) keep their own look; System style has no Paint event
        if (c is CheckBox { Appearance: Appearance.Button } or RadioButton { Appearance: Appearance.Button })
            return;
        c.FlatStyle = FlatStyle.Popup; // same value WinFormsTranslator.ReformatDark uses, so re-translation doesn't flip it
        c.Paint += (_, e) => PaintCheckGlyph(c, e.Graphics);
        c.EnabledChanged += (_, _) => c.Invalidate();
        c.Invalidate();
    }

    private static void PaintCheckGlyph(ButtonBase c, Graphics g)
    {
        var s = Scale(c);
        var align = c switch { CheckBox cb => cb.CheckAlign, RadioButton rb => rb.CheckAlign, _ => ContentAlignment.MiddleLeft };
        var native = (int)Math.Round(11 * s);
        var right = align is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight;
        var top = align is ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight;
        var x = right ? c.Width - native - (int)Math.Round(1 * s) : 0;
        var y = top ? (int)Math.Round(3 * s) : (c.Height - native) / 2;
        var nativeRect = new Rectangle(x, y, native, native);

        // Erase the stock glyph, then draw ours slightly larger around the same center.
        var erase = Rectangle.Inflate(nativeRect, (int)Math.Round(2 * s), (int)Math.Round(2 * s));
        erase.Intersect(c.ClientRectangle);
        ButtonRenderer.DrawParentBackground(g, erase, c);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var glyph = RectangleF.Inflate(nativeRect, 1.5f * s, 1.5f * s);
        var enabled = c.Enabled;
        var accent = enabled ? TidalPalette.Accent : Color.FromArgb(120, TidalPalette.AccentSoft);

        if (c is RadioButton rb2)
        {
            using (var brush = new SolidBrush(Color.FromArgb(enabled ? 170 : 90, TidalPalette.Field)))
                g.FillEllipse(brush, glyph);
            using (var pen = new Pen(rb2.Checked ? accent : Color.FromArgb(enabled ? 190 : 90, TidalPalette.TextSoft), 1.4f * s))
                g.DrawEllipse(pen, glyph);
            if (rb2.Checked)
            {
                var dot = RectangleF.Inflate(glyph, -glyph.Width * 0.28f, -glyph.Height * 0.28f);
                using var dotBrush = new SolidBrush(accent);
                g.FillEllipse(dotBrush, dot);
            }
            return;
        }

        var state = c is CheckBox cb2 ? cb2.CheckState : CheckState.Unchecked;
        var radius = 3.5f * s;
        using (var path = TidalPaint.RoundedRect(glyph, radius))
        {
            if (state == CheckState.Unchecked)
            {
                using var brush = new SolidBrush(Color.FromArgb(enabled ? 170 : 90, TidalPalette.Field));
                g.FillPath(brush, path);
                using var pen = new Pen(Color.FromArgb(enabled ? 190 : 90, TidalPalette.TextSoft), 1.3f * s);
                g.DrawPath(pen, path);
            }
            else
            {
                using var brush = new SolidBrush(accent);
                g.FillPath(brush, path);
            }
        }

        var mark = enabled ? TidalPalette.BackdropBottom : TidalPalette.Field;
        using var markPen = new Pen(mark, 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        if (state == CheckState.Checked)
        {
            g.DrawLines(markPen,
            [
                new PointF(glyph.X + (glyph.Width * 0.22f), glyph.Y + (glyph.Height * 0.52f)),
                new PointF(glyph.X + (glyph.Width * 0.42f), glyph.Y + (glyph.Height * 0.72f)),
                new PointF(glyph.X + (glyph.Width * 0.78f), glyph.Y + (glyph.Height * 0.3f)),
            ]);
        }
        else if (state == CheckState.Indeterminate)
        {
            var mid = glyph.Y + (glyph.Height / 2);
            g.DrawLine(markPen, glyph.X + (glyph.Width * 0.26f), mid, glyph.Right - (glyph.Width * 0.26f), mid);
        }
    }

    #endregion

    #region GroupBox

    /// <summary>
    /// Draws the group as a translucent rounded card with a header, keeping <see cref="Control.ForeColor"/> (which PKHeX uses as a state hint).
    /// </summary>
    public static void SkinGroupBox(GroupBox gb)
    {
        if (gb.FlatStyle == FlatStyle.System)
            return;
        gb.FlatStyle = FlatStyle.Popup; // matches ReformatDark
        gb.Paint += (_, e) => PaintGroupBox(gb, e.Graphics);
        gb.Invalidate();
    }

    private static void PaintGroupBox(GroupBox gb, Graphics g)
    {
        var bounds = gb.ClientRectangle;
        if (bounds.Width <= 4 || bounds.Height <= 4)
            return;
        ButtonRenderer.DrawParentBackground(g, bounds, gb);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var s = Scale(gb);
        var textSize = gb.Text.Length == 0 ? Size.Empty : TextRenderer.MeasureText(gb.Text, gb.Font);
        var top = textSize.Height / 2;
        var card = new Rectangle(0, top, bounds.Width - 1, bounds.Height - top - 1);
        var radius = 8 * s;
        TidalPaint.FillRounded(g, card, radius, Color.FromArgb(70, TidalPalette.Field));
        TidalPaint.StrokeRounded(g, card, radius, Color.FromArgb(100, TidalPalette.PanelEdge));
        if (textSize.IsEmpty)
            return;

        // Header pill that interrupts the border line.
        var pill = new Rectangle((int)(8 * s), 0, textSize.Width + (int)(8 * s), textSize.Height);
        ButtonRenderer.DrawParentBackground(g, pill, gb);
        TidalPaint.FillRounded(g, pill, pill.Height / 2f, Color.FromArgb(200, TidalPalette.Selection));
        var color = gb.Enabled ? gb.ForeColor : TidalPalette.TextMuted;
        TextRenderer.DrawText(g, gb.Text, gb.Font, pill, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    #endregion

    #region Native frames (ComboBox, TextBox, NumericUpDown)

    [DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint hdc);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(nint hwnd, nint rect, nint region, uint flags);

    private const int WM_PAINT = 0x000F;
    private const int WM_NCPAINT = 0x0085;
    private const int WM_NCDESTROY = 0x0082;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_FRAME = 0x0400;

    /// <summary>
    /// Attaches <paramref name="factory"/>'s NativeWindow to the control's handle, now and after every handle recreation.
    /// </summary>
    private static void AttachNative(Control c, Func<Control, NativeWindow> factory)
    {
        if (c.IsHandleCreated)
            factory(c);
        // Defer so WinForms' own OnHandleCreated work (dark-mode SetWindowTheme, child subclassing) runs first.
        c.HandleCreated += (_, _) => c.BeginInvoke(() => { if (!c.IsDisposed && c.IsHandleCreated) factory(c); });
    }

    private static Color FrameColor(Control c) => !c.Enabled
        ? Color.FromArgb(70, TidalPalette.FieldBorder)
        : c.ContainsFocus ? TidalPalette.Accent : Color.FromArgb(130, TidalPalette.FieldBorder);

    // ---------- ComboBox ----------

    public static void SkinCombo(ComboBox cb)
    {
        AttachNative(cb, c => new ComboFrame((ComboBox)c));
        cb.Enter += (_, _) => cb.Invalidate();
        cb.Leave += (_, _) => cb.Invalidate();
        cb.MouseEnter += (_, _) => cb.Invalidate();
        cb.MouseLeave += (_, _) => cb.Invalidate();
        cb.DropDownClosed += (_, _) => cb.Invalidate();
        cb.EnabledChanged += (_, _) => cb.Invalidate();
    }

    private sealed class ComboFrame : NativeWindow
    {
        private readonly ComboBox _cb;

        public ComboFrame(ComboBox cb)
        {
            _cb = cb;
            AssignHandle(cb.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT)
                Overpaint();
            else if (m.Msg == WM_NCDESTROY)
                ReleaseHandle();
        }

        private void Overpaint()
        {
            try
            {
                var cb = _cb;
                if (cb.IsDisposed || !cb.IsHandleCreated)
                    return;
                using var g = Graphics.FromHwnd(Handle);
                var s = cb.DeviceDpi / 96f;
                var client = cb.ClientRectangle;
                var arrowWidth = (int)Math.Round(18 * s);
                var arrow = new Rectangle(client.Right - arrowWidth, client.Y, arrowWidth, client.Height);
                var back = cb.Enabled ? cb.BackColor : Blend(cb.BackColor, TidalPalette.Selection, 0.5f);

                if (cb.DropDownStyle == ComboBoxStyle.DropDownList && cb.DrawMode == DrawMode.Normal)
                {
                    // Whole face: our fill + text, which also replaces dark mode's fixed grey for disabled lists.
                    using (var brush = new SolidBrush(back))
                        g.FillRectangle(brush, client);
                    var textRect = new Rectangle(client.X + (int)(4 * s), client.Y, client.Width - arrowWidth - (int)(6 * s), client.Height);
                    var fore = cb.Enabled ? cb.ForeColor : TidalPalette.TextMuted;
                    TextRenderer.DrawText(g, cb.Text, cb.Font, textRect, fore,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                else
                {
                    using var brush = new SolidBrush(back);
                    g.FillRectangle(brush, arrow);
                }

                // Chevron
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var cx = arrow.X + (arrow.Width / 2f);
                var cy = arrow.Y + (arrow.Height / 2f);
                var w = 4f * s;
                using (var pen = new Pen(cb.Enabled ? (cb.DroppedDown ? TidalPalette.Accent : TidalPalette.AccentSoft) : TidalPalette.TextMuted, 1.6f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, [new PointF(cx - w, cy - (w / 2)), new PointF(cx, cy + (w / 2)), new PointF(cx + w, cy - (w / 2))]);

                // Frame
                g.SmoothingMode = SmoothingMode.None;
                using var framePen = new Pen(FrameColor(cb));
                g.DrawRectangle(framePen, client.X, client.Y, client.Width - 1, client.Height - 1);
            }
            catch
            {
                // Painting must never throw into the message loop.
            }
        }
    }

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + ((b.R - a.R) * t)), (int)(a.G + ((b.G - a.G) * t)), (int)(a.B + ((b.B - a.B) * t)));

    // ---------- TextBox / MaskedTextBox ----------

    public static void SkinTextBox(TextBoxBase tb)
    {
        if (tb is MaskedTextBox mt && mt.PromptChar == '_')
            mt.PromptChar = ' '; // no "0_____" underscores; .Text never contains prompt characters (IncludeLiterals)
        if (tb is RichTextBox)
            return; // borderless already (ReformatDark)
        AttachNative(tb, c => new TextFrame((TextBoxBase)c));
        tb.Enter += (_, _) => RedrawFrame(tb);
        tb.Leave += (_, _) => RedrawFrame(tb);
        tb.EnabledChanged += (_, _) => RedrawFrame(tb);
    }

    private static void RedrawFrame(Control c)
    {
        if (c.IsHandleCreated)
            RedrawWindow(c.Handle, 0, 0, RDW_FRAME | RDW_INVALIDATE);
    }

    private sealed class TextFrame : NativeWindow
    {
        private readonly TextBoxBase _tb;

        public TextFrame(TextBoxBase tb)
        {
            _tb = tb;
            AssignHandle(tb.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_NCPAINT)
                PaintFrame();
            else if (m.Msg == WM_NCDESTROY)
                ReleaseHandle();
        }

        private void PaintFrame()
        {
            var tb = _tb;
            if (tb.IsDisposed || tb.BorderStyle == BorderStyle.None)
                return;
            var hdc = GetWindowDC(Handle);
            if (hdc == 0)
                return;
            try
            {
                using var g = Graphics.FromHdc(hdc);
                var w = tb.Width;
                var h = tb.Height;
                using var pen = new Pen(FrameColor(tb));
                g.DrawRectangle(pen, 0, 0, w - 1, h - 1);
                if (tb.BorderStyle == BorderStyle.Fixed3D)
                {
                    // The 3D border is two pixels; blend the inner one into the field.
                    using var inner = new Pen(tb.BackColor);
                    g.DrawRectangle(inner, 1, 1, w - 3, h - 3);
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                ReleaseDC(Handle, hdc);
            }
        }
    }

    // ---------- NumericUpDown ----------

    public static void SkinNumeric(NumericUpDown nud)
    {
        AttachNative(nud, c => new NumericFrame((NumericUpDown)c));
        nud.Enter += (_, _) => nud.Invalidate();
        nud.Leave += (_, _) => nud.Invalidate();
        nud.EnabledChanged += (_, _) => nud.Invalidate(true);

        if (nud.Controls.Count == 0)
            return;
        var buttons = nud.Controls[0]; // UpDownButtons: its Paint event runs after the stock drawing
        var hover = false;
        buttons.MouseEnter += (_, _) => { hover = true; buttons.Invalidate(); };
        buttons.MouseLeave += (_, _) => { hover = false; buttons.Invalidate(); };
        buttons.Paint += (_, e) => PaintSpinner(nud, buttons, e.Graphics, hover);
    }

    private static void PaintSpinner(NumericUpDown nud, Control buttons, Graphics g, bool hover)
    {
        var r = buttons.ClientRectangle;
        using (var brush = new SolidBrush(hover && nud.Enabled ? Blend(nud.BackColor, TidalPalette.ButtonHover, 0.6f) : nud.BackColor))
            g.FillRectangle(brush, r);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var s = nud.DeviceDpi / 96f;
        var cx = r.X + (r.Width / 2f);
        var w = 3.5f * s;
        var color = nud.Enabled ? TidalPalette.AccentSoft : TidalPalette.TextMuted;
        using var pen = new Pen(color, 1.5f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var upY = r.Y + (r.Height * 0.3f);
        var downY = r.Y + (r.Height * 0.7f);
        g.DrawLines(pen, [new PointF(cx - w, upY + (w / 2)), new PointF(cx, upY - (w / 2)), new PointF(cx + w, upY + (w / 2))]);
        g.DrawLines(pen, [new PointF(cx - w, downY - (w / 2)), new PointF(cx, downY + (w / 2)), new PointF(cx + w, downY - (w / 2))]);
    }

    private sealed class NumericFrame : NativeWindow
    {
        private readonly NumericUpDown _nud;

        public NumericFrame(NumericUpDown nud)
        {
            _nud = nud;
            AssignHandle(nud.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT)
            {
                try
                {
                    if (_nud.IsDisposed)
                        return;
                    using var g = Graphics.FromHwnd(Handle);
                    using var pen = new Pen(FrameColor(_nud));
                    g.DrawRectangle(pen, 0, 0, _nud.Width - 1, _nud.Height - 1);
                }
                catch
                {
                    // ignore
                }
            }
            else if (m.Msg == WM_NCDESTROY)
            {
                ReleaseHandle();
            }
        }
    }

    #endregion
}
