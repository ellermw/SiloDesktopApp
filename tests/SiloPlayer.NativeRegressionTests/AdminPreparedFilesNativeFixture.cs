using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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
using Windows.Storage.Streams;

namespace SiloPlayer.NativeRegressionTests;

/// <summary>Actual Frame/controls/dialog coverage for Manage > Downloads > Prepared Files.</summary>
internal static class AdminPreparedFilesNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool value, string message) { if (!value) Failures.Add(message); }

    internal static async Task RunAsync(Window window)
    {
        Failures.Clear();
        var server = new Server();
        using var http = new HttpClient(server);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client));
        auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth);
        var monitorApi = new AdminMonitorApi(client);
        var realtime = new Realtime();
        using var activity = new ServerActivityStore(monitorApi, authority, realtime);
        var navigation = new NavigationService();
        var services = new ServiceCollection()
            .AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(monitorApi).AddSingleton(activity)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory))
            .AddAdminPreparedFiles();
        await using var provider = services.BuildServiceProvider();
        var servicesField = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var priorServices = servicesField.GetValue(null);
        var priorContent = window.Content;
        var outer = new Frame();
        servicesField.SetValue(null, provider);
        try
        {
            Resize(window, 1440, 900);
            window.Content = outer;
            navigation.Frame = outer;
            var request = new AdminRouteRequest(AdminRoute.PreparedFiles,
                new Dictionary<string, string> { ["tab"] = "files", ["location"] = "node:9" });
            Check(AdminShellPage.CanOpen(request), "registration makes Prepared Files openable");
            Check(AdminShellPage.Open(request), "registered route enters the Admin shell");
            await Until(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content;
            var frame = (Frame)Field(shell, "_frame")!;
            await Until(() => frame.Content is AdminPreparedFilesPage page && page.Controller.State.Files.Loaded);
            var prepared = (AdminPreparedFilesPage)frame.Content;
            Check(frame.ContentTransitions == null || frame.ContentTransitions.Count == 0, "page navigation has no slide transition");
            Check(server.Requests.Contains("GET /api/v2/admin/downloads/storage/files?location=node%3A9&sort=size&limit=50"), "URL location opens the official filtered file query");
            Check(Find<FrameworkElement>(shell, "PreparedFilesRow_art-cached") != null, "prepared file row renders through the registered Frame");
            Check(Find<Button>(shell, "PreparedFilesTabStorage") != null && Find<Button>(shell, "PreparedFilesTabHistory") != null, "all official tabs render");
            await CaptureAsync(outer, "admin-prepared-files-wide.png");

            await InvokeAsync(Find<Button>(shell, "PreparedFilesSelect_art-cached"));
            await InvokeAsync(Find<Button>(shell, "PreparedFilesDeleteSelected"));
            Button? confirm = null;
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) is { IsEnabled: true });
            Check(PopupTexts(window).Any(t => t.Contains("Delete prepared file", StringComparison.Ordinal)), "prepared-file deletion is confirmed");
            await InvokeAsync(confirm);
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/storage/files/delete"));
            await Until(() => !prepared.Controller.State.ActionPending);
            Check(server.Requests.Count(r => r == "POST /api/v2/admin/downloads/storage/files/delete") == 1, "delete is sent once");

            await InvokeAsync(Find<Button>(shell, "PreparedFilesTabStorage"));
            await Until(() => prepared.Controller.State.Storage.Loaded && Find<FrameworkElement>(shell, "PreparedFilesStorageCard_node:9") != null);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesCleanup_node:9"));
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/storage/locations/node:9/cleanup"));
            await Until(() => !prepared.Controller.State.ActionPending);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesReviewUntracked_node:9"));
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) is { IsEnabled: true });
            Check(PopupTexts(window).Any(t => t.Contains("only deletes files no prepared-file record accounts for", StringComparison.Ordinal)), "untracked confirmation explains the server-side safety check");
            await InvokeAsync(confirm);
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/storage/locations/node:9/untracked/delete"));
            await Until(() => !prepared.Controller.State.ActionPending);

            await InvokeAsync(Find<Button>(shell, "PreparedFilesEditLocation_node:9"));
            await Until(() => Find<TextBox>(window.Content!, "PreparedFilesLocationDirectory") != null);
            var directory = Find<TextBox>(window.Content!, "PreparedFilesLocationDirectory")!;
            directory.Text = "/prepared/new";
            await InvokeAsync(DialogButton(window, "PrimaryButton"));
            await Until(() => server.Requests.Contains("PUT /api/v2/admin/nodes/9"));
            Check(server.LastIfMatch == "\"node-v1\"", "node location save uses the exact strong validator");

            await InvokeAsync(Find<Button>(shell, "PreparedFilesTabPreparation"));
            await Until(() => prepared.Controller.State.PreparationsLoaded && Find<FrameworkElement>(shell, "PreparedFilesPreparation_art-running") != null);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesPause_art-running"));
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/preparations/pause"));
            await Until(() => !prepared.Controller.State.ActionPending);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesCancel_art-running"));
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) is { IsEnabled: true });
            Check(PopupTexts(window).Any(t => t.Contains("encoding progress", StringComparison.OrdinalIgnoreCase)), "cancel confirmation names lost encoding work");
            await InvokeAsync(confirm);
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/preparations/cancel") && !prepared.Controller.State.ActionPending);

            await InvokeAsync(Find<Button>(shell, "PreparedFilesTabDevices"));
            await Until(() => prepared.Controller.State.Devices.Loaded && Find<FrameworkElement>(shell, "PreparedFilesDevice_phone") != null);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesExpand_phone"));
            await Until(() => Find<FrameworkElement>(shell, "PreparedFilesEntry_dl-1") != null);
            await InvokeAsync(Find<Button>(shell, "PreparedFilesRevoke_dl-1"));
            await Until(() => (confirm = DialogButton(window, "PrimaryButton")) is { IsEnabled: true });
            Check(PopupTexts(window).Any(t => t.Contains("next time it connects", StringComparison.Ordinal)), "revoke confirmation explains device deletion timing");
            await InvokeAsync(confirm);
            await Until(() => server.Requests.Contains("POST /api/v2/admin/downloads/revoke"));
            await Until(() => !prepared.Controller.State.ActionPending);

            await InvokeAsync(Find<Button>(shell, "PreparedFilesTabHistory"));
            await Until(() => prepared.Controller.State.History.Loaded && Find<FrameworkElement>(shell, "PreparedFilesHistory_event-1") != null);
            Check(Texts(shell).Contains("Expired from cache"), "history renders the official reason label");
            Resize(window, 560, 900);
            await Task.Delay(200);
            await CaptureAsync(outer, "admin-prepared-files-compact.png");

            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "admin", Role = "user" });
            await Until(() => prepared.Controller.State.Owner == null);
            Check(prepared.Controller.State.Files.Items.Count == 0 && prepared.Controller.State.Devices.Items.Count == 0, "authority retirement clears every retained row");
            realtime.Silence();
        }
        finally
        {
            window.Content = priorContent;
            for (var waited = 0; waited < 1000 && outer.IsLoaded; waited += 25) await Task.Delay(25);
            servicesField.SetValue(null, priorServices);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Admin Prepared Files fixture failed: " + string.Join("; ", Failures));
        Program.Log("PASS: Admin Prepared Files native fixture completed; synthetic server only, no live server or complete visual parity certification.");
    }

    private sealed class Realtime : IAdminRealtimeChannel
    {
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged;
        public IDisposable Subscribe(params string[] channels) => new Release();
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = default; return false; }
        public void Silence() { SnapshotReceived?.Invoke("", default); StateChanged?.Invoke(CurrentState); }
        private sealed class Release : IDisposable { public void Dispose() { } }
    }

    private sealed class Server : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        internal string? LastIfMatch { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(request.Method + " " + path + request.RequestUri.Query);
            LastIfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString() ?? LastIfMatch;
            string json = path switch
            {
                "/api/v2/admin/downloads/storage" => """{"locations":[{"key":"server","kind":"server","name":"Server","enabled":true,"online":true,"dir":"/srv/prepared","dir_source":"setting","in_use_files":1,"in_use_bytes":10,"cached_files":1,"cached_bytes":20,"waiting_downloads":1,"stale_waiting_bytes":0,"untracked_files":0,"untracked_bytes":0,"budget_bytes":1000,"budget_source":"setting","cleanup_backlog":0,"storage_full":false,"replicas_disagree":false},{"key":"node:9","kind":"node","node_id":"9","name":"gpu-1","enabled":true,"online":true,"dir":"/prepared","dir_source":"override","usage":{"measured_at":"2026-10-08T09:30:00Z","files":2,"bytes":30,"partial_files":0,"partial_bytes":0,"other_bytes":0,"fs_used_bytes":60,"fs_total_bytes":100,"shares_scratch":false,"ephemeral":false,"stale":false},"in_use_files":1,"in_use_bytes":10,"cached_files":1,"cached_bytes":20,"waiting_downloads":1,"stale_waiting_bytes":0,"untracked_files":1,"untracked_bytes":5,"budget_bytes":1000,"budget_source":"override","cleanup_backlog":0,"storage_full":false,"replicas_disagree":false}],"cache_hours":72,"disk_ceiling_percent":85,"default_budget_bytes":1000,"stale_device_days":14,"preparing_jobs":1,"freed_last_30_days_bytes":50}""",
                "/api/v2/admin/downloads/storage/files" => """{"items":[{"id":"art-cached","state":"cached","format":"transcode","location":"node:9","location_name":"gpu-1","media_file_id":"42","title":"Dune","media_type":"movie","container":"mp4","video_codec":"h264","audio_codec":"aac","resolution":"1080p","bitrate_kbps":10000,"bytes":20,"created_at":"2026-10-08T09:30:00Z","last_used_at":"2026-10-08T09:30:00Z","waiting":0,"downloading":0,"finished":1,"stale_waiting":0}],"page":{"has_more":false}}""",
                "/api/v2/admin/downloads/preparations" => """{"counts":{"running":1,"queued":0,"retrying":0,"paused":0,"failed_recent":0},"items":[{"id":"art-running","state":"running","format":"transcode","log_session_id":"prepare-1","media_file_id":"42","media_title":"Dune","media_type":"movie","source":{"hdr":false,"audio_tracks":[],"file_size":30,"duration_seconds":120},"output":{"container":"mp4","video_codec":"h264","audio_codec":"aac","resolution":"1080p","bitrate_kbps":10000,"all_audio_tracks":false},"attempts":1,"max_attempts":3,"created_at":"2026-10-08T09:30:00Z","started_at":"2026-10-08T09:30:00Z","progress":{"encoded_seconds":60,"duration_seconds":120,"speed":2,"updated_at":"2026-10-08T09:31:00Z"},"progress_unavailable":false,"requesters":[{"user_id":"7","username":"maya","profile_id":"p1","device_id":"phone","device_name":"Pixel","status":"preparing"}]}]}""",
                "/api/v2/admin/downloads/devices" => """{"items":[{"user_id":"7","username":"maya","profile_id":"p1","profile_name":"Maya","device_id":"phone","device_name":"Pixel","platform":"android","last_seen_at":"2026-10-08T09:30:00Z","stale":false,"copies":1,"finished":1,"waiting":0,"failed":0,"revoked":0,"bytes_on_device":20,"monitors":1}],"page":{"has_more":false}}""",
                "/api/v2/admin/downloads/entries" => """{"items":[{"id":"dl-1","profile_id":"p1","device_id":"phone","content_id":"movie:dune","title":"Dune","media_type":"movie","status":"completed","quality":"10mbps","effective_quality":"10mbps","delivery_format":"transcode","target_bitrate_kbps":10000,"file_size":20,"created_at":"2026-10-08T09:30:00Z","updated_at":"2026-10-08T09:30:00Z","user_id":"7","username":"maya","device_name":"Pixel","location":"node:9","location_name":"gpu-1"}],"page":{"has_more":false}}""",
                "/api/v2/admin/downloads/storage/events" => """{"items":[{"id":"event-1","reason":"cache_expired","location":"node:9","location_name":"gpu-1","occurred_at":"2026-10-08T09:30:00Z","count":1,"bytes":20,"titles":["Dune"]}],"page":{"has_more":false}}""",
                "/api/v2/admin/nodes" when request.Method == HttpMethod.Get => """{"items":[{"id":"9","config_etag":"\"node-v1\"","name":"gpu-1","type":"transcode","url":"https://node","enabled":true,"healthy":true,"active_jobs":0,"egress_kbps":0,"created_at":"2026-01-01T00:00:00Z","download_artifact_dir_override":"/prepared","download_artifact_max_bytes_override":1000}],"page":{"has_more":false}}""",
                "/api/v2/admin/nodes/9" => """{"id":"9","config_etag":"\"node-v2\"","name":"gpu-1","type":"transcode","url":"https://node","enabled":true,"healthy":true,"active_jobs":0,"egress_kbps":0,"created_at":"2026-01-01T00:00:00Z","download_artifact_dir_override":"/prepared/new","download_artifact_max_bytes_override":1000}""",
                "/api/v2/admin/downloads/storage/files/delete" => """{"results":[{"id":"art-cached","outcome":"deleted","bytes":20}]}""",
                "/api/v2/admin/downloads/storage/locations/node:9/cleanup" => """{"freed_bytes":0}""",
                "/api/v2/admin/downloads/storage/locations/node:9/untracked/delete" => """{"files":1,"bytes":5,"failed_files":0}""",
                "/api/v2/admin/downloads/preparations/pause" or "/api/v2/admin/downloads/preparations/cancel" => """{"results":[{"id":"art-running","outcome":"applied"}]}""",
                "/api/v2/admin/downloads/revoke" => """{"revoked":1,"bytes":20,"paused_monitors":0,"download_ids":["dl-1"]}""",
                _ => "{}",
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (path == "/api/v2/admin/nodes/9") response.Headers.ETag = new EntityTagHeaderValue("\"node-v2\"");
            return Task.FromResult(response);
        }
    }

    private static void Resize(Window window, int width, int height)
    {
        var scale = window.Content?.XamlRoot?.RasterizationScale ?? 1;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)(width * scale), (int)(height * scale)));
    }

    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96,
            (await bitmap.GetPixelsAsync()).ToArray());
        await encoder.FlushAsync();
    }

    private static async Task Until(Func<bool> ready, int timeout = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeout);
        while (!ready() && DateTime.UtcNow < until) await Task.Delay(25);
        if (!ready()) throw new TimeoutException("Prepared Files fixture timed out.");
    }

    private static async Task InvokeAsync(Button? button)
    {
        if (button == null) throw new InvalidOperationException("Expected fixture button.");
        button.StartBringIntoView();
        await Task.Delay(50);
        (FrameworkElementAutomationPeer.CreatePeerForElement(button) as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)?.Invoke();
        await Task.Delay(50);
    }

    private static Button? DialogButton(Window window, string name)
    {
        if (window.Content?.XamlRoot is not { } root) return null;
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(root).SelectMany(p => Descendants(p.Child)).OfType<Button>().FirstOrDefault(b => b.Name == name);
    }

    private static IEnumerable<string> PopupTexts(Window window)
    {
        if (window.Content?.XamlRoot is not { } root) return [];
        return VisualTreeHelper.GetOpenPopupsForXamlRoot(root).SelectMany(p => Descendants(p.Child)).OfType<TextBlock>().Select(t => t.Text);
    }

    private static T? Find<T>(DependencyObject root, string id) where T : FrameworkElement =>
        Descendants(root).OfType<T>().FirstOrDefault(e => AutomationProperties.GetAutomationId(e) == id);
    private static HashSet<string> Texts(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(t => t.Text).ToHashSet(StringComparer.Ordinal);
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject? root)
    {
        if (root == null) yield break;
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
