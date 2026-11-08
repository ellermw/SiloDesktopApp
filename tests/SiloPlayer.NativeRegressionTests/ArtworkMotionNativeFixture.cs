using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static partial class ArtworkMotionNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://artwork-motion.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); auth.SelectProfile("fixture");
        var catalog = new CatalogApi(client); var settings = new SettingsApi(client);
        using var spoilers = new EpisodeSpoilerPreferences(settings);
        using var events = new EventChannelClient(client, auth);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "images"));
        var navigation = new NavigationService();
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(CatalogApi)] = catalog,
            [typeof(EpisodeSpoilerPreferences)] = spoilers, [typeof(SettingsApi)] = settings, [typeof(NavigationService)] = navigation,
            [typeof(SettingsService)] = new SettingsService(Path.Combine(Program.ResultDirectory, "settings")),
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
            [typeof(CardOverlayService)] = new CardOverlayService(settings), [typeof(EventChannelClient)] = events,
            [typeof(ImageService)] = images, [typeof(HttpClient)] = http,
        }, () => new LibraryViewModel(catalog)));
        Frame? frame = null;
        try
        {
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "artwork-first-mount")
            {
                await FirstMountCasesAsync(parent, wire, client, spoilers);
                if (Failures.Count > 0) throw new InvalidOperationException(string.Join("; ", Failures));
                return;
            }
            window.AppWindow.ResizeClient(new(1100, 800));
            frame = new Frame { Width = 1100, Height = 800 }; parent.Children.Add(frame); navigation.Frame = frame;
            frame.Navigate(typeof(LibraryPage), new LibraryPage.NavigationArgs(new Library { Id = 11, Name = "Synthetic artwork", Type = "movie" }, "Library"));
            var page = (LibraryPage)frame.Content;
            await Until(() => page.IsLoaded && page.ViewModel.Library is not null);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_ONLY") == "artwork-review")
            {
                await RecommendedCasesAsync(page, frame, wire, client);
                frame.Navigate(typeof(Page)); parent.Children.Remove(frame); frame = null; await Task.Delay(200);
                await BlurStageCasesAsync(parent, client, spoilers);
                if (Failures.Count > 0) throw new InvalidOperationException(string.Join("; ", Failures));
                return;
            }
            ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("LibraryTab")).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => page.ViewModel.GetWindowItem(0) is not null);
            var scroll = (ScrollViewer)page.FindName("LibraryScroller");
            await Until(() => scroll.ScrollableHeight > 10000);
            await Task.Delay(1200);
            var slots = (IEnumerable<LibraryGridCard>)typeof(LibraryPage).GetField("_libraryCardSlots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            var sources = 0; var decoded = 0;
            foreach (var card in slots)
            {
                var image = CardImage(card);
                image.RegisterPropertyChangedCallback(Image.SourceProperty, (_, _) => { if (image.Source != null) sources++; });
                image.ImageOpened += (_, _) => decoded++;
            }
            var before = wire.ImageRequests;
            for (var step = 1; step <= 18; step++)
            {
                scroll.ChangeView(null, step * 400, null, true);
                await Task.Delay(85);
            }
            var during = wire.ImageRequests - before;
            Program.Log($"INFO artwork owned LibraryPage ChangeView fast request count={during}; new sources={sources}; ImageOpened={decoded}; offset={scroll.VerticalOffset}");
            Check(during == 0 && sources == 0 && decoded == 0, $"Fast LibraryPage work: requests={during}, sources={sources}, openedEvents={decoded}; expected zero.");
            await Until(() => wire.ImageRequests > before);
            await Until(() => slots.Any(card => CardImage(card).Source is BitmapImage { PixelWidth: > 0 }));
            Program.Log("PASS artwork current library requests and decoded presentation resume after settling.");
            async Task MoveLibrary()
            {
                var target = scroll.VerticalOffset + 400;
                scroll.ChangeView(null, target, null, true);
                await Until(() => Math.Abs(scroll.VerticalOffset - target) < 1);
            }
            // Switching the retained library tab hides slots without unloading their controls.
            await MoveLibrary();
            var hidden = slots.First(card => card.MediaItem != null && card.Visibility == Visibility.Visible);
            var hiddenItem = hidden.MediaItem!; hiddenItem.PosterUrl = "https://artwork-motion.invalid/art/library-hidden-tab";
            hidden.BindPlaceholder(); hidden.Bind(hiddenItem, "title");
            Program.Log($"INFO hidden bind offset={scroll.VerticalOffset} loaded={hidden.IsLoaded} pendingRequests={wire.Requests.GetValueOrDefault("/art/library-hidden-tab")}");
            ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("RecommendedTab")).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => ((Grid)page.FindName("LibraryContentArea")).Visibility == Visibility.Collapsed);
            Program.Log($"INFO hidden after tab suspended={typeof(LibraryGridCard).GetField("_artworkSuspended", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hidden)} requests={wire.Requests.GetValueOrDefault("/art/library-hidden-tab")}");
            await Task.Delay(400);
            Check(!wire.Requests.ContainsKey("/art/library-hidden-tab"), "Hidden library tab cancels pending artwork without unloading controls.");
            ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("LibraryTab")).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => slots.Any(card => card.MediaItem?.PosterUrl == hiddenItem.PosterUrl && CardImage(card).Source is BitmapImage { PixelWidth: > 0 }));
            // Keep the real cached page and its current bind across departure.
            await MoveLibrary();
            var retained = slots.First(card => card.MediaItem != null && card.Visibility == Visibility.Visible);
            var retainedItem = retained.MediaItem!;
            retainedItem.PosterUrl = "https://artwork-motion.invalid/art/library-retained";
            retained.BindPlaceholder(); retained.Bind(retainedItem, "title");
            var readAheadField = typeof(LibraryPage).GetField("_libraryReadAheadCts", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var previousReadAhead = (CancellationTokenSource?)readAheadField.GetValue(page);
            previousReadAhead?.Cancel(); previousReadAhead?.Dispose();
            using var departureReadAhead = new CancellationTokenSource();
            readAheadField.SetValue(page, departureReadAhead);
            var motion = (ArtworkMotionPolicy)typeof(LibraryPage).GetField("_artworkMotion", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            motion.Reset(0, scroll.VerticalOffset); motion.Observe(0, scroll.VerticalOffset + 400);
            // Freeze only the settle timer so departure, not wall-clock timing, releases this wait.
            ((ITimer?)typeof(ArtworkMotionPolicy).GetField("_settle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(motion))?.Change(
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            var deferredReadAhead = motion.WaitAsync(departureReadAhead.Token);
            var canceledBeforeRelease = false;
            using var cancellation = departureReadAhead.Token.Register(() => canceledBeforeRelease = motion.IsDeferred);
            frame.Navigate(typeof(Page));
            await Task.Delay(400);
            Check(canceledBeforeRelease && deferredReadAhead.IsCanceled && readAheadField.GetValue(page) == null,
                "Library departure cancels and clears read-ahead before releasing the deferred artwork policy.");
            Check(!wire.Requests.ContainsKey("/art/library-retained"), "Library departure cancels a pending current bind.");
            var heldReads = wire.HeldLibraryStateReads;
            var heldFlagsReads = wire.HeldLibraryFlagsReads;
            var flagsGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stateGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wire.LibraryFlagsReadGate = flagsGate;
            wire.LibraryStateReadGate = stateGate;
            try
            {
                frame.GoBack();
                await Until(() => ReferenceEquals(frame.Content, page) && page.IsLoaded && wire.HeldLibraryFlagsReads > heldFlagsReads);
                await Until(() => slots.Any(card => card.MediaItem?.PosterUrl == retainedItem.PosterUrl && CardImage(card).Source is BitmapImage { PixelWidth: > 0 }));
                Check(!flagsGate.Task.IsCompleted && wire.HeldLibraryStateReads == heldReads,
                    "Retained library slots resume before the include_hidden library-flags read completes.");
                flagsGate.TrySetResult();
                await Until(() => wire.HeldLibraryStateReads > heldReads);
                Check(!stateGate.Task.IsCompleted, "Retained library slots resume before server-backed state restoration completes.");
            }
            finally
            {
                wire.LibraryFlagsReadGate = null; flagsGate.TrySetResult();
                wire.LibraryStateReadGate = null; stateGate.TrySetResult();
            }
            await Task.Delay(300); // Drain restoration before the next tab interaction.
            Program.Log("PASS artwork cached LibraryPage resumes missing current bind.");
            await RecommendedCasesAsync(page, frame, wire, client);
            frame.Navigate(typeof(LibraryPage), new LibraryPage.NavigationArgs(new Library { Id = 12, Name = "Synthetic audiobook library", Type = "audiobook" }, "Library"));
            page = (LibraryPage)frame.Content;
            await Until(() => page.IsLoaded && page.ViewModel.Library?.Id == 12);
            slots = (IEnumerable<LibraryGridCard>)typeof(LibraryPage).GetField("_libraryCardSlots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!;
            await Until(() => slots.Any(card => card.MediaItem?.Type == "audiobook" && CardImage(card).Source is BitmapImage { PixelWidth: > 0 }));
            ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("AudiobookAuthorsButton")).GetPattern(PatternInterface.Invoke)).Invoke();
            var groups = (Panel)page.FindName("AudiobookGroupsHost");
            await Until(() => groups.Children.OfType<Button>().Any());
            ((IInvokeProvider)new ButtonAutomationPeer(groups.Children.OfType<Button>().Single()).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => page.ViewModel.SelectedAuthor == "Synthetic Writer" && ((Grid)page.FindName("LibraryContentArea")).Visibility == Visibility.Visible);
            await Task.Delay(1000);
            var boundBooks = slots.Where(card => card.Visibility == Visibility.Visible && card.MediaItem != null).ToArray();
            Check(boundBooks.Length > 0 && boundBooks.All(card => CardImage(card).Source is BitmapImage { PixelWidth: > 0 }), "Selecting an audiobook group resumes artwork on every retained books-grid slot");
            frame.Navigate(typeof(Page)); parent.Children.Remove(frame); frame = null; await Task.Delay(300);
            foreach (var kind in new[] { "library", "poster", "landscape" })
                await CardCasesAsync(parent, wire, client, kind);
            await LibraryPlaceholderCasesAsync(parent, wire);
            await BlurStageCasesAsync(parent, client, spoilers);
            await FirstMountCasesAsync(parent, wire, client, spoilers);
            if (Failures.Count > 0) throw new InvalidOperationException(string.Join("; ", Failures));
        }
        finally
        {
            if (frame != null) { frame.Navigate(typeof(Page)); parent.Children.Remove(frame); await Task.Delay(300); }
            field.SetValue(null, previous);
        }
    }
    private static async Task Until(Func<bool> ready)
    {
        for (var i = 0; i < 200; i++) { if (ready()) return; await Task.Delay(25); }
        throw new TimeoutException("Artwork native condition did not become ready.");
    }
    private static readonly List<string> Failures = [];
    private static void Check(bool condition, string message)
    {
        Program.Log((condition ? "PASS " : "FAIL ") + message);
        if (!condition) Failures.Add(message);
    }
    private static Image CardImage(FrameworkElement card) => card switch
    {
        LibraryGridCard library => (Image)typeof(LibraryGridCard).GetField("_posterImage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(library)!,
        PosterCard poster => (Image)poster.FindName("PosterImage"),
        LandscapeCard landscape => (Image)landscape.FindName("BackdropImage"),
        _ => throw new NotSupportedException(),
    };
    private static FrameworkElement NewCard(string kind) => kind switch
    { "library" => new LibraryGridCard(), "poster" => new PosterCard(), _ => new LandscapeCard() };
    private static void Bind(FrameworkElement card, string id, string? contentId = null)
    {
        var item = new MediaItem { ContentId = contentId ?? id, Title = id, Type = "movie", PosterUrl = "https://artwork-motion.invalid/art/" + id, BackdropUrl = "https://artwork-motion.invalid/art/" + id };
        switch (card) { case LibraryGridCard library: library.Bind(item, "title"); break; case PosterCard poster: poster.MediaItem = item; break; case LandscapeCard landscape: landscape.MediaItem = item; break; }
    }
    private static async Task CardCasesAsync(StackPanel parent, Wire wire, SiloApiClient client, string kind)
    {
        var card = NewCard(kind); var image = CardImage(card);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(card); row.Children.Add(new Border { Width = 20000, Height = 400 });
        var scroll = new ScrollViewer { Width = 500, Height = 400, HorizontalScrollMode = ScrollMode.Enabled, Content = row };
        var rail = new CarouselRail(scroll); parent.Children.Add(rail);
        var other = NewCard(kind); var otherImage = CardImage(other); parent.Children.Add(other);
        await Until(() => card.IsLoaded && scroll.ScrollableWidth > 5000 && other.IsLoaded);
        var sources = 0; var openedEvents = 0;
        image.RegisterPropertyChangedCallback(Image.SourceProperty, (_, _) => { if (image.Source != null) sources++; });
        image.ImageOpened += (_, _) => openedEvents++;
        var offset = 0d;
        async Task Move()
        {
            offset += 300; if (offset > 16000) offset = 300;
            scroll.ChangeView(offset, null, null, true);
            await Until(() => Math.Abs(scroll.HorizontalOffset - offset) < 1);
        }
        async Task Burst(int count = 7)
        { for (var i = 0; i < count; i++) { await Move(); await Task.Delay(55); } }
        try
        {
            await Move();
            Bind(card, kind + "-fast"); Bind(other, kind + "-independent");
            await Burst(10);
            Check(!wire.Requests.ContainsKey("/art/" + kind + "-fast") && sources == 0 && openedEvents == 0, kind + " fast motion suppresses requests/source assignments/ImageOpened");
            Check(otherImage.Source is BitmapImage { PixelWidth: > 0 }, kind + " stationary viewport loads during another viewport motion");
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 });
            Check(sources == 1 && openedEvents == 1, kind + " settles into exactly one decoded presentation");

            // Cheap decoded hits remain immediate even while new work is paused.
            await Move();
            if (kind != "library")
            {
                var cached = otherImage.Source;
                Bind(card, kind + "-independent");
                Check(ReferenceEquals(image.Source, cached) && wire.Requests.GetValueOrDefault("/art/" + kind + "-independent") == 1, kind + " decoded cache hit bypasses motion wait");
            }
            await Task.Delay(180);

            // A completed HTTP response during fast motion must not start a native decoder.
            var late = kind + "-late";
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); wire.Gates["/art/" + late] = gate;
            Bind(card, late); await Until(() => wire.Requests.ContainsKey("/art/" + late));
            await Move(); sources = 0; openedEvents = 0; gate.TrySetResult();
            await Burst();
            Check(image.Source == null && sources == 0 && openedEvents == 0, kind + " completed download waits before source assignment/ImageOpened");
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 });

            // Waiting behind an existing class decoder must recheck motion after acquiring it.
            var decoderField = card.GetType().GetField("s_bitmapCreateLock", BindingFlags.NonPublic | BindingFlags.Static);
            if (decoderField?.GetValue(null) is SemaphoreSlim decoder)
            {
                await decoder.WaitAsync();
                try
                {
                    Bind(card, kind + "-decoder-queue");
                    await Until(() => wire.Requests.ContainsKey("/art/" + kind + "-decoder-queue"));
                    await Task.Delay(100); await Move(); sources = 0; openedEvents = 0;
                }
                finally { decoder.Release(); }
                Bind(other, kind + "-decoder-independent");
                await Burst();
                Check(image.Source == null && sources == 0 && openedEvents == 0, kind + " queued decoder rechecks motion");
                Check(otherImage.Source is BitmapImage { PixelWidth: > 0 }, kind + " deferred decoder does not hold another viewport lock");
                await Until(() => image.Source is BitmapImage { PixelWidth: > 0 });
            }

            // Same content id with a replacement URL retires a download already in flight.
            var stale = kind + "-inflight-old";
            var staleGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); wire.Gates["/art/" + stale] = staleGate;
            Bind(card, stale); await Until(() => wire.Requests.ContainsKey("/art/" + stale));
            await Move(); sources = 0; openedEvents = 0;
            Bind(card, kind + "-inflight-new", stale); staleGate.TrySetResult();
            await Burst();
            Check(image.Source == null && sources == 0 && openedEvents == 0, kind + " canceled inflight reply cannot decode or present");
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 });
            Check(sources == 1 && wire.Requests.GetValueOrDefault("/art/" + kind + "-inflight-new") == 1, kind + " replacement URL owns one presentation");

            // Cancel old/new binds during movement, then only show the latest URL.
            await Move(); sources = 0; openedEvents = 0;
            Bind(card, kind + "-old"); await Task.Delay(20); Bind(card, kind + "-new");
            await Burst();
            Check(!wire.Requests.ContainsKey("/art/" + kind + "-old") && !wire.Requests.ContainsKey("/art/" + kind + "-new") && sources == 0, kind + " rebinding retires pending URL work");
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 });
            Check(wire.Requests.GetValueOrDefault("/art/" + kind + "-new") == 1 && sources == 1, kind + " only latest rebind presents");

            await Move(); Bind(card, kind + "-unload");
            row.Children.Remove(card); await Task.Delay(400);
            Check(!wire.Requests.ContainsKey("/art/" + kind + "-unload") && image.Source == null, kind + " unload retires deferred artwork");
            row.Children.Insert(0, card);
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 });
            Check(wire.Requests.GetValueOrDefault("/art/" + kind + "-unload") == 1, kind + " unchanged retained bind reloads on return");

            // Small actual offsets should not postpone a card beyond its existing bind grace.
            Bind(card, kind + "-slow");
            for (var i = 0; i < 14; i++) { offset += 1; scroll.ChangeView(offset, null, null, true); await Task.Delay(35); }
            Check(image.Source is BitmapImage { PixelWidth: > 0 }, kind + " slow motion presents without a settle wait");

            if (card is LibraryGridCard reset)
            {
                await Move(); Bind(card, kind + "-hidden-slot"); reset.Reset(); await Burst(); await Task.Delay(180);
                Check(!wire.Requests.ContainsKey("/art/" + kind + "-hidden-slot") && image.Source == null, "Hidden/unbound library slot retires pending work");
            }
            // A retained DTO cannot acquire a successor profile's identity merely by loading again.
            await Move(); Bind(card, kind + "-retained-owner");
            row.Children.Remove(card); await Until(() => !card.IsLoaded);
            client.SetProfile("successor-" + kind);
            Bind(other, kind + "-retained-owner");
            await Until(() => otherImage.Source is BitmapImage { PixelWidth: > 0 });
            Check(otherImage.Source != null, kind + " authorized successor bind loads");
            row.Children.Insert(0, card); await Until(() => card.IsLoaded); await Task.Delay(450);
            Check(image.Source == null, kind + " retained old DTO cannot adopt successor identity or decoded cache");
            Bind(card, kind + "-retained-owner");
            await Until(() => image.Source is BitmapImage { PixelWidth: > 0 });
            Check(image.Source != null, kind + " fresh current-profile DTO bind recovers");

            row.Children.Remove(card); await Until(() => !card.IsLoaded);
            Bind(card, kind + "-bound-unloaded"); client.SetProfile("unloaded-successor-" + kind);
            row.Children.Insert(0, card); await Until(() => card.IsLoaded); await Task.Delay(450);
            Check(image.Source == null, kind + " bind while unloaded retains its original profile owner");

            await Move(); Bind(card, kind + "-retired-profile");
            await Task.Delay(15); client.SetProfile("retired-" + kind);
            await Burst(); await Task.Delay(450);
            Check(!wire.Requests.ContainsKey("/art/" + kind + "-retired-profile") && image.Source == null, kind + " retired profile cannot adopt queued artwork");
        }
        finally
        {
            foreach (var pending in wire.Gates.Values) pending.TrySetResult();
            parent.Children.Remove(rail); parent.Children.Remove(other); await Task.Delay(300);
        }
    }
    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<LibraryViewModel> model) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(LibraryViewModel) ? model() : values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }
    private sealed class Wire : HttpMessageHandler
    {
        internal int InvalidCursors, LibraryId = 11;
        internal int ImageRequests, HeldLibraryStateReads, HeldLibraryFlagsReads;
        internal TaskCompletionSource? LibraryStateReadGate, LibraryFlagsReadGate;
        internal ConcurrentDictionary<string, int> Requests = new();
        internal ConcurrentDictionary<string, TaskCompletionSource> Gates = new();
        internal List<int> Seeks { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/art/"))
            {
                Interlocked.Increment(ref ImageRequests);
                Requests.AddOrUpdate(path, 1, (_, count) => count + 1);
                var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jT1sAAAAASUVORK5CYII=");
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                response.Content.Headers.ContentType = new("image/png");
                return Gates.TryGetValue(path, out var gate) ? CompleteImageAsync(response, gate) : Task.FromResult(response);
            }
            if (path == "/api/v2/user/libraries" && request.RequestUri.Query.Contains("include_hidden=true", StringComparison.Ordinal)
                && LibraryFlagsReadGate is { } flagsGate)
            {
                Interlocked.Increment(ref HeldLibraryFlagsReads);
                return CompleteLibraryFlagsReadAsync(flagsGate, ct);
            }
            if (path == "/api/v2/catalog/search/capabilities") return Reply(new { letter_index = true });
            if (path == "/api/v2/catalog/letters") return Reply(new
            {
                total = 1000,
                letters = new[] { new { letter = "#", count = 20, position = 0 }, new { letter = "A", count = 679, position = 20 }, new { letter = "M", count = 300, position = 600 }, new { letter = "Z", count = 1, position = 999 } },
                runs = new[] { new { letter = "#", position = 0 }, new { letter = "A", position = 20 }, new { letter = "M", position = 600 }, new { letter = "A", position = 900 }, new { letter = "Z", position = 999 } },
            });
            if (path == "/api/v2/catalog/audiobook-groups") return Reply(new { items = new[] { new { name = "Synthetic Writer", item_count = 20, total_duration_seconds = 36000 } }, total = 1, page = new { has_more = false } });
            if (path == "/api/v2/catalog")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
                    .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
                var limit = int.Parse(query["limit"]); var offset = query.TryGetValue("seek", out var seek) ? int.Parse(seek) : 0;
                if (query.ContainsKey("seek")) Seeks.Add(offset);
                if (query.TryGetValue("cursor", out var cursor))
                {
                    var scope = cursor.Split(':');
                    if (scope[0] != query["library_id"] || scope[1] != query["limit"])
                    {
                        InvalidCursors++;
                        return Reply(new { status = 400, type = "https://fixture.invalid/problems/invalid_cursor", detail = "The cursor is malformed, tampered with, or belongs to a different query." }, HttpStatusCode.BadRequest);
                    }
                    if (!query.ContainsKey("seek")) offset = int.Parse(scope[2]);
                }
                var count = Math.Min(limit, 1000 - offset);
                return Reply(new { items = Enumerable.Range(offset, count).Select(index => new { content_id = "item-" + index, title = "Fixture " + index, type = query["library_id"] == "12" ? "audiobook" : "movie", poster_url = "https://artwork-motion.invalid/art/" + index }),
                    total = 1000, total_exact = !query.ContainsKey("skip_total"), window_cursor = query["library_id"] + ":" + limit + ":0",
                    page = new { has_more = offset + count < 1000, next_cursor = query["library_id"] + ":" + limit + ":" + (offset + count) } });
            }
            if (path == "/api/v2/settings/contract/capabilities") return Reply(new { api_version = 1, manifest_revision = 17, supports_batched_effective = true });
            if (path == "/api/v2/settings/values/effective")
            {
                if (LibraryStateReadGate is { } stateGate && request.RequestUri.Query.Contains("ui.library_page_state", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref HeldLibraryStateReads);
                    return CompleteLibraryStateReadAsync(stateGate, ct);
                }
                return EffectiveSettingsReply();
            }
            if (path == "/api/v2/library/11/layout") return Reply(new { sections = new[] { new { id = "poster", title = "Posters", section_type = "recently_added", item_limit = 30 }, new { id = "landscape", title = "Wide", section_type = "next_up", item_limit = 30 } } });
            if (path.StartsWith("/api/v2/library/11/sections/"))
            {
                var id = path.Split('/')[6];
                return Reply(new { id, title = id, section_type = id == "poster" ? "recently_added" : "next_up", item_limit = 30,
                    items = Enumerable.Range(0, 30).Select(i => new { content_id = id + i, type = "movie", title = id + i, poster_url = "https://artwork-motion.invalid/art/row-" + id + i, backdrop_url = "https://artwork-motion.invalid/art/row-" + id + i }) });
            }
            if (path.EndsWith("/layout") || path.EndsWith("/sections")) return Reply(new { sections = Array.Empty<object>() });
            if (path.EndsWith("/filters")) return Reply(new { genres = Array.Empty<string>() });
            return Reply(new { status = 404, detail = "No isolated fixture value" }, HttpStatusCode.NotFound);
        }
        private static Task<HttpResponseMessage> EffectiveSettingsReply()
            => Reply(new { items = new[] { new { key = "catalog.hide_unwatched_episode_images", value = true }, new { key = "catalog.hide_unwatched_episode_overviews", value = true } } });
        private static async Task<HttpResponseMessage> CompleteLibraryFlagsReadAsync(TaskCompletionSource gate, CancellationToken ct)
        {
            await gate.Task.WaitAsync(ct);
            return await Reply(new { items = new[] { new { id = 11, name = "Synthetic artwork", type = "movie", enabled = true } } });
        }
        private static async Task<HttpResponseMessage> CompleteLibraryStateReadAsync(TaskCompletionSource gate, CancellationToken ct)
        { await gate.Task.WaitAsync(ct); return await EffectiveSettingsReply(); }
        private static async Task<HttpResponseMessage> CompleteImageAsync(HttpResponseMessage response, TaskCompletionSource gate)
        { await gate.Task; return response; }
        private static Task<HttpResponseMessage> Reply(object body, HttpStatusCode status = HttpStatusCode.OK)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
    }
}
