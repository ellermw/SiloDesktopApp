using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Channels;
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
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class AdminMonitorLogsNativeFixture
{
    private static readonly List<string> Failures = [];
    private const string Log = "Synthetic playback started";
    private const string AppEntry = "{\"id\":\"41\",\"timestamp\":\"2026-11-14T12:00:00Z\",\"level\":\"info\",\"component\":\"ffmpeg\",\"message\":\"Synthetic playback started\",\"node_id\":\"synthetic-node\",\"playback_session_id\":\"synthetic-playback\",\"request_id\":\"req-1\",\"attrs\":{\"status\":200,\"duration_ms\":12,\"path\":\"/api/v2/playback\"}}";
    private const string AuditEntry = "{\"id\":\"42\",\"timestamp\":\"2026-11-14T12:00:01Z\",\"method\":\"PATCH\",\"path\":\"/api/v2/profiles/synthetic\",\"status_code\":200,\"action\":\"profile.updated\",\"user_id\":\"1\",\"target_type\":\"profile\",\"target_id\":\"synthetic\",\"changes\":[{\"field\":\"pin\",\"before\":\"false\",\"after\":\"true\"}]}";
    private const string Report = "{\"id\":\"report-1\",\"short_id\":\"ABC123\",\"user_id\":\"1\",\"profile_id\":\"synthetic\",\"state\":\"ready\",\"captured_at\":\"2026-11-14T11:00:00Z\",\"received_at\":\"2026-11-14T12:00:00Z\",\"report_type\":\"crash\",\"platform\":\"android-tv\",\"app_version\":\"1.2.3\",\"app_build\":\"123\",\"crash_summary\":\"Synthetic renderer stopped\",\"playback_session_ids\":[\"synthetic-playback\"],\"blob_bytes\":2048,\"uncompressed_bytes\":8192}";
    internal static async Task RunAsync(Window window)
    {
        Failures.Clear(); var server = new Server(); using var http = new HttpClient(server); var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400); auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" }); auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth); var api = new AdminMonitorApi(client); var sockets = new List<Socket>(); var stream = new LogsStreamClient(api, () => { var socket = new Socket(server); sockets.Add(socket); return socket; });
        using var activity = new ServerActivityStore(api, authority, new IdleRealtime());
        await using var services = new ServiceCollection().AddSingleton(activity).AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(api).AddSingleton(stream).AddSingleton(new LoggingDisclosurePreferences()).AddSingleton(new SettingsService(Program.ResultDirectory)).BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!; var previousServices = servicesField.GetValue(null); var previousContent = window.Content; servicesField.SetValue(null, services);
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        var frame = new Frame { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] }; window.Content = frame;
        try
        {
            frame.Navigate(typeof(AdminLogsPage), new AdminRouteRequest(AdminRoute.ServerLog, new Dictionary<string, string> { ["playback_session_id"] = "synthetic-playback", ["focus"] = "playback" })); var logs = (AdminLogsPage)frame.Content;
            await Until(() => logs.Controller?.State.AppRows.Count == 1); Check(logs.Controller?.State.Connection == "live", "Server Log accepted the captured ticket/socket snapshot");
            Check(ReferenceEquals(((Grid)logs.Content).Children.OfType<ScrollViewer>().Single().Content, logs.MonitorScrollContent), "Server Log restart insertion seam is in the main scroll body");
            var filter = Find<TextBox>(logs, "LogsFilter_message"); filter.Focus(FocusState.Programmatic); filter.Text = "Synthetic"; await Until(() => logs.Controller?.State.AppRows.Count == 1 && sockets.Count >= 2); Check(ReferenceEquals(filter, Find<TextBox>(logs, "LogsFilter_message")), "filters remain mounted during stream replacement");
            await CaptureSizes(window, frame, "admin-logs-populated");
            Invoke(Find<Button>(logs, "LogsHistory_browse")); await Until(() => logs.Controller?.State.History == true && logs.Controller.State.Loading == false); Check(logs.Controller?.State.Page == 1, "history begins at a fresh first page"); await Capture(frame, "admin-logs-history.png");
            Invoke(Find<Button>(logs, "LogsHistory_returnToLive")); await Until(() => logs.Controller?.State.History == false && logs.Controller.State.AppRows.Count == 1);
            await Task.Delay(250); logs.UpdateLayout();
            var row = All(logs).OfType<Button>().First(button => AutomationProperties.GetName(button) == Log); Invoke(row); await Task.Delay(100); Check(All(logs).OfType<TextBlock>().Any(text => text.Text.Contains("Attributes")), "operational detail sheet renders attributes"); await Capture(frame, "admin-logs-detail.png"); Invoke(Find<Button>(logs, "LogsDetailClose"));
            Invoke(Find<Button>(logs, "LogsAuditTab")); await Until(() => logs.Controller?.State.AuditRows.Count == 1); Check(All(logs).OfType<TextBlock>().Any(text => text.Text == "pin: set"), "audit PIN markers use pinned safe wording"); await Capture(frame, "admin-logs-audit.png");
            foreach (var mode in new[] { "empty", "error", "loading" }) { server.Mode = mode; _ = logs.Controller!.ReconnectAsync(); await Task.Delay(400); await Capture(frame, "admin-logs-" + mode + ".png"); } server.Mode = "populated";
            frame.Navigate(typeof(AdminDiagnosticsPage), new AdminRouteRequest(AdminRoute.ClientDiagnostics)); var diagnostics = (AdminDiagnosticsPage)frame.Content; await Until(() => diagnostics.Controller?.State.Reports.Count == 1); Check(sockets.All(socket => socket.Aborted), "unloading Server Log aborts all sockets");
            Check(ReferenceEquals(((Grid)diagnostics.Content).Children.OfType<ScrollViewer>().Single().Content, diagnostics.MonitorScrollContent), "Diagnostics restart insertion seam is in the main scroll body");
            await CaptureSizes(window, frame, "admin-diagnostics-populated"); await diagnostics.Controller!.SelectAsync("report-1"); await Task.Delay(100); Check(All(diagnostics).OfType<TextBlock>().Any(text => text.Text.Contains("Synthetic Device")), "diagnostic detail renders device manifest"); await Capture(frame, "admin-diagnostics-ready.png");
            Invoke(Find<Button>(diagnostics, "LogsDetailClose")); server.Receiving = true; await diagnostics.Controller.SelectAsync("report-1"); await Task.Delay(100); Check(!Find<Button>(diagnostics, "DiagnosticsDownload").IsEnabled, "receiving diagnostic report cannot download"); await Capture(frame, "admin-diagnostics-receiving.png"); Invoke(Find<Button>(diagnostics, "LogsDetailClose")); server.Receiving = false;
            var user = Find<TextBox>(diagnostics, "DiagnosticsFilter_user"); user.Text = "1e2"; Invoke(Find<Button>(diagnostics, "DiagnosticsApply")); await Task.Delay(100); Check(diagnostics.Controller.State.Query.UserId == null, "invalid filter does not replace applied query"); user.Text = "12"; Invoke(Find<Button>(diagnostics, "DiagnosticsApply")); await Until(() => diagnostics.Controller.State.Query.UserId == "12");
            foreach (var mode in new[] { "empty", "error", "loading" }) { server.Mode = mode; _ = diagnostics.Controller.RefreshAsync(); await Task.Delay(250); await Capture(frame, "admin-diagnostics-" + mode + ".png"); } server.Mode = "populated";
            frame.Navigate(typeof(AdminLoggingSettingsPage), new AdminRouteRequest(AdminRoute.LoggingSettings)); var logging = (AdminLoggingSettingsPage)frame.Content; await Until(() => logging.Controller?.State.Loaded == true);
            Check(ReferenceEquals(((Grid)logging.Content).Children.OfType<ScrollViewer>().Single().Content, logging.MonitorScrollContent), "Logging restart insertion seam is in the main scroll body");
            await CaptureSizes(window, frame, "admin-logging-clean"); var days = Find<TextBox>(logging, "LoggingField_opslog.retention_days"); days.Text = "14"; await Task.Delay(100); Check(logging.Controller!.State.DirtyKeys.Contains("opslog.retention_days"), "retention control registers dirty state"); Invoke(Find<Button>(logging, "LoggingAdvanced_server")); await Task.Delay(100); Find<TextBox>(logging, "LoggingField_server.log_quiet").Text = "metadata, scanner"; await Task.Delay(100); Check(logging.Controller.State.ServerAdvancedOpen, "dirty advanced fields remain discoverable"); await Capture(frame, "admin-logging-dirty.png");
            Invoke(Find<Button>(logging, "LoggingSave")); await Until(() => !logging.Controller.State.Saving && logging.Controller.State.DirtyKeys.Count == 0); Check(server.BatchWrites == 1, "Logging saves all dirty values in one batch");
            await Until(() => days.Text == "14" && logging.Controller!.State.DirtyKeys.Count == 0);
            days.Text = "21"; await Until(() => logging.Controller!.State.DirtyKeys.Contains("opslog.retention_days") && logging.Controller.State.Values["opslog.retention_days"] == "21"); var leave = logging.CanLeaveAsync(); await Task.Delay(100); auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" }); Check(!await leave, "retired authority cannot accept unsaved confirmation"); await Task.Delay(100); Check(logging.Controller.State.Values.Count == 0, "admin loss clears every sensitive draft");
            await Capture(frame, "admin-logging-retired.png");
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
            foreach (var route in new[] { AdminRoute.ServerLog, AdminRoute.ClientDiagnostics, AdminRoute.LoggingSettings })
            {
                frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(route));
                await Task.Delay(400);
                if (route == AdminRoute.LoggingSettings)
                {
                    var clean = All(frame).OfType<AdminLoggingSettingsPage>().Single();
                    Check(clean.Controller!.State.DirtyKeys.Count == 0, "fresh Logging values do not become unsaved edits");
                    if (clean.Controller.State.ServerAdvancedOpen) Invoke(Find<Button>(clean, "LoggingAdvanced_server"));
                    if (clean.Controller.State.RetentionAdvancedOpen) Invoke(Find<Button>(clean, "LoggingAdvanced_retention"));
                    await Until(() => !clean.Controller.State.ServerAdvancedOpen && !clean.Controller.State.RetentionAdvancedOpen);
                }
                await CaptureSizes(window, frame, "admin-shell-" + route);
            }
            var shell = (AdminShellPage)frame.Content;
            var settingsPage = All(shell).OfType<AdminLoggingSettingsPage>().Single();
            await Until(() => settingsPage.Controller?.State.Values.Count > 0);
            Find<TextBox>(settingsPage, "LoggingField_opslog.retention_days").Text = "23";
            await Until(() => shell.RequiresLeaveConfirmation);
            shell.Navigate(new AdminRouteRequest(AdminRoute.ServerLog));
            ContentDialog? CurrentDialog() => (ContentDialog?)typeof(AdminLoggingSettingsPage).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settingsPage);
            await Until(() => CurrentDialog() != null);
            Check(All(shell).OfType<AdminLoggingSettingsPage>().Any(), "inner navigation waits for dirty Logging confirmation");
            CurrentDialog()!.Hide();
            await Task.Delay(150);
            Check(shell.RequiresLeaveConfirmation, "cancelled navigation preserves Logging draft");
            shell.Navigate(new AdminRouteRequest(AdminRoute.ServerLog));
            await Until(() => CurrentDialog() != null);
            var dialog = CurrentDialog()!;
            await Until(() => All(dialog).OfType<Button>().Any(b => b.Name == "PrimaryButton"));
            Invoke(All(dialog).OfType<Button>().Single(b => b.Name == "PrimaryButton"));
            await Until(() => All(shell).OfType<AdminLogsPage>().Any());
            Check(!shell.RequiresLeaveConfirmation, "discard confirmation completes inner navigation");
        }
        finally { frame.Content = null; window.Content = previousContent; servicesField.SetValue(null, previousServices); }
        if (Failures.Count > 0) throw new InvalidOperationException(string.Join("; ", Failures));
        Program.Log("PASS: Admin Monitor Logs native synthetic fixture completed; no live interoperability or parity certification.");
    }
    private static IEnumerable<DependencyObject> All(DependencyObject root) { yield return root; for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) foreach (var element in All(VisualTreeHelper.GetChild(root, index))) yield return element; }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => All(root).OfType<T>().First(element => AutomationProperties.GetAutomationId(element) == id);
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Until(Func<bool> condition) { for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(25); Check(condition(), "bounded native state transition completed"); }
    private static void Check(bool condition, string label) { Program.Log((condition ? "PASS: " : "FAIL: ") + label); if (!condition) Failures.Add(label); }
    private static async Task CaptureSizes(Window window, FrameworkElement element, string prefix) { foreach (var (width, height) in new[] { (1440, 900), (1280, 800), (1024, 768), (1023, 768), (800, 900) }) { window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height)); await Task.Delay(200); await Capture(element, prefix + "-" + width + "x" + height + ".png"); } }
    private static async Task Capture(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(100); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element); Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered"); var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var output = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
    private sealed class IdleRealtime : IAdminRealtimeChannel
    {
        public System.Net.WebSockets.WebSocketState CurrentState => System.Net.WebSockets.WebSocketState.Open;
        public event Action<string, System.Text.Json.JsonElement>? SnapshotReceived { add { } remove { } }
        public event Action<string, string, System.Text.Json.JsonElement>? EventReceived { add { } remove { } }
        public event Action<System.Net.WebSockets.WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels) => new Lease();
        public bool TryGetLatestSnapshot(string channel, out System.Text.Json.JsonElement snapshot) { snapshot = default; return false; }
        private sealed class Lease : IDisposable { public void Dispose() { } }
    }
    private sealed class Server : HttpMessageHandler
    {
        public string Mode = "populated";
        public bool Receiving;
        public int BatchWrites;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Mode == "loading") await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Mode == "error") return Json("{}", HttpStatusCode.ServiceUnavailable);
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put) { BatchWrites++; var result = Json("{\"values\":{\"opslog.retention_days\":\"14\",\"server.log_quiet\":\"metadata, scanner\"},\"restart_required\":false}"); result.Headers.ETag = new("\"two\""); return result; }
            var body = path switch
            {
                "/api/v2/admin/logs/ws-ticket" => "{\"ticket\":\"" + new string('c', 43) + "\",\"expires_in\":30,\"max_connection_seconds\":300,\"protocol\":\"silo.admin-logs.v2\"}",
                "/api/v2/admin/logs/app" => "{\"items\":[" + (Mode == "empty" ? "" : AppEntry) + "],\"page\":{\"has_more\":false}}",
                "/api/v2/diagnostics/capabilities" => "{\"status\":\"storage_unavailable\",\"state\":\"enabled\",\"allowed\":true,\"retention_days\":14,\"max_bundle_bytes\":1048576}",
                "/api/v2/admin/diagnostics/reports" => "{\"items\":[" + (Mode == "empty" ? "" : Report) + "],\"page\":{\"has_more\":false}}",
                "/api/v2/admin/diagnostics/reports/report-1" => Report[..^1].Replace("\"ready\"", Receiving ? "\"receiving\"" : "\"ready\"") + ",\"manifest\":{\"device_summary\":{\"manufacturer\":\"Synthetic\",\"model\":\"Device\",\"os\":\"Android 16\",\"form_factor\":\"tv\"},\"future_extension\":{\"retained\":true}}}",
                "/api/v2/admin/settings/effective" => "{\"server.log_level\":\"info\",\"server.log_quiet\":\"\",\"opslog.retention_days\":\"7\",\"opslog.max_size_mb\":\"1024\",\"opslog.max_rows\":\"1000000\",\"policy.decision_log_retention_days\":\"7\",\"policy.decision_log_verbosity\":\"digest\"}",
                "/api/v2/admin/settings/restart-keys" => "{\"keys\":[],\"prefixes\":[]}",
                _ => "{}"
            };
            var response = Json(body); if (path.EndsWith("/effective")) response.Headers.ETag = new("\"one\""); return response;
        }
        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
    private sealed class Socket(Server server) : ILogsSocket
    {
        private readonly Channel<string> _frames = Channel.CreateBounded<string>(8);
        private readonly CancellationTokenSource _abort = new();
        public bool Aborted;
        public Task ConnectAsync(Uri address, IReadOnlyList<string> protocols, CancellationToken ct) { var audit = address.Query.Contains("stream=audit"); _frames.Writer.TryWrite("{\"type\":\"snapshot\",\"stream\":\"" + (audit ? "audit" : "app") + "\",\"entries\":[" + (server.Mode == "empty" ? "" : audit ? AuditEntry : AppEntry) + "]}"); return Task.CompletedTask; }
        public async Task<string?> ReceiveAsync(CancellationToken ct) { using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _abort.Token); return await _frames.Reader.ReadAsync(linked.Token); }
        public void Abort() { Aborted = true; _abort.Cancel(); }
        public void Dispose() { Aborted = true; _abort.Cancel(); }
    }
}
