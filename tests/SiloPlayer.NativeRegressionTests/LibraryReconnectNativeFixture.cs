using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

/// <summary>
/// The server closes the event channel every five minutes and each reconnect bumps
/// the read generation. A scrolled Library tab must keep its page, offset and cards
/// while the catch-up merges server changes in place.
/// </summary>
internal static class LibraryReconnectNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, Window window)
    {
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://library-reconnect.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" }); auth.SelectProfile("fixture");
        var catalog = new CatalogApi(client); var settings = new SettingsApi(client);
        using var events = new EventChannelClient(client, auth);
        using var images = new ImageService(Path.Combine(Program.ResultDirectory, "images"));
        var navigation = new NavigationService();
        field.SetValue(null, new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(AuthService)] = auth, [typeof(CatalogApi)] = catalog,
            [typeof(SettingsApi)] = settings, [typeof(NavigationService)] = navigation,
            [typeof(SettingsService)] = new SettingsService(Path.Combine(Program.ResultDirectory, "settings")),
            [typeof(UICustomizationService)] = new UICustomizationService(settings),
            [typeof(CardOverlayService)] = new CardOverlayService(settings), [typeof(EventChannelClient)] = events,
            [typeof(ImageService)] = images, [typeof(HttpClient)] = http,
        }, () => new LibraryViewModel(catalog)));
        Frame? frame = null;
        try
        {
            window.AppWindow.ResizeClient(new(1920, 1080));
            frame = new Frame { Width = 1920, Height = 1080 }; parent.Children.Add(frame); navigation.Frame = frame;
            frame.Navigate(typeof(LibraryPage), new Library { Id = 41, Name = "Fixture movies", Type = "movie" });
            var page = (LibraryPage)frame.Content;
            await Until(() => page.IsLoaded && page.ViewModel.Library is not null);
            ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("LibraryTab")).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => page.ViewModel.GetWindowItem(0) is not null);
            var scroll = (ScrollViewer)page.FindName("LibraryScroller");
            const double offset = 30000;
            await Until(() => scroll.ScrollableHeight > offset);
            // Scroll the way a user does, in steps; retry until the restored-offset pass has run.
            foreach (var step in new[] { 600d, 4000d, 12000d, offset })
                for (var attempt = 0; attempt < 20 && Math.Abs(scroll.VerticalOffset - step) > 2; attempt++)
                {
                    scroll.ChangeView(null, step, null, true);
                    await Task.Delay(150);
                }
            await Task.Delay(300);
            await Until(() => !page.ViewModel.IsLoading && page.ViewModel.WindowStartIndex > 100,
                () => $"window={page.ViewModel.WindowStartIndex} loading={page.ViewModel.IsLoading} offset={scroll.VerticalOffset:0} extent={scroll.ScrollableHeight:0}");
            var first = page.ViewModel.WindowStartIndex;
            var total = page.ViewModel.TotalCount;
            var totals = new List<int>();
            void Watch(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(LibraryViewModel.TotalCount)) totals.Add(page.ViewModel.TotalCount); }
            page.ViewModel.PropertyChanged += Watch;
            try
            {
                // 1. A reconnect, then the user keeps scrolling before any catch-up runs.
                Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true), "fixture bumps the read generation like a reconnect");
                scroll.ChangeView(null, offset + 300, null, true);
                await Task.Delay(300); await Until(() => !page.ViewModel.IsLoading);
                Check(!totals.Contains(0) && page.ViewModel.TotalCount == total && Math.Abs(scroll.VerticalOffset - (offset + 300)) < 2 &&
                    page.ViewModel.GetWindowItem(first) is not null,
                    "scrolling after a reconnect keeps the extent, offset and loaded cards (no restart from the top)");
                scroll.ChangeView(null, offset, null, true);
                await Task.Delay(300); await Until(() => !page.ViewModel.IsLoading);

                // 2. Titles changed while disconnected; the shell's catch-up runs in place.
                var index = first + 5;
                var held = Enumerable.Range(0, total).Where(row => page.ViewModel.GetWindowItem(row) is not null).ToArray();
                wire.Inserted = index;
                Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true), "second reconnect");
                Check(page.CanPreserveOnReconnect, "a scrolled, loaded Library tab can be kept across a reconnect");
                await page.CatchUpAfterReconnectAsync();
                await Task.Delay(300); await Until(() => !page.ViewModel.IsLoading);
                Check(ReferenceEquals(frame.Content, page), "catch-up keeps the same page instance");
                Check(!totals.Contains(0) && Math.Abs(scroll.VerticalOffset - offset) < 2,
                    $"catch-up keeps the scroll offset (offset {scroll.VerticalOffset:0}, totals {string.Join(",", totals)})");
                Check(page.ViewModel.GetWindowItem(index)?.ContentId == "inserted" &&
                    page.ViewModel.GetWindowItem(index + 1)?.ContentId == "item-" + index,
                    "an added title is merged into its position and later titles shift by one");
                await Until(() => page.ViewModel.TotalCount == total + 1); // the count is read after the merge
                Check(held.Length > 0 && held.All(row => page.ViewModel.GetWindowItem(row) is not null),
                    $"all {held.Length} rows loaded before the reconnect are still held (lost: {string.Join(",", held.Where(row => page.ViewModel.GetWindowItem(row) is null).Take(12))})");

                // Returning from another tab asks for the letter rail before the viewport.
                // A large active window must not make that optional read prioritize its start.
                await page.ViewModel.LoadWindowAsync(0, 1200, force: true);
                await Task.Delay(300);
                var heldStart = page.ViewModel.WindowStartIndex;
                var cards = (Dictionary<int, LibraryGridCard>)typeof(LibraryPage)
                    .GetField("_visibleLibraryCards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
                Check(cards.Keys.Min() > heldStart + 100, "the viewport is far below the retained active-window start");
                ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("RecommendedTab")).GetPattern(PatternInterface.Invoke)).Invoke();
                Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true), "reconnect while another tab is active");
                var reads = wire.WindowSeeks.Count;
                ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("LibraryTab")).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => wire.WindowSeeks.Count > reads && !page.ViewModel.RequiresCatalogRefresh);
                Check(wire.WindowSeeks[reads] > heldStart + 100 && Math.Abs(scroll.VerticalOffset - offset) < 2,
                    "Library tab activation confirms the visible range first, not the distant held-window start");
                Program.Log("PASS: Library tab keeps its page, offset and loaded windows across event-channel reconnects and merges changes in place.");
            }
            finally { page.ViewModel.PropertyChanged -= Watch; }

            // A reconnect can miss an access/rating change on Recommended too.
            wire.RecommendedEnabled = true;
            ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("RecommendedTab")).GetPattern(PatternInterface.Invoke)).Invoke();
            var recommended = (ScrollViewer)page.FindName("RecommendedPanel");
            var hero = (HeroCarousel)page.FindName("RecommendedHeroCarousel");
            var sections = (StackPanel)page.FindName("RecommendedSectionsPanel");
            var awaiting = typeof(LibraryPage).GetField("_recommendedCardsAwaitingConfirmation", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var rawField = typeof(LibraryPage).GetField("_rawRecommendedSections", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var refreshField = typeof(LibraryPage).GetField("_recommendationsRefreshCts", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var refreshGate = (DeferredSurfaceRefreshGate)typeof(LibraryPage)
                .GetField("_recommendedRefreshGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            foreach (var activation in new[] { "hidden", "debounce", "read" })
            {
                wire.RecommendedRevoked = false;
                await page.RefreshLocalizedContentAsync();
                await Until(() => hero.Visibility == Visibility.Visible && sections.Children.OfType<SectionRow>().Any(row => row.Section?.Items.Count > 0));
                if (activation == "hidden")
                    ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("LibraryTab")).GetPattern(PatternInterface.Invoke)).Invoke();
                if (activation == "read")
                {
                    wire.RecommendedLayoutGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    wire.RecommendedLayoutStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                wire.RecommendedRevoked = true;
                Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true), "Recommended activation reconnect");
                page.HideUnconfirmedCatalogRows();
                await page.CatchUpAfterReconnectAsync();
                if (activation != "hidden")
                {
                    try
                    {
                        if (activation == "read")
                            await wire.RecommendedLayoutStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                        ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("LibraryTab")).GetPattern(PatternInterface.Invoke)).Invoke();
                    }
                    finally
                    {
                        wire.RecommendedLayoutGate?.TrySetResult();
                        wire.RecommendedLayoutGate = null;
                    }
                    await Until(() => refreshField.GetValue(page) is null);
                    Check(refreshGate.HasPendingRefresh, $"leaving during the confirming {activation} keeps a pending refresh");
                }
                Check((bool)awaiting.GetValue(page)! && recommended.Visibility == Visibility.Collapsed,
                    $"Recommended stays quarantined after the {activation} reconnect");
                var layoutReads = wire.RecommendedLayoutReads;
                ((IInvokeProvider)new ButtonAutomationPeer((Button)page.FindName("RecommendedTab")).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => !(bool)awaiting.GetValue(page)! && recommended.Visibility == Visibility.Visible);
                Check(wire.RecommendedLayoutReads > layoutReads && recommended.IsHitTestVisible,
                    $"Recommended activation rereads and reveals the {activation} surface without another event");
                Check(hero.ItemsSource is null && !sections.Children.OfType<SectionRow>().Any(row => row.Section?.Items.Count > 0),
                    $"Recommended activation after {activation} cannot restore revoked cards");
            }
            foreach (var failure in new[] { "none", "section", "null", "layout" })
            {
                wire.RecommendedFailure = "none";
                wire.RecommendedRevoked = false;
                await page.RefreshLocalizedContentAsync();
                await Until(() => hero.Visibility == Visibility.Visible && sections.Children.OfType<SectionRow>().Any(row => row.Section?.Items.Count > 0));
                Check(page.CanPreserveOnReconnect, "a populated Recommended tab can catch up in place");
                wire.RecommendedRevoked = true; // missed access_changed while disconnected
                wire.RecommendedFailure = failure;
                Check(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true), "Recommended reconnect");
                page.HideUnconfirmedCatalogRows(); // shell hides before playback can defer the reads
                Check(recommended.Visibility == Visibility.Collapsed && !recommended.IsHitTestVisible,
                    "Recommended hero and sections are hidden and disabled before catch-up reads");
                await page.CatchUpAfterReconnectAsync();
                await Until(() => !(bool)awaiting.GetValue(page)!);
                Check(ReferenceEquals(frame.Content, page), "Recommended catch-up keeps its page instance");
                Check(hero.ItemsSource is null && !sections.Children.OfType<SectionRow>().Any(row => row.Section?.Items.Count > 0),
                    $"Recommended {failure} reread cannot expose previously authorized titles");
                var raw = (Dictionary<string, HomeSectionWithItems>)rawField.GetValue(page)!;
                Check(raw.Values.All(section => section.Items.Count == 0), $"Recommended {failure} reread does not retain old raw sections");
            }
            Program.Log("PASS: Recommended reconnect quarantines hero/rows and never restores revoked titles after empty, failed or null section/layout reads.");
        }
        finally
        {
            frame?.Navigate(typeof(Page)); if (frame is not null) parent.Children.Remove(frame);
            await Task.Delay(150); navigation.Frame = null; field.SetValue(null, previous);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Library reconnect: " + message);
    }

    private static async Task Until(Func<bool> ready, Func<string>? state = null)
    {
        for (var i = 0; i < 250; i++) { if (ready()) return; await Task.Delay(20); }
        throw new TimeoutException("Library reconnect native state did not settle. " + state?.Invoke());
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<LibraryViewModel> model) : IServiceProvider
    { public object? GetService(Type type) => type == typeof(LibraryViewModel) ? model() : values.TryGetValue(type, out var value) ? value : fallback.GetService(type); }

    private sealed class Wire : HttpMessageHandler
    {
        private const int BaseTotal = 3000;
        internal int Inserted = -1;
        internal bool RecommendedEnabled, RecommendedRevoked;
        internal string RecommendedFailure = "none";
        internal readonly List<int> WindowSeeks = [];
        internal int RecommendedLayoutReads;
        internal TaskCompletionSource? RecommendedLayoutGate;
        internal TaskCompletionSource RecommendedLayoutStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string Id(int index) => Inserted < 0 || index < Inserted ? "item-" + index : index == Inserted ? "inserted" : "item-" + (index - 1);
        private int Total => BaseTotal + (Inserted < 0 ? 0 : 1);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/catalog")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2))
                    .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair[1]));
                var limit = int.Parse(query["limit"]); var offset = query.TryGetValue("seek", out var seek) ? int.Parse(seek) : 0;
                if (query.TryGetValue("cursor", out var cursor) && !query.ContainsKey("seek") && cursor.StartsWith("next:", StringComparison.Ordinal))
                    offset = int.Parse(cursor[5..]);
                if (query.ContainsKey("skip_total")) WindowSeeks.Add(offset);
                var total = Total;
                var count = Math.Max(0, Math.Min(limit, total - offset));
                return Reply(new { items = Enumerable.Range(offset, count).Select(index => new { content_id = Id(index), title = "Fixture " + Id(index), type = "movie" }),
                    total, total_exact = !query.ContainsKey("skip_total"), window_cursor = "window",
                    page = new { has_more = offset + count < total, next_cursor = "next:" + (offset + count) } });
            }
            if (path.EndsWith("/layout") && RecommendedEnabled)
            {
                RecommendedLayoutReads++;
                if (RecommendedFailure == "layout") return Reply(new { detail = "Synthetic layout failure" }, HttpStatusCode.ServiceUnavailable);
                return ReplyRecommendedLayoutAsync(new { sections = new[] {
                    new { id = "hero", section_type = "recently_added", title = "Featured", featured = true, item_limit = 1 },
                    new { id = "row", section_type = "recently_added", title = "Titles", featured = false, item_limit = 1 },
                } }, ct);
            }
            if (path.Contains("/sections/", StringComparison.Ordinal) && path.EndsWith("/items") && RecommendedEnabled)
            {
                if (RecommendedFailure == "section") return Reply(new { detail = "Synthetic section failure" }, HttpStatusCode.ServiceUnavailable);
                if (RecommendedFailure == "null")
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json") });
                var id = path.Contains("/hero/", StringComparison.Ordinal) ? "hero" : "row";
                return Reply(new { id, section_type = "recently_added", title = "Fixture " + id, item_limit = 1,
                    items = RecommendedRevoked ? Array.Empty<object>() : new object[] { new { content_id = id + "-title", title = "Previously authorized", type = "movie" } } });
            }
            if (path.EndsWith("/layout") || path.EndsWith("/sections")) return Reply(new { sections = Array.Empty<object>() });
            if (path.EndsWith("/filters")) return Reply(new { genres = Array.Empty<string>() });
            return Reply(new { status = 404, detail = "No isolated fixture value" }, HttpStatusCode.NotFound);
        }

        private async Task<HttpResponseMessage> ReplyRecommendedLayoutAsync(object body, CancellationToken ct)
        {
            if (RecommendedLayoutGate is { } gate)
            {
                RecommendedLayoutStarted.TrySetResult();
                await gate.Task.WaitAsync(ct);
            }
            return await Reply(body);
        }

        private static Task<HttpResponseMessage> Reply(object body, HttpStatusCode status = HttpStatusCode.OK)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
    }
}
