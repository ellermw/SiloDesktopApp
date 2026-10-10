using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Views;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;
using Windows.Foundation;

internal static class MediaFirstCaptionNativeFixture
{
    internal static async Task RunAsync()
    {
        var owner = new Grid();
        var window = new Window { Content = owner };
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.Show(false);
            await Task.Delay(75);
            foreach (var (width, height) in new[] { (1440d, 900d), (1024d, 651d), (1024d, 650d), (900d, 700d), (460d, 720d), (1920d, 1080d), (3440d, 1440d), (3840d, 2160d), (5120d, 2160d) })
            {
                var scale = owner.XamlRoot.RasterizationScale;
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
                await Task.Delay(75);
                Require(Math.Abs(owner.XamlRoot.Size.Width - width) < 2 && Math.Abs(owner.XamlRoot.Size.Height - height) < 2, "Caption test requires the real requested client bounds.");
                var page = new ItemDetailPage { Width = width, Height = height };
                owner.Children.Add(page);
                await Task.Delay(50);
                page.ViewModel.Seasons.Clear(); page.ViewModel.Episodes.Clear();
                page.ViewModel.IsSeries = false;
                page.ViewModel.Item = new MediaItemDetail { Type = "season", ContentId = "one-episode-season", Title = "Season 6", SeriesTitle = "Abbott fixture", SeasonNumber = 6, EpisodeCount = 1 };
                page.ViewModel.Episodes.Add(new Episode { ContentId = "single-episode", Title = "A complete first episode caption", EpisodeNumber = 1, SeasonNumber = 6, Runtime = 22, Overview = "The description must occupy its full first measured caption space before any scroll." });
                Call(page, "UpdateUI");
                Require(((Grid)page.FindName("BackdropContainer")).Children.OfType<Border>().First().Opacity <= .100001, "Current detail backdrop must use the10% tint rather than the legacy60% darkening layer.");
                Call(page, "BuildEpisodeRows");
                ((FrameworkElement)page.FindName("SeasonsSection")).Visibility = Visibility.Collapsed;
                Call(page, "UpdateResponsiveLayout", width);
                page.UpdateLayout();
                // No ChangeView, click, resize or extra dispatcher layout occurs
                // before these checks: the symptom concerns initial paint.
                AssertFirstCaption(page, width);
                Require(((TextBlock)page.FindName("TitleText")).Text == "Abbott fixture", "Season heading must use the series title; season identity belongs in its breadcrumb and badge.");
                var hero = (FrameworkElement)page.FindName("BackdropContainer");
                var poster = (FrameworkElement)page.FindName("HeroPosterContainer");
                var episodeNavigation = Field<ScrollViewer>(page, "_tvNavigation");
                if (width >= 1024 && height >= 651)
                {
                    Require(episodeNavigation.ActualWidth <= 1401, "Desktop season navigation must retain the WebUI1400px page shell on wide displays.");
                    var heroBounds = Bounds(hero, page); var posterBounds = Bounds(poster, page);
                    Require(heroBounds.Bottom - posterBounds.Bottom <= 25, "Season poster must sit at the hero's bottom instead of leaving unused space below its copy.");
                }
                var viewport = Field<Grid>(page, "_tvViewport");
                if (width >= 1024 && height >= 651)
                    Require(double.IsNaN(viewport.Height) && viewport.MinHeight == height, "Tall single-episode season must grow rather than clip the caption in a fixed rail.");
                await MediaParityNativeFixture.CaptureAsync(owner, $"media-first-caption-{width}x{height}.png");

                for (var number = 2; number <= 26; number++)
                    page.ViewModel.Episodes.Add(new Episode { ContentId = $"episode-{number}", Title = $"Episode {number}", EpisodeNumber = number, Overview = "A variable caption on a long season." });
                Call(page, "BuildEpisodeRows"); page.UpdateLayout(); await Task.Delay(75); page.UpdateLayout();
                var grid = (Grid)page.FindName("EpisodesPanel");
                var scroll = (ScrollViewer)page.FindName("EpisodesScroll");
                if (width >= 1024)
                {
                    var fourth = grid.Children.OfType<FrameworkElement>().First(card => Grid.GetRow(card) == 3);
                    var fifth = grid.Children.OfType<FrameworkElement>().First(card => Grid.GetRow(card) == 4);
                    var fourthBounds = Bounds(fourth, grid); var fifthBounds = Bounds(fifth, grid);
                    Require(double.IsFinite(scroll.MaxHeight) && scroll.MaxHeight >= fourthBounds.Bottom - 1 && scroll.MaxHeight < fifthBounds.Top,
                        "Long season must expose exactly four complete measured rows and scroll the remaining rows.");
                }
                page.ViewModel.Episodes.RemoveAt(page.ViewModel.Episodes.Count - 1);
                while (page.ViewModel.Episodes.Count > 1) page.ViewModel.Episodes.RemoveAt(1);
                Call(page, "BuildEpisodeRows"); page.UpdateLayout();
                Require(double.IsPositiveInfinity(scroll.MaxHeight), "Returning to one episode must remove the old four-row cap.");
                owner.Children.Remove(page);

                if (width >= 1024 && height >= 651)
                    await AssertSeasonRailAsync(owner, width, height);

                await AssertEpisodeRailStatesAsync(owner, width, height);
            }
            Program.Log("PASS: MEDIA_FIRST_CAPTION_COMPLETED initial single-episode caption, four-row cap/reset, and loading/empty/single/multiple/error sibling geometry.");
        }
        finally { window.Close(); }
    }

    private static void AssertFirstCaption(ItemDetailPage page, double width)
    {
        var grid = (Grid)page.FindName("EpisodesPanel");
        var card = grid.Children.OfType<FrameworkElement>().Single();
        var caption = Descendants(card).OfType<TextBlock>().Where(text => text.Visibility == Visibility.Visible && text.ActualHeight > 0).ToArray();
        Require(caption.Any(text => text.Text == "Episode 1") && caption.Any(text => text.Text == "A complete first episode caption"), "Initial caption must contain the episode number and title.");
        var scroll = (ScrollViewer)page.FindName("EpisodesScroll");
        var railBounds = Bounds(scroll, page);
        var navigationBounds = Bounds(Field<ScrollViewer>(page, "_tvNavigation"), page);
        foreach (var text in caption)
        {
            var bounds = Bounds(text, page);
            Require(bounds.Height > 0 && bounds.Top >= railBounds.Top - 1 && bounds.Bottom <= railBounds.Bottom + 1,
                $"Initial caption '{text.Text}' is clipped inside EpisodesScroll at {width}px.");
            Require(bounds.Top >= navigationBounds.Top - 1 && bounds.Bottom <= navigationBounds.Bottom + 1,
                $"Initial caption '{text.Text}' is clipped by the outer TV navigation at {width}px.");
        }
        if (width >= 1024) Require(caption.Any(text => text.Text.StartsWith("The description")), "Desktop initial caption must include the description.");
        var still = Descendants(card).OfType<Panel>().Single(panel => panel.GetType().Name == "EpisodeStillAspectPanel");
        Require(Math.Abs(still.ActualHeight - still.ActualWidth * 9 / 16) < 1, "Still must have its16:9 height during the first measure.");
        var placeholder = Descendants(card).OfType<Viewbox>().Single(box => box.Name == "EpisodeStillPlaceholder");
        Require(placeholder.Width == 32 && placeholder.Height == 32 && Math.Abs(placeholder.Opacity - .3) < .000001, "No-art episode must use the current32px/30% Play placeholder.");
        Program.Log($"TRACE: first caption width={width}, card={card.ActualWidth}x{card.ActualHeight}, rail={railBounds}, still={still.ActualWidth}x{still.ActualHeight}.");
    }

    private static async Task AssertSeasonRailAsync(Grid owner, double width, double height)
    {
        var page = new ItemDetailPage { Width = width, Height = height };
        owner.Children.Add(page); await Task.Delay(50);
        try
        {
            page.ViewModel.Item = new MediaItemDetail { Type = "series", ContentId = "season-rail-fixture", Title = "Series fixture" };
            page.ViewModel.IsSeries = true;
            for (var n = 1; n <= 6; n++) page.ViewModel.Seasons.Add(new Season { ContentId = $"season-{n}", SeasonNumber = n, EpisodeCount = n });
            Call(page, "UpdateUI"); Call(page, "BuildSeasonCards");
            ((FrameworkElement)page.FindName("SeasonsSection")).Visibility = Visibility.Visible;
            Call(page, "UpdateResponsiveLayout", width); page.UpdateLayout(); await Task.Delay(50); page.UpdateLayout();
            var navigation = Field<ScrollViewer>(page, "_tvNavigation");
            var card = ((StackPanel)page.FindName("SeasonsPanel")).Children.OfType<FrameworkElement>().First();
            Program.Log($"TRACE: season rail {width}x{height}: navigation={navigation.ActualHeight}, card={card.ActualWidth}x{card.ActualHeight}.");
            Require(Math.Abs(card.ActualHeight - 255.5) < 2, "Season card must retain the current210px poster and19.5px/16px caption lines.");
            Require(Math.Abs(navigation.ActualHeight - 347.5) < 3, "Season rail must include current header and20px carousel bottom space rather than inflate the hero.");
        }
        finally { owner.Children.Remove(page); }
    }

    private static async Task AssertEpisodeRailStatesAsync(Grid owner, double width, double height)
    {
        var page = new ItemDetailPage { Width = width, Height = height };
        owner.Children.Add(page); await Task.Delay(50);
        page.ViewModel.Seasons.Clear(); page.ViewModel.Episodes.Clear(); page.ViewModel.IsSeries = false;
        page.ViewModel.Item = new MediaItemDetail { Type = "episode", ContentId = "episode-detail", Title = "Episode detail", SeasonNumber = 6, EpisodeNumber = 1 };
        Call(page, "UpdateUI");
        Require(((FrameworkElement)page.FindName("EpisodeContextText")).Visibility == Visibility.Collapsed && ((FrameworkElement)page.FindName("FavoriteButton")).Visibility == Visibility.Collapsed, "Episode header must retain its breadcrumb and current watched action bar without duplicate episode identity/favorite controls.");
        var siblings = (FrameworkElement)page.FindName("SiblingEpisodesSection");
        var skeleton = (FrameworkElement)page.FindName("SiblingEpisodesLoadingSkeleton");
        var panel = (StackPanel)page.FindName("SiblingEpisodesPanel");
        var siblingScroll = (FrameworkElement)page.FindName("SiblingEpisodesScrollViewer");
        var viewport = Field<Grid>(page, "_tvViewport");
        var navigation = Field<ScrollViewer>(page, "_tvNavigation");
        // Geometry uses the real named section controls. API success/failure
        // authority stays covered by the existing owned load fixtures.
        foreach (var state in new[] { "loading", "empty", "single", "multiple", "error" })
        {
            var visible = state is "loading" or "multiple";
            panel.Children.Clear();
            if (state == "multiple") for (var n = 0; n < 3; n++) panel.Children.Add(new LandscapeCard { MediaItem = new MediaItem { ContentId = $"sibling-{n}", Title = $"Sibling episode {n}", Type = "episode" } });
            skeleton.Visibility = state == "loading" ? Visibility.Visible : Visibility.Collapsed;
            siblingScroll.Visibility = state == "multiple" ? Visibility.Visible : Visibility.Collapsed;
            siblings.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            Call(page, "SizeTvViewport"); page.UpdateLayout(); await Task.Delay(50);
            Require(navigation.Visibility == (visible ? Visibility.Visible : Visibility.Collapsed), $"{state}: inactive siblings must not retain their navigation shell.");
            if (state == "multiple")
            {
                var expectedWidth = width < 1024 ? 160 : height <= 450 ? 140 : height <= 650 ? 180 : 240;
                Require(panel.Children.OfType<LandscapeCard>().All(card => Math.Abs(card.ActualWidth - expectedWidth) < 2), "Current sibling cards must use the responsive160/140/180/240px width.");
                Require(((StackPanel)siblings).Spacing == 4, "Episode rail heading must use the current4px bottom gap.");
            }
            if (!visible) Require(viewport.RowDefinitions[1].Height.Value == 0, $"{state}: an absent rail must reserve no second navigation row.");
            // MediaLocations is permission dependent; use its actual section
            // to measure spacing without requiring any personal credentials.
            var locations = (StackPanel)page.FindName("MediaLocationsSection");
            locations.Visibility = Visibility.Visible;
            page.UpdateLayout();
            var flowGap = Bounds(locations, page).Top - Bounds(viewport, page).Bottom;
            Require(Math.Abs(flowGap - (width < 1024 ? 28 : 40)) < 2, $"{state}: Media locations gap must equal the current supporting-content padding, actual{flowGap}.");
            if (width >= 1024) Require(Math.Abs(viewport.ActualHeight - height) < 2, $"{state}: desktop episode must preserve the current full viewport hero contract.");
            Program.Log($"TRACE: sibling state={state} {width}x{height}, viewport={viewport.ActualHeight}, rail={navigation.ActualHeight}, Media locations gap={flowGap}.");
        }
        AssertCurrentSemantics(page);
        owner.Children.Remove(page);
    }

    private static void AssertCurrentSemantics(ItemDetailPage page)
    {
        var credits = new List<CrewMember> { new() { Name = "Creator fixture", Job = "Creator" }, new() { Name = "Director fixture", Job = "Director" } };
        page.ViewModel.Item = new MediaItemDetail { Type = "series", ContentId = "credits", Crew = credits };
        Call(page, "BuildHeroCrewLine", page.ViewModel.Item); Call(page, "BuildCrew", credits);
        var hero = (TextBlock)page.FindName("HeroCrewLine");
        var copy = string.Concat(hero.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Select(run => run.Text));
        Require(copy.Contains("Created by Creator fixture") && !copy.Contains("Director fixture"), "Series hero must prefer Creator credits to Director.");
        Require(((FrameworkElement)page.FindName("CreatorsPanel")).Visibility == Visibility.Visible, "Series supporting crew must include Creator credits.");
        credits.RemoveAt(0); Call(page, "BuildHeroCrewLine", page.ViewModel.Item);
        copy = string.Concat(hero.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Select(run => run.Text));
        Require(copy.Contains("Created by Director fixture"), "Series with no Creator must retain Director fallback.");
        foreach (var type in new[] { "movie", "series", "season", "episode" })
        {
            page.ViewModel.Item = new MediaItemDetail { Type = type, ContentId = "menu", Crew = [new CrewMember { Name = "Creator fixture", Job = "Creator" }] };
            Call(page, "BuildCrew", page.ViewModel.Item.Crew);
            Require(((FrameworkElement)page.FindName("CreatorsPanel")).Visibility == (type == "series" ? Visibility.Visible : Visibility.Collapsed), "Creator row must clear when leaving series detail.");
            Call(page, "BuildMoreFlyout");
            var menu = (MenuFlyout)page.FindName("MoreFlyout");
            Require(menu.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Add to Collection") == (type is "movie" or "series"), "Only the current source's eligible detail types may add to collections.");
        }
        var formatter = typeof(ItemDetailPage).GetMethod("FormatSeasonProgressText", BindingFlags.NonPublic | BindingFlags.Static)!;
        Require((string)formatter.Invoke(null, [new Season { EpisodeCount = 1 }])! == "1 episode", "Single-episode season must use singular copy.");
        Require((string)formatter.Invoke(null, [new Season { EpisodeCount = 1, UserData = new SeasonUserData { WatchedCount = 1 } }])! == "1 of 1 episode", "Single-episode progress must retain singular copy.");
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement relative) => element.TransformToVisual(relative).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static object? Call(object value, string method, params object?[] args) => value.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, args);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject value)
    {
        for (var n = 0; n < VisualTreeHelper.GetChildrenCount(value); n++)
        {
            var child = VisualTreeHelper.GetChild(value, n); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
