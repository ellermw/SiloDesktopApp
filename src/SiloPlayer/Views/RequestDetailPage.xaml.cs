using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class RequestDetailPage : Page
{
    private readonly RequestsApi _api = App.Services.GetRequiredService<RequestsApi>();
    private readonly CancellationTokenSource _lifetime = new();
    private RequestMediaDetail? _item;

    public RequestDetailPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not RequestDetailNavigation nav) { Fail("This title could not be identified."); return; }
        try
        {
            _item = await _api.GetDetailAsync(nav.MediaType, nav.TmdbId, _lifetime.Token);
            Render(_item);
            LoadingLayer.Visibility = Visibility.Collapsed;
            if (App.MainWindowInstance is MainWindow window) window.SetDynamicTitle(_item.Title);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Request detail load failed: {ex}");
            Fail("Couldn't load this title.\nThe TMDB record may be temporarily unavailable.");
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) { _lifetime.Cancel(); base.OnNavigatedFrom(e); }

    private void Render(RequestMediaDetail item)
    {
        TitleText.Text = item.Title;
        ContextText.Text = $"REQUEST · {(item.MediaType == "series" ? "SERIES" : "MOVIE")}";
        TaglineText.Text = item.Tagline ?? "";
        TaglineText.Visibility = string.IsNullOrWhiteSpace(item.Tagline) ? Visibility.Collapsed : Visibility.Visible;
        OverviewText.Text = item.Overview ?? "";
        StudioText.Text = item.MediaType == "series" ? item.Networks?.FirstOrDefault() ?? "" : item.ProductionCompanies?.FirstOrDefault() ?? "";
        ScoreText.Text = item.VoteAverage is > 0 ? $"★ {item.VoteAverage:0.0} TMDB{(item.VoteCount is > 0 ? $"  ·  {item.VoteCount:N0} votes" : "")}" : "";
        CrewText.Text = BuildCrew(item);
        SetImage(BackdropImage, Tmdb(item.BackdropPath, "original"));
        SetImage(PosterImage, Tmdb(item.PosterPath, "w500"));
        BuildMetadata(item);
        BuildActions(item);
        BuildCast(item.Cast ?? []);
        BuildRecommendations(item.Recommendations ?? []);
    }

    private void BuildMetadata(RequestMediaDetail item)
    {
        MetadataPanel.Children.Clear();
        var values = new List<string>();
        if (item.Year is > 0) values.Add(item.Year.Value.ToString());
        if (!string.IsNullOrWhiteSpace(item.ContentRating)) values.Add(item.ContentRating);
        if (item.MediaType == "movie" && item.Runtime is > 0) values.Add(FormatDuration(item.Runtime.Value));
        if (item.MediaType == "series" && item.NumberOfSeasons is > 0) values.Add($"{item.NumberOfSeasons} season{(item.NumberOfSeasons == 1 ? "" : "s")}");
        if (item.MediaType == "series" && !string.IsNullOrWhiteSpace(item.Status)) values.Add(item.Status);
        values.AddRange((item.Genres ?? []).Take(4));
        foreach (var value in values)
            MetadataPanel.Children.Add(new Border { Background = Brush("SurfaceRaisedBrush"), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3, 8, 3), Child = new TextBlock { Text = value, FontSize = 12 } });
    }

    private void BuildActions(RequestMediaDetail item)
    {
        ActionsPanel.Children.Clear();
        if (item.Request.Requestable)
        {
            var button = new Button { Content = $"＋  Request {(item.MediaType == "series" ? "series" : "movie")}", Padding = new Thickness(20, 10, 20, 10), CornerRadius = new CornerRadius(22) };
            button.Click += Request_Click;
            ActionsPanel.Children.Add(button);
        }
        else if (item.Availability == "available" && string.IsNullOrWhiteSpace(item.Request.Status))
        {
            ActionsPanel.Children.Add(StatusPill("✓  Already in your library"));
            if (!string.IsNullOrWhiteSpace(item.LibraryContentId))
            {
                var open = new Button { Content = "Open in library", Tag = item.LibraryContentId, CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 10, 18, 10) };
                open.Click += OpenLibrary_Click;
                ActionsPanel.Children.Add(open);
            }
        }
        else ActionsPanel.Children.Add(StatusPill(!string.IsNullOrWhiteSpace(item.Request.Status) ? Format(item.Request.Status) : Reason(item.Request.Reason)));

        if (!string.IsNullOrWhiteSpace(item.ImdbId))
            ActionsPanel.Children.Add(ExternalLinkButton("IMDb", $"https://www.imdb.com/title/{item.ImdbId}"));
        ActionsPanel.Children.Add(ExternalLinkButton("TMDB", $"https://www.themoviedb.org/{(item.MediaType == "series" ? "tv" : "movie")}/{item.TmdbId}"));
    }

    private async void Request_Click(object sender, RoutedEventArgs e)
    {
        if (_item == null || sender is not Button button) return;
        button.IsEnabled = false; button.Content = "Submitting…";
        try
        {
            await _api.CreateAsync(new CreateMediaRequestInput { MediaType = _item.MediaType, TmdbId = _item.TmdbId, TvdbId = _item.TvdbId, ImdbId = _item.ImdbId, Title = _item.Title, Year = _item.Year, Overview = _item.Overview, PosterPath = _item.PosterPath, BackdropPath = _item.BackdropPath }, _lifetime.Token);
            _item = await _api.GetDetailAsync(_item.MediaType, _item.TmdbId, _lifetime.Token);
            BuildActions(_item);
        }
        catch (Exception ex) { button.IsEnabled = true; button.Content = ex.Message; }
    }

    private void BuildCast(IEnumerable<RequestMediaCastMember> cast)
    {
        CastPanel.Children.Clear();
        foreach (var member in cast.Take(20))
        {
            var panel = new StackPanel { Width = 112, Spacing = 5 };
            var image = new Image { Width = 112, Height = 168, Stretch = Stretch.UniformToFill };
            SetImage(image, Tmdb(member.ProfilePath, "w185"));
            panel.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = Brush("SurfaceRaisedBrush"), Child = image });
            panel.Children.Add(new TextBlock { Text = member.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = member.Character ?? "", FontSize = 11, Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
            CastPanel.Children.Add(panel);
        }
        CastSection.Visibility = CastPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildRecommendations(IEnumerable<RequestMediaResult> items)
    {
        RecommendationsPanel.Children.Clear();
        foreach (var item in items)
        {
            var panel = new StackPanel { Width = 142, Spacing = 6, Tag = item };
            panel.Tapped += Recommendation_Tapped;
            var image = new Image { Width = 142, Height = 213, Stretch = Stretch.UniformToFill };
            SetImage(image, Tmdb(item.PosterPath, "w342"));
            panel.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = Brush("SurfaceRaisedBrush"), Child = image });
            panel.Children.Add(new TextBlock { Text = item.Title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (item.Request.Requestable)
            {
                var request = new Button { Content = "Request", Tag = item, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5, 8, 5) };
                request.Tapped += (_, args) => args.Handled = true;
                request.Click += RecommendationRequest_Click;
                panel.Children.Add(request);
            }
            RecommendationsPanel.Children.Add(panel);
        }
        RecommendationsSection.Visibility = RecommendationsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Recommendation_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) { if (sender is FrameworkElement { Tag: RequestMediaResult item }) App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(item.MediaType, item.TmdbId)); }
    private async void RecommendationRequest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RequestMediaResult item } button) return;
        button.IsEnabled = false;
        button.Content = "Submitting…";
        try
        {
            await _api.CreateAsync(new CreateMediaRequestInput { MediaType = item.MediaType, TmdbId = item.TmdbId, Title = item.Title, Year = item.Year, Overview = item.Overview, PosterPath = item.PosterPath, BackdropPath = item.BackdropPath }, _lifetime.Token);
            button.Content = "Requested";
        }
        catch (Exception ex)
        {
            button.IsEnabled = true;
            button.Content = ex.Message;
        }
    }
    private void OpenLibrary_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string id }) App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(id); }
    private async void ExternalLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }
    private void Back_Click(object sender, RoutedEventArgs e) => App.Services.GetRequiredService<NavigationService>().GoBack();
    private void Fail(string text) { DetailSkeleton.Visibility = Visibility.Collapsed; LoadingText.Text = text; LoadingText.Visibility = Visibility.Visible; }

    private static Border StatusPill(string text) => new() { CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 11, 18, 11), Background = Brush("SurfaceRaisedBrush"), Child = new TextBlock { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold } };
    private Button ExternalLinkButton(string label, string url)
    {
        var button = new Button { Content = label, Tag = url, CornerRadius = new CornerRadius(18), Padding = new Thickness(12, 7, 12, 7) };
        button.Click += ExternalLink_Click;
        return button;
    }
    private static string BuildCrew(RequestMediaDetail item) { var p = new List<string>(); if (!string.IsNullOrWhiteSpace(item.Director)) p.Add($"Director: {item.Director}"); if (item.Creators?.Count > 0) p.Add($"Created by: {string.Join(", ", item.Creators)}"); if (item.Networks?.Count > 0) p.Add($"Network: {string.Join(", ", item.Networks)}"); return string.Join("  ·  ", p); }
    private static string FormatDuration(int minutes) => minutes >= 60 ? $"{minutes / 60}h{(minutes % 60 == 0 ? "" : $" {minutes % 60}m")}" : $"{minutes}m";
    private static string Format(string value) => string.IsNullOrWhiteSpace(value) ? "Requested" : char.ToUpperInvariant(value[0]) + value[1..];
    private static string Reason(string value) => value switch { "already_requested" => "Already requested", "already_available" => "Available", "requests_disabled" => "Requests disabled", "quota_exceeded" => "Limit reached", "blocked" => "Blocked", _ => "Unavailable" };
    private static string? Tmdb(string? path, string size) => string.IsNullOrWhiteSpace(path) ? null : $"https://image.tmdb.org/t/p/{size}{path}";
    private static void SetImage(Image image, string? url) { if (url != null) image.Source = new BitmapImage(new Uri(url)); }
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}

public sealed record RequestDetailNavigation(string MediaType, int TmdbId);
