using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed record SearchNavigation(string Query, string? MediaScope = null);

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
    private readonly SiloPlayer.Core.Api.CatalogApi _catalogApi;
    private readonly CatalogSortChoices.Choice[] _resultSortChoices;
    private IReadOnlySet<string> _shownRatingSources = new HashSet<string>();

    public SearchPage()
    {
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        this.InitializeComponent();
        CatalogToolbarChoices.Apply(ResultTypeCombo, ResultSortCombo, ResultOrderCombo);
        _catalogApi = App.Services.GetRequiredService<SiloPlayer.Core.Api.CatalogApi>();
        _resultSortChoices = CatalogSortChoices.Capture(ResultSortCombo);
        UpdateResultSortChoices();
        ResultFiltersSheet.ConfigureFilterHeader(SearchQueryFilters.DetachModeSelector(), "Refine your catalog results");
        NavigationCacheMode = NavigationCacheMode.Required;
        SearchLoadingRepeater.ItemsSource = Enumerable.Range(0, 24).ToArray();

        ResultsRepeater.ItemsSource = ViewModel.Results;
        PeopleRepeater.ItemsSource = ViewModel.PeopleResults;
        RequestResultsRepeater.ItemsSource = ViewModel.OutsideLibraryResults;

        ViewModel.Results.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdateResultsState);
        };

        ViewModel.PeopleResults.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(UpdatePeopleSection);
        };
        ViewModel.OutsideLibraryResults.CollectionChanged += (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateRequestResults();
                if (ViewModel.Results.Count == 0 && ViewModel.PeopleResults.Count == 0)
                    UpdateResultsState();
            });
        };

        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ViewModel.OutsidePage) or nameof(ViewModel.OutsideTotalPages) or nameof(ViewModel.OutsideError) or nameof(ViewModel.IsOutsideLoading))
                DispatcherQueue.TryEnqueue(UpdateRequestResults);
            if (args.PropertyName == nameof(ViewModel.PeopleError)) DispatcherQueue.TryEnqueue(UpdatePeopleSection);
            if (args.PropertyName == nameof(ViewModel.TotalCount) ||
                args.PropertyName == nameof(ViewModel.IsLoading) ||
                args.PropertyName == nameof(ViewModel.ErrorMessage))
            {
                DispatcherQueue.TryEnqueue(UpdateResultsState);
            }
        };
        SizeChanged += SearchPage_SizeChanged;
        Loaded += (_, _) => PositionSearchSurface();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isNavigated = true;
        _uiCustomizationService.Changed += UICustomization_Changed;

        if (e.NavigationMode == NavigationMode.Back && !string.IsNullOrWhiteSpace(ViewModel.Query))
        {
            SearchBox.Text = ViewModel.Query;
            PositionSearchSurface();
            UpdateScopeButtons();
            UpdateResultsState();
        }
        else
        {
            ViewModel.CancelPendingSearch();
            ViewModel.Query = "";
            ViewModel.Results.Clear();
            ViewModel.PeopleResults.Clear();
            ViewModel.OutsideLibraryResults.Clear();
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
        var generation = ++_navigationGeneration;
        _shownRatingSources = _catalogApi.CachedShownRatingSources;
        UpdateResultSortChoices();
        _ = RefreshRatingSortChoicesAsync(generation);
        if (e.Parameter is string query && !string.IsNullOrWhiteSpace(query)) _ = OpenQueryAsync(query, generation);
        else if (e.Parameter is SearchNavigation route && !string.IsNullOrWhiteSpace(route.Query)) _ = OpenQueryAsync(route.Query, generation, route.MediaScope);
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

    private void UpdateResultsState()
    {
        bool hasQuery = !string.IsNullOrWhiteSpace(ViewModel.Query);

        EmptyState.Visibility = hasQuery ? Visibility.Collapsed : Visibility.Visible;
        ResultsState.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;

        if (hasQuery)
        {
            ResultsTitle.Text = $"Results for \"{ViewModel.Query}\"";
            var mediaCount = ViewModel.TotalCount;
            var peopleCount = ViewModel.PeopleResults.Count;
            // Match the catalog surface: the visible count describes in-library
            // media results. Optional request/discovery suggestions render in
            // their own section and should not make the header count jump a few
            // seconds after the media grid has settled.
            var totalDisplay = mediaCount + peopleCount;
            ResultCountText.Text = $"{totalDisplay:N0} in library";
            // Current WebUI Catalog.tsx deliberately hides the header count
            // while source=query. It avoids a misleading exact-total read while
            // the virtualized query window and request/discovery section settle.
            ResultCountPanel.Visibility = Visibility.Collapsed;

            NoResultsText.Visibility = mediaCount == 0 && peopleCount == 0 && ViewModel.OutsideLibraryResults.Count == 0 && !ViewModel.IsLoading
                ? Visibility.Visible : Visibility.Collapsed;
        }

        SearchLoadingRepeater.Visibility = hasQuery &&
            ViewModel.IsLoading &&
            ViewModel.Results.Count == 0 &&
            ViewModel.PeopleResults.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        UpdatePeopleSection();
        UpdateRequestResults();
    }

    private void UpdatePeopleSection()
    {
        PeopleSection.Visibility = ViewModel.PeopleResults.Count > 0 || ViewModel.PeopleError != null ? Visibility.Visible : Visibility.Collapsed;
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

    private void UpdateRequestResults()
    {
        // Optional request-provider discovery can arrive several seconds after
        // the in-library catalog grid. Keep the primary count/grid visually
        // stable, but still match the WebUI by rendering Request to Add below
        // local hits when discovery is enabled and returns suggestions.
        RequestResultsSection.Visibility = ViewModel.OutsideLibraryResults.Count > 0 || ViewModel.OutsideTotalPages > 0 || ViewModel.OutsideError != null
            ? Visibility.Visible
            : Visibility.Collapsed;
        var hasLibraryHits = ViewModel.Results.Count > 0;
        RequestResultsEyebrow.Text = hasLibraryHits
            ? "Discover · Outside your library"
            : "Outside your library";
        RequestResultsTitle.Text = "Request to add";
        var count = ViewModel.OutsideLibraryResults.Count;
        RequestResultsCount.Text = $"{count} {(count == 1 ? "result" : "results")}";
        RequestPageText.Text = ViewModel.OutsideTotalPages > 0 ? $"Page {ViewModel.OutsidePage} of {ViewModel.OutsideTotalPages}" : "";
        RequestPreviousButton.IsEnabled = !ViewModel.IsOutsideLoading && ViewModel.OutsidePage > 1;
        RequestNextButton.IsEnabled = !ViewModel.IsOutsideLoading && ViewModel.OutsidePage < ViewModel.OutsideTotalPages;
        RequestPagingPanel.Visibility = ViewModel.OutsideTotalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
        RequestErrorText.Text = ViewModel.OutsideError ?? "";
    }

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
    {
        host.Content = SiloPlayer.Controls.ExternalTitleCard.Build(item, _catalogCardWidth,
            request: async () =>
            {
                await ViewModel.RequestOutsideTitleAsync(item);
                if (!ReferenceEquals(host.Tag, item) || !ViewModel.OutsideLibraryResults.Contains(item)) return;
                BuildExternalRequestCard(host, item);
                App.Services.GetRequiredService<ToastService>().Success("Request submitted");
            },
            watchlist: ViewModel.OutsideWatchlistTitlesSupported ? async () =>
            {
                await ViewModel.ToggleOutsideWatchlistAsync(item);
                App.Services.GetService<WatchlistViewModel>()?.InvalidateExternalTitles();
            } : null);
    }

    private async void RequestPrevious_Click(object sender, RoutedEventArgs e) { await ViewModel.SetOutsidePageAsync(ViewModel.OutsidePage - 1); UpdateRequestResults(); }
    private async void RequestNext_Click(object sender, RoutedEventArgs e) { await ViewModel.SetOutsidePageAsync(ViewModel.OutsidePage + 1); UpdateRequestResults(); }
    private async void RequestRetry_Click(object sender, RoutedEventArgs e) { await ViewModel.SetOutsidePageAsync(ViewModel.OutsideRequestedPage); UpdateRequestResults(); }

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
        ResultsTitle.Text = $"Results for \"{ViewModel.Query}\"";
    }

    private void PositionSearchSurface()
    {
        if (!IsLoaded)
            return;

        // Keep the TextBox in one visual parent. Reparenting a live WinUI
        // element can fail inside Frame.Navigate with 0x800F1000.
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
        if (e.Key != Windows.System.VirtualKey.Enter ||
            string.IsNullOrWhiteSpace(SearchBox.Text))
            return;

        e.Handled = true;
        _searchDebounce?.Stop();
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
        if (!_resultsScrollUserScrolled ||
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
        var gutter = e.NewSize.Width < 640 ? 16
            : e.NewSize.Width < 1024 ? 24
            : 40;
        ResultsHeader.Margin = new Thickness(gutter, e.NewSize.Width < 640 ? 16 : 24, gutter, 24);
        var titleSize = Math.Clamp(e.NewSize.Width * 0.05, 32, 56);
        ResultsTitle.FontSize = titleSize;
        ResultsTitle.LineHeight = titleSize * 0.95;
        var emptyTitleSize = Math.Clamp(e.NewSize.Width * 0.04, 32, 60);
        EmptySearchTitle.FontSize = emptyTitleSize;
        EmptySearchTitle.LineHeight = emptyTitleSize * 0.95;
        ResultsSearchHost.Margin = new Thickness(gutter, 0, gutter, 12);
        ResultsToolbar.Margin = new Thickness(gutter, 0, gutter, 24);
        ActiveResultFiltersPanel.Margin = new Thickness(gutter, 0, gutter, 24);
        ResultsScroll.Padding = new Thickness(gutter, 0, gutter, 24);
        // page-shell's 1400px maximum includes its responsive inline
        // padding. XAML margins/padding are outside these children, so reduce
        // their maxima to preserve the same outer shell width on ultrawide
        // displays instead of letting search stretch 96px wider than WebUI.
        var shellContentWidth = Math.Max(0, 1400 - (gutter * 2));
        ResultsHeader.MaxWidth = shellContentWidth;
        ResultsToolbar.MaxWidth = shellContentWidth;
        ActiveResultFiltersPanel.MaxWidth = shellContentWidth;
        ResultsContent.MaxWidth = shellContentWidth;
        var searchWidth = Math.Max(280, Math.Min(576, e.NewSize.Width - (gutter * 2)));
        EmptySearchHost.Width = searchWidth;
        ResultsSearchHost.Width = searchWidth;
        SearchSurface.Width = searchWidth;
        PositionSearchSurface();
        UpdateCatalogGridLayout(e.NewSize.Width, gutter);
    }

    private void UpdateCatalogGridLayout(double viewportWidth, double gutter)
    {
        var contentWidth = Math.Max(
            320,
            Math.Min(1400 - (gutter * 2), viewportWidth - (gutter * 2)));
        var columns = _uiCustomizationService.GetPosterColumnCount(contentWidth);
        var gap = _uiCustomizationService.CardPresentation.PosterSize == "large" ? 16d : 12d;
        ResultsGridLayout.MinColumnSpacing = ResultsGridLayout.MinRowSpacing = gap;
        SearchLoadingGridLayout.MinColumnSpacing = SearchLoadingGridLayout.MinRowSpacing = gap;
        _catalogCardWidth = Math.Max(96, (contentWidth - (gap * (columns - 1))) / columns);
        ResultsGridLayout.MaximumRowsOrColumns = columns;
        ResultsGridLayout.MinItemWidth = _catalogCardWidth;
        ResultsGridLayout.MinItemHeight = (_catalogCardWidth * 1.5) + _uiCustomizationService.CardCaptionHeight;
        SearchLoadingGridLayout.MaximumRowsOrColumns = columns;
        SearchLoadingGridLayout.MinItemWidth = _catalogCardWidth;
        SearchLoadingGridLayout.MinItemHeight = (_catalogCardWidth * 1.5) + _uiCustomizationService.CardCaptionHeight;
        for (var index = 0; index < ViewModel.Results.Count; index++)
            if (ResultsRepeater.TryGetElement(index) is SiloPlayer.Controls.PosterCard card)
                card.SetCatalogGridLayout(_catalogCardWidth);
        for (var index = 0; index < 24; index++)
            if (SearchLoadingRepeater.TryGetElement(index) is StackPanel skeleton)
                SetSearchSkeletonLayout(skeleton);
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
        _uiCustomizationService.Changed -= UICustomization_Changed;
        _searchDebounce?.Stop();
        ViewModel.CancelPendingSearch();
        base.OnNavigatedFrom(e);
    }

    private void UICustomization_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_isNavigated) return;
            var width = Math.Max(320, ActualWidth);
            var gutter = width < 640 ? 16d : width < 1024 ? 24d : 40d;
            UpdateCatalogGridLayout(width, gutter);
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
        ViewModel.SortField = SelectedTag(ResultSortCombo) ?? "added_at";
        var ascending = ViewModel.SortField is "title" or "content_rating" or "author" or "narrator" or "series";
        _filterInitializing = true;
        SelectComboTag(ResultOrderCombo, ascending ? "asc" : "desc");
        _filterInitializing = false;
        if (!string.IsNullOrWhiteSpace(ViewModel.Query)) await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private async void OpenResultFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchQueryFilters.ConfigureSort();
        ViewModel.AdvancedQuery.Sort = new() { Field = ViewModel.SortField, Order = ViewModel.SortOrder };
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
        if (ViewModel.AdvancedQuery.Sort is {} sort)
        {
            ViewModel.SortField = sort.Field; ViewModel.SortOrder = sort.Order;
            _filterInitializing = true;
            try { SelectComboTag(ResultSortCombo, sort.Field); SelectComboTag(ResultOrderCombo, sort.Order); }
            finally { _filterInitializing = false; }
        }
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
        AddActiveResultFilter("genre", ViewModel.Genre, value => $"Genre: {value}");
        AddActiveResultFilter("rating", ViewModel.ContentRating, value => $"Rated: {value}");
        AddActiveResultFilter("resolution", ViewModel.Resolution, value => $"Resolution: {value}");
        AddActiveResultFilter("country", ViewModel.Country, value => $"Country: {value}");
        foreach (var badge in SiloPlayer.Core.Services.CatalogFilterBadges.Create(ViewModel.AdvancedQuery, ViewModel.MediaScope))
        {
            var chip = CatalogFilterBadgeView.Build(badge.Label, async () => { badge.Remove(); SearchQueryFilters.Load(ViewModel.AdvancedQuery, ViewModel.MediaScope, filters: ViewModel.AvailableFilters); UpdateActiveResultFilters(); await ViewModel.SearchCommand.ExecuteAsync(null); });
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
}
