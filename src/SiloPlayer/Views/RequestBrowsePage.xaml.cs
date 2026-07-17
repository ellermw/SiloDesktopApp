using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class RequestBrowsePage : Page
{
    private readonly RequestsApi _api = App.Services.GetRequiredService<RequestsApi>();
    private readonly CancellationTokenSource _lifetime = new();
    private RequestBrowseNavigation? _navigation;
    private bool _initialized;
    private int _page = 1;
    private int _totalPages;
    private double _browseCardWidth = 184;

    public RequestBrowsePage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not RequestBrowseNavigation nav) { Fail("Browse category not found."); return; }
        _navigation = nav;
        var fallbackTitle = HumanizeSlug(nav.Slug);
        TitleText.Text = fallbackTitle;
        HeaderTileText.Text = fallbackTitle;
        HeaderTileText.Visibility = Visibility.Visible;
        PageText.Text = "Loading...";
        MediaTypeCombo.Visibility = nav.Kind == "genre" ? Visibility.Visible : Visibility.Collapsed;
        if (nav.Kind == "network") MediaTypeCombo.SelectedIndex = 1;
        _initialized = true;
        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) { _lifetime.Cancel(); base.OnNavigatedFrom(e); }

    private async Task LoadAsync()
    {
        if (_navigation == null) return;
        LoadingLayer.Visibility = Visibility.Visible;
        BrowseSkeleton.Visibility = Visibility.Visible;
        LoadingText.Visibility = Visibility.Collapsed;
        try
        {
            var mediaType = _navigation.Kind switch { "studio" => "movie", "network" => "series", _ => SelectedTag(MediaTypeCombo, "movie") };
            var response = await _api.BrowseDiscoverAsync(_navigation.Kind, _navigation.Slug, mediaType, SelectedTag(SortCombo, "popularity"), _page, _lifetime.Token);
            TitleText.Text = response.DisplayName;
            HeaderTileText.Text = response.DisplayName;
            HeaderTileText.Visibility = string.IsNullOrWhiteSpace(response.LogoUrl) ? Visibility.Visible : Visibility.Collapsed;
            HeaderLogo.Source = string.IsNullOrWhiteSpace(response.LogoUrl) ? null : new BitmapImage(new Uri(response.LogoUrl));
            _totalPages = Math.Max(1, response.TotalPages);
            PageText.Text = response.Results.Count == 0 ? "No results." : $"Page {_page} of {_totalPages}";
            FooterPageText.Text = $"Page {_page} of {_totalPages}";
            PreviousButton.IsEnabled = _page > 1; NextButton.IsEnabled = _page < _totalPages;
            FooterPanel.Visibility = _totalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
            ResultsGrid.ItemsSource = response.Results;
            LoadingLayer.Visibility = Visibility.Collapsed;
            if (App.MainWindowInstance is MainWindow window) window.SetDynamicTitle(response.DisplayName);
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            Fail($"{(_navigation.Kind == "studio" ? "Studio" : _navigation.Kind == "network" ? "Network" : "Genre")} not found.");
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (!_initialized) return; _page = 1; await LoadAsync(); }
    private async void Previous_Click(object sender, RoutedEventArgs e) { if (_page <= 1) return; _page--; await LoadAsync(); ResultsGrid.StartBringIntoView(); }
    private async void Next_Click(object sender, RoutedEventArgs e) { if (_page >= _totalPages) return; _page++; await LoadAsync(); ResultsGrid.StartBringIntoView(); }
    private void ResultsGrid_ItemClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is RequestMediaResult item) App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(item.MediaType, item.TmdbId)); }

    private async void Request_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RequestMediaResult item } button) return;
        button.IsEnabled = false; button.Content = "Submitting…";
        try
        {
            await _api.CreateAsync(new CreateMediaRequestInput { MediaType = item.MediaType, TmdbId = item.TmdbId, Title = item.Title, Year = item.Year, Overview = item.Overview, PosterPath = item.PosterPath, BackdropPath = item.BackdropPath }, _lifetime.Token);
            button.Content = "Requested";
        }
        catch (Exception ex) { button.Content = ex.Message; }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => App.Services.GetRequiredService<NavigationService>().GoBack();
    private void Fail(string text) { BrowseSkeleton.Visibility = Visibility.Collapsed; FooterPanel.Visibility = Visibility.Collapsed; LoadingText.Text = text; LoadingText.Visibility = Visibility.Visible; }
    private static string SelectedTag(ComboBox box, string fallback) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private void ResultCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card && card.FindName("InlineRequestButton") is Button button)
            button.Opacity = 1;
    }

    private void ResultCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement card && card.FindName("InlineRequestButton") is Button button)
            button.Opacity = 0;
    }

    private void InlineRequest_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void ResultsGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.ItemContainer.ContentTemplateRoot is Grid card)
            SizeResultCard(card);
    }

    private void SizeResultCard(Grid card)
    {
        card.Width = _browseCardWidth;
        if (card.RowDefinitions.Count > 0)
            card.RowDefinitions[0].Height = new GridLength(_browseCardWidth * 1.5);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;
        var gutter = width < 640 ? 16d : width < 1024 ? 24d : width < 1280 ? 40d : 48d;
        HeaderPanel.Padding = new Thickness(gutter, width < 640 ? 24 : 32, gutter, 12);
        ResultsGrid.Padding = new Thickness(gutter, 8, gutter, 20);
        FooterPanel.Padding = new Thickness(gutter, 8, gutter, 18);
        BrowseSkeleton.Padding = new Thickness(gutter, 8, gutter, 20);

        var compact = width < 700;
        Grid.SetRow(FilterPanel, compact ? 1 : 0);
        Grid.SetColumn(FilterPanel, compact ? 0 : 1);
        Grid.SetColumnSpan(FilterPanel, compact ? 2 : 1);

        var columns = width < 640 ? 3 : width < 768 ? 4 : width < 1024 ? 5 : width < 1280 ? 7 : 8;
        _browseCardWidth = Math.Max(96, Math.Floor((width - gutter * 2) / columns) - 12);
        if (ResultsGrid.ItemsPanelRoot is ItemsWrapGrid panel)
        {
            panel.ItemWidth = _browseCardWidth + 12;
            panel.ItemHeight = _browseCardWidth * 1.5 + 62;
        }

        foreach (var item in ResultsGrid.Items)
        {
            if (ResultsGrid.ContainerFromItem(item) is GridViewItem { ContentTemplateRoot: Grid card })
                SizeResultCard(card);
        }
    }

    private static string HumanizeSlug(string slug) => string.Join(" ", slug
        .Split('-', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}

public sealed record RequestBrowseNavigation(string Kind, string Slug);
