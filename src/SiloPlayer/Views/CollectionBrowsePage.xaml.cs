using System.Text.Json;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

/// <summary>
/// Browse view for a collection (user-defined or library-discovered).
/// B40 + B41: before this, clicking a collection card opened the editor or
/// mutated LibraryPage in place. This page mirrors the webui
/// <c>/catalog?source=user_collection&amp;collection_id=X</c> route — a
/// standalone grid of items with a back button.
/// </summary>
public sealed partial class CollectionBrowsePage : Page
{
    private readonly CatalogApi _catalogApi;

    /// <summary>Parameter passed via NavigationService.Navigate.</summary>
    public sealed class NavArgs
    {
        public string CollectionId { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Subtitle { get; set; }
        /// <summary>true = user_collection, false = library_collection.</summary>
        public bool IsUserCollection { get; set; }
        /// <summary>Library ID for library_collection pins; null for user_collection.</summary>
        public int? LibraryId { get; set; }
    }

    private NavArgs? _currentArgs;
    private readonly ObservableCollection<MediaItem> _items = [];
    private CancellationTokenSource? _loadCts;
    private string? _sort;
    private string? _order;
    private string? _mediaScope;
    private int _total;
    private bool _hasMore;
    private bool _isLoadingMore;
    private bool _suppressSortEvents = true;
    private double _catalogCardWidth = 178;
    private const int PageSize = 60;

    public CollectionBrowsePage()
    {
        _catalogApi = App.Services.GetRequiredService<CatalogApi>();
        this.InitializeComponent();
        PosterRepeater.ItemsSource = _items;
        LoadingPosterRepeater.ItemsSource = Enumerable.Range(0, 24).ToArray();
        SortCombo.SelectedIndex = 0;
        OrderCombo.IsEnabled = false;
        _suppressSortEvents = false;
        SizeChanged += CollectionBrowsePage_SizeChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not NavArgs args) return;

        _currentArgs = args;
        TitleText.Text = args.Title;
        SubtitleText.Text = "Refine the archive by type, era, rating, or genre.";

        // Pinning is exposed on the library collection cards, matching the
        // WebUI. The catalog result header itself has no extra pin control.
        PinButton.Visibility = Visibility.Collapsed;

        await LoadFirstPageAsync();
    }

    private async Task LoadFirstPageAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;
        _items.Clear();
        _total = 0;
        _hasMore = false;
        ShowLoading();
        try
        {
            var response = await LoadPageAsync(0, loadCts.Token);
            foreach (var item in response.Items) _items.Add(item);
            _total = response.Total > 0 ? response.Total : _items.Count;
            _hasMore = response.HasMore || _items.Count < _total;
            UpdateCountDisplay();
            if (_items.Count == 0) ShowEmpty();
            else ShowContent();
        }
        catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private Task<CatalogResponse> LoadPageAsync(int offset, CancellationToken ct)
    {
        if (_currentArgs is not { } args)
            throw new InvalidOperationException("Collection navigation details are unavailable.");
        return _catalogApi.GetCatalogAsync(
            libraryId: null,
            sort: _sort,
            order: _order,
            type: _mediaScope,
            yearMin: EmptyToNull(YearMinBox.Text),
            yearMax: EmptyToNull(YearMaxBox.Text),
            studio: EmptyToNull(StudioBox.Text),
            contentRating: EmptyToNull(ContentRatingBox.Text),
            country: EmptyToNull(CountryBox.Text),
            source: args.IsUserCollection ? "user_collection" : "library_collection",
            collectionId: args.CollectionId,
            extraRules: BuildExtraRules(),
            limit: PageSize,
            offset: offset,
            ct: ct);
    }

    private async Task LoadMoreAsync()
    {
        if (_isLoadingMore || !_hasMore || _currentArgs == null) return;
        _isLoadingMore = true;
        LoadMoreRing.Visibility = Visibility.Visible;
        LoadMoreRing.IsActive = true;
        try
        {
            var response = await LoadPageAsync(_items.Count, _loadCts?.Token ?? CancellationToken.None);
            foreach (var item in response.Items) _items.Add(item);
            if (response.Total > 0) _total = response.Total;
            _hasMore = response.HasMore || (_items.Count < _total && response.Items.Count > 0);
            UpdateCountDisplay();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Could not load more items: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
        finally
        {
            _isLoadingMore = false;
            LoadMoreRing.IsActive = false;
            LoadMoreRing.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateCountDisplay()
    {
        var count = _total > 0 ? _total : _items.Count;
        ItemCountText.Text = count.ToString("N0");
        ItemCountLabel.Text = count == 1 ? "RESULT" : "RESULTS";
        CountPanel.Visibility = ActualWidth >= 720 ? Visibility.Visible : Visibility.Collapsed;
        LoadedCountText.Text = _hasMore
            ? $"Showing {_items.Count:N0} of {count:N0}"
            : $"{count:N0} {(count == 1 ? "item" : "items")}";
    }

    private async void ContentScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (ContentScroll.ScrollableHeight - ContentScroll.VerticalOffset < 900)
            await LoadMoreAsync();
    }

    private async void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSortEvents || _currentArgs == null) return;
        _sort = SortCombo.SelectedItem is ComboBoxItem { Tag: string value } && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
        _order = _sort is "title" or "content_rating" or "author" or "narrator" or "series" ? "asc" : "desc";
        OrderCombo.IsEnabled = _sort != null;
        _suppressSortEvents = true;
        OrderCombo.SelectedIndex = _order == "asc" ? 1 : 0;
        _suppressSortEvents = false;
        await LoadFirstPageAsync();
    }

    private async void OrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSortEvents || _sort == null || _currentArgs == null) return;
        _order = OrderCombo.SelectedItem is ComboBoxItem { Tag: string order } ? order : "desc";
        await LoadFirstPageAsync();
    }

    private async void MediaScopeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSortEvents || _currentArgs == null) return;
        _mediaScope = MediaScopeCombo.SelectedItem is ComboBoxItem { Tag: string scope } && !string.IsNullOrWhiteSpace(scope)
            ? scope
            : null;
        await LoadFirstPageAsync();
    }

    private void FiltersButton_Click(object sender, RoutedEventArgs e) => FiltersSheet.IsOpen = true;

    private async void ApplyFilters_Click(object sender, RoutedEventArgs e)
    {
        FiltersSheet.IsOpen = false;
        UpdateActiveFilterBadge();
        await LoadFirstPageAsync();
    }

    private async void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        GenresBox.Text = "";
        YearMinBox.Text = "";
        YearMaxBox.Text = "";
        MinimumRatingBox.Text = "";
        ContentRatingBox.Text = "";
        OriginalLanguageBox.Text = "";
        StudioBox.Text = "";
        CountryBox.Text = "";
        FourKToggle.IsChecked = false;
        HdrToggle.IsChecked = false;
        DolbyVisionToggle.IsChecked = false;
        UpdateActiveFilterBadge();
        await LoadFirstPageAsync();
    }

    private List<QueryRule> BuildExtraRules()
    {
        var rules = GenresBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(genre => new QueryRule { Field = "genre", Op = "is", Value = genre })
            .ToList();
        AddTextRule("rating_imdb", "gte", MinimumRatingBox.Text);
        AddTextRule("original_language", "is", OriginalLanguageBox.Text);
        if (FourKToggle.IsChecked == true) rules.Add(new QueryRule { Field = "resolution", Op = "is", Value = "4k" });
        if (HdrToggle.IsChecked == true) rules.Add(new QueryRule { Field = "hdr", Op = "is", Value = true });
        if (DolbyVisionToggle.IsChecked == true) rules.Add(new QueryRule { Field = "dolby_vision", Op = "is", Value = true });
        return rules;

        void AddTextRule(string field, string op, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                rules.Add(new QueryRule { Field = field, Op = op, Value = value.Trim() });
        }
    }

    private void UpdateActiveFilterBadge()
    {
        var count = BuildExtraRules().Count;
        if (!string.IsNullOrWhiteSpace(YearMinBox.Text) || !string.IsNullOrWhiteSpace(YearMaxBox.Text)) count++;
        if (!string.IsNullOrWhiteSpace(ContentRatingBox.Text)) count++;
        if (!string.IsNullOrWhiteSpace(StudioBox.Text)) count++;
        if (!string.IsNullOrWhiteSpace(CountryBox.Text)) count++;
        ActiveFilterCountText.Text = count.ToString();
        ActiveFilterCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string? EmptyToNull(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void CollectionBrowsePage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 720;
        var gutter = e.NewSize.Width < 640 ? 16 : e.NewSize.Width < 1024 ? 24 : 40;
        CollectionHeaderGrid.Margin = new Thickness(gutter, e.NewSize.Width < 640 ? 16 : 24, gutter, 16);
        CollectionToolbar.Margin = new Thickness(gutter, 0, gutter, 14);
        ContentScroll.Padding = new Thickness(gutter, 0, gutter, 24);
        LoadingSkeleton.Padding = new Thickness(gutter, 0, gutter, 24);
        CountPanel.Visibility = !compact ? Visibility.Visible : Visibility.Collapsed;
        ReflowToolbar(e.NewSize.Width);
        UpdateCatalogGridLayout(e.NewSize.Width, gutter);
    }

    private void ReflowToolbar(double width)
    {
        var narrow = width < 560;
        var compact = width < 760;
        foreach (var control in new FrameworkElement[] { MediaScopeCombo, SortCombo, OrderCombo, FiltersButton, LoadedCountText })
        {
            Grid.SetRow(control, 0);
            Grid.SetColumnSpan(control, 1);
        }

        if (!compact)
        {
            Grid.SetColumn(MediaScopeCombo, 0);
            Grid.SetColumn(SortCombo, 1);
            Grid.SetColumn(OrderCombo, 2);
            Grid.SetColumn(FiltersButton, 3);
            Grid.SetColumn(LoadedCountText, 4);
            return;
        }

        MediaScopeCombo.MinWidth = narrow ? 132 : 150;
        SortCombo.MinWidth = narrow ? 154 : 180;
        Grid.SetColumn(MediaScopeCombo, 0);
        Grid.SetColumn(SortCombo, 1);
        Grid.SetColumn(OrderCombo, narrow ? 0 : 2);
        Grid.SetRow(OrderCombo, narrow ? 1 : 0);
        Grid.SetColumn(FiltersButton, narrow ? 1 : 0);
        Grid.SetRow(FiltersButton, 1);
        Grid.SetColumn(LoadedCountText, narrow ? 0 : 1);
        Grid.SetRow(LoadedCountText, narrow ? 2 : 1);
        Grid.SetColumnSpan(LoadedCountText, 4);
    }

    private void UpdateCatalogGridLayout(double viewportWidth, double gutter)
    {
        var columns = viewportWidth >= 1280 ? 8
            : viewportWidth >= 1024 ? 7
            : viewportWidth >= 768 ? 5
            : viewportWidth >= 640 ? 4
            : 3;
        var contentWidth = Math.Max(320, Math.Min(1400, viewportWidth - (gutter * 2)));
        _catalogCardWidth = Math.Max(96, (contentWidth - (12 * (columns - 1))) / columns);
        PosterGridLayout.MaximumRowsOrColumns = columns;
        PosterGridLayout.MinItemWidth = _catalogCardWidth;
        PosterGridLayout.MinItemHeight = (_catalogCardWidth * 1.5) + 56;

        for (var index = 0; index < _items.Count; index++)
            if (PosterRepeater.TryGetElement(index) is SiloPlayer.Controls.PosterCard card)
                card.SetCatalogGridLayout(_catalogCardWidth);
    }

    private void PosterRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is SiloPlayer.Controls.PosterCard card)
            card.SetCatalogGridLayout(_catalogCardWidth);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
        base.OnNavigatedFrom(e);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (nav.Frame?.CanGoBack == true)
        {
            nav.Frame.GoBack();
        }
        else
        {
            nav.Navigate<CollectionsPage>();
        }
    }

    private void ShowLoading()
    {
        LoadingSkeleton.Visibility = Visibility.Visible;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowContent()
    {
        LoadingSkeleton.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowEmpty()
    {
        LoadingSkeleton.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Visible;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        LoadingSkeleton.Visibility = Visibility.Collapsed;
        ContentScroll.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        ErrorText.Text = $"Could not load collection: {message}";
        ErrorText.Visibility = Visibility.Visible;
    }

    // ===== Sidebar pins (webui parity: sidebar_pins JSON user setting) =====
    //
    // Shape: { "<libraryId>": [ { "type": "collection", "id": "<collectionId>", "label": "..." }, ... ] }
    // Stored as a JSON string under the single settings key "sidebar_pins".

    private const string SidebarPinsKey = "sidebar_pins";
    private bool _isPinned;

    private async Task SyncPinStateAsync()
    {
        if (_currentArgs?.LibraryId == null) return;
        var libraryKey = _currentArgs.LibraryId.Value.ToString();
        try
        {
            var settingsApi = App.Services.GetRequiredService<SettingsApi>();
            var entry = await settingsApi.GetSettingAsync(SidebarPinsKey);
            _isPinned = IsPinnedInMap(entry.Value, libraryKey, _currentArgs.CollectionId);
        }
        catch { _isPinned = false; }
        UpdatePinButtonVisual();
    }

    private async void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentArgs?.LibraryId == null) return;
        var libraryKey = _currentArgs.LibraryId.Value.ToString();

        try
        {
            PinButton.IsEnabled = false;
            var settingsApi = App.Services.GetRequiredService<SettingsApi>();
            var entry = await settingsApi.GetSettingAsync(SidebarPinsKey);
            var map = ParsePinsMap(entry.Value);

            var list = map.TryGetValue(libraryKey, out var existing) ? existing : [];
            var idx = list.FindIndex(p =>
                string.Equals(p.Type, "collection", StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Id, _currentArgs.CollectionId, StringComparison.Ordinal));

            if (idx >= 0)
            {
                list.RemoveAt(idx);
                if (list.Count == 0) map.Remove(libraryKey);
                else map[libraryKey] = list;
                _isPinned = false;
            }
            else
            {
                list.Add(new PinEntry { Type = "collection", Id = _currentArgs.CollectionId, Label = _currentArgs.Title });
                map[libraryKey] = list;
                _isPinned = true;
            }

            await settingsApi.PutSettingAsync(SidebarPinsKey, SerializePinsMap(map));
            UpdatePinButtonVisual();

            // Tell the main window to refresh the sidebar so the pin appears /
            // disappears immediately.
            if (App.MainWindowInstance is MainWindow mw)
            {
                await mw.RefreshSidebarPinsAsync();
            }
        }
        catch { /* non-fatal; button visual just won't update */ }
        finally
        {
            PinButton.IsEnabled = true;
        }
    }

    private void UpdatePinButtonVisual()
    {
        // MDL2: E840 = Pinned (outline) — using it both states with label change
        // to keep the visual compact. Active state uses accent foreground.
        if (_isPinned)
        {
            PinLabel.Text = "Pinned";
            PinIcon.Glyph = "\uE841"; // Pinned (filled)
            PinIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
            PinLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentBrush"];
        }
        else
        {
            PinLabel.Text = "Pin to sidebar";
            PinIcon.Glyph = "\uE840"; // Pinned (outline)
            PinIcon.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
            PinLabel.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"];
        }
    }

    // ----- JSON helpers for sidebar_pins setting -----

    private sealed class PinEntry
    {
        public string Type { get; set; } = "";
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
    }

    private static bool IsPinnedInMap(string? rawJson, string libraryKey, string collectionId)
    {
        var map = ParsePinsMap(rawJson);
        if (!map.TryGetValue(libraryKey, out var list)) return false;
        return list.Any(p =>
            string.Equals(p.Type, "collection", StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Id, collectionId, StringComparison.Ordinal));
    }

    private static Dictionary<string, List<PinEntry>> ParsePinsMap(string? rawJson)
    {
        var result = new Dictionary<string, List<PinEntry>>();
        if (string.IsNullOrWhiteSpace(rawJson)) return result;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;
                var entries = new List<PinEntry>();
                foreach (var el in prop.Value.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    entries.Add(new PinEntry
                    {
                        Type = el.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
                        Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                        Label = el.TryGetProperty("label", out var lb) ? lb.GetString() ?? "" : "",
                    });
                }
                if (entries.Count > 0) result[prop.Name] = entries;
            }
        }
        catch { /* malformed — treat as empty map */ }
        return result;
    }

    private static string SerializePinsMap(Dictionary<string, List<PinEntry>> map)
    {
        // Match web shape exactly: camelCase type/id/label keys.
        var serializable = map.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(p => new Dictionary<string, string>
            {
                ["type"] = p.Type,
                ["id"] = p.Id,
                ["label"] = p.Label,
            }).ToList());
        return JsonSerializer.Serialize(serializable);
    }
}
