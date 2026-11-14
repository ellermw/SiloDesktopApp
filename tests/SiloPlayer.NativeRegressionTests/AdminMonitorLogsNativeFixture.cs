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
using SiloPlayer.Helpers;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static partial class AdminMonitorLogsNativeFixture
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
        var navigation = new NavigationService();
        await using var services = new ServiceCollection().AddSingleton(navigation).AddSingleton(activity).AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(api).AddSingleton(stream).AddSingleton(new LoggingDisclosurePreferences()).AddSingleton(new SettingsService(Program.ResultDirectory)).BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!; var previousServices = servicesField.GetValue(null); var previousContent = window.Content; servicesField.SetValue(null, services);
        new ThemeService(new SettingsService(Program.ResultDirectory), new SettingsApi(client)).ApplyTheme("midnight-cinema");
        var frame = new Frame { Background = (Brush)Application.Current.Resources["AppBackgroundBrush"] }; window.Content = frame; navigation.Frame = frame;
        try
        {
            frame.Navigate(typeof(AdminLogsPage), new AdminRouteRequest(AdminRoute.ServerLog, new Dictionary<string, string> { ["playback_session_id"] = "synthetic-playback", ["focus"] = "playback" })); var logs = (AdminLogsPage)frame.Content;
            await Until(() => logs.Controller?.State.AppRows.Count == 1); Check(logs.Controller?.State.Connection == "live", "Server Log accepted the captured ticket/socket snapshot");
            Check(ReferenceEquals(((Grid)logs.Content).Children.OfType<ScrollViewer>().Single().Content, logs.MonitorScrollContent), "Server Log restart insertion seam is in the main scroll body");
            var filter = Find<TextBox>(logs, "LogsFilter_message"); filter.Focus(FocusState.Programmatic); filter.Text = "Synthetic"; await Until(() => logs.Controller?.State.AppRows.Count == 1 && logs.Controller.State.Query.Message == "Synthetic" && sockets.Count >= 2); Check(ReferenceEquals(filter, Find<TextBox>(logs, "LogsFilter_message")), "filters remain mounted during stream replacement");
            await CaptureSizes(window, frame, "admin-logs-populated");
            Invoke(Find<Button>(logs, "LogsHistory_browse")); await Until(() => logs.Controller?.State.History == true && logs.Controller.State.Loading == false); Check(logs.Controller?.State.Page == 1, "history begins at a fresh first page"); await Capture(frame, "admin-logs-history.png");
            Invoke(Find<Button>(logs, "LogsHistory_returnToLive")); await Until(() => logs.Controller?.State.History == false && logs.Controller.State.AppRows.Count == 1);
            await Task.Delay(250); logs.UpdateLayout();
            var row = All(logs).OfType<Button>().First(button => AutomationProperties.GetName(button) == Log); Invoke(row); await Task.Delay(100); Check(All(logs).OfType<TextBlock>().Any(text => text.Text.Contains("Attributes")), "operational detail sheet renders attributes"); await Capture(frame, "admin-logs-detail.png"); Invoke(Find<Button>(logs, "LogsDetailClose"));
            Invoke(Find<Button>(logs, "LogsAuditTab")); await Until(() => logs.Controller?.State.AuditRows.Count == 1); Check(All(logs).OfType<TextBlock>().Any(text => text.Text == "pin: set"), "audit PIN markers use pinned safe wording"); await Capture(frame, "admin-logs-audit.png");
            Program.Log($"Synthetic pre-retirement filter: {logs.Controller!.State.Query.Message}; input: {filter.Text}");
            var logsOwner = logs.Controller!.State.Owner!;
            var oldSockets = sockets.ToArray();
            client.InvalidateAccessContext();
            await Until(() => logs.Controller.State.Owner is { IsCurrent: true } owner && !ReferenceEquals(owner, logsOwner) && logs.Controller.State.AuditRows.Count == 1);
            Check(!logsOwner.IsCurrent && oldSockets.All(socket => socket.Aborted), "log access invalidation retires the old owner and sockets");
            Check(logs.Controller.State.Stream == "audit" && logs.Controller.State.Query.Message == "Synthetic", $"log recapture retains the selected tab and filters (stream={logs.Controller.State.Stream}, message={logs.Controller.State.Query.Message}, input={filter.Text})");
            foreach (var mode in new[] { "empty", "error", "loading" }) { server.Mode = mode; _ = logs.Controller!.ReconnectAsync(); await Task.Delay(400); await Capture(frame, "admin-logs-" + mode + ".png"); } server.Mode = "populated";
            frame.Navigate(typeof(AdminDiagnosticsPage), new AdminRouteRequest(AdminRoute.ClientDiagnostics)); var diagnostics = (AdminDiagnosticsPage)frame.Content; await Until(() => diagnostics.Controller?.State.Reports.Count == 1); Check(sockets.All(socket => socket.Aborted), "unloading Server Log aborts all sockets");
            Check(ReferenceEquals(((Grid)diagnostics.Content).Children.OfType<ScrollViewer>().Single().Content, diagnostics.MonitorScrollContent), "Diagnostics restart insertion seam is in the main scroll body");
            await CaptureSizes(window, frame, "admin-diagnostics-populated"); await diagnostics.Controller!.SelectAsync("report-1"); await Task.Delay(100); Check(All(diagnostics).OfType<TextBlock>().Any(text => text.Text.Contains("Synthetic Device")), "diagnostic detail renders device manifest"); await Capture(frame, "admin-diagnostics-ready.png");
            Invoke(Find<Button>(diagnostics, "LogsDetailClose")); server.Receiving = true; await diagnostics.Controller.SelectAsync("report-1"); await Task.Delay(100); Check(!Find<Button>(diagnostics, "DiagnosticsDownload").IsEnabled, "receiving diagnostic report cannot download"); await Capture(frame, "admin-diagnostics-receiving.png"); Invoke(Find<Button>(diagnostics, "LogsDetailClose")); server.Receiving = false;
            var user = Find<TextBox>(diagnostics, "DiagnosticsFilter_user"); user.Text = "1e2"; Invoke(Find<Button>(diagnostics, "DiagnosticsApply")); await Task.Delay(100); Check(diagnostics.Controller.State.Query.UserId == null, "invalid filter does not replace applied query"); user.Text = "12"; Invoke(Find<Button>(diagnostics, "DiagnosticsApply")); await Until(() => diagnostics.Controller.State.Query.UserId == "12");
            var diagnosticsOwner = diagnostics.Controller.State.Owner!;
            client.InvalidateAccessContext();
            await Until(() => diagnostics.Controller.State.Owner is { IsCurrent: true } owner && !ReferenceEquals(owner, diagnosticsOwner) && diagnostics.Controller.State.Reports.Count == 1);
            Check(!diagnosticsOwner.IsCurrent && diagnostics.Controller.State.Query.UserId == "12", "diagnostics recaptures and reloads the applied query after access invalidation");
            foreach (var mode in new[] { "empty", "error", "loading" }) { server.Mode = mode; _ = diagnostics.Controller.RefreshAsync(); await Task.Delay(250); await Capture(frame, "admin-diagnostics-" + mode + ".png"); } server.Mode = "populated";
            server.Mode = "loading"; frame.Navigate(typeof(AdminLoggingSettingsPage), new AdminRouteRequest(AdminRoute.LoggingSettings)); await Task.Delay(100); Check(All(frame).OfType<Border>().Count(item => Math.Abs(item.Height - 40) < 0.1) >= 5, "Logging first load uses five skeleton rows"); await Capture(frame, "admin-logging-loading.png"); server.Mode = "populated";
            frame.Navigate(typeof(AdminLoggingSettingsPage), new AdminRouteRequest(AdminRoute.LoggingSettings)); var logging = (AdminLoggingSettingsPage)frame.Content; await Until(() => logging.Controller?.State.Loaded == true);
            Check(ReferenceEquals(((Grid)logging.Content).Children.OfType<ScrollViewer>().Single().Content, logging.MonitorScrollContent), "Logging restart insertion seam is in the main scroll body");
            var loggingOwner = logging.Controller!.State.Owner!;
            logging.Controller.Set("opslog.retention_days", "99");
            client.SetProfile("p1", "synthetic-replaced-proof");
            Check(logging.Controller.State.DirtyKeys.Count == 1 && !loggingOwner.IsCurrent, "silent proof replacement leaves a stale draft before notification");
            Check(await logging.CanLeaveAsync(), "a stale owner cannot block leaving before retirement notification");
            client.InvalidateAccessContext();
            await Until(() => logging.Controller.State.Owner is { IsCurrent: true } owner && !ReferenceEquals(owner, loggingOwner) && logging.Controller.State.Loaded);
            Check(!loggingOwner.IsCurrent && logging.Controller.State.DirtyKeys.Count == 0 && logging.Controller.State.Values["opslog.retention_days"] == "7", "logging recaptures and reloads without reviving retired drafts");
            logging.Controller.Set("opslog.retention_days", "99");
            var verificationOwner = logging.Controller.State.Owner!;
            var verificationContext = verificationOwner.Context;
            var verificationLeave = logging.CanLeaveAsync();
            await Task.Delay(100);
            auth.HandleProfileVerificationRequired(new(verificationContext.AuthenticationGeneration, verificationContext.RequestContextGeneration, verificationContext.ProfileId));
            Check(await verificationLeave, "profile verification releases navigation while an unsaved dialog is open");
            await Until(() => logging.Controller.State.Loaded && logging.Controller.State.Owner is { IsCurrent: true } owner && !ReferenceEquals(owner, verificationOwner) && logging.SettingsHealth.DirtyCount == 0);
            Check(verificationOwner.Token.IsCancellationRequested && logging.Controller.State.DirtyKeys.Count == 0 && logging.SettingsHealth.DirtyCount == 0, "profile verification retires the draft and clears unsaved health without reviving edits");
            await CaptureSizes(window, frame, "admin-logging-clean"); var days = Find<TextBox>(logging, "LoggingField_opslog.retention_days"); days.Text = "14"; await Task.Delay(100); Check(logging.Controller!.State.DirtyKeys.Contains("opslog.retention_days"), "retention control registers dirty state"); Invoke(Find<Button>(logging, "LoggingAdvanced_server")); await Task.Delay(100); Find<TextBox>(logging, "LoggingField_server.log_quiet").Text = "metadata, scanner"; await Task.Delay(100); Check(logging.Controller.State.ServerAdvancedOpen, "dirty advanced fields remain discoverable"); await Capture(frame, "admin-logging-dirty.png");
            Invoke(Find<Button>(logging, "LoggingSave")); await Until(() => !logging.Controller.State.Saving && logging.Controller.State.DirtyKeys.Count == 0); Check(server.BatchWrites == 1, "Logging saves all dirty values in one batch");
            await Until(() => days.Text == "14" && logging.Controller!.State.DirtyKeys.Count == 0);
            days.Text = "21"; await Until(() => logging.Controller!.State.DirtyKeys.Contains("opslog.retention_days") && logging.Controller.State.Values["opslog.retention_days"] == "21"); var leave = logging.CanLeaveAsync(); await Task.Delay(100); auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" }); Check(await leave, "authority retirement releases unsaved navigation without accepting the retired draft"); await Task.Delay(100); Check(logging.Controller.State.Values.Count == 0, "admin loss clears every sensitive draft");
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
                if (route is AdminRoute.ServerLog or AdminRoute.ClientDiagnostics)
                {
                    var rowName = route == AdminRoute.ServerLog ? Log : SiloPlayer.Core.Services.Admin.LogsText.T("web.admin.diagnostics.row.openLabel", ("id", "ABC123"));
                    Invoke(All(frame).OfType<Button>().Single(button => AutomationProperties.GetName(button) == rowName));
                    await Until(() => All(frame).OfType<FrameworkElement>().Any(item => item.GetType().Name == "LogsDetailSheet" && item.Visibility == Visibility.Visible));
                    await CaptureSizes(window, frame, "admin-shell-" + route + "-detail");
                    Invoke(Find<Button>(frame, "LogsDetailClose"));
                }
                if (route == AdminRoute.LoggingSettings)
                {
                    var expanded = All(frame).OfType<AdminLoggingSettingsPage>().Single();
                    Invoke(Find<Button>(expanded, "LoggingAdvanced_server")); Invoke(Find<Button>(expanded, "LoggingAdvanced_retention"));
                    await CaptureSizes(window, frame, "admin-shell-LoggingSettings-expanded");
                }
            }
            var shell = (AdminShellPage)frame.Content;
            var settingsPage = All(shell).OfType<AdminLoggingSettingsPage>().Single();
            await Until(() => settingsPage.Controller?.State.Values.Count > 0);
            Find<TextBox>(settingsPage, "LoggingField_opslog.retention_days").Text = "23";
            await Until(() => shell.RequiresLeaveConfirmation);
            var otherDialog = new ContentDialog { XamlRoot = shell.XamlRoot, Content = "Another admin dialog", CloseButtonText = "Cancel" };
            var otherDialogTask = otherDialog.ShowAsync();
            await Task.Delay(100);
            try
            {
                Check(!await shell.CanLeaveAsync(), "a dialog collision cancels leaving without throwing or discarding edits");
                Check(shell.RequiresLeaveConfirmation, "a dialog collision preserves the Logging draft");
            }
            finally { otherDialog.Hide(); await otherDialogTask; }

            shell.TryOpenSearch();
            FrameworkElement? SearchDialog() => (typeof(AdminShellPage).GetField("_searchOverlay", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(shell) ?? typeof(AdminShellPage).GetField("_searchDialog", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(shell)) as FrameworkElement;
            await Until(() => SearchDialog() != null && All(SearchDialog()!).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Activity"));
            Invoke(All(SearchDialog()!).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Activity"));
            ContentDialog? CurrentDialog() => (ContentDialog?)typeof(AdminLoggingSettingsPage).GetField("_dialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settingsPage);
            await Until(() => CurrentDialog() != null);
            Check(CurrentDialog() != null, "search closes before Logging opens its leave confirmation");
            CurrentDialog()?.Hide();
            await Task.Delay(150);
            Check(shell.RequiresLeaveConfirmation, "cancelling a search departure preserves Logging edits");
            shell.Navigate(new AdminRouteRequest(AdminRoute.ServerLog));
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
            if (Environment.GetEnvironmentVariable("SILO_LOGS_CAPTURE_STATES") == "1")
                await RunStateCoverageAsync(window, frame, server, client, auth, authority);
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
    private static async Task CaptureSizes(Window window, FrameworkElement element, string prefix)
    {
        foreach (var (width, height) in (prefix.StartsWith("admin-shell", StringComparison.Ordinal) ? new[] { (1440, 900), (1280, 800), (1024, 768), (1023, 768), (800, 900), (640, 900), (639, 900), (560, 900) } : new[] { (1440, 900), (1280, 800), (1024, 768), (1023, 768), (800, 900) }))
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height)); await Task.Delay(200);
            if (prefix == "admin-shell-ClientDiagnostics")
            {
                var user = Find<TextBox>(element, "DiagnosticsFilter_user");
                Check(All((DependencyObject)user.Parent).OfType<TextBlock>().Any(t => t.Text == "USER"), $"{width}px Diagnostics User label resolves correctly");
                Check(user.PlaceholderText == "Any", "Diagnostics User placeholder matches reference");
                Check(!Find<Button>(element, "DiagnosticsClear").IsEnabled, "Diagnostics Clear is disabled without applied filters");
                if (width == 1440)
                {
                    var platform = Find<ComboBox>(element, "web.admin.diagnostics.filters.platform");
                    var reportType = Find<ComboBox>(element, "web.admin.diagnostics.filters.reportType");
                    var from = Find<Button>(element, "DiagnosticsFrom");
                    Check(Math.Abs(platform.ActualWidth - from.ActualWidth) < 2 && Math.Abs(platform.ActualWidth - reportType.ActualWidth) < 2, "Diagnostics flex fields share width when all minimum widths fit");
                }
                if (width == 800)
                {
                    var subtitle = All(element).OfType<TextBlock>().Single(t => t.Text == SiloPlayer.Core.Services.Admin.LogsText.T("web.admin.diagnostics.page.subtitle"));
                    Check(subtitle.ActualHeight <= 25, "800px Diagnostics header wraps actions instead of squeezing subtitle");
                }
            }
            if (prefix == "admin-shell-ServerLog" && width == 800)
            {
                var message = All(element).OfType<TextBlock>().Single(t => t.Text == Log);
                var natural = new TextBlock { Text = Log, FontFamily = message.FontFamily, FontSize = message.FontSize };
                natural.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                Check(message.ActualWidth >= natural.DesiredSize.Width - 2, "800px log message fits as in the reference table");
            }
            if (prefix == "admin-shell-ClientDiagnostics-detail" && width == 800)
            {
                var download = Find<Button>(element, "DiagnosticsDownload");
                var glyph = All(download).OfType<Microsoft.UI.Xaml.Shapes.Path>().Single();
                Check(glyph.Stroke is SolidColorBrush actual && actual.Color == ((SolidColorBrush)Application.Current.Resources["AccentForegroundBrush"]).Color,
                    "primary download icon uses the contrasting primary foreground");
            }
            if (prefix.EndsWith("-detail", StringComparison.Ordinal))
            {
                var overlay = All(element).OfType<FrameworkElement>().Single(item => item.GetType().Name == "LogsDetailSheet" && item.Visibility == Visibility.Visible);
                var origin = overlay.TransformToVisual(element).TransformPoint(new Windows.Foundation.Point(0, 0));
                Check(Math.Abs(origin.X) < 1 && Math.Abs(origin.Y) < 1 && Math.Abs(overlay.ActualWidth - element.ActualWidth) < 1, $"{width}px details dim the whole Admin shell");
            }
            await Capture(element, prefix + "-" + width + "x" + height + ".png");
        }
    }
    private static async Task<bool> Capture(FrameworkElement element, string name, bool allowUnavailableCalendar = false)
    {
        element.UpdateLayout(); await Task.Delay(100); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        if ((bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) && allowUnavailableCalendar && All(element).OfType<CalendarView>().FirstOrDefault() is { } calendar)
        {
            Program.Log($"GAP: {name}: popup RenderTargetBitmap is {bitmap.PixelWidth}x{bitmap.PixelHeight}; probing CalendarView content ({calendar.ActualWidth}x{calendar.ActualHeight}, loaded={calendar.IsLoaded})");
            await bitmap.RenderAsync(calendar);
            if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
            {
                Program.Log($"GAP: {name}: native calendar popup pixels unavailable in the offscreen RenderTargetBitmap fixture; functional calendar assertions are separate.");
                await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, name + ".unavailable.json"), System.Text.Json.JsonSerializer.Serialize(new { name, reason = "Offscreen native CalendarView renders zero pixels", calendar.ActualWidth, calendar.ActualHeight, calendar.IsLoaded }));
                return false;
            }
            await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, name + ".capture.json"), System.Text.Json.JsonSerializer.Serialize(new { scope = "CalendarView only; popup root produced zero pixels", calendar.ActualWidth, calendar.ActualHeight }));
            element = calendar; // This image is only the CalendarView, not the entire popup.
        }
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered"); var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var output = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
        var textGeometry = new List<object>();
        foreach (var text in All(element).OfType<TextBlock>().Where(text => text.ActualWidth > 0 && text.ActualHeight > 0))
        {
            try
            {
                var origin = text.TransformToVisual(element).TransformPoint(new Windows.Foundation.Point(0, 0));
                textGeometry.Add(new { text.Text, Rect = new[] { origin.X, origin.Y, text.ActualWidth, text.ActualHeight }, text.FontSize, Weight = text.FontWeight.Weight, text.LineHeight, Family = text.FontFamily.Source });
            }
            catch (System.Runtime.InteropServices.COMException) { }
        }
        await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, Path.ChangeExtension(name, ".json")), System.Text.Json.JsonSerializer.Serialize(textGeometry));
        return true;
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
        public ReferenceData? Reference;
        public Socket? LatestSocket;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Reference != null) return await Reference.SendAsync(request, cancellationToken);
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
        public Task ConnectAsync(Uri address, IReadOnlyList<string> protocols, CancellationToken ct) { server.LatestSocket = this; var stream = address.Query.Contains("stream=audit") ? "audit" : "app"; if (server.Reference?.HoldSocket != true) _frames.Writer.TryWrite(server.Reference?.Snapshot(stream) ?? "{\"type\":\"snapshot\",\"stream\":\"" + stream + "\",\"entries\":[" + (server.Mode == "empty" ? "" : stream == "audit" ? AuditEntry : AppEntry) + "]}"); return Task.CompletedTask; }
        public void Disconnect() => _frames.Writer.TryComplete();
        public async Task<string?> ReceiveAsync(CancellationToken ct) { using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _abort.Token); return await _frames.Reader.WaitToReadAsync(linked.Token) ? await _frames.Reader.ReadAsync(linked.Token) : null; }
        public void Abort() { Aborted = true; _abort.Cancel(); }
        public void Dispose() { Aborted = true; _abort.Cancel(); }
    }
}
