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

/// <summary>
/// Admin › Playback › History and Skip Markers in the real registered shell
/// (G10 read-only audit). Synthetic admin and backend only; no live server,
/// account, user data or installed player. Proves: both routes register and
/// appear; the history table, filters, local pager, empty, error/retry and
/// refresh states render as controls; local paging sends no request; URL state
/// is replaced in place; departure retires reads and polling; marker history
/// renders kinds (unknown as sent) and its count selector. Captures 1440, 1024
/// and 800 DIP layouts (scale-aware). Program.cs dispatch
/// (SILO_NATIVE_TEST_ONLY=admin-playback-audit) is wired by the lead.
/// Screenshots are evidence for review, not a visual-parity certification.
/// </summary>
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

    /// <summary>Synthetic backend. History: 60 finalized rows; completed=false is empty; media_item_id=broken fails until healed.</summary>
    private sealed class Server : HttpMessageHandler
    {
        private readonly object _gate = new();
        public List<string> Requests { get; } = [];
        public bool Healed { get; set; }
        public bool ChangeTitle { get; set; }
        private readonly DateTimeOffset _historyNow = DateTimeOffset.UtcNow;

        public int Count(string prefix) { lock (_gate) return Requests.Count(path => path.StartsWith(prefix, StringComparison.Ordinal)); }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            lock (_gate) Requests.Add(path);
            var status = HttpStatusCode.OK;
            string body;
            if (path.StartsWith("/api/v2/admin/playback-history", StringComparison.Ordinal))
            {
                if (path.Contains("media_item_id=broken", StringComparison.Ordinal) && !Healed)
                {
                    status = HttpStatusCode.InternalServerError;
                    body = "{\"type\":\"internal_error\",\"title\":\"Unable to list playback history.\",\"status\":500}";
                }
                else if (path.Contains("completed=false", StringComparison.Ordinal)) body = "{\"items\":[],\"page\":{\"has_more\":false}}";
                else body = History(path.Contains("completed=true", StringComparison.Ordinal) ? 30 : 60);
            }
            else body = request.RequestUri.AbsolutePath switch
            {
                "/api/v2/admin/users" => "{\"items\":[{\"id\":\"7\",\"username\":\"synthetic-viewer\"},{\"id\":\"8\",\"username\":\"synthetic-guest\"}],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/users/7/profiles" => "{\"items\":[{\"id\":\"p1\",\"name\":\"Living room\"},{\"id\":\"p2\",\"name\":\"Kids\"}],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/markers/history" => Markers(),
                "/api/v2/admin/sessions" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/diagnostics/reports" => "{\"items\":[],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/tasks" => "{\"items\":[]}",
                "/api/v2/libraries" => "{\"items\":[]}",
                "/api/v2/admin/downloads/preparations" => "{\"counts\":{\"running\":0,\"queued\":0,\"retrying\":0,\"paused\":0,\"failed_recent\":0},\"items\":[]}",
                "/api/v2/admin/system/build" => "{\"display\":\"v1.2.3\",\"revision\":\"abc\",\"dirty\":false,\"build_number\":4321,\"available\":true}",
                "/api/v2/admin/server/status" => "{\"restart_required\":false,\"restart_mark_count\":0,\"restart_requested\":false}",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }

        private string History(int count)
        {
            var now = _historyNow;
            var rows = Enumerable.Range(0, count).Select(index =>
            {
                var episode = index % 3 == 0;
                var ended = now.AddMinutes(-15 * index);
                var started = ended.AddMinutes(-42);
                return $$"""
                    {"session_id":"session-{{index:D4}}abcd","user_id":"{{(index % 2 == 0 ? 7 : 8)}}","username":"{{(index % 5 == 4 ? "" : index % 2 == 0 ? "synthetic-viewer" : "synthetic-guest")}}",
                    "profile_id":"p{{index % 2 + 1}}","profile_name":"{{(index % 2 == 0 ? "Living room" : "")}}","media_item_id":"{{(index % 7 == 6 ? "" : "item-" + index)}}",
                    "media_file_id":"{{index + 100}}","media_title":"{{(ChangeTitle && index == 1 ? "Changed title" : "Title " + index)}}","media_type":"{{(episode ? "episode" : "movie")}}","play_method":"{{(index % 4 == 0 ? "transcode" : "direct")}}",
                    "started_at":"{{started:O}}","ended_at":"{{ended:O}}","end_position_seconds":2520,"duration_seconds":{{(index % 9 == 8 ? "null" : "5400")}},
                    "completed":{{(index % 3 != 1 ? "true" : "false")}},"played_seconds":{{(index % 6 == 5 ? "null" : "2400")}},
                    "series_title":"{{(episode ? "Synthetic Series" : "")}}","season_number":{{(episode ? "1" : "null")}},"episode_number":{{(episode ? (index % 10 + 1).ToString() : "null")}},
                    "measured":true,"client_ip":"192.0.2.10","device_platform":"Windows","end_reason":"stopped"}
                    """;
            });
            return "{\"items\":[" + string.Join(",", rows) + "],\"page\":{\"has_more\":true,\"next_cursor\":\"synthetic-next\"}}";
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
            .AddAdminPlaybackAudit();
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

            // Rendered table: wait for actual row controls, not controller state.
            Check(await Until(() => TextShown(history, "Synthetic Series") && TextShown(history, "Title 1")), "history rows render as controls");
            Check(history.RenderedContent == "table" && history.RenderedSessionIds.Count == 25, "first local page shows 25 of the bounded window");
            Check(server.Count("/api/v2/admin/playback-history?limit=100") == 1 && !server.Requests.Any(path => path.Contains("cursor=", StringComparison.Ordinal)),
                "one bounded history request; no cursor walk");
            Check(TextShown(history, AdminPlaybackAuditText.T("web.admin.history.stats.visibleRows")) && TextShown(history, "60"), "summary card labels visible rows of the window");
            Check(TextShown(history, AdminPlaybackAuditText.T("admin.native.playbackAudit.windowBounded", AdminPlaybackAuditText.Count(60))), "bounded window is disclosed");
            Check(TextShown(history, AdminPlaybackAuditText.T("web.admin.history.pagination.range", ("from", 1), ("to", 25), ("total", 60))), "pager range is shown");
            var profile = Find<ComboBox>(history, "AdminHistoryProfile");
            Check(profile is { IsEnabled: false }, "profile picker is disabled without a user");
            CheckLogActionBounds(history);
            await CaptureAsync(window, "admin-playback-history-1440.png");

            // Local paging: mounted, enabled pager controls; no request.
            var before = server.Count("/api/v2/admin/playback-history");
            var next = Find<Button>(history, "AdminHistoryNext")!;
            Check(next.IsEnabled && next.IsLoaded, "Next is enabled and loaded");
            Invoke(next);
            Check(await Until(() => history.RenderedSessionIds.FirstOrDefault() == "session-0025abcd"), "Next shows the second local page");
            Check(ReferenceEquals(next, Find<Button>(history, "AdminHistoryNext")), "pager controls stay mounted across pages");
            var pageSize = Find<ComboBox>(history, "AdminHistoryPageSize")!;
            SelectTag(pageSize, "50");
            Check(await Until(() => history.RenderedSessionIds.Count == 50 && history.RenderedSessionIds[0] == "session-0000abcd"), "50 rows per page restarts at page one");
            Check(server.Count("/api/v2/admin/playback-history") == before, "local paging sends no request");

            // Filters replace URL state in place and reload.
            var completion = Find<ComboBox>(history, "AdminHistoryCompletion")!;
            SelectTag(completion, "true");
            Check(await Until(() => shell.CurrentRequest.Get("completed") == "true" && ReferenceEquals(inner.Content, history)), "completion filter replaces the URL in place");
            Check(await Until(() => history.RenderedContent == "table" && TextShown(history, AdminPlaybackAuditText.T("web.admin.history.pagination.range", ("from", 1), ("to", 30), ("total", 30)))),
                "completed filter shows its own window");
            Check(!inner.CanGoBack, "filter replacement adds no history entry");
            var user = Find<ComboBox>(history, "AdminHistoryUser")!;
            Check(await Until(() => user.Items.Count == 3), "user picker lists the bounded accounts");
            SelectTag(user, "7");
            Check(await Until(() => Find<ComboBox>(history, "AdminHistoryProfile") is { IsEnabled: true } picker && picker.Items.Count == 3), "choosing a user loads and enables profiles");
            Check(shell.CurrentRequest.Get("user_id") == "7" && shell.CurrentRequest.Get("profile_id") == null, "choosing a user drops the profile");
            SelectTag(Find<ComboBox>(history, "AdminHistoryProfile")!, "p2");
            Check(await Until(() => shell.CurrentRequest.Get("profile_id") == "p2"), "profile selection is URL state");

            // Empty state.
            SelectTag(completion, "false");
            Check(await Until(() => history.RenderedContent == "empty" && TextShown(history, AdminPlaybackAuditText.T("web.admin.history.empty.title"))), "empty state renders");
            Check(((Grid)Field(history, "_pager")!).Visibility == Visibility.Collapsed, "pager is hidden when empty");
            await CaptureAsync(window, "admin-playback-history-empty-1440.png");

            // Reset clears every parameter.
            Invoke(Find<Button>(history, "AdminHistoryReset")!);
            Check(await Until(() => shell.CurrentRequest.Query == null && history.RenderedContent == "table"), "Reset clears the URL and reloads");

            // Error and retry through the item filter route (push, a new page instance).
            AdminShellPage.Open(new AdminRouteRequest(AdminRoute.PlaybackHistory, new Dictionary<string, string> { ["media_item_id"] = "broken" }));
            Check(await Until(() => inner.Content is AdminPlaybackHistoryPage page && !ReferenceEquals(page, history) && page.RenderedContent == "error"), "load error renders");
            Check(history.Controller == null, "the departed page released its controller");
            var errorPage = (AdminPlaybackHistoryPage)inner.Content;
            Check(TextShown(errorPage, AdminPlaybackAuditText.T("web.admin.history.itemFilterActive")) && TextShown(errorPage, "broken"), "item filter chip shows the raw id without rows");
            await ResizeAsync(window, 1024, 768);
            await CaptureAsync(window, "admin-playback-history-error-1024.png");
            server.Healed = true;
            var refresh = Find<Button>(errorPage, "AdminHistoryRefresh")!;
            Invoke(refresh);
            Check(await Until(() => !refresh.IsEnabled), "Refresh is disabled while pending");
            Check(await Until(() => errorPage.RenderedContent == "table"), "Refresh retries after an error");
            Check(await Until(() => refresh.IsEnabled, 3000), "Refresh re-enables after its minimum spinner");
            var reads = server.Count("/api/v2/admin/playback-history");
            var retainedTable = ((StackPanel)Field(errorPage, "_content")!).Children[0];
            Invoke(refresh);
            Invoke(refresh);
            Check(await Until(() => server.Count("/api/v2/admin/playback-history") == reads + 1), "manual refresh reaches the backend");
            Check(await Until(() => refresh.IsEnabled && errorPage.Controller?.State.Fetching == false, 3000), "manual refresh and spinner settle");
            Check(server.Count("/api/v2/admin/playback-history") == reads + 1, "overlapping refresh clicks coalesce into exactly one read");
            Check(ReferenceEquals(retainedTable, ((StackPanel)Field(errorPage, "_content")!).Children[0]), "unchanged refresh retains the table and its focus/scroll state");
            await CaptureAsync(window, "admin-playback-history-1024.png");
            await ResizeAsync(window, 800, 900);
            Check(await Until(() => TextShown(errorPage, "Title 1")), "narrow layout keeps table rows");
            await CaptureAsync(window, "admin-playback-history-800.png");

            // Visible polling (WebUI 15 s) happens while shown.
            var polled = server.Count("/api/v2/admin/playback-history");
            Check(await Until(() => server.Count("/api/v2/admin/playback-history") > polled, 17000), "a visible page polls about every 15 seconds");
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
        Program.Log("PASS: Admin playback audit native fixture completed; synthetic backend only; no live server, phone or visual-parity certification.");
    }

    private static void CheckLogActionBounds(AdminPlaybackHistoryPage page)
    {
        var link = Find<HyperlinkButton>(page, "AdminHistoryLogs_session-0000abcd");
        Check(link is { IsLoaded: true }, "log actions are rendered for bounds check");
        if (link == null) return;
        var actions = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(link) as StackPanel;
        Check(actions != null, "log action container is available for bounds checks");
        if (actions == null) return;
        foreach (var action in actions.Children.OfType<HyperlinkButton>())
        {
            var left = action.TransformToVisual(actions).TransformPoint(new Windows.Foundation.Point()).X;
            Check(left >= -1 && left + action.ActualWidth <= actions.ActualWidth + 1, "log action stays inside its table cell");
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
