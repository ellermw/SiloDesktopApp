using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Imaging;
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

    public RequestBrowsePage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not RequestBrowseNavigation nav) { Fail("Browse category not found."); return; }
        _navigation = nav;
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
    private void Fail(string text) { BrowseSkeleton.Visibility = Visibility.Collapsed; LoadingText.Text = text; LoadingText.Visibility = Visibility.Visible; }
    private static string SelectedTag(ComboBox box, string fallback) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
}

public sealed record RequestBrowseNavigation(string Kind, string Slug);
