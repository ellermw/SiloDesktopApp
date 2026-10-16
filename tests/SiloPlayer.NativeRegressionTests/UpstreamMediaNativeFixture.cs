using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class UpstreamMediaNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = (IServiceProvider)slot.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://upstream-media-fixture.invalid"); client.SetProfile("media-profile");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetCurrentUser(new UserInfo { Id = "fixture", Role = "user" });
        var catalog = new CatalogApi(client); var cache = new ItemDetailPrefetchCache(catalog, auth);
        using var spoilers = new EpisodeSpoilerPreferences(new SettingsApi(client));
        slot.SetValue(null, new Services(original, client, auth, catalog, cache, spoilers, http));
        using var lifetime = new CancellationTokenSource();
        var page = new ItemDetailPage { Width = 1280, Height = 720 };
        var hero = new HeroCarousel { Width = 1280, Height = 500 };
        try
        {
            parent.Children.Add(page); await Task.Delay(80);
            var detail = new MediaItemDetail { ContentId = "fixture-movie", Type = "movie", Title = "A shared Cast & Crew row", Overview = "A human-written overview.", Tagline = "Une histoire traduite.", MachineTranslatedFields = ["tagline"], UserData = new() { Played = true, PositionSeconds = 80, DurationSeconds = 120 } };
            for (var i = 1; i <= 3; i++) { detail.Crew.Add(new() { PersonId = "crew" + i, Name = "Crew Person " + i, Job = "Director" }); detail.Crew.Add(new() { PersonId = "crew" + i, Name = "Crew Person " + i, Job = "Writer" }); }
            for (var i = 0; i < 15; i++) detail.Cast.Add(new() { PersonId = "cast" + i, Name = "Cast Person " + i, Character = "Character " + i, Order = i });
            page.ViewModel.Item = detail; Set(page, "_navigationCts", lifetime); Set(page, "_currentContentId", detail.ContentId);
            Call(page, "UpdateUI"); await Task.Delay(100); page.UpdateLayout();
            var castPanel = (StackPanel)page.FindName("CastPanel");
            if (((TextBlock)page.FindName("CastHeader")).Text != "Cast & Crew" || castPanel.Children.OfType<Button>().Count() != 15 || ((FrameworkElement)page.FindName("CrewSection")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Combined crew/cast caps, ordering or hidden separate crew section differ.");
            if (((FrameworkElement)page.FindName("OverviewTranslationLabel")).Visibility != Visibility.Collapsed || ((FrameworkElement)page.FindName("TaglineTranslationLabel")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("A machine tagline incorrectly labels a human overview.");
            foreach (var width in new[] { 460d, 1280d, 3440d, 3840d })
            {
                page.Width = width; Call(page, "UpdateResponsiveLayout", width); page.UpdateLayout(); await Task.Delay(80);
                await MediaParityNativeFixture.CaptureAsync((FrameworkElement)page.FindName("CastSection"), $"upstream-cast-crew-{width}.png");
            }
            Call(page, "BuildMoreFlyout"); await Task.Delay(80);
            var menu = (MenuFlyout)page.FindName("MoreFlyout");
            var reset = menu.Items.OfType<MenuFlyoutItem>().Single(x => x.Text == "Reset Progress");
            if (reset.Visibility != Visibility.Visible) throw new InvalidOperationException("Rewatched movie with resume position lacks supported Reset Progress.");
            wire.SupportsReset = false; Call(page, "BuildMoreFlyout"); await Task.Delay(60);
            reset = menu.Items.OfType<MenuFlyoutItem>().Single(x => x.Text == "Reset Progress");
            if (reset.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Old server exposes Reset Progress.");
            foreach (var type in new[] { "episode", "season" })
            {
                var item = new MediaItem { ContentId = type, Type = type, UserState = new() };
                var cardMenu = MediaItemMenu.Build(item, owner: page);
                if (cardMenu.Items.OfType<MenuFlyoutItem>().Any(x => x.Text.StartsWith("Add to Favorites") || x.Text.StartsWith("Add to Watchlist"))) throw new InvalidOperationException("Episode/season offers adding to a title list.");
                item.UserState.IsFavorite = item.UserState.InWatchlist = true;
                cardMenu = MediaItemMenu.Build(item, owner: page);
                if (!cardMenu.Items.OfType<MenuFlyoutItem>().Any(x => x.Text == "Remove from Favorites") || !cardMenu.Items.OfType<MenuFlyoutItem>().Any(x => x.Text == "Remove from Watchlist")) throw new InvalidOperationException("Episode/season loses saved-list removal.");
                var favoriteAction = cardMenu.Items.OfType<MenuFlyoutItem>().Single(x => x.Text == "Remove from Favorites");
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(favoriteAction).GetPattern(PatternInterface.Invoke)).Invoke();
                await WaitAsync(() => !item.UserState.IsFavorite);
                if (favoriteAction.Visibility == Visibility.Visible) throw new InvalidOperationException("Removing a saved episode/season exposes an Add to Favorites action.");
                var watchlistAction = cardMenu.Items.OfType<MenuFlyoutItem>().Single(x => x.Text == "Remove from Watchlist");
                ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(watchlistAction).GetPattern(PatternInterface.Invoke)).Invoke();
                await WaitAsync(() => !item.UserState.InWatchlist);
                if (watchlistAction.Visibility == Visibility.Visible) throw new InvalidOperationException("Removing a saved episode/season exposes an Add to Watchlist action.");

            }
            detail.Cast.Clear(); detail.Crew.Clear(); Call(page, "BuildCast", detail.Cast);
            if (((FrameworkElement)page.FindName("CastSection")).Visibility != Visibility.Collapsed) throw new InvalidOperationException("Empty credits reserve a row.");
            wire.SpoilersOn = true; spoilers.Invalidate(); await spoilers.EnsureLoadedAsync();
            detail = new MediaItemDetail { ContentId = "hidden-episode", Type = "episode", Title = "Protected episode", Overview = "The secret ending is exposed here.", UserData = new() };
            page.ViewModel.Item = detail; Set(page, "_currentContentId", detail.ContentId);
            Call(page, "UpdateUI"); await Task.Delay(100);
            var overview = (FrameworkElement)page.FindName("OverviewText");
            var reveal = (FrameworkElement)page.FindName("RevealEpisodeOverviewButton");
            if (overview.Visibility != Visibility.Collapsed || reveal.Visibility != Visibility.Visible) throw new InvalidOperationException("Episode description is visible before an explicit reveal.");
            Call(page, "RevealEpisodeOverviewButton_Click", reveal, new RoutedEventArgs());
            if (overview.Visibility != Visibility.Visible || reveal.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Episode reveal does not expose the description.");
            detail.Overview = "A newly translated secret ending."; Call(page, "UpdateUI"); await Task.Delay(80);
            if (overview.Visibility != Visibility.Visible) throw new InvalidOperationException("Translation loses the same-episode reveal.");
            detail.ContentId = "different-episode"; Set(page, "_currentContentId", detail.ContentId); Call(page, "UpdateUI"); await Task.Delay(80);
            if (overview.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Reveal leaked to a different episode.");
            detail.Type = "movie"; Call(page, "UpdateUI"); await Task.Delay(60);
            if (overview.Visibility != Visibility.Visible || reveal.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Episode spoiler presentation leaked into a movie detail.");
            detail.Type = "season"; detail.ContentId = "season"; Set(page, "_currentContentId", "season");
            page.ViewModel.Episodes.Add(new Episode { ContentId = "listed", Title = "Listed episode", Overview = "Listed secret.", StillUrl = "https://upstream-media-fixture.invalid/still", PendingTranslationLanguage = "fr", MachineTranslatedFields = ["overview"] });
            Call(page, "BuildEpisodeRows"); await Task.Delay(180);
            if (((FrameworkElement)page.FindName("TranslateEpisodesButton")).Visibility != Visibility.Visible) throw new InvalidOperationException("Listed episode pending language lacks the season translation action.");
            var texts = Descendants((FrameworkElement)page.FindName("EpisodesPanel")).OfType<TextBlock>().Where(x => x.Text == "Listed secret.").ToList();
            if (texts.Count == 0 || texts.Any(x => x.Visibility != Visibility.Collapsed)) throw new InvalidOperationException("Listed unstarted episode description is exposed.");
            var cardItem = new MediaItem { ContentId = "spoiler-card", Type = "episode", Title = "Protected still", PosterUrl = "https://upstream-media-fixture.invalid/still", BackdropUrl = "https://upstream-media-fixture.invalid/still", UserState = new() };
            var landscape = new LandscapeCard { MediaItem = cardItem };
            var poster = new PosterCard { MediaItem = cardItem };
            parent.Children.Remove(page); parent.Children.Add(landscape); parent.Children.Add(poster);
            await WaitAsync(() => ((Image)landscape.FindName("BackdropImage")).Source != null && ((Image)poster.FindName("PosterImage")).Source != null);
            var image = (Image)landscape.FindName("BackdropImage"); landscape.UpdateLayout();
            if ((bool?)Field(landscape, "_spoilerArtworkHidden") != true || (bool?)Field(poster, "_spoilerArtworkHidden") != true) throw new InvalidOperationException("Unstarted selected still did not pass through blur rendering.");
            var blurredContrast = await ContrastAsync(image);
            await MediaParityNativeFixture.CaptureAsync(landscape, "upstream-spoiler-blurred.png");
            cardItem.UserState.Played = true; Call(landscape, "RefreshMenuState", cardItem); poster.RefreshState();
            await WaitAsync(() => image.Source is BitmapImage { UriSource: not null });
            var clearContrast = await ContrastAsync(image);
            if (!(blurredContrast < clearContrast * .8)) throw new InvalidOperationException($"Native still pixels are not blurred: hidden contrast={blurredContrast}, visible={clearContrast}.");
            await MediaParityNativeFixture.CaptureAsync(landscape, "upstream-spoiler-watched-clear.png");
            Program.Log($"PASS: native spoiler pixel contrast hidden={blurredContrast:F3} < watched={clearContrast:F3}; episode reveal persists through translation and resets on new item; listed descriptions hidden with season translation button.");
            parent.Children.Remove(landscape); parent.Children.Remove(poster);
            var room = new WatchTogetherRoomPage { Width = 900, Height = 720 };
            var pickerText = new TextBlock { Tag = cardItem, Text = "Room picker secret." };
            var pickerImage = new Image { Tag = cardItem, Width = 112, Height = 63, Stretch = Stretch.UniformToFill };
            parent.Children.Add(room); parent.Children.Add(pickerText); parent.Children.Add(pickerImage); await Task.Delay(80);
            cardItem.UserState.Played = false;
            Call(room, "EpisodeOverview_Loaded", pickerText, new RoutedEventArgs());
            await (Task)Call(room, "LoadPickerEpisodeStillAsync", pickerImage, cardItem)!;
            if (pickerText.Visibility != Visibility.Collapsed || pickerImage.Source is not BitmapImage { UriSource: null }) throw new InvalidOperationException("Room picker did not hide the unstarted description and transform its still.");
            await MediaParityNativeFixture.CaptureAsync(pickerImage, "upstream-room-picker-blurred.png");
            room.ViewModel.Dispose(); parent.Children.Remove(room); parent.Children.Remove(pickerText); parent.Children.Remove(pickerImage);
            Program.Log("PASS: room picker production description/still renderers apply unstarted episode spoiler preferences.");

            wire.SpoilersOn = false; spoilers.Invalidate(); await spoilers.EnsureLoadedAsync();
            parent.Children.Remove(page);
            parent.Children.Add(hero); hero.ItemsSource = new List<MediaItem> { new() { ContentId = "hero", Type = "movie", Title = "Visible translated hero", Overview = "Une description.", PendingTranslationLanguage = "fr", MachineTranslatedFields = ["overview"] } };
            await Task.Delay(120); hero.UpdateLayout();
            if (((FrameworkElement)hero.FindName("HeroTranslateButton")).Visibility != Visibility.Visible || ((FrameworkElement)hero.FindName("HeroTranslationLabel")).Visibility != Visibility.Visible) throw new InvalidOperationException("Visible Featured hero lacks field provenance/on-view button.");
            await MediaParityNativeFixture.CaptureAsync(hero, "upstream-hero-translation.png");
            ((CancellationTokenSource)Field(hero, "_heroTranslationCts")!).Cancel();
            Call(hero, "HeroTranslateButton_Click", hero.FindName("HeroTranslateButton"), new RoutedEventArgs());
            await WaitAsync(() => wire.TranslationWrites > 0);
            Program.Log("PASS: a timed-out visible hero translation can be explicitly retried with a fresh cancellation scope.");
            wire.HoldAi = true;
            hero.ItemsSource = new List<MediaItem> { new() { ContentId = "retired-slide", Type = "movie", Title = "Retired", Overview = "Retired overview", PendingTranslationLanguage = "fr" } };
            await wire.AiRequested.Task.WaitAsync(TimeSpan.FromSeconds(3));
            hero.ItemsSource = new List<MediaItem> { new() { ContentId = "active-slide", Type = "movie", Title = "Active", Overview = "Active overview" } };
            wire.AiRelease.TrySetResult(); await Task.Delay(100);
            if (((FrameworkElement)hero.FindName("HeroTranslateButton")).Visibility != Visibility.Collapsed || ((TextBlock)hero.FindName("HeroOverview")).Text != "Active overview") throw new InvalidOperationException("Retired slide translation capability painted on the active slide.");
            wire.AiRequested = new(TaskCreationOptions.RunContinuationsAsynchronously); wire.AiRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            hero.ItemsSource = new List<MediaItem> { new() { ContentId = "retired-profile", Type = "movie", Title = "Retired profile", PendingTranslationLanguage = "fr" } };
            await wire.AiRequested.Task.WaitAsync(TimeSpan.FromSeconds(3));
            client.SetProfile("changed-profile"); wire.AiRelease.TrySetResult(); await Task.Delay(100);
            if (((FrameworkElement)hero.FindName("HeroTranslateButton")).Visibility != Visibility.Collapsed) throw new InvalidOperationException("Retired profile translation capability exposed an active action.");
            Program.Log("PASS: hero capability completions are rejected after slide and profile retirement.");

            Program.Log("PASS: upstream media reset capabilities including rewatches/old server, season/episode add/remove eligibility, field-specific detail/hero provenance, hero on-view button, combined crew/cast caps and empty row; cast renders at 460/1280/3440/3840 widths.");
        }
        finally { lifetime.Cancel(); parent.Children.Remove(page); parent.Children.Remove(hero); slot.SetValue(null, original); }
    }
    private static object? Field(object o, string name) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
    private static async Task WaitAsync(Func<bool> ready)
    { for (var i = 0; i < 100; i++) { if (ready()) return; await Task.Delay(50); } throw new TimeoutException("Native media render did not settle."); }
    private static async Task<double> ContrastAsync(FrameworkElement image)
    {
        image.UpdateLayout(); await Task.Delay(100);
        var render = new RenderTargetBitmap(); await render.RenderAsync(image);
        var pixels = (await render.GetPixelsAsync()).ToArray();
        double delta = 0; var count = 0;
        for (var y = 1; y < render.PixelHeight - 1; y++)
        for (var x = 1; x < render.PixelWidth - 1; x++)
        for (var c = 0; c < 3; c++)
        { var offset = (y * render.PixelWidth + x) * 4 + c; delta += Math.Abs(pixels[offset] - pixels[offset - 4]); count++; }
        return delta / Math.Max(1, count);
    }

    private static object? Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(o, args);
    private static void Set(object o, string name, object value) => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, value);
    private sealed class Services(IServiceProvider original, SiloApiClient client, AuthService auth, CatalogApi catalog, ItemDetailPrefetchCache cache, EpisodeSpoilerPreferences spoilers, HttpClient http) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(WatchTogetherRoomViewModel) ? new WatchTogetherRoomViewModel(new PlaybackApi(client), client) : type == typeof(EpisodeSpoilerPreferences) ? spoilers : type == typeof(HttpClient) ? http : type == typeof(SiloApiClient) ? client : type == typeof(AuthService) ? auth : type == typeof(CatalogApi) ? catalog : type == typeof(ItemDetailViewModel) ? new ItemDetailViewModel(catalog, cache) : type == typeof(UICustomizationService) ? new UICustomizationService(new SettingsApi(client)) : type == typeof(ItemDetailPrefetchCache) ? cache : original.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        public bool SupportsReset = true;
        public bool SpoilersOn;
        public bool HoldAi;
        public int TranslationWrites;
        public TaskCompletionSource AiRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AiRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/translate-description")) TranslationWrites++;
            if (path == "/api/v2/capabilities/metadata-ai" && HoldAi) { AiRequested.TrySetResult(); await AiRelease.Task; }
            if (path == "/still") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "audiobook-cover.png"))) };
            var spoilerValue = SpoilersOn ? "true" : "false";
            var body = path == "/api/v2/settings/contract/capabilities" ? "{\"api_version\":1,\"manifest_revision\":17,\"supports_batched_effective\":true}"
                : path == "/api/v2/settings/values/effective" ? "{\"items\":[{\"key\":\"catalog.hide_unwatched_episode_images\",\"value\":" + spoilerValue + "},{\"key\":\"catalog.hide_unwatched_episode_overviews\",\"value\":" + spoilerValue + "}]}"
                : path == "/api/v2/progress/capabilities" ? (SupportsReset ? "{\"clear_resume_position\":true}" : "{}")
                : path == "/api/v2/capabilities/metadata-ai" ? "{\"state\":\"available\",\"allowed\":true,\"on_view\":\"button\"}" : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
