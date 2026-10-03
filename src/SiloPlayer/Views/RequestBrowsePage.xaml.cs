using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.Controls;
using SiloPlayer.Converters;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class RequestBrowsePage : Page
{
    private readonly RequestsApi _api = App.Services.GetRequiredService<RequestsApi>();
    private readonly UICustomizationService? _presentation = App.Services.GetService<UICustomizationService>();
    private readonly RequestBrowseSession _browse = new();
    private readonly CancellationTokenSource _lifetime = new();
    private RequestBrowseNavigation? _navigation;
    private RequestFeatureStatus _features = new();
    private ScrollViewer? _scroll;
    private bool _initialized;
    private string _mediaType = "movie";
    private double _browseCardWidth = 184;
    private double _gap = 12;

    public RequestBrowsePage()
    {
        InitializeComponent(); ResultsGrid.ItemsSource = _browse.Results; UpdateMediaTabs();
        _browse.Changed += UpdateState;
        Unloaded += (_, _) => DetachScroll();
    }
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not RequestBrowseNavigation nav) { Fail("Browse category not found."); return; }
        _navigation = nav;
        var title = HumanizeSlug(nav.Slug); TitleText.Text = title; HeaderTileText.Text = title;
        MediaTypeTabs.Visibility = nav.Kind == "genre" ? Visibility.Visible : Visibility.Collapsed;
        SortCombo.Visibility = nav.Kind == "section" ? Visibility.Collapsed : Visibility.Visible;
        if (_presentation != null) _presentation.Changed += Presentation_Changed;
        _initialized = true;
        await LoadAsync();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _initialized = false; _browse.Cancel(); _lifetime.Cancel(); DetachScroll();
        if (_presentation != null) _presentation.Changed -= Presentation_Changed;
        base.OnNavigatedFrom(e);
    }
    private async Task LoadAsync()
    {
        if (_navigation is not { } nav) return;
        var type = nav.Kind switch { "studio" => "movie", "network" => "series", _ => _mediaType };
        var sort = (SortCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "popularity";
        await _browse.ResetAsync(async (page, token) =>
        {
            if (nav.Kind == "section")
            {
                var section = await _api.GetDiscoverySectionAsync(nav.Slug, page, token);
                return (new DiscoverBrowseResponse { DisplayName = section.Title, Results = section.Results, TotalPages = section.TotalPages }, RequestDiscoveryPaging.Next(section, page));
            }
            var response = await _api.BrowseDiscoverAsync(nav.Kind, nav.Slug, type, sort, page, token);
            return (response, page < Math.Min(500, response.TotalPages) ? page + 1 : (int?)null);
        });
        if (_lifetime.IsCancellationRequested) return;
        try { _features = await _api.GetStatusAsync(_lifetime.Token); RebuildRealizedCards(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch { _features = new(); }
        await TraverseEmptyPagesAsync();
    }
    private async Task TraverseEmptyPagesAsync()
    {
        while (_initialized && !_browse.IsLoading && !_browse.IsLoadingMore && _browse.Results.Count == 0 && _browse.HasMore && _browse.MoreError == null)
            await _browse.LoadMoreAsync();
    }
    private void UpdateState()
    {
        if (_browse.FirstPage is { } first)
        {
            TitleText.Text = first.DisplayName; HeaderTileText.Text = first.DisplayName;
            HeaderTileText.Visibility = string.IsNullOrWhiteSpace(first.LogoUrl) ? Visibility.Visible : Visibility.Collapsed;
            HeaderLogo.Source = string.IsNullOrWhiteSpace(first.LogoUrl) ? null : (ImageSource)new UrlToImageSourceConverter().Convert(first.LogoUrl, typeof(ImageSource), null!, "");
            if (App.MainWindowInstance is MainWindow window) window.SetDynamicTitle(first.DisplayName);
        }
        PageText.Text = _browse.IsLoading ? "Loading..." : _navigation?.Kind switch { "studio" => "Studio", "network" => "Network", "genre" => "Genre", _ => "Discover" };
        LoadingLayer.Visibility = _browse.IsLoading || _browse.Error != null ? Visibility.Visible : Visibility.Collapsed;
        BrowseSkeleton.Visibility = _browse.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        LoadingText.Visibility = _browse.Error != null ? Visibility.Visible : Visibility.Collapsed;
        var missing = _browse.Error is ApiException { StatusCode: 404 };
        LoadingText.Text = missing ? $"{PageText.Text} not found." : "Could not load this browse page. Try a different sort or media type.";
        RetryBrowseButton.Visibility = _browse.Error != null && !missing ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = !_browse.IsLoading && _browse.Error == null && !_browse.HasMore && _browse.Results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FooterPanel.Visibility = _browse.HasMore && _browse.Error == null ? Visibility.Visible : Visibility.Collapsed;
        MoreLoadingRing.Visibility = _browse.IsLoadingMore ? Visibility.Visible : Visibility.Collapsed; MoreLoadingRing.IsActive = _browse.IsLoadingMore;
        MoreErrorText.Visibility = _browse.MoreError != null ? Visibility.Visible : Visibility.Collapsed;
        LoadMoreButton.Content = _browse.MoreError != null ? "Try again" : "Load more"; LoadMoreButton.IsEnabled = !_browse.IsLoadingMore;
    }
    private async void RetryBrowse_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (_initialized) await LoadAsync(); }
    private async void MediaType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string type }) return;
        if (_mediaType == type || !_initialized) return; _mediaType = type; UpdateMediaTabs(); await LoadAsync();
    }
    private void UpdateMediaTabs()
    {
        foreach (var tab in new[] { MoviesTab, SeriesTab })
        {
            var selected = tab.Tag?.ToString() == _mediaType;
            tab.Background = selected ? (Brush)Application.Current.Resources["SurfaceRaisedBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            tab.Foreground = (Brush)Application.Current.Resources[selected ? "PrimaryTextBrush" : "SecondaryTextBrush"];
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tab, $"{tab.Content}{(selected ? ", selected" : "")}");
        }
    }
    private async void LoadMore_Click(object sender, RoutedEventArgs e) { await _browse.LoadMoreAsync(); await TraverseEmptyPagesAsync(); }
    private void ResultsGrid_Loaded(object sender, RoutedEventArgs e)
    {
        DetachScroll(); _scroll = FindScroll(ResultsGrid); if (_scroll != null) _scroll.ViewChanged += Scroll_ViewChanged;
        ApplyLayout(ActualWidth);
    }
    private async void Scroll_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scroll == null || _browse.MoreError != null || _scroll.ScrollableHeight - _scroll.VerticalOffset > 600) return;
        await _browse.LoadMoreAsync(); await TraverseEmptyPagesAsync();
    }
    private void DetachScroll() { if (_scroll != null) _scroll.ViewChanged -= Scroll_ViewChanged; _scroll = null; }
    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
    private void ResultsGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is RequestMediaResult item && args.ItemContainer.ContentTemplateRoot is ContentControl host) BuildCard(host, item);
    }
    private void BuildCard(ContentControl host, RequestMediaResult item)
    {
        host.Tag = item; host.Margin = new Thickness(_gap / 2); host.Content = ExternalTitleCard.Build(item, _browseCardWidth,
            request: async () =>
            {
                if (item.MediaType == "series") { App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(item.MediaType, item.TmdbId)); return; }
                var created = await _api.CreateAsync(new() { MediaType = item.MediaType, TmdbId = item.TmdbId, Title = item.Title, Year = item.Year, Overview = item.Overview, PosterPath = item.PosterPath, BackdropPath = item.BackdropPath }, _lifetime.Token);
                item.Request = new() { Status = string.IsNullOrWhiteSpace(created.Status) ? "pending" : created.Status, Requestable = false, RequestId = created.Id };
                if (ReferenceEquals(host.Tag, item)) BuildCard(host, item);
                App.Services.GetRequiredService<ToastService>().Success("Request submitted");
            }, watchlist: _features.WatchlistTitlesSupported ? async () =>
            {
                if (item.InWatchlist == true) await _api.RemoveWatchlistTitleAsync(item.MediaType, item.TmdbId, _lifetime.Token);
                else await _api.AddWatchlistTitleAsync(item.MediaType, item.TmdbId, _lifetime.Token);
                item.InWatchlist = item.InWatchlist != true;
                App.Services.GetService<WatchlistViewModel>()?.InvalidateExternalTitles();
            } : null);
    }
    private void RebuildRealizedCards()
    {
        if (ResultsGrid.ItemsPanelRoot is not { } panel) return;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(panel); i++)
            if (VisualTreeHelper.GetChild(panel, i) is GridViewItem { Content: RequestMediaResult item, ContentTemplateRoot: ContentControl host }) BuildCard(host, item);
    }
    private void Presentation_Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => ApplyLayout(ActualWidth));
    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout(e.NewSize.Width);
    private void ApplyLayout(double width)
    {
        if (width <= 0) return;
        var gutter = width < 640 ? 16d : width < 1024 ? 24d : width < 1280 ? 40d : 48d;
        HeaderPanel.Padding = new Thickness(gutter, width < 640 ? 64 : 72, gutter, 12);
        BackButton.Margin = new Thickness(8, width < 640 ? 16 : 24, 0, 0);
        TitleText.FontSize = width < 640 ? 24 : 30;
        TitleText.LineHeight = width < 640 ? 32 : 36;
        TitleText.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _gap = _presentation?.CardPresentation.PosterSize == "large" ? 16 : 12;
        ResultsGrid.Padding = new Thickness(gutter - _gap / 2, 8, gutter - _gap / 2, 20);
        FooterPanel.Padding = new Thickness(gutter, 8, gutter, 18); BrowseSkeleton.Padding = new Thickness(gutter, 8, gutter, 20);
        var compact = width < 700; Grid.SetRow(FilterPanel, compact ? 1 : 0); Grid.SetColumn(FilterPanel, compact ? 0 : 1); Grid.SetColumnSpan(FilterPanel, compact ? 2 : 1);
        var size = _presentation?.CardPresentation.PosterSize;
        _gap = size == "large" ? 16 : 12;
        var columns = size switch
        {
            "compact" => width < 640 ? 3 : width < 768 ? 5 : width < 1024 ? 6 : width < 1280 ? 8 : 10,
            "large" => width < 640 ? 2 : width < 768 ? 3 : width < 1024 ? 4 : width < 1280 ? 5 : 6,
            _ => width < 640 ? 3 : width < 768 ? 4 : width < 1024 ? 5 : width < 1280 ? 7 : 8,
        };
        _browseCardWidth = Math.Max(64, Math.Floor((width - gutter * 2 - (columns - 1) * _gap) / columns));
        if (ResultsGrid.ItemsPanelRoot is ItemsWrapGrid panel) { panel.ItemWidth = _browseCardWidth + _gap; panel.ItemHeight = _browseCardWidth * 1.5 + (_presentation?.CardPresentation.Caption == "artwork" ? _gap : 72); }
        RebuildRealizedCards();
    }
    private void Back_Click(object sender, RoutedEventArgs e) => App.Services.GetRequiredService<NavigationService>().GoBack();
    private void Fail(string text) { EmptyState.Visibility = Visibility.Collapsed; BrowseSkeleton.Visibility = Visibility.Collapsed; FooterPanel.Visibility = Visibility.Collapsed; LoadingText.Text = text; LoadingText.Visibility = Visibility.Visible; }
    private static string HumanizeSlug(string slug) => string.Join(" ", slug.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
public sealed record RequestBrowseNavigation(string Kind, string Slug);
