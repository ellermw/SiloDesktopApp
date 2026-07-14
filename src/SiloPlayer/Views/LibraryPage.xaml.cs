using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class LibraryPage : Page
{
    private static readonly (string Label, string Value)[] AdvancedRuleFields =
    [
        ("Genre", "genre"), ("Year", "year"), ("IMDb Rating", "rating_imdb"),
        ("Type", "type"), ("Content Rating", "content_rating"), ("Studio", "studio"),
        ("Actor", "actor"), ("Director", "director"), ("Writer", "writer"),
        ("Producer", "producer"), ("Author", "author"), ("Narrator", "narrator"),
        ("Series", "series"), ("Network", "network"), ("Country", "country"),
        ("Match Status", "status"), ("Added", "added_at"), ("Release Date", "release_date"),
        ("Watched", "watched"), ("Favorited", "favorited"), ("In Watchlist", "in_watchlist"),
        ("In Progress", "in_progress"), ("Resolution", "resolution"), ("HDR", "hdr"),
        ("Dolby Vision", "dolby_vision"), ("Bitrate", "bitrate"),
        ("Audio Language", "audio_language"), ("Original Language", "original_language"),
    ];

    private static readonly (string Label, string Value)[] AdvancedRuleOperators =
    [
        ("is", "is"), ("is not", "is_not"), ("contains", "contains"),
        (">=", "gte"), ("<=", "lte"), (">", "gt"), ("<", "lt"),
        ("between", "between"), ("in the last", "in_last"),
    ];

    private static IReadOnlyList<(string Label, string Value)> GetAdvancedOperators(string field) => field switch
    {
        "type" or "studio" or "network" or "country" or "original_language" or "content_rating" or
            "actor" or "director" or "writer" or "producer" or "author" or "narrator" or "series" or
            "resolution" or "audio_language" => [("is", "is"), ("is not", "is_not")],
        "genre" => [("is", "is"), ("is not", "is_not"), ("contains", "contains")],
        "year" => [("is", "is"), ("is not", "is_not"), (">=", "gte"), ("<=", "lte"), (">", "gt"), ("<", "lt"), ("between", "between")],
        "rating_imdb" or "bitrate" => [(">=", "gte"), ("<=", "lte"), (">", "gt"), ("<", "lt"), ("between", "between")],
        "added_at" or "release_date" => [(">", "gt"), ("<", "lt"), ("between", "between"), ("in the last", "in_last")],
        "watched" or "favorited" or "in_watchlist" or "in_progress" or "hdr" or "dolby_vision" => [("is", "is")],
        _ => AdvancedRuleOperators,
    };
    // B42: Persist last-viewed tab + filters per library across navigations.
    // Web parses this from the URL (?tab=library|collections); we don't have
    // routing yet, so we mirror the page state in a per-library in-memory dict.
    private sealed class LibraryViewState
    {
        public string Tab { get; set; } = "Recommended";
        public string Sort { get; set; } = "title";
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
        public string? MinimumRating { get; set; }
        public string? OriginalLanguage { get; set; }
        public string? Actor { get; set; }
        public string? Director { get; set; }
        public string? Writer { get; set; }
        public string? Producer { get; set; }
        public string? Author { get; set; }
        public string? Narrator { get; set; }
        public string? Series { get; set; }
        public string? Network { get; set; }
        public string? MatchStatus { get; set; }
        public string? WatchStatus { get; set; }
        public string? AddedInLast { get; set; }
        public string? ReleasedInLast { get; set; }
        public bool FourK { get; set; }
        public bool Hdr { get; set; }
        public bool DolbyVision { get; set; }
    }

    private sealed class FilterOption(string label, string value)
    {
        public string Label { get; } = label;
        public string Value { get; } = value;

        public override string ToString() => Label;
    }

    private sealed record SortOption(string Label, string Value, string DefaultOrder);

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
    private DispatcherTimer? _advancedFilterDebounceTimer;
    private DispatcherTimer? _audiobookGroupSearchTimer;
    private CancellationTokenSource? _audiobookGroupLoadCts;
    private string _currentAudiobookAxis = "books";
    private int _audiobookGroupsOffset;
    private int _audiobookGroupsTotal;
    private bool _audiobookGroupsTotalExact;
    private bool _audiobookGroupsHasMore;
    private bool _isLoadingAudiobookGroups;
    private double _collectionCardWidth;
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
        OrderComboBox.SelectedIndex = 1;
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

        ViewModel.OriginalLanguages.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateOriginalLanguageCombo());

        ViewModel.Networks.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => UpdateNetworkCombo());

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.TotalCount) ||
                args.PropertyName == nameof(ViewModel.DisplayTotalCount))
            {
                DispatcherQueue.TryEnqueue(() => UpdateCountDisplay());
            }
            else if (args.PropertyName == nameof(ViewModel.IsLoading))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    var showSkeletons = _isNavigated && _currentTab == "Library" &&
                        ViewModel.IsLoading && ViewModel.TotalCount == 0 && _currentAudiobookAxis == "books";
                    if (showSkeletons) BuildLibrarySkeletons();
                    LibrarySkeletonScroll.Visibility = showSkeletons ? Visibility.Visible : Visibility.Collapsed;
                    LibraryEmptyText.Visibility = !ViewModel.IsLoading && _libraryCatalogLoaded &&
                        ViewModel.TotalCount == 0 && _currentTab == "Library"
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                });
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
            RecommendedTab.Content = library.Type is "audiobook" or "audiobooks" ? "Home" : "Recommended";
            _currentAudiobookAxis = "books";
            MediaTypeFilterField.Visibility = string.Equals(library.Type, "mixed", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ViewModel.Library = library;

            // F7: set window title to the library name
            if (App.MainWindowInstance is MainWindow mw)
                mw.SetDynamicTitle(library.Name);

            // B42: Restore previously-viewed tab + filters for this library if any.
            // Falls back to fresh defaults on first visit.
            _viewStateByLibrary.TryGetValue(library.Id, out var state);
            state ??= new LibraryViewState();

            _suppressFilterEvents = true;
            UpdateSortOptions(library.Type, state.Sort);
            ConfigureBrowseTypeSelector(library.Type, state.MediaType);
            MediaTypeComboBox.SelectedIndex = IndexOfMediaType(state.MediaType);
            GenreComboBox.SelectedIndex = -1;
            ContentRatingComboBox.SelectedIndex = -1;
            StudioComboBox.SelectedIndex = -1;
            CountryComboBox.SelectedIndex = -1;
            ResolutionComboBox.SelectedIndex = -1;
            AudioLangComboBox.SelectedIndex = -1;
            YearMinBox.Text = state.YearMin ?? "";
            YearMaxBox.Text = state.YearMax ?? "";
            MinimumRatingBox.Text = state.MinimumRating ?? "";
            OriginalLanguageComboBox.SelectedIndex = -1;
            ActorBox.Text = state.Actor ?? "";
            DirectorBox.Text = state.Director ?? "";
            WriterBox.Text = state.Writer ?? "";
            ProducerBox.Text = state.Producer ?? "";
            AuthorBox.Text = state.Author ?? "";
            NarratorBox.Text = state.Narrator ?? "";
            SeriesBox.Text = state.Series ?? "";
            NetworkComboBox.SelectedIndex = -1;
            MatchStatusComboBox.SelectedIndex = IndexOfTaggedItem(MatchStatusComboBox, state.MatchStatus);
            WatchStatusComboBox.SelectedIndex = IndexOfTaggedItem(WatchStatusComboBox, state.WatchStatus);
            AddedInLastBox.Text = state.AddedInLast ?? "";
            ReleasedInLastBox.Text = state.ReleasedInLast ?? "";
            FourKToggle.IsOn = state.FourK;
            HdrToggle.IsOn = state.Hdr;
            DolbyVisionToggle.IsOn = state.DolbyVision;
            ConfigureFilterSections(library.Type);
            _orderAsc = state.Order != "desc";
            UpdateOrderButton();
            var restoredSort = SortComboBox.SelectedItem is SortOption restoredOption ? restoredOption.Value : "title";
            ViewModel.SelectedSort = restoredSort;
            Controls.PosterCard.CurrentSortKey = restoredSort;
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
            ViewModel.SelectedMinimumRating = state.MinimumRating;
            ViewModel.SelectedOriginalLanguage = state.OriginalLanguage;
            ViewModel.SelectedActor = state.Actor;
            ViewModel.SelectedDirector = state.Director;
            ViewModel.SelectedWriter = state.Writer;
            ViewModel.SelectedProducer = state.Producer;
            ViewModel.SelectedAuthor = state.Author;
            ViewModel.SelectedNarrator = state.Narrator;
            ViewModel.SelectedSeries = state.Series;
            ViewModel.SelectedNetwork = state.Network;
            ViewModel.SelectedMatchStatus = state.MatchStatus;
            ViewModel.SelectedWatchStatus = state.WatchStatus;
            ViewModel.SelectedAddedInLast = state.AddedInLast;
            ViewModel.SelectedReleasedInLast = state.ReleasedInLast;
            ViewModel.SelectedFourK = state.FourK;
            ViewModel.SelectedHdr = state.Hdr;
            ViewModel.SelectedDolbyVision = state.DolbyVision;
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
        _audiobookGroupSearchTimer?.Stop();
        _audiobookGroupLoadCts?.Cancel();
        _audiobookGroupLoadCts?.Dispose();
        _audiobookGroupLoadCts = null;
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

    private void UpdateSortOptions(string libraryType, string selectedSort)
    {
        var type = libraryType.Trim().ToLowerInvariant();
        var options = new List<SortOption>
        {
            new("Title", "title", "asc"),
            new("Date Added", "added_at", "desc"),
            new("Release Date", "release_date", "desc"),
            new("Year", "year", "desc"),
            new("Duration", "runtime", "desc"),
            new("Bitrate", "bitrate", "desc"),
            new("Progress", "progress", "desc"),
            new("Date Viewed", "date_viewed", "desc"),
            new("Plays", "plays", "desc"),
        };

        var isBook = type is "audiobook" or "audiobooks" or "ebook" or "ebooks" or "manga";
        if (!isBook)
        {
            options.InsertRange(4,
            [
                new("Content Rating", "content_rating", "asc"),
                new("IMDb Rating", "rating_imdb", "desc"),
                new("TMDB Rating", "rating_tmdb", "desc"),
                new("RT Critic Rating", "rating_rt_critic", "desc"),
                new("RT Audience Rating", "rating_rt_audience", "desc"),
                new("Resolution", "resolution", "desc"),
            ]);
        }

        if (type is "series" or "tv")
        {
            options.Insert(3, new("Latest Episode Air Date", "last_air_date", "desc"));
            options.Insert(4, new("Latest Episode Added", "latest_episode_added", "desc"));
        }

        if (type is "audiobook" or "audiobooks" or "ebook" or "ebooks" or "manga")
            options.Add(new("Author", "author", "asc"));
        if (type is "audiobook" or "audiobooks")
            options.Add(new("Narrator", "narrator", "asc"));
        if (type is "audiobook" or "audiobooks" or "ebook" or "ebooks")
            options.Add(new("Series", "series", "asc"));

        var normalized = selectedSort switch
        {
            "sort_title" => "title",
            "recently_added" => "added_at",
            "rating" => "rating_imdb",
            _ => selectedSort,
        };
        var selected = options.FirstOrDefault(option => option.Value == normalized) ?? options[0];
        SortComboBox.DisplayMemberPath = nameof(SortOption.Label);
        SortComboBox.SelectedValuePath = nameof(SortOption.Value);
        SortComboBox.ItemsSource = options;
        SortComboBox.SelectedItem = selected;
    }

    private static int IndexOfMediaType(string? type) => type switch
    {
        "movie" => 1,
        "series" => 2,
        "episode" => 3,
        _ => 0,
    };

    private static int IndexOfTaggedItem(ComboBox comboBox, string? value)
    {
        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is ComboBoxItem item &&
                string.Equals(item.Tag as string ?? "", value ?? "", StringComparison.Ordinal))
                return index;
        }
        return 0;
    }

    private void ConfigureFilterSections(string libraryType)
    {
        var type = libraryType.Trim().ToLowerInvariant();
        var isAudiobook = type is "audiobook" or "audiobooks";
        var isEbook = type is "ebook" or "ebooks" or "manga";
        var isBook = isAudiobook || isEbook;

        VideoMetadataFilters.Visibility = isBook ? Visibility.Collapsed : Visibility.Visible;
        VideoQualityToggles.Visibility = isBook ? Visibility.Collapsed : Visibility.Visible;
        BookMetadataFilters.Visibility = isBook ? Visibility.Visible : Visibility.Collapsed;
        NarratorFilterField.Visibility = isAudiobook ? Visibility.Visible : Visibility.Collapsed;
        WatchStatusLabel.Text = isEbook ? "Read status" : isAudiobook ? "Listening status" : "Watch status";

        if (WatchStatusComboBox.Items.Count >= 4)
        {
            ((ComboBoxItem)WatchStatusComboBox.Items[1]).Content = isEbook ? "Read" : isAudiobook ? "Listened" : "Watched";
            ((ComboBoxItem)WatchStatusComboBox.Items[3]).Content = isEbook ? "Unread" : isAudiobook ? "Unlistened" : "Unwatched";
        }
    }

    private void ConfigureBrowseTypeSelector(string libraryType, string? selectedType)
    {
        var type = libraryType.Trim().ToLowerInvariant();
        var isAudiobook = type is "audiobook" or "audiobooks";
        AudiobookAxisPanel.Visibility = isAudiobook ? Visibility.Visible : Visibility.Collapsed;
        SortComboBox.Visibility = Visibility.Visible;
        OrderComboBox.Visibility = Visibility.Visible;
        OpenFiltersButton.Visibility = Visibility.Visible;
        ResultCountText.Visibility = Visibility.Collapsed;
        if (isAudiobook) UpdateAudiobookAxisButtons();
        List<FilterOption>? options = null;

        if (type is "series" or "tv")
        {
            options =
            [
                new("Series", "series"),
                new("Episodes", "episode"),
            ];
            selectedType ??= "series";
        }
        else if (type == "mixed")
        {
            options =
            [
                new("All Media", ""),
                new("Movies & Series", "video"),
                new("Movies", "movie"),
                new("Series", "series"),
                new("Episodes", "episode"),
                new("Audiobooks", "audiobook"),
                new("Ebooks", "ebook"),
                new("Manga", "manga"),
            ];
        }

        BrowseTypePanel.Visibility = options == null ? Visibility.Collapsed : Visibility.Visible;
        BrowseTypeComboBox.ItemsSource = options;
        if (options != null)
        {
            BrowseTypeComboBox.DisplayMemberPath = nameof(FilterOption.Label);
            BrowseTypeComboBox.SelectedValuePath = nameof(FilterOption.Value);
            BrowseTypeComboBox.SelectedItem = options.FirstOrDefault(option => option.Value == selectedType) ?? options[0];
        }

        var hasToolbarType = options != null;
        Grid.SetColumn(SortComboBox, hasToolbarType ? 1 : 0);
        Grid.SetColumn(OrderComboBox, hasToolbarType ? 2 : 1);
        Grid.SetColumn(OpenFiltersButton, hasToolbarType ? 3 : 2);
        Grid.SetColumn(ResultCountText, hasToolbarType ? 4 : 3);
    }

    /// <summary>B42: Persist current page state for this library.</summary>
    private void SaveViewState(string tab)
    {
        if (ViewModel.Library == null) return;
        _viewStateByLibrary[ViewModel.Library.Id] = new LibraryViewState
        {
            Tab = tab,
            Sort = ViewModel.SelectedSort ?? "title",
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
            MinimumRating = ViewModel.SelectedMinimumRating,
            OriginalLanguage = ViewModel.SelectedOriginalLanguage,
            Actor = ViewModel.SelectedActor,
            Director = ViewModel.SelectedDirector,
            Writer = ViewModel.SelectedWriter,
            Producer = ViewModel.SelectedProducer,
            Author = ViewModel.SelectedAuthor,
            Narrator = ViewModel.SelectedNarrator,
            Series = ViewModel.SelectedSeries,
            Network = ViewModel.SelectedNetwork,
            MatchStatus = ViewModel.SelectedMatchStatus,
            WatchStatus = ViewModel.SelectedWatchStatus,
            AddedInLast = ViewModel.SelectedAddedInLast,
            ReleasedInLast = ViewModel.SelectedReleasedInLast,
            FourK = ViewModel.SelectedFourK,
            Hdr = ViewModel.SelectedHdr,
            DolbyVision = ViewModel.SelectedDolbyVision,
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
        LibraryContentArea.Visibility = tag == "Library" && _currentAudiobookAxis == "books" ? Visibility.Visible : Visibility.Collapsed;
        AudiobookGroupsPanel.Visibility = tag == "Library" && _currentAudiobookAxis != "books" ? Visibility.Visible : Visibility.Collapsed;
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
            ResultCountText.Text = $"{ViewModel.DisplayTotalCount:N0} {(ViewModel.DisplayTotalCount == 1 ? "item" : "items")}";
        }
        else if (ViewModel.TotalCount > 0)
        {
            ResultCountText.Text = "Loading item count";
        }
        else
        {
            ResultCountText.Text = "";
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
        OrderComboBox.SelectedIndex = _orderAsc ? 1 : 0;
    }

    private async void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        if (SortComboBox.SelectedItem is SortOption option)
        {
            ViewModel.SelectedSort = option.Value;
            _orderAsc = option.DefaultOrder == "asc";
            _suppressFilterEvents = true;
            UpdateOrderButton();
            _suppressFilterEvents = false;
            ViewModel.SelectedOrder = option.DefaultOrder;
            // Keep non-library PosterCards in sync with the current sort meta.
            // The virtualized Library tab passes ViewModel.SelectedSort directly.
            Controls.PosterCard.CurrentSortKey = option.Value;
            await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
            await FillViewportAsync();
            SaveViewState(_currentTab); // B42
        }
    }

    private async void OrderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || OrderComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string order)
            return;

        _orderAsc = order == "asc";
        ViewModel.SelectedOrder = order;
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        SaveViewState(_currentTab); // B42
    }

    private void OpenFilters_Click(object sender, RoutedEventArgs e)
    {
        FiltersSheet.IsOpen = true;
    }

    private void CloseFilters_Click(object sender, RoutedEventArgs e)
    {
        FiltersSheet.IsOpen = false;
    }

    private async void GuidedFilterMode_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.UseAdvancedRules) return;
        ViewModel.UseAdvancedRules = false;
        GuidedFiltersScroll.Visibility = Visibility.Visible;
        AdvancedFiltersScroll.Visibility = Visibility.Collapsed;
        GuidedFilterModeButton.Style = (Style)Resources["PrimaryButtonStyle"];
        AdvancedFilterModeButton.Style = (Style)Resources["OutlineButtonStyle"];
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
    }

    private async void AdvancedFilterMode_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.UseAdvancedRules)
            ViewModel.SeedAdvancedRulesFromGuided();
        ViewModel.UseAdvancedRules = true;
        BuildAdvancedRulesPanel();
        _suppressFilterEvents = true;
        AdvancedMatchComboBox.SelectedIndex = ViewModel.AdvancedRulesMatch == "any" ? 1 : 0;
        _suppressFilterEvents = false;
        GuidedFiltersScroll.Visibility = Visibility.Collapsed;
        AdvancedFiltersScroll.Visibility = Visibility.Visible;
        GuidedFilterModeButton.Style = (Style)Resources["OutlineButtonStyle"];
        AdvancedFilterModeButton.Style = (Style)Resources["PrimaryButtonStyle"];
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
    }

    private void BuildAdvancedRulesPanel()
    {
        AdvancedRulesHost.Children.Clear();
        foreach (var group in ViewModel.AdvancedGroups)
            AdvancedRulesHost.Children.Add(BuildAdvancedGroupCard(group));
    }

    private FrameworkElement BuildAdvancedGroupCard(EditableQueryGroup group)
    {
        var content = new StackPanel { Spacing = 10 };
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(96) },
                new ColumnDefinition(),
                new ColumnDefinition { Width = new GridLength(36) },
            },
            ColumnSpacing = 8,
        };
        header.Children.Add(new TextBlock
        {
            Text = "Match",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        var match = new ComboBox { MinWidth = 88 };
        foreach (var (label, value) in new[] { ("ALL", "all"), ("ANY", "any") })
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            match.Items.Add(item);
            if (group.Match == value) match.SelectedItem = item;
        }
        match.SelectionChanged += (_, _) =>
        {
            if (match.SelectedItem is ComboBoxItem { Tag: string value })
            {
                group.Match = value == "any" ? "any" : "all";
                QueueAdvancedRuleApply();
            }
        };
        Grid.SetColumn(match, 1);
        header.Children.Add(match);
        var rulesLabel = new TextBlock
        {
            Text = "rules",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(rulesLabel, 2);
        header.Children.Add(rulesLabel);
        var removeGroup = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
        };
        removeGroup.Click += (_, _) =>
        {
            ViewModel.AdvancedGroups.Remove(group);
            if (ViewModel.AdvancedGroups.Count == 0)
                ViewModel.AdvancedGroups.Add(LibraryViewModel.CreateEmptyAdvancedGroup());
            BuildAdvancedRulesPanel();
            QueueAdvancedRuleApply();
        };
        Grid.SetColumn(removeGroup, 3);
        header.Children.Add(removeGroup);
        content.Children.Add(header);

        foreach (var rule in group.Rules)
            content.Children.Add(BuildAdvancedRuleRow(group, rule));

        var addRule = new Button
        {
            Content = "Add Rule",
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        addRule.Click += (_, _) =>
        {
            group.Rules.Add(new QueryRule { Field = "genre", Op = "is", Value = "" });
            BuildAdvancedRulesPanel();
        };
        content.Children.Add(addRule);

        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private FrameworkElement BuildAdvancedRuleRow(EditableQueryGroup group, QueryRule rule)
    {
        var panel = new StackPanel { Spacing = 8 };
        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(116) }, new ColumnDefinition { Width = new GridLength(36) } }, ColumnSpacing = 8 };
        var field = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (label, value) in AdvancedRuleFields)
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            field.Items.Add(item);
            if (value == rule.Field) field.SelectedItem = item;
        }
        if (field.SelectedItem == null) field.SelectedIndex = 0;
        field.SelectionChanged += (_, _) =>
        {
            if (field.SelectedItem is ComboBoxItem { Tag: string value })
            {
                rule.Field = value;
                rule.Op = GetAdvancedOperators(value)[0].Value;
                rule.Value = DefaultAdvancedValue(value);
                BuildAdvancedRulesPanel();
                QueueAdvancedRuleApply();
            }
        };

        var op = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (label, value) in GetAdvancedOperators(rule.Field))
        {
            var item = new ComboBoxItem { Content = label, Tag = value };
            op.Items.Add(item);
            if (value == rule.Op) op.SelectedItem = item;
        }
        if (op.SelectedItem == null) op.SelectedIndex = 0;
        op.SelectionChanged += (_, _) =>
        {
            if (op.SelectedItem is ComboBoxItem { Tag: string value })
            {
                rule.Op = value;
                rule.Value = value == "between" ? new object[] { "", "" } : DefaultAdvancedValue(rule.Field);
                BuildAdvancedRulesPanel();
                QueueAdvancedRuleApply();
            }
        };

        var remove = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["GhostButtonStyle"],
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
        };
        remove.Click += (_, _) =>
        {
            group.Rules.Remove(rule);
            if (group.Rules.Count == 0)
                ViewModel.AdvancedGroups.Remove(group);
            if (ViewModel.AdvancedGroups.Count == 0)
                ViewModel.AdvancedGroups.Add(LibraryViewModel.CreateEmptyAdvancedGroup());
            BuildAdvancedRulesPanel();
            QueueAdvancedRuleApply();
        };
        Grid.SetColumn(op, 1);
        Grid.SetColumn(remove, 2);
        header.Children.Add(field);
        header.Children.Add(op);
        header.Children.Add(remove);

        panel.Children.Add(header);
        panel.Children.Add(BuildAdvancedValueEditor(rule));
        return panel;
    }

    private void AddAdvancedGroup_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AdvancedGroups.Add(LibraryViewModel.CreateEmptyAdvancedGroup());
        BuildAdvancedRulesPanel();
    }

    private async void AdvancedMatch_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || AdvancedMatchComboBox.SelectedItem is not ComboBoxItem { Tag: string match }) return;
        ViewModel.AdvancedRulesMatch = match == "any" ? "any" : "all";
        await ApplyAdvancedRulesAsync();
    }

    private void QueueAdvancedRuleApply()
    {
        if (!ViewModel.UseAdvancedRules) return;
        _advancedFilterDebounceTimer?.Stop();
        _advancedFilterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _advancedFilterDebounceTimer.Tick += async (_, _) =>
        {
            _advancedFilterDebounceTimer?.Stop();
            _advancedFilterDebounceTimer = null;
            await ApplyAdvancedRulesAsync();
        };
        _advancedFilterDebounceTimer.Start();
    }

    private async Task ApplyAdvancedRulesAsync()
    {
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
        SaveViewState(_currentTab);
    }

    private static object CoerceAdvancedValue(string field, string value)
    {
        var trimmed = value.Trim();
        if (field is "watched" or "favorited" or "in_watchlist" or "in_progress" or "hdr" or "dolby_vision")
            return bool.TryParse(trimmed, out var boolean) ? boolean : trimmed;
        if (field is "year" or "runtime" or "rating_imdb" or "bitrate" &&
            double.TryParse(trimmed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
            return number;
        return trimmed;
    }

    private FrameworkElement BuildAdvancedValueEditor(QueryRule rule)
    {
        if (rule.Op == "between")
        {
            var values = rule.Value is System.Collections.IEnumerable enumerable and not string
                ? enumerable.Cast<object?>().Select(value => value?.ToString() ?? "").Take(2).ToList()
                : [];
            while (values.Count < 2) values.Add("");
            var grid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() },
                ColumnSpacing = 8,
            };
            for (var index = 0; index < 2; index++)
            {
                var capturedIndex = index;
                var box = new TextBox
                {
                    PlaceholderText = index == 0 ? "From" : "To",
                    Text = values[index],
                };
                box.TextChanged += (_, _) =>
                {
                    values[capturedIndex] = box.Text;
                    rule.Value = values.Select(value => CoerceAdvancedValue(rule.Field, value)).ToArray();
                    QueueAdvancedRuleApply();
                };
                Grid.SetColumn(box, index);
                grid.Children.Add(box);
            }
            return grid;
        }

        if (IsAdvancedBooleanField(rule.Field))
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var (label, value) in new[] { ("True", true), ("False", false) })
            {
                var item = new ComboBoxItem { Content = label, Tag = value };
                combo.Items.Add(item);
                if (rule.Value is bool current && current == value) combo.SelectedItem = item;
            }
            if (combo.SelectedItem == null) combo.SelectedIndex = 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: bool value })
                {
                    rule.Value = value;
                    QueueAdvancedRuleApply();
                }
            };
            return combo;
        }

        var selectValues = GetAdvancedSelectValues(rule.Field);
        if (selectValues.Count > 0)
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var (label, value) in selectValues)
            {
                var item = new ComboBoxItem { Content = label, Tag = value };
                combo.Items.Add(item);
                if (string.Equals(rule.Value?.ToString(), value, StringComparison.OrdinalIgnoreCase)) combo.SelectedItem = item;
            }
            if (combo.SelectedItem == null) combo.SelectedIndex = 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: string value })
                {
                    rule.Value = value;
                    QueueAdvancedRuleApply();
                }
            };
            return combo;
        }

        var valueBox = new TextBox
        {
            PlaceholderText = rule.Op == "in_last" ? "e.g. 30d, 2w" : "Value...",
            Text = rule.Value?.ToString() ?? "",
        };
        valueBox.TextChanged += (_, _) =>
        {
            rule.Value = CoerceAdvancedValue(rule.Field, valueBox.Text);
            QueueAdvancedRuleApply();
        };
        return valueBox;
    }

    private static object DefaultAdvancedValue(string field) => IsAdvancedBooleanField(field) ? false : "";

    private static bool IsAdvancedBooleanField(string field) =>
        field is "watched" or "favorited" or "in_watchlist" or "in_progress" or "hdr" or "dolby_vision";

    private static IReadOnlyList<(string Label, string Value)> GetAdvancedSelectValues(string field) => field switch
    {
        "type" => [("Movie", "movie"), ("Series", "series"), ("Episode", "episode"), ("Audiobook", "audiobook"), ("Ebook", "ebook"), ("Manga", "manga")],
        "resolution" => [("4K (2160p)", "2160p"), ("1080p", "1080p"), ("720p", "720p"), ("SD", "480p")],
        "status" => [("Matched", "matched"), ("Unmatched", "unmatched"), ("Ambiguous", "ambiguous")],
        "content_rating" => [("G", "G"), ("PG", "PG"), ("PG-13", "PG-13"), ("R", "R"), ("TV-Y", "TV-Y"), ("TV-G", "TV-G"), ("TV-PG", "TV-PG"), ("TV-14", "TV-14"), ("TV-MA", "TV-MA"), ("NR", "NR")],
        _ => [],
    };

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
        LibraryEmptyText.Visibility = total <= 0 && !ViewModel.IsLoading && _libraryCatalogLoaded
            ? Visibility.Visible
            : Visibility.Collapsed;
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

    private async void BrowseTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || BrowseTypeComboBox.SelectedItem is not FilterOption option)
            return;

        ViewModel.SelectedType = string.IsNullOrEmpty(option.Value) ? null : option.Value;
        _suppressFilterEvents = true;
        MediaTypeComboBox.SelectedIndex = IndexOfMediaType(ViewModel.SelectedType);
        _suppressFilterEvents = false;
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
        SaveViewState(_currentTab);
    }

    private async void AudiobookAxis_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string axis } || axis == _currentAudiobookAxis)
            return;

        _currentAudiobookAxis = axis;
        UpdateAudiobookAxisButtons();
        var showBooks = axis == "books";
        LibraryContentArea.Visibility = showBooks ? Visibility.Visible : Visibility.Collapsed;
        AudiobookGroupsPanel.Visibility = showBooks ? Visibility.Collapsed : Visibility.Visible;
        SortComboBox.Visibility = showBooks ? Visibility.Visible : Visibility.Collapsed;
        OrderComboBox.Visibility = showBooks ? Visibility.Visible : Visibility.Collapsed;
        OpenFiltersButton.Visibility = showBooks ? Visibility.Visible : Visibility.Collapsed;
        ResultCountText.Visibility = Visibility.Collapsed;
        ActiveFiltersBar.Visibility = showBooks && FilterBadgesPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (showBooks)
        {
            await EnsureLibraryCatalogLoadedAsync();
            await FillViewportAsync();
            return;
        }

        _suppressFilterEvents = true;
        AudiobookGroupSortComboBox.SelectedIndex = axis == "series" ? 0 : 1;
        _suppressFilterEvents = false;
        AudiobookGroupSearchBox.PlaceholderText = $"Search {AudiobookGroupNoun(axis)}…";
        await LoadAudiobookGroupsAsync(reset: true);
    }

    private void UpdateAudiobookAxisButtons()
    {
        foreach (var button in new[] { AudiobookBooksButton, AudiobookSeriesButton, AudiobookAuthorsButton, AudiobookNarratorsButton })
        {
            button.Style = (Style)Resources[
                string.Equals(button.Tag as string, _currentAudiobookAxis, StringComparison.Ordinal)
                    ? "PillTabButtonActiveStyle"
                    : "PillTabButtonStyle"];
        }
    }

    private void AudiobookGroupSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFilterEvents || _currentAudiobookAxis == "books") return;
        _audiobookGroupSearchTimer?.Stop();
        _audiobookGroupSearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _audiobookGroupSearchTimer.Tick += async (_, _) =>
        {
            _audiobookGroupSearchTimer?.Stop();
            _audiobookGroupSearchTimer = null;
            await LoadAudiobookGroupsAsync(reset: true);
        };
        _audiobookGroupSearchTimer.Start();
    }

    private async void AudiobookGroupSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents || _currentAudiobookAxis == "books") return;
        await LoadAudiobookGroupsAsync(reset: true);
    }

    private async void AudiobookGroupsPanel_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (!_audiobookGroupsHasMore || _isLoadingAudiobookGroups) return;
        if (AudiobookGroupsPanel.ScrollableHeight - AudiobookGroupsPanel.VerticalOffset < 360)
            await LoadAudiobookGroupsAsync(reset: false);
    }

    private async Task LoadAudiobookGroupsAsync(bool reset)
    {
        if (ViewModel.Library == null || _currentAudiobookAxis == "books" || _isLoadingAudiobookGroups)
            return;

        if (reset)
        {
            _audiobookGroupLoadCts?.Cancel();
            _audiobookGroupLoadCts?.Dispose();
            _audiobookGroupLoadCts = new CancellationTokenSource();
            _audiobookGroupsOffset = 0;
            _audiobookGroupsHasMore = false;
            AudiobookGroupsHost.Children.Clear();
            AudiobookGroupsEmpty.Visibility = Visibility.Collapsed;
        }

        _audiobookGroupLoadCts ??= new CancellationTokenSource();
        var ct = _audiobookGroupLoadCts.Token;
        _isLoadingAudiobookGroups = true;
        AudiobookGroupsLoading.IsActive = true;
        AudiobookGroupsLoading.Visibility = Visibility.Visible;

        try
        {
            var sort = (AudiobookGroupSortComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "name";
            var response = await App.Services.GetRequiredService<CatalogApi>().GetAudiobookGroupsAsync(
                ViewModel.Library.Id,
                _currentAudiobookAxis,
                sort,
                AudiobookGroupSearchBox.Text,
                offset: _audiobookGroupsOffset,
                includeTotal: reset,
                ct: ct);
            if (ct.IsCancellationRequested) return;

            if (reset)
            {
                _audiobookGroupsTotal = response.Total;
                _audiobookGroupsTotalExact = response.TotalExact;
            }
            foreach (var group in response.Groups)
                AudiobookGroupsHost.Children.Add(CreateAudiobookGroupCard(group, _currentAudiobookAxis == "series"));

            _audiobookGroupsOffset += response.Groups.Count;
            _audiobookGroupsHasMore = response.HasMore;
            var noun = AudiobookGroupNoun(_currentAudiobookAxis);
            AudiobookGroupCountText.Text = _audiobookGroupsTotalExact
                ? $"{_audiobookGroupsOffset:N0} of {_audiobookGroupsTotal:N0} {noun}"
                : $"{_audiobookGroupsOffset:N0}{(_audiobookGroupsHasMore ? "+" : "")} {noun}";
            AudiobookGroupsEmpty.Text = string.IsNullOrWhiteSpace(AudiobookGroupSearchBox.Text)
                ? $"No {noun} found in this library."
                : $"No {noun} match “{AudiobookGroupSearchBox.Text.Trim()}”.";
            AudiobookGroupsEmpty.Visibility = AudiobookGroupsHost.Children.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AudiobookGroupsEmpty.Text = $"Could not load {AudiobookGroupNoun(_currentAudiobookAxis)}: {ex.Message}";
            AudiobookGroupsEmpty.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoadingAudiobookGroups = false;
            AudiobookGroupsLoading.IsActive = false;
            AudiobookGroupsLoading.Visibility = Visibility.Collapsed;
        }
    }

    private FrameworkElement CreateAudiobookGroupCard(AudiobookGroup group, bool large)
    {
        var cover = CreateAudiobookGroupCover(group, large);
        var title = new TextBlock
        {
            Text = group.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        var stats = new TextBlock
        {
            Text = AudiobookGroupStats(group),
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        var button = new Button
        {
            Padding = large ? new Thickness(0) : new Thickness(16, 12, 16, 12),
            Background = large ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Tag = group,
        };

        if (large)
        {
            button.Width = 220;
            button.Content = new StackPanel
            {
                Width = 220,
                Spacing = 3,
                Children = { cover, title, stats },
            };
        }
        else
        {
            button.Width = 420;
            var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(title);
            text.Children.Add(stats);
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } }, ColumnSpacing = 14 };
            row.Children.Add(cover);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 13, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"], VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chevron, 2);
            row.Children.Add(chevron);
            button.Content = row;
        }

        button.Click += async (_, _) => await SelectAudiobookGroupAsync(group.Name);
        return button;
    }

    private FrameworkElement CreateAudiobookGroupCover(AudiobookGroup group, bool large)
    {
        var size = large ? 220d : 56d;
        var host = new Grid { Width = large ? size : 96, Height = size };
        var urls = group.PosterUrls.Take(3).ToList();
        if (urls.Count == 0)
        {
            host.Children.Add(new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(10),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceRaisedBrush"],
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = GroupInitials(group.Name),
                    FontSize = large ? 24 : 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
            return host;
        }

        for (var index = urls.Count - 1; index >= 0; index--)
        {
            var poster = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(10),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"],
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = large ? new Thickness(index * 4, 0, 0, 0) : new Thickness(index * 20, 0, 0, 0),
            };
            host.Children.Add(poster);
            _ = LoadAudiobookGroupPosterAsync(poster, group.Name, urls[index], size);
        }
        return host;
    }

    private async Task LoadAudiobookGroupPosterAsync(Border host, string groupName, string url, double size)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();
            var key = $"audiobook-group-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(groupName + url))).Substring(0, 16)}";
            var path = await imageService.GetImageDiskPathAsync(key, "poster", url, httpClient);
            if (string.IsNullOrWhiteSpace(path)) return;
            host.Child = new Image
            {
                Source = new BitmapImage { UriSource = new Uri(path), DecodePixelWidth = (int)Math.Ceiling(size * 1.25) },
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
            };
        }
        catch { }
    }

    private async Task SelectAudiobookGroupAsync(string name)
    {
        switch (_currentAudiobookAxis)
        {
            case "author": ViewModel.SelectedAuthor = name; AuthorBox.Text = name; break;
            case "narrator": ViewModel.SelectedNarrator = name; NarratorBox.Text = name; break;
            case "series": ViewModel.SelectedSeries = name; SeriesBox.Text = name; break;
        }
        _currentAudiobookAxis = "books";
        UpdateAudiobookAxisButtons();
        AudiobookGroupsPanel.Visibility = Visibility.Collapsed;
        LibraryContentArea.Visibility = Visibility.Visible;
        SortComboBox.Visibility = Visibility.Visible;
        OrderComboBox.Visibility = Visibility.Visible;
        OpenFiltersButton.Visibility = Visibility.Visible;
        ResultCountText.Visibility = Visibility.Collapsed;
        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
        SaveViewState(_currentTab);
    }

    private static string AudiobookGroupNoun(string axis) => axis switch
    {
        "author" => "authors",
        "narrator" => "narrators",
        _ => "series",
    };

    private static string AudiobookGroupStats(AudiobookGroup group)
    {
        var hours = group.TotalDurationSeconds / 3600;
        var minutes = (group.TotalDurationSeconds % 3600) / 60;
        var parts = new List<string>
        {
            $"{group.ItemCount:N0} {(group.ItemCount == 1 ? "book" : "books")}",
            hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m",
        };
        if (group.InProgressCount > 0) parts.Add($"{group.InProgressCount} in progress");
        else if (group.FinishedCount == group.ItemCount && group.ItemCount > 0) parts.Add("all finished");
        else if (group.FinishedCount > 0) parts.Add($"{group.FinishedCount} finished");
        return string.Join(" · ", parts);
    }

    private static string GroupInitials(string name) => string.Concat(name
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2)
        .Select(part => char.ToUpperInvariant(part[0])));

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
        card.SetLayout(layout.ItemWidth, layout.PosterHeight, layout.ItemHeight);
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
        var availableWidth = Math.Max(
            120,
            GetLibraryViewportWidth());
        var columns = availableWidth switch
        {
            >= 1000 => 8,
            >= 744 => 7,
            >= 508 => 5,
            >= 380 => 4,
            _ => 3,
        };
        columns = Math.Max(1, Math.Min(columns, (int)Math.Floor((availableWidth + columnGap) / (100 + columnGap))));
        var itemWidth = Math.Max(100, (availableWidth - columnGap * (columns - 1)) / columns);
        var isAudiobook = ViewModel.Library?.Type is "audiobook" or "audiobooks";
        var posterHeight = isAudiobook ? itemWidth : itemWidth * 1.5;
        var itemHeight = posterHeight + 56;
        return new GridLayoutInfo(
            Columns: columns,
            AvailableWidth: availableWidth,
            ItemWidth: itemWidth,
            PosterHeight: posterHeight,
            ItemHeight: itemHeight,
            ColumnGap: columnGap,
            RowGap: rowGap);
    }

    private readonly record struct GridLayoutInfo(
        int Columns,
        double AvailableWidth,
        double ItemWidth,
        double PosterHeight,
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

        BuildLibrarySkeletons();
        LibrarySkeletonScroll.Visibility = Visibility.Visible;
        var overlayWarmup = App.Services.GetRequiredService<global::SiloPlayer.Services.CardOverlayService>().EnsureLoadedAsync();
        try
        {
            await ViewModel.LoadCommand.ExecuteAsync(null);
            try { await overlayWarmup; } catch { }
        }
        finally
        {
            LibrarySkeletonScroll.Visibility = Visibility.Collapsed;
        }
    }

    private void BuildLibrarySkeletons()
    {
        LibrarySkeletonHost.Children.Clear();
        var layout = GetGridLayout();
        for (var index = 0; index < 24; index++)
        {
            var skeleton = new StackPanel { Width = layout.ItemWidth, Spacing = 8 };
            skeleton.Children.Add(new Border
            {
                Width = layout.ItemWidth,
                Height = layout.PosterHeight,
                CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
            });
            skeleton.Children.Add(new Border
            {
                Width = layout.ItemWidth * 0.75,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            LibrarySkeletonHost.Children.Add(skeleton);
        }
    }

    private void ReleaseRecommendedContent()
    {
        _recommendationsVersion++;
        if (!_recommendedLoaded) return;

        RecommendedHeroCarousel.ItemsSource = null;
        RecommendedHeroCarousel.Visibility = Visibility.Collapsed;
        RecommendedNowListeningHero.Visibility = Visibility.Collapsed;
        RecommendedHeroSkeleton.Visibility = Visibility.Collapsed;
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
        RecommendedNowListeningHero.Visibility = Visibility.Collapsed;
        RecommendedHeroSkeleton.Visibility = Visibility.Collapsed;

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
            var layoutResponse = await catalogApi.GetLibraryLayoutAsync(libraryId);
            if (version != _recommendationsVersion || _currentTab != "Recommended")
                return;

            RecommendedLoading.IsActive = false;
            RecommendedLoading.Visibility = Visibility.Collapsed;

            if (layoutResponse.Sections.Count == 0)
            {
                RecommendedError.Text = "No recommendations available yet.";
                RecommendedErrorPanel.Visibility = Visibility.Visible;
                return;
            }

            var heroLayout = layoutResponse.Sections.FirstOrDefault(section => section.Featured);
            if (heroLayout != null)
                RecommendedHeroSkeleton.Visibility = Visibility.Visible;

            var rows = new Dictionary<string, SectionRow>(StringComparer.Ordinal);
            foreach (var layout in layoutResponse.Sections.Where(section => !ReferenceEquals(section, heroLayout)))
            {
                var placeholder = ToLoadingSection(layout);
                var row = CreateLibrarySectionRow(placeholder, libraryId);
                rows[layout.Id] = row;
                RecommendedSectionsPanel.Children.Add(row);
            }

            using var gate = new SemaphoreSlim(4, 4);
            var tasks = layoutResponse.Sections.Select(async layout =>
            {
                await gate.WaitAsync();
                try
                {
                    var response = await catalogApi.GetLibrarySectionItemsAsync(libraryId, layout.Id);
                    if (version != _recommendationsVersion || _currentTab != "Recommended") return;
                    var section = response.Section;

                    if (ReferenceEquals(layout, heroLayout))
                    {
                        RecommendedHeroSkeleton.Visibility = Visibility.Collapsed;
                        if (section?.Items.Count > 0)
                        {
                            var isAudiobook = ViewModel.Library?.Type is "audiobook" or "audiobooks";
                            if (isAudiobook && section.SectionType == "continue_watching")
                            {
                                RecommendedNowListeningHero.Bind(section.Items[0]);
                                RecommendedNowListeningHero.Visibility = Visibility.Visible;
                                if (section.Items.Count > 1)
                                {
                                    var rest = new HomeSectionWithItems
                                    {
                                        Id = $"{section.Id}-rest",
                                        SectionType = "continue_listening",
                                        Title = section.Title,
                                        ItemLimit = section.Items.Count - 1,
                                        TotalCount = section.Items.Count - 1,
                                        Items = new System.Collections.ObjectModel.ObservableCollection<MediaItem>(section.Items.Skip(1)),
                                    };
                                    var restRow = CreateLibrarySectionRow(rest, libraryId);
                                    var firstDynamic = RecommendedSectionsPanel.Children
                                        .Select((child, index) => (child, index))
                                        .FirstOrDefault(pair => pair.child is SectionRow).index;
                                    RecommendedSectionsPanel.Children.Insert(Math.Max(5, firstDynamic), restRow);
                                }
                            }
                            else
                            {
                                var limit = section.ItemLimit > 0 ? section.ItemLimit : section.Items.Count;
                                RecommendedHeroCarousel.ItemsSource = section.Items.Take(limit).ToList();
                                RecommendedHeroCarousel.Visibility = Visibility.Visible;
                            }
                            UpdateHeaderOverlayMode(true);
                        }
                        return;
                    }

                    if (!rows.TryGetValue(layout.Id, out var row)) return;
                    if (section == null || section.Items.Count == 0)
                    {
                        RecommendedSectionsPanel.Children.Remove(row);
                        return;
                    }
                    ConfigureLibrarySectionRow(row, section, libraryId);
                    row.Section = section;
                }
                catch
                {
                    if (version != _recommendationsVersion || _currentTab != "Recommended") return;
                    if (ReferenceEquals(layout, heroLayout))
                    {
                        RecommendedHeroSkeleton.Visibility = Visibility.Collapsed;
                        RecommendedError.Text = "The featured section could not be loaded right now.";
                        RecommendedErrorPanel.Visibility = Visibility.Visible;
                        return;
                    }

                    if (rows.TryGetValue(layout.Id, out var row))
                    {
                        var failed = ToLoadingSection(layout);
                        failed.LoadFailed = true;
                        row.Section = failed;
                    }
                }
                finally
                {
                    gate.Release();
                }
            });

            await Task.WhenAll(tasks);
            if (version == _recommendationsVersion && _currentTab == "Recommended")
                await LoadPinnedCollectionRowsAsync(catalogApi, libraryId, version);
        }
        catch (Exception ex)
        {
            if (version != _recommendationsVersion)
                return;

            RecommendedLoading.IsActive = false;
            RecommendedLoading.Visibility = Visibility.Collapsed;
            RecommendedHeroSkeleton.Visibility = Visibility.Collapsed;
            RecommendedError.Text = $"Failed to load recommendations: {ex.Message}";
            RecommendedErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private static HomeSectionWithItems ToLoadingSection(HomeSection layout) => new()
    {
        Id = layout.Id,
        SectionType = layout.SectionType,
        Title = layout.Title,
        Featured = layout.Featured,
        ItemLimit = layout.ItemLimit,
        IsCustom = layout.IsCustom,
        Customized = layout.Customized,
    };

    private SectionRow CreateLibrarySectionRow(HomeSectionWithItems section, int libraryId)
    {
        var row = new SectionRow { LibraryId = libraryId };
        ConfigureLibrarySectionRow(row, section, libraryId);
        row.Section = section;
        return row;
    }

    private void ConfigureLibrarySectionRow(SectionRow row, HomeSectionWithItems section, int libraryId)
    {
        row.OnRetry = ignored => { _ = RetryLibrarySectionAsync(row, section.Id, libraryId); };
        row.OnViewAll = SectionRow.IsBrowseSupported(section.SectionType) &&
            (section.TotalCount <= 0 || section.TotalCount > section.ItemLimit)
            ? () => App.Services.GetRequiredService<NavigationService>().Navigate<CatalogPage>(new CatalogNavigation(
                Source: "section",
                Title: section.Title,
                Scope: "library",
                SectionId: section.Id,
                LibraryId: libraryId))
            : null;
    }

    private async Task RetryLibrarySectionAsync(SectionRow row, string sectionId, int libraryId)
    {
        var placeholder = row.Section;
        if (placeholder == null) return;
        placeholder.LoadFailed = false;
        row.Section = null;
        row.Section = placeholder;
        try
        {
            var response = await App.Services.GetRequiredService<CatalogApi>()
                .GetLibrarySectionItemsAsync(libraryId, sectionId);
            if (response.Section == null || response.Section.Items.Count == 0)
            {
                RecommendedSectionsPanel.Children.Remove(row);
                return;
            }
            ConfigureLibrarySectionRow(row, response.Section, libraryId);
            row.Section = response.Section;
        }
        catch
        {
            placeholder.LoadFailed = true;
            row.Section = null;
            row.Section = placeholder;
        }
    }

    private async Task LoadPinnedCollectionRowsAsync(CatalogApi catalogApi, int libraryId, int version)
    {
        if (App.MainWindowInstance == null) return;
        foreach (var pin in App.MainWindowInstance.GetSidebarPins(libraryId, "collection"))
        {
            try
            {
                var response = await catalogApi.GetLibraryCollectionItemsAsync(libraryId, pin.Id);
                if (version != _recommendationsVersion) return;
                if (response.Items.Count == 0) continue;
                var section = new HomeSectionWithItems
                {
                    Id = $"pinned-collection-{pin.Id}",
                    SectionType = "collection",
                    Title = pin.Label,
                    ItemLimit = response.Items.Count,
                    TotalCount = response.Total,
                    Items = new System.Collections.ObjectModel.ObservableCollection<MediaItem>(response.Items),
                };
                var row = new SectionRow { Section = section };
                row.OnViewAll = () => App.Services.GetRequiredService<NavigationService>()
                    .Navigate<CollectionBrowsePage>(new CollectionBrowsePage.NavArgs
                    {
                        CollectionId = pin.Id,
                        Title = pin.Label,
                        Subtitle = ViewModel.Library?.Name,
                        LibraryId = libraryId,
                    });
                RecommendedSectionsPanel.Children.Add(row);
            }
            catch { }
        }
    }

    private async void RecommendedRetry_Click(object sender, RoutedEventArgs e)
    {
        _recommendedLoaded = false;
        await LoadRecommendationsAsync();
    }

    // ===== Collections Tab =====

    private async Task LoadCollectionsAsync()
    {
        _collectionsLoaded = true;
        BuildCollectionSkeletons();
        CollectionsSkeletonHost.Visibility = Visibility.Visible;

        await ViewModel.LoadCollectionsCommand.ExecuteAsync(null);
        _collectionsLoaded = ViewModel.CollectionsLoaded;

        CollectionsSkeletonHost.Visibility = Visibility.Collapsed;

        BuildCollectionCards();
    }

    private void BuildCollectionCards()
    {
        var sections = ViewModel.CollectionSections;
        _collectionCardWidth = GetCollectionCardWidth();

        if (sections.Count == 0)
        {
            CollectionsEmptyCard.Visibility = Visibility.Visible;
            CollectionsRepeater.Visibility = Visibility.Collapsed;
            RemoveDynamicCollectionPanels();
            return;
        }

        CollectionsEmptyCard.Visibility = Visibility.Collapsed;
        CollectionsRepeater.Visibility = Visibility.Visible;
        CollectionsRepeater.ItemsSource = null;

        var sectionsPanel = new StackPanel { Spacing = 28 };
        foreach (var section in sections)
            sectionsPanel.Children.Add(BuildCollectionSection(section));

        // Replace the empty card with our content
        var parent = (StackPanel)CollectionsRepeater.Parent!;
        int idx = parent.Children.IndexOf(CollectionsRepeater);
        if (idx >= 0)
        {
            RemoveDynamicCollectionPanels();
            sectionsPanel.Tag = "CollectionGrid";
            parent.Children.Insert(idx + 1, sectionsPanel);
        }
    }

    private FrameworkElement BuildCollectionSection(LibraryTabSection section)
    {
        var sectionPanel = new StackPanel { Spacing = 12 };
        var cardWidth = GetCollectionCardWidth();

        if (section.HasTitle)
        {
            sectionPanel.Children.Add(new TextBlock
            {
                Text = section.Title,
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"]
            });
        }

        var wrapPanel = new WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 16 };
        foreach (var collection in section.Collections)
            wrapPanel.Children.Add(CreateCollectionCard(collection, cardWidth));

        sectionPanel.Children.Add(wrapPanel);
        return sectionPanel;
    }

    private void RemoveDynamicCollectionPanels()
    {
        if (CollectionsRepeater.Parent is not StackPanel parent) return;

        for (int i = parent.Children.Count - 1; i >= 0; i--)
        {
            if (parent.Children[i] is StackPanel sp && sp.Name == null && sp != parent && sp.Tag is "CollectionGrid")
                parent.Children.RemoveAt(i);
        }
    }

    private FrameworkElement CreateCollectionCard(LibraryTabCollectionDisplay collection, double cardWidth)
    {
        var posterHeight = cardWidth * 1.5;
        // Poster area
        var posterBorder = new Border
        {
            Width = cardWidth,
            Height = posterHeight,
            CornerRadius = new CornerRadius(12),
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

        var countBadge = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(170, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 8, 8),
            Child = new TextBlock
            {
                Text = collection.ItemCount.ToString(),
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
            }
        };

        var posterGrid = new Grid { Width = cardWidth, Height = posterHeight };
        posterGrid.Children.Add(posterBorder);
        posterGrid.Children.Add(countBadge);

        // Title
        var titleText = new TextBlock
        {
            Text = collection.Title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Margin = new Thickness(2, 10, 2, 0)
        };

        var content = new StackPanel
        {
            Width = cardWidth,
            Children = { posterGrid, titleText }
        };

        if (collection.IsUserCollection)
        {
            content.Children.Add(new TextBlock
            {
                Text = "User collection",
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TertiaryTextBrush"],
                Margin = new Thickness(2, 0, 2, 0)
            });
        }

        var card = new Grid
        {
            Width = cardWidth,
            Children = { content },
            Tag = collection
        };

        Button? pinButton = null;
        FontIcon? pinIcon = null;
        if (!collection.IsUserCollection && ViewModel.Library is { } ownerLibrary)
        {
            var pinned = App.MainWindowInstance?.IsSidebarPin(ownerLibrary.Id, "collection", collection.Id) == true;
            pinIcon = new FontIcon { Glyph = pinned ? "\uE841" : "\uE840", FontSize = 14 };
            pinButton = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Margin = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(8),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(170, 0, 0, 0)),
                Content = pinIcon,
                Opacity = pinned ? 1 : 0,
            };
            ToolTipService.SetToolTip(pinButton, pinned ? "Unpin from sidebar" : "Pin to sidebar");
            pinButton.Tapped += (_, tapArgs) => tapArgs.Handled = true;
            pinButton.Click += async (_, args) =>
            {
                if (App.MainWindowInstance is not MainWindow window) return;
                pinButton.IsEnabled = false;
                try
                {
                    var nowPinned = await window.ToggleSidebarPinAsync(
                        ownerLibrary.Id, "collection", collection.Id, collection.Title);
                    pinIcon.Glyph = nowPinned ? "\uE841" : "\uE840";
                    pinButton.Opacity = nowPinned ? 1 : 0;
                    ToolTipService.SetToolTip(pinButton, nowPinned ? "Unpin from sidebar" : "Pin to sidebar");
                }
                finally { pinButton.IsEnabled = true; }
            };
            card.Children.Add(pinButton);
        }

        card.PointerEntered += (s, _) =>
        {
            if (pinButton != null) pinButton.Opacity = 1;
        };
        card.PointerExited += (s, _) =>
        {
            if (pinButton != null && ViewModel.Library is { } lib
                && App.MainWindowInstance?.IsSidebarPin(lib.Id, "collection", collection.Id) != true)
                pinButton.Opacity = 0;
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
                IsUserCollection = collection.IsUserCollection,
                LibraryId = ViewModel.Library?.Id,
            });
        };

        return card;
    }

    private double GetCollectionCardWidth()
    {
        var viewportWidth = Math.Max(360, CollectionsPanel.ActualWidth);
        var contentWidth = Math.Min(1320, Math.Max(300, viewportWidth - 80));
        var columns = contentWidth switch
        {
            >= 1020 => 8,
            >= 764 => 7,
            >= 508 => 5,
            >= 380 => 4,
            _ => 3,
        };
        return Math.Max(96, (contentWidth - (columns - 1) * 12) / columns);
    }

    private void CollectionsPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_collectionsLoaded || CollectionsPanel.Visibility != Visibility.Visible)
            return;

        var nextWidth = GetCollectionCardWidth();
        if (Math.Abs(nextWidth - _collectionCardWidth) < 1)
            return;

        _collectionCardWidth = nextWidth;
        BuildCollectionCards();
    }

    private void BuildCollectionSkeletons()
    {
        CollectionsSkeletonHost.Children.Clear();
        var width = GetCollectionCardWidth();
        for (var index = 0; index < 24; index++)
        {
            var skeleton = new StackPanel { Width = width, Spacing = 8 };
            skeleton.Children.Add(new Border
            {
                Width = width,
                Height = width * 1.5,
                CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
            });
            skeleton.Children.Add(new Border
            {
                Width = width * 0.75,
                Height = 16,
                CornerRadius = new CornerRadius(4),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SurfaceBrush"],
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            CollectionsSkeletonHost.Children.Add(skeleton);
        }
    }

    private async Task LoadCollectionPosterAsync(Border posterBorder, LibraryTabCollectionDisplay collection)
    {
        try
        {
            var imageService = App.Services.GetRequiredService<ImageService>();
            var httpClient = App.Services.GetRequiredService<HttpClient>();

            var bytes = await imageService.GetImageAsync(
                $"{(collection.IsUserCollection ? "user" : "library")}_{collection.Id}",
                "collection_poster",
                collection.PosterUrl!,
                httpClient,
                CancellationToken.None);

            if (bytes == null) return;

            var bitmapImage = new BitmapImage
            {
                DecodePixelWidth = Math.Clamp((int)Math.Ceiling(posterBorder.Width * 1.25), 160, 640),
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

    private void UpdateOriginalLanguageCombo()
    {
        UpdateFilterCombo(OriginalLanguageComboBox, ViewModel.OriginalLanguages, "All Languages", ViewModel.SelectedOriginalLanguage, "original-languages");
    }

    private void UpdateNetworkCombo()
    {
        UpdateFilterCombo(NetworkComboBox, ViewModel.Networks, "All Networks", ViewModel.SelectedNetwork, "networks");
    }

    private void AdvancedTextFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;

        _advancedFilterDebounceTimer?.Stop();
        _advancedFilterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _advancedFilterDebounceTimer.Tick += async (_, _) =>
        {
            _advancedFilterDebounceTimer?.Stop();
            _advancedFilterDebounceTimer = null;
            await ApplyAdvancedFiltersAsync();
        };
        _advancedFilterDebounceTimer.Start();
    }

    private async void AdvancedComboFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        await ApplyAdvancedFiltersAsync();
    }

    private async void AdvancedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressFilterEvents) return;
        await ApplyAdvancedFiltersAsync();
    }

    private async Task ApplyAdvancedFiltersAsync()
    {
        ViewModel.SelectedMinimumRating = NullIfWhiteSpace(MinimumRatingBox.Text);
        ViewModel.SelectedOriginalLanguage = SelectedFilterValue(OriginalLanguageComboBox);
        ViewModel.SelectedActor = NullIfWhiteSpace(ActorBox.Text);
        ViewModel.SelectedDirector = NullIfWhiteSpace(DirectorBox.Text);
        ViewModel.SelectedWriter = NullIfWhiteSpace(WriterBox.Text);
        ViewModel.SelectedProducer = NullIfWhiteSpace(ProducerBox.Text);
        ViewModel.SelectedAuthor = NullIfWhiteSpace(AuthorBox.Text);
        ViewModel.SelectedNarrator = NullIfWhiteSpace(NarratorBox.Text);
        ViewModel.SelectedSeries = NullIfWhiteSpace(SeriesBox.Text);
        ViewModel.SelectedNetwork = SelectedFilterValue(NetworkComboBox);
        ViewModel.SelectedMatchStatus = SelectedTag(MatchStatusComboBox);
        ViewModel.SelectedWatchStatus = SelectedTag(WatchStatusComboBox);
        ViewModel.SelectedAddedInLast = NullIfWhiteSpace(AddedInLastBox.Text);
        ViewModel.SelectedReleasedInLast = NullIfWhiteSpace(ReleasedInLastBox.Text);
        ViewModel.SelectedFourK = FourKToggle.IsOn;
        ViewModel.SelectedHdr = HdrToggle.IsOn;
        ViewModel.SelectedDolbyVision = DolbyVisionToggle.IsOn;

        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
        UpdateActiveFilterBadges();
        SaveViewState(_currentTab);
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? SelectedTag(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string value || string.IsNullOrEmpty(value))
            return null;
        return value;
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

        if (ViewModel.UseAdvancedRules)
        {
            foreach (var rule in ViewModel.AdvancedGroups
                         .SelectMany(group => group.Rules)
                         .Where(rule => !string.IsNullOrWhiteSpace(rule.Value?.ToString())).ToList())
            {
                var captured = rule;
                filters.Add((captured.Field.Replace('_', ' '),
                    $"{captured.Op.Replace('_', ' ')} {FormatAdvancedRuleValue(captured.Value)}",
                    () =>
                    {
                        var owner = ViewModel.AdvancedGroups.FirstOrDefault(group => group.Rules.Contains(captured));
                        owner?.Rules.Remove(captured);
                        if (owner is { Rules.Count: 0 }) ViewModel.AdvancedGroups.Remove(owner);
                        if (ViewModel.AdvancedGroups.Count == 0)
                            ViewModel.AdvancedGroups.Add(LibraryViewModel.CreateEmptyAdvancedGroup());
                        BuildAdvancedRulesPanel();
                    }));
            }
        }

        if (!ViewModel.UseAdvancedRules)
        {
            if (!string.IsNullOrEmpty(ViewModel.SelectedType))
            {
                var label = ViewModel.SelectedType switch
                {
                    "movie" => "Movies",
                    "series" => "Series",
                    "episode" => "Episodes",
                    _ => ViewModel.SelectedType
                };
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
            if (!string.IsNullOrEmpty(ViewModel.SelectedMinimumRating))
                filters.Add(("IMDb", $"{ViewModel.SelectedMinimumRating}+", () => { ViewModel.SelectedMinimumRating = null; MinimumRatingBox.Text = ""; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedOriginalLanguage))
                filters.Add(("Language", ViewModel.SelectedOriginalLanguage, () => { ViewModel.SelectedOriginalLanguage = null; OriginalLanguageComboBox.SelectedIndex = 0; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedActor))
                filters.Add(("Actor", ViewModel.SelectedActor, () => { ViewModel.SelectedActor = null; ActorBox.Text = ""; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedDirector))
                filters.Add(("Director", ViewModel.SelectedDirector, () => { ViewModel.SelectedDirector = null; DirectorBox.Text = ""; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedWriter))
                filters.Add(("Writer", ViewModel.SelectedWriter, () => { ViewModel.SelectedWriter = null; WriterBox.Text = ""; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedProducer))
                filters.Add(("Producer", ViewModel.SelectedProducer, () => { ViewModel.SelectedProducer = null; ProducerBox.Text = ""; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedAuthor))
                filters.Add(("Author", ViewModel.SelectedAuthor, () => { ViewModel.SelectedAuthor = null; AuthorBox.Text = ""; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedNarrator))
                filters.Add(("Narrator", ViewModel.SelectedNarrator, () => { ViewModel.SelectedNarrator = null; NarratorBox.Text = ""; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedSeries))
                filters.Add(("Series", ViewModel.SelectedSeries, () => { ViewModel.SelectedSeries = null; SeriesBox.Text = ""; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedNetwork))
                filters.Add(("Network", ViewModel.SelectedNetwork, () => { ViewModel.SelectedNetwork = null; NetworkComboBox.SelectedIndex = 0; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedMatchStatus))
                filters.Add(("Match", ViewModel.SelectedMatchStatus, () => { ViewModel.SelectedMatchStatus = null; MatchStatusComboBox.SelectedIndex = 0; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedWatchStatus))
                filters.Add((WatchStatusLabel.Text, ViewModel.SelectedWatchStatus.Replace('_', ' '), () => { ViewModel.SelectedWatchStatus = null; WatchStatusComboBox.SelectedIndex = 0; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedAddedInLast))
                filters.Add(("Added", ViewModel.SelectedAddedInLast, () => { ViewModel.SelectedAddedInLast = null; AddedInLastBox.Text = ""; }));
            if (!string.IsNullOrEmpty(ViewModel.SelectedReleasedInLast))
                filters.Add(("Released", ViewModel.SelectedReleasedInLast, () => { ViewModel.SelectedReleasedInLast = null; ReleasedInLastBox.Text = ""; }));
            if (ViewModel.SelectedFourK)
                filters.Add(("Quality", "4K", () => { ViewModel.SelectedFourK = false; FourKToggle.IsOn = false; }));
            if (ViewModel.SelectedHdr)
                filters.Add(("Quality", "HDR", () => { ViewModel.SelectedHdr = false; HdrToggle.IsOn = false; }));
            if (ViewModel.SelectedDolbyVision)
                filters.Add(("Quality", "Dolby Vision", () => { ViewModel.SelectedDolbyVision = false; DolbyVisionToggle.IsOn = false; }));
        }

        if (filters.Count == 0)
        {
            ActiveFiltersBar.Visibility = Visibility.Collapsed;
            FilterCountBadge.Visibility = Visibility.Collapsed;
            FilterCountText.Text = "";
            return;
        }

        ActiveFiltersBar.Visibility = Visibility.Visible;
        FilterCountText.Text = filters.Count.ToString();
        FilterCountBadge.Visibility = Visibility.Visible;
        ClearAllFiltersButton.Visibility = filters.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var (label, value, clearAction) in filters)
        {
            var badge = CreateFilterBadge(label, value, clearAction);
            FilterBadgesPanel.Children.Add(badge);
        }
    }

    private static string FormatAdvancedRuleValue(object? value)
    {
        if (value is System.Collections.IEnumerable values and not string)
            return string.Join(" – ", values.Cast<object?>().Select(entry => entry?.ToString() ?? ""));
        return value?.ToString() ?? "";
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
        if (ViewModel.UseAdvancedRules)
        {
            ViewModel.AdvancedGroups.Clear();
            ViewModel.AdvancedGroups.Add(LibraryViewModel.CreateEmptyAdvancedGroup());
            ViewModel.AdvancedRulesMatch = "all";
            AdvancedMatchComboBox.SelectedIndex = 0;
            BuildAdvancedRulesPanel();
        }
        ViewModel.SelectedType = null;
        ViewModel.SelectedGenre = null;
        ViewModel.SelectedContentRating = null;
        ViewModel.SelectedStudio = null;
        ViewModel.SelectedCountry = null;
        ViewModel.SelectedResolution = null;
        ViewModel.SelectedAudioLanguage = null;
        ViewModel.SelectedYearMin = null;
        ViewModel.SelectedYearMax = null;
        ViewModel.SelectedMinimumRating = null;
        ViewModel.SelectedOriginalLanguage = null;
        ViewModel.SelectedActor = null;
        ViewModel.SelectedDirector = null;
        ViewModel.SelectedWriter = null;
        ViewModel.SelectedProducer = null;
        ViewModel.SelectedAuthor = null;
        ViewModel.SelectedNarrator = null;
        ViewModel.SelectedSeries = null;
        ViewModel.SelectedNetwork = null;
        ViewModel.SelectedMatchStatus = null;
        ViewModel.SelectedWatchStatus = null;
        ViewModel.SelectedAddedInLast = null;
        ViewModel.SelectedReleasedInLast = null;
        ViewModel.SelectedFourK = false;
        ViewModel.SelectedHdr = false;
        ViewModel.SelectedDolbyVision = false;
        MediaTypeComboBox.SelectedIndex = 0;
        GenreComboBox.SelectedIndex = 0;
        ContentRatingComboBox.SelectedIndex = 0;
        StudioComboBox.SelectedIndex = 0;
        CountryComboBox.SelectedIndex = 0;
        ResolutionComboBox.SelectedIndex = 0;
        AudioLangComboBox.SelectedIndex = 0;
        YearMinBox.Text = "";
        YearMaxBox.Text = "";
        MinimumRatingBox.Text = "";
        OriginalLanguageComboBox.SelectedIndex = 0;
        ActorBox.Text = "";
        DirectorBox.Text = "";
        WriterBox.Text = "";
        ProducerBox.Text = "";
        AuthorBox.Text = "";
        NarratorBox.Text = "";
        SeriesBox.Text = "";
        NetworkComboBox.SelectedIndex = 0;
        MatchStatusComboBox.SelectedIndex = 0;
        WatchStatusComboBox.SelectedIndex = 0;
        AddedInLastBox.Text = "";
        ReleasedInLastBox.Text = "";
        FourKToggle.IsOn = false;
        HdrToggle.IsOn = false;
        DolbyVisionToggle.IsOn = false;
        _suppressFilterEvents = false;

        ActiveFiltersBar.Visibility = Visibility.Collapsed;
        FilterCountBadge.Visibility = Visibility.Collapsed;
        FilterCountText.Text = "";

        await ViewModel.ApplyFilterCommand.ExecuteAsync(null);
        await FillViewportAsync();
    }
}
