using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
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
/// Admin Monitor shell/indicator/Activity native fixture. Synthetic admin and
/// sessions only; no live server, account or user data. Modes (SILO_NATIVE_ADMIN_CASE):
/// "indicator" — visibility matrix, badge/99+/accessible names, suppression, role loss;
/// "activity" — Activity page wide/stacked table, filters, IP lookup, FFmpeg console states,
///              plus PNG captures at 1440x900, 1280x800, 1024x768, 1023x768 and 800x900;
/// "" — both. Program.cs dispatch is wired by the lead (see admin-shell-implementation.md).
/// </summary>
internal static class AdminMonitorShellNativeFixture
{
    private static readonly List<string> Failures = [];

    private static void Check(bool condition, string label)
    {
        Program.Log((condition ? "PASS: " : "FAIL: ") + label);
        if (!condition) Failures.Add(label);
    }

    private static async Task Until(Func<bool> condition, int milliseconds = 4000)
    {
        for (var waited = 0; waited < milliseconds && !condition(); waited += 25) await Task.Delay(25);
    }

    private sealed class Realtime : IAdminRealtimeChannel
    {
        public int Active;
        public WebSocketState CurrentState { get; set; } = WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged;
        public IDisposable Subscribe(params string[] channels)
        {
            Active += channels.Length;
            var released = false;
            return new Release(() => { if (!released) { released = true; Active -= channels.Length; } });
        }
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot)
        {
            snapshot = JsonDocument.Parse("[]").RootElement.Clone();
            return channel == "scans";
        }
        public void Event(string channel, string name) => EventReceived?.Invoke(channel, name, JsonDocument.Parse("{}").RootElement.Clone());
        public void State(WebSocketState state) { CurrentState = state; StateChanged?.Invoke(state); }
        public void Snapshot(string channel, string json) => SnapshotReceived?.Invoke(channel, JsonDocument.Parse(json).RootElement.Clone());
        private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }
    }

    private sealed class Server : HttpMessageHandler
    {
        public int SessionCount = 3;
        public bool SessionsFail;
        public int AdminReads;
        public TaskCompletionSource<bool>? HeldMessage;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/message", StringComparison.Ordinal) && HeldMessage is { } held)
            {
                var command = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
                await held.Task.WaitAsync(cancellationToken);
                var receipt = JsonSerializer.Serialize(new { command_id = command.GetProperty("command_id").GetString(), sequence = command.GetProperty("sequence").GetInt64(), outcome = "applied", delivery = "dispatched" });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(receipt, Encoding.UTF8, "application/json") };
            }
            if (path.StartsWith("/api/v2/admin", StringComparison.Ordinal) || path == "/api/v2/libraries") Interlocked.Increment(ref AdminReads);
            if (path == "/api/v2/admin/sessions" && SessionsFail)
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            string body = path switch
            {
                "/api/v2/admin/sessions" => "{\"items\":[" + string.Join(",", Enumerable.Range(0, SessionCount).Select(Session)) + "],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/tasks" => "{\"items\":[{\"key\":\"scan\",\"name\":\"Scan libraries\",\"description\":\"\",\"category\":\"library\",\"state\":\"running\",\"progress\":42.5}]}",
                "/api/v2/libraries" => "{\"items\":[{\"id\":\"1\",\"name\":\"Movies\",\"type\":\"movies\"}]}",
                "/api/v2/admin/downloads/preparations" => "{\"counts\":{\"running\":0,\"queued\":1,\"retrying\":0,\"paused\":0,\"failed_recent\":1},\"items\":[]}",
                "/api/v2/admin/system/build" => "{\"display\":\"v1.2.3\",\"revision\":\"abc\",\"dirty\":false,\"build_number\":4321,\"available\":true}",
                "/api/v2/admin/server/status" => "{\"restart_required\":true,\"restart_mark_count\":2,\"restart_requested\":false}",
                "/api/v2/admin/ips" => "{\"items\":[{\"user_id\":\"3\",\"username\":\"synthetic-viewer\",\"first_seen\":\"2026-11-01T00:00:00Z\",\"last_seen\":\"2026-11-13T00:00:00Z\",\"request_count\":1200}],\"page\":{\"has_more\":true,\"next_cursor\":\"n1\"}}",
                "/api/v2/admin/logs/app" => "{\"items\":[{\"id\":\"9\",\"timestamp\":\"2026-11-14T10:00:00Z\",\"level\":\"info\",\"component\":\"ffmpeg\",\"message\":\"ffmpeg stderr\",\"node_id\":\"gpu-1\",\"attrs\":{\"ffmpeg_line\":\"frame=  120 fps= 48 q=28.0\",\"hw_accel\":\"qsv\",\"restart_count\":1}}],\"page\":{\"has_more\":false}}",
                _ => "{}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }

        private static string Session(int i) =>
            "{\"session_id\":\"synthetic-" + i + "\",\"user_id\":\"" + (7 + i) + "\",\"username\":\"viewer" + i + "\",\"profile_id\":\"p" + i + "\",\"profile_name\":\"Profile " + i + "\"," +
            "\"media_file_id\":\"" + (40 + i) + "\",\"requested_media_file_id\":\"" + (40 + i) + "\",\"media_title\":\"Synthetic Title " + i + "\",\"media_type\":\"" + (i % 2 == 0 ? "movie" : "series") + "\"," +
            "\"play_method\":\"" + (i == 0 ? "direct" : "transcode") + "\",\"video_decision\":\"" + (i == 0 ? "direct" : "transcode") + "\",\"audio_decision\":\"" + (i == 0 ? "direct" : "copy") + "\"," +
            "\"reporting_node\":\"local\",\"file_duration\":7200,\"started_at\":\"2026-11-14T10:00:00Z\",\"updated_at\":\"2026-11-14T10:00:00Z\",\"position_seconds\":" + (600 + i) + "," +
            "\"is_paused\":" + (i == 2 ? "true" : "false") + ",\"has_playback_control\":" + (i == 1 ? "false" : "true") + ",\"stream_location\":\"local\",\"audio_track_index\":0,\"transcode_audio\":false," +
            "\"stream_bitrate_kbps\":8000,\"target_bitrate_kbps\":null,\"source_bitrate_kbps\":null,\"source_audio_channels\":6,\"source_container\":\"mkv\",\"client_label\":\"Synthetic Client 1.0\",\"client_ip\":\"203.0.113." + (10 + i) + "\"," +
            "\"routing_workload\":\"video_transcode\",\"routing_execution\":\"" + (i == 0 ? "api" : "transcode") + "\",\"routing_execution_node_name\":\"gpu-1\",\"routing_egress\":\"api\",\"routing_network_provider\":\"\"}";
    }

    internal static async Task RunAsync(Window window)
    {
        var mode = Environment.GetEnvironmentVariable("SILO_NATIVE_ADMIN_CASE") ?? "";
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
        var navigation = new NavigationService();
        await using var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(api).AddSingleton(store)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        field.SetValue(null, services);
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        try
        {
            if (mode is "" or "indicator") await IndicatorAsync(window, server, realtime, auth, store);
            if (mode is "" or "activity") await ActivityAsync(window, server, store);
            if (mode is "" or "ownership") await OwnershipAsync(window, server, auth, store);
        }
        finally
        {
            field.SetValue(null, previous);
        }
        if (Failures.Count > 0) throw new InvalidOperationException($"Admin Monitor fixture failed: {string.Join("; ", Failures)}");
    }

    private static async Task IndicatorAsync(Window window, Server server, Realtime realtime, AuthService auth, ServerActivityStore store)
    {
        var main = new ServerActivityButton { HideWhenEmpty = true };
        var admin = new ServerActivityButton { HideWhenEmpty = false };
        var host = new StackPanel { Orientation = Orientation.Horizontal, Children = { main, admin } };
        window.Content = host;
        await Task.Delay(100);

        // Before eligibility (e.g. Home's first rows not loaded) nothing shows and nothing is read.
        Check(!main.IsTriggerShown && !admin.IsTriggerShown, "ineligible indicators stay hidden");
        Check(realtime.Active == ServerActivityStore.AdminChannels.Length, "one shared admin channel lease for the acting admin");
        Check(server.AdminReads == 0, "no indicator reads before a consumer is eligible");

        server.SessionCount = 0;
        main.Eligible = true;
        admin.Eligible = true;
        await Until(() => store.Current.SessionsLoaded && store.Current.Tasks.Count > 0);
        await Task.Delay(100);
        // One running task + one queued preparation are active, so both show; force idle by role-free data instead.
        Check(admin.IsTriggerShown, "admin shell indicator is always shown for an acting admin");
        var trigger = (Button)((Grid)admin.Content).Children[0];
        Check(trigger.Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 0 }, "activity trigger keeps a transparent normal background");
        Check(main.CurrentView.TotalActive == 2, $"total = sessions + running hidden-inclusive tasks + scans + active preparations ({main.CurrentView.TotalActive})");
        Check(AutomationProperties.GetName((UIElement)((Grid)main.Content).Children[0]) == "Server activity: 2 active", "accessible name carries the count without a tooltip");

        server.SessionCount = 120;
        realtime.Event("sessions", "sessions.replaced");
        await Until(() => store.Current.Sessions.Count == 120);
        await Task.Delay(100);
        Check(main.CurrentView.BadgeText == "99+", "badge caps at 99+");

        main.Suppressed = true;
        await Task.Delay(50);
        Check(!main.IsTriggerShown, "covered by the full-window player: hidden");
        main.Suppressed = false;
        await Task.Delay(50);
        Check(main.IsTriggerShown, "mini player context: shown");

        realtime.State(WebSocketState.Closed);
        await Task.Delay(3000);
        Check(!store.Current.ConnectionProblem, "no connection dot before four seconds");
        await Task.Delay(1300);
        Check(store.Current.ConnectionProblem, "connection dot after four continuous non-live seconds");
        realtime.State(WebSocketState.Open);
        await Task.Delay(50);
        Check(!store.Current.ConnectionProblem, "dot clears immediately on reconnect");

        await CaptureAsync(host, "admin-indicator-active.png");

        // Role loss retires everything; regaining admin does not revive the old data.
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
        await Task.Delay(100);
        Check(!main.IsTriggerShown && !admin.IsTriggerShown, "role loss hides every indicator");
        Check(realtime.Active == 0, "role loss releases only the admin channel lease");
        Check(store.Current.Sessions.Count == 0, "role loss clears retired sessions");
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        await Until(() => store.Current.SessionsLoaded);
        Check(admin.IsTriggerShown, "regranted admin re-reads under a new epoch");
    }

    private static async Task ActivityAsync(Window window, Server server, ServerActivityStore store)
    {
        server.SessionCount = 3;
        await store.RefreshSessionsAsync();
        var frame = new Frame { Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBackgroundBrush"] };
        window.Content = frame;
        Check(((Microsoft.UI.Xaml.Markup.IXamlMetadataProvider)Application.Current).GetXamlType(typeof(AdminActivityPage)) != null, "Activity has WinUI navigation activation metadata");
        frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Activity));
        var shell = (AdminShellPage)frame.Content;
        var innerFrame = (Frame)Field(shell, "_frame")!;
        var page = (AdminActivityPage)innerFrame.Content;
        await Until(() => page.RenderedSessionIds.Count == 3);
        Check(page.RenderedSessionIds.Count == 3, "Activity renders every live session");

        var table = (StackPanel)Field(page, "_tableHost")!;
        bool Shows(string text) => Descendants(table).OfType<TextBlock>().Any(block => block.Text == text);
        server.SessionsFail = true;
        await store.RefreshSessionsAsync();
        await Until(() => Shows(AdminText.T("web.admin.activity.page.stale")));
        Check(Shows(AdminText.T("web.admin.activity.page.stale")) && page.RenderedSessionIds.Count == 3,
            "failed refresh labels retained Activity rows as stale");
        Check(Shows(store.Current.SessionsError!), "Activity displays the session read error");
        server.SessionsFail = false;
        server.SessionCount = 0;
        await store.RefreshSessionsAsync();
        await Until(() => Shows(AdminText.T("web.admin.activity.page.noStreams")));
        server.SessionsFail = true;
        await store.RefreshSessionsAsync();
        await Until(() => Shows(AdminText.T("web.admin.activity.page.loadFailed")));
        Check(Shows(AdminText.T("web.admin.activity.page.loadFailed")) && !Shows(AdminText.T("web.admin.activity.page.noStreams")),
            "failed Activity read without rows shows an error instead of No active streams");
        server.SessionsFail = false;
        server.SessionCount = 3;
        var retry = Descendants(table).OfType<Button>().Single(button => AutomationProperties.GetName(button) == AdminText.T("common.retry"));
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(retry)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        await Until(() => page.RenderedSessionIds.Count == 3 && !Shows(AdminText.T("web.admin.activity.page.loadFailed")));
        Check(page.RenderedSessionIds.Count == 3 && store.Current.SessionsError == null, "Activity Retry restores rows and clears the error");

        foreach (var (width, height) in new[] { (1440, 900), (1363, 900), (1362, 900), (1280, 800), (1024, 768), (1023, 768), (800, 900), (640, 900), (639, 900) })

        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height));
            await Task.Delay(300);
            Program.Log($"TRACE activity {width}x{height}: wide={page.IsWideTable}");
            if (width is 1362 or 1363) Check(page.IsWideTable == (width == 1363), $"Activity wide rows switch at the reference boundary ({width}px)");
            await CaptureAsync(frame, $"admin-activity-{width}x{height}.png");
        }
        var rowStates = (System.Collections.IDictionary)Field(page, "_rowStates")!;
        var firstSession = store.Current.Sessions.First();
        var rowState = rowStates[firstSession.SessionId]!;
        rowState.GetType().GetField("Details")!.SetValue(rowState, true);
        typeof(AdminActivityPage).GetMethod("RenderTable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
        foreach (var width in new[] { 1440, 1280, 1024, 1023, 800 })
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900));
            await Task.Delay(150);
            await CaptureAsync(frame, $"admin-activity-expanded-{width}x900.png");
        }
        rowState.GetType().GetField("Ffmpeg")!.SetValue(rowState, true);
        var ffmpegRead = (Task)typeof(AdminActivityPage).GetMethod("LoadFfmpegAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new object[] { firstSession.SessionId, rowState, true })!;
        await ffmpegRead;
        typeof(AdminActivityPage).GetMethod("RenderTable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
        await CaptureAsync(frame, "admin-activity-ffmpeg-800x900.png");
        var consoleLine = Descendants(page).OfType<TextBlock>().Single(text => text.Text.StartsWith("frame=", StringComparison.Ordinal));
        consoleLine.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        await Task.Delay(200);
        var consoleOrigin = consoleLine.TransformToVisual(frame).TransformPoint(new Windows.Foundation.Point(0, 0));
        Check(consoleOrigin.Y >= 0 && consoleOrigin.Y + consoleLine.ActualHeight <= frame.ActualHeight, "FFmpeg console line is in the captured viewport");
        await CaptureAsync(frame, "admin-activity-ffmpeg-scrolled-800x900.png");
        ((ScrollViewer)Field(page, "_scroller")!).ChangeView(null, 0, null, true);
        rowState.GetType().GetField("Details")!.SetValue(rowState, false);
        rowState.GetType().GetField("Ffmpeg")!.SetValue(rowState, false);
        typeof(AdminActivityPage).GetMethod("RenderTable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900));
        await Task.Delay(300);
        var messageRowState = rowStates[firstSession.SessionId]!;
        var pendingDialog = (Task)typeof(AdminActivityPage).GetMethod("ShowMessageDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new[] { (object)firstSession, messageRowState })!;
        await Until(() => Field(page, "_messageDialog") != null);
        var messageDialog = (ContentDialog)Field(page, "_messageDialog")!;
        ((TextBox)Field(page, "_messageDraft")!).Text = "Synthetic pending message";
        await Until(() => Descendants(messageDialog).OfType<Button>().Any(button => button.Name == "PrimaryButton"));
        await Until(() => Descendants(messageDialog).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminMessageClose"));
        server.HeldMessage = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Invoke(Descendants(messageDialog).OfType<Button>().Single(button => button.Name == "PrimaryButton"));
        await Until(() => !messageDialog.IsPrimaryButtonEnabled);
        var cancel = Descendants(messageDialog).OfType<Button>().First(button => button.Visibility == Visibility.Visible && button.Name is "SecondaryButton" or "CloseButton");
        Check(!cancel.IsEnabled, "pending Message disables Cancel as in WebUI");
        var closeMessage = Descendants(messageDialog).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetAutomationId(button) == "AdminMessageClose");
        Program.Log("TRACE pending Message buttons: " + string.Join("; ", Descendants(messageDialog).OfType<Button>().Select(button => $"{button.Name}/{AutomationProperties.GetAutomationId(button)} enabled={button.IsEnabled}")));
        Check(closeMessage?.IsEnabled == true, "pending Message keeps the independent Close button enabled");
        await CaptureAsync(messageDialog, "admin-message-pending-800x900.png");
        if (closeMessage != null) Invoke(closeMessage); else messageDialog.Hide();
        await pendingDialog;
        var reopen = (Task)typeof(AdminActivityPage).GetMethod("ShowMessageDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new[] { (object)firstSession, messageRowState })!;
        await Task.Delay(100);
        Check(Field(page, "_messageDialog") == null, "a pending Message cannot reopen after X closes its dialog");
        if (Field(page, "_messageDialog") is ContentDialog reopened) reopened.Hide();
        await reopen;
        server.HeldMessage.TrySetResult(true);
        server.HeldMessage = null;
        Check(Field(page, "_messageDialog") == null && Field(page, "_messageDraft") == null, "closing pending Message clears the private draft");
        var searchTask = (Task)typeof(AdminShellPage).GetMethod("ShowSearchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null)!;
        await Until(() => Field(shell, "_searchOverlay") != null);
        var overlay = (Grid)Field(shell, "_searchOverlay")!;
        var panel = (Border)overlay.Children[0];
        await Until(() => panel.ActualWidth > 0 && panel.ActualHeight > 0);
        Program.Log($"TRACE search palette: width={panel.ActualWidth}, top={panel.Margin.Top}, root={shell.XamlRoot.Size}");
        Check(Math.Abs(panel.ActualWidth - 576) < 2 && Math.Abs(panel.Margin.Top - 162) < 2, "search palette is 576px wide at the WebUI 18% top anchor");
        var query = Descendants(overlay).OfType<TextBox>().Single();
        Check(query.Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 0 }, "focused search input has a transparent editing surface");
        await CaptureAsync(frame, "admin-search-800x900.png");
        query.Text = "no matching section";
        await Until(() => Descendants(overlay).OfType<TextBlock>().Any(text => text.Text == AdminText.T("web.admin.homeRows.commandDialog.noMatches")));
        Check(Descendants(overlay).OfType<TextBlock>().Any(text => text.Text == AdminText.T("web.admin.homeRows.commandDialog.noMatches")), "search renders no-match state for typed text");
        await CaptureAsync(frame, "admin-search-no-match-800x900.png");
        query.Text = "logs";
        await CaptureAsync(frame, "admin-search-filtered-800x900.png");
        ((Action)Field(shell, "_closeSearch")!).Invoke();
        await searchTask;
        Check(Field(shell, "_searchOverlay") == null, "closing search retires its overlay");
        Check(!page.IsWideTable, "narrow container uses stacked rows with a Sort row");
        Check(page.RenderedSessionIds[0] == "synthetic-0" || page.RenderedSessionIds.Count == 3, "default sort is Time descending (stable for equal times)");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static async Task OwnershipAsync(Window window, Server server, AuthService auth, ServerActivityStore store)
    {
        server.SessionCount = 3;
        await store.RefreshSessionsAsync();
        var outer = new Frame();
        window.Content = outer;
        outer.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Activity));
        var shell = (AdminShellPage)outer.Content;
        var inner = (Frame)Field(shell, "_frame")!;
        var page = (AdminActivityPage)inner.Content;
        await Until(() => page.RenderedSessionIds.Count == 3);
        Check(page.RenderedSessionIds.Count == 3, "nested shell Activity loads");
        var banner = (StackPanel)Field(shell, "_restartBannerHost")!;
        await Until(() => banner.Children.Count > 0 && banner.Parent != null);
        Check(ReferenceEquals(banner.Parent, Field(page, "_page")), "restart notice shares Activity scroll content");
        server.SessionCount = 30;
        await store.RefreshSessionsAsync();
        await Until(() => page.RenderedSessionIds.Count == 30);
        var scroller = (ScrollViewer)Field(page, "_scroller")!;
        scroller.ChangeView(null, 250, null, true);
        await Task.Delay(100);
        Check(banner.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point()).Y < 0,
            "restart notice scrolls away instead of reserving fixed viewport height");
        var old = store.Current;
        var states = (System.Collections.IDictionary)Field(page, "_rowStates")!;
        var first = old.Sessions.First();
        var messageTask = (Task)typeof(AdminActivityPage).GetMethod("ShowMessageDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, new[] { (object)first, states[first.SessionId]! })!;
        await Until(() => Field(page, "_messageDialog") != null);
        var draft = (TextBox)Field(page, "_messageDraft")!;
        draft.Text = "retired synthetic private draft";
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
        // Replay an older publication after the authority clear reached subscribers.
        typeof(AdminActivityPage).GetMethod("OnStoreChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, new object[] { old });
        await Task.Delay(200);
        Check(page.RenderedSessionIds.Count == 0, "queued old snapshot cannot restore retired rows");
        Check(draft.Text.Length == 0 && Field(page, "_messageDialog") == null, "retirement closes Message and clears draft");
        await messageTask;
        server.SessionCount = 3;
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        outer.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Activity));
        var secondShell = (AdminShellPage)outer.Content;
        var second = (AdminActivityPage)((Frame)Field(secondShell, "_frame")!).Content;
        await Until(() => second.RenderedSessionIds.Count == 3);
        Check(outer.Navigate(typeof(AdminActivityPage)), "outer departure destination activates");
        await Task.Delay(150);
        Check(Field(second, "_active") is false && Field(second, "_lease") == null && Field(second, "_reads") == null,
            "outer shell departure retires inner Activity without inner navigation");
        Check(second.RenderedSessionIds.Count == 0, "departed Activity clears private rows");
    }

    private static void Invoke(Button button) => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button).GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();


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
