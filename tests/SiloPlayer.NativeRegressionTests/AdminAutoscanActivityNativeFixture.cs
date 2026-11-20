using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class AdminAutoscanActivityNativeFixture
{
    private sealed class Wire : HttpMessageHandler
    {
        internal readonly System.Collections.Concurrent.ConcurrentQueue<string> Reads = new();
        internal int Writes;
        internal bool FailHistory, FailStatus, EmptyHistory, UnknownCancel;
        internal TaskCompletionSource? HoldHistory;
        internal long ActiveScans = 2;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Reads.Enqueue(request.RequestUri.PathAndQuery);
            if (request.Method == HttpMethod.Post)
            {
                Writes++;
                using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                Check(path == "/api/v2/scan/cancel" && data.RootElement.GetProperty("library_id").GetString() == "7", "cancel uses the exact published body");
                if (UnknownCancel) throw new HttpRequestException("synthetic cancellation uncertainty");
                return Json("""{"library_id":"7","cancelled":2}""");
            }
            if (path.EndsWith("/scans") || path.EndsWith("/events"))
            {
                if (HoldHistory is { } hold) await hold.Task;
                if (FailHistory) return Json("""{"title":"Synthetic unavailable","status":503}""", HttpStatusCode.ServiceUnavailable);
                var scan = """{"id":"scan-one","library_id":"7","mode":"file","path":"/synthetic/movies/a-long-folder/a-long-title.mkv","trigger":"autoscan","status":"completed","requested_at":"2026-11-17T12:00:00Z","completed_at":"2026-11-17T12:00:01Z","source_id":"source-1","plugin_id":"synthetic","capability_id":"files","autoscan_event_id":"11","event_status":"success","result":{"new":1,"updated":2,"unchanged":0,"missing":0,"missing_skipped_protected":1,"files_deleted":0,"items_deleted":0,"memberships_removed":0,"errors":0,"skipped":0}}""";
                var poll = """{"id":"11","source_id":"source-1","plugin_id":"synthetic","capability_id":"files","started_at":"2026-11-17T12:00:00Z","completed_at":"2026-11-17T12:00:01Z","duration_ms":1000,"status":"success","delivery_mode":"webhook","provider_event_type":"Download","changes_returned":2,"changes_resolved":1,"targets_claimed":1,"scans_created":1,"scans_reused":1,"scans_suppressed":0,"scan_runs":[],"changes":[{"source_path":"/incoming/movie.mkv","rewritten_path":"/synthetic/movie.mkv","outcome":"joined","reason":"follow_up_scan","target_mode":"file","target_path":"/synthetic/movie.mkv","library_id":"7"},{"source_path":"/incoming/future.mkv","rewritten_path":"/incoming/future.mkv","outcome":"future_outcome","reason":"future_reason"}],"changes_truncated":true}""";
                var second = request.RequestUri.Query.Contains("cursor=", StringComparison.Ordinal);
                return Json("{\"items\":[" + (EmptyHistory ? "" : path.EndsWith("/scans") ? scan.Replace("scan-one", second ? "scan-two" : "scan-one") : poll) +
                    "],\"total\":75,\"page\":{\"has_more\":" + (second || EmptyHistory ? "false" : "true") + (second || EmptyHistory ? "" : ",\"next_cursor\":\"" + (path.EndsWith("/scans") ? "scan-cursor" : "poll-cursor") + "\"") + "}}");
            }
            if (path == "/api/v2/admin/autoscan/status" && FailStatus)
                return Json("""{"title":"Synthetic status unavailable","status":503}""", HttpStatusCode.ServiceUnavailable);
            return Json(path switch
            {
                "/api/v2/admin/autoscan/status" => "{\"enabled\":true,\"active_scans\":" + ActiveScans + ",\"accepted_scans\":1,\"running_scans\":1,\"sources\":[],\"running_polls\":[{\"id\":\"12\",\"source_id\":\"source-1\",\"plugin_id\":\"synthetic\",\"capability_id\":\"files\",\"started_at\":\"2026-11-17T12:00:00Z\",\"elapsed_ms\":500}]}",
                "/api/v2/libraries" => """{"items":[{"id":"7","name":"Synthetic Movies","type":"movies","enabled":true}],"page":{"has_more":false}}""",
                "/api/v2/admin/autoscan/sources" => """{"items":[{"id":"source-1","label":"Synthetic Source","plugin_id":"synthetic","capability_id":"files"}],"page":{"has_more":false}}""",
                "/api/v2/admin/autoscan/connections" => """{"items":[],"page":{"has_more":false}}""",
                "/api/v2/admin/autoscan/scan-source-plugins" => """{"items":[{"plugin_id":"synthetic","capability_id":"files","display_name":"Synthetic Plugin"}],"page":{"has_more":false}}""",
                "/api/v2/admin/server/status" => """{"restart_required":false,"restart_mark_count":0,"restart_requested":false}""",
                _ => "{}"
            });
        }
        private static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) => new(status)
            { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }

    private sealed class Channel : IAdminRealtimeChannel
    {
        internal int Leases;
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived { add { } remove { } }
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels) { Check(channels.SequenceEqual(["scans"]), "leases only the shared scans channel"); Leases++; return new Release(() => Leases--); }
        public bool TryGetLatestSnapshot(string channel, out JsonElement data) { data = default; return false; }
        internal void Snapshot() => SnapshotReceived?.Invoke("scans", JsonDocument.Parse("""[{"id":"queued","library_id":7,"mode":"library","trigger":"autoscan","status":"accepted"},{"id":"running","library_id":7,"mode":"file","path":"/synthetic/movie.mkv","trigger":"autoscan","status":"running","result":{"total_files":10,"files_processed":4,"message":"Processing files"}}]""").RootElement.Clone());
        private sealed class Release(Action release) : IDisposable { public void Dispose() => release(); }
    }

    internal static async Task RunAsync(Window window)
    {
        var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var channel = new Channel(); var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(navigation)
            .AddSingleton(new AdminMonitorApi(client)).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddSingleton(new AdminAutoscanActivityRealtime(channel));
        collection.AddAdminAutoscanActivity();
        await using var services = collection.BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = servicesField.GetValue(null); var previousContent = window.Content; var previousTitle = DocumentTitle.AppName;
        Frame? outer = null; AdminAutoscanActivityPage? page = null; AdminShellPage? shell = null;
        Task outstanding = Task.CompletedTask;
        var pageUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shellUnloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            servicesField.SetValue(null, services);
            new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000));
            outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            Check(AdminShellPage.TryOpenHref("/admin/autoscan/activity") is { Kind: AdminRouteResolutionKind.Route }, "Activity resolves through production registration");
            await Until(() => outer.Content is AdminShellPage, "real Admin shell"); shell = (AdminShellPage)outer.Content;
            shell.Unloaded += (_, _) => shellUnloaded.TrySetResult();
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminAutoscanActivityPage active && active.IsLoaded, "real Activity Frame page"); page = (AdminAutoscanActivityPage)inner.Content;
            page.Unloaded += (_, _) => pageUnloaded.TrySetResult();
            await Until(() => page.RenderedContent == "table" && TextShown(page, "Synthetic Source"), "scan rows and bounded label lookups");
            Check(!wire.Reads.Any(path => path.StartsWith("/api/v2/admin/autoscan/events")), "inactive Polls history is not read");
            Check(!wire.Reads.Any(path => path.Contains("offset=")), "history uses signed cursors rather than invented offset parameters");
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "in-place Frame has no slide transition");
            channel.Snapshot(); await Until(() => TextShown(page, "Processing files · 4 / 10 (40%)"), "live progress");
            Check(page.Controller!.State.Queue[0].Id == "running", "live queue sorts running before queued");
            await Capture(page, "admin-autoscan-activity-scans-1440.png");
            Invoke(Find<Button>(page, "AutoscanActivityNext")); await Until(() => page.RenderedIds.FirstOrDefault() == "scan-two", "bounded next scan page");
            Invoke(Find<Button>(page, "AutoscanActivityPolls")); await Until(() => TextShown(page, "Webhook · Download"), "poll tab and webhook label");
            var details = Find<Expander>(page, "AutoscanActivityDetails_11"); details.IsExpanded = true;
            await Until(() => TextShown(page, "future_outcome") && TextShown(page, "future_reason"), "forward-compatible change details");
            Check(TextShown(page, "Rewritten to /synthetic/movie.mkv"), "rewritten path is visible");
            Check(TextShown(page, "Showing the first 2 of 2 changes."), "truncated path receipt is visible");
            await Capture(page, "admin-autoscan-activity-polls-details-1440.png");
            Invoke(Find<Button>(page, "AutoscanActivityNext"));
            await Until(() => page.Controller!.State.Polls.PageIndex == 1 && !page.Controller.State.Polls.Fetching, "independent next poll cursor");
            Invoke(Find<Button>(page, "AutoscanActivityScans")); await Until(() => page.RenderedIds.FirstOrDefault() == "scan-two", "Scans keeps its own cursor chain");
            Invoke(Find<Button>(page, "AutoscanActivityRelatedPoll_scan-two"));
            await Until(() => page.Controller!.State.Polls.Filter.Query == "scan-two" && !page.Controller.State.Polls.Fetching, "related poll searches by the linked scan identity");
            Check(page.Controller!.State.Polls.Filter.SourceId == "source-1" && page.Controller.State.Polls.PageIndex == 0, "related poll keeps the official source filter and restarts only Polls");
            Check(wire.Reads.Last(path => path.StartsWith("/api/v2/admin/autoscan/events")).Contains("q=scan-two"), "related link does not invent event-id search support");
            Invoke(Find<Button>(page, "AutoscanActivityScans")); await Until(() => page.RenderedIds.FirstOrDefault() == "scan-two", "related poll navigation retains Scans position");
            var search = Find<TextBox>(page, "AutoscanActivitySearch"); search.Text = "synthetic filter";
            await Until(() => page.Controller!.State.Scans.Filter.Query == "synthetic filter" && !page.Controller.State.Scans.Fetching, "debounced search");
            Check(page.Controller!.State.Scans.PageIndex == 0, "filter resets only its chain");
            wire.EmptyHistory = true; Invoke(Find<Button>(page, "AutoscanActivityRefresh")); await Until(() => page.RenderedContent == "empty", "filtered empty state");
            Check(TextShown(page, "No autoscan activity matches those filters."), "official filtered empty label");
            wire.EmptyHistory = false; wire.FailHistory = true; Invoke(Find<Button>(page, "AutoscanActivityRefresh"));
            await Until(() => page.RenderedContent == "error", "history failure"); wire.FailHistory = false;
            Invoke(Find<Button>(page, "AutoscanActivityRetry")); await Until(() => page.RenderedContent == "table", "visible read-only retry");
            wire.FailStatus = true; Invoke(Find<Button>(page, "AutoscanActivityRefresh"));
            await Until(() => page.Controller!.State.StatusFailure != null && TextShown(page, "Autoscan status could not be loaded. Retry to refresh live counts."), "independent live status failure");
            wire.FailStatus = false; Invoke(Find<Button>(page, "AutoscanActivityStatusRetry"));
            await Until(() => page.Controller!.State.StatusFailure == null && page.Controller.State.Status != null, "visible status retry recovers without a write");
            wire.UnknownCancel = true; channel.Snapshot(); await Task.Delay(80);
            Invoke(Find<Button>(page, "AutoscanActivityCancel_running")); await Until(() => page.Controller!.State.CancelReview.ContainsKey("7"), "unknown cancellation review");
            Check(wire.Writes == 1 && !Find<Button>(page, "AutoscanActivityCancel_running").IsEnabled, "ambiguous command is sent once and disabled");
            Invoke(Find<Button>(page, "AutoscanReconcile_7")); await Task.Delay(80); Check(wire.Writes == 1, "reconciliation never replays a write");
            channel.Snapshot(); wire.ActiveScans = 0; Invoke(Find<Button>(page, "AutoscanReconcile_7"));
            await Until(() => page.Controller!.State.CancelReview.Count == 0, "explicit authoritative reconciliation");
            foreach (var width in new[] { 1024, 800 })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 1000)); await Task.Delay(100);
                await Capture(page, "admin-autoscan-activity-scans-" + width + ".png");
            }
            Check(!Descendants(page).OfType<TextBlock>().Any(text => text.Text.StartsWith("web.", StringComparison.Ordinal)), "controls display resolved labels");
            wire.HoldHistory = new(TaskCreationOptions.RunContinuationsAsynchronously);
            outstanding = page.Controller!.RefreshAsync(); await Until(() => page.Controller.State.Scans.Fetching, "held read started");
            outer.Content = null;
            await pageUnloaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); await shellUnloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            wire.HoldHistory.TrySetResult(); await outstanding.WaitAsync(TimeSpan.FromSeconds(5));
            Check(page.Controller == null && page.RenderedIds.Count == 0 && channel.Leases == 0, "actual page/shell unload retires reads and realtime before restoring globals");
        }
        finally
        {
            wire.HoldHistory?.TrySetResult();
            if (outer != null) { outer.BackStack.Clear(); outer.ForwardStack.Clear(); outer.Content = null; }
            if (page is { IsLoaded: true }) await pageUnloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (shell is { IsLoaded: true }) await shellUnloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await outstanding.WaitAsync(TimeSpan.FromSeconds(5));
            window.Content = previousContent;
            servicesField.SetValue(null, previousServices); DocumentTitle.SetServerName(previousTitle);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Autoscan Activity: " + message);
        Program.Log("PASS: Autoscan Activity: " + message);
    }
    private static async Task Until(Func<bool> ready, string message)
    {
        for (var attempt = 0; attempt < 200; attempt++) { if (ready()) return; await Task.Delay(25); }
        throw new TimeoutException("Autoscan Activity: " + message);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private static ControlType Find<ControlType>(DependencyObject root, string id) where ControlType : FrameworkElement =>
        Descendants(root).OfType<ControlType>().Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static bool TextShown(DependencyObject root, string value) => Descendants(root).OfType<TextBlock>().Any(text => text.Text == value);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Capture(FrameworkElement page, string name)
    {
        page.UpdateLayout(); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(page);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
}
