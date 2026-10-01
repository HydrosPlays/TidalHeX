using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Vertical stack: lays children out top-to-bottom in collection order at full width.
/// A child whose <see cref="Control.Tag"/> is <see cref="Fill"/> takes the leftover height.
/// </summary>
public sealed class VStack : Panel
{
    public static readonly object Fill = new();

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Spacing { get; set; } = 4;

    public VStack()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        AutoScroll = true; // small windows: scroll instead of hiding the bottom controls
    }

    // Transparent + scrolling: blitted pixels would drag the backdrop along, so repaint instead.
    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate(true);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate(true);
    }

    public T Add<T>(T control, int topMargin = 0) where T : Control
    {
        control.Margin = new Padding(0, topMargin, 0, 0);
        Controls.Add(control);
        return control;
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var width = ClientSize.Width - Padding.Horizontal;
        var visible = Controls.Cast<Control>().Where(c => c.Visible).ToList();

        int HeightOf(Control c) => c switch
        {
            FlowLayoutPanel f => f.GetPreferredSize(new Size(width, 0)).Height,
            _ => c.Height,
        };

        var used = 0;
        foreach (var c in visible)
        {
            used += c.Margin.Top + Spacing;
            if (!ReferenceEquals(c.Tag, Fill))
                used += HeightOf(c);
        }
        var remaining = Math.Max(120, ClientSize.Height - Padding.Vertical - used);

        var offset = AutoScrollPosition.Y; // <= 0 when scrolled down
        var y = Padding.Top;
        foreach (var c in visible)
        {
            y += c.Margin.Top;
            var h = ReferenceEquals(c.Tag, Fill) ? remaining : HeightOf(c);
            c.SetBounds(Padding.Left, y + offset, width, h);
            y += h + Spacing;
        }

        var total = y + Padding.Bottom;
        var min = total > ClientSize.Height ? new Size(0, total) : Size.Empty;
        if (AutoScrollMinSize != min)
            AutoScrollMinSize = min;
    }
}

/// <summary>
/// Shared layout for the Tidal database browsers:
/// header, sidebar with sections, quick-filter toolbar + tile grid, detail card with actions, and a hint footer.
/// </summary>
public abstract class TidalBrowserForm : Form
{
    protected readonly TidalHeader Header = new();
    protected readonly TidalFooter Footer = new();
    protected readonly TileGrid Grid = new() { Dock = DockStyle.Fill };
    protected readonly DetailView Detail = new() { Dock = DockStyle.Fill };
    protected readonly TextBox QuickFilter = new() { PlaceholderText = "Filter results (name, game, location...)", BorderStyle = BorderStyle.FixedSingle };
    protected readonly ComboBox SortBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    protected readonly Label CountLabel = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Width = 170 };

    private readonly FlowLayoutPanel _sectionChips = new() { Dock = DockStyle.Top, Height = 38, WrapContents = false, BackColor = Color.Transparent };
    private readonly Panel _sectionHost = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
    private readonly List<(TidalChip Chip, Control Page)> _sections = [];
    private readonly VStack _actions = new() { Dock = DockStyle.Bottom, Spacing = 8 };

    private readonly System.Windows.Forms.Timer _filterDelay = new() { Interval = 200 };

    protected TidalBrowserForm()
    {
        SuspendLayout();
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(1280, 780);
        MinimumSize = new Size(1000, 620);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        Icon = TidalAssets.AppIcon;
        DoubleBuffered = true;

        // Body: [sidebar | results | details]
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(12, 10, 12, 6),
            BackColor = Color.Transparent,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 310));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Sidebar
        var sidebar = new TidalCard { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 12), Margin = new Padding(0, 0, 10, 0) };
        sidebar.Controls.Add(_sectionHost);
        sidebar.Controls.Add(_sectionChips);

        // Center: toolbar + grid
        var center = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };
        var gridCard = new TidalCard { Dock = DockStyle.Fill, Padding = new Padding(4), Fill = Color.FromArgb(70, TidalPalette.Field) };
        gridCard.Controls.Add(Grid);
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 8) };
        QuickFilter.Dock = DockStyle.Fill;
        QuickFilter.Font = new Font("Segoe UI", 10f);
        SortBox.Dock = DockStyle.Right;
        CountLabel.Dock = DockStyle.Right;
        CountLabel.ForeColor = TidalPalette.TextSoft;
        var sortSpacer = new Panel { Dock = DockStyle.Right, Width = 8, BackColor = Color.Transparent };
        toolbar.Controls.Add(QuickFilter);
        toolbar.Controls.Add(sortSpacer);
        toolbar.Controls.Add(SortBox);
        toolbar.Controls.Add(CountLabel);
        center.Controls.Add(gridCard);
        center.Controls.Add(toolbar);

        // Details
        var details = new TidalCard { Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 12), Margin = new Padding(10, 0, 0, 0) };
        details.Controls.Add(Detail);
        details.Controls.Add(_actions);

        body.Controls.Add(sidebar, 0, 0);
        body.Controls.Add(center, 1, 0);
        body.Controls.Add(details, 2, 0);

        Controls.Add(body);
        Controls.Add(Header);
        Controls.Add(Footer);

        QuickFilter.TextChanged += (_, _) => { _filterDelay.Stop(); _filterDelay.Start(); };
        _filterDelay.Tick += (_, _) => { _filterDelay.Stop(); OnQuickFilterChanged(); };
        SortBox.SelectedIndexChanged += (_, _) => OnQuickFilterChanged();
        Grid.SelectionChanged += (_, _) => OnSelectionChanged(Grid.SelectedItem);
        Grid.ItemActivated += (_, _) => { if (Grid.SelectedItem is { } item) OnActivated(item); };
        ResumeLayout(false);
    }

    protected override void OnLoad(EventArgs e)
    {
        TidalTheme.Apply(this, force: true);
        base.OnLoad(e);
        FitToScreen();
    }

    /// <summary> Keeps the (DPI-scaled) window within the screen's working area. </summary>
    private void FitToScreen()
    {
        var area = Screen.FromControl(this).WorkingArea;
        if (MinimumSize.Width > area.Width || MinimumSize.Height > area.Height)
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        if (Width <= area.Width && Height <= area.Height)
            return;
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        CenterToScreen();
    }

    #region Sidebar sections

    /// <summary>
    /// Adds a sidebar section with a selector chip; returns the stack to fill with filter controls.
    /// </summary>
    protected VStack AddSection(string name)
    {
        var page = new VStack { Dock = DockStyle.Fill, Visible = _sections.Count == 0 };
        var chip = new TidalChip { Text = name, Checked = _sections.Count == 0 };
        chip.FitToText();
        chip.Click += (_, _) => ShowSection(page);
        _sections.Add((chip, page));
        _sectionChips.Controls.Add(chip);
        _sectionHost.Controls.Add(page);
        return page;
    }

    protected void ShowSection(Control page)
    {
        foreach (var (chip, p) in _sections)
        {
            var active = ReferenceEquals(p, page);
            chip.Checked = active;
            p.Visible = active;
        }
    }

    /// <summary> Adds a detail-pane action button; buttons stack top-to-bottom in the order added. </summary>
    protected TidalButton AddAction(string text, EventHandler onClick, bool primary = false)
    {
        var b = new TidalButton { Text = text, Primary = primary, Height = 36 };
        b.Click += onClick;
        _actions.Controls.Add(b);
        _actions.Height = (_actions.Controls.Count * (36 + _actions.Spacing)) + 4;
        return b;
    }

    #endregion

    #region Helpers for building filter UI

    protected static ComboBox MakeCombo(bool searchable)
    {
        var cb = new ComboBox
        {
            DropDownStyle = searchable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Height = 26,
            Font = new Font("Segoe UI", 9.5f),
        };
        if (searchable)
        {
            cb.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            cb.AutoCompleteSource = AutoCompleteSource.ListItems;
        }
        return cb;
    }

    /// <summary> Two-by-two grid of controls, used for move pickers. </summary>
    protected static TableLayoutPanel MakeQuad(Control a, Control b, Control c, Control d)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, Height = (a.Height + 4) * 2, BackColor = Color.Transparent, Margin = Padding.Empty };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        foreach (var x in new[] { a, b, c, d })
        {
            x.Dock = DockStyle.Fill;
            x.Margin = new Padding(0, 0, 4, 4);
        }
        t.Controls.Add(a, 0, 0);
        t.Controls.Add(b, 1, 0);
        t.Controls.Add(c, 0, 1);
        t.Controls.Add(d, 1, 1);
        return t;
    }

    /// <summary> Two controls side by side. </summary>
    protected static TableLayoutPanel MakePair(Control a, Control b, int height = 36)
    {
        var t = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Height = height, BackColor = Color.Transparent, Margin = Padding.Empty };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        a.Dock = b.Dock = DockStyle.Fill;
        a.Margin = new Padding(0, 0, 4, 0);
        b.Margin = new Padding(4, 0, 0, 0);
        t.Controls.Add(a, 0, 0);
        t.Controls.Add(b, 1, 0);
        return t;
    }

    protected static FlowLayoutPanel MakeChipFlow() => new()
    {
        WrapContents = true,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
    };

    #endregion

    #region Overridable behavior

    /// <summary> Called (debounced) when the quick-filter text or sort order changes. </summary>
    protected abstract void OnQuickFilterChanged();

    /// <summary> Called when the highlighted tile changes. </summary>
    protected abstract void OnSelectionChanged(TileItem? item);

    /// <summary> Called when a tile is double-clicked or Enter is pressed. </summary>
    protected abstract void OnActivated(TileItem item);

    #endregion

    /// <summary>
    /// Esc closes the window, but only after the focused control declines it (a dropped-down combo,
    /// an autocomplete list or a PropertyGrid edit consume Esc first).
    /// </summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.F:
                QuickFilter.Focus();
                QuickFilter.SelectAll();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary> Applies the quick filter text to a set of tiles. </summary>
    protected IReadOnlyList<TileItem> ApplyQuickFilter(IEnumerable<TileItem> items)
    {
        var text = QuickFilter.Text.Trim();
        if (text.Length == 0)
            return items as IReadOnlyList<TileItem> ?? items.ToList();
        var terms = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return items.Where(i => terms.All(t => i.SearchText.Contains(t, StringComparison.Ordinal))).ToList();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _filterDelay.Dispose();
        base.Dispose(disposing);
    }
}
