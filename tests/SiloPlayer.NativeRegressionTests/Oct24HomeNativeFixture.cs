using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media;
using SiloPlayer;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Messaging;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class Oct24HomeNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var field = typeof(App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "home-images"));
        var client = new SiloApiClient(http); client.SetBaseUrl("https://home-warm.fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SelectProfile("alpha");
        using var spoilers = new EpisodeSpoilerPreferences(new SettingsApi(client));
        var firstOwner = client.CaptureContext();
        var vm = new HomeViewModel(new HomeApi(client), auth);
        var navigation = new NavigationService();
        var events = new EventChannelClient(client, auth);
        var local = new SettingsService(Path.Combine(Program.ResultDirectory, "home-local"));
        field.SetValue(null, new Services(previous, vm, auth, navigation, events, local, new CatalogApi(client), client, http, images, spoilers));
        var frame = new Frame { Width = 1200, Height = 750, CacheSize = 8 };
        navigation.Frame = frame; parent.Children.Add(frame);
        try
        {
            navigation.Navigate<HomePage>();
            await WaitUntil(() => frame.Content is HomePage p && vm.Sections.Count == 8 && vm.Sections.All(s => s.LoadCompleted)
                && ((StackPanel)p.FindName("SectionsPanel")).Children.Count == 8);
            await Task.Delay(120);
            var page = (HomePage)frame.Content;
            var panel = (StackPanel)page.FindName("SectionsPanel");
            var rows = panel.Children.ToArray();
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_HOME_GUARDS_ONLY") == "1")
            {
                var row = (SectionRow)rows[0]; var section = row.Section!;
                section.Items.Add(new() { ContentId = "temporary", Type = "movie", Title = "Temporary" });
                row.Section = null;
                await Task.Delay(80);
                row.Section = section; section.Items.RemoveAt(section.Items.Count - 1);
                await Task.Delay(80);
                Program.Log("PASS: queued section collection callback ignores a retired/null section.");
                return;
            }
            var repeater = (ItemsRepeater)((SectionRow)rows[0]).FindName("CardsRepeater");
            await WaitUntil(() => repeater.TryGetElement(0) is FrameworkElement { IsLoaded: true });
            var card = repeater.TryGetElement(0);
            var itemsSource = repeater.ItemsSource;
            await WaitUntil(() => ((Image)((PosterCard)card!).FindName("PosterImage")).Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 });
            // Pixel metadata can arrive before ImageOpened/native composition.
            // Capture a rendered card before testing retention of seen artwork.
            await MediaParityNativeFixture.CaptureAsync((FrameworkElement)card!, "home-warm-before.png");
            var artwork = ((Image)((PosterCard)card!).FindName("PosterImage")).Source;
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_HOME_CATCHUP_ONLY") == "1")
            {
                var retained = vm.Sections.ToArray();
                var refreshedContext = client.InvalidateAccessContext();
                await page.CatchUpAfterReconnectAsync(refreshedContext);
                await WaitUntil(() => vm.Sections.All(section => section.LoadCompleted));
                Require(retained.SequenceEqual(vm.Sections), "Transport catch-up discarded retained view-model rows.");
                navigation.Navigate(typeof(Page)); await Task.Delay(60); navigation.GoBack(); await Task.Delay(120);
                Require(rows.SequenceEqual(panel.Children), "Transport catch-up rebuilt unchanged native Home rows on return.");
                Require(ReferenceEquals(card, repeater.TryGetElement(0)), "Transport catch-up rebuilt unchanged native Home cards.");
                Program.Log("PASS: intentional reconnect read-generation catch-up retains the same native rows/cards across Home return.");
                client.InvalidateAccessContext(); vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
                await WaitUntil(() => vm.Sections.Count == 8 && vm.Sections.All(section => section.LoadCompleted));
                navigation.Navigate(typeof(Page)); await Task.Delay(60); navigation.GoBack(); await Task.Delay(120);
                Require(panel.Children.Count == 8 && !rows.SequenceEqual(panel.Children),
                    "An ordinary access retirement incorrectly retained permission-scoped Home controls.");
                Program.Log("PASS: an ordinary access-context retirement still replaces permission-scoped native Home controls.");
                return;
            }
            var decodedArtwork = rows.OfType<SectionRow>().Select(row => ((ItemsRepeater)row.FindName("CardsRepeater")).TryGetElement(0))
                .OfType<PosterCard>().Where(candidate => candidate.MediaItem?.ContentId == "movie0")
                .Select(candidate => ((Image)candidate.FindName("PosterImage")).Source).Where(source => source != null).ToArray();
            navigation.Navigate(typeof(Page)); await Task.Delay(80);
            navigation.GoBack(); await Task.Delay(220);
            Require(ReferenceEquals(page, frame.Content) && rows.SequenceEqual(panel.Children), "No-change control round trip rebuilt Home rows.");
            Require(ReferenceEquals(itemsSource, repeater.ItemsSource), "No-change control round trip replaced ItemsSource.");
            await WaitUntil(() => repeater.TryGetElement(0) is FrameworkElement { IsLoaded: true });
            Program.Log($"TRACE no-change Frame round trip: sameContainer={ReferenceEquals(card, repeater.TryGetElement(0))}, oldLoaded={(card as FrameworkElement)?.IsLoaded}, sameRows={rows.SequenceEqual(panel.Children)}, sameItemsSource={ReferenceEquals(itemsSource, repeater.ItemsSource)}.");
            Program.Log($"TRACE no-change artwork: sameDecoded={ReferenceEquals(artwork, ((Image)((PosterCard)repeater.TryGetElement(0)!).FindName("PosterImage")).Source)}.");
            Program.Log($"TRACE same-artwork decoded set: count={decodedArtwork.Length}, reusedPredecoded={decodedArtwork.Any(source => ReferenceEquals(source, ((Image)((PosterCard)repeater.TryGetElement(0)!).FindName("PosterImage")).Source))}, sourceNull={((Image)((PosterCard)repeater.TryGetElement(0)!).FindName("PosterImage")).Source == null}.");
            Require(decodedArtwork.Any(source => ReferenceEquals(source, ((Image)((PosterCard)repeater.TryGetElement(0)!).FindName("PosterImage")).Source)),
                "Unchanged Frame return decoded the same artwork again.");
            card = repeater.TryGetElement(0);
            var scroll = (ScrollViewer)page.FindName("ContentScrollViewer");
            scroll.ChangeView(null, 180, null, true); await Task.Delay(80);
            var offset = scroll.VerticalOffset;
            Program.Log($"TRACE scroll-only: sameContainer={ReferenceEquals(card, repeater.TryGetElement(0))}, offset={offset}, sameItemsSource={ReferenceEquals(itemsSource, repeater.ItemsSource)}.");
            card = repeater.TryGetElement(0);
            var media = vm.Sections[0].Items[0];
            navigation.Navigate(typeof(Page)); await Task.Delay(80);
            // Real stop/progress notifications change Home's revision while it
            // is cached offscreen, even when all row identities are unchanged.
            vm.Receive(new PlaybackProgressUpdated("movie0", 20, 120, false));
            navigation.GoBack(); await Task.Delay(220);
            Program.Log($"TRACE progress Frame round trip: sameContainer={ReferenceEquals(card, repeater.TryGetElement(0))}, oldLoaded={(card as FrameworkElement)?.IsLoaded}, newType={repeater.TryGetElement(0)?.GetType().Name}, sameItemsSource={ReferenceEquals(itemsSource, repeater.ItemsSource)}, sameMedia={ReferenceEquals(media, vm.Sections[0].Items[0])}, offset={scroll.VerticalOffset}.");
            Require(ReferenceEquals(page, frame.Content), "Cached Home page was discarded.");
            Require(rows.SequenceEqual(panel.Children), "Playback-return rebuilt Home's mounted row controls.");
            Require(ReferenceEquals(card, repeater.TryGetElement(0)), "Playback-return discarded unchanged artwork/card control.");
            Require(Math.Abs(scroll.VerticalOffset - offset) < 1, "Home return lost vertical scroll.");
            // Stale background data should patch the same native rows too.
            vm.InvalidateCache(); wire.ChangeOneTitle = true;
            await vm.LoadCommand.ExecuteAsync(null);
            await WaitUntil(() => vm.Sections[0].Items[0].Title == "Updated title");
            Require(rows.SequenceEqual(panel.Children), "Stale refresh rebuilt unchanged Home rows.");
            itemsSource = repeater.ItemsSource;
            wire.ChangeMetadata = true; vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
            await WaitUntil(() => vm.Sections[0].Title == "alpha renamed row0");
            Require(ReferenceEquals(itemsSource, repeater.ItemsSource), "Metadata refresh discarded the mounted item collection.");
            // Repeated back/forward, queued refresh then departure, and new
            // profile retirement must not let old callbacks mutate a new tree.
            for (var i = 0; i < 5; i++)
            {
                navigation.Navigate(typeof(Page)); vm.Receive(new PlaybackProgressUpdated("movie0", i + 21, 120, false));
                navigation.GoBack(); await Task.Delay(60);
                Require(rows.SequenceEqual(panel.Children), "Repeated warm navigation recreated Home rows.");
            }
            wire.ChangeMetadata = false;
            vm.ShowUndoBanner = true;
            navigation.Navigate(typeof(Page)); await Task.Delay(80);
            Require(((Border)page.FindName("UndoBanner")).Visibility == Visibility.Collapsed, "Departed Home applied its queued banner callback.");
            navigation.GoBack(); await Task.Delay(100);
            Require(((Border)page.FindName("UndoBanner")).Visibility == Visibility.Visible, "Returning Home did not reconcile current banner state.");
            navigation.Navigate(typeof(Page)); auth.SelectProfile("beta"); navigation.GoBack();
            await WaitUntil(() => vm.Sections.Count == 8 && vm.Sections[0].Title == "beta row0" && vm.Sections.All(s => s.LoadCompleted));
            Require(!ReferenceEquals(rows[0], panel.Children[0]), "Retired profile's Home visual leaked into new owner.");

            // Initial layout failures expose a working Retry; a valid empty
            // layout is a distinct state, and section failures retry in place.
            wire.FailLayout = true; navigation.Navigate(typeof(Page)); auth.SelectProfile("initial-error"); navigation.GoBack();
            await WaitUntil(() => vm.ErrorMessage != null && !vm.IsLoading);
            Require(vm.Sections.Count == 0, "A failed new owner retained a prior profile's Home rows.");
            wire.FailLayout = false;
            Invoke(Descendants<Button>(page).First(button => button.Content as string == "Retry"));
            await WaitUntil(() => vm.Sections.Count == 8 && vm.Sections.All(s => s.LoadCompleted));
            Require(vm.ErrorMessage == null, "Home Retry retained its initial error.");

            wire.EmptyLayout = true; navigation.Navigate(typeof(Page)); auth.SelectProfile("empty"); navigation.GoBack();
            await WaitUntil(() => !vm.IsLoading && vm.SurfaceContext?.ProfileId == "empty" && vm.Sections.Count == 0);
            await Task.Delay(80);
            Require(((FrameworkElement)page.FindName("EmptyHomeState")).Visibility == Visibility.Visible, "Valid empty Home did not show its empty state.");
            wire.EmptyLayout = false; vm.InvalidateCache(); await vm.LoadCommand.ExecuteAsync(null);
            await WaitUntil(() => vm.Sections.Count == 8 && vm.Sections.All(s => s.LoadCompleted));

            wire.FailSection = true; navigation.Navigate(typeof(Page)); auth.SelectProfile("section-error"); navigation.GoBack();
            await WaitUntil(() => vm.Sections.Count == 8 && vm.Sections[0].LoadFailed && vm.Sections.All(s => s.LoadCompleted)
                && panel.Children.FirstOrDefault() is SectionRow failed && ReferenceEquals(failed.Section, vm.Sections[0]));
            var failedRow = (SectionRow)panel.Children[0]; wire.FailSection = false;
            Invoke(Descendants<Button>(failedRow).First(button => button.Content as string == "Retry"));
            await WaitUntil(() => !vm.Sections[0].LoadFailed && vm.Sections[0].Items.Count == 10);
            Require(ReferenceEquals(failedRow, panel.Children[0]), "Retry rebuilt the existing section control.");

            wire.HoldProfile = "delayed"; navigation.Navigate(typeof(Page)); auth.SelectProfile("delayed"); navigation.GoBack();
            await wire.LayoutEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            navigation.Navigate(typeof(Page)); auth.SelectProfile("fresh"); navigation.GoBack(); wire.ReleaseLayout.TrySetResult();
            await WaitUntil(() => vm.Sections.Count == 8 && vm.Sections.All(s => s.Title.StartsWith("fresh ", StringComparison.Ordinal) && s.LoadCompleted));
            var reads = wire.SectionReads;
            vm.Receive(new MediaSurfaceChanged(MediaSurfaceChangeKind.WatchedMarked, "movie0", context: firstOwner));
            await Task.Delay(650);
            Require(wire.SectionReads == reads, "A retired captured media event refreshed a new profile's Home.");
            var freshRepeater = (ItemsRepeater)((SectionRow)panel.Children[0]).FindName("CardsRepeater");
            await WaitUntil(() => freshRepeater.TryGetElement(0) is PosterCard fresh && ((Image)fresh.FindName("PosterImage")).Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 });
            Require(!ReferenceEquals(artwork, ((Image)((PosterCard)freshRepeater.TryGetElement(0)!).FindName("PosterImage")).Source), "Decoded artwork crossed profile ownership.");
            await VerifyArtworkWorkingSetAsync(parent, wire, client, spoilers);
            Program.Log("PASS: Home unchanged/play-return row, source/card/art identity and scroll; metadata/title patches; repeated navigation; queued inactive callbacks; initial failure/Retry, empty layout, section failure/Retry; delayed retired layout, event and artwork ownership.");
        }
        finally
        {
            navigation.Navigate(typeof(Page)); vm.SetActive(false);
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(vm);
            parent.Children.Remove(frame); await Task.Delay(100); field.SetValue(null, previous); events.Dispose();
        }
    }
    private static void Require(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
    private static async Task VerifyArtworkWorkingSetAsync(StackPanel parent, Wire wire, SiloApiClient client, EpisodeSpoilerPreferences spoilers)
    {
        var poster = new PosterCard(); poster.SetCatalogGridLayout(120); parent.Children.Insert(0, poster); await Task.Delay(50);
        var posterImage = (Image)poster.FindName("PosterImage");
        var small = new List<(SiloPlayer.Core.Models.Home.MediaItem Item, ImageSource Source)>();
        try
        {
            for (var i = 0; i < 34; i++)
            {
                var item = new SiloPlayer.Core.Models.Home.MediaItem { ContentId = "cache" + i, Type = "movie", Title = "Cache " + i, PosterUrl = $"https://home-warm.fixture.invalid/cache-{i}.png" };
                poster.MediaItem = item; await WaitUntil(() => posterImage.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 });
                await Task.Delay(20);
                small.Add((item, posterImage.Source));
            }
            poster.MediaItem = small[0].Item; await WaitUntil(() => posterImage.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 });
            Require(!ReferenceEquals(small[0].Source, posterImage.Source), "Decoded artwork retained more than32 entries.");
            poster.MediaItem = small[33].Item; await WaitUntil(() => posterImage.Source != null);
            Require(ReferenceEquals(small[33].Source, posterImage.Source), "Most recently visible poster missed its decoded cache entry.");
            Program.Log("PASS: actual decoded poster working set evicts beyond32 entries and retains the most recent.");

            var episode = new SiloPlayer.Core.Models.Home.MediaItem { ContentId = "spoiler-cache", Type = "episode", Title = "Spoiler", PosterUrl = "https://home-warm.fixture.invalid/spoiler.png", PosterIsEpisodeStill = true,
                UserState = new() { Played = false, IsInProgress = false } };
            wire.SpoilersEnabled = true; spoilers.Invalidate(); poster.MediaItem = episode;
            await WaitUntil(() => posterImage.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 });
            await MediaParityNativeFixture.CaptureAsync(poster, "home-hidden-state.png");
            var hidden = posterImage.Source;
            Program.Log($"TRACE hidden bitmap retained={IsRetainedSource(hidden)} sourceHidden={typeof(PosterCard).GetField("_spoilerArtworkHidden", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(poster)}.");
            episode.UserState.Played = true; poster.RefreshState();
            await WaitUntil(() => posterImage.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 } && !ReferenceEquals(hidden, posterImage.Source));
            await MediaParityNativeFixture.CaptureAsync(poster, "home-revealed-state.png");
            var revealed = posterImage.Source;
            Program.Log($"TRACE revealed bitmap retained={IsRetainedSource(revealed)}.");
            episode.UserState.Played = false; poster.RefreshState();
            await WaitUntil(() => ReferenceEquals(hidden, posterImage.Source));
            Require(!ReferenceEquals(hidden, revealed), "A spoiler policy change reused an artwork source from the wrong state.");
            Program.Log("PASS: actual episode artwork changes decoded source when revealed and reuses only the matching hidden state.");

            wire.HoldImage = true;
            poster.MediaItem = new() { ContentId = "late-art", Type = "movie", Title = "Old owner", PosterUrl = "https://home-warm.fixture.invalid/held.png" };
            await wire.ImageEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            client.SetProfile("artwork-retired-owner"); wire.ReleaseImage.TrySetResult(); await Task.Delay(200);
            Require(posterImage.Source == null, "Late old-profile artwork was assigned after its owner retired.");
            poster.MediaItem = new() { ContentId = "fresh-art", Type = "movie", Title = "New owner", PosterUrl = "https://home-warm.fixture.invalid/fresh-art.png" };
            await WaitUntil(() => posterImage.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 });
            Program.Log("PASS: held artwork from a retired profile cannot publish into an unchanged native card; current artwork loads normally.");
        }
        finally { parent.Children.Remove(poster); await Task.Delay(60); }

        // Eight decoded1024² images exceed24MiB while staying below32 entries.
        using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
        {
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            var pixels = new byte[1024 * 1024 * 4]; for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, 1024, 1024, 96, 96, pixels);
            await encoder.FlushAsync(); stream.Seek(0);
            using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)); await reader.LoadAsync((uint)stream.Size);
            wire.LargePng = new byte[checked((int)stream.Size)]; reader.ReadBytes(wire.LargePng);
        }
        var landscape = new LandscapeCard(); landscape.SetCardWidth(512); parent.Children.Insert(0, landscape); await Task.Delay(50);
        var landscapeImage = (Image)landscape.FindName("BackdropImage");
        var large = new List<(SiloPlayer.Core.Models.Home.MediaItem Item, ImageSource Source)>();
        try
        {
            for (var i = 0; i < 8; i++)
            {
                var item = new SiloPlayer.Core.Models.Home.MediaItem { ContentId = "large-cache" + i, Type = "movie", Title = "Large " + i, BackdropUrl = $"https://home-warm.fixture.invalid/large-{i}.png" };
                landscape.MediaItem = item; await WaitUntil(() => landscapeImage.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 });
                await Task.Delay(20);
                large.Add((item, landscapeImage.Source));
            }
            var decoded = (Microsoft.UI.Xaml.Media.Imaging.BitmapImage)large[0].Source;
            Require((long)decoded.PixelWidth * decoded.PixelHeight * 4 * 8 > 24L * 1024 * 1024, "Byte-budget native scenario did not exceed24MiB.");
            landscape.MediaItem = large[0].Item; await WaitUntil(() => landscapeImage.Source is Microsoft.UI.Xaml.Media.Imaging.BitmapImage { PixelWidth: > 0 });
            Require(!ReferenceEquals(large[0].Source, landscapeImage.Source), "Decoded working set retained more than24MiB below its entry limit.");
            landscape.MediaItem = large[7].Item; await WaitUntil(() => landscapeImage.Source != null);
            Require(ReferenceEquals(large[7].Source, landscapeImage.Source), "Recent landscape artwork missed its decoded working set.");
            Program.Log("PASS: actual landscape decodes honor24MiB eviction independently of the32-entry bound.");
        }
        finally { parent.Children.Remove(landscape); await Task.Delay(60); }
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static bool IsRetainedSource(ImageSource source)
    {
        var cache = typeof(PosterCard).Assembly.GetType("SiloPlayer.Helpers.DecodedArtworkCache")!;
        if (cache.GetField("_entries", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null) is not System.Collections.IDictionary entries) return false;
        foreach (var node in entries.Values)
        {
            var entry = node!.GetType().GetProperty("Value")!.GetValue(node)!;
            if (ReferenceEquals(source, entry.GetType().GetProperty("Bitmap")!.GetValue(entry))) return true;
        }
        return false;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static async Task WaitUntil(Func<bool> condition)
    { for (var i = 0; i < 120 && !condition(); i++) await Task.Delay(25); Require(condition(), "Home fixture timed out."); }
    private sealed class Services(IServiceProvider fallback, HomeViewModel vm, AuthService auth, NavigationService navigation,
        EventChannelClient events, SettingsService local, CatalogApi catalog, SiloApiClient client, HttpClient http, ImageService images, EpisodeSpoilerPreferences spoilers) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(HomeViewModel) ? vm : type == typeof(AuthService) ? auth
            : type == typeof(NavigationService) ? navigation : type == typeof(EventChannelClient) ? events
            : type == typeof(SettingsService) ? local : type == typeof(CatalogApi) ? catalog
            : type == typeof(SiloApiClient) ? client : type == typeof(HttpClient) ? http : type == typeof(ImageService) ? images
            : type == typeof(EpisodeSpoilerPreferences) ? spoilers : fallback.GetService(type);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal bool ChangeOneTitle, ChangeMetadata;
        internal bool FailLayout, EmptyLayout, FailSection;
        internal string? HoldProfile;
        internal TaskCompletionSource LayoutEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseLayout = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int SectionReads;
        internal bool HoldImage, SpoilersEnabled;
        internal byte[]? LargePng;
        internal TaskCompletionSource ImageEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseImage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "home-warm.fixture.invalid") throw new InvalidOperationException("Unexpected Home network.");
            var profile = request.Headers.TryGetValues("X-Profile-Id", out var ids) ? ids.Single() : "none";
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("held.png") && HoldImage) { HoldImage = false; ImageEntered.TrySetResult(); await ReleaseImage.Task; }
            if (path.EndsWith(".png")) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(path.Contains("large-") && LargePng != null ? LargePng : Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jT1sAAAAASUVORK5CYII=")) };
            if (path == "/api/v2/settings/contract/capabilities") return new(HttpStatusCode.OK) { Content = new StringContent("{\"api_version\":1,\"manifest_revision\":17,\"supports_batched_effective\":true}") };
            if (path == "/api/v2/settings/values/effective") return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { items = new[] { new { key = "catalog.hide_unwatched_episode_images", value = SpoilersEnabled }, new { key = "catalog.hide_unwatched_episode_overviews", value = SpoilersEnabled } } })) };
            if (path.EndsWith("/home/layout") && HoldProfile == profile)
            { HoldProfile = null; LayoutEntered.TrySetResult(); await ReleaseLayout.Task; }
            if (path.EndsWith("/home/layout") && FailLayout || path.Contains("/home/sections/row0/") && FailSection)
                return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":{\"code\":\"fixture_failure\",\"message\":\"Fixture unavailable\"}}") };
            object value;
            string Title(string id) => profile + (ChangeMetadata && id == "row0" ? " renamed " : " ") + id;
            if (path.EndsWith("/home/layout")) value = new { sections = Enumerable.Range(0, EmptyLayout ? 0 : 8).Select(i => new { id = "row" + i, title = Title("row" + i), section_type = "recently_added", item_limit = 20 }) };
            else if (path.Contains("/home/sections/"))
            {
                Interlocked.Increment(ref SectionReads);
                var id = path.Split('/')[5];
                value = new { id, title = Title(id), section_type = "recently_added", item_limit = 20, items = Enumerable.Range(0, 10).Select(i => new { content_id = "movie" + i, type = "movie", title = ChangeOneTitle && i == 0 ? "Updated title" : "Movie " + i, poster_url = $"https://home-warm.fixture.invalid/{profile}/{i}.png" }) };
            }
            else value = new { items = Array.Empty<object>() };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        }
    }
}
