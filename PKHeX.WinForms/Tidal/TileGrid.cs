using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// One entry displayed in a <see cref="TileGrid"/>.
/// </summary>
public sealed class TileItem(object tag, Func<Image?> imageFactory)
{
    public object Tag { get; } = tag;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Corner { get; init; } = string.Empty;
    public string Badge { get; init; } = string.Empty;
    public Color BadgeColor { get; init; } = TidalPalette.BadgeOther;

    /// <summary> Lower-cased text used by quick filtering. </summary>
    public string SearchText { get; init; } = string.Empty;

    private Image? _image;
    private bool _imageLoaded;

    public Image? Image
    {
        get
        {
            if (_imageLoaded)
                return _image;
            _imageLoaded = true;
            try { _image = imageFactory(); }
            catch { _image = null; }
            return _image;
        }
    }
}

/// <summary>
/// Virtualized, keyboard-navigable grid of rounded tiles with a slim custom scrollbar.
/// Only visible tiles are painted, so tens of thousands of results stay responsive.
/// </summary>
public sealed class TileGrid : Control
{
    private IReadOnlyList<TileItem> _items = [];
    private int _scroll; // pixels
    private int _hover = -1;
    private bool _draggingThumb;
    private int _dragOffset;

    private readonly Font _titleFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font _subFont = new("Segoe UI", 8f);
    private readonly Font _badgeFont = new("Segoe UI", 7f, FontStyle.Bold);
    private readonly Font _emptyFont = new("Segoe UI", 12f);

    private int _wheelRemainder;

    private int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    private int ScrollbarWidth => S(10);
    private int Pad => S(12);
    private Size Tile => new(S(TileSize.Width), S(TileSize.Height));
    private int GapPx => S(Gap);

    public event EventHandler? SelectionChanged;
    public event EventHandler? ItemActivated;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Size TileSize { get; set; } = new(132, 128);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Gap { get; set; } = 10;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set { field = value; Invalidate(); } } = "No results";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get; private set; } = -1;

    public TileItem? SelectedItem => (uint)SelectedIndex < (uint)_items.Count ? _items[SelectedIndex] : null;

    public IReadOnlyList<TileItem> Items => _items;

    public TileGrid()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
               | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        TabStop = true;
    }

    protected override void OnContextMenuStripChanged(EventArgs e)
    {
        base.OnContextMenuStripChanged(e);
        if (ContextMenuStrip is { } menu)
            menu.Opening += ContextMenuOpening;
    }

    /// <summary> Only open the menu over a tile (selecting it first); keyboard invocation uses the current selection. </summary>
    private void ContextMenuOpening(object? sender, CancelEventArgs e)
    {
        var p = PointToClient(Cursor.Position);
        if (!ClientRectangle.Contains(p))
        {
            e.Cancel = SelectedItem is null; // opened from the keyboard (menu key / Shift+F10)
            return;
        }
        var index = HitTest(p);
        if (index < 0)
        {
            e.Cancel = true;
            return;
        }
        Select(index);
    }

    public void SetItems(IReadOnlyList<TileItem> items, bool keepSelection = false)
    {
        var previous = keepSelection ? SelectedItem : null;
        _items = items;
        _scroll = 0;
        _hover = -1;
        SelectedIndex = -1;
        if (previous is not null)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!ReferenceEquals(items[i], previous))
                    continue;
                SelectedIndex = i;
                EnsureVisible(i);
                break;
            }
        }
        if (SelectedIndex == -1 && items.Count != 0)
            SelectedIndex = 0;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Select(int index)
    {
        if (_items.Count == 0)
            return;
        index = Math.Clamp(index, 0, _items.Count - 1);
        if (index == SelectedIndex)
            return;
        SelectedIndex = index;
        EnsureVisible(index);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    #region Layout

    private int Columns => Math.Max(1, (Width - (Pad * 2) - ScrollbarWidth + GapPx) / (Tile.Width + GapPx));
    private int Rows => (_items.Count + Columns - 1) / Columns;
    private int RowHeight => Tile.Height + GapPx;
    private int ContentHeight => (Rows * RowHeight) + (Pad * 2) - GapPx;
    private int MaxScroll => Math.Max(0, ContentHeight - Height);

    private int LeftOffset
    {
        get
        {
            // Center the grid in the available width.
            var used = (Columns * (Tile.Width + GapPx)) - GapPx;
            return Math.Max(Pad, (Width - ScrollbarWidth - used) / 2);
        }
    }

    private Rectangle GetTileRect(int index)
    {
        var col = index % Columns;
        var row = index / Columns;
        return new Rectangle(LeftOffset + (col * (Tile.Width + GapPx)), Pad + (row * RowHeight) - _scroll, Tile.Width, Tile.Height);
    }

    private int HitTest(Point p)
    {
        if (p.X >= Width - ScrollbarWidth - 2)
            return -1;
        if (p.X < LeftOffset)
            return -1;
        var col = (p.X - LeftOffset) / (Tile.Width + GapPx);
        if (p.Y + _scroll < Pad)
            return -1;
        var row = (p.Y + _scroll - Pad) / RowHeight;
        if (col < 0 || col >= Columns || row < 0)
            return -1;
        var index = (row * Columns) + col;
        if (index >= _items.Count)
            return -1;
        return GetTileRect(index).Contains(p) ? index : -1;
    }

    private void SetScroll(int value)
    {
        value = Math.Clamp(value, 0, MaxScroll);
        if (value == _scroll)
            return;
        _scroll = value;
        Invalidate();
    }

    public void EnsureVisible(int index)
    {
        if (index < 0)
            return;
        var r = GetTileRect(index);
        if (r.Top < Pad)
            SetScroll(_scroll + r.Top - Pad);
        else if (r.Bottom > Height - Pad)
            SetScroll(_scroll + (r.Bottom - (Height - Pad)));
    }

    private Rectangle ThumbRect
    {
        get
        {
            var track = Height - 8;
            if (ContentHeight <= Height || track <= 0)
                return Rectangle.Empty;
            var thumbH = Math.Max(S(30), (int)((long)track * Height / ContentHeight));
            var y = 4 + (int)((long)(track - thumbH) * _scroll / Math.Max(1, MaxScroll));
            return new Rectangle(Width - ScrollbarWidth - 2, y, ScrollbarWidth - 2, thumbH);
        }
    }

    #endregion

    #region Painting

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, EmptyText, _emptyFont, ClientRectangle, TidalPalette.TextSoft,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        var cols = Columns;
        var firstRow = Math.Max(0, (_scroll - Pad) / RowHeight);
        var lastRow = Math.Min(Rows - 1, (_scroll + Height) / RowHeight);
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                var index = (row * cols) + col;
                if (index >= _items.Count)
                    break;
                DrawTile(g, index);
            }
        }

        var thumb = ThumbRect;
        if (!thumb.IsEmpty)
        {
            var track = new Rectangle(thumb.X, 4, thumb.Width, Height - 8);
            TidalPaint.FillRounded(g, track, track.Width / 2f, Color.FromArgb(50, TidalPalette.Field));
            TidalPaint.FillRounded(g, thumb, thumb.Width / 2f, _draggingThumb ? TidalPalette.Accent : Color.FromArgb(200, TidalPalette.AccentSoft));
        }
    }

    private void DrawTile(Graphics g, int index)
    {
        var item = _items[index];
        var r = GetTileRect(index);
        var selected = index == SelectedIndex;
        var hover = index == _hover;

        TidalPaint.FillRounded(g, r, 10, selected ? Color.FromArgb(230, TidalPalette.Selection)
                                           : hover ? Color.FromArgb(200, TidalPalette.FieldAlt)
                                           : Color.FromArgb(150, TidalPalette.Field));

        // Text rows are laid out from the bottom using the (DPI-scaled) font heights.
        var subH = _subFont.Height + S(2);
        var titleH = _titleFont.Height + S(2);
        var subRect = new Rectangle(r.X + S(6), r.Bottom - S(6) - subH, r.Width - S(12), subH);
        var titleRect = new Rectangle(r.X + S(6), subRect.Y - titleH, r.Width - S(12), titleH);

        // Sprite: centered in the space above the title, scaled with DPI.
        if (item.Image is { } img)
        {
            var scale = DeviceDpi / 96f;
            var w = (int)(img.Width * scale);
            var h = (int)(img.Height * scale);
            var top = r.Y + S(18);
            var avail = titleRect.Y - top;
            var ix = r.X + ((r.Width - w) / 2);
            var iy = top + Math.Max(0, (avail - h) / 2);
            g.DrawImage(img, ix, iy, w, h);
        }

        // Badge (top-left)
        if (item.Badge.Length != 0)
        {
            var size = TextRenderer.MeasureText(item.Badge, _badgeFont);
            var br = new Rectangle(r.X + S(6), r.Y + S(6), size.Width + S(4), _badgeFont.Height + S(2));
            TidalPaint.FillRounded(g, br, br.Height / 2f, item.BadgeColor);
            TextRenderer.DrawText(g, item.Badge, _badgeFont, br, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        // Corner text (top-right)
        if (item.Corner.Length != 0)
        {
            var cr = new Rectangle(r.X + (r.Width / 2), r.Y + S(5), (r.Width / 2) - S(8), _badgeFont.Height + S(4));
            TextRenderer.DrawText(g, item.Corner, _badgeFont, cr, TidalPalette.TextSoft, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        TextRenderer.DrawText(g, item.Title, _titleFont, titleRect, selected ? TidalPalette.Accent : TidalPalette.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, item.Subtitle, _subFont, subRect, TidalPalette.TextSoft,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

        if (selected)
            TidalPaint.DrawSelectionRing(g, r, 10);
        else
            TidalPaint.StrokeRounded(g, r, 10, Color.FromArgb(hover ? 160 : 60, TidalPalette.PanelEdge));
    }

    #endregion

    #region Input

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var thumb = ThumbRect;
        if (e.Button == MouseButtons.Left && !thumb.IsEmpty && e.X >= thumb.X - 2)
        {
            if (thumb.Contains(e.Location))
            {
                _draggingThumb = true;
                _dragOffset = e.Y - thumb.Y;
            }
            else
            {
                // Page toward the click.
                SetScroll(_scroll + (e.Y < thumb.Y ? -Height : Height));
            }
            Invalidate();
            return;
        }

        var index = HitTest(e.Location);
        if (index >= 0)
            Select(index);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_draggingThumb)
        {
            _draggingThumb = false;
            Invalidate();
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!_draggingThumb || Capture)
            return;
        _draggingThumb = false;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_draggingThumb && (e.Button & MouseButtons.Left) == 0)
            _draggingThumb = false;
        if (_draggingThumb)
        {
            var thumb = ThumbRect;
            var track = Height - 8 - thumb.Height;
            if (track > 0)
                SetScroll((int)((long)(e.Y - _dragOffset - 4) * MaxScroll / track));
            return;
        }
        var hover = HitTest(e.Location);
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

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitTest(e.Location) is >= 0)
            ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // Accumulate so touchpads / high-resolution wheels (deltas < 120) still scroll smoothly.
        _wheelRemainder += e.Delta * RowHeight;
        var pixels = _wheelRemainder / 120;
        _wheelRemainder -= pixels * 120;
        if (pixels != 0)
            SetScroll(_scroll - pixels);
    }

    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Enter or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_items.Count == 0)
            return;
        var cols = Columns;
        var pageRows = Math.Max(1, Height / RowHeight);
        var current = Math.Max(0, SelectedIndex);
        switch (e.KeyCode)
        {
            case Keys.Left: Select(current - 1); break;
            case Keys.Right: Select(current + 1); break;
            case Keys.Up: if (current >= cols) Select(current - cols); break;
            case Keys.Down: Select(current + cols); break;
            case Keys.PageUp: Select(current - (cols * pageRows)); break;
            case Keys.PageDown: Select(current + (cols * pageRows)); break;
            case Keys.Home: Select(0); break;
            case Keys.End: Select(_items.Count - 1); break;
            case Keys.Enter: ItemActivated?.Invoke(this, EventArgs.Empty); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        if (SelectedIndex >= 0)
            EnsureVisible(SelectedIndex);
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _subFont.Dispose();
            _badgeFont.Dispose();
            _emptyFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
