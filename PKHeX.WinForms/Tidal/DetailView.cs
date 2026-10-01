using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Right-hand detail pane: large sprite, title, badges, and key/value rows.
/// </summary>
public sealed class DetailView : Control
{
    public sealed record Badge(string Text, Color Color);

    private Image? _image;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private readonly List<Badge> _badges = [];
    private readonly List<(string Key, string Value)> _rows = [];
    private int _scroll;
    private int _contentHeight;
    private int _wheelRemainder;

    private readonly Font _titleFont = new("Segoe UI", 15f, FontStyle.Bold);
    private readonly Font _subFont = new("Segoe UI", 9.5f);
    private readonly Font _badgeFont = new("Segoe UI", 8f, FontStyle.Bold);
    private readonly Font _keyFont = new("Segoe UI", 8.5f, FontStyle.Bold);
    private readonly Font _valueFont = new("Segoe UI", 9.5f);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder { get; set; } = "Select a result to see its details.";

    public DetailView()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    public void Clear()
    {
        _image = null;
        _title = _subtitle = string.Empty;
        _badges.Clear();
        _rows.Clear();
        _scroll = 0;
        Invalidate();
    }

    public void Show(Image? image, string title, string subtitle, IEnumerable<Badge> badges, IEnumerable<string> lines)
    {
        _image = image;
        _title = title;
        _subtitle = subtitle;
        _badges.Clear();
        _badges.AddRange(badges);
        _rows.Clear();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var split = line.IndexOf(": ", StringComparison.Ordinal);
            if (split > 0 && split < 40)
                _rows.Add((line[..split], line[(split + 2)..]));
            else
                _rows.Add((string.Empty, line.Trim()));
        }
        _scroll = 0;
        Invalidate();
    }

    /// <summary> Plain-text copy of what is shown, for the clipboard. </summary>
    public string GetText()
    {
        var lines = new List<string> { _title };
        if (_subtitle.Length != 0)
            lines.Add(_subtitle);
        foreach (var (k, v) in _rows)
            lines.Add(k.Length == 0 ? v : $"{k}: {v}");
        return string.Join(Environment.NewLine, lines);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const int pad = 16;
        var width = Width - (pad * 2);

        if (_title.Length == 0)
        {
            TextRenderer.DrawText(g, Placeholder, _subFont, ClientRectangle, TidalPalette.TextSoft,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        int y = pad - _scroll;

        // Hero sprite on a soft spotlight.
        if (_image is { } img)
        {
            const int scale = 2;
            var w = img.Width * scale;
            var h = img.Height * scale;
            var x = (Width - w) / 2;
            var spot = new Rectangle((Width / 2) - 80, y + (h / 2) - 50, 160, 100);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(spot);
                using var brush = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(90, TidalPalette.Accent),
                    SurroundColors = [Color.FromArgb(0, TidalPalette.Accent)],
                };
                g.FillPath(brush, path);
            }
            var oldInterp = g.InterpolationMode;
            var oldOffset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(img, x, y, w, h);
            g.InterpolationMode = oldInterp;
            g.PixelOffsetMode = oldOffset;
            y += h + 6;
        }

        // Title + subtitle
        var titleHeight = TextRenderer.MeasureText(g, _title, _titleFont, new Size(width, 200), TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter).Height;
        TextRenderer.DrawText(g, _title, _titleFont, new Rectangle(pad, y, width, titleHeight), TidalPalette.Text, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter);
        y += titleHeight;
        if (_subtitle.Length != 0)
        {
            var subHeight = TextRenderer.MeasureText(g, _subtitle, _subFont, new Size(width, 200), TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter).Height;
            TextRenderer.DrawText(g, _subtitle, _subFont, new Rectangle(pad, y, width, subHeight), TidalPalette.TextSoft, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter);
            y += subHeight;
        }
        y += 8;

        // Badges, centered and wrapped.
        if (_badges.Count != 0)
            y = DrawBadges(g, y, pad, width) + 10;

        using (var line = new Pen(Color.FromArgb(90, TidalPalette.PanelEdge)))
            g.DrawLine(line, pad, y, Width - pad, y);
        y += 10;

        // Key/value rows
        const TextFormatFlags wrap = TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.NoPrefix;
        foreach (var (key, value) in _rows)
        {
            if (key.Length != 0)
            {
                TextRenderer.DrawText(g, key.ToUpperInvariant(), _keyFont, new Point(pad, y), TidalPalette.TextMuted, TextFormatFlags.NoPrefix);
                y += 16;
            }
            var h = TextRenderer.MeasureText(g, value, _valueFont, new Size(width, 1000), wrap).Height;
            TextRenderer.DrawText(g, value, _valueFont, new Rectangle(pad, y, width, h), TidalPalette.Text, wrap);
            y += h + 8;
        }

        _contentHeight = y + _scroll + pad;

        // Content or size changed: keep the offset valid so the top never disappears.
        var max = Math.Max(0, _contentHeight - Height);
        if (_scroll > max)
        {
            _scroll = max;
            Invalidate();
        }
    }

    private int DrawBadges(Graphics g, int y, int pad, int width)
    {
        const int h = 20;
        const int gap = 6;
        var sizes = new List<int>(_badges.Count);
        foreach (var b in _badges)
            sizes.Add(TextRenderer.MeasureText(b.Text, _badgeFont).Width + 12);

        int i = 0;
        while (i < _badges.Count)
        {
            // Measure how many fit on this line.
            int lineWidth = 0, start = i;
            while (i < _badges.Count && (lineWidth == 0 || lineWidth + gap + sizes[i] <= width))
            {
                lineWidth += (lineWidth == 0 ? 0 : gap) + sizes[i];
                i++;
            }
            int x = pad + ((width - lineWidth) / 2);
            for (int j = start; j < i; j++)
            {
                var r = new Rectangle(x, y, sizes[j], h);
                TidalPaint.FillRounded(g, r, h / 2f, _badges[j].Color);
                TextRenderer.DrawText(g, _badges[j].Text, _badgeFont, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                x += sizes[j] + gap;
            }
            y += h + gap;
        }
        return y - gap;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var max = Math.Max(0, _contentHeight - Height);
        _wheelRemainder += e.Delta * 40;
        var pixels = _wheelRemainder / 120;
        _wheelRemainder -= pixels * 120;
        var next = Math.Clamp(_scroll - pixels, 0, max);
        if (next == _scroll)
            return;
        _scroll = next;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _subFont.Dispose();
            _badgeFont.Dispose();
            _keyFont.Dispose();
            _valueFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
