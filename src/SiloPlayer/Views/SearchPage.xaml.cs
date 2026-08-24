using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

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

    public SearchPage()
    {
        ViewModel = App.Services.GetRequiredService<SearchViewModel>();
        _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
        this.InitializeComponent();
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
        // The current full catalog search surface only renders media results;
        // people remain available through the global command palette.
        PeopleSection.Visibility = Visibility.Collapsed;
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
        RequestResultsSection.Visibility = ViewModel.OutsideLibraryResults.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        var hasLibraryHits = ViewModel.Results.Count > 0;
        RequestResultsEyebrow.Text = hasLibraryHits
            ? "Discover · Outside your library"
            : "Outside your library";
        RequestResultsTitle.Text = hasLibraryHits
            ? "Request to Add"
            : "Not in your library, but you can request";
        var count = ViewModel.OutsideLibraryResults.Count;
        RequestResultsCount.Text = $"{count} {(count == 1 ? "result" : "results")}";
    }

    private void RequestResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RequestMediaResult item })
            App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(item.MediaType, item.TmdbId));
    }

    private async void RequestNow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RequestMediaResult item } button || !item.Request.Requestable)
            return;

        button.IsEnabled = false;
        var previousContent = button.Content;
        button.Content = "Requesting…";
        try
        {
            var created = await App.Services.GetRequiredService<SiloPlayer.Core.Api.RequestsApi>()
                .CreateAsync(new CreateMediaRequestInput
                {
                    MediaType = item.MediaType,
                    TmdbId = item.TmdbId,
                    Title = item.Title,
                    Year = item.Year,
                    Overview = item.Overview,
                    PosterPath = item.PosterPath,
                    BackdropPath = item.BackdropPath,
                });
            item.Request.Requestable = false;
            item.Request.Status = created.Status;
            item.Request.RequestId = created.Id;
            button.Content = item.RequestLabel;
            App.Services.GetRequiredService<ToastService>().Success("Request submitted");
        }
        catch (Exception ex)
        {
            button.Content = previousContent;
            button.IsEnabled = true;
            App.Services.GetRequiredService<ToastService>().Error($"Request failed: {ex.Message}");
        }
    }

    private static Border? FindRequestOverlay(object sender)
        => sender is Grid grid
            ? grid.Children.OfType<Border>().FirstOrDefault(border => Equals(border.Tag, "request-overlay"))
            : null;

    private void RequestCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (FindRequestOverlay(sender) is not { } overlay) return;
        overlay.Opacity = 1;
        overlay.IsHitTestVisible = true;
    }

    private void RequestCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (FindRequestOverlay(sender) is not { } overlay) return;
        overlay.Opacity = 0;
        overlay.IsHitTestVisible = false;
    }

    private void RequestCard_GotFocus(object sender, RoutedEventArgs e)
    {
        if (FindRequestOverlay(sender) is not { } overlay) return;
        overlay.Opacity = 1;
        overlay.IsHitTestVisible = true;
    }

    private void RequestCard_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is Grid grid && grid.FocusState != FocusState.Unfocused) return;
        if (FindRequestOverlay(sender) is not { } overlay) return;
        overlay.Opacity = 0;
        overlay.IsHitTestVisible = false;
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
        _catalogCardWidth = Math.Max(96, (contentWidth - (12 * (columns - 1))) / columns);
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
        var key = $"{query}\u001F{mediaType ?? ""}";
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
            !string.Equals(ViewModel.Query.Trim(), query, StringComparison.Ordinal) ||
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
        UpdateActiveResultFilters();
    }

    private static void FillResultCombo(ComboBox combo, string allLabel, IEnumerable<string> values)
    {
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = allLabel, Tag = "" });
        foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
            combo.Items.Add(new ComboBoxItem { Content = value, Tag = value });
        combo.SelectedIndex = 0;
    }

    private async void ResultFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_filterInitializing || string.IsNullOrWhiteSpace(ViewModel.Query)) return;
        if (ReferenceEquals(sender, ResultTypeCombo))
        {
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
        ResultFiltersSheet.IsOpen = true;
        await EnsureSearchFiltersLoadedAsync();
    }
    private void CloseResultFilters_Click(object sender, RoutedEventArgs e) => ResultFiltersSheet.IsOpen = false;

    private void UpdateActiveResultFilters()
    {
        ActiveResultFiltersPanel.Children.Clear();
        AddActiveResultFilter("genre", ViewModel.Genre, value => $"Genre: {value}");
        AddActiveResultFilter("rating", ViewModel.ContentRating, value => $"Rated: {value}");
        AddActiveResultFilter("resolution", ViewModel.Resolution, value => $"Resolution: {value}");
        AddActiveResultFilter("country", ViewModel.Country, value => $"Country: {value}");

        var count = ActiveResultFiltersPanel.Children.Count;
        ActiveResultFiltersPanel.Visibility = count > 0
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
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
        };
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        content.Children.Add(new FontIcon
        {
            Glyph = "\uE711",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var button = new Button
        {
            Tag = key,
            Content = content,
            Padding = new Thickness(8, 4, 6, 4),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(0),
            Background = (Brush)Application.Current.Resources["SurfaceRaisedBrush"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Remove {text}");
        button.Click += ActiveResultFilter_Click;
        ActiveResultFiltersPanel.Children.Add(button);
    }

    private async void ActiveResultFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key })
            return;

        _filterInitializing = true;
        try
        {
            switch (key)
            {
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
        UpdateActiveResultFilters();
        if (!string.IsNullOrWhiteSpace(ViewModel.Query)) await ViewModel.SearchCommand.ExecuteAsync(null);
    }

    private static string? SelectedTag(ComboBox combo)
    {
        var value = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void SelectComboTag(ComboBox combo, string value)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = index;
                return;
            }
        }
    }
}
