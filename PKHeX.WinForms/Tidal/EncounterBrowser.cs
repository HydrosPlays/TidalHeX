using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PKHeX.Core;
using PKHeX.Core.Searching;
using PKHeX.Drawing.PokeSprite;
using PKHeX.WinForms.Controls;
using static PKHeX.Core.MessageStrings;

namespace PKHeX.WinForms.Tidal;

/// <summary>
/// Tidal replacement for <see cref="SAV_Encounters"/>: same search engine, card-grid presentation.
/// </summary>
public sealed class EncounterBrowser : TidalBrowserForm
{
    private readonly PKMEditor PKME_Tabs;
    private SaveFile SAV => PKME_Tabs.RequestSaveFile;
    private readonly TrainerDatabase Trainers;
    private readonly CancellationTokenSource TokenSource = new();

    // Filters
    private readonly ComboBox CB_Species = MakeCombo(true);
    private readonly ComboBox CB_Game = MakeCombo(true);
    private readonly ComboBox CB_Move1 = MakeCombo(true);
    private readonly ComboBox CB_Move2 = MakeCombo(true);
    private readonly ComboBox CB_Move3 = MakeCombo(true);
    private readonly ComboBox CB_Move4 = MakeCombo(true);
    private readonly TidalTriState TS_Shiny = new();
    private readonly TidalTriState TS_Egg = new();
    private readonly Dictionary<EncounterTypeGroup, TidalChip> TypeChips = [];
    private readonly TidalButton B_Search = new() { Text = "Search", Primary = true };
    private readonly TidalButton B_Reset = new() { Text = "Reset" };

    // Advanced
    private readonly EntityInstructionBuilder UC_Builder;
    private readonly RichTextBox RTB_Instructions = new() { Height = 110, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.None };
    private readonly PropertyGrid PG_Criteria = new() { ToolbarVisible = false, HelpVisible = true, Tag = VStack.Fill };
    private EncounterCriteria _criteria = EncounterCriteria.Unrestricted;

    // Results
    private List<TileItem> AllTiles = [];
    private readonly GameStrings Strings = GameInfo.Strings;

    private static readonly (EncounterTypeGroup Group, string Label)[] TypeLabels =
    [
        (EncounterTypeGroup.Slot, "Wild"),
        (EncounterTypeGroup.Static, "Static"),
        (EncounterTypeGroup.Trade, "Trades"),
        (EncounterTypeGroup.Egg, "Eggs"),
        (EncounterTypeGroup.Mystery, "Mystery Gifts"),
    ];

    public EncounterBrowser(PKMEditor f1, TrainerDatabase db)
    {
        PKME_Tabs = f1;
        Trainers = db;
        UC_Builder = new EntityInstructionBuilder(() => f1.PreparePKM()) { ReadOnly = true, Height = 46 };

        Text = "Encounter Database";
        Header.Text = "Encounter Database";
        Grid.EmptyText = "Pick a Pokémon, game or move on the left, then press Search.";
        Detail.Placeholder = "Select an encounter to see where and how it can be obtained.";

        BuildSearchSection();
        BuildAdvancedSection();
        BuildSettingsSection();

        SortBox.Items.AddRange(["Sort: Default", "Sort: Species", "Sort: Game", "Sort: Level", "Sort: Type"]);
        SortBox.SelectedIndex = 0;

        AddAction("Load into Editor", (_, _) => LoadSelected(), primary: true);
        AddAction("Save as File...", (_, _) => SaveSelected());
        AddAction("Copy Details", (_, _) => CopyDetails());

        var menu = new ContextMenuStrip();
        menu.Items.Add("Load into Editor", null, (_, _) => LoadSelected());
        menu.Items.Add("Save as File...", null, (_, _) => SaveSelected());
        menu.Items.Add("Copy Details", null, (_, _) => CopyDetails());
        Grid.ContextMenuStrip = menu;

        Footer.LeftText = "Shift+click a type chip to show only that type";
        Footer.SetHints(
            new("F5", "Search", Search),
            new("Enter", "Load", LoadSelected),
            new("Ctrl+S", "Save", SaveSelected),
            new("⟲", "Classic view", OpenClassic),
            new("Esc", "Close", Close));

        PopulateComboBoxes();
        UpdateCriteriaGrid(BuildCriteriaFromTabs());
        CheckIsSearchAllowed();
        UpdateHeader();
    }

    #region Build UI

    private void BuildSearchSection()
    {
        var s = AddSection("Search");
        s.Add(new TidalCaption("POKÉMON"));
        s.Add(CB_Species);
        s.Add(new TidalCaption("GAME"), 4);
        s.Add(CB_Game);
        s.Add(new TidalCaption("KNOWS MOVES"), 4);
        s.Add(MakeQuad(CB_Move1, CB_Move2, CB_Move3, CB_Move4));
        s.Add(MakePair(new TidalCaption("SHINY"), new TidalCaption("EGG"), 20), 2);
        s.Add(MakePair(TS_Shiny, TS_Egg, 28));
        s.Add(new TidalCaption("ENCOUNTER TYPES"), 4);

        var flow = MakeChipFlow();
        foreach (var (group, label) in TypeLabels)
        {
            var chip = new TidalChip { Text = label, Checked = true };
            chip.FitToText();
            chip.Click += (_, _) =>
            {
                if ((ModifierKeys & Keys.Shift) != 0)
                {
                    foreach (var c in TypeChips.Values)
                        c.Checked = c == chip;
                }
                CheckIsSearchAllowed();
            };
            TypeChips[group] = chip;
            flow.Controls.Add(chip);
        }
        s.Add(flow);
        s.Add(MakePair(B_Reset, B_Search, 38), 10);

        B_Search.Click += (_, _) => Search();
        B_Reset.Click += (_, _) => ResetFilters();
        CB_Species.SelectedIndexChanged += (_, _) => CheckIsSearchAllowed();
        foreach (var cb in new[] { CB_Species, CB_Game, CB_Move1, CB_Move2, CB_Move3, CB_Move4 })
        {
            cb.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter || cb.DroppedDown)
                    return;
                e.SuppressKeyPress = true;
                Search();
            };
            cb.SelectedIndexChanged += (_, _) => CheckIsSearchAllowed();
        }
    }

    private void BuildAdvancedSection()
    {
        var s = AddSection("Advanced");
        s.Add(new TidalCaption("BATCH FILTER BUILDER"));
        s.Add(UC_Builder);
        var add = new TidalButton { Text = "Add Filter Line", Height = 32 };
        add.Click += (_, _) => AddInstruction();
        s.Add(add, 2);
        s.Add(new TidalCaption("FILTER LINES (e.g. =Level=5)"), 4);
        s.Add(RTB_Instructions);

        s.Add(new TidalCaption("GENERATION CRITERIA (used when loading)"), 8);
        PG_Criteria.PropertyValueChanged += (_, _) =>
        {
            if (PG_Criteria.SelectedObject is EncounterCriteria crit)
                _criteria = crit;
        };
        s.Add(PG_Criteria);
        var fromTabs = new TidalButton { Text = "From Editor", Height = 32 };
        fromTabs.Click += (_, _) => { UpdateCriteriaGrid(BuildCriteriaFromTabs()); WinFormsUtil.Asterisk(); };
        var reset = new TidalButton { Text = "Unrestricted", Height = 32 };
        reset.Click += (_, _) => { UpdateCriteriaGrid(EncounterCriteria.Unrestricted); WinFormsUtil.Asterisk(); };
        s.Add(MakePair(fromTabs, reset, 32), 4);
    }

    private void BuildSettingsSection()
    {
        var s = AddSection("Settings");
        s.Add(new TidalCaption("ENCOUNTER DATABASE SETTINGS"));
        var grid = new PropertyGrid { ToolbarVisible = false, Tag = VStack.Fill };
        PropertyGridLocalization.Apply(grid, Main.Settings.EncounterDb, Main.CurrentLanguage);
        s.Add(grid);
    }

    private void PopulateComboBoxes()
    {
        CB_Species.InitializeBinding();
        CB_Game.InitializeBinding();

        var any = new ComboItem(MsgAny, 0);
        var filtered = GameInfo.FilteredSources;
        var source = filtered.Source;
        var species = new List<ComboItem>(source.SpeciesDataSource) { [0] = any };
        CB_Species.DataSource = species;

        var moves = new List<ComboItem>(filtered.Moves);
        moves.RemoveAt(0);
        moves.Insert(0, any);
        foreach (var cb in new[] { CB_Move1, CB_Move2, CB_Move3, CB_Move4 })
        {
            cb.InitializeBinding();
            cb.DataSource = new BindingSource(moves, string.Empty);
        }

        var versions = new List<ComboItem>(source.VersionDataSource);
        versions.Insert(0, any);
        versions.RemoveAt(versions.Count - 1);
        CB_Game.DataSource = versions;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        foreach (var cb in new[] { CB_Species, CB_Game, CB_Move1, CB_Move2, CB_Move3, CB_Move4 })
        {
            cb.SelectedIndex = 0;
            cb.Select(0, 0);
        }

        // Start from the Pokémon currently in the editor, so the common case is one click away.
        var current = PKME_Tabs.Data.Species;
        if (current != 0 && current <= SAV.MaxSpeciesID)
        {
            CB_Species.SelectedValue = (int)current;
            Search();
        }

        // Focus the grid, not a text field: the shortcut that opened this window (Ctrl+N) leaves a
        // control character in the queue that would otherwise land in the species box and clear it.
        Grid.Focus();
    }

    #endregion

    #region Criteria

    private void UpdateCriteriaGrid(EncounterCriteria value)
    {
        _criteria = value;
        PropertyGridLocalization.Apply(PG_Criteria, _criteria, Main.CurrentLanguage);
    }

    private EncounterCriteria BuildCriteriaFromTabs()
    {
        var editor = PKME_Tabs.Data;
        var set = new ShowdownSet(editor);
        var mutations = EncounterMutationUtil.GetSuggested(editor.Context, set.Level);
        var criteria = EncounterCriteria.GetCriteria(set, editor.PersonalInfo, mutations);
        if (editor.Context.IsHyperTrainingAvailable(100))
            criteria = criteria.ReviseIVsHyperTrainAvailable();
        return criteria;
    }

    private EncounterCriteria GetCriteria(IEncounterTemplate enc, EncounterDatabaseSettings settings)
    {
        if (!settings.UseTabsAsCriteria)
            return EncounterCriteria.Unrestricted;

        var editor = PKME_Tabs.Data;
        var tree = EvolutionTree.GetEvolutionTree(editor.Context);
        bool isInChain = tree.IsSpeciesDerivedFrom(editor.Species, editor.Form, enc.Species, enc.Form);

        if (!settings.UseTabsAsCriteriaAnySpecies && !isInChain)
            return EncounterCriteria.Unrestricted;

        var criteria = _criteria;
        if (!isInChain || EntityGender.IsSingleGender(enc.Species))
            criteria = criteria with { Gender = Gender.Random };
        if (!criteria.Mutations.CanGetAbility(enc.Ability, criteria.Ability))
            criteria = criteria with { Ability = AbilityPermission.Any12H };
        return criteria;
    }

    #endregion

    #region Search

    private EncounterTypeGroup[] GetTypes() => TypeChips.Where(z => z.Value.Checked).Select(z => z.Key).OrderBy(z => z).ToArray();

    private SearchSettings GetSearchSettings()
    {
        var settings = new SearchSettings
        {
            Context = SAV.Context,
            Generation = SAV.Generation,
            Species = GetU16(CB_Species),
            BatchInstructions = RTB_Instructions.Text,
            Version = (GameVersion)WinFormsUtil.GetIndex(CB_Game),
            SearchEgg = TS_Egg.Value,
            SearchShiny = TS_Shiny.Value,
        };
        settings.AddMove(GetU16(CB_Move1));
        settings.AddMove(GetU16(CB_Move2));
        settings.AddMove(GetU16(CB_Move3));
        settings.AddMove(GetU16(CB_Move4));
        return settings;

        static ushort GetU16(ListControl cb)
        {
            var val = WinFormsUtil.GetIndex(cb);
            return val <= 0 ? (ushort)0 : (ushort)val;
        }
    }

    private bool IsSearchAllowed(SearchSettings settings)
    {
        if (!TypeChips.Values.Any(z => z.Checked))
            return false;
        if (settings is { Species: 0, Moves.Count: 0 } && Main.Settings.EncounterDb.ReturnNoneIfEmptySearch)
            return false;
        return true;
    }

    private void CheckIsSearchAllowed() => B_Search.Enabled = IsSearchAllowed(GetSearchSettings());

    private IEnumerable<IEncounterInfo> SearchDatabase(CancellationToken token)
    {
        var settings = GetSearchSettings();
        if (!IsSearchAllowed(settings))
            return [];
        var pk = SAV.BlankPKM;

        var moves = settings.Moves.ToArray();
        var versions = settings.GetVersions(SAV);
        var species = settings.Species == 0 ? GetFullRange(SAV.MaxSpeciesID) : [settings.Species];
        var results = GetAllSpeciesFormEncounters(species, SAV.Personal, versions, moves, pk, token);
        if (settings.SearchEgg is { } egg)
            results = results.Where(z => z.IsEgg == egg);
        if (settings.SearchShiny is { } shiny)
            results = results.Where(z => z.IsShiny == shiny);

        results = results.Distinct(new ReferenceComparer<IEncounterInfo>());

        if (Main.Settings.EncounterDb.FilterUnavailableSpecies)
        {
            var filter = EntityPresenceFilters.GetFilterGeneric<IEncounterInfo>(SAV.Context);
            if (filter != null)
                results = results.Where(filter);
        }

        ReadOnlySpan<char> batchText = RTB_Instructions.Text;
        if (batchText.Length != 0 && !StringInstructionSet.HasEmptyLine(batchText))
        {
            var filters = StringInstruction.GetFilters(batchText);
            EntityBatchEditor.ScreenStrings(filters);
            results = results.Where(enc => BatchEditingUtil.IsFilterMatch(filters, enc));
        }
        return results;
    }

    private static IEnumerable<ushort> GetFullRange(int max)
    {
        for (ushort i = 1; i <= max; i++)
            yield return i;
    }

    private IEnumerable<IEncounterInfo> GetAllSpeciesFormEncounters(IEnumerable<ushort> species, IPersonalTable pt, ReadOnlyMemory<GameVersion> versions, ReadOnlyMemory<ushort> moves, PKM pk, CancellationToken token)
    {
        foreach (var s in species)
        {
            if (token.IsCancellationRequested)
                break;

            var pi = pt.GetFormEntry(s, 0);
            var fc = pi.FormCount;
            if (fc == 0 && !Main.Settings.EncounterDb.FilterUnavailableSpecies)
            {
                pi = PersonalTable.USUM.GetFormEntry(s, 0);
                fc = pi.FormCount;
            }
            for (byte f = 0; f < fc; f++)
            {
                if (FormInfo.IsBattleOnlyForm(s, f, pk.Format))
                    continue;
                pk.Species = s;
                pk.Form = f;
                pk.SetGender(pk.GetSaneGender());
                EncounterMovesetGenerator.OptimizeCriteria(pk, SAV);
                foreach (var enc in EncounterMovesetGenerator.GenerateEncounters(pk, moves, versions))
                    yield return enc;
            }
        }
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private bool _searching;

    // ReSharper disable once AsyncVoidMethod
    private async void Search()
    {
        if (_searching || !B_Search.Enabled)
            return;
        if (Application.OpenForms.OfType<SAV_Encounters>().Any())
        {
            WinFormsUtil.Alert("Close the classic Encounter Database window first; both share the same search engine settings.");
            return;
        }
        _searching = true;
        B_Search.Enabled = false;
        B_Search.Text = "Searching...";
        Header.Status = "Searching...";
        Grid.SetItems([]);
        Grid.EmptyText = "Searching...";
        try
        {
            EncounterMovesetGenerator.PriorityList = GetTypes();
            var token = TokenSource.Token;
            var search = SearchDatabase(token);
            var results = await Task.Run(search.ToList, token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            AllTiles = results.ConvertAll(CreateTile);
            Grid.EmptyText = "No encounters match these filters.";
            RefreshGrid(keepSelection: false);
            if (AllTiles.Count != 0 && ContainsFocus)
                Grid.Focus();
            WinFormsUtil.Asterisk();
        }
        catch (OperationCanceledException)
        {
            // Window closed mid-search.
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error("Encounter search failed.", ex);
        }
        finally
        {
            EncounterMovesetGenerator.ResetFilters();
            _searching = false;
            if (!IsDisposed)
            {
                B_Search.Text = "Search";
                CheckIsSearchAllowed();
                UpdateHeader();
            }
        }
    }

    private void ResetFilters()
    {
        CB_Species.SelectedIndex = 0;
        CB_Move1.SelectedIndex = CB_Move2.SelectedIndex = CB_Move3.SelectedIndex = CB_Move4.SelectedIndex = 0;
        CB_Game.SelectedIndex = 0;
        RTB_Instructions.Clear();
        TS_Shiny.Value = TS_Egg.Value = null;
        foreach (var chip in TypeChips.Values)
            chip.Checked = true;
        CheckIsSearchAllowed();
        WinFormsUtil.Asterisk();
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

    private TileItem CreateTile(IEncounterInfo enc)
    {
        var species = GetSpeciesDisplay(enc);
        var version = GetVersionName(enc.Version);
        var location = enc.GetEncounterLocation() ?? string.Empty;
        var (badge, color) = GetTypeBadge(enc);
        var level = enc.LevelMin == enc.LevelMax ? $"Lv. {enc.LevelMin}" : $"Lv. {enc.LevelMin}-{enc.LevelMax}";
        return new TileItem(enc, () => TidalSprites.GetClean(enc))
        {
            Title = species,
            Subtitle = location.Length != 0 ? location : version,
            Corner = level,
            Badge = badge,
            BadgeColor = color,
            SearchText = $"{species} {version} {location} {badge} {enc.Version}".ToLowerInvariant(),
        };
    }

    private string GetSpeciesDisplay(IEncounterTemplate enc)
    {
        var name = enc.Species < Strings.Species.Count ? Strings.Species[enc.Species] : enc.Species.ToString();
        if (enc.Form == 0)
            return name;
        var form = FormConverter.GetStringFromForm(enc.Species, enc.Form, Strings, enc.Context);
        return string.IsNullOrWhiteSpace(form) ? name : $"{name}-{form}";
    }

    private string GetVersionName(GameVersion version) => version.IsValidSavedVersion() && (int)version < Strings.gamelist.Length
        ? Strings.gamelist[(int)version]
        : version.ToString();

    private string GetBallName(Ball ball) => (uint)ball < Strings.balllist.Length ? Strings.balllist[(int)ball] : $"{ball} Ball";

    /// <summary>
    /// Raid-style encounters: Gen 8 Max Raid dens / Max Lair (EncounterStatic8N*, 8U) and Gen 9 Tera raids (Tera9, Dist9, Might9).
    /// </summary>
    private static bool IsRaid(string name, string type) =>
        name.Contains("Raid", StringComparison.Ordinal)
        || type.StartsWith("EncounterStatic8N", StringComparison.Ordinal)
        || type.StartsWith("EncounterStatic8U", StringComparison.Ordinal)
        || type.StartsWith("EncounterTera9", StringComparison.Ordinal)
        || type.StartsWith("EncounterDist9", StringComparison.Ordinal)
        || type.StartsWith("EncounterMight9", StringComparison.Ordinal);

    internal static (string Text, Color Color) GetTypeBadge(IEncounterTemplate enc)
    {
        if (enc is MysteryGift)
            return ("Event", TidalPalette.BadgeGift);
        var name = enc is IEncounterable e ? e.Name : string.Empty;
        var type = enc.GetType().Name;
        if (enc.IsEgg || name.Contains("Egg"))
            return ("Egg", TidalPalette.BadgeEgg);
        if (name.Contains("Trade") || type.Contains("Trade"))
            return ("Trade", TidalPalette.BadgeTrade);
        if (name.Contains("Wild") || type.Contains("Slot"))
            return ("Wild", TidalPalette.BadgeWild);
        if (IsRaid(name, type))
            return ("Raid", TidalPalette.BadgeTrade);
        if (name.Contains("Outbreak") || type.Contains("Outbreak"))
            return ("Outbreak", TidalPalette.BadgeWild);
        if (name.Contains("Gift") || type.Contains("Gift"))
            return ("Gift", TidalPalette.BadgeGift);
        return ("Static", TidalPalette.BadgeStatic);
    }

    private void RefreshGrid(bool keepSelection)
    {
        IEnumerable<TileItem> items = AllTiles;
        items = SortBox.SelectedIndex switch
        {
            1 => items.OrderBy(z => ((IEncounterInfo)z.Tag).Species).ThenBy(z => ((IEncounterInfo)z.Tag).Form),
            2 => items.OrderBy(z => (int)((IEncounterInfo)z.Tag).Version),
            3 => items.OrderBy(z => ((IEncounterInfo)z.Tag).LevelMin),
            4 => items.OrderBy(z => z.Badge, StringComparer.Ordinal),
            _ => items,
        };
        Grid.SetItems(ApplyQuickFilter(items), keepSelection);
        UpdateHeader();
    }

    private void UpdateHeader()
    {
        var shown = Grid.Items.Count;
        var total = AllTiles.Count;
        CountLabel.Text = total == 0 ? string.Empty : shown == total ? $"{total:N0} results" : $"{shown:N0} of {total:N0}";
        Header.Status = $"{GetVersionName(SAV.Version)} save  •  Gen {SAV.Generation}";
    }

    protected override void OnQuickFilterChanged() => RefreshGrid(keepSelection: true);

    protected override void OnSelectionChanged(TileItem? item)
    {
        if (item?.Tag is not IEncounterInfo enc)
        {
            Detail.Clear();
            return;
        }

        var (badge, color) = GetTypeBadge(enc);
        var badges = new List<DetailView.Badge>
        {
            new(badge, color),
            new($"Gen {enc.Generation}", TidalPalette.BadgeOther),
            new(GetVersionName(enc.Version), TidalPalette.BadgeOther),
        };
        if (enc.Shiny == Shiny.Never)
            badges.Add(new("Shiny locked", TidalPalette.BadgeOther));
        else if (enc.IsShiny)
            badges.Add(new("Shiny", TidalPalette.BadgeEgg));
        if (enc.FixedBall != Ball.None)
            badges.Add(new(GetBallName(enc.FixedBall), TidalPalette.BadgeOther));
        if (enc is IAlphaReadOnly { IsAlpha: true })
            badges.Add(new("Alpha", TidalPalette.BadgeGift));
        if (enc is IGigantamaxReadOnly { CanGigantamax: true })
            badges.Add(new("Gigantamax", TidalPalette.BadgeGift));

        var lines = enc.GetTextLines(Main.Settings.Hover.HoverSlotShowEncounterVerbose, Main.CurrentLanguage).Skip(1);
        var subtitle = enc is IEncounterable e ? e.LongName : string.Empty;
        Detail.Show(item.Image, item.Title, subtitle, badges, lines);
    }

    protected override void OnActivated(TileItem item) => LoadSelected();

    #endregion

    #region Actions

    private bool TryCreate(out PKM pk)
    {
        pk = null!;
        if (Grid.SelectedItem?.Tag is not IEncounterInfo enc)
        {
            WinFormsUtil.Exclamation();
            return false;
        }

        var criteria = GetCriteria(enc, Main.Settings.EncounterDb);
        var trainer = Trainers.GetTrainer(enc.Version, enc.Generation <= 2 ? (LanguageID)SAV.Language : null) ?? SAV;
        var temp = enc.ConvertToPKM(trainer, criteria);
        var converted = EntityConverter.ConvertToType(temp, SAV.PKMType, out var c);
        if (converted is null)
        {
            WinFormsUtil.Error(c.GetDisplayString(temp, SAV.PKMType));
            return false;
        }
        SAV.AdaptToSaveFile(converted);
        converted.RefreshChecksum();
        pk = converted;
        return true;
    }

    private void LoadSelected()
    {
        if (!TryCreate(out var pk))
            return;
        PKME_Tabs.PopulateFields(pk, false);
        Footer.LeftText = $"Loaded {Grid.SelectedItem?.Title} into the editor.";
    }

    private void SaveSelected()
    {
        if (TryCreate(out var pk))
            WinFormsUtil.SavePKMDialog(pk);
    }

    private void CopyDetails()
    {
        if (Grid.SelectedItem is null)
            return;
        WinFormsUtil.SetClipboardText(Detail.GetText());
        Footer.LeftText = "Details copied to clipboard.";
    }

    private void OpenClassic()
    {
        if (_searching)
        {
            WinFormsUtil.Alert("Wait for the current search to finish before opening the classic view.");
            return;
        }
        if (this.OpenWindowExists<SAV_Encounters>())
            return;
        new SAV_Encounters(PKME_Tabs, Trainers).Show();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5:
                Search();
                return true;
            case Keys.Control | Keys.S:
                SaveSelected();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    #endregion

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        TokenSource.Cancel();
        base.OnFormClosing(e);
    }
}
