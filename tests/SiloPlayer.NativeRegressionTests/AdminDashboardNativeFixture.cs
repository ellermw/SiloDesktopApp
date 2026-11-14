using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>
/// Admin Monitor Dashboard native state fixture. Synthetic admin, data and
/// layout only: no live server, account, user data or app-data cache (the
/// layout cache is in memory). Covers the default 15, all 28 widgets with
/// data, idle collapse, breakpoints with PNG captures at 1440x900, 1280x800,
/// 1024x768, 1023x768 and 800x900, customize chrome, the debounced
/// If-Match layout save, a 412 save-blocked banner, reset, and role-loss
/// retirement. Program.cs dispatch is lead-owned:
///   if (SILO_NATIVE_TEST_ONLY == "admin-dashboard") await AdminDashboardNativeFixture.RunAsync(_window);
/// Synthetic captures are not live visual parity evidence.
/// </summary>
internal static class AdminDashboardNativeFixture
{
    private static readonly List<string> Failures = [];

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + "dashboard " + label);
        if (!condition) Failures.Add(label);
    }

    private static async Task Until(Func<bool> condition, int milliseconds = 5000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 25) await Task.Delay(25);
    }

    private sealed class Realtime : IAdminRealtimeChannel
    {
        public int Active;
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged;
        public IDisposable Subscribe(params string[] channels)
        {
            Active += channels.Length;
            return new Release(() => Active -= channels.Length);
        }
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot)
        {
            snapshot = JsonDocument.Parse(channel == "scans"
                ? """[{"id":"scan-1","library_id":1,"mode":"library","status":"running","result":{"total_files":400,"files_processed":120}}]"""
                : "[]").RootElement.Clone();
            return channel == "scans";
        }
        public void PublishScans(JsonElement scans) => SnapshotReceived?.Invoke("scans", scans);
        public void Raise() { SnapshotReceived?.Invoke("noop", default); EventReceived?.Invoke("noop", "noop", default); StateChanged?.Invoke(WebSocketState.Open); }
        private sealed class Release(Action action) : IDisposable
        {
            private Action? _action = action;
            public void Dispose() { _action?.Invoke(); _action = null; }
        }
    }

    private sealed class Server : HttpMessageHandler
    {
        public int Sessions;
        public bool RejectSaves;
        public readonly List<string> Writes = [];
        public readonly List<string> Reads = [];
        public string LayoutETag = "\"L1\"";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method != HttpMethod.Get)
            {
                lock (Writes) Writes.Add($"{request.Method} {path} If-Match={string.Join(",", request.Headers.IfMatch)}");
                if (path == "/api/v2/admin/dashboard/layout" && request.Method == HttpMethod.Put)
                {
                    if (RejectSaves) return Task.FromResult(Json("""{"title":"Stale","status":412}""", HttpStatusCode.PreconditionFailed));
                    LayoutETag = "\"L" + (Writes.Count + 1) + "\"";
                    var saved = new HttpResponseMessage(HttpStatusCode.NoContent);
                    saved.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(LayoutETag);
                    return Task.FromResult(saved);
                }
                return Task.FromResult(new HttpResponseMessage(path.EndsWith("/layout") ? HttpStatusCode.NoContent : HttpStatusCode.OK)
                    { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
            }
            Reads.Add(request.RequestUri!.PathAndQuery);
            var response = Json(Body(path));
            if (path is "/api/v2/admin/dashboard/layout" or "/api/v2/admin/settings/effective")
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(path.EndsWith("layout") ? LayoutETag : "\"S1\"");
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private string Body(string path)
        {
            var now = DateTimeOffset.UtcNow;
            string Iso(DateTimeOffset value) => value.ToString("O");
            string Points(int count) => string.Join(",", Enumerable.Range(0, count).Select(i =>
                $$$$"""{"t":"{{{{Iso(now.AddMinutes(i - count + 1))}}}}","streams":{{{{i % 5}}}},"direct":1,"remux":0,"transcode":{{{{i % 3}}}},"egress_kbps":{{{{12_000 + i * 300}}}},"download_egress_kbps":{{{{i % 4 * 500}}}},"egress_by_source":{"api":4000,"node:2":{{{{6000 + i * 200}}}}}}"""));
            return path switch
            {
                "/api/v2/admin/dashboard/capabilities" => """{"allowed":true,"state":"available","timeseries":true,"playback_activity":true,"top_activity":true,"health":true,"watch_providers":true,"downloads_stats":true,"egress_totals":true,"egress_by_source":true,"api_clients":true,"log_level_list":true}""",
                "/api/v2/admin/dashboard/layout" => """{"layout":null,"updated_at":null}""",
                "/api/v2/admin/stats" => """{"total_items":1500,"total_files":4200,"total_users":12,"total_movies":900,"total_movie_files":950,"total_shows":120,"total_show_files":3250,"advisory_titles":700,"active_streams":3,"total_storage_bytes":5497558138880,"watch_providers":[{"provider":"trakt","display_name":"Trakt","registered":true,"connected_profiles":3,"exporting":true,"scrobbling":true,"imported_watched_24h":12,"exported_watched_24h":4,"open_scrobbles":1,"sync_errors_24h":1,"failed_exports":0,"last_sync_completed_at":"2026-11-14T11:00:00Z"}]}""",
                "/api/v2/admin/stats/timeseries" => $$$$"""{"resolution_seconds":60,"from":"{{{{Iso(now.AddMinutes(-59))}}}}","to":"{{{{Iso(now)}}}}","egress_bytes":5400000000,"download_egress_bytes":300000000,"egress_sources":[{"key":"api","kind":"api","name":"API","egress_bytes":1800000000},{"key":"node:2","kind":"node","name":"edge-2","node_id":"2","egress_bytes":3600000000}],"points":[{{{{Points(60)}}}}]}""",
                "/api/v2/admin/stats/playback-activity" => $$$$"""{"hours":24,"bucket_seconds":3600,"from":"{{{{Iso(now.AddHours(-24))}}}}","to":"{{{{Iso(now)}}}}","profiles_active_24h":5,"buckets":[{"hour":"{{{{Iso(now.AddHours(-3))}}}}","direct":4,"remux":2,"transcode":1},{"hour":"{{{{Iso(now.AddHours(-1))}}}}","direct":2,"remux":0,"transcode":3}],"reliability":{"sessions_started":12,"transcode_starts":4,"finalized_sessions":10,"completed_sessions":8,"completion_rate":0.8,"unique_profiles":5}}""",
                "/api/v2/admin/stats/top-activity" => """{"days":7,"limit":10,"titles":[{"media_item_id":"m1","title":"Synthetic Feature","media_type":"movie","plays":9,"total_seconds":30000},{"media_item_id":"m2","title":"Synthetic Series","media_type":"series","plays":4,"total_seconds":2000}],"profiles":[{"user_id":"7","username":"viewer","profile_id":"p1","profile_name":"Kids","plays":6,"total_seconds":12000}]}""",
                "/api/v2/admin/stats/downloads" => """{"users_with_downloads":2,"active_downloads":5,"total_bytes":8589934592,"downloads_started_24h":3,"downloads_completed_24h":2,"top_users":[{"user_id":"7","username":"viewer","downloads":4,"total_bytes":6442450944}]}""",
                "/api/v2/admin/stats/api-clients" => """{"hours":24,"total_requests":1200,"clients":[{"session_id":"login-1","user_id":"7","username":"viewer","device_name":"Synthetic TV","user_agent":"SyntheticAgent/1.0","requests":900,"refused":3,"peak_hour_requests":120,"session_active":true},{"session_id":"login-2","user_id":"8","username":"guest","device_name":"","user_agent":"curl/8","requests":300,"refused":0,"peak_hour_requests":40,"session_active":false}]}""",
                "/api/v2/admin/server/status" => $$$$"""{"started_at":"{{{{Iso(now.AddHours(-76))}}}}","restart_required":false,"restart_requested":false,"restart_mark_count":0,"health":{"postgres":{"configured":true,"ok":true,"latency_ms":0.42},"redis":{"configured":false},"errors_24h":2,"warnings_24h":5}}""",
                "/api/v2/admin/system/build" => """{"display":"v1.2.3","revision":"abc","dirty":false,"build_number":4321,"available":true}""",
                "/api/v2/admin/system/resources/capabilities" => """{"state":"available","allowed":true,"instance_attribution":true,"process_resources":true}""",
                "/api/v2/admin/system/resources" => $$$$"""{"available":true,"stale":false,"sampled_at":"{{{{Iso(now)}}}}","system":{"cpu_pct":37,"cores":8,"load1":1.25,"mem_used_mb":6144,"mem_total_mb":16384,"disks":[]},"gpu":[{"device":"gpu0","source":"nvml","video_busy_pct":22,"sessions":1}],"attribution":{"instance_id":"0123456789abcdef","sample_interval_seconds":5,"cpu":{"available":true,"scope":"host"},"memory":{"available":true,"scope":"host"},"network":{"available":true},"load":{"available":true},"process":{"resident_bytes":734003200,"heap_live_bytes":104857600}}}""",
                "/api/v2/admin/nodes" => $$$$"""{"items":[{"id":"2","name":"edge-2","type":"proxy","group":"prod-east","enabled":true,"healthy":true,"active_jobs":3,"max_jobs":10,"egress_kbps":6000,"max_bandwidth_kbps":100000,"drain_requested":false,"drain_state":"none","last_health_check":"{{{{Iso(now)}}}}"},{"id":"3","name":"gpu-transcoder-3","type":"transcode","group":"prod-east","enabled":true,"healthy":false,"active_jobs":0,"egress_kbps":0,"drain_requested":false,"drain_state":"none","last_health_check":"{{{{Iso(now.AddMinutes(-3))}}}}"}],"page":{"has_more":false}}""",
                "/api/v2/libraries" => """{"items":[{"id":"1","name":"Movies","type":"movies","enabled":true,"paths":["/media/movies"],"sort_order":0},{"id":"2","name":"Shows","type":"series","enabled":false,"paths":["/a","/b"],"sort_order":1}]}""",
                "/api/v2/admin/users" => $$$$"""{"items":[{"id":"1","username":"synthetic-admin","email":"admin@example.invalid","role":"admin","is_owner":true,"enabled":true,"last_active_at":"{{{{Iso(now.AddMinutes(-2))}}}}"},{"id":"7","username":"viewer","email":"viewer@example.invalid","role":"user","is_owner":false,"enabled":false,"last_active_at":null}],"page":{"has_more":false}}""",
                "/api/v2/admin/autoscan/status" => $$$$"""{"enabled":true,"running_scans":1,"accepted_scans":2,"active_scans":3,"latest_event_at":"{{{{Iso(now.AddMinutes(-7))}}}}","running_polls":[{}],"sources":[]}""",
                "/api/v2/admin/autoscan/scans" => $$$$"""{"items":[{"id":"h1","library_id":"1","mode":"library","trigger":"autoscan","status":"completed","started_at":"{{{{Iso(now.AddMinutes(-62))}}}}","completed_at":"{{{{Iso(now.AddMinutes(-60))}}}}"},{"id":"h2","library_id":"2","mode":"file","trigger":"manual","status":"failed","started_at":"{{{{Iso(now.AddMinutes(-5))}}}}","completed_at":"{{{{Iso(now.AddMinutes(-4))}}}}"}],"page":{"has_more":false},"total":2}""",
                "/api/v2/admin/logs/app" => $$$$"""{"items":[{"id":"1","timestamp":"{{{{Iso(now.AddMinutes(-1))}}}}","level":"error","component":"scan","message":"Synthetic error line"},{"id":"2","timestamp":"{{{{Iso(now.AddMinutes(-3))}}}}","level":"warn","component":"","message":"Synthetic warning line"}],"page":{"has_more":false}}""",
                "/api/v2/admin/sessions" => "{\"items\":[" + string.Join(",", Enumerable.Range(0, Sessions).Select(Session)) + "],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/settings/effective" => """{"playback.transcode_enabled":"true","email.enabled":"false","server.public_url":"","ui.dashboard.hidden_attention":"[\"public-url\"]"}""",
                "/api/v2/admin/settings/sensitive-status" => """{"configured":[]}""",
                "/api/v2/theme/branding" => """{"server_name":"Synthetic","storage_available":true}""",
                _ => "{\"items\":[],\"page\":{\"has_more\":false}}",
            };
        }

        private static string Session(int i) =>
            "{\"session_id\":\"synthetic-" + i + "\",\"user_id\":\"" + (7 + i) + "\",\"username\":\"viewer" + i + "\",\"profile_id\":\"p" + i + "\",\"profile_name\":\"Profile " + i + "\"," +
            "\"media_file_id\":\"" + (40 + i) + "\",\"requested_media_file_id\":\"" + (40 + i) + "\",\"media_title\":\"Synthetic Title " + i + "\",\"media_type\":\"movie\"," +
            "\"play_method\":\"" + (i == 0 ? "direct" : "transcode") + "\",\"video_decision\":\"" + (i == 0 ? "direct" : "transcode") + "\",\"audio_decision\":\"direct\"," +
            "\"reporting_node\":\"local\",\"file_duration\":7200,\"started_at\":\"" + DateTimeOffset.UtcNow.AddMinutes(-5 - i).ToString("O") + "\",\"updated_at\":\"2026-11-14T10:00:00Z\",\"position_seconds\":600," +
            "\"is_paused\":" + (i == 1 ? "true" : "false") + ",\"has_playback_control\":true,\"stream_location\":\"local\",\"audio_track_index\":0,\"transcode_audio\":false," +
            "\"stream_bitrate_kbps\":8000,\"target_bitrate_kbps\":null,\"source_bitrate_kbps\":null,\"source_audio_channels\":6,\"client_label\":\"Synthetic Client 1.0\"," +
            "\"routing_workload\":\"video_transcode\",\"routing_execution\":\"" + (i == 0 ? "api" : "transcode") + "\",\"routing_execution_node_name\":\"gpu-1\",\"routing_egress\":\"api\",\"routing_network_provider\":\"\"}";
    }

    internal static async Task RunAsync(Window window)
    {
        Failures.Clear();
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        var realtime = new Realtime();
        using var authority = new AdminMonitorAuthority(client, auth);
        var api = new AdminMonitorApi(client);
        using var store = new ServerActivityStore(api, authority, realtime);
        var cache = new DashboardMemoryLayoutCache();
        await using var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(api).AddSingleton(store)
            .AddSingleton(new ToastService()).AddSingleton(new SiloPlayer.Helpers.NavigationService()).AddSingleton(new SettingsService(Program.ResultDirectory)).AddSingleton<IDashboardLayoutCache>(cache)
            .BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        field.SetValue(null, services);
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        try
        {
            await DashboardAsync(window, server, auth, realtime);
        }
        finally
        {
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException($"Admin Dashboard fixture failed: {string.Join("; ", Failures)}");
    }

    private static async Task DashboardAsync(Window window, Server server, AuthService auth, Realtime realtime)
    {
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
        var frame = new Frame();
        window.Content = frame;
        frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Dashboard));
        var innerFrame = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(frame.Content)!;
        var page = (AdminDashboardPage)innerFrame.Content;
        page.SetHostActivity(visible: true, focused: true);
        await Until(() => page.LayoutSync?.IsLoaded == true && page.Store?.HasDashboardData == true);
        realtime.TryGetLatestSnapshot("scans", out var freshScans);
        realtime.PublishScans(freshScans);
        await Task.Delay(300);
        Check(page.LayoutSync?.IsLoaded == true, "server layout read settled");
        Check(page.PlacedWidgets().Count == 15, "default layout places the 15 default widgets");
        Check(page.GridMode == DashboardGridMode.Twelve, "1440px uses twelve columns");
        var nowPlaying = page.PlacedWidgets().Single(w => w.Id == "now-playing");
        Check(Math.Abs(nowPlaying.Height - 100) < 1, $"idle Now Playing collapses to one row (h={nowPlaying.Height:0})");
        var health = page.PlacedWidgets().Single(w => w.Id == "health-strip");
        var tile = page.PlacedWidgets().Single(w => w.Id == "stat-movies");
        Check(Math.Abs(health.Height - 100) < 1 && Math.Abs(tile.Width * 6 + 5 * 14 - health.Width) < 2, "spans and rows follow the 12-column 100px/14px grid");
        Check(page.IsAttentionShown, "setup attention notice shows unhidden items");
        await CaptureAsync(frame, "admin-dashboard-idle-1440x900.png");

        // Every Add-only widget renders with typed data, not placeholders.
        foreach (var widget in page.LayoutSync!.HiddenWidgets.ToList()) page.LayoutSync.AddWidget(widget.Id);
        server.Sessions = 3;
        await (page.Store?.RefreshDashboardAsync(manual: true) ?? Task.CompletedTask);
        await Until(() => page.Store?.Get(DashboardDataStore.Sessions).As<IReadOnlyList<SiloPlayer.Core.Models.Admin.DashboardSession>>()?.Count == 3);
        await Task.Delay(800);
        var headingTop = ((FrameworkElement)typeof(AdminDashboardPage).GetField("_header", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!).TransformToVisual(frame).TransformPoint(new Windows.Foundation.Point()).Y;
        Check(headingTop >= 60 && headingTop < 100, "desktop header uses the WebUI top gutter without decorative layout space");
        Check(page.PlacedWidgets().Count == 28, "all 28 widgets can be placed");
        foreach (var key in new[]
                 {
                     DashboardDataStore.Stats, DashboardDataStore.Timeseries(24), DashboardDataStore.Timeseries(1), DashboardDataStore.Playback(24),
                     DashboardDataStore.TopActivity(7), DashboardDataStore.Downloads, DashboardDataStore.ApiClients(24), DashboardDataStore.Status,
                     DashboardDataStore.Resources, DashboardDataStore.Nodes, DashboardDataStore.Autoscan, DashboardDataStore.ScanHistory,
                     DashboardDataStore.RecentErrors, DashboardDataStore.Libraries, DashboardDataStore.Users,
                 })
            Check(page.Store?.Get(key).IsSuccess == true, $"{key} loaded for its mounted widget");
        Check(page.Store?.ActiveScans.Count == 1, "scanner/libraries read the shared scans channel");
        var apiView = Descendants(page).First(element => element.GetType().Name == "ApiClientsWidget");
        Button ClientButton() => Descendants(apiView).OfType<Button>().First(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "Synthetic TV");
        void Click(Button button) => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        Click(ClientButton());
        await Until(() => server.Reads.Any(path => path.Contains("/login-1/endpoints?hours=24")));
        Check(server.Reads.Any(path => path.Contains("/login-1/endpoints?hours=24")), "opening client detail fetches endpoints");
        apiView.GetType().GetMethod("SetRange")!.Invoke(apiView, [DashboardWidgetRange.Hour]);
        await Until(() => server.Reads.Any(path => path.Contains("/login-1/endpoints?hours=1")));
        Check(server.Reads.Any(path => path.Contains("/login-1/endpoints?hours=1")), "open detail remounts endpoints at the new range");
        await Until(() => Descendants(apiView).OfType<Button>().Any(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == "Synthetic TV"));
        Click(ClientButton());
        await Task.Delay(100);
        var detailReads = server.Reads.Count(path => path.Contains("/endpoints?"));
        page.Store!.Invalidate("api-clients:");
        await Task.Delay(100);
        Check(server.Reads.Count(path => path.Contains("/endpoints?")) == detailReads, "closed client details release every endpoint mount");
        nowPlaying = page.PlacedWidgets().Single(w => w.Id == "now-playing");
        Check(nowPlaying.Height > 300, "active Now Playing returns to its placed rows");

        foreach (var (width, height) in new[] { (1440, 900), (1280, 800), (1024, 768), (1023, 768), (800, 900) })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height));
            page.ViewportWidthOverride = width;
            await Task.Delay(400);
            var expected = DashboardGridPlacement.ModeFor(width);
            Check(page.GridMode == expected, $"{width}px uses {expected}");
            await CaptureAsync(frame, $"admin-dashboard-{width}x{height}.png");
        }
        page.ViewportWidthOverride = 1440;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
        await Task.Delay(300);

        // Customize: chrome, keyboard-equivalent edits and a debounced guarded save.
        page.ToggleCustomize();
        Check(page.LayoutSync.IsCustomizing, "Customize enters customize mode");
        page.OpenAddSheet();
        Check(page.IsSheetOpen, "Add widget sheet opens in customize mode");
        await CaptureAsync(frame, "admin-dashboard-customize-1440x900.png");
        page.LayoutSync.MoveWidget("users", "stat-active-streams");
        page.LayoutSync.ResizeWidget("libraries", 12, 6);
        page.LayoutSync.SetWidgetRange("egress-24h", DashboardWidgetRange.Week);
        await Task.Delay(1200);
        string[] puts;
        lock (server.Writes) puts = server.Writes.Where(w => w.StartsWith("PUT /api/v2/admin/dashboard/layout", StringComparison.Ordinal)).ToArray();
        Check(puts.Length >= 1 && puts[^1].Contains("If-Match=\"L"), "edits debounce into a validator-guarded layout save");
        Check(page.PlacedWidgets()[0].Id == "users", "move reorders without recreating widgets");

        server.RejectSaves = true;
        page.LayoutSync.RemoveWidget("api-clients");
        await Task.Delay(1200);
        Check(page.IsSaveBlockedShown, "a 412 stops server saves and shows the reload banner");
        Check(page.PlacedWidgets().All(w => w.Id != "api-clients"), "local edits are retained after a refused save");
        server.RejectSaves = false;
        await page.LayoutSync.ReloadServerLayoutAsync();
        await Task.Delay(200);
        Check(!page.IsSaveBlockedShown, "explicit reload resumes saving");
        page.LayoutSync.ResetLayout();
        await Task.Delay(300);
        Check(page.PlacedWidgets().Count == 15, "reset restores the default arrangement");
        lock (server.Writes) Check(server.Writes.Any(w => w.StartsWith("DELETE /api/v2/admin/dashboard/layout", StringComparison.Ordinal)), "reset deletes the server layout");
        page.ToggleCustomize();

        // Hold an OS add-drop read across authority loss/regain. It cannot edit the replacement layout.
        if (!page.LayoutSync.IsCustomizing) page.ToggleCustomize();
        var gridHost = page.GetType().GetField("_gridHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        gridHost.GetType().GetProperty("AddDragId")!.SetValue(gridHost, "api-clients");
        var dropData = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateDrop = (Task)gridHost.GetType().GetMethod("CompleteAddDropAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(gridHost, [new Func<Task<string?>>(() => dropData.Task), null])!;

        // Role loss retires everything; nothing from the old authority remains or is sent.
        int writesBefore;
        lock (server.Writes) writesBefore = server.Writes.Count;
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
        await Task.Delay(300);
        Check(page.Store == null && page.PlacedWidgets().Count == 0, "role loss clears the dashboard");
        await Task.Delay(1200);
        lock (server.Writes) Check(server.Writes.Count == writesBefore, "no write follows role loss");
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        await Until(() => page.Store?.HasDashboardData == true);
        Check(page.Store != null && page.PlacedWidgets().Count == 15, "regained admin starts a fresh authority set");
        page.ToggleCustomize();
        dropData.SetResult("api-clients");
        await lateDrop;
        Check(page.PlacedWidgets().Count == 15, "held old-authority drop cannot add to the replacement layout");
        window.Content = null;
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        await Task.Delay(150);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, $"{name} rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96,
            (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }
}
