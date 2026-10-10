using System.Collections.Specialized;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class CatalogPage
{
    private WatchlistViewModel? _watchlistTitles;
    private bool _externalWatchlist;
    private double _externalTitleWidth = 180;
    private bool _watchlistAttached;

    private void InitializeWatchlistTitles()
    {
        if (_source != "watchlist") return;
        _watchlistTitles = App.Services.GetRequiredService<WatchlistViewModel>();
        ExternalTitlesRepeater.ItemsSource = _watchlistTitles.ExternalTitles;
        ExternalTitlesSkeleton.ItemsSource = Enumerable.Range(0, 6).ToArray();
        ExternalTitlesStateIcon.Content = WebUiIcon.Create("bookmark", 32);
        ExternalTitlesDiscoverIcon.Content = WebUiIcon.Create("compass", 16);
        _watchlistTitles.ExternalTitles.CollectionChanged += ExternalTitles_Changed;
        _watchlistAttached = true;
        ExternalWatchlistScroller.SizeChanged += ExternalWatchlist_SizeChanged;
        _ = LoadWatchlistTitlesAsync();
    }

    private void StopWatchlistTitles()
    {
        if (!_watchlistAttached || _watchlistTitles == null) return;
        _watchlistAttached = false;
        _watchlistTitles.CancelExternalLoad();
        _watchlistTitles.ExternalTitles.CollectionChanged -= ExternalTitles_Changed;
        // This collection is shared across successive catalog pages. Release
        // the old native repeater's vector subscription before another page
        // clears/reloads it, so an unloaded view cannot handle new changes.
        ExternalTitlesRepeater.ItemsSource = null;
        ExternalWatchlistScroller.SizeChanged -= ExternalWatchlist_SizeChanged;
    }

    private async Task LoadWatchlistTitlesAsync()
    {
        if (_watchlistTitles == null) return;
        ExternalTitlesLoading.Visibility = Visibility.Collapsed; ExternalTitlesLoading.IsActive = true;
        ExternalTitlesSkeleton.Visibility = Visibility.Visible;
        ExternalTitlesState.Visibility = ExternalTitlesStatePanel.Visibility = Visibility.Collapsed;
        ExternalWatchlistHintPanel.Visibility = ExternalWatchlistHintGrid.Visibility = Visibility.Collapsed;
        ExternalTitlesRepeater.Visibility = Visibility.Collapsed;
        await _watchlistTitles.LoadExternalTitlesAsync();
        if (!_watchlistAttached) return;
        WatchlistTabs.Visibility = _watchlistTitles.ExternalTitlesSupported ? Visibility.Visible : Visibility.Collapsed;
        if (!_watchlistTitles.ExternalTitlesSupported) _externalWatchlist = false;
        ExternalTitlesLoading.IsActive = false; ExternalTitlesLoading.Visibility = Visibility.Collapsed;
        ExternalTitlesSkeleton.Visibility = Visibility.Collapsed;
        ExternalWatchlistHint.Text = "These titles aren't in the library yet, so they can't be played. " +
            (_watchlistTitles.WatchlistRequests
                ? "They've been requested for you. When one arrives, it moves to In your library and you get a notification."
                : "When one arrives, it moves to In your library.");
        UpdateWatchlistTitlesState(); UpdateWatchlistTabs();
    }

    private void ExternalTitles_Changed(object? sender, NotifyCollectionChangedEventArgs e)
        => DispatcherQueue.TryEnqueue(() => { if (_watchlistAttached) { UpdateWatchlistTitlesState(); UpdateWatchlistTabs(); } });

    private void UpdateWatchlistTitlesState()
    {
        if (_watchlistTitles == null) return;
        var error = _watchlistTitles.ExternalTitlesError != null;
        var empty = _watchlistTitles.ExternalTitles.Count == 0;
        ExternalTitlesState.Visibility = ExternalTitlesLoading.IsActive ? Visibility.Collapsed : error || empty ? Visibility.Visible : Visibility.Collapsed;
        ExternalTitlesStatePanel.Visibility = ExternalTitlesState.Visibility;
        var loaded = !ExternalTitlesLoading.IsActive && !error && !empty;
        ExternalWatchlistHintPanel.Visibility = ExternalWatchlistHintGrid.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
        ExternalTitlesRepeater.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
        ExternalTitlesStateText.Text = error ? "Could not load these titles." : "Nothing waiting for the library";
        ExternalTitlesStateIcon.Visibility = ExternalTitlesStateDescription.Visibility = !error && empty ? Visibility.Visible : Visibility.Collapsed;
        ExternalTitlesStateOutline.StrokeDashArray = error ? null : new DoubleCollection { 4, 4 };
        ExternalTitlesStateOutline.Visibility = Visibility.Visible;
        ExternalTitlesRetry.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        ExternalTitlesDiscover.Visibility = empty && !error ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateWatchlistTabs()
    {
        var count = _watchlistTitles?.ExternalTitles.Count ?? 0;
        ExternalTitleCountText.Text = count.ToString();
        ExternalTitleCountBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var attention = _watchlistTitles?.ExternalTitles.Any(t => t.Status is "needs_review" or "removed") == true;
        ExternalTitleAttention.Visibility = attention ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(ExternalWatchlistTab, $"Not in your library yet, {count} titles{(attention ? ", some need attention" : "")}");
        var accent = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        LibraryWatchlistTab.BorderBrush = _externalWatchlist ? transparent : accent;
        ExternalWatchlistTab.BorderBrush = _externalWatchlist ? accent : transparent;
        LibraryWatchlistTab.Foreground = (Brush)Application.Current.Resources[_externalWatchlist ? "SecondaryTextBrush" : "PrimaryTextBrush"];
        ExternalWatchlistTab.Foreground = (Brush)Application.Current.Resources[_externalWatchlist ? "PrimaryTextBrush" : "SecondaryTextBrush"];
        LibraryWatchlistTab.Background = transparent; ExternalWatchlistTab.Background = transparent;
        ExternalWatchlistScroller.Visibility = _externalWatchlist ? Visibility.Visible : Visibility.Collapsed;
        CatalogScrollViewer.Visibility = _externalWatchlist ? Visibility.Collapsed : Visibility.Visible;
        FilterPanel.Visibility = _externalWatchlist ? Visibility.Collapsed : Visibility.Visible;
        CountPanel.Visibility = _externalWatchlist ? Visibility.Collapsed : Visibility.Visible;
        HeaderGrid.RowSpacing = !_externalWatchlist && PageShell.ActualWidth < 640 ? 12 : 0;
    }

    private void LibraryWatchlistTab_Click(object sender, RoutedEventArgs e) { _externalWatchlist = false; UpdateWatchlistTabs(); }
    private async void ExternalWatchlistTab_Click(object sender, RoutedEventArgs e)
    {
        _externalWatchlist = true; UpdateWatchlistTabs(); await LoadWatchlistTitlesAsync();
    }
    private void WatchlistTab_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)) return;
        e.Handled = true;
        _externalWatchlist = e.Key == Windows.System.VirtualKey.Right;
        UpdateWatchlistTabs(); (_externalWatchlist ? ExternalWatchlistTab : LibraryWatchlistTab).Focus(FocusState.Keyboard);
    }
    private async void ExternalTitlesRetry_Click(object sender, RoutedEventArgs e) => await LoadWatchlistTitlesAsync();
    private void WatchlistDiscover_Click(object sender, RoutedEventArgs e)
        => App.Services.GetRequiredService<NavigationService>().Navigate<RequestsPage>();

    private void ExternalWatchlist_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyExternalTitleLayout(e.NewSize.Width);
    }
    private void ApplyExternalTitleLayout(double viewportWidth)
    {
        var breakpointWidth = PageShell.ActualWidth > 0 ? PageShell.ActualWidth : viewportWidth;
        var width = Math.Max(240, breakpointWidth - ExternalWatchlistScroller.Padding.Left - ExternalWatchlistScroller.Padding.Right);
        // The external watchlist uses cardGridClasses("large") independently
        // of the library's configurable poster density.
        var columns = breakpointWidth >= 1280 ? 6 : breakpointWidth >= 1024 ? 5 : breakpointWidth >= 768 ? 4 : breakpointWidth >= 640 ? 3 : 2;
        const int gap = 16;
        _externalTitleWidth = Math.Floor((width - (columns - 1) * gap) / columns);
        ExternalTitlesLayout.MinColumnSpacing = ExternalTitlesLayout.MinRowSpacing = gap;
        // UniformGridLayout's fit calculation includes the trailing spacing.
        // Leave that spacing budget in the minimum; the card still fills the
        // intended column width without dropping a column at exact boundaries.
        ExternalTitlesLayout.MinItemWidth = Math.Max(1, _externalTitleWidth - gap);
        ExternalTitlesLayout.MinItemHeight = _externalTitleWidth * 1.5 + (_uiCustomizationService.CardPresentation.Caption == "artwork" ? 24 : 80);
        ExternalTitlesLayout.MaximumRowsOrColumns = columns;
        ExternalTitlesSkeletonLayout.MinItemWidth = ExternalTitlesLayout.MinItemWidth;
        ExternalTitlesSkeletonLayout.MinItemHeight = _externalTitleWidth * 1.5;
        ExternalTitlesSkeletonLayout.MaximumRowsOrColumns = columns;
        for (var i = 0; i < 6; i++)
            if (ExternalTitlesSkeleton.TryGetElement(i) is Border placeholder) { placeholder.Width = _externalTitleWidth; placeholder.Height = _externalTitleWidth * 1.5; }
        ExternalWatchlistTabLabel.Text = ActualWidth < 640 ? "Not in library" : "Not in your library yet";
        var compact = breakpointWidth < 640;
        ExternalWatchlistHintGrid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : GridLength.Auto;
        Grid.SetColumn(ExternalWatchlistDiscoverLink, compact ? 0 : 1);
        Grid.SetRow(ExternalWatchlistDiscoverLink, compact ? 1 : 0);
        ExternalWatchlistDiscoverLink.Margin = new Thickness(compact ? 0 : 16, compact ? 8 : 0, 0, 0);
        if (_watchlistTitles == null) return;
        for (var i = 0; i < _watchlistTitles.ExternalTitles.Count; i++)
            if (ExternalTitlesRepeater.TryGetElement(i) is ContentControl host) SetExternalTitle(host, _watchlistTitles.ExternalTitles[i]);
    }

    private void ExternalTitlesSkeleton_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (e.Element is Border placeholder) { placeholder.Width = _externalTitleWidth; placeholder.Height = _externalTitleWidth * 1.5; }
    }

    private void ExternalTitlesRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (_watchlistTitles != null && e.Element is ContentControl host && e.Index < _watchlistTitles.ExternalTitles.Count)
            SetExternalTitle(host, _watchlistTitles.ExternalTitles[e.Index]);
    }

    private void SetExternalTitle(ContentControl host, WatchlistTitle title)
    {
        var status = SiloPlayer.Core.Services.RequestViewerPolicy.WatchlistPresentation(title);
        var caption = status.Caption;
        title.InWatchlist = true;
        var card = ExternalTitleCard.Build(title, _externalTitleWidth,
            request: status.Requestable ? async () =>
            {
                await App.Services.GetRequiredService<RequestsApi>().CreateAsync(new CreateMediaRequestInput { MediaType = title.MediaType, TmdbId = title.TmdbId,
                    Title = title.Title, Year = title.Year, Overview = title.Overview, PosterPath = title.PosterPath, BackdropPath = title.BackdropPath });
                await LoadWatchlistTitlesAsync();
            } : null,
            watchlist: async () => { await _watchlistTitles!.RemoveExternalTitleAsync(title); },
            removeOnly: true, statusCaption: caption, attention: title.Status is "needs_review" or "removed",
            statusBadge: status.Badge, watchlistCard: true,
            statusSearchAction: title.Status is "needs_review" or "removed" ? () =>
                App.Services.GetRequiredService<NavigationService>().Navigate<SearchPage>(new SearchNavigation(title.Title, title.MediaType)) : null);
        host.Content = card;
    }
}
