using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Admin;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Controls;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class LibraryPage : Page
{
    // B42: Persist last-viewed tab + filters per library across navigations.
    // Web parses this from the URL (?tab=library|collections); we don't have
    // routing yet, so we mirror the page state in a per-library in-memory dict.
    private sealed class LibraryViewState
    {
        public string Tab { get; set; } = "Recommended";
        public string Sort { get; set; } = "sort_title";
        public string Order { get; set; } = "asc";
        public string? MediaType { get; set; }
        public string? Genre { get; set; }
        public string? ContentRating { get; set; }
        public string? Studio { get; set; }
        public string? Country { get; set; }
        public string? Resolution { get; set; }
        public string? AudioLanguage { get; set; }
        public string? YearMin { get; set; }
        public string? YearMax { get; set; }
    }

    private sealed class FilterOption(string label, string value)
    {
        public string Label { get; } = label;
        public string Value { get; } = value;

        public override string ToString() => Label;
    }

    private static readonly Dictionary<int, LibraryViewState> _viewStateByLibrary = new();

    public LibraryViewModel ViewModel { get; }
    private bool _suppressFilterEvents;
    private bool _recommendedLoaded;
    private bool _collectionsLoaded;
    private bool _libraryCatalogLoaded;
    private int _recommendationsVersion;
    private bool _orderAsc = true;
    private string _currentTab = "Recommended";
    private bool _overlayMode;
    private bool _scrollListenerAttached;
    private DispatcherTimer? _yearDebounceTimer;
    private DispatcherTimer? _visibleRangeDebounceTimer;
    private bool _forceVisibleRangeLoad;
    private int _lastRequestedStartIndex = -1;
    private int _lastRequestedEndIndex = -1;
    private int _currentFirstRow;
    private bool _suppressScrollBarValueChanged;
    private readonly Dictionary<int, LibraryGridCard> _visibleLibraryCards = [];
    private readonly List<LibraryGridCard> _libraryCardSlots = [];
    private readonly Queue<int> _pendingCardBinds = [];
    private readonly HashSet<int> _pendingCardBindSet = [];
    private int _lastRenderedStartIndex = -1;
    private int _lastRenderedEndIndex = -1;
    private bool _renderQueued;
    private bool _forceQueuedRender;
    private DispatcherTimer? _cardBindTimer;
    private bool _isNavigated;
    private bool _viewModelEventsAttached;
    private const int CardBindsPerTick = 12;
    private const int MaxRealizedLibraryCards = 40;
    private const int LibraryOverscanRows = 1;

    public LibraryPage()
    {
        ViewModel = App.Services.GetRequiredService<LibraryViewModel>();
        this.InitializeComponent();
        AttachViewModelEvents();
        UpdateVirtualGridMetrics();
        _visibleRangeDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _visibleRangeDebounceTimer.Tick += VisibleRangeDebounceTimer_Tick;
        _cardBindTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _cardBindTimer.Tick += CardBindTimer_Tick;

        _suppressFilterEvents = true;
        SortComboBox.SelectedIndex = 0;
        _suppressFilterEvents = false;

        ViewModel.Genres.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateGenreCombo());

        ViewModel.ContentRatings.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateContentRatingCombo());

        ViewModel.Studios.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateStudioCombo());

        ViewModel.Countries.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateCountryCombo());

        ViewModel.Resolutions.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateResolutionCombo());

        ViewModel.AudioLanguages.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateAudioLangCombo());

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.TotalCount) ||
                args.PropertyName == nameof(ViewModel.DisplayTotalCount))
            {
                DispatcherQueue.TryEnqueue(() => UpdateCountDisplay());
            }
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isNavigated = true;
        AttachViewModelEvents();

        if (e.Parameter is Library library)
        {
            LibraryTitle.Text = library.Name;
            LibraryEyebrowName.Text = library.Name.ToUpperInvariant();
            ViewModel.Library = library;

            // F7: set window title to the library name
            if (App.MainWindowInstance is MainWindow mw)
                mw.SetDynamicTitle(library.Name);

            // B42: Restore previously-viewed tab + filters for this library if any.
            // Falls back to fresh defaults on first visit.
            _viewStateByLibrary.TryGetValue(library.Id, out var state);
            state ??= new LibraryViewState();

            _suppressFilterEvents = true;
            SortComboBox.SelectedIndex = IndexOfSortTag(state.Sort);
            MediaTypeComboBox.SelectedIndex = IndexOfMediaType(state.MediaType);
            GenreComboBox.SelectedIndex = -1;
            ContentRatingComboBox.SelectedIndex = -1;
            StudioComboBox.SelectedIndex = -1;
            CountryComboBox.SelectedIndex = -1;
            ResolutionComboBox.SelectedIndex = -1;
            AudioLangComboBox.SelectedIndex = -1;
            YearMinBox.Text = state.YearMin ?? "";
            YearMaxBox.Text = state.YearMax ?? "";
            _orderAsc = state.Order != "desc";
            UpdateOrderButton();
            ViewModel.SelectedSort = state.Sort;
            Controls.PosterCard.CurrentSortKey = state.Sort;
            ViewModel.SelectedOrder = state.Order;
            ViewModel.SelectedType = state.MediaType;
            ViewModel.SelectedGenre = state.Genre;
            ViewModel.SelectedContentRating = state.ContentRating;
            ViewModel.SelectedStudio = state.Studio;
            ViewModel.SelectedCountry = state.Country;
            ViewModel.SelectedResolution = state.Resolution;
            ViewModel.SelectedAudioLanguage = state.AudioLanguage;
            ViewModel.SelectedYearMin = state.YearMin;
            ViewModel.SelectedYearMax = state.YearMax;
            _suppressFilterEvents = false;
            _recommendedLoaded = false;
            _collectionsLoaded = false;
            _libraryCatalogLoaded = false;
            ActiveFiltersBar.Visibility = Visibility.Collapsed;

            ShowTab(state.Tab);

            if (state.Tab == "Recommended" && !_recommendedLoaded)
                await LoadRecommendationsAsync();

            if (state.Tab == "Library")
            {
                await EnsureLibraryCatalogLoadedAsync();
                await FillViewportAsync();
            }
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _isNavigated = false;
        _visibleRangeDebounceTimer?.Stop();
        _cardBindTimer?.Stop();
        _currentFirstRow = 0;
        _pendingCardBinds.Clear();
        _pendingCardBindSet.Clear();
        ViewModel.CancelCatalogLoads();
        DetachViewModelEvents();
        ClearVirtualCards();
    }

    private void AttachViewModelEvents()
    {
        if (_viewModelEventsAttached) return;
        ViewModel.WindowLoaded += OnLibraryWindowLoaded;
        _viewModelEventsAttached = true;
    }

    private void DetachViewModelEvents()
    {
        if (!_viewModelEventsAttached) return;
        ViewModel.WindowLoaded -= OnLibraryWindowLoaded;
        _viewModelEventsAttached = false;
    }

    private static int IndexOfSortTag(string tag) => tag switch
    {
        "sort_title" => 0,
        "recently_added" => 1,
        "year" => 2,
        "rating_imdb" => 3,
        _ => 0,
    };

    private static int IndexOfMediaType(string? type) => type switch
    {
        "movie" => 1,
        "series" => 2,
        _ => 0,
    };

    /// <summary>B42: Persist current page state for this library.</summary>
    private void SaveViewState(string tab)
    {
        if (ViewModel.Library == null) return;
        _viewStateByLibrary[ViewModel.Library.Id] = new LibraryViewState
        {
            Tab = tab,
            Sort = ViewModel.SelectedSort ?? "sort_title",
            Order = ViewModel.SelectedOrder ?? "asc",
            MediaType = ViewModel.SelectedType,
            Genre = ViewModel.SelectedGenre,
            ContentRating = ViewModel.SelectedContentRating,
            Studio = ViewModel.SelectedStudio,
            Country = ViewModel.SelectedCountry,
            Resolution = ViewModel.SelectedResolution,
            AudioLanguage = ViewModel.SelectedAudioLanguage,
            YearMin = ViewModel.SelectedYearMin,
            YearMax = ViewModel.SelectedYearMax,
        };
    }

    private void ShowTab(string tag)
    {
        // Update tab button styles
        var tabs = new[] { RecommendedTab, LibraryTab, CollectionsTab };
        foreach (var tab in tabs)
        {
            tab.Style = (Style)Resources["PillTabButtonStyle"];
        }

        Button activeTab = tag switch
        {
            "Library" => LibraryTab,
            "Collections" => CollectionsTab,
            _ => RecommendedTab
        };
        activeTab.Style = (Style)Resources["PillTabButtonActiveStyle"];

        // Toggle panel visibility
        if (tag == "Library")
            ReleaseRecommendedContent();

        FilterBar.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        LibraryContentArea.Visibility = tag == "Library" ? Visibility.Visible : Visibility.Collapsed;
        RecommendedPanel.Visibility = tag == "Recommended" ? Visibility.Visible : Visibility.Collapsed;
        CollectionsPanel.Visibility = tag == "Collections" ? Visibility.Visible : Visibility.Collapsed;

        _currentTab = tag;
        // B42: Persist tab selection so a return to this library lands on the same tab.
        SaveViewState(tag);

        // Overlay mode: when Recommended tab is active and hero is showing,
        // make header transparent to overlay the hero banner.
        UpdateHeaderOverlayMode(tag == "Recommended" && RecommendedHeroCarousel.Visibility == Visibility.Visible);
    }

    /// <summary>
    /// Keeps the virtualized library grid backed by data for the visible
    /// viewport after first load and filter changes.
    /// </summary>
    private async Task FillViewportAsync()
    {
        if (!_isNavigated || LibraryContentArea == null)
            return;

        UpdateVirtualGridMetrics();
        RenderVirtualGrid();
        _lastRequestedStartIndex = -1;
        _lastRequestedEndIndex = -1;
        await EnsureVisibleRangeLoadedAsync(force: true);
    }

    private void UpdateCountDisplay()
    {
        if (ViewModel.DisplayTotalCount > 0)
        {
            CountText.Text = ViewModel.DisplayTotalCount.ToString("N0");
            CountLabel.Text = ViewModel.DisplayTotalCount == 1 ? "item" : "items";
        }
        else if (ViewModel.TotalCount > 0)
        {
            CountText.Text = "...";
            CountLabel.Text = "items";
        }
        else
        {
            CountText.Text = "0";
            CountLabel.Text = "items";
        }
    }

    private static List<FilterOption> BuildFilterOptions(IEnumerable<string> values, string allLabel)
    {
        var options = new List<FilterOption> { new(allLabel, "") };
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                options.Add(new FilterOption(value, value));
        }

        return options;
    }

    private void UpdateFilterCombo(
        ComboBox comboBox,
        IEnumerable<string> values,
        string allLabel,
        string? selectedValue,
        string breadcrumbName)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var options = BuildFilterOptions(values, allLabel);
        App.SetPerfBreadcrumb($"Library filters {breadcrumbName} start count={options.Count}");

        _suppressFilterEvents = true;
        comboBox.DisplayMemberPath = nameof(FilterOption.Label);
        comboBox.SelectedValuePath = nameof(FilterOption.Value);
        comboBox.ItemsSource = options;
        comboBox.SelectedItem = options.FirstOrDefault(option => option.Value == selectedValue) ?? options[0];
        _suppressFilterEvents = false;

        App.SetPerfBreadcrumb($"Library filters {breadcrumbName} end count={options.Count} elapsed_ms={stopwatch.ElapsedMilliseconds}");
    }

    private static string? SelectedFilterValue(ComboBox comboBox)
    {
        return comboBox.SelectedItem switch
        {
            FilterOption option => string.IsNullOrEmpty(option.Value) ? null : option.Value,
            ComboBoxItem item when item.Tag is string tag => string.IsNullOrEmpty(tag) ? null : tag,
            _ => null
        };
    }

    private void UpdateGenreCombo()
    {
        UpdateFilterCombo(GenreComboBox, ViewModel.Genres, "All Genres", ViewModel.SelectedGenre, "genres");
    }

    private void UpdateContentRatingCombo()
    {
        UpdateFilterCombo(ContentRatingComboBox, ViewModel.ContentRatings, "All Ratings", ViewModel.SelectedContentRating, "ratings");
    }

    private void UpdateOrderButton()
    {
        OrderIcon.Glyph = _orderAsc ? "\uE74A" : "\uE74B"; // Up arrow : Down arrow
        ToolTipService.SetToolTip(OrderToggleButton, _orderAsc ? "Ascending" : "Descending");
    }

    private async void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (SortComboBox.SelectedItem is ComboBoxItem item && item.Tag is string sort)
        {
            ViewModel.SelectedSort = sort;
            // Keep non-library PosterCards in sync with the current sort meta.
            // The virtualized Library tab passes ViewModel.SelectedSort directly.
            Controls.PosterCard.CurrentSortKey = sort;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            SaveViewState(_currentTab); // B42
        }
    }

    private async void OrderToggle_Click(object sender, RoutedEventArgs e)
    {
        _orderAsc = !_orderAsc;
        UpdateOrderButton();
        ViewModel.SelectedOrder = _orderAsc ? "asc" : "desc";
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        SaveViewState(_currentTab); // B42
    }

    private async void MediaTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (MediaTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string type)
        {
            ViewModel.SelectedType = string.IsNullOrEmpty(type) ? null : type;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            UpdateActiveFilterBadges();
            SaveViewState(_currentTab); // B42
        }
    }

    private async void GenreComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;

        ViewModel.SelectedGenre = SelectedFilterValue(GenreComboBox);
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
        SaveViewState(_currentTab); // B42
    }

    private async void ContentRatingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;

        ViewModel.SelectedContentRating = SelectedFilterValue(ContentRatingComboBox);
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
        SaveViewState(_currentTab); // B42
    }

    private void YearFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;

        // Debounce year filter changes
        _yearDebounceTimer?.Stop();
        _yearDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _yearDebounceTimer.Tick += async (s, args) =>
        {
            _yearDebounceTimer?.Stop();
            _yearDebounceTimer = null;

            ViewModel.SelectedYearMin = string.IsNullOrWhiteSpace(YearMinBox.Text) ? null : YearMinBox.Text;
            ViewModel.SelectedYearMax = string.IsNullOrWhiteSpace(YearMaxBox.Text) ? null : YearMaxBox.Text;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            UpdateActiveFilterBadges();
            SaveViewState(_currentTab); // B42
        };
        _yearDebounceTimer.Start();
    }

    private void LibraryScrollBar_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isNavigated || _suppressScrollBarValueChanged)
            return;

        SetLibraryFirstRow((int)Math.Round(e.NewValue));
    }

    private void LibraryViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isNavigated)
            return;

        UpdateVirtualGridMetrics();
        QueueRenderVirtualGrid(force: true);
        ScheduleVisibleRangeLoad(force: true);
    }

    private void LibraryViewport_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!_isNavigated)
            return;

        var delta = e.GetCurrentPoint(LibraryViewportHost).Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        var layout = GetGridLayout();
        var visibleRows = VirtualGridScrollGate.GetVisibleRowCount(GetLibraryViewportHeight(), layout.RowHeight);
        var rows = delta < 0 ? visibleRows : -visibleRows;
        SetLibraryFirstRow(_currentFirstRow + rows);
        e.Handled = true;
    }

    private void SetLibraryFirstRow(int firstRow, bool force = false)
    {
        var layout = GetGridLayout();
        var visibleRows = VirtualGridScrollGate.GetVisibleRowCount(GetLibraryViewportHeight(), layout.RowHeight);
        var maxFirstRow = VirtualGridScrollGate.GetMaxFirstVisibleRow(ViewModel.TotalCount, layout.Columns, visibleRows);
        var clamped = Math.Clamp(firstRow, 0, maxFirstRow);

        if (!force && clamped == _currentFirstRow)
            return;

        _currentFirstRow = clamped;
        SetScrollBarValue(clamped);

        var scrollToTopVisibility = clamped > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (ScrollToTopButton.Visibility != scrollToTopVisibility)
            ScrollToTopButton.Visibility = scrollToTopVisibility;

        App.SetPerfBreadcrumb($"Library scroll row={clamped} cards={_visibleLibraryCards.Count} pending={_pendingCardBinds.Count} loading={ViewModel.IsLoading}");
        QueueRenderVirtualGrid(force);
        ScheduleVisibleRangeLoad(force);
    }

    private void SetScrollBarValue(double value)
    {
        if (LibraryScrollBar.Value == value)
            return;

        _suppressScrollBarValueChanged = true;
        try { LibraryScrollBar.Value = value; }
        finally { _suppressScrollBarValueChanged = false; }
    }

    private void OnLibraryWindowLoaded()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isNavigated)
                return;

            UpdateVirtualGridMetrics();
            QueueRenderVirtualGrid(force: true);
        });
    }

    private void QueueRenderVirtualGrid(bool force = false)
    {
        if (!_isNavigated)
            return;

        _forceQueuedRender |= force;
        if (_renderQueued) return;

        _renderQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isNavigated)
            {
                _renderQueued = false;
                _forceQueuedRender = false;
                return;
            }

            _renderQueued = false;
            var shouldForce = _forceQueuedRender;
            _forceQueuedRender = false;
            RenderVirtualGrid(shouldForce);
        });
    }

    private void UpdateVirtualGridMetrics()
    {
        var total = ViewModel.TotalCount;
        var layout = GetGridLayout();
        var viewportHeight = GetLibraryViewportHeight();
        var visibleRows = VirtualGridScrollGate.GetVisibleRowCount(viewportHeight, layout.RowHeight);
        var maxFirstRow = VirtualGridScrollGate.GetMaxFirstVisibleRow(total, layout.Columns, visibleRows);

        VirtualGridCanvas.Width = layout.AvailableWidth;
        VirtualGridCanvas.Height = Math.Max(layout.RowHeight, viewportHeight);

        LibraryScrollBar.Maximum = maxFirstRow;
        LibraryScrollBar.ViewportSize = Math.Max(1, visibleRows);
        LibraryScrollBar.LargeChange = Math.Max(1, visibleRows - 1);

        if (_currentFirstRow > maxFirstRow)
            _currentFirstRow = maxFirstRow;
        SetScrollBarValue(_currentFirstRow);
    }

    private void RenderVirtualGrid(bool force = false)
    {
        App.SetPerfBreadcrumb($"Library render start force={force} cards={_visibleLibraryCards.Count} total={ViewModel.TotalCount}");
        var total = ViewModel.TotalCount;
        if (!_isNavigated || total <= 0 || LibraryContentArea == null || LibraryContentArea.Visibility != Visibility.Visible)
        {
            App.SetPerfBreadcrumb($"Library render clear cards={_visibleLibraryCards.Count} total={total}");
            ClearVirtualCards();
            _lastRenderedStartIndex = -1;
            _lastRenderedEndIndex = -1;
            App.SetPerfBreadcrumb("Library render clear end");
            return;
        }

        EnsureVirtualCardSlots();

        var layout = GetGridLayout();
        var range = GetVisibleItemRange(layout);
        range = ClampRealizedRange(range, layout);
        var windowStartRow = range.StartIndex / layout.Columns;

        App.SetPerfBreadcrumb($"Library render range {range.StartIndex}-{range.EndIndex} row={windowStartRow} last={_lastRenderedStartIndex}-{_lastRenderedEndIndex} cards={_visibleLibraryCards.Count}");
        if (!force &&
            range.StartIndex == _lastRenderedStartIndex &&
            range.EndIndex == _lastRenderedEndIndex)
        {
            App.SetPerfBreadcrumb($"Library render unchanged end cards={_visibleLibraryCards.Count} pending={_pendingCardBinds.Count}");
            return;
        }

        _lastRenderedStartIndex = range.StartIndex;
        _lastRenderedEndIndex = range.EndIndex;

        _cardBindTimer?.Stop();
        _pendingCardBinds.Clear();
        _pendingCardBindSet.Clear();
        _visibleLibraryCards.Clear();

        var slotIndex = 0;
        for (int index = range.StartIndex; index <= range.EndIndex; index++)
        {
            if (slotIndex >= _libraryCardSlots.Count)
                break;

            var card = _libraryCardSlots[slotIndex];
            _visibleLibraryCards[index] = card;
            card.Tag = index;
            PositionVirtualCardSlot(card, slotIndex, layout);
            if (card.Visibility != Visibility.Visible)
                card.Visibility = Visibility.Visible;
            slotIndex++;

            var item = ViewModel.GetWindowItem(index);
            if (item == null)
            {
                card.BindPlaceholder();
                continue;
            }

            QueueCardBind(index);
        }

        for (int i = slotIndex; i < _libraryCardSlots.Count; i++)
            HideVirtualCardSlot(_libraryCardSlots[i]);

        App.SetPerfBreadcrumb($"Library render end cards={_visibleLibraryCards.Count} pending={_pendingCardBinds.Count} row={windowStartRow} total={ViewModel.TotalCount}");
    }

    private void UpdateVisibleCardItems()
    {
        App.SetPerfBreadcrumb($"Library update visible start cards={_visibleLibraryCards.Count}");
        foreach (var (index, card) in _visibleLibraryCards)
        {
            if (ViewModel.GetWindowItem(index) == null) continue;
            card.Tag = index;
            QueueCardBind(index);
        }
        App.SetPerfBreadcrumb($"Library update visible end cards={_visibleLibraryCards.Count} pending={_pendingCardBinds.Count}");
    }

    private void RefreshChangedVirtualItems(int startIndex, int count)
    {
        if (startIndex < 0 || count <= 0) return;

        var endIndex = startIndex + count - 1;
        foreach (var (index, card) in _visibleLibraryCards)
        {
            if (index < startIndex || index > endIndex || ViewModel.GetWindowItem(index) == null)
                continue;

            card.Tag = index;
            QueueCardBind(index);
        }

        QueueRenderVirtualGrid(force: true);
    }

    private void EnsureVirtualCardSlots()
    {
        while (_libraryCardSlots.Count < MaxRealizedLibraryCards)
        {
            var card = new LibraryGridCard();
            HideVirtualCardSlot(card);
            _libraryCardSlots.Add(card);
            VirtualGridCanvas.Children.Add(card);
        }
    }

    private static void HideVirtualCardSlot(LibraryGridCard card)
    {
        card.Tag = null;
        card.Reset();
        Canvas.SetLeft(card, -10000);
        Canvas.SetTop(card, -10000);
        card.Visibility = Visibility.Collapsed;
    }

    private void QueueCardBind(int index)
    {
        if (!_visibleLibraryCards.ContainsKey(index))
            return;

        if (_pendingCardBindSet.Add(index))
        {
            _pendingCardBinds.Enqueue(index);
            _cardBindTimer?.Start();
        }
    }

    private void CardBindTimer_Tick(object? sender, object e)
    {
        App.SetPerfBreadcrumb($"Library bind tick pending={_pendingCardBinds.Count} cards={_visibleLibraryCards.Count}");
        int attempts = 0;
        int processed = 0;

        while (attempts < CardBindsPerTick && _pendingCardBinds.Count > 0)
        {
            attempts++;
            var index = _pendingCardBinds.Dequeue();
            _pendingCardBindSet.Remove(index);

            if (!_visibleLibraryCards.TryGetValue(index, out var card))
                continue;

            if (card.Tag is not int cardIndex || cardIndex != index)
                continue;

            var item = ViewModel.GetWindowItem(index);
            App.SetPerfBreadcrumb($"Library bind item index={index} hasItem={item != null} attempt={attempts} processed={processed}");
            if (item != null && (!ReferenceEquals(card.MediaItem, item) || card.SortKey != ViewModel.SelectedSort))
                card.Bind(item, ViewModel.SelectedSort);

            processed++;
        }

        if (_pendingCardBinds.Count == 0)
            _cardBindTimer?.Stop();
        App.SetPerfBreadcrumb($"Library bind end pending={_pendingCardBinds.Count} processed={processed} attempts={attempts}");
    }

    private static void PositionVirtualCardSlot(LibraryGridCard card, int slotIndex, GridLayoutInfo layout)
    {
        var (left, top) = VirtualGridScrollGate.GetSlotPosition(
            slotIndex,
            layout.Columns,
            layout.ItemWidth,
            layout.ColumnGap,
            layout.RowHeight);
        SetCanvasCoordinateIfChanged(card, left, top);
    }

    private static void SetCanvasCoordinateIfChanged(UIElement element, double left, double top)
    {
        if (!AreClose(Canvas.GetLeft(element), left))
            Canvas.SetLeft(element, left);
        if (!AreClose(Canvas.GetTop(element), top))
            Canvas.SetTop(element, top);
    }

    private static bool AreClose(double a, double b)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
            return false;
        return Math.Abs(a - b) < 0.1;
    }

    private void ClearVirtualCards()
    {
        _cardBindTimer?.Stop();
        _pendingCardBinds.Clear();
        _pendingCardBindSet.Clear();
        _visibleLibraryCards.Clear();
        _lastRenderedStartIndex = -1;
        _lastRenderedEndIndex = -1;
        foreach (var card in _libraryCardSlots)
            HideVirtualCardSlot(card);
    }

    private void ScheduleVisibleRangeLoad(bool force = false)
    {
        _forceVisibleRangeLoad |= force;
        _visibleRangeDebounceTimer?.Stop();
        _visibleRangeDebounceTimer?.Start();
    }

    private async void VisibleRangeDebounceTimer_Tick(object? sender, object e)
    {
        _visibleRangeDebounceTimer?.Stop();
        var force = _forceVisibleRangeLoad;
        _forceVisibleRangeLoad = false;
        await EnsureVisibleRangeLoadedAsync(force);
    }

    private async Task EnsureVisibleRangeLoadedAsync(bool force = false)
    {
        if (!_isNavigated || LibraryContentArea == null || LibraryContentArea.Visibility != Visibility.Visible || ViewModel.TotalCount <= 0)
            return;

        var range = GetVisibleItemRange(GetGridLayout());
        range = ClampRealizedRange(range, GetGridLayout());
        if (range.EndIndex < range.StartIndex)
            return;

        if (!force &&
            range.StartIndex == _lastRequestedStartIndex &&
            range.EndIndex == _lastRequestedEndIndex)
        {
            return;
        }

        _lastRequestedStartIndex = range.StartIndex;
        _lastRequestedEndIndex = range.EndIndex;
        await ViewModel.EnsureRangeLoadedAsync(range.StartIndex, range.EndIndex);
    }

    private (int StartIndex, int EndIndex) GetVisibleItemRange(GridLayoutInfo layout)
    {
        var total = ViewModel.TotalCount;
        if (total <= 0)
            return (0, -1);

        var visibleRows = VirtualGridScrollGate.GetVisibleRowCount(GetLibraryViewportHeight(), layout.RowHeight);
        return VirtualGridScrollGate.GetWindowedItemRange(
            total,
            layout.Columns,
            _currentFirstRow,
            visibleRows,
            LibraryOverscanRows,
            MaxRealizedLibraryCards);
    }

    private (int StartIndex, int EndIndex) ClampRealizedRange(
        (int StartIndex, int EndIndex) range,
        GridLayoutInfo layout)
    {
        var total = ViewModel.TotalCount;
        if (total <= 0 || range.EndIndex < range.StartIndex)
            return range;

        var maxItems = Math.Max(layout.Columns, MaxRealizedLibraryCards);
        var (startIndex, endIndex) = VirtualGridScrollGate.ClampRealizedItemRange(
            total,
            layout.Columns,
            _currentFirstRow,
            range.StartIndex,
            range.EndIndex,
            maxItems);

        return (startIndex, endIndex);
    }

    private double GetLibraryViewportWidth()
    {
        var padding = LibraryViewportHost?.Padding ?? default;
        var scrollBarWidth = (LibraryScrollBar?.ActualWidth ?? 0) + 12;
        return Math.Max(
            (double)Application.Current.Resources["PosterCardWidth"],
            (LibraryViewportHost?.ActualWidth ?? 0) - padding.Left - padding.Right - scrollBarWidth);
    }

    private double GetLibraryViewportHeight()
    {
        var padding = LibraryViewportHost?.Padding ?? default;
        return Math.Max(
            (double)Application.Current.Resources["PosterCardTotalHeight"],
            (LibraryViewportHost?.ActualHeight ?? 0) - padding.Top - padding.Bottom);
    }

    private GridLayoutInfo GetGridLayout()
    {
        const double columnGap = 12;
        const double rowGap = 16;
        var itemWidth = (double)Application.Current.Resources["PosterCardWidth"];
        var itemHeight = (double)Application.Current.Resources["PosterCardTotalHeight"];
        var availableWidth = Math.Max(
            itemWidth,
            GetLibraryViewportWidth());
        var columns = Math.Max(1, (int)Math.Floor((availableWidth + columnGap) / (itemWidth + columnGap)));
        return new GridLayoutInfo(
            Columns: columns,
            AvailableWidth: availableWidth,
            ItemWidth: itemWidth,
            ItemHeight: itemHeight,
            ColumnGap: columnGap,
            RowGap: rowGap);
    }

    private readonly record struct GridLayoutInfo(
        int Columns,
        double AvailableWidth,
        double ItemWidth,
        double ItemHeight,
        double ColumnGap,
        double RowGap)
    {
        public double RowHeight => ItemHeight + RowGap;
    }

    private void ScrollToTop_Click(object sender, RoutedEventArgs e)
    {
        SetLibraryFirstRow(0, force: true);
        ScrollToTopButton.Visibility = Visibility.Collapsed;
        _ = EnsureVisibleRangeLoadedAsync(force: true);
    }

    // ===== Overlay Header (webui: LibraryHeader with glass-on-scroll) =====

    private void UpdateHeaderOverlayMode(bool overlay)
    {
        _overlayMode = overlay;

        if (overlay)
        {
            // Transparent background — header floats over hero
            HeaderGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

            // Attach scroll listener for glass transition
            if (!_scrollListenerAttached)
            {
                _scrollListenerAttached = true;
                RecommendedPanel.ViewChanged += RecommendedPanel_ViewChanged;
            }
        }
        else
        {
            // Solid background — normal header
            HeaderGrid.Background = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["AppBackgroundBrush"];
        }
    }

    private void RecommendedPanel_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!_overlayMode) return;

        const double GLASS_THRESHOLD = 160;
        bool pastThreshold = RecommendedPanel.VerticalOffset > GLASS_THRESHOLD;

        if (pastThreshold)
        {
            // Glass mode: semi-transparent background
            HeaderGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xDD, 0x10, 0x17, 0x22)); // AppBackgroundColor at ~87% opacity
        }
        else
        {
            // Transparent mode: floating over hero
            HeaderGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    private async void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton || clickedButton.Tag is not string tag)
            return;

        ShowTab(tag);

        if (tag == "Recommended" && !_recommendedLoaded)
            await LoadRecommendationsAsync();

        if (tag == "Collections" && !_collectionsLoaded)
            await LoadCollectionsAsync();

        if (tag == "Library")
        {
            await EnsureLibraryCatalogLoadedAsync();
            await FillViewportAsync();
        }
    }

    private async Task EnsureLibraryCatalogLoadedAsync()
    {
        if (_libraryCatalogLoaded) return;
        _libraryCatalogLoaded = true;

        var overlayWarmup = App.Services.GetRequiredService<global::ContinuumPlayer.Services.CardOverlayService>().EnsureLoadedAsync();
        await ViewModel.LoadCommand.ExecuteAsync(null);
        try { await overlayWarmup; } catch { }
    }

    private void ReleaseRecommendedContent()
    {
        _recommendationsVersion++;
        if (!_recommendedLoaded) return;

        RecommendedHeroCarousel.ItemsSource = null;
        RecommendedHeroCarousel.Visibility = Visibility.Collapsed;
        RecommendedLoading.IsActive = false;
        RecommendedLoading.Visibility = Visibility.Collapsed;

        for (int i = RecommendedSectionsPanel.Children.Count - 1; i >= 0; i--)
        {
            if (RecommendedSectionsPanel.Children[i] is SectionRow)
                RecommendedSectionsPanel.Children.RemoveAt(i);
        }

        _recommendedLoaded = false;
    }

    private async Task LoadRecommendationsAsync()
    {
        var version = ++_recommendationsVersion;
        _recommendedLoaded = true;
        RecommendedLoading.IsActive = true;
        RecommendedLoading.Visibility = Visibility.Visible;
        RecommendedErrorPanel.Visibility = Visibility.Collapsed;
        RecommendedHeroCarousel.Visibility = Visibility.Collapsed;

        // Clear any previous section rows (keep loading ring, error text, hero carousel)
        for (int i = RecommendedSectionsPanel.Children.Count - 1; i >= 0; i--)
        {
            if (RecommendedSectionsPanel.Children[i] is SectionRow)
                RecommendedSectionsPanel.Children.RemoveAt(i);
        }

        try
        {
            var catalogApi = App.Services.GetRequiredService<CatalogApi>();
            var libraryId = ViewModel.Library?.Id ?? 0;
            var response = await catalogApi.GetLibrarySectionsAsync(libraryId);
            if (version != _recommendationsVersion || _currentTab != "Recommended")
                return;

            RecommendedLoading.IsActive = false;
            RecommendedLoading.Visibility = Visibility.Collapsed;

            if (response.Sections.Count == 0)
            {
                RecommendedError.Text = "No recommendations available yet.";
                RecommendedErrorPanel.Visibility = Visibility.Visible;
                return;
            }

            // Web parity (LibraryRecommended.tsx/splitLibrarySections): first featured
            // section becomes the HeroBanner, the rest render as SectionRows.
            HomeSectionWithItems? heroSection = null;
            var rowSections = new List<HomeSectionWithItems>();
            foreach (var section in response.Sections)
            {
                if (section.Items.Count == 0) continue;
                if (heroSection == null && section.Featured)
                    heroSection = section;
                else
                    rowSections.Add(section);
            }

            if (heroSection != null)
            {
                var limit = heroSection.ItemLimit > 0 ? heroSection.ItemLimit : heroSection.Items.Count;
                var heroItems = heroSection.Items.Take(limit).ToList();
                RecommendedHeroCarousel.ItemsSource = heroItems;
                RecommendedHeroCarousel.Visibility = Visibility.Visible;
            }

            foreach (var section in rowSections)
            {
                var row = new SectionRow { Section = section };
                // Per-section retry — refetches just this section's items.
                row.OnRefresh = async (sec) => await RefreshSectionAsync(row, sec);
                RecommendedSectionsPanel.Children.Add(row);
            }
        }
        catch (Exception ex)
        {
            if (version != _recommendationsVersion)
                return;

            RecommendedLoading.IsActive = false;
            RecommendedLoading.Visibility = Visibility.Collapsed;
            RecommendedError.Text = $"Failed to load recommendations: {ex.Message}";
            RecommendedErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private async void RecommendedRetry_Click(object sender, RoutedEventArgs e)
    {
        _recommendedLoaded = false;
        await LoadRecommendationsAsync();
    }

    private async Task RefreshSectionAsync(SectionRow row, HomeSectionWithItems section)
    {
        try
        {
            var catalogApi = App.Services.GetRequiredService<CatalogApi>();
            var libraryId = ViewModel.Library?.Id ?? 0;
            var response = await catalogApi.GetLibrarySectionItemsAsync(libraryId, section.Id);

            // Re-bind by rebuilding the section instance (SectionRow is
            // data-driven via the Section DP).
            var refreshed = response.Sections?.FirstOrDefault();
            if (refreshed != null)
            {
                row.Section = refreshed;
            }
        }
        catch
        {
            // Non-fatal — the row keeps its previous items on failure.
        }
    }

    // ===== Collections Tab =====

    private async Task LoadCollectionsAsync()
    {
        _collectionsLoaded = true;
        CollectionsLoading.IsActive = true;
        CollectionsLoading.Visibility = Visibility.Visible;

        await ViewModel.LoadCollectionsCommand.ExecuteAsync(null);

        CollectionsLoading.IsActive = false;
        CollectionsLoading.Visibility = Visibility.Collapsed;

        BuildCollectionCards();
    }

    private void BuildCollectionCards()
    {
        var collections = ViewModel.Collections;

        if (collections.Count == 0)
        {
            CollectionsEmptyCard.Visibility = Visibility.Visible;
            CollectionsRepeater.Visibility = Visibility.Collapsed;
            return;
        }

        CollectionsEmptyCard.Visibility = Visibility.Collapsed;
        CollectionsRepeater.Visibility = Visibility.Visible;

        // Build a wrapped grid of collection cards
        CollectionsRepeater.ItemsSource = null;

        var cardElements = new List<FrameworkElement>();
        foreach (var c in collections)
        {
            cardElements.Add(CreateCollectionCard(c));
        }

        // Replace the repeater content with a simple panel
        var wrapPanel = new StackPanel();
        var currentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        int cardsPerRow = 5;
        int count = 0;

        foreach (var card in cardElements)
        {
            currentRow.Children.Add(card);
            count++;
            if (count % cardsPerRow == 0)
            {
                wrapPanel.Children.Add(currentRow);
                currentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 16, 0, 0) };
            }
        }

        if (currentRow.Children.Count > 0)
            wrapPanel.Children.Add(currentRow);

        // Replace the empty card with our content
        var parent = (StackPanel)CollectionsRepeater.Parent!;
        int idx = parent.Children.IndexOf(CollectionsRepeater);
        if (idx >= 0)
        {
            // Remove old dynamic panels if any
            for (int i = parent.Children.Count - 1; i >= 0; i--)
            {
                if (parent.Children[i] is StackPanel sp && sp.Name == null && sp != parent && sp.Tag is "CollectionGrid")
                    parent.Children.RemoveAt(i);
            }

            wrapPanel.Tag = "CollectionGrid";
            parent.Children.Insert(idx + 1, wrapPanel);
        }
    }

    private Border CreateCollectionCard(LibraryCollection collection)
    {
        // Poster area
        var posterBorder = new Border
        {
            Width = 180,
            Height = 200,
            CornerRadius = new CornerRadius(8),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"]
        };

        var posterPlaceholder = new FontIcon
        {
            Glyph = "\uE8F1",
            FontSize = 32,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        posterBorder.Child = posterPlaceholder;

        if (!string.IsNullOrEmpty(collection.PosterUrl))
        {
            _ = LoadCollectionPosterAsync(posterBorder, collection);
        }

        // Type badge overlay
        var typeBadge = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBackgroundBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 6, 0, 0),
            Child = new TextBlock
            {
                Text = collection.CollectionType.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"]
            }
        };

        var posterGrid = new Grid { Width = 180, Height = 200 };
        posterGrid.Children.Add(posterBorder);
        posterGrid.Children.Add(typeBadge);

        // Title
        var titleText = new TextBlock
        {
            Text = collection.Title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(2, 8, 2, 0)
        };

        // Item count
        var countText = new TextBlock
        {
            Text = $"{collection.ItemCount} {(collection.ItemCount == 1 ? "item" : "items")}",
            FontSize = 11,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"],
            Margin = new Thickness(2, 2, 2, 0)
        };

        var content = new StackPanel
        {
            Width = 180,
            Children = { posterGrid, titleText, countText }
        };

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = content,
            Tag = collection
        };

        card.PointerEntered += (s, _) =>
        {
            if (s is Border b)
                b.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceHoverBrush"];
        };
        card.PointerExited += (s, _) =>
        {
            if (s is Border b)
                b.Background = null;
        };

        // B41: navigate to a standalone collection browse page instead of
        // mutating the library tab items in place. Preserves back navigation
        // and mirrors the webui /catalog?source=library_collection route.
        card.Tapped += (s, _) =>
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
            {
                CollectionId = collection.Id,
                Title = collection.Title,
                Subtitle = ViewModel.Library?.Name,
                IsUserCollection = false,
            });
        };

        return card;
    }

    private async Task LoadCollectionPosterAsync(Border posterBorder, LibraryCollection collection)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                collection.Id, "collection_poster", collection.PosterUrl!, httpClient, CancellationToken.None);

            if (bytes == null) return;

            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = 200,
                DecodePixelType = DecodePixelType.Logical
            };
            using var stream = new MemoryStream(bytes);
            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());

            posterBorder.Child = new Image
            {
                Source = bitmapImage,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };
        }
        catch { }
    }

    // ===== Enhanced Filters =====

    private void UpdateStudioCombo()
    {
        UpdateFilterCombo(StudioComboBox, ViewModel.Studios, "All Studios", ViewModel.SelectedStudio, "studios");
    }

    private void UpdateCountryCombo()
    {
        UpdateFilterCombo(CountryComboBox, ViewModel.Countries, "All Countries", ViewModel.SelectedCountry, "countries");
    }

    private void UpdateResolutionCombo()
    {
        UpdateFilterCombo(ResolutionComboBox, ViewModel.Resolutions, "All Quality", ViewModel.SelectedResolution, "resolutions");
    }

    private void UpdateAudioLangCombo()
    {
        UpdateFilterCombo(AudioLangComboBox, ViewModel.AudioLanguages, "All Audio", ViewModel.SelectedAudioLanguage, "audio");
    }

    private async void StudioComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;

        ViewModel.SelectedStudio = SelectedFilterValue(StudioComboBox);
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
        SaveViewState(_currentTab); // B42
    }

    private async void CountryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;

        ViewModel.SelectedCountry = SelectedFilterValue(CountryComboBox);
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
        SaveViewState(_currentTab); // B42
    }

    private async void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;

        ViewModel.SelectedResolution = SelectedFilterValue(ResolutionComboBox);
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
        SaveViewState(_currentTab); // B42
    }

    private async void AudioLangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;

        ViewModel.SelectedAudioLanguage = SelectedFilterValue(AudioLangComboBox);
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
        SaveViewState(_currentTab); // B42
    }

    private void UpdateActiveFilterBadges()
    {
        FilterBadgesPanel.Children.Clear();

        var filters = new List<(string Label, string Value, Action ClearAction)>();

        if (!string.IsNullOrEmpty(ViewModel.SelectedType))
        {
            var label = ViewModel.SelectedType == "movie" ? "Movies" : "Series";
            filters.Add(("Type", label, () => { ViewModel.SelectedType = null; MediaTypeComboBox.SelectedIndex = 0; }));
        }
        if (!string.IsNullOrEmpty(ViewModel.SelectedGenre))
            filters.Add(("Genre", ViewModel.SelectedGenre, () => { ViewModel.SelectedGenre = null; GenreComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedContentRating))
            filters.Add(("Rating", ViewModel.SelectedContentRating, () => { ViewModel.SelectedContentRating = null; ContentRatingComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedStudio))
            filters.Add(("Studio", ViewModel.SelectedStudio, () => { ViewModel.SelectedStudio = null; StudioComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedCountry))
            filters.Add(("Country", ViewModel.SelectedCountry, () => { ViewModel.SelectedCountry = null; CountryComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedResolution))
            filters.Add(("Quality", ViewModel.SelectedResolution, () => { ViewModel.SelectedResolution = null; ResolutionComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedAudioLanguage))
            filters.Add(("Audio", ViewModel.SelectedAudioLanguage, () => { ViewModel.SelectedAudioLanguage = null; AudioLangComboBox.SelectedIndex = 0; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedYearMin))
            filters.Add(("Year From", ViewModel.SelectedYearMin, () => { ViewModel.SelectedYearMin = null; YearMinBox.Text = ""; }));
        if (!string.IsNullOrEmpty(ViewModel.SelectedYearMax))
            filters.Add(("Year To", ViewModel.SelectedYearMax, () => { ViewModel.SelectedYearMax = null; YearMaxBox.Text = ""; }));

        if (filters.Count == 0)
        {
            ActiveFiltersBar.Visibility = Visibility.Collapsed;
            return;
        }

        ActiveFiltersBar.Visibility = Visibility.Visible;
        ClearAllFiltersButton.Visibility = filters.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (label, value, clearAction) in filters)
        {
            var badge = CreateFilterBadge(label, value, clearAction);
            FilterBadgesPanel.Children.Add(badge);
        }
    }

    private Border CreateFilterBadge(string label, string value, Action clearAction)
    {
        var closeBtn = new Button
        {
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Padding = new Thickness(2),
            MinWidth = 0,
            MinHeight = 0,
            Content = new FontIcon
            {
                Glyph = "\uE711",
                FontSize = 10,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"]
            }
        };
        closeBtn.Click += async (_, _) =>
        {
            _suppressFilterEvents = true;
            clearAction();
            _suppressFilterEvents = false;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            UpdateActiveFilterBadges();
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = $"{label}: {value}",
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                },
                closeBtn
            }
        };

        return new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 6, 4),
            Child = content
        };
    }

    private async void ClearAllFilters_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilterEvents = true;
        ViewModel.SelectedType = null;
        ViewModel.SelectedGenre = null;
        ViewModel.SelectedContentRating = null;
        ViewModel.SelectedStudio = null;
        ViewModel.SelectedCountry = null;
        ViewModel.SelectedResolution = null;
        ViewModel.SelectedAudioLanguage = null;
        ViewModel.SelectedYearMin = null;
        ViewModel.SelectedYearMax = null;
        MediaTypeComboBox.SelectedIndex = 0;
        GenreComboBox.SelectedIndex = 0;
        ContentRatingComboBox.SelectedIndex = 0;
        StudioComboBox.SelectedIndex = 0;
        CountryComboBox.SelectedIndex = 0;
        ResolutionComboBox.SelectedIndex = 0;
        AudioLangComboBox.SelectedIndex = 0;
        YearMinBox.Text = "";
        YearMaxBox.Text = "";
        _suppressFilterEvents = false;

        ActiveFiltersBar.Visibility = Visibility.Collapsed;

        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
    }
}
