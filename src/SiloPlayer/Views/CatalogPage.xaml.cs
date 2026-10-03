using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed record CatalogNavigation(
    string Source = "library",
    string? Title = null,
    string? Subtitle = null,
    string? Scope = null,
    string? SectionId = null,
    int? LibraryId = null,
    string? Genre = null);

public sealed partial class CatalogPage : Page,
    IRecipient<MediaSurfaceChanged>,
    IRecipient<PlaybackProgressUpdated>
{
    private readonly CatalogApi _api = App.Services.GetRequiredService<CatalogApi>();
    private readonly CollectionsApi _collectionsApi = App.Services.GetRequiredService<CollectionsApi>();
    private readonly AuthService _authService = App.Services.GetRequiredService<AuthService>();
    private readonly UICustomizationService _uiCustomizationService = App.Services.GetRequiredService<UICustomizationService>();
    private readonly ObservableCollection<MediaItem> _items = [];
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _navigationCts;
    private DispatcherTimer? _debounce;
    private int _offset;
    private bool _hasMore;
    private bool _loading;
    private bool _initializing = true;
    private bool _personalDefaultOrderTouched;
    private long _loadGeneration;
    private bool _selectionMode;
    private double _catalogCardWidth = 154;
    private string _source = "library";
    private string? _scope;
    private string? _sectionId;
    private int? _fixedLibraryId;
    private string? _initialGenre;
    private string? _snapshot;
    private readonly HashSet<string> _selectedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<QueryRule> _advancedRules = [];
    private bool _advancedMode;
    private const int PageSize = 60;
    private bool _messengerRegistered;
    private readonly object _sortPreferenceGate = new();
    private Task _sortPreferenceTail = Task.CompletedTask;

    private static readonly (string Label, string Value)[] AdvancedFields =
    [
        ("Genre", "genre"), ("Year", "year"), ("IMDb Rating", "rating_imdb"),
        ("Type", "type"), ("Content Rating", "content_rating"), ("Studio", "studio"),
        ("Actor", "actor"), ("Director", "director"), ("Writer", "writer"),
        ("Producer", "producer"), ("Network", "network"), ("Country", "country"),
        ("Status", "status"), ("Added", "added_at"), ("Release Date", "release_date"),
        ("Watched", "watched"), ("Favorited", "favorited"), ("In Watchlist", "in_watchlist"),
        ("In Progress", "in_progress"), ("Resolution", "resolution"), ("HDR", "hdr"),
        ("Dolby Vision", "dolby_vision"), ("Bitrate", "bitrate")
    ];

    private static readonly (string Label, string Value)[] AdvancedOperators =
    [
        ("is", "is"), ("is not", "is_not"), ("contains", "contains"),
        (">=", "gte"), ("<=", "lte"), (">", "gt"), ("<", "lt"),
        ("in the last", "in_last")
    ];

    public CatalogPage()
    {
        InitializeComponent();
        ItemsRepeater.ItemsSource = _items;
        CatalogLoadingRepeater.ItemsSource = Enumerable.Range(0, 24).ToArray();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _uiCustomizationService.Changed += UICustomization_Changed;
        _navigationCts?.Cancel();
        _navigationCts?.Dispose();
        _navigationCts = new CancellationTokenSource();
        var navigationToken = _navigationCts.Token;
        _personalDefaultOrderTouched = false;
        RegisterMediaMessages();
        if (e.Parameter is CatalogNavigation navigation)
        {
            _source = navigation.Source;
            _scope = navigation.Scope;
            _sectionId = navigation.SectionId;
            _fixedLibraryId = navigation.LibraryId;
            _initialGenre = navigation.Genre;
            PageTitleText.Text = navigation.Title ?? (_source == "history" ? "History" : "Catalog");
            PageSubtitleText.Text = navigation.Subtitle ?? (_source == "watchlist" ? "Things you've saved to watch later." : _source == "history"
                ? "Everything you've recently watched."
                : "Refine the archive by library, type, era, rating, or genre.");
            if (App.MainWindowInstance is MainWindow window)
                window.SetDynamicTitle(PageTitleText.Text);
        }

        if (_source is "history" or "favorites" or "watchlist")
        {
            SortCombo.Items.Insert(0, new ComboBoxItem
            {
                Content = PersonalCatalogSortPolicy.DefaultSortLabel(_source),
                // The current WebUI presents these labels while preserving the
                // server-defined personal-list order. Sending an explicit
                // added_at sort changes the history resolver path and can show
                // the wrong catalog instead of the profile's visible history.
                Tag = ""
            });
            SortCombo.SelectedIndex = 0;
            if (_source is "favorites" or "history")
                OrderCombo.SelectedIndex = 0; // current WebUI defaults these recency lists to Descending

            // The live WebUI treats Favorites and Watchlist order as a single,
            // server-owned
            // "List Order" control.  Showing a second Ascending/Descending
            // selector here both diverges visually and can accidentally turn
            // the personal ordering into an added-at sort.
            OrderCombo.Visibility = PersonalCatalogSortPolicy.ShouldShowOrderSelector(_source, null)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        InitializeWatchlistTitles();
        if (_source == "history") HistoryActions.Visibility = Visibility.Visible;
        if (_source == "section")
        {
            // A section is a stored server recipe. The current WebUI deliberately
            // suppresses the catalog overlay so local filters cannot silently
            // change the recipe being explored.
            FilterPanel.Visibility = Visibility.Collapsed;
            LockedFiltersPanel.Visibility = Visibility.Visible;
        }
        Task? initialCatalogLoad = null;
        try
        {
            ShowInitialLoadingState();

            // Personal shelves do not depend on the supplementary filter
            // metadata.  The WebUI paints their result/empty state immediately
            // while that metadata is fetched in parallel.  Waiting for the
            // (potentially very large) filter response left an empty watchlist
            // showing skeleton cards indefinitely on real servers.
            if (_source is "favorites" or "watchlist" or "history")
                initialCatalogLoad = LoadAsync(true);

            await InitializeFiltersAsync(navigationToken);
            navigationToken.ThrowIfCancellationRequested();
            _initializing = false;
            UpdateFilterCount();
            if (initialCatalogLoad != null)
                await initialCatalogLoad;
            else
                await LoadAsync(true);
        }
        catch (OperationCanceledException) when (navigationToken.IsCancellationRequested)
        {
            // A newer route owns the frame. Do not paint stale filters or an
            // error state over the page the user has already opened.
        }
        catch (Exception ex)
        {
            // Filter metadata is supplementary. A malformed or temporarily
            // unavailable filter response must never crash the app or leave a
            // catalog surface permanently displaying skeletons.
            _initializing = false;
            System.Diagnostics.Debug.WriteLine($"Catalog filter metadata unavailable: {ex}");
            UpdateFilterCount();
            if (initialCatalogLoad != null)
                await initialCatalogLoad;
            else
                await LoadAsync(true);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Interlocked.Exchange(ref _queryOptionsOwner, null)?.Cancel();
        StopWatchlistTitles();
        Interlocked.Increment(ref _loadGeneration);
        _navigationCts?.Cancel();
        _loadCts?.Cancel();
        _debounce?.Stop();
        UnregisterMediaMessages();
        _uiCustomizationService.Changed -= UICustomization_Changed;
        base.OnNavigatedFrom(e);
    }

    private void UICustomization_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() =>
        {
            var width = Math.Min(Math.Max(ActualWidth, 320), 1400);
            var gutter = width < 640 ? 16d : width < 1024 ? 24d : 40d;
            ApplyCatalogCardLayout(width, gutter);
            if (_source == "watchlist") ApplyExternalTitleLayout(ExternalWatchlistScroller.ActualWidth);
        });

    private void RegisterMediaMessages()
    {
        if (_messengerRegistered) return;
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(this);
        WeakReferenceMessenger.Default.Register<PlaybackProgressUpdated>(this);
        _messengerRegistered = true;
    }

    private void UnregisterMediaMessages()
    {
        if (!_messengerRegistered) return;
        WeakReferenceMessenger.Default.Unregister<MediaSurfaceChanged>(this);
        WeakReferenceMessenger.Default.Unregister<PlaybackProgressUpdated>(this);
        _messengerRegistered = false;
    }

    public void Receive(MediaSurfaceChanged message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var item = _items.FirstOrDefault(candidate =>
                string.Equals(candidate.ContentId, message.ContentId, StringComparison.OrdinalIgnoreCase));
            if (item == null) return;

            var remove = (_source == "favorites" && message.Kind == MediaSurfaceChangeKind.FavoriteRemoved)
                || (_source == "watchlist" && message.Kind == MediaSurfaceChangeKind.WatchlistRemoved)
                || (_source == "history" && message.Kind == MediaSurfaceChangeKind.WatchedCleared);
            if (remove)
            {
                _items.Remove(item);
                CountText.Text = Math.Max(0, _items.Count).ToString("N0");
                EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            var changed = message.Kind switch
            {
                MediaSurfaceChangeKind.FavoriteAdded => MediaItemStateUpdater.SetFavorite(item, true),
                MediaSurfaceChangeKind.FavoriteRemoved => MediaItemStateUpdater.SetFavorite(item, false),
                MediaSurfaceChangeKind.WatchlistAdded => MediaItemStateUpdater.SetWatchlist(item, true),
                MediaSurfaceChangeKind.WatchlistRemoved => MediaItemStateUpdater.SetWatchlist(item, false),
                MediaSurfaceChangeKind.WatchedMarked => MediaItemStateUpdater.SetWatched(item, true),
                MediaSurfaceChangeKind.WatchedCleared => MediaItemStateUpdater.SetWatched(item, false),
                _ => false,
            };
            if (changed) RefreshRealizedItemState(message.ContentId);
        });
    }

    public void Receive(PlaybackProgressUpdated message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var item = _items.FirstOrDefault(candidate =>
                string.Equals(candidate.ContentId, message.ContentId, StringComparison.OrdinalIgnoreCase));
            if (item == null) return;
            if (MediaItemStateUpdater.SetPlaybackProgress(
                item,
                message.PositionSeconds,
                message.DurationSeconds,
                message.Completed,
                message.UpdatedAt))
            {
                RefreshRealizedItemState(message.ContentId);
            }
        });
    }

    private void RefreshRealizedItemState(string contentId)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (!string.Equals(_items[i].ContentId, contentId, StringComparison.OrdinalIgnoreCase)) continue;
            if (ItemsRepeater.TryGetElement(i) is PosterCard card) card.RefreshState();
        }
    }

    private async Task InitializeFiltersAsync(CancellationToken ct)
    {
        var librariesTask = _api.GetLibrariesAsync(ct);
        var filtersTask = _api.GetFiltersAsync(
            libraryId: _fixedLibraryId,
            ct: ct,
            source: _source == "library" ? null : _source,
            scope: _scope,
            sectionId: _sectionId);
        await Task.WhenAll(librariesTask, filtersTask);
        ct.ThrowIfCancellationRequested();
        LibraryCombo.Items.Add(new ComboBoxItem { Content = "All libraries", Tag = (int?)null });
        foreach (var library in librariesTask.Result) LibraryCombo.Items.Add(new ComboBoxItem { Content = library.Name, Tag = (int?)library.Id });
        var fixedLibraryIndex = _fixedLibraryId is > 0
            ? librariesTask.Result.FindIndex(l => l.Id == _fixedLibraryId.Value) + 1
            : 0;
        LibraryCombo.SelectedIndex = Math.Max(0, fixedLibraryIndex);
        LibraryCombo.IsEnabled = _fixedLibraryId is not > 0;
        if (_source != "section")
        {
            InitializeQueryFilters(filtersTask.Result);
            return;
        }
        Fill(GenreCombo, "All genres", filtersTask.Result.Genres);
        if (!string.IsNullOrWhiteSpace(_initialGenre))
        {
            for (var index = 0; index < GenreCombo.Items.Count; index++)
            {
                if (GenreCombo.Items[index] is ComboBoxItem { Tag: string value } &&
                    value.Equals(_initialGenre, StringComparison.OrdinalIgnoreCase))
                {
                    GenreCombo.SelectedIndex = index;
                    break;
                }
            }
        }
        Fill(RatingCombo, "All ratings", filtersTask.Result.ContentRatings);
        Fill(ResolutionCombo, "All resolutions", filtersTask.Result.Resolutions);
        Fill(CountryCombo, "All countries", filtersTask.Result.Countries);
        Fill(StudioCombo, "All studios", filtersTask.Result.Studios);
        Fill(NetworkCombo, "All networks", filtersTask.Result.Networks);
        Fill(OriginalLanguageCombo, "All languages", filtersTask.Result.OriginalLanguages);
        Fill(AudioLanguageCombo, "All languages", filtersTask.Result.AudioLanguages);
        Fill(AuthorCombo, "All authors", filtersTask.Result.Authors);
        Fill(NarratorCombo, "All narrators", filtersTask.Result.Narrators);
        Fill(SeriesCombo, "All series", filtersTask.Result.Series);
    }

    private static void Fill(ComboBox combo, string all, IEnumerable<string>? values)
    {
        combo.Items.Add(new ComboBoxItem { Content = all, Tag = "" });
        foreach (var value in values ?? [])
            if (!string.IsNullOrWhiteSpace(value))
                combo.Items.Add(new ComboBoxItem { Content = value, Tag = value });
        combo.SelectedIndex = 0;
    }

    private async Task LoadAsync(bool reset)
    {
        if (_queryFilters?.IsValid == false) return;
        if (_loading && !reset) return;
        if (reset)
        {
            Interlocked.Increment(ref _loadGeneration);
            _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = new CancellationTokenSource();
            _offset = 0; _snapshot = null; _items.Clear();
            _selectedIds.Clear();
            UpdateSelectionUi();
        }
        if (_loadCts == null) _loadCts = new CancellationTokenSource();
        var generation = Volatile.Read(ref _loadGeneration);
        _loading = true;
        var initialLoad = reset && _items.Count == 0;
        CatalogLoadingRepeater.Visibility = initialLoad ? Visibility.Visible : Visibility.Collapsed;
        ItemsRepeater.Visibility = initialLoad ? Visibility.Collapsed : Visibility.Visible;
        LoadingRing.IsActive = !initialLoad;
        LoadingRing.Visibility = !initialLoad ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
        CatalogErrorPanel.Visibility = Visibility.Collapsed;
        try
        {
            var sort = SelectedTag(SortCombo);
            var requestSort = sort;
            if (requestSort == null && _personalDefaultOrderTouched && _source is "favorites" or "watchlist" or "history")
                requestSort = "added_at";
            var isSection = _source == "section";
            var useGuidedFilters = !isSection && !_advancedMode && _queryFilters == null;
            var queryRules = isSection || _queryFilters != null
                ? null
                : _advancedMode
                    ? BuildAdvancedRules()
                    : BuildGuidedRules();
            var response = await _api.GetCatalogAsync(
                isSection ? _fixedLibraryId : SelectedLibrary(),
                sort: isSection ? null : requestSort,
                order: isSection || requestSort == null ? null : SelectedTag(OrderCombo),
                genre: useGuidedFilters ? SelectedTag(GenreCombo) : null,
                studio: useGuidedFilters ? SelectedTag(StudioCombo) : null,
                contentRating: useGuidedFilters ? SelectedTag(RatingCombo) : null,
                country: useGuidedFilters ? SelectedTag(CountryCombo) : null,
                resolution: useGuidedFilters ? SelectedTag(ResolutionCombo) : null,
                audioLanguage: useGuidedFilters ? SelectedTag(AudioLanguageCombo) : null,
                yearMin: useGuidedFilters ? TextOrNull(YearFromBox) : null,
                yearMax: useGuidedFilters ? TextOrNull(YearToBox) : null,
                q: isSection ? null : QueryBox.Text.Trim(),
                type: isSection ? null : SelectedTag(TypeCombo),
                limit: PageSize,
                offset: _offset,
                snapshot: _snapshot,
                source: _source == "library" ? null : _source,
                scope: _scope,
                sectionId: _sectionId,
                extraRules: queryRules,
                extraRulesMatch: _advancedMode ? SelectedTag(AdvancedMatchCombo) ?? "all" : "all",
                queryGroups: !isSection && _queryFilters != null ? _catalogQuery.Groups : null,
                queryGroupsMatch: _catalogQuery.Match,
                ct: _loadCts.Token);
            if (generation != Volatile.Read(ref _loadGeneration)) return;
            foreach (var item in response.Items)
            {
                item.ItemSource = _source;
                _items.Add(item);
            }
            _snapshot = response.Snapshot ?? _snapshot;
            _offset += response.Items.Count;
            _hasMore = response.HasMore || _offset < response.Total;
            CountText.Text = response.Total.ToString("N0");
            ResultNounText.Text = response.Total == 1 ? "RESULT" : "RESULTS";
            CountPanel.Visibility = response.TotalExact ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ItemsRepeater.Visibility = Visibility.Visible;
            CatalogLoadingRepeater.Visibility = Visibility.Collapsed;
            // Current WebUI ItemGrid does not expose a manual "Load more"
            // control; it fetches additional windows as the user scrolls.
            // Keep the native scroll loader active, but do not show a
            // desktop-only button at the bottom of catalog surfaces.
            LoadMoreButton.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                var collectionUnavailable = ex is ApiException { StatusCode: 404 } && _source is "user_collection" or "library_collection";
                CatalogErrorTitle.Text = collectionUnavailable ? "This collection isn't available" : "Couldn't load the catalog";
                ErrorText.Text = collectionUnavailable ? "It may have been deleted, or you may not have access to it." : "The catalog request failed. Please retry.";
                ErrorText.Visibility = Visibility.Visible;
                CatalogErrorPanel.Visibility = Visibility.Visible;
                CatalogRetryButton.Visibility = collectionUnavailable ? Visibility.Collapsed : Visibility.Visible;
                CatalogCollectionsButton.Visibility = collectionUnavailable ? Visibility.Visible : Visibility.Collapsed;
                EmptyText.Visibility = Visibility.Collapsed;
                _hasMore = false;
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                _loading = false;
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                CatalogLoadingRepeater.Visibility = Visibility.Collapsed;
                ItemsRepeater.Visibility = Visibility.Visible;
            }
        }
    }

    private void ShowInitialLoadingState()
    {
        EmptyText.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
        CatalogErrorPanel.Visibility = Visibility.Collapsed;
        LoadMoreButton.Visibility = Visibility.Collapsed;
        CatalogLoadingRepeater.Visibility = Visibility.Visible;
        ItemsRepeater.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
    }

    private async void CatalogRetry_Click(object sender, RoutedEventArgs e)
    {
        CatalogRetryButton.IsEnabled = false;
        try { _hasMore = true; await LoadAsync(_items.Count == 0); }
        finally { CatalogRetryButton.IsEnabled = true; }
    }
    private void CatalogCollections_Click(object sender, RoutedEventArgs e)
        => App.Services.GetRequiredService<SiloPlayer.Helpers.NavigationService>().Navigate<CollectionsPage>();

    private void Filter_Changed(object sender, object e)
    {
        if (_initializing) return;
        if (ReferenceEquals(sender, LibraryCombo)) _ = RefreshQueryScopeAsync();
        if (ReferenceEquals(sender, OrderCombo) && _source is "favorites" or "watchlist" or "history")
            _personalDefaultOrderTouched = true;
        if ((ReferenceEquals(sender, SortCombo) || ReferenceEquals(sender, OrderCombo))
            && PersonalCatalogSortPolicy.SupportsSourceOrder(_source))
        {
            QueuePersonalSortPreferenceSave();
        }
        UpdateFilterCount();
        _debounce?.Stop();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(sender is TextBox ? 300 : 40) };
        _debounce.Tick += async (_, _) => { _debounce?.Stop(); await LoadAsync(true); };
        _debounce.Start();
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (OrderCombo == null) return;
        var sort = SelectedTag(SortCombo);
        OrderCombo.Visibility = PersonalCatalogSortPolicy.ShouldShowOrderSelector(_source, sort)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!_initializing && sort != null)
        {
            var ascendingByDefault = sort is "title" or "content_rating" or "author" or "narrator" or "series";
            OrderCombo.SelectedIndex = ascendingByDefault ? 1 : 0;
        }
        Filter_Changed(sender, e);
    }

    private void QueuePersonalSortPreferenceSave()
    {
        var source = _source;
        var profileId = _authService.SelectedProfileId;
        var field = SelectedTag(SortCombo) ?? "";
        var order = field.Length == 0 ? "" : SelectedTag(OrderCombo) ?? "desc";

        lock (_sortPreferenceGate)
        {
            _sortPreferenceTail = SavePersonalSortPreferenceAfterAsync(
                _sortPreferenceTail,
                source,
                profileId,
                field,
                order);
        }
    }

    private async Task SavePersonalSortPreferenceAfterAsync(
        Task previous,
        string source,
        string? profileId,
        string field,
        string order)
    {
        try { await previous.ConfigureAwait(false); }
        catch { }

        if (!string.Equals(_authService.SelectedProfileId, profileId, StringComparison.Ordinal))
            return;

        try
        {
            await _collectionsApi.SetCollectionSortPreferenceAsync(
                source,
                "",
                field,
                order).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The current sort already applies to this visit. Match the WebUI
            // by treating preference persistence as silent best effort.
            LocalLog.AppendLine(
                "catalog_error.txt",
                $"sort_preference | source={source} | {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e) { if (_hasMore) await LoadAsync(false); }

    private async void CatalogScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll) return;

        CatalogScrollToTopButton.Visibility = scroll.VerticalOffset > 720
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!e.IsIntermediate && _hasMore && scroll.ScrollableHeight - scroll.VerticalOffset < 900)
            await LoadAsync(false);
    }

    private void CatalogScrollToTop_Click(object sender, RoutedEventArgs e)
    {
        CatalogScrollViewer.ChangeView(null, 0, null);
        CatalogScrollToTopButton.Visibility = Visibility.Collapsed;
    }

    private void ItemsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not PosterCard card || card.MediaItem is not { } item) return;
        card.SelectionToggled -= PosterCard_SelectionToggled;
        card.SelectionMode = _selectionMode;
        card.IsSelected = _selectedIds.Contains(item.ContentId);
        card.SetCatalogGridLayout(_catalogCardWidth);
        card.SelectionToggled += PosterCard_SelectionToggled;
    }

    private void CatalogLoadingRepeater_ElementPrepared(
        ItemsRepeater sender,
        ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not StackPanel card) return;
        card.Width = _catalogCardWidth;
        if (card.Children.FirstOrDefault() is FrameworkElement poster)
            poster.Height = _catalogCardWidth * 1.5;
    }

    private void PosterCard_SelectionToggled(object? sender, EventArgs e)
    {
        if (sender is not PosterCard { MediaItem: { } item } card) return;
        if (card.IsSelected) _selectedIds.Add(item.ContentId);
        else _selectedIds.Remove(item.ContentId);
        UpdateSelectionUi();
    }

    private void SelectionMode_Click(object sender, RoutedEventArgs e)
    {
        _selectionMode = !_selectionMode;
        if (!_selectionMode) _selectedIds.Clear();
        RefreshRealizedSelection();
        UpdateSelectionUi();
    }

    private void SelectLoaded_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items) _selectedIds.Add(item.ContentId);
        RefreshRealizedSelection();
        UpdateSelectionUi();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        _selectedIds.Clear();
        RefreshRealizedSelection();
        UpdateSelectionUi();
    }

    private async void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(item => _selectedIds.Contains(item.ContentId)).ToList();
        if (selected.Count == 0) return;

        var description = selected.Count > 1
            ? $"{selected.Count} selected items will have their watch history, watched status, and resume progress cleared for this profile."
            : selected[0].Type is "series" or "season"
                ? "This clears the show's watch history, watched episodes, and resume progress for this profile."
                : "This clears the item's watch history, watched status, and resume progress for this profile.";
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap },
                error,
            },
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = selected.Count > 1
                ? "Remove selected watch data?"
                : selected[0].Type is "series" or "season" ? "Remove show watch data?" : "Remove watch data?",
            Content = content,
            PrimaryButtonText = "Remove",
            PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.PrimaryButtonText = "Removing...";
                RemoveSelectedButton.IsEnabled = false;
                error.Visibility = Visibility.Collapsed;
                await _api.RemoveHistoryAsync(selected.Select(item => new HistoryRemovalTarget
                {
                    ContentId = item.ContentId,
                    Scope = item.Type is "series" or "season" ? "show" : "item"
                }));
                _selectionMode = false;
                _selectedIds.Clear();
                await LoadAsync(true);
                args.Cancel = false;
            }
            catch (Exception ex)
            {
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
                dialog.PrimaryButtonText = "Remove";
                dialog.IsPrimaryButtonEnabled = true;
            }
            finally
            {
                RemoveSelectedButton.IsEnabled = true;
                RefreshRealizedSelection();
                UpdateSelectionUi();
                deferral.Complete();
            }
        };
        await dialog.ShowAsync();
    }

    private void RefreshRealizedSelection()
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (ItemsRepeater.TryGetElement(i) is not PosterCard card) continue;
            card.SelectionMode = _selectionMode;
            card.IsSelected = card.MediaItem is { } item && _selectedIds.Contains(item.ContentId);
        }
    }

    private void UpdateSelectionUi()
    {
        SelectionModeButton.Content = _selectionMode ? "Done" : "Select";
        SelectedCountText.Text = $"{_selectedIds.Count} selected";
        SelectedCountText.Visibility = _selectionMode ? Visibility.Visible : Visibility.Collapsed;
        SelectLoadedButton.Visibility = _selectionMode ? Visibility.Visible : Visibility.Collapsed;
        ClearSelectionButton.Visibility = _selectionMode ? Visibility.Visible : Visibility.Collapsed;
        RemoveSelectedButton.Visibility = _selectionMode ? Visibility.Visible : Visibility.Collapsed;
        ClearSelectionButton.IsEnabled = _selectedIds.Count > 0;
        RemoveSelectedButton.IsEnabled = _selectedIds.Count > 0;
    }

    private void OpenFilters_Click(object sender, RoutedEventArgs e) => FiltersSheet.IsOpen = true;
    private void CloseFilters_Click(object sender, RoutedEventArgs e) => FiltersSheet.IsOpen = false;

    private async void FilterMode_Click(object sender, RoutedEventArgs e)
    {
        var advanced = (sender as FrameworkElement)?.Tag?.ToString() == "advanced";
        if (_advancedMode == advanced) return;
        _advancedMode = advanced;
        GuidedFiltersPanel.Visibility = advanced ? Visibility.Collapsed : Visibility.Visible;
        AdvancedFiltersPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        var outline = Application.Current.Resources["OutlineButtonStyle"] as Style;
        GuidedModeButton.Style = advanced ? outline : null;
        AdvancedModeButton.Style = advanced ? null : outline;
        UpdateFilterCount();
        await LoadAsync(true);
    }

    private void AddAdvancedRule_Click(object sender, RoutedEventArgs e)
    {
        _advancedRules.Add(new QueryRule { Field = "genre", Op = "is", Value = "" });
        BuildAdvancedRulesUi();
        UpdateFilterCount();
    }

    private void AdvancedRule_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || !_advancedMode) return;
        Filter_Changed(sender, e);
    }

    private void BuildAdvancedRulesUi()
    {
        AdvancedRulesPanel.Children.Clear();
        NoAdvancedRulesText.Visibility = _advancedRules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var rule in _advancedRules.ToList())
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var field = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in AdvancedFields)
                field.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
            field.SelectedIndex = Math.Max(0, Array.FindIndex(AdvancedFields, option => option.Value == rule.Field));

            var operation = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in AdvancedOperators)
                operation.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
            operation.SelectedIndex = Math.Max(0, Array.FindIndex(AdvancedOperators, option => option.Value == rule.Op));

            var value = CreateAdvancedValueEditor(rule);
            var remove = new Button { Content = "Remove", Padding = new Thickness(9, 5, 9, 5), Tag = rule };
            if (Application.Current.Resources["OutlineButtonStyle"] is Style outline) remove.Style = outline;

            Grid.SetColumn(operation, 1);
            Grid.SetColumn(value, 2);
            Grid.SetColumn(remove, 3);
            row.Children.Add(field);
            row.Children.Add(operation);
            row.Children.Add(value);
            row.Children.Add(remove);

            field.SelectionChanged += (_, _) =>
            {
                rule.Field = SelectedTag(field) ?? "genre";
                rule.Op = DefaultOperator(rule.Field);
                rule.Value = DefaultAdvancedValue(rule.Field);
                BuildAdvancedRulesUi();
                Filter_Changed(field, new SelectionChangedEventArgs([], []));
            };
            operation.SelectionChanged += (_, _) =>
            {
                rule.Op = SelectedTag(operation) ?? "is";
                Filter_Changed(operation, new SelectionChangedEventArgs([], []));
            };
            remove.Click += (_, _) =>
            {
                _advancedRules.Remove(rule);
                BuildAdvancedRulesUi();
                Filter_Changed(remove, new RoutedEventArgs());
            };
            AdvancedRulesPanel.Children.Add(row);
        }
    }

    private FrameworkElement CreateAdvancedValueEditor(QueryRule rule)
    {
        if (IsBooleanField(rule.Field))
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            combo.Items.Add(new ComboBoxItem { Content = "True", Tag = "true" });
            combo.Items.Add(new ComboBoxItem { Content = "False", Tag = "false" });
            combo.SelectedIndex = rule.Value is false || string.Equals(rule.Value?.ToString(), "false", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            combo.SelectionChanged += (_, e) =>
            {
                rule.Value = SelectedTag(combo) == "true";
                Filter_Changed(combo, e);
            };
            return combo;
        }

        var box = new TextBox
        {
            Text = rule.Value?.ToString() ?? "",
            PlaceholderText = rule.Op == "in_last" ? "e.g. 30d, 2w" : "Value",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (IsNumberField(rule.Field)) box.InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) } };
        box.TextChanged += (_, e) =>
        {
            rule.Value = IsNumberField(rule.Field) && double.TryParse(box.Text, out var number) ? number : box.Text.Trim();
            Filter_Changed(box, e);
        };
        return box;
    }

    private static string DefaultOperator(string field) => field switch
    {
        "rating_imdb" or "bitrate" => "gte",
        "added_at" or "release_date" => "in_last",
        _ => "is"
    };

    private static object DefaultAdvancedValue(string field) => IsBooleanField(field) ? true : IsNumberField(field) ? 0d : "";
    private static bool IsBooleanField(string field) => field is "watched" or "favorited" or "in_watchlist" or "in_progress" or "hdr" or "dolby_vision";
    private static bool IsNumberField(string field) => field is "year" or "rating_imdb" or "bitrate";

    private List<QueryRule> BuildAdvancedRules() => _advancedRules
        .Where(rule => rule.Value is bool || !string.IsNullOrWhiteSpace(rule.Value?.ToString()))
        .Select(rule => new QueryRule { Field = rule.Field, Op = rule.Op, Value = rule.Value })
        .ToList();

    private async void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        if (_queryFilters != null) { ClearQueryFilters(); await LoadAsync(true); return; }
        _initializing = true;
        if (_advancedMode)
        {
            _advancedRules.Clear();
            AdvancedMatchCombo.SelectedIndex = 0;
            BuildAdvancedRulesUi();
            _initializing = false;
            UpdateFilterCount();
            await LoadAsync(true);
            return;
        }
        if (_fixedLibraryId is not > 0) LibraryCombo.SelectedIndex = 0;
        GenreCombo.SelectedIndex = 0;
        RatingCombo.SelectedIndex = 0;
        ResolutionCombo.SelectedIndex = 0;
        CountryCombo.SelectedIndex = 0;
        StudioCombo.SelectedIndex = 0;
        NetworkCombo.SelectedIndex = 0;
        OriginalLanguageCombo.SelectedIndex = 0;
        AudioLanguageCombo.SelectedIndex = 0;
        AuthorCombo.SelectedIndex = 0;
        NarratorCombo.SelectedIndex = 0;
        SeriesCombo.SelectedIndex = 0;
        MinimumRatingCombo.SelectedIndex = 0;
        StatusCombo.SelectedIndex = 0;
        WatchStatusCombo.SelectedIndex = 0;
        AddedInLastCombo.SelectedIndex = 0;
        ReleasedInLastCombo.SelectedIndex = 0;
        YearFromBox.Text = "";
        YearToBox.Text = "";
        ActorBox.Text = "";
        DirectorBox.Text = "";
        WriterBox.Text = "";
        ProducerBox.Text = "";
        FourKCheckBox.IsChecked = false;
        HdrCheckBox.IsChecked = false;
        DolbyVisionCheckBox.IsChecked = false;
        _initializing = false;
        UpdateFilterCount();
        await LoadAsync(true);
    }

    private void UpdateFilterCount()
    {
        if (_queryFilters != null) { UpdateQueryFilterChips(); return; }
        if (_advancedMode)
        {
            var advancedCount = BuildAdvancedRules().Count;
            FilterCountText.Text = advancedCount.ToString();
            FilterCountBadge.Visibility = advancedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        var count = 0;
        if (_fixedLibraryId is not > 0 && SelectedLibrary() is > 0) count++;
        if (SelectedTag(GenreCombo) != null) count++;
        if (SelectedTag(RatingCombo) != null) count++;
        if (SelectedTag(ResolutionCombo) != null) count++;
        if (SelectedTag(CountryCombo) != null) count++;
        if (SelectedTag(StudioCombo) != null) count++;
        if (SelectedTag(NetworkCombo) != null) count++;
        if (SelectedTag(OriginalLanguageCombo) != null) count++;
        if (SelectedTag(AudioLanguageCombo) != null) count++;
        if (SelectedTag(MinimumRatingCombo) != null) count++;
        if (TextOrNull(YearFromBox) != null || TextOrNull(YearToBox) != null) count++;
        if (TextOrNull(ActorBox) != null || TextOrNull(DirectorBox) != null || TextOrNull(WriterBox) != null || TextOrNull(ProducerBox) != null) count++;
        if (SelectedTag(StatusCombo) != null) count++;
        if (SelectedTag(WatchStatusCombo) != null) count++;
        if (SelectedTag(AddedInLastCombo) != null) count++;
        if (SelectedTag(ReleasedInLastCombo) != null) count++;
        if (SelectedTag(AuthorCombo) != null || SelectedTag(NarratorCombo) != null || SelectedTag(SeriesCombo) != null) count++;
        if (FourKCheckBox.IsChecked == true) count++;
        if (HdrCheckBox.IsChecked == true) count++;
        if (DolbyVisionCheckBox.IsChecked == true) count++;
        FilterCountText.Text = count.ToString();
        FilterCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private int? SelectedLibrary() => (LibraryCombo.SelectedItem as ComboBoxItem)?.Tag as int?;
    private static string? SelectedTag(ComboBox combo) { var value = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString(); return string.IsNullOrWhiteSpace(value) ? null : value; }
    private static string? TextOrNull(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    private List<QueryRule> BuildGuidedRules()
    {
        var rules = new List<QueryRule>();
        AddTextRule(rules, "original_language", SelectedTag(OriginalLanguageCombo));
        AddTextRule(rules, "network", SelectedTag(NetworkCombo));
        AddTextRule(rules, "actor", TextOrNull(ActorBox));
        AddTextRule(rules, "director", TextOrNull(DirectorBox));
        AddTextRule(rules, "writer", TextOrNull(WriterBox));
        AddTextRule(rules, "producer", TextOrNull(ProducerBox));
        AddTextRule(rules, "author", SelectedTag(AuthorCombo));
        AddTextRule(rules, "narrator", SelectedTag(NarratorCombo));
        AddTextRule(rules, "series", SelectedTag(SeriesCombo));
        AddTextRule(rules, "status", SelectedTag(StatusCombo));

        if (double.TryParse(SelectedTag(MinimumRatingCombo), out var rating))
            rules.Add(new QueryRule { Field = "rating_imdb", Op = "gte", Value = rating });

        switch (SelectedTag(WatchStatusCombo))
        {
            case "watched":
                rules.Add(new QueryRule { Field = "watched", Op = "is", Value = true });
                break;
            case "in_progress":
                rules.Add(new QueryRule { Field = "in_progress", Op = "is", Value = true });
                break;
            case "unwatched":
                rules.Add(new QueryRule { Field = "watched", Op = "is", Value = false });
                rules.Add(new QueryRule { Field = "in_progress", Op = "is", Value = false });
                break;
        }

        AddTextRule(rules, "added_at", SelectedTag(AddedInLastCombo), "in_last");
        AddTextRule(rules, "release_date", SelectedTag(ReleasedInLastCombo), "in_last");
        if (FourKCheckBox.IsChecked == true)
            rules.Add(new QueryRule { Field = "resolution", Op = "is", Value = "2160p" });
        if (HdrCheckBox.IsChecked == true)
            rules.Add(new QueryRule { Field = "hdr", Op = "is", Value = true });
        if (DolbyVisionCheckBox.IsChecked == true)
            rules.Add(new QueryRule { Field = "dolby_vision", Op = "is", Value = true });
        return rules;
    }

    private static void AddTextRule(List<QueryRule> rules, string field, string? value, string op = "is")
    {
        if (!string.IsNullOrWhiteSpace(value))
            rules.Add(new QueryRule { Field = field, Op = op, Value = value });
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var windowWidth = e.NewSize.Width;
        if (windowWidth <= 0) return;
        var width = Math.Min(windowWidth, 1400);
        PageShell.Width = width;
        PageShell.HorizontalAlignment = HorizontalAlignment.Center;
        var gutter = width < 640 ? 16d : width < 1024 ? 24d : 40d;
        HeaderGrid.Margin = new Thickness(gutter, width < 640 ? 16 : 24, gutter, 24);
        FilterPanel.Margin = new Thickness(gutter, 0, gutter, 18);
        LockedFiltersPanel.Margin = new Thickness(gutter, 0, gutter, 18);
        HistoryActions.Margin = new Thickness(gutter, 0, gutter, 18);
        CatalogScrollViewer.Padding = new Thickness(gutter, 0, gutter, 28);
        WatchlistTabs.Margin = new Thickness(gutter, 0, gutter, 24);
        ExternalWatchlistScroller.Padding = new Thickness(gutter, 0, gutter, 28);

        PageTitleText.FontSize = width < 640 ? 32 : width < 1024 ? 44 : 56;
        PageTitleText.LineHeight = PageTitleText.FontSize * .95;
        PageSubtitleText.FontSize = width < 640 ? 14 : 16;
        PageSubtitleText.LineHeight = width < 640 ? 20 : 24;
        var compactHeader = width < 640;
        HeaderGrid.RowSpacing = compactHeader && CountPanel.Visibility == Visibility.Visible ? 12 : 0;
        Grid.SetRow(CountPanel, compactHeader ? 1 : 0);
        Grid.SetColumn(CountPanel, compactHeader ? 0 : 1);
        CountPanel.HorizontalAlignment = compactHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        CountText.FontSize = compactHeader ? 20 : 30;

        var compactHistory = width < 700;
        Grid.SetRow(HistoryButtonsPanel, compactHistory ? 1 : 0);
        Grid.SetColumn(HistoryButtonsPanel, compactHistory ? 0 : 1);
        Grid.SetColumnSpan(HistoryButtonsPanel, compactHistory ? 2 : 1);

        ApplyCatalogCardLayout(width, gutter);
        if (_source == "watchlist") ApplyExternalTitleLayout(width);
    }

    private void ApplyCatalogCardLayout(double width, double gutter)
    {
        var contentWidth = Math.Max(280, width - gutter * 2);
        var columns = _uiCustomizationService.GetPosterColumnCount(width);
        var gap = _uiCustomizationService.CardPresentation.PosterSize == "large" ? 16 : 12;
        CatalogGridLayout.MinColumnSpacing = CatalogGridLayout.MinRowSpacing = gap;
        CatalogLoadingGridLayout.MinColumnSpacing = CatalogLoadingGridLayout.MinRowSpacing = gap;
        _catalogCardWidth = Math.Max(96, Math.Floor((contentWidth - (columns - 1) * gap) / columns));
        CatalogGridLayout.MinItemWidth = _catalogCardWidth;
        CatalogGridLayout.MinItemHeight = _catalogCardWidth * 1.5 + _uiCustomizationService.CardCaptionHeight;
        CatalogLoadingGridLayout.MinItemWidth = _catalogCardWidth;
        CatalogLoadingGridLayout.MinItemHeight = _catalogCardWidth * 1.5 + _uiCustomizationService.CardCaptionHeight;
        for (var i = 0; i < _items.Count; i++)
            if (ItemsRepeater.TryGetElement(i) is PosterCard card)
                card.SetCatalogGridLayout(_catalogCardWidth);
        for (var i = 0; i < 24; i++)
        {
            if (CatalogLoadingRepeater.TryGetElement(i) is not StackPanel skeleton) continue;
            skeleton.Width = _catalogCardWidth;
            if (skeleton.Children.FirstOrDefault() is FrameworkElement poster)
                poster.Height = _catalogCardWidth * 1.5;
        }
    }
}
