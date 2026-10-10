using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

internal static class HomeCurrentNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        await RetiredLayoutAsync();
        await RetiredSectionAsync();
        await ListeningCaptionsAsync(parent);
        await LateHeroSlidesAsync(parent);
        await HeroPresentationAsync(parent);
        await EpisodeCaptionsAsync(parent);
        await BottomOverlayClearanceAsync(parent);
        await SectionRhythmAsync(parent);
        ResponsiveSkeletons();
        Program.Log("PASS: current Home ownership, listening captions, and late hero slideshow regression cases.");
    }

    private static async Task BottomOverlayClearanceAsync(StackPanel parent)
    {
        var card = new LandscapeCard(); card.SetCardWidth(315);
        parent.Children.Add(card);
        try
        {
            foreach (var (source, position, expectedBottom) in new[] { ("next_up", 0d, 8d), ("continue_watching", 20d, 16d), ("continue_watching", 0d, 8d) })
            {
                card.MediaItem = new() { ContentId = "overlay-" + position + source, Title = "Overlay fixture", Type = "episode", ItemSource = source, PositionSeconds = position, DurationSeconds = 60 };
                var host = (StackPanel)card.FindName("OverlayBottomRight");
                host.Children.Add(new Border { Width = 30, Height = 14 });
                await Task.Delay(50); card.UpdateLayout();
                var painted = (FrameworkElement)host.Children.Last();
                var bottom = painted.TransformToVisual(card).TransformPoint(new Windows.Foundation.Point(0, painted.ActualHeight)).Y;
                if (Math.Abs(315 * 9d / 16d - bottom - expectedBottom) > .6)
                    throw new InvalidOperationException($"Bottom badge clearance must be {expectedBottom}px for {source}/{position}, actual {315 * 9d / 16d - bottom}.");
            }
        }
        finally { parent.Children.Remove(card); }
    }

    private static async Task SectionRhythmAsync(StackPanel parent)
    {
        var row = new SectionRow { Width = 900, Section = new() { Id = "rhythm", Title = "Continue Watching", SectionType = "continue_watching", LoadCompleted = true, Items = [new() { ContentId = "rhythm-title", Title = "Fixture", Type = "movie" }] } };
        parent.Children.Add(row);
        try
        {
            await Task.Delay(100); row.UpdateLayout();
            var header = (Grid)row.FindName("SectionHeader");
            if (Math.Abs(header.ActualHeight - 28) > .6)
                throw new InvalidOperationException($"Current carousel heading must reserve its28px line box, actual{header.ActualHeight}.");
        }
        finally { parent.Children.Remove(row); }
    }

    private static async Task EpisodeCaptionsAsync(StackPanel parent)
    {
        var card = new LandscapeCard { Width = 315, MediaItem = new() { ContentId = "same-title", Type = "episode", SeriesTitle = "Extraordinary Attorney Woo", Title = "Extraordinary Attorney Woo", SeasonNumber = 1, EpisodeNumber = 1, ItemSource = "next_up", Badges = ["season_premiere"] } };
        parent.Children.Add(card);
        try
        {
            await Task.Delay(80); card.UpdateLayout();
            if (((TextBlock)card.FindName("SubtitleText")).Text != "Season 1 Episode 1 • Extraordinary Attorney Woo")
                throw new InvalidOperationException("A real episode title matching its series name was omitted from Home's secondary caption.");
            var badge = (Border)card.FindName("BadgePill");
            if (badge.ActualHeight != 16 || ((TextBlock)card.FindName("BadgeText")).Foreground != Application.Current.Resources["AccentBrush"])
                throw new InvalidOperationException("The premiere pill must use the current16px frame and selected primary theme color.");
        }
        finally { parent.Children.Remove(card); }
    }

    private static async Task RetiredLayoutAsync()
    {
        using var wire = new Wire { HoldLayout = true };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-current.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth);
        try
        {
            var pending = vm.LoadCommand.ExecuteAsync(null);
            await wire.LayoutStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            auth.SelectProfile("beta"); wire.LayoutRelease.TrySetResult(); await pending;
            if (vm.Sections.Count != 0 || vm.HasConfiguredSections || vm.ErrorMessage != null)
                throw new InvalidOperationException("An Alpha layout response must not become Beta's Home layout.");
            wire.HoldLayout = false; await vm.LoadCommand.ExecuteAsync(null);
            await WaitUntil(() => vm.Sections.FirstOrDefault()?.Items.Count == 1);
            if (vm.Sections[0].Title != "beta") throw new InvalidOperationException("The current profile must remain able to load after the retired read settles.");
        }
        finally { vm.SetActive(false); CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(vm); }
    }

    private static async Task RetiredSectionAsync()
    {
        using var wire = new Wire { HoldSection = true };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-current.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        var vm = new HomeViewModel(new HomeApi(client), auth);
        try
        {
            await vm.LoadCommand.ExecuteAsync(null);
            await wire.SectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            client.InvalidateAccessContext(); wire.SectionRelease.TrySetResult();
            await Task.Delay(120);
            if (vm.Sections[0].LoadCompleted || vm.Sections[0].Items.Count != 0)
                throw new InvalidOperationException("A retired access-context section cannot publish merely because its profile and section id still match.");
            wire.HoldSection = false; await vm.LoadCommand.ExecuteAsync(null);
            await WaitUntil(() => vm.Sections.FirstOrDefault()?.Items.Count == 1);
        }
        finally { vm.SetActive(false); CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(vm); }
    }

    private static async Task ListeningCaptionsAsync(StackPanel parent)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-current.invalid");
        field.SetValue(null, new Services(previous, new CatalogApi(client)));
        var hero = new NowListeningHero { Width = 460, LibraryId = 7 };
        try
        {
            parent.Children.Add(hero);
            hero.Bind(new() { ContentId = "book", Type = "audiobook", Title = "Fixture book", DurationSeconds = 3600 });
            await WaitUntil(() => ((TextBlock)hero.FindName("PositionText")).Text == "Chapter 1 of 1");
            if (((TextBlock)hero.FindName("TimeLeftText")).Text != "1 hr left")
                throw new InvalidOperationException("Zero detail duration must preserve the valid deck duration.");
            if (wire.DetailReads != 1 || wire.DetailQuery != "?library_id=7")
                throw new InvalidOperationException($"A changed mounted hero must issue one correctly scoped detail read; actual {wire.DetailReads}, {wire.DetailQuery}.");
            if (((TextBlock)hero.FindName("CreditsText")).Text != "Author · Narrated by Narrator")
                throw new InvalidOperationException("Listening credits must trim empty names and surrounding whitespace.");
        }
        finally { parent.Children.Remove(hero); field.SetValue(null, previous); }
    }

    private static async Task LateHeroSlidesAsync(StackPanel parent)
    {
        var hero = new HeroCarousel { Width = 460 };
        try
        {
            parent.Children.Add(hero); await Task.Delay(80);
            hero.ItemsSource = new List<MediaItem> { new() { ContentId = "late-a", Type = "movie", Title = "First" }, new() { ContentId = "late-b", Type = "movie", Title = "Second" } };
            await Task.Delay(80);
            var animations = (bool)typeof(HeroCarousel).GetField("_animationsEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hero)!;
            var timer = (DispatcherTimer?)typeof(HeroCarousel).GetField("_autoAdvanceTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hero);
            if (animations && timer?.IsEnabled != true)
                throw new InvalidOperationException("Slides arriving after the hero mounts must start the eight-second slideshow.");
            var mountedTimer = timer;
            hero.ItemsSource = new List<MediaItem> { new() { ContentId = "late-a", Type = "movie", Title = "First refreshed" }, new() { ContentId = "late-b", Type = "movie", Title = "Second refreshed" } };
            timer = (DispatcherTimer?)typeof(HeroCarousel).GetField("_autoAdvanceTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hero);
            if (!ReferenceEquals(mountedTimer, timer)) throw new InvalidOperationException("A refresh preserving the same active slide/count must preserve its timer cycle.");
            hero.ItemsSource = new List<MediaItem> { new() { ContentId = "late-a", Type = "movie", Title = "First" } };
            timer = (DispatcherTimer?)typeof(HeroCarousel).GetField("_autoAdvanceTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hero);
            if (timer?.IsEnabled == true) throw new InvalidOperationException("A one-slide refresh must stop the slideshow timer.");
        }
        finally { parent.Children.Remove(hero); }
    }

    private static async Task HeroPresentationAsync(StackPanel parent)
    {
        var hero = new HeroCarousel { Width = 460 };
        try
        {
            parent.Children.Add(hero);
            hero.ItemsSource = new List<MediaItem> { new()
            {
                ContentId = "rating-imdb", Type = "movie", Title = "Fixture rated title", Year = 2026,
                Runtime = 120, RatingImdb = 7.35, RatingTmdb = 8, Genres = [" Drama ", "Drama", "Crime", "History"], ContentRating = " pg-13 ",
            } };
            await Task.Delay(80); hero.UpdateLayout();
            var metadata = (Panel)hero.FindName("HeroMetaPillsRow");
            var words = Texts(metadata).Where(text => text.Text != "·").Select(text => text.Text).ToArray();
            if (!words.SequenceEqual(new[] { "2026", "2h", "IMDb", "7.4", "Drama", "Crime", "PG-13" }))
                throw new InvalidOperationException("Actual hero metadata order/rating priority/deduplication differ: " + string.Join("|", words));
            if (Texts(metadata).Count(text => text.Text == "·") != 3)
                throw new InvalidOperationException("Metadata separators only belong between adjacent text entries, not around the rating component.");
            if (((TextBlock)hero.FindName("HeroOverview")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Missing hero overview must not retain its bottom margin.");
            hero.ItemsSource = new List<MediaItem> { new() { ContentId = "rating-tmdb", Type = "movie", Title = "TMDB fixture", RatingImdb = 12, RatingTmdb = 7.35 } };
            await Task.Delay(60);
            if (Texts(metadata).Any(text => text.Text == "IMDb") || !Texts(metadata).Any(text => text.Text == "7.4")
                || !Images(metadata).Any(image => image.Height == 8))
                throw new InvalidOperationException("An invalid IMDb score must yield the TMDB 8px source mark and rounded 7.4 score.");
            hero.ItemsSource = new List<MediaItem> { new() { ContentId = "rating-none", Type = "movie", Title = "No metadata", RatingImdb = -1, RatingTmdb = 0 } };
            if (metadata.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Absent metadata must remove the metadata track and its margin.");
            hero.IsTall = true; await Task.Delay(60);
            if (((Border)hero.FindName("HeroTopScrim")).Visibility != Visibility.Visible
                || ((Border)hero.FindName("HeroBottomBorder")).Visibility != Visibility.Collapsed
                || ((Border)hero.FindName("HeroVignetteBorder")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Library hero must use its top scrim and omit the Home bottom border/vignette.");
            Program.Log($"TRACE: library hero fade={((GradientStop)hero.FindName("HeroBottomFull")).Offset:R}.");
            if (Math.Abs(((GradientStop)hero.FindName("HeroBottomFull")).Offset - .9) > .000001)
                throw new InvalidOperationException("Library hero strong fade must reach solid background at 90% from the top.");
            hero.IsTall = false; await Task.Delay(60);
            if (((Border)hero.FindName("HeroTopScrim")).Visibility != Visibility.Collapsed
                || ((Border)hero.FindName("HeroBottomBorder")).Visibility != Visibility.Visible
                || ((GradientStop)hero.FindName("HeroBottomFull")).Offset != 1)
                throw new InvalidOperationException("Home hero must restore its own fade/border after a library-style change.");
            var rail = (Border)hero.FindName("ProgressRailContainer");
            if (rail.Width != 144 || rail.Height != 2)
                throw new InvalidOperationException("Hero progress rail must follow the source 144x2 contract.");
        }
        finally { parent.Children.Remove(hero); }
    }

    private static IEnumerable<TextBlock> Texts(DependencyObject owner) => Descendants(owner).OfType<TextBlock>();
    private static IEnumerable<Image> Images(DependencyObject owner) => Descendants(owner).OfType<Image>();
    private static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(owner); i++)
        {
            var child = VisualTreeHelper.GetChild(owner, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void ResponsiveSkeletons()
    {
        var poster = new SkeletonPoster();
        var row = new StackPanel(); row.Children.Add(poster);
        var section = new StackPanel(); section.Children.Add(row);
        typeof(SiloPlayer.Views.HomePage).GetMethod("ResizeSkeletonPosters", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { section, 130d });
        if (poster.Width != 130) throw new InvalidOperationException("Responsive Home skeleton width must reach posters nested inside each row.");
    }

    private static async Task WaitUntil(Func<bool> ready)
    {
        for (var attempt = 0; attempt < 100; attempt++) { if (ready()) return; await Task.Delay(20); }
        throw new InvalidOperationException("Current Home fixture did not reach its expected state.");
    }

    private sealed class Services(IServiceProvider fallback, CatalogApi catalog) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(CatalogApi) ? catalog : fallback.GetService(type); }

    private sealed class Wire : HttpMessageHandler
    {
        internal bool HoldLayout, HoldSection;
        internal int DetailReads;
        internal string? DetailQuery;
        internal TaskCompletionSource LayoutStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SectionStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource LayoutRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SectionRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "home-current.invalid") throw new InvalidOperationException("Home fixture attempted external network.");
            var profile = request.Headers.TryGetValues("X-Profile-Id", out var profiles) ? profiles.Single() : "none";
            var path = request.RequestUri.AbsolutePath;
            string body;
            if (path.EndsWith("/layout"))
            {
                LayoutStarted.TrySetResult(); if (HoldLayout) await LayoutRelease.Task; // Deliberately ignore cancellation.
                body = $$"""{"sections":[{"id":"same-section","title":"{{profile}}","section_type":"recently_added","item_limit":20}]}""";
            }
            else if (path.Contains("/home/sections/"))
            {
                SectionStarted.TrySetResult(); if (HoldSection) await SectionRelease.Task;
                body = $$"""{"id":"same-section","title":"{{profile}}","section_type":"recently_added","items":[{"content_id":"fixture","type":"movie","title":"Fixture item"}]}""";
            }
            else if (path.EndsWith("/catalog/items/book"))
            {
                DetailReads++; DetailQuery = request.RequestUri.Query;
                body = """{"content_id":"book","type":"audiobook","audiobook":{"total_duration_seconds":0,"authors":[{"name":" Author "},{"name":" "}],"narrators":[{"name":" Narrator "}]},"versions":[{"file_id":1,"duration":0,"chapters":[{"index":0,"title":"","start_seconds":10}]}]}""";
            }
            else throw new InvalidOperationException("Unexpected Home fixture request " + path);
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
