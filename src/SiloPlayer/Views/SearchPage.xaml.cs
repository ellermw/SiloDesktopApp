using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Messaging;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Services;
using System.Collections.ObjectModel;
using Presentation = SiloPlayer.Core.Services.SearchResultsPresentation;
using SearchView = SiloPlayer.Core.Services.SearchResultsView;

namespace SiloPlayer.Views;

public sealed record SearchNavigation(string Query, string? MediaScope = null, int? LibraryId = null);

public sealed partial class SearchPage : Page
{
    public SearchViewModel ViewModel { get; }
    private readonly UICustomizationService _uiCustomizationService;
    private bool _filterInitializing = true;
    private bool _initialized;
    private bool _isNavigated;
    private Task? _initializationTask;
    private double _catalogCardWidth = 178;
    private bool _resultsScrollUserScrolled;
    private Task? _searchFiltersTask;
    private string? _searchFiltersTaskKey;
    private long _navigationGeneration;
    private SiloPlayer.Core.Api.ApiIdentityContext? _metadataIdentity;
    private long _metadataRevision;
    private readonly SiloPlayer.Core.Api.CatalogApi _catalogApi;
    private readonly SiloPlayer.Core.Services.LocalizedMediaReads _localizedReads;
    private readonly CatalogSortChoices.Choice[] _resultSortChoices;
    private IReadOnlySet<string> _shownRatingSources = new HashSet<string>();
    // Official #4450: the view the viewer asked for, and the one that shows once discovery has answered.
    private SearchView _view = SearchView.Overview;
    private SearchView _shownView = SearchView.Overview;
    private int _gridColumns = 6;
    private double _pageWidth = 1000;
    private double _railCardWidth = 178;
    private string? _railKey;
    private CancellationTokenSource? _peoplePhotosCts;
    private readonly ObservableCollection<MediaItem> _bestResults = [];
    private readonly ObservableCollection<MediaItem> _moreResults = [];
    private TextBlock _firstShowAllLabel = null!, _moreShowAllLabel = null!, _showAllPillLabel = null!, _showAllPillCount = null!;
    private const int SkeletonCount = 12;

    public SearchPage()
    {
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        this.InitializeComponent();
        ImeCompositionGuard.Attach(SearchBox);
        SiloPlayer.Services.AppLocalization.Register(this, () => { RefreshInlineChoices(); ViewModel.RefreshLocalization(); _railKey = null; UpdateResultsState(); UpdateActiveResultFilters(); });
        CatalogToolbarChoices.Apply(ResultTypeCombo, ResultSortCombo, ResultOrderCombo);
        _catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
        _localizedReads = App.Services.GetRequiredService<SiloPlayer.Core.Api.SiloApiClient>().LocalizedReads;
        _resultSortChoices = CatalogSortChoices.Capture(ResultSortCombo);
        UpdateResultSortChoices();
        ResultFiltersSheet.ConfigureFilterHeader(SearchQueryFilters.DetachModeSelector(), "Refine your catalog results", "web.catalog.filters.subtitle");
        NavigationCacheMode = NavigationCacheMode.Required;
        SearchLoadingRepeater.ItemsSource = Enumerable.Range(0, SkeletonCount).ToArray();
        SearchQueryFilters.ShowSort = false; // the toolbar owns a search's sort (official showAdvancedSort)
        BuildResultChrome();

        ResultsRepeater.ItemsSource = ViewModel.Results;
        BestRepeater.ItemsSource = _bestResults;
        MoreRepeater.ItemsSource = _moreResults;
        BoxSetSearchRepeater.ItemsSource = ViewModel.BoxSets.Items;
        ViewModel.BoxSets.Items.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateResultsState);
        ViewModel.BoxSets.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateResultsState);
        PeopleRepeater.ItemsSource = ViewModel.PeopleResults;
        RequestResultsRepeater.ItemsSource = ViewModel.OutsideLibraryResults;

        ViewModel.Results.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdateResultsState);
        };

        ViewModel.PeopleResults.CollectionChanged += (_, _) =>
        {
            _peoplePhotosCts?.Cancel();
            DispatcherQueue.TryEnqueue(UpdateResultsState);
        };
        ViewModel.OutsideLibraryResults.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateResultsState);

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ViewModel.OutsidePage) or nameof(ViewModel.OutsideTotalPages) or nameof(ViewModel.OutsideError) or nameof(ViewModel.IsOutsideLoading)
                or nameof(ViewModel.OutsideSettled) or nameof(ViewModel.OutsideFirstPageCount) or nameof(ViewModel.PeopleError) or nameof(ViewModel.IsPeopleSearching)
                or nameof(ViewModel.SortField) or nameof(ViewModel.SortsByRelevance) or nameof(ViewModel.TotalCount) or nameof(ViewModel.IsLoading) or nameof(ViewModel.ErrorMessage)
                or "")
            {
                DispatcherQueue.TryEnqueue(UpdateResultsState);
            }
        };
        SizeChanged += SearchPage_SizeChanged;
        Loaded += (_, _) => PositionSearchSurface();
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(this, (_, change) =>
        {
            var visit = _navigationGeneration;
            var context = change.Context ?? _catalogApi.CaptureContext();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isNavigated || visit != _navigationGeneration || !_catalogApi.IsCurrentContext(context)) return;
                ViewModel.RecordCatalogMutation();
                if (change.Kind == MediaSurfaceChangeKind.ItemMetadataRefreshed)
                    _ = RefreshCatalogMetadataAsync();
                if (change.Kind is MediaSurfaceChangeKind.WatchedMarked or MediaSurfaceChangeKind.WatchedCleared or MediaSurfaceChangeKind.ItemMetadataRefreshed)
                    RefreshBoxSetSearch();
            });
        });
        WeakReferenceMessenger.Default.Register<PlaybackProgressUpdated>(this, (_, change) =>
        {
            var visit = _navigationGeneration;
            var identity = change.Identity ?? _catalogApi.CaptureIdentityContext();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isNavigated && visit == _navigationGeneration && _catalogApi.IsCurrentIdentityContext(identity))
                    RefreshBoxSetsAfterPlayback(change);
            });
        });
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var generation = ++_navigationGeneration;
        _isNavigated = true;
        var requestApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.RequestsApi>();
        _requestIdentity = requestApi.CaptureIdentityContext(); _requestProfileAllowed = requestApi.ProfileRequestsAllowed;
        App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>().ProfileChanged += RequestAuthorityChanged;
        ViewModel.BeginSearchVisit();
        var metadataIdentity = _catalogApi.CaptureIdentityContext();
        var metadataRevision = MetadataRefreshFlow.GetCatalogRevision(metadataIdentity);
        var metadataChanged = _metadataIdentity != metadataIdentity || _metadataRevision != metadataRevision;
        _catalogApi.LibraryCacheInvalidated += RefreshBoxSetSearch;
        _localizedReads.Invalidated += BoxSetLanguageChanged;
        _uiCustomizationService.Changed += UICustomization_Changed;

        if (e.NavigationMode == NavigationMode.Back && !string.IsNullOrWhiteSpace(ViewModel.Query))
        {
            SearchBox.Text = ViewModel.Query;
            PositionSearchSurface();
            UpdateScopeButtons();
            UpdateResultsState();
            _ = ViewModel.RefreshBoxSetSearchAsync();
            if (metadataChanged && metadataRevision != 0) _ = RefreshCatalogMetadataAsync();
            var replaysQuery = e.Parameter is string routeQuery && !string.IsNullOrWhiteSpace(routeQuery) ||
                e.Parameter is SearchNavigation scopedQuery && !string.IsNullOrWhiteSpace(scopedQuery.Query);
            if (!replaysQuery) _ = ViewModel.RefreshRetainedOutsideAsync();
        }
        else
        {
            // No retained query needs this revision; any new search reads fresh.
            _metadataIdentity = metadataIdentity;
            _metadataRevision = metadataRevision;
            ViewModel.CancelPendingSearch();
            ViewModel.BeginSearchVisit();
            ViewModel.LibraryId = e.Parameter is SearchNavigation scoped ? scoped.LibraryId : null;
            ViewModel.Query = "";
            ViewModel.Results.Clear();
            ViewModel.PeopleResults.Clear();
            ViewModel.OutsideLibraryResults.Clear();
            _view = SearchView.Overview;
            SearchBox.Text = "";
            PositionSearchSurface();
            UpdateScopeButtons();
            EmptyState.Visibility = Visibility.Visible;
            ResultsState.Visibility = Visibility.Collapsed;
            SearchBox.Focus(FocusState.Programmatic);
        }

        // The empty search surface is interactive immediately. The saved
        // scope loads without blocking typing; query facets remain deferred
        // until the user opens the filter sheet.
        _ = EnsureInitializedAsync();
        _shownRatingSources = _catalogApi.CachedShownRatingSources;
        UpdateResultSortChoices();
        _ = RefreshRatingSortChoicesAsync(generation);
        if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query)) _ = OpenQueryAsync(query, generation);
        else if (e.Parameter is SearchNavigation route && !string.IsNullOrWhiteSpace(route.Query)) _ = OpenQueryAsync(route.Query, generation, route.MediaScope);
    }

    private async Task RefreshCatalogMetadataAsync()
    {
        var visit = _navigationGeneration;
        var identity = _catalogApi.CaptureIdentityContext();
        var revision = MetadataRefreshFlow.GetCatalogRevision(identity);
        // A canceled/failed reread leaves the revision unconsumed for Back.
        if (await ViewModel.RefreshCatalogMetadataAsync() && _isNavigated && visit == _navigationGeneration
            && _catalogApi.IsCurrentIdentityContext(identity))
        {
            _metadataIdentity = identity;
            _metadataRevision = revision;
        }
    }

    private async Task OpenQueryAsync(string query, long generation, string? requestedScope = null)
    {
        await EnsureInitializedAsync();
        if (!_isNavigated || generation != _navigationGeneration) return;
        if (requestedScope != null)
        {
            // Apply route scope after saved preference loading but before the
            // initial query. Clearing a cached query avoids an intermediate
            // request at the new scope with the previous page's search term.
            ViewModel.CancelPendingSearch(); ViewModel.Query = "";
            await ViewModel.SetMediaTypeAsync(requestedScope);
            if (!_isNavigated || generation != _navigationGeneration) return;
            _filterInitializing = true;
            SelectComboTag(ResultTypeCombo, ViewModel.MediaType ?? "all");
            _filterInitializing = false;
            UpdateScopeButtons();
        }
        SearchBox.Text = query.Trim(); ViewModel.Query = query.Trim();
        _searchDebounce?.Stop(); ShowResultsShellForCurrentQuery();
        await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private Task EnsureInitializedAsync() => _initialized
        ? Task.CompletedTask
        : _initializationTask ??= InitializeAsync();

    private async Task InitializeAsync()
    {
        // The empty WebUI search surface does not enumerate every catalog
        // facet. Load only the lightweight saved scope here; query-scoped
        // filters are requested on demand when the filter sheet opens.
        await ViewModel.LoadMediaScopeAsync();
        var context = _catalogApi.CaptureContext();
        var shown = await CatalogSortChoices.LoadShownSourcesAsync(_catalogApi);
        if (!_isNavigated || context != _catalogApi.CaptureContext()) return;
        _shownRatingSources = shown;
        UpdateResultSortChoices();
        _filterInitializing = false;
        _initialized = true;
        UpdateScopeButtons();
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The whole results region follows one set of rules (official SearchResults, #4450); every change of data or view lands here.</summary>
    private void UpdateResultsState()
    {
        bool hasQuery = !string.IsNullOrWhiteSpace(ViewModel.Query);

        EmptyState.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;
        ResultsState.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        if (!hasQuery) return;

        SiloPlayer.Controls.MediaItemCardActions.BindText(ResultsTitle, "Text", element => element.Text = SiloPlayer.Core.Localization.AppStrings.Get("web.catalog.page.resultsFor", new Dictionary<string, object?> { ["query"] = ViewModel.Query }, fallback: $"Results for \"{ViewModel.Query}\""));

        var failed = ViewModel.ErrorMessage != null;
        var libraryKnown = ViewModel.LibraryResultsKnown;
        var libraryEmpty = libraryKnown && ViewModel.Results.Count == 0;
        var requestsEnabled = ViewModel.RequestsEnabled;
        var hasRequestResults = ViewModel.HasRequestResults;
        var deciding = ViewModel.RequestsDeciding;
        _shownView = Presentation.ShownView(_view, deciding, requestsEnabled, hasRequestResults);
        var overview = _shownView == SearchView.Overview;
        var library = _shownView == SearchView.Library;
        var requestsView = _shownView == SearchView.Requests;
        var byRelevance = ViewModel.SortsByRelevance;
        var boxSets = ViewModel.BoxSets.Items.Count;
        var people = ViewModel.PeopleResults.Count;

        RebuildOverviewRows();
        var hasMoreThanShown = Presentation.SplitOverview(ViewModel.Results, _gridColumns, ViewModel.LibraryHasMore).HasMoreThanShown;
        var countLabel = Presentation.CountLabel(ViewModel.Results.Count, ViewModel.LibraryHasMore);
        var requestsSettled = !requestsEnabled || (!deciding && !ViewModel.IsOutsideLoading);
        var nothingFound = Presentation.NothingFound(libraryEmpty, requestsSettled, hasRequestResults, boxSets, people,
            ViewModel.IsPeopleSearching || ViewModel.BoxSets.IsLoading, ViewModel.PeopleError != null);

        // Skeleton while the library search loads (and while a linked request view waits for discovery).
        SearchLoadingRepeater.Visibility = Show(requestsView
            ? deciding
            : (ViewModel.IsLoading || ViewModel.BoxSets.IsLoading) && ViewModel.Results.Count == 0 && people == 0 && boxSets == 0);
        SearchErrorCard.Visibility = Show(failed && !requestsView);
        NoResultsText.Visibility = Show(overview && nothingFound);

        BoxSetSearchSection.Visibility = Show(overview && boxSets > 0);

        // The library's first rows: two rows of best matches, or the same rows as one sorted list.
        FirstRowsSection.Visibility = Show(overview && !failed && _bestResults.Count > 0);
        var sortName = (ResultSortCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "";
        SiloPlayer.Controls.MediaItemCardActions.BindText(FirstRowsTitle, "Text", element => element.Text = SiloPlayer.Core.Localization.AppStrings.Get(
            byRelevance ? "web.search.results.bestMatches" : "web.search.results.inLibrary", fallback: byRelevance ? "Best matches" : "In your library"));
        SortedByPanel.Visibility = Show(!byRelevance);
        SortedByText.Text = Presentation.SortedByText(sortName);
        var showAll = SiloPlayer.Core.Localization.AppStrings.Get("web.search.results.showAll", fallback: "Show all results");
        _firstShowAllLabel.Text = _moreShowAllLabel.Text = _showAllPillLabel.Text = showAll;
        _showAllPillCount.Text = countLabel;
        AutomationProperties.SetName(FirstShowAllButton, showAll); AutomationProperties.SetName(MoreShowAllLink, showAll); AutomationProperties.SetName(ShowAllResultsButton, showAll + " " + countLabel);
        FirstShowAllButton.Visibility = Show(!byRelevance && hasMoreThanShown);

        // More results: under relevance a heading with its own link; under another sort the same list, then the button.
        MoreResultsSection.Visibility = Show(overview && !failed && (_moreResults.Count > 0 || hasMoreThanShown));
        MoreResultsHeader.Visibility = Show(byRelevance && _moreResults.Count > 0);
        MoreShowAllLink.Visibility = Show(byRelevance && hasMoreThanShown);
        MoreRepeater.Visibility = Show(_moreResults.Count > 0);
        ShowAllResultsButton.Visibility = Show(hasMoreThanShown);

        LibraryViewSection.Visibility = Show(library && !failed);

        // People follow the request row under relevance, and end the page under another sort.
        var peopleSearching = ViewModel.IsPeopleSearching && libraryEmpty && people == 0 && ViewModel.PeopleError == null;
        PeopleSection.Visibility = Show(overview && (people > 0 || ViewModel.PeopleError != null || peopleSearching));
        Grid.SetRow(PeopleSection, byRelevance ? 3 : 5);
        PeopleErrorPanel.Visibility = Show(ViewModel.PeopleError != null);
        PeopleErrorText.Text = ViewModel.PeopleError ?? "";
        PeopleSearchingText.Visibility = Show(peopleSearching);
        PeopleRepeater.Visibility = Show(ViewModel.PeopleError == null && people > 0);

        UpdateViewTabs(libraryKnown, countLabel, requestsEnabled, hasRequestResults);
        UpdateRequestRegion(overview, requestsView, deciding, libraryKnown, libraryEmpty, failed, nothingFound, requestsEnabled);
        ResultOrderCombo.Visibility = Show(!byRelevance);
    }

    private void UpdateViewTabs(bool libraryKnown, string countLabel, bool requestsEnabled, bool hasRequestResults)
    {
        StyleViewTab(ViewOverviewButton, _shownView == SearchView.Overview);
        StyleViewTab(ViewLibraryButton, _shownView == SearchView.Library);
        StyleViewTab(ViewRequestsButton, _shownView == SearchView.Requests);
        // A count before the library answers would be 0, or the last search's.
        ViewLibraryCount.Text = libraryKnown ? countLabel : "";
        ViewLibraryCount.Visibility = Show(libraryKnown);
        ViewRequestsButton.Visibility = Show(Presentation.OffersRequestView(requestsEnabled, hasRequestResults));
        var requestCount = ViewModel.OutsideFirstPageCount > 0 ? Presentation.CountLabel(ViewModel.OutsideFirstPageCount, ViewModel.OutsideTotalPages > 1) : "";
        ViewRequestsCount.Text = requestCount;
        ViewRequestsCount.Visibility = Show(requestCount.Length > 0);
    }

    /// <summary>A pill of the result views: the viewed one on the accent surface, the rest quiet (official aria-pressed buttons).</summary>
    private static void StyleViewTab(Button button, bool active)
    {
        var background = active ? (Brush)Application.Current.Resources["SurfaceRaisedBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var foreground = (Brush)Application.Current.Resources[active ? "PrimaryTextBrush" : "SecondaryTextBrush"];
        var hover = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        button.Background = background; button.Foreground = foreground;
        button.Resources["ButtonBackground"] = background; button.Resources["ButtonBackgroundPointerOver"] = background; button.Resources["ButtonBackgroundPressed"] = background;
        button.Resources["ButtonForeground"] = foreground;
        button.Resources["ButtonForegroundPointerOver"] = active ? foreground : hover; button.Resources["ButtonForegroundPressed"] = active ? foreground : hover;
        button.Resources["ButtonBorderBrush"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Resources["ButtonBorderBrushPointerOver"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent); button.Resources["ButtonBorderBrushPressed"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        // Text colours of the labels follow the pill, since TextBlocks do not inherit the button's resource overrides.
        IEnumerable<TextBlock> labels = button.Content is StackPanel panel ? panel.Children.OfType<TextBlock>() : button.Content is TextBlock single ? [single] : [];
        foreach (var text in labels)
            if (!text.Name.EndsWith("Count", StringComparison.Ordinal)) text.Foreground = foreground;
        AutomationProperties.SetItemStatus(button, active ? "Selected" : "Not selected");
    }

    /// <summary>Request to add: one row between the library's rows, the paged grid when the library has nothing, or the request view.</summary>
    private void UpdateRequestRegion(bool overview, bool requestsView, bool deciding, bool libraryKnown, bool libraryEmpty, bool failed, bool nothingFound, bool requestsEnabled)
    {
        var items = ViewModel.OutsideLibraryResults.Count;
        var pageError = ViewModel.OutsideError != null;
        var page = Math.Max(1, ViewModel.OutsideRequestedPage);
        var totalPages = pageError ? 0 : ViewModel.OutsideTotalPages;
        var loading = ViewModel.IsOutsideLoading;

        // "Not in your library": the first page's titles as a row between the library's.
        var row = requestsEnabled && overview && !libraryEmpty && !failed && page == 1 && !pageError && items > 0;
        RequestOverviewSection.Visibility = Show(row);
        if (row) RebuildRequestRail();

        // The grid: the answer when the library has nothing (or the search failed), and the whole request view.
        var wanted = requestsEnabled && !deciding && !nothingFound && (requestsView || (overview && (libraryEmpty || failed)));
        // A failed first read hides the section; a failed later page keeps it. A first page with nothing to request and no other pages shows nothing.
        var hidden = (pageError && page <= 1) || (!pageError && items == 0 && page <= 1 && !(totalPages > 1));
        RequestResultsSection.Visibility = Show(wanted && !hidden);
        var query = ViewModel.Query.Trim();
        RequestNoLibraryMatchText.Visibility = Show(libraryKnown && ViewModel.Results.Count == 0);
        RequestNoLibraryMatchText.Text = SiloPlayer.Core.Localization.AppStrings.Get("web.requests.toAdd.noLibraryMatch", new Dictionary<string, object?> { ["query"] = query }, fallback: $"Nothing in your library matches “{query}”.");
        RequestResultsRepeater.Visibility = Show(!pageError && items > 0);
        RequestResultsRepeater.Opacity = loading ? 0.6 : 1;
        RequestAllInLibraryText.Visibility = Show(!pageError && items == 0);
        RequestErrorPanel.Visibility = Show(pageError);
        RequestErrorText.Text = ViewModel.OutsideError ?? "";
        RequestRetryButton.IsEnabled = RequestFirstPageButton.IsEnabled = !loading;
        var pager = Presentation.RequestPager(page, totalPages);
        RequestPageText.Text = Presentation.RequestPagerPosition(pager);
        RequestPreviousButton.IsEnabled = !loading && pager.HasPrevious;
        RequestNextButton.IsEnabled = !loading && pager.HasNext;
        RequestPagingPanel.Visibility = Show(pager.HasPrevious || pager.HasNext);
    }

    private void UpdatePeopleSection() => UpdateResultsState();
    private void UpdateRequestResults() => UpdateResultsState();

    private void RebuildOverviewRows()
    {
        var split = Presentation.SplitOverview(ViewModel.Results, _gridColumns, ViewModel.LibraryHasMore);
        SyncRows(_bestResults, split.Best);
        SyncRows(_moreResults, split.More);
    }

    private static void SyncRows(ObservableCollection<MediaItem> target, IReadOnlyList<MediaItem> source)
    {
        if (target.Count == source.Count && target.Zip(source).All(pair => ReferenceEquals(pair.First, pair.Second))) return;
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    /// <summary>The "Not in your library" row: a rail of the first page's titles, rebuilt only when they or the card width change.</summary>
    private void RebuildRequestRail()
    {
        var key = string.Join("|", ViewModel.OutsideLibraryResults.Select(item => item.MediaType + item.TmdbId)) + "@" + _railCardWidth + "/" + ViewModel.OutsideWatchlistTitlesSupported;
        if (key == _railKey) return;
        _railKey = key;
        var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var item in ViewModel.OutsideLibraryResults.ToArray())
            cards.Children.Add(BuildExternalCard(item, _railCardWidth));
        RequestOverviewRailHost.Children.Clear();
        RequestOverviewRailHost.Children.Add(new SiloPlayer.Controls.CarouselRail(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = cards,
        }));
    }

    private void ShowView(SearchView view)
    {
        if (view == _view) return;
        _view = view;
        // Switching views starts the request pager over (official withSearchView).
        if (ViewModel.OutsideRequestedPage != 1 && ViewModel.RequestsEnabled) _ = ViewModel.SetOutsidePageAsync(1);
        UpdateResultsState();
        ResultsScroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void ResultView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
            ShowView(tag switch { "library" => SearchView.Library, "requests" => SearchView.Requests, _ => SearchView.Overview });
    }

    private void ShowAllResults_Click(object sender, RoutedEventArgs e) => ShowView(SearchView.Library);
    private void RequestOverviewExplore_Click(object sender, RoutedEventArgs e) => ShowView(SearchView.Requests);

    private async void BackToRelevance_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SortField = Presentation.RelevanceSort; ViewModel.SortOrder = "desc"; ViewModel.RelevanceChosen = true;
        var previous = _filterInitializing; _filterInitializing = true;
        try { SelectComboTag(ResultSortCombo, Presentation.RelevanceSort); SelectComboTag(ResultOrderCombo, "desc"); }
        finally { _filterInitializing = previous; }
        UpdateResultsState();
        if (!string.IsNullOrWhiteSpace(ViewModel.Query)) await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private void BoxSetLanguageChanged(long _) => DispatcherQueue.TryEnqueue(RefreshBoxSetSearch);

    private void RefreshBoxSetSearch()
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(RefreshBoxSetSearch); return; }
        if (_isNavigated) _ = ViewModel.RefreshBoxSetSearchAsync();
    }

    private async void RefreshBoxSetsAfterPlayback(PlaybackProgressUpdated change)
    {
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(() => RefreshBoxSetsAfterPlayback(change)); return; }
        if (!_isNavigated || (change.Identity is { } identity && !_catalogApi.IsCurrentIdentityContext(identity))) return;
        ViewModel.RecordCatalogMutation(change.Persisted);
        var visit = _navigationGeneration; var owner = _catalogApi.CaptureContext();
        try { await change.Persisted; } catch { return; }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isNavigated && visit == _navigationGeneration && _catalogApi.IsCurrentContext(owner)) RefreshBoxSetSearch();
        });
    }

    private void BoxSetSearch_Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not ContentControl host || args.Index < 0 || args.Index >= ViewModel.BoxSets.Items.Count) return;
        var set = ViewModel.BoxSets.Items[args.Index]; var generation = ViewModel.BoxSets.Generation; var visit = _navigationGeneration;
        var card = new BoxSetCard(); card.SetSummary(set); card.SetCatalogLayout(_catalogCardWidth, _catalogCardWidth * 1.5, (_catalogCardWidth * 1.5) + _uiCustomizationService.CardCaptionHeight);
        card.Invoked += summary =>
        {
            if (_isNavigated && visit == _navigationGeneration && ViewModel.BoxSets.CanOpen(summary, generation))
                App.Services.GetRequiredService<NavigationService>().Navigate(typeof(BoxSetPage), new BoxSetNavigationArgs(summary.Id, summary.LibraryId));
        };
        host.Content = card;
    }

    private async void Scope_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string scope }) return;
        await EnsureInitializedAsync();
        await ViewModel.SetMediaScopeAsync(scope);
        UpdateScopeButtons();
        RefreshOpenSearchFilters();
    }

    private void UpdateScopeButtons()
    {
        UpdateResultSortChoices();
        foreach (var button in new[] { EmptyVideoScope, EmptyAudiobookScope, EmptyAllScope, ResultsVideoScope, ResultsAudiobookScope, ResultsAllScope })
        {
            var active = string.Equals(button.Tag?.ToString(), ViewModel.MediaScope, StringComparison.Ordinal);
            // SearchScopeChips in the WebUI is a rounded radiogroup rather
            // than three independent outline buttons. Keep the empty-search
            // controls compatible, but paint the visible results chips as a
            // single pill group and expose their selected state to assistive
            // technology.
            if (ReferenceEquals(button, ResultsVideoScope)
                || ReferenceEquals(button, ResultsAudiobookScope)
                || ReferenceEquals(button, ResultsAllScope))
            {
                button.Style = null;
                var background = active
                    ? (Brush)Application.Current.Resources["AccentBrush"]
                    : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                var foreground = (Brush)Application.Current.Resources[
                    active ? "AccentForegroundBrush" : "SecondaryTextBrush"];
                // Upstream only transitions the label color for an inactive
                // chip; it does not paint a second raised pill on hover.
                // Keep the selected accent fill equally stable on hover.
                var pointerBackground = background;
                var pointerForeground = (Brush)Application.Current.Resources[
                    active ? "AccentForegroundBrush" : "PrimaryTextBrush"];
                button.Background = background;
                button.Foreground = foreground;
                button.Resources["ButtonBackground"] = background;
                button.Resources["ButtonBackgroundPointerOver"] = pointerBackground;
                button.Resources["ButtonBackgroundPressed"] = pointerBackground;
                button.Resources["ButtonForeground"] = foreground;
                button.Resources["ButtonForegroundPointerOver"] = pointerForeground;
                button.Resources["ButtonForegroundPressed"] = pointerForeground;
                button.BorderThickness = new Thickness(0);
                button.Height = 32;
                button.MinHeight = 0;
                button.CornerRadius = new CornerRadius(16);
                button.Padding = new Thickness(16, 0, 16, 0);
                AutomationProperties.SetHelpText(
                    button,
                    active ? "Selected search scope" : "Select search scope");
                AutomationProperties.SetItemStatus(
                    button,
                    active ? "Selected" : "Not selected");
            }
            else
            {
                button.Style = (Style)Application.Current.Resources[
                    active ? "AccentButtonStyle" : "OutlineButtonStyle"];
            }
        }
        var placeholder = ViewModel.MediaScope == "audiobook" ? "Search audiobooks..." : ViewModel.MediaScope == "all" ? "Search all media..." : "Search movies, series...";
        SearchBox.PlaceholderText = placeholder;
        _filterInitializing = true;
        var typeSelection = ViewModel.MediaType
            ?? (ViewModel.MediaScope is "video" or "audiobook" ? ViewModel.MediaScope : "all");
        SelectComboTag(ResultTypeCombo, typeSelection);
        _filterInitializing = false;
    }

    private bool _requestProfileAllowed;
    private SiloPlayer.Core.Api.ApiIdentityContext _requestIdentity;
    private void RequestAuthorityChanged() => DispatcherQueue.TryEnqueue(async () =>
    {
        var api = App.Services.GetRequiredService<SiloPlayer.Core.Api.RequestsApi>();
        var identity = api.CaptureIdentityContext();
        if (!_isNavigated || identity.AuthenticationGeneration != _requestIdentity.AuthenticationGeneration ||
            identity.BaseUrl != _requestIdentity.BaseUrl || identity.ProfileId != _requestIdentity.ProfileId) return;
        var allowed = api.ProfileRequestsAllowed;
        if (allowed == _requestProfileAllowed) return;
        _requestIdentity = identity;
        _requestProfileAllowed = allowed;
        if (allowed) await ViewModel.SetOutsidePageAsync(1);
        else ViewModel.RetireOutsideDiscovery();
        UpdateRequestResults();
    });

    private async void RetrySearch_Click(object sender, RoutedEventArgs e) => await ViewModel.SearchCommand.ExecuteAsync(null);
    private async void RetryPeople_Click(object sender, RoutedEventArgs e) => await ViewModel.RetryPeopleAsync();

    private void ExternalRequestCard_Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not ContentControl host || args.Index < 0 || args.Index >= ViewModel.OutsideLibraryResults.Count) return;
        var item = ViewModel.OutsideLibraryResults[args.Index];
        host.Tag = item;
        BuildExternalRequestCard(host, item);
    }

    private void BuildExternalRequestCard(ContentControl host, RequestMediaResult item)
        => host.Content = BuildExternalCard(item, _catalogCardWidth);

    private Grid BuildExternalCard(RequestMediaResult item, double width)
        => SiloPlayer.Controls.ExternalTitleCard.Build(item, width, discoveryOwner: ViewModel.OutsideDiscoveryOwner,
            watchlist: ViewModel.OutsideWatchlistTitlesSupported ? async () =>
            {
                await ViewModel.ToggleOutsideWatchlistAsync(item);
                App.Services.GetService<WatchlistViewModel>()?.InvalidateExternalTitles();
            } : null);

    // Paging the request grid keeps the viewer's place: the new page's first row comes into view when the section starts above it.
    private async Task ChangeRequestPageAsync(int page)
    {
        await ViewModel.SetOutsidePageAsync(page);
        UpdateRequestResults();
        if (RequestResultsSection.Visibility == Visibility.Visible &&
            RequestResultsSection.TransformToVisual(ResultsScroll).TransformPoint(new Windows.Foundation.Point()).Y < 0)
            RequestResultsSection.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 0 });
    }

    private async void RequestPrevious_Click(object sender, RoutedEventArgs e) => await ChangeRequestPageAsync(ViewModel.OutsideRequestedPage - 1);
    private async void RequestNext_Click(object sender, RoutedEventArgs e) => await ChangeRequestPageAsync(ViewModel.OutsideRequestedPage + 1);
    private async void RequestRetry_Click(object sender, RoutedEventArgs e) => await ChangeRequestPageAsync(ViewModel.OutsideRequestedPage);
    private async void RequestFirstPage_Click(object sender, RoutedEventArgs e) => await ChangeRequestPageAsync(1);

    // The official CastCarousel: a portrait photo (initials without one) over the name.
    private void PersonCard_Prepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not ContentControl host || args.Index < 0 || args.Index >= ViewModel.PeopleResults.Count) return;
        host.Content = BuildPersonCard(ViewModel.PeopleResults[args.Index]);
    }

    private Button BuildPersonCard(Person person)
    {
        var photo = new Border { Width = 110, Height = 165, CornerRadius = new CornerRadius(8), Background = (Brush)Application.Current.Resources["CardBackgroundBrush"], Margin = new Thickness(0, 0, 0, 10) };
        if (!string.IsNullOrEmpty(person.PhotoUrl))
            _ = LoadPersonPhotoAsync(photo, person);
        else
        {
            var initials = string.Join("", person.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));
            photo.Child = new TextBlock { Text = initials.Length > 0 ? initials : "?", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        }
        var card = new StackPanel { Width = 110 };
        card.Children.Add(photo);
        card.Children.Add(new TextBlock { Text = person.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium, MaxLines = 1, TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 2, 0), Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] });
        var button = new Button { Content = card, Tag = person.Id, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0), CornerRadius = new CornerRadius(8) };
        AutomationProperties.SetName(button, person.Name);
        button.Click += PersonCard_Click;
        return button;
    }

    private async Task LoadPersonPhotoAsync(Border photo, Person person)
    {
        var cts = _peoplePhotosCts ??= new CancellationTokenSource();
        try
        {
            var bytes = await App.Services.GetRequiredService<SiloPlayer.Core.Services.ImageService>().GetImageAsync(
                person.Name, "cast_photo", person.PhotoUrl!, App.Services.GetRequiredService<HttpClient>(), cts.Token);
            if (bytes == null || cts.IsCancellationRequested || !_isNavigated) return;
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 330 };
            using var stream = new MemoryStream(bytes);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            if (cts.IsCancellationRequested) return;
            photo.Child = new Image { FlowDirection = FlowDirection.LeftToRight, Source = bitmap, Stretch = Stretch.UniformToFill };
        }
        catch { }
    }

    /// <summary>The chrome the XAML cannot declare: link content, icons and the translucent card of the request row.</summary>
    private void BuildResultChrome()
    {
        StackPanel Link(out TextBlock label)
        {
            label = new TextBlock { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(label);
            content.Children.Add(SiloPlayer.Controls.WebUiIcon.Create("arrow-right", 16, (Brush)Application.Current.Resources["SecondaryTextBrush"]));
            return content;
        }
        FirstShowAllButton.Content = Link(out _firstShowAllLabel);
        MoreShowAllLink.Content = Link(out _moreShowAllLabel);
        var pill = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _showAllPillLabel = new TextBlock { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium };
        _showAllPillCount = new TextBlock { FontSize = 14, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };
        pill.Children.Add(_showAllPillLabel); pill.Children.Add(_showAllPillCount);
        ShowAllResultsButton.Content = pill;
        RequestRetryIcon.Content = SiloPlayer.Controls.WebUiIcon.Create("refresh-cw", 16);
        RetryPeopleIcon.Content = SiloPlayer.Controls.WebUiIcon.Create("refresh-cw", 16);
        RetrySearchIcon.Content = SiloPlayer.Controls.WebUiIcon.Create("refresh-cw", 16);
        var card = (SolidColorBrush)Application.Current.Resources["CardBackgroundBrush"];
        var border = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
        RequestOverviewSection.Background = new SolidColorBrush(card.Color) { Opacity = 0.6 };
        RequestOverviewSection.BorderBrush = new SolidColorBrush(border.Color) { Opacity = 0.6 };
        SiloPlayer.Controls.MediaItemCardActions.BindText(RequestOverviewExploreButton, "Name", element => AutomationProperties.SetName(element, SiloPlayer.Core.Localization.AppStrings.Get("web.catalog.carousel.exploreAllLabel",
            new Dictionary<string, object?> { ["title"] = SiloPlayer.Core.Localization.AppStrings.Get("web.search.results.notInLibrary", fallback: "Not in your library") }, fallback: "Explore all")));
    }

    private DispatcherTimer? _searchDebounce;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        ViewModel.Query = textBox.Text;

        if (string.IsNullOrWhiteSpace(ViewModel.Query))
        {
            _searchDebounce?.Stop();
            ViewModel.CancelPendingSearch();
            ViewModel.Results.Clear();
            ViewModel.PeopleResults.Clear();
            ViewModel.OutsideLibraryResults.Clear();
            EmptyState.Visibility = Visibility.Visible;
            ResultsState.Visibility = Visibility.Collapsed;
            PositionSearchSurface();
            _resultsScrollUserScrolled = false;
            _view = SearchView.Overview;
            RestoreSearchFocus(textBox.FocusState);
            return;
        }

        // The same native TextBox is moved between the empty and results
        // hosts. Preserve its focus/caret immediately; there is no mirrored
        // control whose TextChanged event can steal focus or duplicate text.
        var focusState = textBox.FocusState;
        ShowResultsShellForCurrentQuery();
        RestoreSearchFocus(focusState);

        // Match the current WebUI's 100ms live-search navigation debounce.
        _searchDebounce?.Stop();
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce?.Stop();

            var querySnapshot = ViewModel.Query.Trim();
            if (!_isNavigated || !string.Equals(querySnapshot, ViewModel.Query.Trim(), StringComparison.Ordinal))
                return;

            _resultsScrollUserScrolled = false;
            // A new search starts on the overview (official buildCatalogQueryUpdateHref drops the view).
            _view = SearchView.Overview;
            ResultsScroll.ChangeView(null, 0, null, disableAnimation: true);

            ShowResultsShellForCurrentQuery();

            await ViewModel.SearchCommand.ExecuteAsync(null);
            RefreshOpenSearchFilters();
        };
        _searchDebounce.Start();
    }

    private void ShowResultsShellForCurrentQuery()
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ResultsState.Visibility = Visibility.Visible;
        PositionSearchSurface();
        SiloPlayer.Controls.MediaItemCardActions.BindText(ResultsTitle, "Text", element => element.Text = SiloPlayer.Core.Localization.AppStrings.Get("web.catalog.page.resultsFor", new Dictionary<string, object?> { ["query"] = ViewModel.Query }, fallback: $"Results for \"{ViewModel.Query}\""));
    }

    private void PositionSearchSurface()
    {
        if (!IsLoaded)
            return;

        // Keep the TextBox in one visual parent. Reparenting a live WinUI
        // element can fail inside Frame.Navigate with 0x800F1000.
        ApplySearchSurfaceStyle(hero: !string.IsNullOrWhiteSpace(ViewModel.Query));
        SearchRoot.UpdateLayout();
        var target = string.IsNullOrWhiteSpace(ViewModel.Query)
            ? EmptySearchHost
            : ResultsSearchHost;
        var point = target.TransformToVisual(SearchRoot).TransformPoint(new Windows.Foundation.Point());
        SearchSurface.Margin = new Thickness(point.X, point.Y, 0, 0);
    }

    private void RestoreSearchFocus(FocusState previousFocusState)
    {
        if (previousFocusState == FocusState.Unfocused)
            return;

        SearchBox.Focus(previousFocusState == FocusState.Keyboard
            ? FocusState.Keyboard
            : FocusState.Programmatic);
        SearchBox.SelectionStart = SearchBox.Text.Length;
        SearchBox.SelectionLength = 0;
    }

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Official #4287: the Enter that confirms an IME conversion is not a search.
        if (ImeCompositionGuard.Attach(SearchBox).ShouldIgnore(e) ||
            e.Key != Windows.System.VirtualKey.Enter ||
            string.IsNullOrWhiteSpace(SearchBox.Text))
            return;

        e.Handled = true;
        _searchDebounce?.Stop();
        _view = SearchView.Overview;
        ShowResultsShellForCurrentQuery();
        await ViewModel.SearchCommand.ExecuteAsync(null);
        RefreshOpenSearchFilters();
    }

    private void PersonCard_Click(object sender, RoutedEventArgs e)
    {
        // B33: Person.Id is a string end-to-end now.
        if (sender is Button btn && btn.Tag is string personId && !string.IsNullOrEmpty(personId))
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            nav.Navigate<PersonDetailPage>(personId);
        }
    }

    private async void ResultsScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        SearchScrollToTopButton.Visibility = ResultsScroll.VerticalOffset > 720
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (ResultsScroll.VerticalOffset > 24)
            _resultsScrollUserScrolled = true;

        // Avoid firing an automatic "load more" while the initial result grid
        // is settling. On wide displays the first layout pass can report
        // "near bottom" before the user has moved, which looks like a random
        // late page refresh even though the query/results are unchanged.
        // Only the full library list windows through the pages as it scrolls; the overview reads the first page.
        if (_shownView != SearchView.Library ||
            !_resultsScrollUserScrolled ||
            ResultsScroll.ScrollableHeight <= 0 ||
            ViewModel.IsLoading ||
            ViewModel.Results.Count == 0)
        {
            return;
        }

        if (ResultsScroll.ScrollableHeight - ResultsScroll.VerticalOffset < 900)
            await ViewModel.LoadMoreAsync();
    }

    private void SearchScrollToTop_Click(object sender, RoutedEventArgs e)
    {
        ResultsScroll.ChangeView(null, 0, null);
        SearchScrollToTopButton.Visibility = Visibility.Collapsed;
        SearchBox.Focus(FocusState.Programmatic);
    }

    private void SearchPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0) ResultFiltersSheet.PreferredWidth = Math.Min(e.NewSize.Width * .75, e.NewSize.Width >= 640 ? 448 : double.PositiveInfinity);
        _pageWidth = e.NewSize.Width;
        var gutter = Gutter(e.NewSize.Width);
        var emptyTitleSize = Math.Clamp(e.NewSize.Width * 0.04, 32, 60);
        EmptySearchTitle.FontSize = emptyTitleSize;
        EmptySearchTitle.LineHeight = emptyTitleSize * 0.95;
        // The results page takes the full width, with no page-shell cap; the page's own padding
        // is px-4 sm:px-6 lg:px-10 xl:px-12 with py-4 sm:py-6.
        ResultsSearchHost.Margin = new Thickness(gutter, e.NewSize.Width < 640 ? 16 : 24, gutter, 16);
        ResultsToolbar.Margin = new Thickness(gutter, 0, gutter, 16);
        ActiveResultFiltersPanel.Margin = new Thickness(gutter, 0, gutter, 16);
        ResultViewsPanel.Margin = new Thickness(gutter, 0, gutter, 0);
        ResultsScroll.Padding = new Thickness(gutter, 0, gutter, e.NewSize.Width < 640 ? 16 : 24);
        // Section headings are text-lg sm:text-xl.
        var headingSize = e.NewSize.Width >= 640 ? 20d : 18d;
        foreach (var heading in new[] { FirstRowsTitle, MoreResultsTitle, PeopleTitle, LibraryViewTitle })
            heading.FontSize = headingSize;
        var searchWidth = Math.Max(280, Math.Min(576, e.NewSize.Width - (gutter * 2)));
        EmptySearchHost.Width = searchWidth;
        PositionSearchSurface();
        UpdateCatalogGridLayout(e.NewSize.Width, gutter);
    }

    private static double Gutter(double width) => width < 640 ? 16 : width < 1024 ? 24 : width < 1280 ? 40 : 48;

    /// <summary>
    /// The one persistent input is the empty page's 576px field, and on the results page the official "hero" bar: the
    /// query itself, large, on a hairline (display type 30/36/48px, full width, no box).
    /// </summary>
    private void ApplySearchSurfaceStyle(bool hero)
    {
        var width = _pageWidth;
        var gutter = Gutter(width);
        if (hero)
        {
            var font = width >= 1024 ? 48d : width >= 640 ? 36d : 30d;
            var height = Math.Ceiling(font * 1.25) + 24 + 1;
            SearchSurface.Background = null;
            SearchSurface.CornerRadius = new CornerRadius(0);
            SearchSurface.BorderThickness = new Thickness(0, 0, 0, 1);
            SearchSurface.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
            SearchSurface.MaxWidth = double.PositiveInfinity;
            SearchSurface.Width = Math.Max(280, width - (gutter * 2));
            SearchSurface.Height = ResultsSearchHost.Height = height;
            SearchSurfaceIcon.FontSize = width >= 640 ? 28 : 24;
            SearchSurfaceIcon.Margin = new Thickness(0);
            SearchBox.Height = height - 1;
            SearchBox.FontSize = font;
            SearchBox.FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold;
            SearchBox.CharacterSpacing = -40;
            SearchBox.Padding = new Thickness(width >= 640 ? 48 : 40, 12, 48, 12);
            SearchBox.CornerRadius = new CornerRadius(0);
            return;
        }
        SearchSurface.Background = (Brush)Application.Current.Resources["SurfaceBrush"];
        SearchSurface.CornerRadius = new CornerRadius(22);
        SearchSurface.BorderThickness = new Thickness(0);
        SearchSurface.MaxWidth = 576;
        SearchSurface.Width = Math.Max(280, Math.Min(576, width - (gutter * 2)));
        SearchSurface.Height = 56;
        SearchSurfaceIcon.FontSize = 20;
        SearchSurfaceIcon.Margin = new Thickness(16, 0, 0, 0);
        SearchBox.Height = 56;
        SearchBox.FontSize = 16;
        SearchBox.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        SearchBox.CharacterSpacing = 0;
        SearchBox.Padding = new Thickness(48, 16, 12, 16);
        SearchBox.CornerRadius = new CornerRadius(22);
    }

    private void UpdateCatalogGridLayout(double viewportWidth, double gutter)
    {
        var contentWidth = Math.Max(320, viewportWidth - (gutter * 2));
        var columns = _uiCustomizationService.GetPosterColumnCount(contentWidth);
        _gridColumns = columns;
        var gap = _uiCustomizationService.CardPresentation.PosterSize == "large" ? 16d : 12d;
        foreach (var layout in new[] { ResultsGridLayout, BestGridLayout, MoreGridLayout, BoxSetSearchGridLayout, SearchLoadingGridLayout, RequestGridLayout })
            layout.MinColumnSpacing = layout.MinRowSpacing = gap;
        _catalogCardWidth = Math.Max(96, (contentWidth - (gap * (columns - 1))) / columns);
        _railCardWidth = viewportWidth >= 1024 ? 178 : viewportWidth >= 640 ? 150 : 130;
        var cardHeight = (_catalogCardWidth * 1.5) + _uiCustomizationService.CardCaptionHeight;
        foreach (var layout in new[] { ResultsGridLayout, BestGridLayout, MoreGridLayout, BoxSetSearchGridLayout, SearchLoadingGridLayout })
        {
            layout.MaximumRowsOrColumns = columns;
            layout.MinItemWidth = _catalogCardWidth;
            layout.MinItemHeight = cardHeight;
        }
        // The request grid is the library's poster grid, so a requestable title lines up with the titles above it.
        RequestGridLayout.MaximumRowsOrColumns = columns;
        RequestGridLayout.MinItemWidth = _catalogCardWidth;
        for (var index = 0; index < ViewModel.BoxSets.Items.Count; index++)
            if (BoxSetSearchRepeater.TryGetElement(index) is ContentControl { Content: BoxSetCard card }) card.SetCatalogLayout(_catalogCardWidth, _catalogCardWidth * 1.5, (_catalogCardWidth * 1.5) + _uiCustomizationService.CardCaptionHeight);
        foreach (var repeater in new[] { ResultsRepeater, BestRepeater, MoreRepeater })
            for (var index = 0; index < ((repeater.ItemsSource as System.Collections.ICollection)?.Count ?? 0); index++)
                if (repeater.TryGetElement(index) is SiloPlayer.Controls.PosterCard card)
                    card.SetCatalogGridLayout(_catalogCardWidth);
        for (var index = 0; index < SkeletonCount; index++)
            if (SearchLoadingRepeater.TryGetElement(index) is StackPanel skeleton)
                SetSearchSkeletonLayout(skeleton);
        for (var index = 0; index < ViewModel.OutsideLibraryResults.Count; index++)
            if (RequestResultsRepeater.TryGetElement(index) is ContentControl host && host.Tag is RequestMediaResult item)
                BuildExternalRequestCard(host, item);
        _railKey = null;
        // The overview's rows are whole rows of the new column count.
        if (ResultsState.Visibility == Visibility.Visible) UpdateResultsState();
    }

    private void ResultsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is SiloPlayer.Controls.PosterCard card)
            card.SetCatalogGridLayout(_catalogCardWidth);
    }

    private void SearchLoadingRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is StackPanel skeleton)
            SetSearchSkeletonLayout(skeleton);
    }

    private void SetSearchSkeletonLayout(StackPanel skeleton)
    {
        skeleton.Width = _catalogCardWidth;
        if (skeleton.Children.Count > 0 && skeleton.Children[0] is Border poster)
        {
            poster.Width = _catalogCardWidth;
            poster.Height = _catalogCardWidth * 1.5;
        }
        if (skeleton.Children.Count > 1 && skeleton.Children[1] is Border title)
            title.Width = _catalogCardWidth * 0.74;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _navigationGeneration++;
        _isNavigated = false;
        App.Services.GetRequiredService<SiloPlayer.Core.Services.AuthService>().ProfileChanged -= RequestAuthorityChanged;
        _uiCustomizationService.Changed -= UICustomization_Changed;
        _catalogApi.LibraryCacheInvalidated -= RefreshBoxSetSearch;
        _localizedReads.Invalidated -= BoxSetLanguageChanged;
        _searchDebounce?.Stop();
        _peoplePhotosCts?.Cancel(); _peoplePhotosCts = null;
        ViewModel.CancelPendingSearch();
        base.OnNavigatedFrom(e);
    }

    private void UICustomization_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isNavigated) return;
            var width = Math.Max(320, ActualWidth);
            UpdateCatalogGridLayout(width, Gutter(width));
        });

    private Task EnsureSearchFiltersLoadedAsync()
    {
        var query = ViewModel.Query.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return Task.CompletedTask;

        var mediaType = ViewModel.MediaType;
        var key = $"{_catalogApi.CaptureContext()}\u001F{_catalogApi.FilterCacheGeneration}\u001F{mediaType ?? ""}";
        if (string.Equals(_searchFiltersTaskKey, key, StringComparison.Ordinal) &&
            _searchFiltersTask is { IsCompleted: false })
            return _searchFiltersTask;

        _searchFiltersTaskKey = key;
        _searchFiltersTask = LoadSearchFiltersCoreAsync(key, query, mediaType);
        return _searchFiltersTask;
    }

    private void RefreshOpenSearchFilters()
    {
        // The current WebUI requests query facets only while the filter sheet
        // is actually open. A normal keystroke search must not start a second,
        // potentially expensive catalog aggregation beside the visible query.
        if (ResultFiltersSheet.IsOpen)
            _ = EnsureSearchFiltersLoadedAsync();
    }

    private async Task LoadSearchFiltersCoreAsync(string key, string query, string? mediaType)
    {
        await ViewModel.LoadFiltersAsync(query, mediaType);
        if (!_isNavigated ||
            !string.Equals(_searchFiltersTaskKey, key, StringComparison.Ordinal) ||
            !string.Equals(ViewModel.MediaType, mediaType, StringComparison.Ordinal))
        {
            return;
        }

        PopulateResultFilters();
    }

    private void PopulateResultFilters()
    {
        var priorInitializing = _filterInitializing;
        _filterInitializing = true;
        try
        {
            FillResultCombo(ResultGenreCombo, "All Genres", ViewModel.AvailableFilters?.Genres ?? []);
            FillResultCombo(ResultRatingCombo, "All Ratings", ViewModel.AvailableFilters?.ContentRatings ?? []);
            FillResultCombo(ResultResolutionCombo, "All Resolutions", ViewModel.AvailableFilters?.Resolutions ?? []);
            FillResultCombo(ResultCountryCombo, "All Countries", ViewModel.AvailableFilters?.Countries ?? []);
            if (!string.IsNullOrWhiteSpace(ViewModel.Genre))
                SelectComboTag(ResultGenreCombo, ViewModel.Genre);
            if (!string.IsNullOrWhiteSpace(ViewModel.ContentRating))
                SelectComboTag(ResultRatingCombo, ViewModel.ContentRating);
            if (!string.IsNullOrWhiteSpace(ViewModel.Resolution))
                SelectComboTag(ResultResolutionCombo, ViewModel.Resolution);
            if (!string.IsNullOrWhiteSpace(ViewModel.Country))
                SelectComboTag(ResultCountryCombo, ViewModel.Country);
        }
        finally
        {
            _filterInitializing = priorInitializing;
        }
        SearchQueryFilters.Load(ViewModel.AdvancedQuery, ViewModel.MediaScope, filters: ViewModel.AvailableFilters);
        UpdateActiveResultFilters();
    }

    private static void FillResultCombo(ComboBox combo, string allLabel, IEnumerable<string> values)
    {
        // Plain options let the native popup virtualize containers instead of
        // constructing every facet's XAML item during a filter-sheet open.
        combo.ItemsSource = new[] { new SearchFilterOption(allLabel, "") }
            .Concat(values.Distinct(StringComparer.OrdinalIgnoreCase).Select(value => new SearchFilterOption(value, value))).ToArray();
        combo.DisplayMemberPath = nameof(SearchFilterOption.Label);
        combo.SelectedIndex = 0;
    }

    private async void ResultFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filterInitializing || string.IsNullOrWhiteSpace(ViewModel.Query)) return;
        if (ReferenceEquals(sender, ResultTypeCombo))
        {
            UpdateResultSortChoices(SelectedTag(ResultTypeCombo));
            await ViewModel.SetMediaTypeAsync(SelectedTag(ResultTypeCombo));
            UpdateScopeButtons();
            RefreshOpenSearchFilters();
            return;
        }

        ViewModel.SortOrder = SelectedTag(ResultOrderCombo) ?? "desc";
        ViewModel.Genre = SelectedTag(ResultGenreCombo);
        ViewModel.ContentRating = SelectedTag(ResultRatingCombo);
        ViewModel.Resolution = SelectedTag(ResultResolutionCombo);
        ViewModel.Country = SelectedTag(ResultCountryCombo);
        UpdateActiveResultFilters();
        await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private void UpdateResultSortChoices(string? scope = null)
    {
        var initializing = _filterInitializing;
        _filterInitializing = true;
        try
        {
            ViewModel.SortField = CatalogSortChoices.Apply(ResultSortCombo, _resultSortChoices,
                _shownRatingSources, null, ViewModel.SortField);
            // Relevance has no ascending or descending of its own.
            ResultOrderCombo.Visibility = ViewModel.SortsByRelevance ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _filterInitializing = initializing; }
    }

    private async Task RefreshRatingSortChoicesAsync(long navigationGeneration)
    {
        var context = _catalogApi.CaptureContext();
        var shown = await CatalogSortChoices.LoadShownSourcesAsync(_catalogApi);
        if (!_isNavigated || navigationGeneration != _navigationGeneration || context != _catalogApi.CaptureContext()) return;
        _shownRatingSources = shown;
        UpdateResultSortChoices();
    }

    private async void ResultSort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filterInitializing) return;
        ViewModel.SortField = SelectedTag(ResultSortCombo) ?? Presentation.RelevanceSort;
        // Picking Relevance sends sort=relevance; leaving it is a choice even when it lands on Date Added (official explicit_sort).
        ViewModel.RelevanceChosen = ViewModel.SortsByRelevance;
        var ascending = ViewModel.SortField is "title" or "content_rating" or "author" or "narrator" or "series";
        _filterInitializing = true;
        SelectComboTag(ResultOrderCombo, ascending ? "asc" : "desc");
        _filterInitializing = false;
        ViewModel.SortOrder = ViewModel.SortsByRelevance ? "desc" : (SelectedTag(ResultOrderCombo) ?? "desc");
        ResultOrderCombo.Visibility = ViewModel.SortsByRelevance ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(ViewModel.Query)) await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private async void OpenResultFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchQueryFilters.ConfigureSort();
        if (ViewModel.AdvancedQuery.Groups.Count == 0)
        {
            var group = new SiloPlayer.Core.Models.Collections.QueryGroup();
            foreach (var (field, value) in new[] { ("genre", ViewModel.Genre), ("content_rating", ViewModel.ContentRating), ("resolution", ViewModel.Resolution), ("country", ViewModel.Country) })
                if (!string.IsNullOrWhiteSpace(value)) group.Rules.Add(new() { Field = field, Op = "is", Value = value });
            ViewModel.AdvancedQuery.Groups.Add(group);
            ViewModel.Genre = ViewModel.ContentRating = ViewModel.Resolution = ViewModel.Country = null;
        }
        SearchQueryFilters.Load(ViewModel.AdvancedQuery, ViewModel.MediaScope, filters: ViewModel.AvailableFilters);
        ResultFiltersSheet.PreferredWidth = Math.Min(ActualWidth * .75, ActualWidth >= 640 ? 448 : double.PositiveInfinity);
        ResultFiltersSheet.IsOpen = true;
        await EnsureSearchFiltersLoadedAsync();
    }
    private async void CloseResultFilters_Click(object sender, RoutedEventArgs e)
    {
        if (!SearchQueryFilters.IsValid) return;
        UpdateActiveResultFilters(); await ViewModel.SearchCommand.ExecuteAsync(null);
        ResultFiltersSheet.IsOpen = false;
    }

    private void AdvancedSearch_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.AdvancedQuery.Groups.Count == 0)
        {
            var group = new SiloPlayer.Core.Models.Collections.QueryGroup();
            foreach (var (field, value) in new[] { ("genre", ViewModel.Genre), ("content_rating", ViewModel.ContentRating), ("resolution", ViewModel.Resolution), ("country", ViewModel.Country) })
                if (!string.IsNullOrWhiteSpace(value)) group.Rules.Add(new() { Field = field, Op = "is", Value = value });
            ViewModel.AdvancedQuery.Groups.Add(group);
            ViewModel.Genre = ViewModel.ContentRating = ViewModel.Resolution = ViewModel.Country = null;
        }
        SearchRulesEditor.Load(ViewModel.AdvancedQuery);
        AdvancedSearchHost.Visibility = Visibility.Visible;
        GuidedSearchFilters.Visibility = AdvancedSearchButton.Visibility = Visibility.Collapsed;
        UpdateActiveResultFilters();
    }

    private async void ApplyAdvancedSearch_Click(object sender, RoutedEventArgs e)
    {
        if (!SearchRulesEditor.IsValid) return;
        UpdateActiveResultFilters(); await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private void UpdateActiveResultFilters()
    {
        ActiveResultFiltersPanel.Children.Clear();
        AddActiveResultFilter("genre", ViewModel.Genre, value => SiloPlayer.Core.Localization.AppStrings.Get("web.catalog.filters.badgeGenre", new Dictionary<string, object?> { ["genre"] = value }));
        AddActiveResultFilter("rating", ViewModel.ContentRating, value => SiloPlayer.Core.Localization.AppStrings.Get("web.catalog.filters.badgeContentRating", new Dictionary<string, object?> { ["rating"] = value }));
        AddActiveResultFilter("resolution", ViewModel.Resolution, value => $"Resolution: {value}");
        AddActiveResultFilter("country", ViewModel.Country, value => SiloPlayer.Core.Localization.AppStrings.Get("web.catalog.filters.badgeCountry", new Dictionary<string, object?> { ["country"] = value }));
        foreach (var badge in SiloPlayer.Core.Services.CatalogFilterBadges.Create(ViewModel.AdvancedQuery, ViewModel.MediaScope))
        {
            var chip = CatalogFilterBadgeView.Build(badge, ViewModel.MediaScope, async () => { badge.Remove(); SearchQueryFilters.Load(ViewModel.AdvancedQuery, ViewModel.MediaScope, filters: ViewModel.AvailableFilters); UpdateActiveResultFilters(); await ViewModel.SearchCommand.ExecuteAsync(null); });
            ActiveResultFiltersPanel.Children.Add(chip);
        }

        var count = new[] { ViewModel.Genre, ViewModel.ContentRating, ViewModel.Resolution, ViewModel.Country }.Count(value => !string.IsNullOrWhiteSpace(value))
            + SiloPlayer.Core.Services.CatalogFilterBadges.ActiveCount(ViewModel.AdvancedQuery, ViewModel.MediaScope);
        ActiveResultFiltersPanel.Visibility = ActiveResultFiltersPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResultFiltersButtonCountBadge.Visibility = count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResultFiltersButtonCount.Text = count.ToString();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            ResultFiltersButton,
            count > 0 ? $"Filters, {count} active" : "Filters");
    }

    private void AddActiveResultFilter(string key, string? value, Func<string, string> label)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var text = label(value);
        var chip = CatalogFilterBadgeView.Build(text, async () => await RemoveActiveResultFilterAsync(key));
        ActiveResultFiltersPanel.Children.Add(chip);
    }

    private async Task RemoveActiveResultFilterAsync(string key)
    {
        _filterInitializing = true;
        try
        {
            switch (key)
            {
                case "advanced":
                    ViewModel.AdvancedQuery.Groups.Clear(); ViewModel.AdvancedQuery.Match = "all";
                    AdvancedSearchHost.Visibility = Visibility.Collapsed;
                    GuidedSearchFilters.Visibility = AdvancedSearchButton.Visibility = Visibility.Visible;
                    break;
                case "genre":
                    ResultGenreCombo.SelectedIndex = 0;
                    ViewModel.Genre = null;
                    break;
                case "rating":
                    ResultRatingCombo.SelectedIndex = 0;
                    ViewModel.ContentRating = null;
                    break;
                case "resolution":
                    ResultResolutionCombo.SelectedIndex = 0;
                    ViewModel.Resolution = null;
                    break;
                case "country":
                    ResultCountryCombo.SelectedIndex = 0;
                    ViewModel.Country = null;
                    break;
            }
        }
        finally
        {
            _filterInitializing = false;
        }

        UpdateActiveResultFilters();
        if (!string.IsNullOrWhiteSpace(ViewModel.Query))
            await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private async void ClearResultFilters_Click(object sender, RoutedEventArgs e)
    {
        _filterInitializing = true;
        ResultGenreCombo.SelectedIndex = 0;
        ResultRatingCombo.SelectedIndex = 0;
        ResultResolutionCombo.SelectedIndex = 0;
        ResultCountryCombo.SelectedIndex = 0;
        _filterInitializing = false;
        ViewModel.Genre = null;
        ViewModel.ContentRating = null;
        ViewModel.Resolution = null;
        ViewModel.Country = null;
        ViewModel.AdvancedQuery.Groups.Clear(); ViewModel.AdvancedQuery.Match = "all";
        AdvancedSearchHost.Visibility = Visibility.Collapsed;
        GuidedSearchFilters.Visibility = AdvancedSearchButton.Visibility = Visibility.Visible;
        UpdateActiveResultFilters();
        if (!string.IsNullOrWhiteSpace(ViewModel.Query)) await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private static string? SelectedTag(ComboBox combo)
    {
        var value = combo.SelectedItem is SearchFilterOption option ? option.Value : (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void SelectComboTag(ComboBox combo, string value)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            var optionValue = combo.Items[index] is SearchFilterOption option ? option.Value : (combo.Items[index] as ComboBoxItem)?.Tag?.ToString();
            if (string.Equals(optionValue, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = index;
                return;
            }
        }
    }

    private sealed record SearchFilterOption(string Label, string Value);

    private void RefreshInlineChoices()
    {
        var previous = _filterInitializing;
        _filterInitializing = true;
        try
        {
            SiloPlayer.Services.Localized.RefreshItems(ResultTypeCombo);
            SiloPlayer.Services.Localized.RefreshItems(ResultSortCombo);
            SiloPlayer.Services.Localized.RefreshItems(ResultOrderCombo);
        }
        finally { _filterInitializing = previous; }
    }
}
