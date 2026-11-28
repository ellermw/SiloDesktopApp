using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

/// <summary>
/// Vertical mouse-wheel scrolling, measured per frame on real pages in a large
/// window (3440x1440 client, the owner's monitor size) that stays off-screen and never
/// takes focus. A wheel notch is applied the way WinUI's ScrollViewer applies one (see
/// Wheel): a DirectManipulation animation of 15% of the viewport, ticked by the XAML frame
/// on the UI thread, exactly as a hardware notch is. A posted WM_MOUSEWHEEL is not used:
/// WinUI 3 reads the wheel as WM_POINTER input from the real cursor position, so it never
/// reaches an off-screen window, and SendInput would move the user's real input.
/// Each frame (CompositionTarget.Rendering) records the UI-thread frame interval, the
/// scroller's vertical offset and the layout passes run since the previous frame.
///   scroll-smoothness-plain      a bare ScrollViewer of rows (platform baseline)
///   scroll-smoothness-home       HomePage: hero + 13 rows x 24 cards with decoded JPEG artwork
///   scroll-smoothness-dashboard  Admin Dashboard: default widgets, Now Playing active (AdminDashboardNativeFixture.Scroll.cs)
/// Results: results.txt INFO lines and scroll-&lt;page&gt;-&lt;segment&gt;.txt per-frame tables.
/// Timing is a measurement, not a pass/fail gate; the checks fail only on what
/// does not depend on machine load.
/// </summary>
internal static class ScrollSmoothnessNativeFixture
{
    internal const int ClientWidth = 3440, ClientHeight = 1440;
    private static readonly List<string> Failures = [];

    /// <summary>The runtime's GC events for the whole run (SILO_SCROLL_GC=0 turns it off).</summary>
    internal static readonly ScrollGcProbe? Gc = Environment.GetEnvironmentVariable("SILO_SCROLL_GC") == "0" ? null : new ScrollGcProbe();

    /// <summary>Working set and managed heap now.</summary>
    internal static void LogMemory(string when)
    {
        var info = GC.GetGCMemoryInfo();
        Program.Log($"INFO: scroll memory {when}: working set {Environment.WorkingSet / 1048576} MB, managed heap {GC.GetTotalMemory(false) / 1048576} MB (after last GC {info.HeapSizeBytes / 1048576} MB, committed {info.TotalCommittedBytes / 1048576} MB), gc gen0/1/2 {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>GC handles after full collections (outside any measured segment): what stays alive, in wrappers.</summary>
    internal static async Task<long> HandlesAfterFullGcAsync(string when)
    {
        if (Gc == null) return -1;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var gc = GC.CollectionCount(2);
        for (var waited = 0; waited < 4000 && Gc.LastHandles.Gc < gc; waited += 100) await Task.Delay(100);
        Program.Log($"INFO: scroll gc handles {when}: {Gc.LastHandles.Count} after full collections (managed heap {GC.GetTotalMemory(false) / 1048576} MB); that collection: {Gc.PauseOf(Gc.LastHandles.Gc)}");
        return Gc.LastHandles.Count;
    }

    internal static async Task RunAsync(Window window, string scenario)
    {
        Failures.Clear();
        ScrollGcProbe.UiThread = GetCurrentThreadId();
        var previousContent = window.Content;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(ClientWidth, ClientHeight));
        try
        {
            if (scenario is "plain" or "all") await PlainAsync(window);
            if (scenario is "home" or "all") await HomeAsync(window);
            if (scenario is "dashboard" or "all") await AdminDashboardNativeFixture.ScrollSmoothnessAsync(window);
        }
        finally { window.Content = previousContent; }
        LogMemory("at the end");
        if (Gc != null) Program.Log("INFO: scroll gc pauses over the whole run: " + Gc.PauseStatistics());
        if (Failures.Count > 0) throw new InvalidOperationException("Scroll smoothness: " + string.Join("; ", Failures));
    }

    internal static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + "scroll " + label);
        if (!condition) Failures.Add(label);
    }

    // ---------------------------------------------------------------- plain baseline

    private static async Task PlainAsync(Window window)
    {
        var panel = new StackPanel { Spacing = 40, Padding = new Thickness(0, 8, 0, 0) };
        for (var row = 0; row < 14; row++)
        {
            var cards = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            for (var i = 0; i < 20; i++)
                cards.Children.Add(new Border
                {
                    Width = 200, Height = 300, CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(40 + row * 12), (byte)(60 + i * 7), 120)),
                    Child = new TextBlock { Text = $"Row {row} card {i}", Margin = new Thickness(12), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
                });
            panel.Children.Add(new StackPanel { Spacing = 12, Children = { new TextBlock { Text = $"Row {row}", FontSize = 20 }, cards } });
        }
        var scroller = new ScrollViewer { Content = panel, IsScrollInertiaEnabled = true };
        window.Content = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 23, 34)), Children = { scroller } };
        await WaitLoadedAsync(scroller);
        await MeasurePageAsync(scroller, "plain", (FrameworkElement)window.Content);
    }

    // ---------------------------------------------------------------- Home

    private static async Task HomeAsync(Window window)
    {
        var wire = new HomeWire();
        Program.Log("INFO: scroll home artwork " + await wire.PrepareArtworkAsync());
        LogMemory("before Home opens");
        await HandlesAfterFullGcAsync("before Home opens");
        ScrollHeapCensus.Take("before Home opens");
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl(HomeWire.Base);
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("scroll-fixture-access", "scroll-fixture-refresh", 0); auth.SelectProfile("scroll-profile");
        var homeApi = new HomeApi(client);
        var vm = new HomeViewModel(homeApi, auth);
        using var events = new EventChannelClient(client, auth);
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        field.SetValue(null, new Services(previous, new Dictionary<Type, object>
        {
            [typeof(SiloApiClient)] = client, [typeof(HomeApi)] = homeApi, [typeof(HomeViewModel)] = vm, [typeof(AuthService)] = auth,
            [typeof(CatalogApi)] = new CatalogApi(client), [typeof(LibraryViewModel)] = new LibraryViewModel(new CatalogApi(client)),
            [typeof(SettingsApi)] = new SettingsApi(client), [typeof(EventChannelClient)] = events, [typeof(HttpClient)] = http,
            [typeof(ImageService)] = new ImageService(Path.Combine(Program.ResultDirectory, "scroll-home-images")),
            [typeof(NavigationService)] = new NavigationService { Frame = new Frame(), NavigationRequestHandler = (_, _) => true },
        }));
        if (Environment.GetEnvironmentVariable("SILO_SCROLL_NOBADGES") == "1")
        {
            // Measurement only: Home without card badges, to see what badges cost a full collection.
            var overlays = (CardOverlayService)((IServiceProvider)field.GetValue(null)!).GetService(typeof(CardOverlayService))!;
            typeof(CardOverlayService).GetField("_enabled", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(overlays, false);
        }
        // Measurement only: Home under another GC latency mode (process-wide), e.g. SILO_SCROLL_LATENCY=LowLatency.
        var latency = System.Runtime.GCSettings.LatencyMode;
        if (Enum.TryParse<System.Runtime.GCLatencyMode>(Environment.GetEnvironmentVariable("SILO_SCROLL_LATENCY"), out var mode))
        {
            System.Runtime.GCSettings.LatencyMode = mode;
            Program.Log($"INFO: scroll GC latency mode {System.Runtime.GCSettings.LatencyMode} for Home");
        }
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_SCROLL_CARDGC") == "1") { await ScrollCardHandles.CostPerCardAsync(window, vm); return; }
            if (Environment.GetEnvironmentVariable("SILO_SCROLL_CARDGC") == "elements") { await ScrollCardHandles.ElementCostAsync(window); return; }
            if (Environment.GetEnvironmentVariable("SILO_SCROLL_CARDGC") == "variants") { await ScrollCardHandles.CardVariantCostAsync(window, vm); return; }
            if (Environment.GetEnvironmentVariable("SILO_SCROLL_CARDGC") == "traversal") { await ScrollCardHandles.TraversalCostAsync(window, vm); return; }
            var opened = Stopwatch.StartNew();
            var page = new HomePage();
            var root = new Grid { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"], Children = { page } };
            window.Content = root;
            var scroller = (ScrollViewer)page.FindName("ContentScrollViewer");
            // Time to first content: the first row's cards on screen, then every card inside the window showing its artwork.
            List<SiloPlayer.Controls.SectionRow> sectionRows = [];
            await Until(() => (sectionRows = ScrollArtworkCensus.Rows(root)).Count > 0 && ScrollArtworkCensus.RealizedCards(sectionRows.Take(1))[0] > 0, 15000, 50);
            var firstCards = opened.ElapsedMilliseconds;
            bool InViewShown() => ScrollArtworkCensus.Take(sectionRows.Take(2)) is var rows && rows.Count == 2 && rows.All(r => r.Cards > 0 && r.Revealed == r.Cards);
            await Until(InViewShown, 20000, 50);
            var inViewArtwork = opened.ElapsedMilliseconds;
            Program.Log($"INFO: scroll home time to first content: first row cards {firstCards} ms, all in-window artwork shown {inViewArtwork} ms");
            LogMemory("at first Home content");
            Check(InViewShown(), "Home shows the artwork of every card inside the window");
            // After first content: how long until every row has the cards it shows built, and what the frames cost meanwhile.
            var gcBefore = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration());
            long allRows;
            List<double> loadIntervals;
            using (var load = new FrameRecorder(scroller, root))
            {
                await load.SegmentAsync("home-load", async () =>
                {
                    await Until(() => ScrollArtworkCensus.Rows(root) is var all && all.Count == HomeWire.RowCount && (sectionRows = all) != null, 10000, 100);
                    await Until(() => ScrollArtworkCensus.RealizedCards(sectionRows).All(cards => cards >= 16), 10000, 100);
                }, 0);
                allRows = opened.ElapsedMilliseconds;
                load.Analyze();
                loadIntervals = load.LastIntervals;
                foreach (var line in load.Summary) Program.Log(line);
            }
            Program.Log($"INFO: scroll home load GC gen0/1/2 +{GC.CollectionCount(0) - gcBefore.Item1}/+{GC.CollectionCount(1) - gcBefore.Item2}/+{GC.CollectionCount(2) - gcBefore.Item3} pause +{(GC.GetTotalPauseDuration() - gcBefore.Item4).TotalMilliseconds:0} ms; heap {GC.GetTotalMemory(false) / 1048576} MB");
            LogMemory("after Home built its rows");
            var builtAhead = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Controls.SectionRowLayout")?
                .GetProperty("BuiltAhead", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            Check(ScrollArtworkCensus.RealizedCards(sectionRows).All(cards => cards >= 16), "every Home row is built before it is scrolled to ("
                + string.Join(",", ScrollArtworkCensus.RealizedCards(sectionRows)) + " cards per row)");
            Program.Log($"INFO: scroll home every row built at {(ScrollArtworkCensus.RealizedCards(sectionRows).All(cards => cards >= 16) ? allRows + " ms" : "never (rows below the window keep one card)")}; "
                + $"frames after first content: worst {loadIntervals.DefaultIfEmpty(0).Max():0.0} ms, over33.3={loadIntervals.Count(v => v > 33.3)}, over16.7={loadIntervals.Count(v => v > 16.7)} of {loadIntervals.Count}; cards built ahead={builtAhead ?? "n/a"}");
            await Until(() => vm.Sections.Count == HomeWire.RowCount && vm.Sections.All(s => s.LoadCompleted && s.Items.Count > 0)
                && vm.FeaturedSections.Count == 1, 15000);
            Check(vm.Sections.Count == HomeWire.RowCount, $"Home mounted {vm.Sections.Count} rows of {HomeWire.RowCount} (+ hero {vm.FeaturedSections.Count})");
            await WaitLoadedAsync(scroller);
            await Task.Delay(1500);
            Program.Log($"INFO: scroll home artwork census before measuring: {ScrollArtworkCensus.Format(ScrollArtworkCensus.Take(root))}");
            Program.Log($"INFO: scroll home artwork reads before measuring={wire.ArtworkReads} extent={scroller.ExtentHeight:0} viewport={scroller.ViewportHeight:0}");
            LogMemory("before the Home scroll");
            await MeasurePageAsync(scroller, "home", root);
            LogMemory("after the Home scroll");
            await HandlesAfterFullGcAsync("after the first Home scroll (Home built, nothing else alive)");
            ScrollHeapCensus.Take("after the first Home scroll");
            Program.Log($"INFO: scroll home artwork reads after measuring={wire.ArtworkReads}");
            Program.Log($"INFO: scroll home artwork census after measuring: {ScrollArtworkCensus.Format(ScrollArtworkCensus.Take(root))}");
            if (Environment.GetEnvironmentVariable("SILO_SCROLL_VARIANTS") != "0")
            {
                // Isolation: the hero as it is while the pointer rests on it (rail fill paused, no auto-advance).
                var hero = (SiloPlayer.Controls.HeroCarousel)page.FindName("HeroCarouselControl");
                const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
                // Builds before the compositor rail ran it as a Storyboard; later ones pause and restart it.
                var rail = typeof(SiloPlayer.Controls.HeroCarousel).GetField("_progressStoryboard", Private)?.GetValue(hero) as Microsoft.UI.Xaml.Media.Animation.Storyboard;
                typeof(SiloPlayer.Controls.HeroCarousel).GetMethod("StopAutoAdvance", Private)!.Invoke(hero, null);
                rail?.Pause();
                typeof(SiloPlayer.Controls.HeroCarousel).GetMethod("PauseProgressRail", Private)?.Invoke(hero, null);
                await MeasurePageAsync(scroller, "home-hero-still", root);
                // The rail in the window's real pixels: its compositor fill against the fill drawn that wide, at the top of Home.
                // Held as a pointer resting on the hero holds it, so coming back into view does not start a new cycle.
                var pointerOver = typeof(SiloPlayer.Controls.HeroCarousel).GetField("_isPointerOver", Private)!;
                pointerOver.SetValue(hero, true);
                scroller.ChangeView(null, 0, null, true);
                await Task.Delay(400);
                typeof(SiloPlayer.Controls.HeroCarousel).GetMethod("StopAutoAdvance", Private)!.Invoke(hero, null);
                var (railIdentical, railSummary) = await ScrollHeroRailPixels.CompareAsync(window, hero);
                Program.Log("INFO: scroll hero rail pixels: " + railSummary);
                if (railIdentical is bool same) Check(same, "the hero rail's compositor fill draws exactly as a fill that wide, at every width (window pixels)");
                pointerOver.SetValue(hero, false);
                rail?.Resume();
                typeof(SiloPlayer.Controls.HeroCarousel).GetMethod("StartAutoAdvance", Private)!.Invoke(hero, null);
                typeof(SiloPlayer.Controls.HeroCarousel).GetMethod("AnimateProgressRail", Private)?.Invoke(hero, null);
                await Task.Delay(500);
                await MeasurePageAsync(scroller, "home-again", root);
            }
            // The owner's Home re-reads every row on catalog events up to 47 times a minute while the
            // server scans (installed home_refresh.txt); reproduce one refresh a second while scrolling.
            var readsBefore = wire.SectionReads;
            await MeasurePageAsync(scroller, "home-live", root, () =>
            {
                Interlocked.Increment(ref wire.Generation);
                vm.QueueRealtimeRefresh("catalog:library.item_added");
            });
            Program.Log($"INFO: scroll home-live section reads during measurement={wire.SectionReads - readsBefore} generation={wire.Generation}");
            // One catalog event a second, as a scanning server sends them: Home re-reads its 14 sections at most once per
            // 2 s window (WebUI realtimeCatalogInvalidation.ts), not once per event.
            Check(wire.SectionReads - readsBefore <= (wire.Generation / 2 + 2) * 14,
                $"catalog events refresh Home once per 2 s window ({wire.SectionReads - readsBefore} section reads for {wire.Generation} events)");
            if (Environment.GetEnvironmentVariable("SILO_SCROLL_BADGES") == "1")
            {
                // Card badges drawn with the default preset, then with "plate", whose BoxShadow gives every badge a ThemeShadow.
                var overlays = (CardOverlayService)((IServiceProvider)field.GetValue(null)!).GetService(typeof(CardOverlayService))!;
                var documentField = typeof(CardOverlayService).GetField("_document", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var original = documentField.GetValue(overlays)!;
                async Task UsePresetAsync(string preset)
                {
                    var document = (CardOverlayPrefs)original;
                    documentField.SetValue(overlays, document with { Preset = preset });
                    (typeof(CardOverlayService).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(overlays) as Action)?.Invoke();
                    await Task.Delay(2500);
                    var badges = ScrollArtworkCensus.Descendants(root).OfType<Border>().Count(border => border.Shadow != null);
                    Program.Log($"INFO: scroll home preset {preset}: {badges} badges with a ThemeShadow");
                }
                await UsePresetAsync("classic");
                await MeasurePageAsync(scroller, "home-badges-classic", root);
                await UsePresetAsync("plate");
                await MeasurePageAsync(scroller, "home-badges-plate", root);
                documentField.SetValue(overlays, original);
            }
            if (Environment.GetEnvironmentVariable("SILO_SCROLL_CARD_COST") == "1")
            {
                Program.Log("INFO: scroll card cost " + await ScrollCardCost.MeasureAsync(root, vm.Sections[2].Items, "poster"));
                Program.Log("INFO: scroll card cost " + await ScrollCardCost.MeasureAsync(root, vm.Sections[4].Items, "landscape"));
            }
            if (Environment.GetEnvironmentVariable("SILO_SCROLL_REFRESH_GC") == "1")
            {
                // Measurement only: what the live refreshes leave for full collections, before a second Home page exists.
                await HandlesAfterFullGcAsync("after the live refreshes");
                await HandlesAfterFullGcAsync("after the live refreshes, again");
            }
            await AwayWhileBuildingAsync(window, root);
            if (Gc != null)
            {
                await HandlesAfterFullGcAsync("after the live refreshes and a second Home page");
                ScrollHeapCensus.Take("after the live refreshes and a second Home page", Environment.GetEnvironmentVariable("SILO_SCROLL_ROOTS"));
                Program.Log("INFO: scroll gc " + await ScrollCardHandles.MeasureAsync(root, vm.Sections[2].Items, landscape: false));
                Program.Log("INFO: scroll gc " + await ScrollCardHandles.MeasureAsync(root, vm.Sections[4].Items, landscape: true));
            }
        }
        finally
        {
            System.Runtime.GCSettings.LatencyMode = latency;
            window.Content = new Grid();
            // A card's Unloaded, raised up to a second or more after its page leaves the window, reads the
            // app's services: every Home card has run it before the services go back.
            var overlayService = (CardOverlayService)((IServiceProvider)field.GetValue(null)!).GetService(typeof(CardOverlayService))!;
            var changed = typeof(CardOverlayService).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Delegate[] Subscribers() => (changed.GetValue(overlayService) as Delegate)?.GetInvocationList() ?? [];
            var unloading = Stopwatch.StartNew();
            await Until(() => !Subscribers().Any(d => d.Target is SiloPlayer.Controls.PosterCard), 10000, 50);
            Program.Log($"INFO: scroll home cards still attached to the app's services when they went back: {Subscribers().Count(d => d.Target is SiloPlayer.Controls.PosterCard)} "
                + $"(waited {unloading.ElapsedMilliseconds} ms; other overlay subscribers {Subscribers().Count(d => d.Target is not SiloPlayer.Controls.PosterCard)})");
            await Task.Delay(200);
            vm.SetActive(false); WeakReferenceMessenger.Default.UnregisterAll(vm);
            field.SetValue(null, previous);
        }
    }

    /// <summary>
    /// Home left while its rows are still being built ahead, as navigation leaves it (the page is kept,
    /// out of the tree): nothing runs for it meanwhile, and back in the window it finishes building its rows.
    /// </summary>
    private static async Task AwayWhileBuildingAsync(Window window, Grid root)
    {
        var layout = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Controls.SectionRowLayout");
        int? Read(string name) => layout?.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as int?;
        if (Read("Kicks") is null) { Program.Log("INFO: scroll home away-while-building: this build has no build-ahead queue"); return; }
        var page = new HomePage();
        var host = new Grid { Background = root.Background, Children = { page } };
        window.Content = host;
        List<SiloPlayer.Controls.SectionRow> rows = [];
        await Until(() => (rows = ScrollArtworkCensus.Rows(host)).Count == HomeWire.RowCount && Read("Waiting") > 0, 10000, 10);
        var builtBefore = ScrollArtworkCensus.RealizedCards(rows).Sum();
        Check(Read("Waiting") > 0, $"a new Home page has rows waiting to build ahead ({Read("Waiting")} waiting, {builtBefore} cards built)");
        // Away: the page leaves the window but stays alive, as the navigation cache keeps it.
        window.Content = new Grid { Background = root.Background };
        await Task.Delay(300);
        var kicks = Read("Kicks");
        await Task.Delay(1500);
        var kicksAway = Read("Kicks") - kicks;
        Check(kicksAway <= 1 && Read("Waiting") == 0, $"Home out of the window runs nothing for its rows ({kicksAway} queue runs in 1.5 s, {Read("Waiting")} rows held)");
        window.Content = host;
        await Until(() => ScrollArtworkCensus.RealizedCards(ScrollArtworkCensus.Rows(host)).All(cards => cards >= 16), 15000, 100);
        var after = ScrollArtworkCensus.RealizedCards(ScrollArtworkCensus.Rows(host));
        Check(after.All(cards => cards >= 16), $"Home back in the window finishes building its rows ({string.Join(",", after)} cards per row)");
        window.Content = root;
        await Task.Delay(200);
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> overrides) : IServiceProvider
    { public object? GetService(Type type) => overrides.TryGetValue(type, out var value) ? value : fallback.GetService(type); }

    private sealed class HomeWire : HttpMessageHandler
    {
        internal const string Base = "https://scroll-fixture.invalid";
        // SILO_SCROLL_ROWS sets the row count for measurements that scale it (13 by default, as the owner's Home).
        internal static readonly int RowCount = int.TryParse(Environment.GetEnvironmentVariable("SILO_SCROLL_ROWS"), out var rows) && rows > 0 ? rows : 13;
        internal const int CardsPerRow = 24;
        private static readonly string[] RowTypes = ["continue_watching", "recently_added", "trending_on_server", "recommended_for_you"];
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<byte[]>> _art = new();
        private int _artworkReads, _sectionReads;
        private long _artworkBytes;
        public int Generation;
        public int ArtworkReads => Volatile.Read(ref _artworkReads);
        public int SectionReads => Volatile.Read(ref _sectionReads);

        public long ArtworkBytesServed => Interlocked.Read(ref _artworkBytes);

        /// <summary>Every card has its own poster (500x750 JPEG, photo-like grain) and every hero slide its own
        /// 1920x1080 backdrop, encoded before the page opens so encoding never competes with a measurement.</summary>
        public async Task<string> PrepareArtworkAsync()
        {
            var posterKeys = Enumerable.Range(0, RowCount).SelectMany(r => Enumerable.Range(0, CardsPerRow + (RowTypes[r % RowTypes.Length] == "recently_added" ? 40 : 0)).Select(i => $"poster/row{r}-item{i}"))
                .Concat(Enumerable.Range(0, 8).Select(i => $"poster/hero-item{i}")).ToList();
            var backdropKeys = Enumerable.Range(0, 8).Select(i => $"backdrop/hero-item{i}").ToList();
            var posters = await Task.WhenAll(posterKeys.Select(Art));
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var backdrops = await Task.WhenAll(backdropKeys.Select(Art));
            return $"{_art.Count} distinct images; posters 500x750 avg {posters.Average(b => b.Length) / 1024:0} KB (min {posters.Min(b => b.Length) / 1024}, max {posters.Max(b => b.Length) / 1024}); hero backdrops 1920x1080 avg {backdrops.Average(b => b.Length) / 1024:0} KB; served after 40-119 ms";
        }

        private Task<byte[]> Art(string key) => _art.GetOrAdd(key, k => Task.Run(() => EncodeAsync(k)));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/art/", StringComparison.Ordinal) && path.EndsWith(".jpg", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _artworkReads);
                var key = path["/art/".Length..^".jpg".Length];
                var bytes = await Art(key);
                // A small, uneven network delay, as artwork from a real server arrives.
                await Task.Delay(40 + StableHash(key) % 80, ct);
                Interlocked.Add(ref _artworkBytes, bytes.Length);
                var art = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                art.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return art;
            }
            if (request.Method != HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (path == "/api/v2/home/capabilities") return Json("{\"row_item_limit_max\":100,\"section_item_limit_max\":100}");
            if (path == "/api/v2/home/layout")
                return Json(JsonSerializer.Serialize(new
                {
                    sections = new[] { new { id = "hero", title = "Featured", section_type = "recently_added", featured = true, item_limit = 8 } }
                        .Concat(Enumerable.Range(0, RowCount).Select(r => new { id = "row" + r, title = "Row " + r, section_type = RowTypes[r % RowTypes.Length], featured = false, item_limit = CardsPerRow })),
                }));
            if (path.StartsWith("/api/v2/home/sections/", StringComparison.Ordinal) && path.EndsWith("/items", StringComparison.Ordinal))
            {
                var id = path.Split('/')[5];
                var hero = id == "hero";
                var row = hero ? 99 : int.Parse(id[3..]);
                var type = hero ? "recently_added" : RowTypes[row % RowTypes.Length];
                var count = hero ? 8 : CardsPerRow;
                Interlocked.Increment(ref _sectionReads);
                // Live churn as a scanning server produces it: Recently Added rows gain a title at the
                // front (the oldest leaves), Trending rows see one title's metadata change.
                var generation = Volatile.Read(ref Generation);
                // Recently Added lists newest first at every generation: generation g adds item 23+g at the front.
                var recent = !hero && type == "recently_added";
                var shift = recent ? generation : 0;
                var edit = !hero && type == "trending_on_server" && generation > 0 ? $" v{generation}" : "";
                return Json(JsonSerializer.Serialize(new
                {
                    id, title = hero ? "Featured" : "Row " + row, section_type = type, featured = hero, item_limit = count, total_count = count, has_more = false,
                    items = Enumerable.Range(0, count).Select(slot => recent ? shift + count - 1 - slot : slot).Select(i => new
                    {
                        content_id = $"{id}-item{i}", type = "movie", title = $"Synthetic Title {row}-{i}" + (i == 0 ? edit : ""), year = 2000 + (i % 25), runtime = 6000,
                        overview = "A synthetic overview long enough to wrap onto several lines in the featured hero banner, as real metadata does.",
                        genres = new[] { "Drama", "Thriller" }, content_rating = "PG-13",
                        poster_url = $"{Base}/art/poster/{id}-item{i}.jpg",
                        backdrop_url = $"{Base}/art/{(hero ? "backdrop" : "landscape")}/{id}-item{i}.jpg",
                        progress_updated_at = type == "continue_watching" ? "2026-11-01T00:00:00Z" : null,
                        // Real library items carry these, so the default card badges (resolution, HDR, audio) show.
                        overlay_summary = new { resolution = i % 3 == 0 ? "2160p" : "1080p", hdr = i % 2 == 0 ? "DV HDR10" : "", audio = i % 4 == 0 ? "TrueHD Atmos" : "EAC3" },
                    }),
                }));
            }
            return Json("{}");
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private static int StableHash(string value) { var hash = 17; foreach (var c in value) hash = unchecked(hash * 31 + c); return hash & 0x7fffffff; }

        private static async Task<byte[]> EncodeAsync(string key)
        {
            var (width, height) = key.StartsWith("backdrop/", StringComparison.Ordinal) ? (1920, 1080)
                : key.StartsWith("landscape/", StringComparison.Ordinal) ? (1280, 720) : (500, 750);
            var seed = StableHash(key);
            // A distinct picture per key (its own colours and diagonal bands) with film-grain noise, so the
            // JPEG weighs about what a real poster does.
            var pixels = new byte[width * height * 4];
            var rng = (uint)seed | 1;
            int r0 = 40 + seed % 170, g0 = 30 + (seed >> 8) % 170, b0 = 50 + (seed >> 16) % 170;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                    var grain = (int)(rng & 15) - 8;
                    var band = (x + 2 * y + seed % 97) / 41 % 6 * 9;
                    var p = (y * width + x) * 4;
                    pixels[p] = (byte)Math.Clamp(b0 * (width - x) / width + band + grain, 0, 255);
                    pixels[p + 1] = (byte)Math.Clamp(g0 * y / height + 20 + grain, 0, 255);
                    pixels[p + 2] = (byte)Math.Clamp(r0 * (height - y) / height + band / 2 + grain, 0, 255);
                    pixels[p + 3] = 255;
                }
            using var stream = new InMemoryRandomAccessStream();
            var quality = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(0.85f, Windows.Foundation.PropertyType.Single) };
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, quality);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
            await encoder.FlushAsync();
            var bytes = new byte[stream.Size];
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            return bytes;
        }
    }

    internal static async Task MeasurePageAsync(ScrollViewer scroller, string page, FrameworkElement root, Action? liveTick = null)
    {
        var scale = scroller.XamlRoot?.RasterizationScale ?? 1;
        Program.Log("INFO: scroll adapters " + ScrollLoadProbe.AdapterSummary);
        Program.Log($"INFO: scroll {page} client={ClientWidth}x{ClientHeight} scale={scale:0.##} viewport={scroller.ViewportWidth:0}x{scroller.ViewportHeight:0} extent={scroller.ExtentHeight:0} scrollable={scroller.ScrollableHeight:0}");
        Check(scroller.ScrollableHeight >= 1200, $"{page} has at least 1200px to scroll ({scroller.ScrollableHeight:0})");
        await CaptureAsync(root, $"scroll-{page}-top.png");

        var viewChanged = 0; var intermediate = 0;
        void Counted(object? sender, ScrollViewerViewChangedEventArgs e) { viewChanged++; if (e.IsIntermediate) intermediate++; }
        scroller.ViewChanged += Counted;
        scroller.ChangeView(null, 0, null, true);
        await Task.Delay(800);

        var wheel = new Wheel(scroller);
        // Live variants: the page's own data refreshes keep arriving once a second while the user scrolls.
        Microsoft.UI.Dispatching.DispatcherQueueTimer? live = null;
        if (liveTick != null)
        {
            live = scroller.DispatcherQueue.CreateTimer();
            live.Interval = TimeSpan.FromSeconds(1);
            live.Tick += (_, _) => liveTick();
            live.Start();
        }
        using (var recorder = new FrameRecorder(scroller, root))
        {
            await recorder.SegmentAsync($"{page}-idle", async () => await Task.Delay(1500), 0);
            // A quick wheel spin (10 notches 30 ms apart), separate notches (6 notches 250 ms apart), then a spin back up.
            await recorder.SegmentAsync($"{page}-spin-down", async () => { for (var i = 0; i < 10; i++) { wheel.Notch(+1); await Task.Delay(30); } }, 10);
            await recorder.SegmentAsync($"{page}-notches-down", async () => { for (var i = 0; i < 6; i++) { wheel.Notch(+1); await Task.Delay(250); } }, 6);
            await recorder.SegmentAsync($"{page}-spin-up", async () => { for (var i = 0; i < 10; i++) { wheel.Notch(-1); await Task.Delay(30); } }, 10);
            if (liveTick != null)
                await recorder.SegmentAsync($"{page}-notches-long", async () => { for (var i = 0; i < 24; i++) { wheel.Notch(i < 12 ? +1 : -1); await Task.Delay(250); } }, 24);
            recorder.Analyze();
            foreach (var line in recorder.Summary) Program.Log(line);
            Check(recorder.Segments.Where(s => s.Notches > 0).All(s => s.Moved > 0), $"{page} every wheel segment moved the page");
            if (page == "home")
            {
                // The hero's progress rail ran as a Width (dependent) animation: a layout pass on every frame while Home sat idle.
                var idle = recorder.Segments.Single(s => s.Name == "home-idle");
                Check(idle.Layouts <= idle.Frames / 10, $"idle Home does not lay out every frame ({idle.Layouts} layout passes in {idle.Frames} frames)");
                // Rows come into view already built: no frame of the first scroll creates a row's worth of cards.
                var firstPass = recorder.Segments.Where(s => s.Notches > 0).ToList();
                Check(firstPass.All(s => s.MostCardsInAFrame < 10), "the first scroll through Home creates no row of cards in a frame ("
                    + string.Join(", ", firstPass.Select(s => $"{s.Name} at most {s.MostCardsInAFrame}")) + ")");
            }
            if (page != "plain")
            {
                // Load-independent regression checks: the page follows the wheel itself, and a quick wheel spin never
                // stops mid-motion. (The platform path stops for a frame and then jumps 100-235 px on every notch.)
                Check(wheel.Path == "app wheel follower", $"{page} follows the mouse wheel with the app's continuous scroller (path={wheel.Path})");
                var spins = recorder.Segments.Where(s => s.Name.Contains("-spin-", StringComparison.Ordinal)).ToList();
                Check(spins.Count > 0 && spins.All(s => s.MotionFrames == s.Moved), $"{page} wheel spins never stall mid-motion ("
                    + string.Join(", ", spins.Select(s => $"{s.Name}={s.MotionFrames - s.Moved}")) + " stalled frames)");
            }
        }
        live?.Stop();
        scroller.ViewChanged -= Counted;
        Program.Log($"INFO: scroll {page} wheel path={wheel.Path} ViewChanged={viewChanged} intermediate={intermediate}");
        scroller.ChangeView(null, 0, null, true);
        await Task.Delay(500);
        await CaptureAsync(root, $"scroll-{page}-after.png");
    }

    /// <summary>
    /// One wheel notch. A page with the app's wheel follower gets it through the same method its
    /// PointerWheelChanged handler calls. Otherwise it is applied the way WinUI's ScrollViewer applies
    /// a hardware notch: DirectManipulation animates the content by 15% of the viewport
    /// (ScrollViewer_Partial.h GetVerticalScrollWheelDelta) and each further notch retargets the running
    /// animation. Both that and this animated ChangeView are DirectManipulation animations ticked by the
    /// XAML frame on the UI thread; this one reaches half a notch in ~46 ms, as the measured hardware
    /// wheel does (~47 ms).
    /// </summary>
    private sealed class Wheel(ScrollViewer scroller)
    {
        // The app's own wheel follower (SmoothWheelScrolling), when the page has one: the method its
        // PointerWheelChanged handler calls for a notch. Absent in builds before it existed.
        private static readonly MethodInfo? AppNotch = typeof(SiloPlayer.App).Assembly
            .GetType("SiloPlayer.Helpers.SmoothWheelScrolling")?.GetMethod("Notch", BindingFlags.NonPublic | BindingFlags.Static);
        private double? _target;
        public string Path { get; private set; } = "none";
        public void Notch(int direction)
        {
            if (AppNotch?.Invoke(null, [scroller, (double)direction]) is true) { Path = "app wheel follower"; return; }
            Path = "platform DirectManipulation animation";
            var step = Math.Max(48, Math.Round(scroller.ViewportHeight * 0.15));
            var from = _target is { } pending && Math.Abs(pending - scroller.VerticalOffset) > 1 ? pending : scroller.VerticalOffset;
            _target = Math.Clamp(from + direction * step, 0, scroller.ScrollableHeight);
            scroller.ChangeView(null, _target, null, disableAnimation: false);
        }
    }

    internal sealed record Segment(string Name, int Notches, int Frames, double Average, double P95, double Worst, int Over16, int Over33,
        int Moved, int MotionFrames, double MotionMs, double LargestStep, double Travel, int Layouts, double Judder, double LargestJump, int MostCardsInAFrame);

    internal sealed class FrameRecorder : IDisposable
    {
        private readonly ScrollViewer _scroller;
        private readonly FrameworkElement _root;
        // Allocated once, before anything is measured: recording a frame allocates nothing.
        private readonly List<(long Ticks, double Offset, int Layouts, int Prepared, long LayoutEnd, int Art, double GcMs, long Alloc)> _frames = new(65536);
        private readonly List<(string Name, int Notches, int Start, int End, string Load, DateTime From, DateTime To)> _recorded = new(32);
        private int _layouts, _prepared, _art;
        private readonly List<(Image Image, long Token)> _images = new();
        private long _layoutEnd;
        private readonly List<ItemsRepeater> _repeaters;
        private readonly ScrollLoadProbe _load = new();
        public readonly List<Segment> Segments = [];
        public readonly List<string> Summary = [];
        public List<double> LastIntervals { get; private set; } = [];

        public FrameRecorder(ScrollViewer scroller, FrameworkElement root)
        {
            _scroller = scroller; _root = root;
            CompositionTarget.Rendering += OnRendering;
            // LayoutUpdated is raised once per layout pass anywhere in the tree.
            _root.LayoutUpdated += OnLayout;
            // Cards the rows' repeaters create or rebind per frame.
            _repeaters = ScrollArtworkCensus.Descendants(root).OfType<ItemsRepeater>().ToList();
            foreach (var repeater in _repeaters) repeater.ElementPrepared += OnPrepared;
            // Artwork assigned or decoded per frame (every Image realized when recording starts).
            foreach (var image in ScrollArtworkCensus.Descendants(root).OfType<Image>())
            {
                image.ImageOpened += OnArt;
                _images.Add((image, image.RegisterPropertyChangedCallback(Image.SourceProperty, (_, _) => _art++)));
            }
        }

        private void OnArt(object sender, RoutedEventArgs e) => _art++;

        private void OnPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args) => _prepared++;
        private void OnLayout(object? sender, object e) { _layouts++; _layoutEnd = Stopwatch.GetTimestamp(); }
        private void OnRendering(object? sender, object e) => _frames.Add((Stopwatch.GetTimestamp(), _scroller.VerticalOffset, _layouts, _prepared, _layoutEnd, _art, GC.GetTotalPauseDuration().TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread()));

        public void Dispose()
        {
            CompositionTarget.Rendering -= OnRendering; _root.LayoutUpdated -= OnLayout; _load.Dispose();
            foreach (var repeater in _repeaters) repeater.ElementPrepared -= OnPrepared;
            foreach (var (image, token) in _images) { image.ImageOpened -= OnArt; image.UnregisterPropertyChangedCallback(Image.SourceProperty, token); }
        }

        /// <summary>Runs the input, then keeps recording until the offset has been still for 400 ms (at most 4 s).</summary>
        public async Task SegmentAsync(string name, Func<Task> input, int notches)
        {
            var start = _frames.Count;
            var fromUtc = DateTime.UtcNow;
            var gc = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalPauseDuration());
            var blocking = GC.GetGCMemoryInfo(GCKind.FullBlocking).Index;
            var allocated = GC.GetTotalAllocatedBytes();
            var background = GC.GetGCMemoryInfo(GCKind.Background).Index;
            _load.Begin();
            await input();
            var settleStart = Stopwatch.GetTimestamp();
            var lastOffset = _scroller.VerticalOffset; var lastChange = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(settleStart).TotalMilliseconds < 4000)
            {
                await Task.Delay(16);
                if (Math.Abs(_scroller.VerticalOffset - lastOffset) > 0.01) { lastOffset = _scroller.VerticalOffset; lastChange = Stopwatch.GetTimestamp(); }
                else if (Stopwatch.GetElapsedTime(lastChange).TotalMilliseconds >= 400) break;
            }
            var fullBlocking = GC.GetGCMemoryInfo(GCKind.FullBlocking);
            var load = _load.End() + $" gc gen0/1/2 +{GC.CollectionCount(0) - gc.Item1}/+{GC.CollectionCount(1) - gc.Item2}/+{GC.CollectionCount(2) - gc.Item3} pause {(GC.GetTotalPauseDuration() - gc.Item4).TotalMilliseconds:0}ms"
                + $" allocated {(GC.GetTotalAllocatedBytes() - allocated) / 1048576.0:0.0}MB fullBlocking +{(fullBlocking.Index > blocking ? 1 : 0)}{(fullBlocking.Index > blocking ? $"(compacted={fullBlocking.Compacted}, pause {fullBlocking.PauseDurations[0].TotalMilliseconds:0}ms, heap {fullBlocking.HeapSizeBytes / 1048576}MB)" : "")} background +{(GC.GetGCMemoryInfo(GCKind.Background).Index > background ? 1 : 0)}";
            // Analysis allocates (lists, text): it runs after the last segment (Analyze), never between segments,
            // so the fixture's own garbage cannot make a collection due inside the next measured segment.
            _recorded.Add((name, notches, start, _frames.Count, load, fromUtc, DateTime.UtcNow));
        }

        /// <summary>Turns the recorded segments into Segments and Summary lines and writes the per-frame tables.</summary>
        public void Analyze()
        {
            foreach (var recorded in _recorded) AnalyzeSegment(recorded.Name, recorded.Notches, recorded.Start, recorded.End, recorded.Load);
            if (Gc != null)
            {
                // The runtime delivers its events a little after they happen.
                Thread.Sleep(1500);
                var detail = new StringBuilder();
                foreach (var recorded in _recorded)
                {
                    var (summary, text, _) = Gc.Describe(recorded.Name, recorded.From, recorded.To);
                    Summary.Add($"INFO: scroll {recorded.Name} gc: {summary}");
                    detail.Append(text);
                }
                File.AppendAllText(Path.Combine(Program.ResultDirectory, "scroll-gc.txt"), detail.ToString());
            }
            _recorded.Clear();
        }

        private void AnalyzeSegment(string name, int notches, int start, int end, string load)
        {
            var frames = _frames.Skip(start).Take(end - start).ToList();
            // Bytes the UI thread allocated per frame (Rendering to Rendering).
            var uiAlloc = frames.Zip(frames.Skip(1), (a, b) => b.Alloc - a.Alloc).ToList();
            if (uiAlloc.Count > 0) Summary.Add($"INFO: scroll {name} UI-thread allocation per frame: avg {uiAlloc.Average() / 1024:0.0}KB, p95 {uiAlloc.OrderBy(v => v).ElementAt((int)(uiAlloc.Count * 0.95)) / 1024:0.0}KB, max {uiAlloc.Max() / 1024:0}KB, total {uiAlloc.Sum() / 1048576.0:0.00}MB over {uiAlloc.Count} frames");
            var intervals = frames.Zip(frames.Skip(1), (a, b) => (b.Ticks - a.Ticks) * 1000.0 / Stopwatch.Frequency).ToList();
            LastIntervals = intervals;
            // Motion window: first to last frame whose offset differs from the previous frame.
            var steps = frames.Zip(frames.Skip(1), (a, b) => Math.Abs(b.Offset - a.Offset)).ToList();
            var firstMove = steps.FindIndex(s => s > 0.01); var lastMove = steps.FindLastIndex(s => s > 0.01);
            var motionFrames = firstMove < 0 ? 0 : lastMove - firstMove + 1;
            var moved = steps.Count(s => s > 0.01);
            var motionMs = firstMove < 0 ? 0 : (frames[lastMove + 1].Ticks - frames[firstMove].Ticks) * 1000.0 / Stopwatch.Frequency;
            // Frame timing statistics cover the motion window when there is one (what the eye follows), else the whole segment.
            var timed = firstMove < 0 ? intervals : intervals.Skip(firstMove).Take(motionFrames).ToList();
            var sorted = timed.OrderBy(v => v).ToList();
            double P(double q) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(q * sorted.Count) - 1)];
            var layouts = frames.Count < 2 ? 0 : frames[^1].Layouts - frames[0].Layouts;
            // Motion quality inside the motion window: how much the per-frame step changes from one frame to
            // the next (px). A continuous scroll changes speed gradually; a lurching one stops and jumps.
            var signed = frames.Zip(frames.Skip(1), (a, b) => b.Offset - a.Offset).ToList();
            var jumps = firstMove < 0 ? new List<double>() : Enumerable.Range(firstMove + 1, Math.Max(0, lastMove - firstMove)).Select(i => Math.Abs(signed[i] - signed[i - 1])).ToList();
            var judder = jumps.Count == 0 ? 0 : Math.Sqrt(jumps.Average(j => j * j));
            var segment = new Segment(name, notches, frames.Count, sorted.Count == 0 ? 0 : sorted.Average(), P(0.95), sorted.LastOrDefault(),
                sorted.Count(v => v > 16.7), sorted.Count(v => v > 33.3), moved, motionFrames, motionMs, steps.DefaultIfEmpty(0).Max(),
                frames.Count == 0 ? 0 : Math.Abs(frames[^1].Offset - frames[0].Offset), layouts, judder, jumps.DefaultIfEmpty(0).Max(),
                frames.Zip(frames.Skip(1), (a, b) => b.Prepared - a.Prepared).DefaultIfEmpty(0).Max());
            Segments.Add(segment);
            // Where each long frame's time went: layout (where repeaters create and measure cards) is the time from
            // the frame's Rendering callback to the end of its last layout pass; the rest is rendering plus any other
            // UI-thread work (async continuations, image callbacks, timers) before the next frame.
            double LayoutMs(int i) => frames[i].LayoutEnd > frames[i - 1].Ticks ? (frames[i].LayoutEnd - frames[i - 1].Ticks) * 1000.0 / Stopwatch.Frequency : 0;
            var longFrames = Enumerable.Range(1, Math.Max(0, frames.Count - 1)).Where(i => intervals[i - 1] > 33.3)
                .Select(i => $"{intervals[i - 1]:0}ms(layout {Math.Min(LayoutMs(i), intervals[i - 1]):0}, cards {frames[i].Prepared - frames[i - 1].Prepared}, art {frames[i].Art - frames[i - 1].Art}, gc {frames[i].GcMs - frames[i - 1].GcMs:0})").ToList();
            if (longFrames.Count > 0) Summary.Add($"INFO: scroll {name} long frames: " + string.Join(" ", longFrames));
            Summary.Add($"INFO: scroll {name} frames={segment.Frames} avg={segment.Average:0.0}ms p95={segment.P95:0.0}ms worst={segment.Worst:0.0}ms over16.7={segment.Over16} over33.3={segment.Over33} layoutPasses={layouts}"
                + (notches == 0 ? "" : $" | notches={notches} travel={segment.Travel:0}px motion={motionMs:0}ms movingFrames={moved}/{motionFrames}"
                + $" ({(motionMs > 0 ? moved * 1000 / motionMs : 0):0} position updates/s) stalledFrames={motionFrames - moved} largestStep={segment.LargestStep:0.#}px largestSpeedJump={segment.LargestJump:0.#}px judderRms={judder:0.0}px") + " | " + load);
            var table = new StringBuilder("frame,t_ms,interval_ms,offset,step,layout_passes,layout_ms,cards_prepared\n");
            for (var i = 0; i < frames.Count; i++)
                table.Append(i).Append(',').Append(((frames[i].Ticks - frames[0].Ticks) * 1000.0 / Stopwatch.Frequency).ToString("0.00"))
                    .Append(',').Append(i == 0 ? "" : intervals[i - 1].ToString("0.00")).Append(',').Append(frames[i].Offset.ToString("0.00"))
                    .Append(',').Append(i == 0 ? "" : steps[i - 1].ToString("0.00"))
                    .Append(',').Append(i == 0 ? 0 : frames[i].Layouts - frames[i - 1].Layouts)
                    .Append(',').Append(i == 0 ? "" : Math.Min(LayoutMs(i), intervals[i - 1]).ToString("0.00"))
                    .Append(',').Append(i == 0 ? 0 : frames[i].Prepared - frames[i - 1].Prepared).Append('\n');
            File.WriteAllText(Path.Combine(Program.ResultDirectory, $"scroll-{name}.txt"), table.ToString());
        }
    }

    // ---------------------------------------------------------------- helpers

    internal static async Task WaitLoadedAsync(FrameworkElement element)
    {
        await Until(() => element.IsLoaded && element.ActualHeight > 0, 10000);
        element.UpdateLayout();
        await Task.Delay(300);
    }

    internal static async Task Until(Func<bool> condition, int milliseconds, int poll = 25)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += poll) await Task.Delay(poll);
    }

    internal static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        await Task.Delay(150);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96,
            (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
        Program.Log($"INFO: scroll capture {name} {bitmap.PixelWidth}x{bitmap.PixelHeight}");
        // A capture copies tens of MB of pixels onto the large-object heap. Collect them here so they do not
        // make a full collection due inside the next measured segment (the app never captures itself).
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }
}
