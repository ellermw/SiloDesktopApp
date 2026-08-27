using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Converters;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class RequestDetailPage : Page
{
    private static readonly UrlToImageSourceConverter RemoteImageConverter = new();
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
        ScoreText.Text = item.VoteAverage is > 0
            ? $"★ {item.VoteAverage:0.0} TMDB{(item.VoteCount is > 0 ? $"  ·  {FormatVoteCount(item.VoteCount.Value)} votes" : "")}"
            : "";
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
        foreach (var value in values)
            MetadataPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(102, 26, 27, 31)),
                BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(96, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock { Text = value, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium },
            });
        foreach (var genre in (item.Genres ?? []).Take(4))
            MetadataPanel.Children.Add(new TextBlock
            {
                Text = genre,
                FontSize = 12,
                Foreground = Brush("SecondaryTextBrush"),
                Padding = new Thickness(4, 3, 4, 3),
                VerticalAlignment = VerticalAlignment.Center,
            });
    }

    private void BuildActions(RequestMediaDetail item)
    {
        ActionsPanel.Children.Clear();
        if (item.Request.Requestable)
        {
            var button = new Button { Content = $"＋  Request {(item.MediaType == "series" ? "series" : "movie")}", Padding = new Thickness(20, 10, 20, 10), CornerRadius = new CornerRadius(22) };
            AutomationProperties.SetName(button, $"Request {(item.MediaType == "series" ? "series" : "movie")}");
            button.Click += Request_Click;
            ActionsPanel.Children.Add(button);
        }
        else if (item.Availability == "available" && string.IsNullOrWhiteSpace(item.Request.Status))
        {
            ActionsPanel.Children.Add(StatusPill("✓  Already in your library", "emerald"));
            if (!string.IsNullOrWhiteSpace(item.LibraryContentId))
            {
                var open = new Button { Content = "Open in library", Tag = item.LibraryContentId, CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 10, 18, 10) };
                open.Click += OpenLibrary_Click;
                ActionsPanel.Children.Add(open);
            }
        }
        else
        {
            var label = !string.IsNullOrWhiteSpace(item.Request.Status)
                ? Format(item.Request.Status)
                : Reason(item.Request.Reason);
            ActionsPanel.Children.Add(StatusPill(label, StatusTone(item.Request.Status)));
        }

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
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            button.IsEnabled = true;
            button.Content = $"＋  Request {(_item.MediaType == "series" ? "series" : "movie")}";
            App.Services.GetRequiredService<ToastService>().Error($"Request failed: {ex.Message}");
        }
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
            var panel = new StackPanel { Width = 142, Spacing = 6 };
            var image = new Image { Width = 142, Height = 213, Stretch = Stretch.UniformToFill };
            SetImage(image, Tmdb(item.PosterPath, "w342"));
            var poster = new Grid { Width = 142, Height = 213 };
            poster.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = Brush("SurfaceRaisedBrush"), Child = image });

            var status = item.Availability == "available"
                ? "In library"
                : !string.IsNullOrWhiteSpace(item.Request.Status)
                    ? Format(item.Request.Status)
                    : "";
            if (!string.IsNullOrWhiteSpace(status))
            {
                poster.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(220, 20, 20, 22)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(7, 3, 7, 3),
                    Margin = new Thickness(7),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = new TextBlock { Text = status, FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                });
            }

            var cardContent = new StackPanel { Width = 142, Spacing = 6 };
            cardContent.Children.Add(poster);
            cardContent.Children.Add(new TextBlock { Text = item.Title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            cardContent.Children.Add(new TextBlock
            {
                Text = item.DisplayMeta,
                FontSize = 11,
                Opacity = 0.65,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var openDetail = new Button
            {
                Content = cardContent,
                Tag = item,
                Style = (Style)Application.Current.Resources["GhostButtonStyle"],
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            AutomationProperties.SetName(openDetail, $"Open {item.Title} request details");
            openDetail.Click += Recommendation_Click;
            panel.Children.Add(openDetail);
            if (item.Availability == "available" && !string.IsNullOrWhiteSpace(item.LibraryContentId))
            {
                var library = new Button
                {
                    Content = "Library",
                    Tag = item.LibraryContentId,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(8, 5, 8, 5),
                };
                AutomationProperties.SetName(library, $"Open {item.Title} in library");
                library.Click += OpenLibrary_Click;
                panel.Children.Add(library);
            }
            else if (item.Request.Requestable)
            {
                var request = new Button { Content = "Request", Tag = item, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5, 8, 5) };
                AutomationProperties.SetName(request, $"Request {item.Title}");
                request.Click += RecommendationRequest_Click;
                panel.Children.Add(request);
            }
            RecommendationsPanel.Children.Add(panel);
        }
        RecommendationsSection.Visibility = RecommendationsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Recommendation_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement { Tag: RequestMediaResult item }) App.Services.GetRequiredService<NavigationService>().Navigate<RequestDetailPage>(new RequestDetailNavigation(item.MediaType, item.TmdbId)); }
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
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            button.IsEnabled = true;
            button.Content = "Request";
        }
        catch (Exception ex)
        {
            button.IsEnabled = true;
            button.Content = "Request";
            App.Services.GetRequiredService<ToastService>().Error($"Request failed: {ex.Message}");
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

    private static Border StatusPill(string text, string tone)
    {
        var (background, foreground, border) = tone switch
        {
            "amber" => (Microsoft.UI.ColorHelper.FromArgb(38, 245, 158, 11), Microsoft.UI.ColorHelper.FromArgb(255, 254, 243, 199), Microsoft.UI.ColorHelper.FromArgb(102, 251, 191, 36)),
            "sky" => (Microsoft.UI.ColorHelper.FromArgb(38, 14, 165, 233), Microsoft.UI.ColorHelper.FromArgb(255, 224, 242, 254), Microsoft.UI.ColorHelper.FromArgb(102, 56, 189, 248)),
            "emerald" => (Microsoft.UI.ColorHelper.FromArgb(38, 16, 185, 129), Microsoft.UI.ColorHelper.FromArgb(255, 209, 250, 229), Microsoft.UI.ColorHelper.FromArgb(102, 52, 211, 153)),
            _ => (Microsoft.UI.ColorHelper.FromArgb(153, 63, 63, 70), Microsoft.UI.ColorHelper.FromArgb(255, 228, 228, 231), Microsoft.UI.ColorHelper.FromArgb(102, 113, 113, 122)),
        };
        var pill = new Border
        {
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(18, 11, 18, 11),
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(border),
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = text, Foreground = new SolidColorBrush(foreground), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
        };
        AutomationProperties.SetName(pill, text);
        return pill;
    }

    private static string StatusTone(string? status) => status switch
    {
        "pending" => "amber",
        "queued" or "downloading" => "sky",
        "approved" or "completed" => "emerald",
        _ => "zinc",
    };
    private Button ExternalLinkButton(string label, string url)
    {
        var button = new Button { Content = label, Tag = url, CornerRadius = new CornerRadius(18), Padding = new Thickness(12, 7, 12, 7) };
        button.Click += ExternalLink_Click;
        return button;
    }
    private static string BuildCrew(RequestMediaDetail item) { var p = new List<string>(); if (!string.IsNullOrWhiteSpace(item.Director)) p.Add($"Director: {item.Director}"); if (item.Creators?.Count > 0) p.Add($"Created by: {string.Join(", ", item.Creators)}"); if (item.Networks?.Count > 0) p.Add($"Network: {string.Join(", ", item.Networks)}"); return string.Join("  ·  ", p); }
    private static string FormatDuration(int minutes) => minutes >= 60 ? $"{minutes / 60}h{(minutes % 60 == 0 ? "" : $" {minutes % 60}m")}" : $"{minutes}m";
    private static string FormatVoteCount(int count) => count >= 1000 ? $"{count / 1000d:0.0}k" : count.ToString();
    private static string Format(string value) => string.IsNullOrWhiteSpace(value) ? "Requested" : char.ToUpperInvariant(value[0]) + value[1..];
    private static string Reason(string value) => value switch { "already_requested" => "Already requested", "already_available" => "Available", "requests_disabled" => "Requests disabled", "quota_exceeded" => "Limit reached", "blocked" => "Blocked", _ => "Unavailable" };
    private static string? Tmdb(string? path, string size) => string.IsNullOrWhiteSpace(path) ? null : $"https://image.tmdb.org/t/p/{size}{path}";
    private static void SetImage(Image image, string? url)
    {
        if (url != null)
            image.Source = (ImageSource)RemoteImageConverter.Convert(
                url,
                typeof(ImageSource),
                null!,
                string.Empty);
    }
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width <= 0) return;
        var gutter = width < 640 ? 16d : width < 1024 ? 24d : width < 1280 ? 40d : 48d;
        var compact = width < 1024;

        BackButton.Margin = new Thickness(gutter, 24, 0, 0);
        LoadingBackButton.Margin = new Thickness(gutter, 24, 0, 0);
        LowerContent.Padding = new Thickness(gutter, 20, gutter, 50);
        HeroContent.Margin = new Thickness(gutter, compact ? 96 : 104, gutter, compact ? 32 : 46);
        // Do not derive the hero from Page.ActualHeight: this page sits inside
        // a ScrollViewer, so its measured content height includes the hero
        // itself and creates a growth feedback loop. On wide windows that put
        // the title/poster below the viewport. The WebUI uses a bounded hero
        // whose height follows the viewport width.
        HeroGrid.MinHeight = compact ? 780 : Math.Clamp(width * 0.33, 620, 720);
        TitleText.FontSize = width < 640 ? 32 : width < 1024 ? 38 : 42;

        HeroContent.ColumnDefinitions[0].Width = compact
            ? new GridLength(1, GridUnitType.Star) : new GridLength(210);
        Grid.SetRow(PosterBorder, 0);
        Grid.SetColumn(PosterBorder, 0);
        Grid.SetRow(HeroInfo, compact ? 1 : 0);
        Grid.SetColumn(HeroInfo, compact ? 0 : 1);
        Grid.SetColumnSpan(HeroInfo, compact ? 2 : 1);
        PosterBorder.Width = compact ? 170 : 210;
        PosterBorder.Height = compact ? 255 : 315;

        DetailSkeleton.ColumnDefinitions[0].Width = compact
            ? new GridLength(1, GridUnitType.Star) : new GridLength(210);
        Grid.SetRow(DetailSkeletonPoster, 0);
        Grid.SetColumn(DetailSkeletonPoster, 0);
        Grid.SetRow(DetailSkeletonInfo, compact ? 1 : 0);
        Grid.SetColumn(DetailSkeletonInfo, compact ? 0 : 1);
        Grid.SetColumnSpan(DetailSkeletonInfo, compact ? 2 : 1);
        DetailSkeletonPoster.Width = compact ? 170 : 210;
        DetailSkeletonPoster.Height = compact ? 255 : 315;
        DetailSkeleton.Margin = new Thickness(gutter, compact ? 96 : 105, gutter, 50);
    }
}

public sealed record RequestDetailNavigation(string MediaType, int TmdbId);
