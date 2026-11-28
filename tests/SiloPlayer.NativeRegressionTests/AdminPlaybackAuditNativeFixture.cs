using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>Playback History and Skip Markers through the registered shell and a synthetic server.</summary>
internal static class AdminPlaybackAuditNativeFixture
{
    private static readonly List<string> Failures = [];

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }

    private static async Task<bool> Until(Func<bool> condition, int milliseconds = 5000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 25) await Task.Delay(25);
        return condition();
    }

    private sealed class Realtime : IAdminRealtimeChannel
    {
        public WebSocketState CurrentState { get; set; } = WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged;
        public IDisposable Subscribe(params string[] channels) => new Release();
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot)
        {
            snapshot = JsonDocument.Parse("[]").RootElement.Clone();
            return channel == "scans";
        }
        private sealed class Release : IDisposable { public void Dispose() { } }
        internal void Silence() { SnapshotReceived?.Invoke("", default); EventReceived?.Invoke("", "", default); StateChanged?.Invoke(CurrentState); }
    }

    /// <summary>Synthetic backend with server-side filters, cursors and detail replies.</summary>
    private sealed class Server : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<string> Requests { get; } = [];
        public bool Healed { get; set; }
        public bool ChangeTitle { get; set; }
        internal readonly DateTimeOffset HistoryNow = DateTimeOffset.UtcNow;

        public int Count(string prefix) { lock (_gate) return Requests.Count(path => path.StartsWith(prefix, StringComparison.Ordinal)); }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            lock (_gate) Requests.Add(path);
            var status = HttpStatusCode.OK;
            string body;
            const string historyPath = "/api/v2/admin/playback-history";
            if (request.RequestUri.AbsolutePath == historyPath)
            {
                if (path.Contains("media_item_id=broken", StringComparison.Ordinal) && !Healed)
                {
                    status = HttpStatusCode.InternalServerError;
                    body = "{\"type\":\"internal_error\",\"title\":\"Unable to list playback history.\",\"status\":500}";
                }
                else if (Healed && path.Contains("media_item_id=broken", StringComparison.Ordinal))
                    body = History(new AdminRouteRequest(AdminRoute.PlaybackHistory));
                else
                {
                    AdminRouteCatalog.TryResolve("/admin/history" + request.RequestUri.Query, out var resolved);
                    body = History(resolved.Request!);
                }
            }
            else if (path == historyPath + "/capabilities") body = """
                {"allowed":true,"available":true,"client_identity":true,"client_ip":true,"device_identity":true,"end_reason":true,
                "end_reason_values":["stopped"],"ended_before":true,"lifecycle_seq":true,"revision":"fixture","rich_filters":true,
                "route_facts":true,"series_filter":true,"session_detail":true,"state":"available","stream_decisions":true,"watch_measurement":true}
                """;
            else if (path == historyPath + "/session-0055abcd") body = HistoryEntry(55);
            else if (path == historyPath + "/failed-session")
            {
                status = HttpStatusCode.InternalServerError;
                body = "{\"title\":\"Synthetic detail failure\",\"status\":500}";
            }
            else if (path.StartsWith(historyPath + "/", StringComparison.Ordinal))
            {
                status = HttpStatusCode.NotFound;
                body = "{}";
            }
            else body = request.RequestUri.AbsolutePath switch
            {
                "/api/v2/admin/users" => "{\"items\":[{\"id\":\"7\",\"username\":\"synthetic-viewer\"},{\"id\":\"8\",\"username\":\"synthetic-guest\"}],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/users/7/profiles" => "{\"items\":[{\"id\":\"p1\",\"name\":\"Living room\"},{\"id\":\"p2\",\"name\":\"Kids\"}],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/markers/history" => Markers(),
                "/api/v2/admin/sessions" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/diagnostics/reports" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/tasks" => "{\"items\":[]}",
                "/api/v2/libraries" => "{\"items\":[{\"id\":\"3\",\"name\":\"Synthetic films\"},{\"id\":\"4\",\"name\":\"Synthetic series\"}]}",
                "/api/v2/admin/downloads/preparations" => "{\"counts\":{\"running\":0,\"queued\":0,\"retrying\":0,\"paused\":0,\"failed_recent\":0},\"items\":[]}",
                "/api/v2/admin/system/build" => "{\"display\":\"v1.2.3\",\"revision\":\"abc\",\"dirty\":false,\"build_number\":4321,\"available\":true}",
                "/api/v2/admin/server/status" => "{\"restart_required\":false,\"restart_mark_count\":0,\"restart_requested\":false}",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }

        private string History(AdminRouteRequest request)
        {
            var limit = int.TryParse(request.Get("limit"), out var size) ? size : 25;
            var cursor = request.Get("cursor");
            var offset = cursor != null && int.TryParse(cursor["page-".Length..], out var start) ? start : 0;
            var matches = Enumerable.Range(0, 60).Where(Matches).ToArray();
            var rows = matches.Skip(offset).Take(limit).Select(HistoryEntry);
            var next = offset + limit < matches.Length ? $",\"next_cursor\":\"page-{offset + limit}\"" : "";
            return "{\"items\":[" + string.Join(",", rows) + "],\"page\":{\"has_more\":"
                + (next.Length > 0 ? "true" : "false") + next + "}}";

            bool Matches(int index)
            {
                var transcode = index % 4 == 0;
                var episode = index % 3 == 0;
                var started = HistoryNow.AddMinutes(-42 - 15 * index);
                var search = request.Get("search");
                return Equal("user_id", index % 2 == 0 ? "7" : "8")
                    && Equal("profile_id", ProfileId(index))
                    && Equal("media_item_id", "item-" + index)
                    && Equal("series_id", episode ? "series-1" : "")
                    && Equal("library_id", episode ? "4" : "3")
                    && Equal("play_method", transcode ? "transcode" : "direct")
                    && Equal("completed", index % 3 != 1 ? "true" : "false")
                    && Equal("stream_location", transcode ? "remote" : "local")
                    && Equal("subtitle_burn_in", transcode ? "true" : "false")
                    && Equal("client_name", transcode ? "Jellyfin Web" : "Silo Desktop")
                    && Equal("device_platform", transcode ? "Web" : "Windows")
                    && Equal("source_video_codec", transcode ? "hevc" : "h264")
                    && Equal("adaptation_reason", transcode ? "subtitle_burn_in" : "")
                    && (!DateTimeOffset.TryParse(request.Get("started_after"), out var after) || started >= after)
                    && (!DateTimeOffset.TryParse(request.Get("started_before"), out var before) || started <= before)
                    && (search == null || $"Title {index} session-{index:D4}abcd".Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            bool Equal(string key, string value) => request.Get(key) is not { } expected || expected == value;
        }

        private static string ProfileId(int index) => index == 0 ? "retired-profile" : "p" + (index % 2 + 1);

        private string HistoryEntry(int index)
        {
            var episode = index % 3 == 0;
            var transcode = index % 4 == 0;
            var ended = HistoryNow.AddMinutes(-15 * index);
            var started = ended.AddMinutes(-42);
            var routeFacts = transcode ? """
                "video_decision":"transcode","audio_decision":"transcode","output_protocol":"hls","output_container":"fmp4",
                "target_video_codec":"h264","target_resolution":"1080p","target_audio_codec":"aac","target_audio_channels":2,
                "target_bitrate_kbps":8000,"tone_map_mode":"hardware","transcode_hw_accel":"vaapi","source_video_range_type":"HDR10",
                "subtitle":{"language":"en","codec":"hdmv_pgs_subtitle","delivery":"burn_in"},"is_jellyfin_client":true,
                "adaptation_reasons":["video_codec_unsupported","subtitle_burn_in"],"routing_egress":"proxy","routing_egress_node_id":"3","routing_egress_node_name":"edge-eu-1",
                """ : "";
            return $$"""
                {"session_id":"session-{{index:D4}}abcd","user_id":"{{(index % 2 == 0 ? 7 : 8)}}","username":"{{(index % 5 == 4 ? "" : index % 2 == 0 ? "silo-viewer" : "silo-guest")}}",
                "profile_id":"{{ProfileId(index)}}","profile_name":"{{(index == 0 ? "Retired profile" : index % 2 == 0 ? "Living room" : "")}}",
                "media_item_id":"{{(index % 7 == 6 ? "" : "item-" + index)}}","media_file_id":"{{index + 100}}",
                "media_title":"{{(ChangeTitle && index == 1 ? "Changed title" : "Title " + index)}}","media_type":"{{(episode ? "episode" : "movie")}}",
                "play_method":"{{(transcode ? "transcode" : "direct")}}","started_at":"{{started:O}}","ended_at":"{{ended:O}}",
                "end_position_seconds":2520,"duration_seconds":{{(index % 9 == 8 ? "null" : "5400")}},
                "completed":{{(index % 3 != 1 ? "true" : "false")}},"played_seconds":{{(index % 6 == 5 ? "null" : "2400")}},
                "series_title":"{{(episode ? "Synthetic Series" : "")}}","season_number":{{(episode ? "1" : "null")}},"episode_number":{{(episode ? (index % 10 + 1).ToString() : "null")}},
                "library_id":"{{(episode ? "4" : "3")}}","library_name":"{{(episode ? "Synthetic series" : "Synthetic films")}}",
                "client_name":"{{(transcode ? "Jellyfin Web" : "Silo Desktop")}}","client_version":"1.2","device_platform":"{{(transcode ? "Web" : "Windows")}}",
                "stream_location":"{{(transcode ? "remote" : "local")}}","source_video_codec":"{{(transcode ? "hevc" : "h264")}}",
                "source_resolution":"{{(transcode ? "2160p" : "1080p")}}","source_audio_codec":"{{(transcode ? "truehd" : "aac")}}",
                "source_audio_channels":{{(transcode ? 8 : 2)}},"source_container":"mkv","source_bitrate_kbps":42000,
                "route_facts_recorded":{{(index == 1 ? "false" : "true")}},{{routeFacts}}"reporting_node":"api-1",
                "measured":true,"client_ip":"192.0.2.10","paused_seconds":120,"end_reason":"{{(transcode ? "terminated" : "stopped")}}"}
                """;
        }

        private static string Markers()
        {
            var created = DateTimeOffset.UtcNow.AddHours(-3).ToString("O");
            return $$"""
                {"history":[
                {"id":"1","media_file_id":"100","item_id":"item-1","item_type":"episode","media_title":"Synthetic Series S01E02","file_path":"/media/series/S01E02.mkv","segment":"intro","action":"set",
                 "before":{"start_seconds":12,"end_seconds":70},"after":{"start_seconds":15,"end_seconds":88},"user_id":"7","username":"synthetic-viewer","request_id":"req-synthetic-1","client_ip":"192.0.2.10","created_at":"{{created}}"},
                {"id":"2","media_file_id":"101","segment":"credits","action":"clear","before":{"start_seconds":2500,"end_seconds":2610},"user_id":"8","username":"synthetic-guest","impersonator_username":"synthetic-admin","created_at":"{{created}}"},
                {"id":"3","media_file_id":"102","file_path":"/media/movies/Example.mkv","segment":"recap","action":"set","after":{"start_seconds":0,"end_seconds":45},"user_agent":"ExampleAgent/1","created_at":"{{created}}"},
                {"id":"4","media_file_id":"103","media_title":"Future kind","segment":"commercial","action":"set","after":{"start_seconds":600,"end_seconds":660},"created_at":"not-a-time"}
                ]}
                """;
        }
    }

    internal static async Task RunAsync(Window window)
    {
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var api = new AdminMonitorApi(client);
        using var store = new ServerActivityStore(api, authority, new Realtime());
        var navigation = new NavigationService();
        var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(api).AddSingleton(store)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddSingleton<AdminLibrariesApi>().AddAdminPlaybackAudit()
            .AddSingleton(new AdminPageRegistration(AdminRoute.Users, typeof(Page)));
        await using var provider = services.BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var previousContent = window.Content;
        field.SetValue(null, provider);
        try
        {
            var outer = new Frame();
            window.Content = outer;
            navigation.Frame = outer;
            await ResizeAsync(window, 1440, 900);

            // Registration: both routes open through the real shell and appear in the sidebar.
            Check(AdminShellPage.CanOpen(new AdminRouteRequest(AdminRoute.PlaybackHistory)), "Playback History is registered");
            Check(AdminShellPage.CanOpen(new AdminRouteRequest(AdminRoute.SkipMarkers)), "Skip Markers is registered");
            Check(!AdminShellPage.CanOpen(new AdminRouteRequest(AdminRoute.SkipMarkers) { Segments = ["1"] }), "Skip Markers owns no descendant path");
            Check(AdminShellPage.Open(new AdminRouteRequest(AdminRoute.PlaybackHistory)), "Playback History enters Admin");
            await Until(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)Field(shell, "_frame")!;
            await Until(() => inner.Content is AdminPlaybackHistoryPage page && page.IsLoaded);
            var history = (AdminPlaybackHistoryPage)inner.Content;
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "page frame swaps without a transition");
            var sidebar = (Grid)Field(shell, "_desktopSidebarHost")!;
            await Until(() => Descendants(sidebar).OfType<Button>().Any(button => AutomationProperties.GetName(button) == AdminNavigation.SidebarLabel(AdminRoute.PlaybackHistory)));
            Check(Descendants(sidebar).OfType<Button>().Any(button => AutomationProperties.GetName(button) == AdminNavigation.SidebarLabel(AdminRoute.PlaybackHistory)),
                "sidebar shows the History leaf");
            Check(Descendants(sidebar).OfType<Button>().Any(button => AutomationProperties.GetName(button) == AdminNavigation.SidebarLabel(AdminRoute.SkipMarkers)),
                "sidebar shows the Skip Markers leaf");

            // Rendered table, current-page metrics and cursor controls.
            Check(await Until(() => TextShown(history, "Synthetic Series") && TextShown(history, "Title 1")), "history rows render as controls");
            Check(history.RenderedContent == "table" && history.RenderedSessionIds.Count == 25, "page one shows the 25 server rows");
            Check(server.Count("/api/v2/admin/playback-history?limit=25") == 1 && !server.Requests.Any(path => path.Contains("cursor=", StringComparison.Ordinal)),
                "the first read sends limit 25 without a cursor");
            Check(TextShown(history, "Showing 25 rows") && TextShown(history, "Account") && TextShown(history, "Actions") &&
                TextShown(history, "Client") && TextShown(history, "JF"), "official pager, columns and Jellyfin badge render");
            Check(TextShown(Find<FrameworkElement>(history, "AdminHistoryStatVisible")!, "25") &&
                TextShown(Find<FrameworkElement>(history, "AdminHistoryStatCompleted")!, "17") &&
                TextShown(Find<FrameworkElement>(history, "AdminHistoryStatPartial")!, "8"), "KPIs count the current page");
            var profile = Find<ComboBox>(history, "AdminHistoryProfile")!;
            Check(!profile.IsEnabled && ((ComboBoxItem)profile.SelectedItem).Content as string == "Choose an account first",
                "profile picker is disabled with its hint");
            var results = (StackPanel)Field(history, "_content")!;
            var tableScroll = Descendants(results).OfType<ScrollViewer>().First();
            Check(await Until(() => tableScroll.ViewportWidth > 0 && tableScroll.ScrollableWidth <= 1), "the wide table fits all columns");
            var watchHeading = Find<TextBlock>(history, null, block => block.Text == "Watch time")!;
            var untrimmed = new TextBlock { Text = watchHeading.Text, FontFamily = watchHeading.FontFamily,
                FontSize = watchHeading.FontSize, FontWeight = watchHeading.FontWeight };
            untrimmed.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            Check(watchHeading.ActualWidth + 1 >= untrimmed.DesiredSize.Width, "the Watch time heading fits without truncation");
            CheckLogActionBounds(history);
            await CaptureAsync(window, "admin-playback-history-rows-1440.png");
            await ResizeAsync(window, 560, 1040);
            await CaptureAsync(window, "admin-playback-history-rows-560.png");
            Check(tableScroll.ScrollableWidth > 0, "the narrow table has its own horizontal scroll");
            tableScroll.ChangeView(tableScroll.ScrollableWidth, null, null, true);
            Check(await Until(() => tableScroll.HorizontalOffset > 0), "the narrow table can scroll to its action column");
            CheckLogActionBounds(history, tableScroll);
            await CaptureAsync(window, "admin-playback-history-rows-actions-560.png");
            await ResizeAsync(window, 1440, 900);

            var next = Find<Button>(history, "AdminHistoryNext")!;
            var previousPage = Find<Button>(history, "AdminHistoryPrevious")!;
            Invoke(next);
            Check(await Until(() => history.RenderedSessionIds.FirstOrDefault() == "session-0025abcd"), "Next reads the second server page");
            Check(server.Count("/api/v2/admin/playback-history?limit=25&cursor=page-25") == 1 && history.Controller?.IsPolling == false,
                "page two sends its cursor and stops polling");
            Invoke(previousPage);
            Check(await Until(() => history.RenderedSessionIds.FirstOrDefault() == "session-0000abcd"), "Previous restores the first page");
            Invoke(next);
            Check(await Until(() => history.RenderedSessionIds.FirstOrDefault() == "session-0025abcd"), "Next again uses the first page continuation");
            Invoke(next);
            Check(await Until(() => history.RenderedSessionIds.FirstOrDefault() == "session-0050abcd"), "Next reads the third server page");
            Check(!next.IsEnabled && TextShown(history, "Showing 10 rows") &&
                server.Count("/api/v2/admin/playback-history?limit=25&cursor=page-50") == 1, "last page shows its row count and disables Next");
            Invoke(Find<Button>(history, "AdminHistoryFirst")!);
            Check(await Until(() => history.RenderedSessionIds.FirstOrDefault() == "session-0000abcd"), "First page clears the cursor stack");
            Check(ReferenceEquals(next, Find<Button>(history, "AdminHistoryNext")), "pager controls stay mounted across pages");
            var pageSize = Find<ComboBox>(history, "AdminHistoryPageSize")!;
            SelectTag(pageSize, "50");
            Check(await Until(() => history.RenderedSessionIds.Count == 50), "50 rows per page sends a new first-page request");
            Check(server.Count("/api/v2/admin/playback-history?limit=50") == 1, "page size is sent as the server limit");

            var user = Find<ComboBox>(history, "AdminHistoryUser")!;
            Check(await Until(() => user.Items.Count == 3), "account picker lists accounts");
            SelectTag(user, "7");
            Check(await Until(() => shell.CurrentRequest.Get("user_id") == "7" && profile.IsEnabled && profile.Items.Count == 3),
                "choosing an account loads and enables profiles");
            Check(await FilterSettled("user_id", "7", row => row.UserId == "7"), "account filters History");
            SelectTag(profile, "p1");
            Check(await FilterSettled("profile_id", "p1", row => row.ProfileId == "p1"), "profile filters History");
            var library = Find<ComboBox>(history, "AdminHistoryLibrary")!;
            Check(await Until(() => library.Items.Count == 3), "library picker lists libraries");
            SelectTag(library, "4");
            Check(await FilterSettled("library_id", "4", row => row.LibraryId == "4"), "library filters History");
            SelectTag(Find<ComboBox>(history, "AdminHistoryMethod")!, "transcode");
            Check(await FilterSettled("play_method", "transcode", row => row.PlayMethod == "transcode"), "method filters History");
            SelectTag(Find<ComboBox>(history, "AdminHistoryCompletion")!, "true");
            Check(await FilterSettled("completed", "true", row => row.Completed), "status filters History");
            SelectTag(Find<ComboBox>(history, "AdminHistoryNetwork")!, "remote");
            Check(await FilterSettled("stream_location", "remote", row => row.StreamLocation == "remote"), "network filters History");
            var burnIn = Find<ToggleButton>(history, "AdminHistoryBurnIn")!;
            ((IToggleProvider)new ToggleButtonAutomationPeer(burnIn).GetPattern(PatternInterface.Toggle)!).Toggle();
            Check(await FilterSettled("subtitle_burn_in", "true", row => row.Subtitle?.Delivery == "burn_in"), "subtitle burn-in filters History");
            var search = Find<TextBox>(history, "AdminHistorySearch")!;
            search.Text = "Title 1";
            search.Text = " Title 12 ";
            Check(shell.CurrentRequest.Get("search") == null, "typing does not synchronously change the route");
            Check(await FilterSettled("search", "Title 12", row => row.MediaTitle == "Title 12"), "debounced search commits the trimmed draft");
            Check(!server.Requests.Any(path => path.EndsWith("search=Title%201", StringComparison.Ordinal)), "the intermediate search draft was never sent");
            Check(!inner.CanGoBack && ReferenceEquals(inner.Content, history), "filter changes replace the address in place");
            Invoke(Find<Button>(history, "AdminHistoryReset")!);
            Check(await Until(() => shell.CurrentRequest.Query == null && history.RenderedSessionIds.Count == 50 && search.Text == ""),
                "Reset clears the route and search and retains page size");
            SelectTag(pageSize, "25");
            Check(await Until(() => history.RenderedSessionIds.Count == 25), "25 rows can be restored");

            // Session details use the row already on the page.
            Invoke(Find<Button>(history, "AdminHistoryDetails_session-0000abcd")!);
            var root = (FrameworkElement)window.Content;
            Check(await Until(() => shell.CurrentRequest.Get("session") == "session-0000abcd" && TextShown(root, "Burned in")),
                "Details opens the transcode sheet at its session route");
            Check(TextShown(root, "SOURCE VS. DELIVERED") && TextShown(root, "HEVC · 2160p") && TextShown(root, "H.264 · 1080p") &&
                TextShown(root, "limit 8.0 Mbps"), "transcode sheet compares delivered and source stream facts");
            Check(TextShown(root, "WHY IT DIDN’T DIRECT PLAY") && TextShown(root, "TIMING & WATCH PROGRESS") &&
                TextShown(root, "ACCOUNT, DEVICE & ROUTE") && TextShown(root, "Stopped by admin") && TextShown(root, "Proxy · edge-eu-1"),
                "transcode sheet includes reasons, watch progress and the recorded route");
            Check(TextShown(root, "Use ↑ and ↓ to move between sessions") && server.Count("/api/v2/admin/playback-history/") == 1 &&
                server.Count("/api/v2/admin/playback-history/capabilities") == 1,
                "an in-page sheet offers keyboard navigation without a detail request");
            await CaptureAsync(window, "admin-playback-history-detail-1440.png");
            await ResizeAsync(window, 560, 1040);
            await CaptureAsync(window, "admin-playback-history-detail-560.png");
            var sheet = Find<FrameworkElement>(root, "AdminHistoryDetail")!;
            var sheetScroll = Descendants(sheet).OfType<ScrollViewer>().Single();
            sheetScroll.ChangeView(null, sheetScroll.ScrollableHeight, null, true);
            Check(await Until(() => sheetScroll.VerticalOffset > 0), "the narrow sheet scrolls to the account and route");
            await CaptureAsync(window, "admin-playback-history-detail-route-560.png");
            sheetScroll.ChangeView(null, 0, null, true);

            var close = Find<Button>(root, "LogsDetailClose")!;
            close.Focus(FocusState.Programmatic);
            Check(SendKey(0x28), "Down reaches the focused fixture window");
            Check(await Until(() => shell.CurrentRequest.Get("session") == "session-0001abcd" && TextShown(root, "None")),
                "Down opens the next direct-play session");
            Check(TextShown(root, "Recorded before detailed playback info was tracked. File details and server route aren’t available for this session.") &&
                !TextShown(root, "WHY IT DIDN’T DIRECT PLAY") && !TextShown(root, "Proxy · edge-eu-1"),
                "legacy direct play shows its notice without adaptation reasons or route badges");
            var account = Find<HyperlinkButton>(root, "AdminHistoryDetailAccount")!;
            Check(account.Focus(FocusState.Programmatic) && SendKey(0x26), "Up reaches an account link inside the sheet body");
            await Task.Delay(100);
            Check(shell.CurrentRequest.Get("session") == "session-0001abcd", "Up inside the scroll body does not step sessions");
            close.Focus(FocusState.Programmatic);
            Check(SendKey(0x26), "Up reaches the sheet header");
            Check(await Until(() => shell.CurrentRequest.Get("session") == "session-0000abcd"), "Up returns to the previous session");
            Check(await Until(() => Find<Button>(root, "AdminHistoryDetailNext") is { IsEnabled: true, IsLoaded: true }), "next detail control is ready");
            Invoke(Find<Button>(root, "AdminHistoryDetailNext")!);
            Check(await Until(() => shell.CurrentRequest.Get("session") == "session-0001abcd" &&
                Find<Button>(root, "AdminHistoryDetailPrevious") is { IsEnabled: true, IsLoaded: true }), "Next session button moves between page rows");
            Invoke(Find<Button>(root, "AdminHistoryDetailPrevious")!);
            Check(await Until(() => shell.CurrentRequest.Get("session") == "session-0000abcd"), "Previous session button moves between page rows");
            Invoke(close);
            Check(await Until(() => shell.CurrentRequest.Get("session") == null && Find<FrameworkElement>(root, "AdminHistoryDetail")?.Visibility != Visibility.Visible),
                "closing the sheet removes the session route");
            await ResizeAsync(window, 1440, 900);
            var currentTable = Descendants(results).OfType<ScrollViewer>().First();
            currentTable.ChangeView(0, null, null, true);
            ((ScrollViewer)Field(history, "_scroll")!).ChangeView(null, 0, null, true);
            await Until(() => currentTable.HorizontalOffset < 1);
            var row = Find<Grid>(history, "AdminHistoryRow_session-0000abcd")!;
            var meta = Find<TextBlock>(row, null, block => block.Text == "episode · session session-")!;
            Check(await ClickAsync(window, meta), "a pointer click is sent to the row in the owned fixture window");
            Check(await Until(() => shell.CurrentRequest.Get("session") == "session-0000abcd" &&
                row.Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: > 0 }), "row body opens the sheet and tints the selected row");
            Invoke(Find<Button>(root, "LogsDetailClose")!);
            Check(await Until(() => shell.CurrentRequest.Get("session") == null), "row-opened sheet also clears its route on close");

            // Drilldown uses the real Insights builder and round-trips through the admin route.
            var drilldown = AdminInsightsRules.BuildHistoryDrilldown(new AdminInsightsHistoryDrilldown
            {
                UserId = "7", ProfileId = "retired-profile", MediaItemId = "item-0", SeriesId = "series-1", LibraryId = "4",
                PlayMethod = "transcode", StreamLocation = "remote", SubtitleBurnIn = true, ClientName = "Jellyfin Web",
                DevicePlatform = "Web", SourceVideoCodec = "hevc", AdaptationReason = "subtitle_burn_in",
                StartedAfter = server.HistoryNow.AddHours(-2).ToString("O"), StartedBefore = server.HistoryNow.ToString("O"),
            });
            Check(AdminRouteCatalog.TryResolve(drilldown.Href, out var resolved) && AdminShellPage.Open(resolved.Request!),
                "an Insights drilldown opens the History query");
            await Until(() => inner.Content is AdminPlaybackHistoryPage current && !ReferenceEquals(current, history));
            var chipPage = (AdminPlaybackHistoryPage)inner.Content;
            Check(await Until(() => chipPage.RenderedSessionIds.SequenceEqual(["session-0000abcd"]) &&
                Find<ComboBox>(chipPage, "AdminHistoryProfile")?.SelectedItem is ComboBoxItem { Content: "Retired profile" }),
                "drilldown returns the matching row and labels its unlisted profile");
            var drilldownPath = AdminPlaybackHistoryApi.HistoryPath(AdminPlaybackHistoryFilter.FromQuery(drilldown.Get), 25);
            Check(server.Requests.Contains(drilldownPath), "all drilldown facets reach the server");
            Check(TextShown(chipPage, "Synthetic Series · S01E01 · Title 0") && TextShown(chipPage, "Client: Jellyfin Web") &&
                TextShown(chipPage, "Platform: Web") && TextShown(chipPage, "Codec: HEVC") &&
                Find<HyperlinkButton>(chipPage, "AdminHistoryItemFilterLink") != null, "drilldown chips include the title link, client, platform and codec");
            var filterActions = Descendants(chipPage).OfType<Button>().Where(button => button.Visibility == Visibility.Visible)
                .Select(AutomationProperties.GetAutomationId).ToArray();
            Check(filterActions.Count(id => id is "AdminHistoryRemove_title" or "AdminHistorySeriesFilter") == 1 &&
                filterActions.Count(id => id is "AdminHistoryRemove_date_range" or "AdminHistoryRemove_started_after" or
                    "AdminHistoryRemove_started_before" or "AdminHistoryStartedAfterFilter" or "AdminHistoryStartedBeforeFilter") == 1 &&
                !filterActions.Any(id => id is "AdminHistoryRemove_library_id" or "AdminHistoryLibraryFilter") &&
                Find<ComboBox>(chipPage, "AdminHistoryLibrary")?.SelectedItem is ComboBoxItem { Tag: "4" },
                "a supported drilldown shows one title chip, one date-range chip and only the library picker");
            await CaptureAsync(window, "admin-playback-history-chips-1440.png");
            await ResizeAsync(window, 560, 1040);
            await CaptureAsync(window, "admin-playback-history-chips-560.png");
            foreach (var (chip, keys) in new (string, string[])[]
            {
                ("title", ["media_item_id", "series_id"]), ("client_name", ["client_name"]), ("device_platform", ["device_platform"]),
                ("source_video_codec", ["source_video_codec"]), ("adaptation_reason", ["adaptation_reason"]),
                ("date_range", ["started_after", "started_before"]),
            })
            {
                var chipId = "AdminHistoryRemove_" + chip;
                Check(await Until(() => Find<Button>(chipPage, chipId) is { IsLoaded: true, IsEnabled: true }),
                    chip + " chip can be invoked");
                Invoke(Find<Button>(chipPage, chipId)!);
                Check(await Until(() => keys.All(key => shell.CurrentRequest.Get(key) == null) &&
                    Find<Button>(chipPage, "AdminHistoryRemove_" + chip) == null), $"removing {chip} clears its route keys and chip");
            }
            Check(ReferenceEquals(chipPage, inner.Content) && shell.CurrentRequest.Get("profile_id") == "retired-profile",
                "chip removal keeps the page and its other facets");

            await CheckSingleDateChip("started_after", server.HistoryNow.AddHours(-2), "From ");
            await CheckSingleDateChip("started_before", server.HistoryNow, "Until ");

            var offPage = new AdminRouteRequest(AdminRoute.PlaybackHistory).WithQuery("session", "session-0055abcd");
            shell.Navigate(offPage);
            Check(await Until(() => TextShown(root, "session-0055abcd") && TextShown(root, "Silo Desktop 1.2")),
                "a session outside the page loads its detail by ID");
            Check(server.Count("/api/v2/admin/playback-history/session-0055abcd") == 1 &&
                Find<Button>(root, "AdminHistoryDetailNext") is { IsEnabled: false }, "off-page detail has no in-page neighbors");
            shell.Navigate(offPage.WithQuery("session", "missing-session"));
            Check(await Until(() => TextShown(root, "This playback session could not be found.")), "404 deep link shows not found");
            shell.Navigate(offPage.WithQuery("session", "failed-session"));
            Check(await Until(() => TextShown(root, "Failed to load session details.") &&
                Find<Button>(root, "AdminHistoryDetailRetry") is { IsEnabled: true, IsLoaded: true }), "detail error offers a distinct state and a ready retry");
            Invoke(Find<Button>(root, "AdminHistoryDetailRetry")!);
            Check(await Until(() => server.Count("/api/v2/admin/playback-history/failed-session") == 2 &&
                TextShown(root, "Failed to load session details.")), "detail error can be retried");

            shell.Navigate(new AdminRouteRequest(AdminRoute.PlaybackHistory).WithQuery("search", "no-synthetic-results"));
            await Until(() => shell.CurrentRequest.Get("search") == "no-synthetic-results");
            var emptyPage = (AdminPlaybackHistoryPage)inner.Content;
            Check(await Until(() => emptyPage.RenderedContent == "empty" && TextShown(emptyPage, "No playback history") &&
                TextShown(emptyPage, "No sessions match these filters. Try clearing or adjusting a filter.")), "official empty state renders");
            Check(((FrameworkElement)Field(emptyPage, "_pager")!).Visibility == Visibility.Collapsed, "pager is hidden on an empty first page");
            await CaptureAsync(window, "admin-playback-history-empty-560.png");
            await ResizeAsync(window, 1440, 900);
            await CaptureAsync(window, "admin-playback-history-empty-1440.png");

            async Task CheckSingleDateChip(string key, DateTimeOffset instant, string label)
            {
                shell.Navigate(new AdminRouteRequest(AdminRoute.PlaybackHistory).WithQuery(key, instant.ToString("O")));
                var chipId = "AdminHistoryRemove_" + key;
                Check(await Until(() => shell.CurrentRequest.Get(key) != null && Find<Button>(root, chipId) is { IsLoaded: true, IsEnabled: true } &&
                    Descendants(root).OfType<TextBlock>().Any(block => block.Text.StartsWith(label, StringComparison.Ordinal))),
                    key + " displays its single-date chip");
                Invoke(Find<Button>(root, chipId)!);
                Check(await Until(() => shell.CurrentRequest.Get(key) == null && Find<Button>(root, chipId) == null),
                    key + " single-date chip clears the filter");
            }

            async Task<bool> FilterSettled(string key, string value, Func<AdminPlaybackHistoryRow, bool> match) =>
                await Until(() => shell.CurrentRequest.Get(key) == value && history.Controller?.State is { Fetching: false, Rows.Count: > 0 } state &&
                    state.Rows.All(match) && server.Requests.Contains(AdminPlaybackHistoryApi.HistoryPath(state.Filter, state.PageSize)));

            // Error and retry through the item filter route (push, a new page instance).
            AdminShellPage.Open(new AdminRouteRequest(AdminRoute.PlaybackHistory, new Dictionary<string, string> { ["media_item_id"] = "broken" }));
            Check(await Until(() => inner.Content is AdminPlaybackHistoryPage page && !ReferenceEquals(page, history) && page.RenderedContent == "error"), "load error renders");
            Check(history.Controller == null, "the departed page released its controller");
            var errorPage = (AdminPlaybackHistoryPage)inner.Content;
            Check(TextShown(errorPage, AdminPlaybackAuditText.T("web.admin.history.itemFilterActive")) && TextShown(errorPage, "broken"), "item filter chip shows the raw id without rows");
            server.Healed = true;
            var refresh = Find<Button>(errorPage, "AdminHistoryRefresh")!;
            Invoke(refresh);
            Check(await Until(() => !refresh.IsEnabled), "Refresh is disabled while pending");
            Check(await Until(() => errorPage.RenderedContent == "table"), "Refresh retries after an error");
            Check(await Until(() => refresh.IsEnabled, 3000), "Refresh re-enables after its minimum spinner");
            var reads = server.Count("/api/v2/admin/playback-history?");
            var retainedTable = ((StackPanel)Field(errorPage, "_content")!).Children[0];
            Invoke(refresh);
            Invoke(refresh);
            Check(await Until(() => server.Count("/api/v2/admin/playback-history?") == reads + 1), "manual refresh reaches the backend");
            Check(await Until(() => refresh.IsEnabled && errorPage.Controller?.State.Fetching == false, 3000), "manual refresh and spinner settle");
            Check(server.Count("/api/v2/admin/playback-history?") == reads + 1, "overlapping refresh clicks coalesce into exactly one read");
            Check(ReferenceEquals(retainedTable, ((StackPanel)Field(errorPage, "_content")!).Children[0]), "unchanged refresh retains the table and its focus/scroll state");

            // Visible polling (WebUI 15 s) happens while shown.
            var polled = server.Count("/api/v2/admin/playback-history?");
            Check(await Until(() => server.Count("/api/v2/admin/playback-history?") > polled, 17000), "a visible page polls about every 15 seconds");
            Check(await Until(() => errorPage.Controller?.State.Fetching == false), "poll response settles");
            await Task.Delay(100);
            Check(ReferenceEquals(retainedTable, ((StackPanel)Field(errorPage, "_content")!).Children[0]), "unchanged poll retains the table");

            server.ChangeTitle = true;
            Invoke(refresh);
            Check(await Until(() => TextShown(errorPage, "Changed title")), "changed history rows replace the table");
            Check(!ReferenceEquals(retainedTable, ((StackPanel)Field(errorPage, "_content")!).Children[0]), "changed refresh redraws current data");

            // Skip Markers.
            await ResizeAsync(window, 1440, 900);
            shell.Navigate(new AdminRouteRequest(AdminRoute.SkipMarkers));
            Check(await Until(() => inner.Content is AdminMarkerHistoryPage page && page.RenderedContent == "table"), "marker history renders");
            Check(errorPage.Controller == null, "leaving History retires its reads and polling");
            var afterLeave = server.Count("/api/v2/admin/playback-history");
            var markers = (AdminMarkerHistoryPage)inner.Content;
            Check(server.Requests.Contains("/api/v2/admin/markers/history?limit=50"), "marker history asks for the default 50");
            Check(await Until(() => TextShown(markers, "Intro") && TextShown(markers, "Credits / Outro") && TextShown(markers, "Recap") && TextShown(markers, "commercial")),
                "marker kinds keep labels and an unknown kind is shown as sent");
            Check(TextShown(markers, "0:12-1:10 -> 0:15-1:28") && TextShown(markers, "41:40-43:30 -> none") && TextShown(markers, "none -> 0:00-0:45"),
                "before and after ranges render");
            Check(TextShown(markers, "via synthetic-admin") && TextShown(markers, "No request id") && TextShown(markers, "not-a-time"), "actor, request and unparsable time render");
            Check(!Descendants(markers).OfType<Button>().Any(button => AutomationProperties.GetName(button) is "Delete" or "Edit" or "Redetect"), "marker history offers no mutation");
            await CaptureAsync(window, "admin-marker-history-1440.png");
            SelectTag(Find<ComboBox>(markers, "AdminMarkerHistoryLimit")!, "25");
            Check(await Until(() => server.Requests.Contains("/api/v2/admin/markers/history?limit=25") && markers.RenderedContent == "table"), "count selector reads the chosen bound");
            await ResizeAsync(window, 1024, 768);
            await CaptureAsync(window, "admin-marker-history-1024.png");
            await ResizeAsync(window, 800, 900);
            await CaptureAsync(window, "admin-marker-history-800.png");
            await Task.Delay(15500);
            Check(server.Count("/api/v2/admin/playback-history") == afterLeave, "no hidden History polling beyond a complete poll interval after departure");

            // Outer Admin departure suspends the shown audit page once.
            outer.Navigate(typeof(Page));
            Check(await Until(() => markers.Controller == null), "outer departure retires marker history");
            var markerReads = server.Count("/api/v2/admin/markers/history");
            await Task.Delay(300);
            Check(server.Count("/api/v2/admin/markers/history") == markerReads, "no marker reads after departure");
        }
        finally
        {
            window.Content = previousContent;
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException($"Admin playback audit fixture failed: {string.Join("; ", Failures)}");
        Program.Log("PASS: Admin playback audit native fixture completed with the synthetic backend.");
    }

    private static void CheckLogActionBounds(AdminPlaybackHistoryPage page, ScrollViewer? viewport = null)
    {
        var link = Find<Button>(page, "AdminHistoryLogs_session-0000abcd");
        Check(link is { IsLoaded: true }, "log actions are rendered for bounds check");
        if (link == null) return;
        var actions = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(link) as StackPanel;
        Check(actions != null, "log action container is available for bounds checks");
        if (actions == null) return;
        var cell = (FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(actions);
        foreach (var action in actions.Children.OfType<Button>())
        {
            var left = action.TransformToVisual(cell).TransformPoint(default).X;
            Check(left >= -1 && left + action.ActualWidth <= cell.ActualWidth + 1, "log action stays inside its table cell");
            if (viewport != null)
            {
                var position = action.TransformToVisual(viewport).TransformPoint(default).X;
                Check(position >= -1 && position + action.ActualWidth <= viewport.ViewportWidth + 1,
                    "the scrolled action is fully inside the narrow table viewport");
            }
        }
    }

    /// <summary>Resizes the client area to DIPs at the current rasterization scale and waits for layout.</summary>
    private static async Task ResizeAsync(Window window, int width, int height)
    {
        await Until(() => window.Content is FrameworkElement { XamlRoot: not null });
        var scale = (window.Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
        var settled = await Until(() => window.Content is FrameworkElement root && Math.Abs(root.ActualWidth - width) < 2, 3000);
        Check(settled, $"client area is {width} DIP wide (scale {scale:0.##})");
        await Task.Delay(250);
    }

    private static void Invoke(Button button)
    {
        if (!button.IsEnabled || !button.IsLoaded) { Check(false, $"{AutomationProperties.GetAutomationId(button)} is enabled and loaded before invoking"); return; }
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    }

    private static bool SendKey(nuint key)
    {
        var target = GetFocus();
        GetWindowThreadProcessId(target, out var process);
        return target != 0 && process == (uint)Environment.ProcessId &&
            PostMessage(target, 0x0100, key, 1) && PostMessage(target, 0x0101, key, unchecked((nint)0xC0000001u));
    }

    private static async Task<bool> ClickAsync(Window window, FrameworkElement target)
    {
        // The native runner starts its process hidden. Show and position only this fixture window.
        window.AppWindow.Show(true);
        window.AppWindow.Move(new(8, 8));
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SetForegroundWindow(hwnd);
        await Task.Delay(150);
        var root = (FrameworkElement)window.Content;
        var offset = target.TransformToVisual(root).TransformPoint(new(target.ActualWidth / 2, target.ActualHeight / 2));
        var scale = root.XamlRoot.RasterizationScale;
        var point = new ScreenPoint { X = (int)Math.Round(offset.X * scale), Y = (int)Math.Round(offset.Y * scale) };
        if (!ClientToScreen(hwnd, ref point) || GetAncestor(WindowFromPoint(point), 2) != hwnd || !GetCursorPos(out var previous))
            return false;
        try
        {
            if (!SetCursorPos(point.X, point.Y) || !GetCursorPos(out var placed) ||
                GetAncestor(WindowFromPoint(placed), 2) != hwnd) return false;
            mouse_event(2, 0, 0, 0, 0);
            mouse_event(4, 0, 0, 0, 0);
            await Task.Delay(75);
            return true;
        }
        finally { SetCursorPos(previous.X, previous.Y); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint { public int X, Y; }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(nint hwnd, ref ScreenPoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extra);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(ScreenPoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flag);
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint target, out uint process);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint target, uint message, nuint wParam, nint lParam);

    private static void SelectTag(ComboBox select, string tag)
    {
        if (!select.IsEnabled || !select.IsLoaded) { Check(false, $"{AutomationProperties.GetAutomationId(select)} is enabled and loaded before selecting"); return; }
        select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == tag);
    }

    private static T? Find<T>(DependencyObject root, string? automationId, Func<T, bool>? predicate = null) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(element =>
            (automationId == null || AutomationProperties.GetAutomationId(element) == automationId) && (predicate?.Invoke(element) ?? true));

    private static bool TextShown(DependencyObject root, string text) =>
        Descendants(root).OfType<TextBlock>().Any(block => block.Text == text && block.ActualWidth > 0);

    private static async Task CaptureAsync(Window window, string name)
    {
        var element = (FrameworkElement)window.Content;
        element.UpdateLayout();
        await Task.Delay(200);
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

    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
