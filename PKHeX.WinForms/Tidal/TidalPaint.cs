using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Drawing primitives for the Tidal theme.
/// </summary>
public static class TidalPaint
{
    public enum Surface
    {
        /// <summary> Outer window background. </summary>
        Backdrop,
        /// <summary> Content panel (tab pages, cards). </summary>
        Panel,
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Renders a surface bitmap of the requested size. Bitmaps are drawn 1:1 (no stretching) so child transparency stays cheap.
    /// </summary>
    /// <remarks>
    /// Premultiplied ARGB: every transparent child control repaints its part of this bitmap, and GDI+ draws PArgb
    /// several times faster than plain ARGB (the surfaces are opaque, so nothing is lost).
    /// </remarks>
    public static Bitmap RenderSurface(Size size, Surface kind)
    {
        var w = Math.Max(1, size.Width);
        var h = Math.Max(1, size.Height);
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, w, h);
        if (kind == Surface.Backdrop)
            DrawBackdrop(g, rect);
        else
            DrawPanel(g, rect);
        return bmp;
    }

    public static void DrawBackdrop(Graphics g, Rectangle rect)
    {
        using (var bg = new LinearGradientBrush(rect, TidalPalette.BackdropTop, TidalPalette.BackdropBottom, 70f))
            g.FillRectangle(bg, rect);

        // Soft glow in the upper-right, like light through water.
        var glowRect = new RectangleF(rect.Width * 0.35f, -rect.Height * 0.6f, rect.Width * 1.1f, rect.Height * 1.4f);
        using (var glowPath = new GraphicsPath())
        {
            glowPath.AddEllipse(glowRect);
            using var glow = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb(70, TidalPalette.BackdropGlow),
                SurroundColors = [Color.FromArgb(0, TidalPalette.BackdropGlow)],
            };
            g.FillPath(glow, glowPath);
        }

        DrawRings(g, rect, new PointF(rect.Width * 0.72f, rect.Height * 0.48f), 5, 26);
        DrawCircuitLines(g, rect, 18);
    }

    public static void DrawPanel(Graphics g, Rectangle rect)
    {
        using (var bg = new LinearGradientBrush(rect, TidalPalette.PanelTop, TidalPalette.PanelBottom, 90f))
            g.FillRectangle(bg, rect);

        // Faint grid.
        using (var grid = new Pen(Color.FromArgb(10, Color.White)))
        {
            for (int x = 0; x < rect.Width; x += 24)
                g.DrawLine(grid, x, 0, x, rect.Height);
            for (int y = 0; y < rect.Height; y += 24)
                g.DrawLine(grid, 0, y, rect.Width, y);
        }

        DrawRings(g, rect, new PointF(rect.Width * 0.85f, rect.Height * 0.30f), 4, 18);
    }

    private static void DrawRings(Graphics g, Rectangle rect, PointF center, int count, int alpha)
    {
        var baseRadius = Math.Max(rect.Width, rect.Height) * 0.18f;
        for (int i = 0; i < count; i++)
        {
            var r = baseRadius * (1 + (i * 0.55f));
            var thickness = i % 2 == 0 ? Math.Max(6f, r * 0.12f) : 1.5f;
            using var pen = new Pen(Color.FromArgb(Math.Max(6, alpha - (i * 4)), TidalPalette.BackdropGlow), thickness);
            // Open arcs rather than full circles, to echo the reference style.
            var start = 200 + (i * 37);
            g.DrawArc(pen, center.X - r, center.Y - r, r * 2, r * 2, start, 250);
        }
    }

    private static void DrawCircuitLines(Graphics g, Rectangle rect, int alpha)
    {
        using var pen = new Pen(Color.FromArgb(alpha, Color.White), 1f);
        var h = rect.Height;
        var w = rect.Width;
        // A handful of thin angled lines along the edges.
        g.DrawLines(pen, new PointF[] { new(0, h * 0.18f), new(w * 0.08f, h * 0.18f), new(w * 0.12f, h * 0.26f), new(w * 0.26f, h * 0.26f) });
        g.DrawLines(pen, new PointF[] { new(w, h * 0.82f), new(w * 0.9f, h * 0.82f), new(w * 0.86f, h * 0.9f), new(w * 0.7f, h * 0.9f) });
        g.DrawLines(pen, new PointF[] { new(w * 0.55f, 0), new(w * 0.58f, h * 0.06f), new(w * 0.8f, h * 0.06f) });
    }

    /// <summary>
    /// Selection outline in the style of a console home menu: bright cyan ring with a soft glow.
    /// </summary>
    public static void DrawSelectionRing(Graphics g, Rectangle r, float radius = 8)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 3; i >= 1; i--)
        {
            var inflate = r;
            inflate.Inflate(i, i);
            using var glowPath = RoundedRect(inflate, radius + i);
            using var glowPen = new Pen(Color.FromArgb(50 * (4 - i), TidalPalette.Accent), 2f);
            g.DrawPath(glowPen, glowPath);
        }
        using var path = RoundedRect(r, radius);
        using var pen = new Pen(TidalPalette.Accent, 2.5f);
        g.DrawPath(pen, path);
        g.SmoothingMode = old;
    }

    /// <summary>
    /// Draws a small round "controller button" glyph (e.g. A, B, +) and returns its width.
    /// </summary>
    public static int DrawButtonGlyph(Graphics g, string glyph, Point location, int size, Font font)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var textSize = TextRenderer.MeasureText(glyph, font);
        int w = Math.Max(size, textSize.Width + 6);
        var r = new Rectangle(location.X, location.Y, w, size);
        using (var path = RoundedRect(r, size / 2f))
        {
            using var fill = new SolidBrush(Color.White);
            g.FillPath(fill, path);
        }
        TextRenderer.DrawText(g, glyph, font, r, TidalPalette.BackdropBottom,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        g.SmoothingMode = old;
        return w;
    }

    public static void FillRounded(Graphics g, Rectangle r, float radius, Color color)
    {
        using var path = RoundedRect(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void StrokeRounded(Graphics g, Rectangle r, float radius, Color color, float width = 1f)
    {
        using var path = RoundedRect(r, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }
}
