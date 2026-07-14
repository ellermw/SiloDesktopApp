using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Controls;

namespace SiloPlayer.Views;

public sealed record CatalogNavigation(
    string Source = "library",
    string? Title = null,
    string? Subtitle = null,
    string? Scope = null,
    string? SectionId = null,
    int? LibraryId = null);

public sealed partial class CatalogPage : Page
{
    private readonly CatalogApi _api = App.Services.GetRequiredService<CatalogApi>();
    private readonly ObservableCollection<MediaItem> _items = [];
    private CancellationTokenSource? _loadCts;
    private DispatcherTimer? _debounce;
    private int _offset;
    private bool _hasMore;
    private bool _loading;
    private bool _initializing = true;
    private long _loadGeneration;
    private bool _selectionMode;
    private string _source = "library";
    private string? _scope;
    private string? _sectionId;
    private int? _fixedLibraryId;
    private string? _snapshot;
    private readonly HashSet<string> _selectedIds = new(StringComparer.OrdinalIgnoreCase);
    private const int PageSize = 60;

    public CatalogPage() { InitializeComponent(); ItemsRepeater.ItemsSource = _items; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is CatalogNavigation navigation)
        {
            _source = navigation.Source;
            _scope = navigation.Scope;
            _sectionId = navigation.SectionId;
            _fixedLibraryId = navigation.LibraryId;
            PageTitleText.Text = navigation.Title ?? (_source == "history" ? "History" : "Catalog");
            PageSubtitleText.Text = navigation.Subtitle ?? (_source == "history"
                ? "Everything you've recently watched."
                : "Refine the archive by library, type, era, rating, or genre.");
        }

        if (_source is "history" or "favorites" or "watchlist")
        {
            SortCombo.Items.Insert(0, new ComboBoxItem
            {
                Content = _source == "watchlist" ? "List Order" : "Date Added",
                Tag = ""
            });
            SortCombo.SelectedIndex = 0;
        }
        if (_source == "history") HistoryActions.Visibility = Visibility.Visible;
        if (_source == "section")
        {
            // A section is a stored server recipe. The current WebUI deliberately
            // suppresses the catalog overlay so local filters cannot silently
            // change the recipe being explored.
            FilterPanel.Visibility = Visibility.Collapsed;
            LockedFiltersPanel.Visibility = Visibility.Visible;
        }
        await InitializeFiltersAsync();
        _initializing = false;
        UpdateFilterCount();
        await LoadAsync(true);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Interlocked.Increment(ref _loadGeneration);
        _loadCts?.Cancel();
        _debounce?.Stop();
        base.OnNavigatedFrom(e);
    }

    private async Task InitializeFiltersAsync()
    {
        var librariesTask = _api.GetLibrariesAsync();
        var filtersTask = _api.GetFiltersAsync(
            libraryId: _fixedLibraryId,
            source: _source == "library" ? null : _source,
            scope: _scope,
            sectionId: _sectionId);
        await Task.WhenAll(librariesTask, filtersTask);
        LibraryCombo.Items.Add(new ComboBoxItem { Content = "All libraries", Tag = (int?)null });
        foreach (var library in librariesTask.Result) LibraryCombo.Items.Add(new ComboBoxItem { Content = library.Name, Tag = (int?)library.Id });
        var fixedLibraryIndex = _fixedLibraryId is > 0
            ? librariesTask.Result.FindIndex(l => l.Id == _fixedLibraryId.Value) + 1
            : 0;
        LibraryCombo.SelectedIndex = Math.Max(0, fixedLibraryIndex);
        LibraryCombo.IsEnabled = _fixedLibraryId is not > 0;
        Fill(GenreCombo, "All genres", filtersTask.Result.Genres);
        Fill(RatingCombo, "All ratings", filtersTask.Result.ContentRatings);
        Fill(ResolutionCombo, "All resolutions", filtersTask.Result.Resolutions);
        Fill(CountryCombo, "All countries", filtersTask.Result.Countries);
    }

    private static void Fill(ComboBox combo, string all, IEnumerable<string> values)
    {
        combo.Items.Add(new ComboBoxItem { Content = all, Tag = "" });
        foreach (var value in values) combo.Items.Add(new ComboBoxItem { Content = value, Tag = value });
        combo.SelectedIndex = 0;
    }

    private async Task LoadAsync(bool reset)
    {
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
        _loading = true; LoadingRing.IsActive = true; LoadingRing.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var sort = SelectedTag(SortCombo);
            var isSection = _source == "section";
            var response = await _api.GetCatalogAsync(
                isSection ? _fixedLibraryId : SelectedLibrary(),
                sort: isSection ? null : sort,
                order: isSection || sort == null ? null : SelectedTag(OrderCombo),
                genre: isSection ? null : SelectedTag(GenreCombo),
                contentRating: isSection ? null : SelectedTag(RatingCombo),
                country: isSection ? null : SelectedTag(CountryCombo),
                resolution: isSection ? null : SelectedTag(ResolutionCombo),
                q: isSection ? null : QueryBox.Text.Trim(),
                type: isSection ? null : SelectedTag(TypeCombo),
                limit: PageSize,
                offset: _offset,
                snapshot: _snapshot,
                source: _source == "library" ? null : _source,
                scope: _scope,
                sectionId: _sectionId,
                ct: _loadCts.Token);
            if (generation != Volatile.Read(ref _loadGeneration)) return;
            foreach (var item in response.Items) _items.Add(item);
            _snapshot = response.Snapshot ?? _snapshot;
            _offset += response.Items.Count;
            _hasMore = response.HasMore || _offset < response.Total;
            CountText.Text = response.Total.ToString("N0");
            ResultNounText.Text = response.Total == 1 ? "RESULT" : "RESULTS";
            CountPanel.Visibility = response.TotalExact ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LoadMoreButton.Visibility = _hasMore ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                ErrorText.Text = ex.Message;
                ErrorText.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                _loading = false;
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void Filter_Changed(object sender, object e)
    {
        if (_initializing) return;
        UpdateFilterCount();
        _debounce?.Stop();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ReferenceEquals(sender, QueryBox) ? 300 : 40) };
        _debounce.Tick += async (_, _) => { _debounce?.Stop(); await LoadAsync(true); };
        _debounce.Start();
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (OrderCombo == null) return;
        var sort = SelectedTag(SortCombo);
        OrderCombo.Visibility = sort == null ? Visibility.Collapsed : Visibility.Visible;
        if (!_initializing && sort != null)
        {
            var ascendingByDefault = sort is "title" or "content_rating" or "author" or "narrator" or "series";
            OrderCombo.SelectedIndex = ascendingByDefault ? 1 : 0;
        }
        Filter_Changed(sender, e);
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e) { if (_hasMore) await LoadAsync(false); }
    private async void CatalogScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) { if (sender is ScrollViewer scroll && !e.IsIntermediate && _hasMore && scroll.ScrollableHeight - scroll.VerticalOffset < 900) await LoadAsync(false); }

    private void ItemsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not PosterCard card || card.MediaItem is not { } item) return;
        card.SelectionToggled -= PosterCard_SelectionToggled;
        card.SelectionMode = _selectionMode;
        card.IsSelected = _selectedIds.Contains(item.ContentId);
        card.SelectionToggled += PosterCard_SelectionToggled;
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

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = selected.Count > 1
                ? "Remove selected watch data?"
                : selected[0].Type is "series" or "season" ? "Remove show watch data?" : "Remove watch data?",
            Content = selected.Count > 1
                ? $"{selected.Count} selected items will have their watch history, watched status, and resume progress cleared for this profile."
                : selected[0].Type is "series" or "season"
                    ? "This clears the show's watch history, watched episodes, and resume progress for this profile."
                    : "This clears the item's watch history, watched status, and resume progress for this profile.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        RemoveSelectedButton.IsEnabled = false;
        try
        {
            await _api.RemoveHistoryAsync(selected.Select(item => new HistoryRemovalTarget
            {
                ContentId = item.ContentId,
                Scope = item.Type is "series" or "season" ? "show" : "item"
            }));
            _selectionMode = false;
            _selectedIds.Clear();
            await LoadAsync(true);
        }
        finally
        {
            RemoveSelectedButton.IsEnabled = true;
            RefreshRealizedSelection();
            UpdateSelectionUi();
        }
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

    private async void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _initializing = true;
        if (_fixedLibraryId is not > 0) LibraryCombo.SelectedIndex = 0;
        GenreCombo.SelectedIndex = 0;
        RatingCombo.SelectedIndex = 0;
        ResolutionCombo.SelectedIndex = 0;
        CountryCombo.SelectedIndex = 0;
        _initializing = false;
        UpdateFilterCount();
        await LoadAsync(true);
    }

    private void UpdateFilterCount()
    {
        var count = 0;
        if (_fixedLibraryId is not > 0 && SelectedLibrary() is > 0) count++;
        if (SelectedTag(GenreCombo) != null) count++;
        if (SelectedTag(RatingCombo) != null) count++;
        if (SelectedTag(ResolutionCombo) != null) count++;
        if (SelectedTag(CountryCombo) != null) count++;
        FilterCountText.Text = count.ToString();
        FilterCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private int? SelectedLibrary() => (LibraryCombo.SelectedItem as ComboBoxItem)?.Tag as int?;
    private static string? SelectedTag(ComboBox combo) { var value = (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString(); return string.IsNullOrWhiteSpace(value) ? null : value; }
}
