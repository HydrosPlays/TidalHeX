using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PKHeX.Core;
using PKHeX.Core.Searching;
using PKHeX.Drawing.PokeSprite;
using PKHeX.WinForms.Controls;
using static PKHeX.Core.MessageStrings;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Tidal replacement for <see cref="SAV_MysteryGiftDB"/>: every event gift as a browsable card grid with live filters.
/// </summary>
public sealed class MysteryGiftBrowser : TidalBrowserForm
{
    private readonly PKMEditor PKME_Tabs;
    private readonly SAVEditor BoxView;
    private readonly SaveFile SAV;
    private readonly string DatabasePath = Main.MGDatabasePath;
    private readonly GameStrings Strings = GameInfo.Strings;

    private List<MysteryGift> RawDB = [];
    private List<TileItem> AllTiles = [];
    private bool _loaded;
    private bool _suspendFilter;

    // Filters
    private readonly ComboBox CB_Species = MakeCombo(true);
    private readonly ComboBox CB_HeldItem = MakeCombo(true);
    private readonly ComboBox CB_Move1 = MakeCombo(true);
    private readonly ComboBox CB_Move2 = MakeCombo(true);
    private readonly ComboBox CB_Move3 = MakeCombo(true);
    private readonly ComboBox CB_Move4 = MakeCombo(true);
    private readonly TidalTriState TS_Shiny = new();
    private readonly TidalTriState TS_Egg = new();
    private readonly Dictionary<byte, TidalChip> GenChips = [];

    // Advanced
    private readonly EntityInstructionBuilder UC_Builder;
    private readonly RichTextBox RTB_Instructions = new() { Height = 130, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None };

    private static readonly Color[] GenColors =
    [
        TidalPalette.BadgeOther,
        Color.FromArgb(200, 60, 60),   // 1
        Color.FromArgb(200, 150, 30),  // 2
        Color.FromArgb(40, 150, 90),   // 3
        Color.FromArgb(60, 110, 200),  // 4
        Color.FromArgb(90, 90, 110),   // 5
        Color.FromArgb(40, 120, 200),  // 6
        Color.FromArgb(220, 110, 40),  // 7
        Color.FromArgb(150, 60, 170),  // 8
        Color.FromArgb(200, 50, 110),  // 9
    ];

    public MysteryGiftBrowser(PKMEditor tabs, SAVEditor sav)
    {
        PKME_Tabs = tabs;
        BoxView = sav;
        SAV = sav.SAV;
        UC_Builder = new EntityInstructionBuilder(() => tabs.PreparePKM()) { ReadOnly = true, Height = 46 };

        Text = "Mystery Gift Database";
        Header.Text = "Mystery Gift Database";
        Grid.EmptyText = "Loading event gifts...";
        Detail.Placeholder = "Select a gift to see its card details.";
        QuickFilter.PlaceholderText = "Filter gifts (title, Pokémon, OT, card #...)";

        BuildFilterSection();
        BuildAdvancedSection();
        BuildSettingsSection();

        SortBox.Items.AddRange(["Sort: Default", "Sort: Newest gen", "Sort: Species", "Sort: Card #", "Sort: Title"]);
        SortBox.SelectedIndex = 0;

        AddAction("Load into Editor", (_, _) => LoadSelected(), primary: true);
        AddAction("Save Pokémon File...", (_, _) => SavePKM());
        AddAction("Save Gift File...", (_, _) => SaveGift());
        AddAction("Copy Details", (_, _) => CopyDetails());

        var menu = new ContextMenuStrip();
        menu.Items.Add("Load into Editor", null, (_, _) => LoadSelected());
        menu.Items.Add("Save Pokémon File...", null, (_, _) => SavePKM());
        menu.Items.Add("Save Gift File...", null, (_, _) => SaveGift());
        menu.Items.Add("Copy Details", null, (_, _) => CopyDetails());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Import All Shown into Boxes...", null, (_, _) => ImportToBoxes());
        menu.Items.Add("Export All Shown to Folder...", null, (_, _) => ExportResults());
        Grid.ContextMenuStrip = menu;

        var hints = new List<TidalFooter.Hint>
        {
            new("Enter", "Load", LoadSelected),
            new("Ctrl+S", "Save gift", SaveGift),
            new("Ctrl+I", "Import shown to boxes", ImportToBoxes),
            new("Ctrl+E", "Export shown", ExportResults),
        };
        if (Directory.Exists(DatabasePath))
            hints.Add(new("📁", "Open DB folder", OpenDB));
        hints.Add(new("⟲", "Classic view", OpenClassic));
        hints.Add(new("Esc", "Close", Close));
        Footer.SetHints([.. hints]);
        Footer.LeftText = "Filters apply instantly";
        UpdateHeader();
    }

    #region Build UI

    private void BuildFilterSection()
    {
        var s = AddSection("Filters");
        s.Add(new TidalCaption("POKÉMON"));
        s.Add(CB_Species);
        s.Add(new TidalCaption("HELD ITEM"), 4);
        s.Add(CB_HeldItem);
        s.Add(new TidalCaption("KNOWS MOVES"), 4);
        s.Add(MakeQuad(CB_Move1, CB_Move2, CB_Move3, CB_Move4));
        s.Add(MakePair(new TidalCaption("SHINY"), new TidalCaption("EGG"), 20), 2);
        s.Add(MakePair(TS_Shiny, TS_Egg, 28));

        s.Add(new TidalCaption("GENERATIONS"), 4);
        var flow = MakeChipFlow();
        for (byte gen = 1; gen <= Latest.Generation; gen++)
        {
            var chip = new TidalChip { Text = $"Gen {gen}", Checked = IsDefaultGen(gen), Width = 60 };
            chip.Click += (_, _) =>
            {
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    foreach (var c in GenChips.Values)
                        c.Checked = c == chip;
                }
                ApplyFilters();
            };
            GenChips[gen] = chip;
            flow.Controls.Add(chip);
        }
        s.Add(flow);

        var all = new TidalButton { Text = "All Gens", Height = 32 };
        all.Click += (_, _) => SetGens(_ => true);
        var compatible = new TidalButton { Text = "This Save", Height = 32 };
        compatible.Click += (_, _) => SetGens(IsDefaultGen);
        s.Add(MakePair(all, compatible, 32), 2);

        var reset = new TidalButton { Text = "Reset Filters", Height = 36 };
        reset.Click += (_, _) => ResetFilters();
        s.Add(reset, 10);

        foreach (var cb in new[] { CB_Species, CB_HeldItem, CB_Move1, CB_Move2, CB_Move3, CB_Move4 })
            cb.SelectedIndexChanged += (_, _) => ApplyFilters();
        TS_Shiny.ValueChanged += (_, _) => ApplyFilters();
        TS_Egg.ValueChanged += (_, _) => ApplyFilters();
    }

    private void BuildAdvancedSection()
    {
        var s = AddSection("Advanced");
        s.Add(new TidalCaption("BATCH FILTER BUILDER"));
        s.Add(UC_Builder);
        var add = new TidalButton { Text = "Add Filter Line", Height = 32 };
        add.Click += (_, _) => AddInstruction();
        s.Add(add, 2);
        s.Add(new TidalCaption("FILTER LINES (e.g. =OriginalTrainerName=Ash)"), 4);
        s.Add(RTB_Instructions);
        var apply = new TidalButton { Text = "Apply Filter Lines", Primary = true, Height = 36 };
        apply.Click += (_, _) => ApplyFilters();
        s.Add(apply, 6);
    }

    private void BuildSettingsSection()
    {
        var s = AddSection("Settings");
        s.Add(new TidalCaption("MYSTERY GIFT DATABASE SETTINGS"));
        var grid = new PropertyGrid { ToolbarVisible = false, Tag = VStack.Fill };
        PropertyGridLocalization.Apply(grid, Main.Settings.MysteryDb, Main.CurrentLanguage);
        s.Add(grid);
        s.Add(new TidalCaption("Reopen the window to apply database settings."), 4);
    }

    private void PopulateComboBoxes()
    {
        _suspendFilter = true;
        var any = new ComboItem(MsgAny, -1);
        var source = GameInfo.FilteredSources;

        CB_Species.InitializeBinding();
        var present = RawDB.Select(z => (int)z.Species).ToHashSet();
        var species = source.Species.Where(z => present.Contains(z.Value)).ToList();
        species.Insert(0, any);
        CB_Species.DataSource = species;

        CB_HeldItem.InitializeBinding();
        var items = new List<ComboItem>(source.Items);
        items.Insert(0, any);
        CB_HeldItem.DataSource = items;

        var moves = new List<ComboItem>(source.Moves);
        moves.RemoveAt(0);
        moves.Insert(0, any);
        foreach (var cb in new[] { CB_Move1, CB_Move2, CB_Move3, CB_Move4 })
        {
            cb.InitializeBinding();
            cb.DataSource = new BindingSource(moves, string.Empty);
        }
        foreach (var cb in new[] { CB_Species, CB_HeldItem, CB_Move1, CB_Move2, CB_Move3, CB_Move4 })
        {
            cb.SelectedIndex = 0;
            cb.Select(0, 0);
        }
        _suspendFilter = false;
    }

    #endregion

    #region Loading

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Grid.Focus(); // keep the opening shortcut's leftover keystroke out of the text fields
        try
        {
            var context = SAV.Context;
            var generation = SAV.Generation;
            var filterUnavailable = Main.Settings.MysteryDb.FilterUnavailableSpecies;
            RawDB = await Task.Run(() => LoadDatabase(context, generation, filterUnavailable)).ConfigureAwait(true);
            if (IsDisposed)
                return;
            AllTiles = RawDB.ConvertAll(CreateTile);
            PopulateComboBoxes();
            _loaded = true;
            Grid.EmptyText = "No gifts match these filters.";
            ApplyFilters();
            Grid.Focus();
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                WinFormsUtil.Error("Failed to load the Mystery Gift database.", ex);
        }
    }

    private static List<MysteryGift> LoadDatabase(EntityContext context, byte generation, bool filterUnavailable)
    {
        var db = EncounterEvent.GetAllEvents();
        if (filterUnavailable)
        {
            var filter = EntityPresenceFilters.GetFilterGift<MysteryGift>(context, generation);
            if (filter != null)
                db = db.Where(filter);
        }
        var list = db.ToList();
        foreach (var mg in list)
            mg.GiftUsed = false;
        return list;
    }

    #endregion

    #region Filtering

    /// <summary>
    /// Gifts usable by the loaded save. Saves older than Gen 4 predate most gift formats, so show everything there.
    /// </summary>
    private bool IsDefaultGen(byte gen) => SAV.Generation < 4 || gen <= SAV.Generation;

    private void SetGens(Func<byte, bool> predicate)
    {
        foreach (var (gen, chip) in GenChips)
            chip.Checked = predicate(gen);
        ApplyFilters();
    }

    private void ResetFilters()
    {
        _suspendFilter = true;
        foreach (var cb in new[] { CB_Species, CB_HeldItem, CB_Move1, CB_Move2, CB_Move3, CB_Move4 })
        {
            if (cb.Items.Count != 0)
                cb.SelectedIndex = 0;
        }
        TS_Shiny.Value = TS_Egg.Value = null;
        RTB_Instructions.Clear();
        QuickFilter.Clear();
        foreach (var (gen, chip) in GenChips)
            chip.Checked = IsDefaultGen(gen);
        _suspendFilter = false;
        ApplyFilters();
        WinFormsUtil.Asterisk();
    }

    private IEnumerable<TileItem> GetFilteredTiles()
    {
        IEnumerable<TileItem> res = AllTiles;
        static MysteryGift G(TileItem t) => (MysteryGift)t.Tag;

        var gens = GenChips.Where(z => z.Value.Checked).Select(z => z.Key).ToHashSet();
        if (gens.Count != GenChips.Count)
            res = res.Where(t => gens.Contains(G(t).Generation));

        var species = WinFormsUtil.GetIndex(CB_Species);
        var item = WinFormsUtil.GetIndex(CB_HeldItem);
        if (species != -1) res = res.Where(t => G(t).Species == species);
        if (item != -1) res = res.Where(t => G(t).HeldItem == item);

        foreach (var cb in new[] { CB_Move1, CB_Move2, CB_Move3, CB_Move4 })
        {
            var move = WinFormsUtil.GetIndex(cb);
            if (move != -1)
                res = res.Where(t => G(t).HasMove((ushort)move));
        }

        if (TS_Shiny.Value is { } shiny)
            res = res.Where(t => G(t).IsShiny == shiny);
        if (TS_Egg.Value is { } egg)
            res = res.Where(t => G(t).IsEgg == egg);

        ReadOnlySpan<char> batchText = RTB_Instructions.Text;
        if (batchText.Length != 0 && !StringInstructionSet.HasEmptyLine(batchText))
        {
            var filters = StringInstruction.GetFilters(batchText);
            EntityBatchEditor.ScreenStrings(filters);
            res = res.Where(t => BatchEditingUtil.IsFilterMatch(filters, G(t)));
        }

        res = SortBox.SelectedIndex switch
        {
            1 => res.OrderByDescending(t => G(t).Generation),
            2 => res.OrderBy(t => G(t).Species).ThenBy(t => G(t).Form),
            3 => res.OrderBy(t => G(t).Generation).ThenBy(t => G(t).CardID),
            4 => res.OrderBy(t => G(t).CardTitle, StringComparer.CurrentCultureIgnoreCase),
            _ => res,
        };
        return res;
    }

    private void ApplyFilters()
    {
        if (!_loaded || _suspendFilter)
            return;
        try
        {
            Grid.SetItems(ApplyQuickFilter(GetFilteredTiles()), keepSelection: true);
        }
        catch (Exception ex)
        {
            // Most likely a malformed batch filter line.
            WinFormsUtil.Error(ex.Message);
        }
        UpdateHeader();
    }

    protected override void OnQuickFilterChanged() => ApplyFilters();

    private void UpdateHeader()
    {
        if (!_loaded)
        {
            Header.Status = "Loading...";
            CountLabel.Text = string.Empty;
            return;
        }
        var shown = Grid.Items.Count;
        CountLabel.Text = shown == AllTiles.Count ? $"{shown:N0} gifts" : $"{shown:N0} of {AllTiles.Count:N0}";
        Header.Status = $"Gen {SAV.Generation} save";
    }

    private void AddInstruction()
    {
        var s = UC_Builder.Create();
        if (s.Length == 0)
        {
            WinFormsUtil.Alert(MsgBEPropertyInvalid);
            return;
        }
        var tb = RTB_Instructions;
        if (tb.Text.Length != 0 && !tb.Text.EndsWith('\n'))
            tb.AppendText(Environment.NewLine);
        tb.AppendText(s);
    }

    #endregion

    #region Tiles

    private string GetSpeciesName(ushort species) => species < Strings.Species.Count ? Strings.Species[species] : species.ToString();

    private static string CleanTitle(MysteryGift g) => g.CardTitle.Replace('　', ' ').Trim();

    private TileItem CreateTile(MysteryGift g)
    {
        var title = CleanTitle(g);
        string name;
        if (g.IsEntity && g.Species != 0)
            name = GetSpeciesName(g.Species);
        else if (g.IsItem && (uint)g.ItemID < Strings.itemlist.Length)
            name = Strings.itemlist[g.ItemID];
        else
            name = title.Length != 0 ? title : g.Type;

        var gen = g.Generation;
        return new TileItem(g, () => TidalSprites.GetClean(g))
        {
            Title = name,
            Subtitle = title.Length != 0 ? title : g.FileName,
            Corner = g.CardID > 0 ? $"#{g.CardID:0000}" : $"Gen {gen}",
            Badge = g.Type.ToUpperInvariant(),
            BadgeColor = gen < GenColors.Length ? GenColors[gen] : TidalPalette.BadgeOther,
            SearchText = $"{name} {title} {g.OriginalTrainerName} {g.Type} {g.CardID} {g.FileName} gen{gen}".ToLowerInvariant(),
        };
    }

    protected override void OnSelectionChanged(TileItem? item)
    {
        if (item?.Tag is not MysteryGift g)
        {
            Detail.Clear();
            return;
        }

        var gen = g.Generation;
        var badges = new List<DetailView.Badge>
        {
            new(g.Type.ToUpperInvariant(), gen < GenColors.Length ? GenColors[gen] : TidalPalette.BadgeOther),
            new($"Gen {gen}", TidalPalette.BadgeOther),
        };
        if (g.IsShiny)
            badges.Add(new("Shiny", TidalPalette.BadgeEgg));
        else if (g.Shiny == Shiny.Never)
            badges.Add(new("Shiny locked", TidalPalette.BadgeOther));
        if (g.IsEgg)
            badges.Add(new("Egg", TidalPalette.BadgeEgg));
        if (g.IsItem)
            badges.Add(new("Item", TidalPalette.BadgeStatic));

        var lines = new List<string>();
        lines.AddRange(g.GetTextLines(Main.Settings.Hover.HoverSlotShowEncounterVerbose, Main.CurrentLanguage).Skip(1));
        lines.Add($"File: {g.FileName}");
        Detail.Show(item.Image, item.Title, CleanTitle(g), badges, lines);
    }

    protected override void OnActivated(TileItem item) => LoadSelected();

    #endregion

    #region Actions

    private MysteryGift? Selected
    {
        get
        {
            if (Grid.SelectedItem?.Tag is MysteryGift g)
                return g;
            WinFormsUtil.Exclamation();
            return null;
        }
    }

    private void LoadSelected()
    {
        if (Selected is not { } gift)
            return;
        var temp = gift.ConvertToPKM(SAV, EncounterCriteria.Unrestricted);
        var pk = EntityConverter.ConvertToType(temp, SAV.PKMType, out var c);
        if (pk is null)
        {
            WinFormsUtil.Error(c.GetDisplayString(temp, SAV.PKMType));
            return;
        }
        SAV.AdaptToSaveFile(pk);
        pk.RefreshChecksum();
        PKME_Tabs.PopulateFields(pk, false);
        Footer.LeftText = $"Loaded {gift.FileName} into the editor.";
    }

    private void SavePKM()
    {
        if (Selected is not { } gift)
            return;
        WinFormsUtil.SavePKMDialog(gift.ConvertToPKM(SAV));
    }

    private void SaveGift()
    {
        if (Selected is not { } gift)
            return;
        if (gift is not DataMysteryGift g) // e.g. WC3
        {
            WinFormsUtil.Alert(MsgExportWC3DataFail);
            return;
        }
        WinFormsUtil.ExportMGDialog(g);
    }

    private void CopyDetails()
    {
        if (Grid.SelectedItem is null)
            return;
        WinFormsUtil.SetClipboardText(Detail.GetText());
        Footer.LeftText = "Details copied to clipboard.";
    }

    private List<MysteryGift> ShownGifts => Grid.Items.Select(z => (MysteryGift)z.Tag).ToList();

    private void ImportToBoxes()
    {
        var gifts = ShownGifts;
        if (gifts.Count == 0)
        {
            WinFormsUtil.Alert(MsgDBCreateReportFail);
            return;
        }
        if (!BoxView.GetBulkImportSettings(out var clearAll, out var overwrite, out var settings))
            return;

        int box = BoxView.Box.CurrentBox;
        int ctr = SAV.LoadBoxes(gifts, out var result, box, clearAll, overwrite, settings);
        if (ctr <= 0)
            return;

        BoxView.SetPKMBoxes();
        BoxView.UpdateBoxViewers();
        WinFormsUtil.Alert(result);
    }

    private void ExportResults()
    {
        var gifts = ShownGifts;
        if (gifts.Count == 0)
        {
            WinFormsUtil.Alert(MsgDBCreateReportFail);
            return;
        }
        if (DialogResult.Yes != WinFormsUtil.Prompt(MessageBoxButtons.YesNo, MsgDBExportResultsPrompt))
            return;

        using var fbd = new FolderBrowserDialog();
        if (DialogResult.OK != fbd.ShowDialog())
            return;

        var folder = fbd.SelectedPath;
        Directory.CreateDirectory(folder);
        foreach (var gift in gifts.OfType<DataMysteryGift>()) // WC3 have no data
        {
            var path = Path.Combine(folder, PathUtil.CleanFileName(gift.FileName));
            File.WriteAllBytes(path, gift.Write());
        }
        Footer.LeftText = $"Exported {gifts.Count:N0} gifts.";
    }

    private void OpenDB()
    {
        if (Directory.Exists(DatabasePath))
            Process.Start("explorer.exe", DatabasePath);
    }

    private void OpenClassic()
    {
        if (this.OpenWindowExists<SAV_MysteryGiftDB>())
            return;
        new SAV_MysteryGiftDB(PKME_Tabs, BoxView).Show();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                SaveGift();
                return true;
            case Keys.Control | Keys.I:
                ImportToBoxes();
                return true;
            case Keys.Control | Keys.E:
                ExportResults();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    #endregion
}
