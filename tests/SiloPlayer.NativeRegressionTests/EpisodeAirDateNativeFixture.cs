using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Views;
using Windows.Graphics;

internal static class EpisodeAirDateNativeFixture
{
    private const string Game = "Bulls vs Wizards";
    private const string Pilot = "A numbered pilot";
    private const string Date = "2026-11-20";
    private const string Joined = Date + " •\u00a0" + Game;

    internal static async Task RunAsync()
    {
        var settings = SiloPlayer.App.Services.GetRequiredService<SettingsService>();
        var prefs = settings.Load();
        prefs.UiDateFormat = "YYYY-MM-DD";
        var host = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] };
        var window = new Window { Content = host };
        try
        {
            window.AppWindow.Move(new PointInt32(-20000, -20000));
            window.AppWindow.Show(false);
            await Task.Delay(80);
            foreach (var width in new[] { 1440, 480 })
            {
                const int height = 900;
                var scale = host.XamlRoot.RasterizationScale;
                window.AppWindow.ResizeClient(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
                await Task.Delay(100);
                Require(Math.Abs(host.XamlRoot.Size.Width - width) < 2, "Fixture client did not reach the requested width.");
                await CheckCards(host, width);
                await CheckDetail(host, width, height, "air_date");
                await CheckDetail(host, width, height, null);
            }
        }
        finally
        {
            host.Children.Clear();
            await Task.Delay(75);
            window.Close();
        }
    }

    private static async Task CheckCards(Grid host, int width)
    {
        var games = Card("game", "NBA", Game, 2026, 296, "air_date");
        var numbered = Card("pilot", "Numbered series", Pilot, 1, 5, null);
        var landscape = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var posters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var panel = new StackPanel { Padding = new Thickness(16), Spacing = 20, Children = { landscape, posters } };
        host.Children.Add(panel);

        foreach (var item in new[] { games, numbered })
        {
            var tile = new LandscapeCard { MediaItem = item };
            tile.SetCardWidth(width == 1440 ? 315 : 210);
            landscape.Children.Add(tile);
            var poster = new PosterCard { MediaItem = item };
            poster.SetCatalogGridLayout(width == 1440 ? 168 : 135);
            posters.Children.Add(poster);
        }
        host.UpdateLayout();
        await Task.Delay(100);

        var continueText = landscape.Children.OfType<LandscapeCard>().Select(card => Named<TextBlock>(card, "SubtitleText").Text).ToArray();
        Require(continueText[0] == Joined && continueText[1].Contains(Pilot)
            && continueText[1].Contains("1") && continueText[1].Contains("5"), "Continue Watching rendered the wrong date or numbered label.");
        var posterText = posters.Children.OfType<PosterCard>().Select(card => Named<TextBlock>(card, "SubtitleText").Text).ToArray();
        Require(posterText.SequenceEqual(new[] { Date, "S01E05" }), "Browse/search poster cards did not replace only the sports episode code.");
        await MediaParityNativeFixture.CaptureAsync(host, $"episode-air-date-cards-{width}.png");

        var hero = new HeroCarousel();
        foreach (var item in new[] { games, numbered })
        {
            Call(hero, "UpdateLocalizedMetadata", item);
            var expected = item.EpisodeLabels is not null ? Date : AppStrings.Get("web.home.hero.episodeIdentity",
                new Dictionary<string, object?> { ["season"] = 1, ["episode"] = 5 });
            Require(Descendants(Named<Panel>(hero, "HeroMetaPillsRow")).OfType<TextBlock>().Any(text => text.Text == expected),
                "Hero metadata used the wrong episode identity.");
        }

        var tonight = new WatchTonightDialog();
        var sports = new SwipeCard { Type = "episode", SeriesTitle = "NBA", Title = Game, SeasonNumber = 2026, EpisodeNumber = 296,
            EpisodeLabels = "air_date", AirDate = Date };
        var pilot = new SwipeCard { Type = "episode", SeriesTitle = "Numbered series", Title = Pilot, SeasonNumber = 1, EpisodeNumber = 5, AirDate = Date };
        Require(Descendants((DependencyObject)Call(tonight, "BuildBackFace", sports)!).OfType<TextBlock>().Any(text => text.Text == Joined)
            && Descendants((DependencyObject)Call(tonight, "BuildBackFace", pilot)!).OfType<TextBlock>().Any(text => text.Text.StartsWith("S1 E5")),
            "Watch Tonight did not preserve the two episode label styles.");

        host.Children.Remove(panel);
        Program.Log($"PASS: browse and Continue Watching cards, hero and Watch Tonight episode labels at {width}px.");
    }

    private static async Task CheckDetail(Grid host, int width, int height, string? mode)
    {
        var page = new ItemDetailPage { Width = width, Height = height };
        host.Children.Add(page);
        await Task.Delay(70);
        var sport = mode is not null;
        var title = sport ? Game : Pilot;
        var season = sport ? 2026 : 1;
        page.ViewModel.IsSeries = true;
        page.ViewModel.Item = new MediaItemDetail { Type = "series", ContentId = "fixture-series",
            Title = sport ? "NBA" : "Numbered series", EpisodeLabels = mode, EpisodeCount = 2 };
        page.ViewModel.Seasons.Add(new Season { ContentId = "fixture-season", SeasonNumber = season, EpisodeCount = 2 });
        page.ViewModel.Episodes.Add(new Episode { ContentId = "fixture-first", Title = title,
            SeasonNumber = season, EpisodeNumber = sport ? 296 : 5, AirDate = Date, Runtime = 44 });
        page.ViewModel.Episodes.Add(new Episode { ContentId = "fixture-second", Title = sport ? "Lakers vs Liberty" : "A second episode",
            SeasonNumber = season, EpisodeNumber = sport ? 297 : 6, AirDate = "2026-11-21", Runtime = 51 });
        Call(page, "UpdateUI");
        Call(page, "BuildEpisodeRows");
        Named<FrameworkElement>(page, "SeasonsSection").Visibility = Visibility.Collapsed;
        Call(page, "UpdateResponsiveLayout", (double)width);
        page.UpdateLayout();
        await Task.Delay(100);

        var grid = Named<Grid>(page, "EpisodesPanel");
        var captions = Descendants(grid).OfType<EpisodeCardCaption>().ToArray();
        Require(captions.Length == 2 && captions[0].TitleText.Text == (sport ? Game : "S01E05 · " + Pilot),
            "Series detail episode title has the wrong prefix.");
        if (sport)
        {
            Require(captions[0].MetadataRow.Visibility == Visibility.Visible && captions[0].MetadataText.Text.StartsWith(Date)
                && AutomationProperties.GetName(captions[0].TitleText) == Game,
                "The sports date is missing from the visible detail caption or the title announces its synthetic number.");
        }

        var scroll = Named<ScrollViewer>(page, "EpisodesScroll");
        scroll.StartBringIntoView();
        page.UpdateLayout();
        await MediaParityNativeFixture.CaptureAsync(host, $"episode-air-date-detail-{(sport ? "dated" : "numbered")}-{width}.png");

        var episode = new MediaItemDetail { Type = "episode", ContentId = "fixture-first", SeriesId = "fixture-series",
            SeriesTitle = "Fixture series", Title = title, SeasonNumber = season, EpisodeNumber = 5, AirDate = Date, EpisodeLabels = mode };
        page.ViewModel.Item = episode;
        Call(page, "UpdateUI");
        var expected = sport ? Date : AppStrings.Get("web.detail.episodes.numbered", new Dictionary<string, object?> { ["episode"] = 5 });
        Require(Named<Panel>(page, "BreadcrumbPanel").Children.OfType<TextBlock>().Any(text => text.Text == expected),
            "Episode detail breadcrumb did not show the expected label.");
        host.Children.Remove(page);
        Program.Log($"PASS: {(sport ? "date-labelled" : "numbered")} series episode list and detail breadcrumb at {width}px.");
    }

    private static MediaItem Card(string id, string series, string title, int season, int episode, string? mode)
        => new() { ContentId = id, SeriesId = id + "-series", SeriesTitle = series, Title = title, Type = "episode",
            SeasonNumber = season, EpisodeNumber = episode, EpisodeLabels = mode, AirDate = Date, PositionSeconds = 300, DurationSeconds = 1200 };

    private static T Named<T>(FrameworkElement owner, string name) where T : class
        => (T)owner.FindName(name);

    private static object? Call(object owner, string name, params object[] arguments)
        => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, arguments);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                yield return child;
                foreach (var node in Descendants(child)) yield return node;
            }
            yield break;
        }
        if (root is Border { Child: { } childElement })
        {
            yield return childElement;
            foreach (var node in Descendants(childElement)) yield return node;
            yield break;
        }
        if (root is ContentControl { Content: DependencyObject content })
        {
            yield return content;
            foreach (var node in Descendants(content)) yield return node;
            yield break;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var node in Descendants(child)) yield return node;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
