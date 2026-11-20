using System.Net;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>Real production registration/shell/Frame; compiles before implementation for coordinator RED.</summary>
internal static class AdminPlaybackAdministrationNativeFixture
{
    private static void Check(bool value, string label) { Program.Log((value ? "PASS: " : "FAIL: ") + label); if (!value) throw new InvalidOperationException(label); }
    private static async Task Until(Func<bool> predicate, string label)
    {
        for (var i = 0; i < 240 && !predicate(); i++) await Task.Delay(25);
        Check(predicate(), label);
    }
    private sealed class Server : HttpMessageHandler
    {
        internal int Writes; internal string? IfMatch, LastBody; internal bool Conflict;
        internal string? AddedNode;
        internal readonly Dictionary<string, string> Values = new() { ["playback.hw_accel"] = "auto", ["playback.transcode_enabled"] = "true", ["allow_4k_transcode"] = "true", ["markers.mode"] = "both", ["markers.online_storage"] = "stored", ["playback.preview_image_width"] = "300", ["playback.trickplay_interval_seconds"] = "10" };
        internal string Node = """{"id":"fixture-node","name":"Synthetic GPU","type":"transcode","url":"https://worker.invalid","enabled":true,"healthy":true,"group":"rack-a","active_jobs":2,"max_jobs":4,"egress_kbps":0,"max_bandwidth_kbps":0,"drain_requested":false,"drain_state":"none","key_issued":true,"config_etag":"\"node-1\"","capabilities":{"resolved":"qsv","render_devices":["/dev/dri/renderD128"],"transport_features":["trickplay_extract_v2"]},"network_access":{"synthetic-overlay":{"state":"connected","hostname":"synthetic-node"}}}""";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; object body = new { };
            if (request.Method == HttpMethod.Put)
            {
                Writes++; IfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString(); LastBody = await request.Content!.ReadAsStringAsync(ct);
                if (Conflict) return Json(new { type = "precondition_failed", status = 412 }, HttpStatusCode.PreconditionFailed);
                if (path.EndsWith("/settings"))
                {
                    using var document = JsonDocument.Parse(LastBody);
                    foreach (var property in document.RootElement.GetProperty("values").EnumerateObject()) Values[property.Name] = property.Value.GetString()!;
                    body = new { values = Values, restart_required = false };
                }
                else
                {
                    var saved = JsonNode.Parse(Node)!.AsObject(); using var update = JsonDocument.Parse(LastBody);
                    foreach (var property in update.RootElement.EnumerateObject()) saved[property.Name] = JsonNode.Parse(property.Value.GetRawText());
                    Node = saved.ToJsonString(); body = JsonSerializer.Deserialize<JsonElement>(Node);
                }
            }
            else if (request.Method == HttpMethod.Delete)
            {
                Writes++; IfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString(); AddedNode = null; return new(HttpStatusCode.NoContent);
            }
            else if (request.Method == HttpMethod.Post)
            {
                Writes++;
                if (path.EndsWith("/nodes"))
                {
                    LastBody = await request.Content!.ReadAsStringAsync(ct); var created = JsonNode.Parse(LastBody)!.AsObject(); created["id"] = "created-node"; created["config_etag"] = "\"node-1\""; created["enabled"] = true; created["healthy"] = true;
                    AddedNode = created.ToJsonString(); return JsonText(AddedNode, HttpStatusCode.Created);
                }
                if (path.EndsWith("/key")) return Json(new { node_key = "synthetic-one-time-key" }, HttpStatusCode.Created);
                if (path.EndsWith("/drain") || path.EndsWith("/resume"))
                {
                    var changed = JsonNode.Parse(Node)!.AsObject(); changed["drain_requested"] = path.EndsWith("/drain"); changed["drain_state"] = path.EndsWith("/drain") ? "draining" : "none"; Node = changed.ToJsonString();
                }
                body = path.EndsWith("/check") ? new { healthy = true, active_jobs = 2, egress_kbps = 0, health_persisted = false }
                    : path.EndsWith("/reprobe") ? (object)new { node_id = "fixture-node", node_name = "Synthetic GPU", status = "ok", capabilities_refreshed = true, capability_hash = "new" }
                    : new { node_id = "fixture-node", drain_requested = path.EndsWith("/drain"), drain_state = "draining", active_jobs = 2 };
            }
            else if (path.EndsWith("/effective")) body = Values;
            else if (path.EndsWith("/sensitive-status")) body = new { configured = Array.Empty<string>(), managed_by_env = Array.Empty<string>() };
            else if (path.EndsWith("/restart-keys")) body = new { keys = Array.Empty<string>(), prefixes = Array.Empty<string>() };
            else if (path.EndsWith("/nodes/capabilities")) body = new { allowed = true, state = "available", revision = "1", drain = true, node_keys = true, source_access = true, summary = true };
            else if (path.EndsWith("/markers/capabilities")) body = new { allowed = true, state = "available", detection_kind_settings = true };
            else if (path == "/api/v2/libraries/capabilities") body = new { trickplay = true };
            else if (path.EndsWith("/hw-accel")) body = new { resolved = "qsv", render_devices = new[] { "/dev/dri/renderD128" }, render_device_details = new[] { new { path = "/dev/dri/renderD128", description = "Synthetic Intel GPU" } }, detected_backends = new[] { new { backend = "qsv", verified = true } } };
            else if (path.EndsWith("/nodes")) return JsonText("{\"items\":[" + Node + (AddedNode == null ? "" : "," + AddedNode) + "]}");
            else body = new { items = Array.Empty<object>() };
            return Json(body);
        }
        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => JsonText(JsonSerializer.Serialize(body), status);
        private static HttpResponseMessage JsonText(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.ETag = new("\"node-1\""); return response;
        }
    }
    internal static async Task RunAsync(Window window)
    {
        using var server = new Server(); using var http = new HttpClient(server); var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth); var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new LoggingDisclosurePreferences(Program.ResultDirectory));
        typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices")!.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider(); var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = field.GetValue(null); var previousContent = window.Content; var outer = new Frame();
        try
        {
            field.SetValue(null, services); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000)); window.Content = outer; navigation.Frame = outer;
            AdminShellPage.TryOpenHref("/admin/nodes"); await Until(() => outer.Content is AdminShellPage, "Nodes enters production shell");
            var shell = (AdminShellPage)outer.Content; var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(shell)!;
            await Until(() => inner.Content?.GetType().Name == "AdminNodesPage" && All(inner).OfType<TextBlock>().Any(t => t.Text == "Synthetic GPU"), "Nodes activates and renders synthetic node");
            Check(All(inner).OfType<TextBlock>().Any(t => t.Text.Contains("synthetic-overlay")), "reported network provider is shown");
            await Capture(outer, "playback-nodes.png");
            var nodes = (FrameworkElement)inner.Content;
            Invoke(Find<Button>(nodes, "NodeCheck_fixture-node")); await Until(() => server.Writes == 1, "health check sent once");
            await Until(() => NodeIdle(nodes) && Find<Button>(nodes, "NodeEdit_fixture-node").IsEnabled, "health reconciliation finished");
            var beforeCommand = server.Writes; Invoke(Find<Button>(nodes, "NodeReprobe_fixture-node")); await Until(() => server.Writes == beforeCommand + 1 && NodeIdle(nodes), "reprobe sent once and reconciled");
            beforeCommand = server.Writes; Invoke(Find<Button>(nodes, "NodeDrain_fixture-node")); await Until(() => server.Writes == beforeCommand + 1 && NodeIdle(nodes) && server.Node.Contains("draining"), "drain acknowledgement reconciled");
            beforeCommand = server.Writes; Invoke(Find<Button>(nodes, "NodeDrain_fixture-node")); await Until(() => server.Writes == beforeCommand + 1 && NodeIdle(nodes) && !server.Node.Contains("draining"), "resume acknowledgement reconciled");
            Invoke(Find<Button>(inner, "NodeEdit_fixture-node")); await Until(() => Field(nodes, "_dialog") is ContentDialog { IsLoaded: true }, "edit dialog opened");
            var dialog = (ContentDialog)Field(nodes, "_dialog")!;
            Check(All(dialog).OfType<ComboBox>().Any(c => AutomationProperties.GetAutomationId(c) == "Node_source_access"), "source access capability shows local/core selector");
            await Capture(dialog, "playback-node-editor.png"); Find<TextBox>(dialog, "Node_name").Text = "Synthetic GPU edited";
            Invoke(DialogButton(dialog, "Save")); await Until(() => !dialog.IsLoaded, "editor saved and closed");
            Check(server.IfMatch == "\"node-1\"", "node editor sends captured configuration validator");
            using (var update = JsonDocument.Parse(server.LastBody!)) Check(update.RootElement.GetProperty("source_access").GetString() == "local", "typed node editor sends source access");
            await Until(() => Find<Button>(nodes, "NodeKey_fixture-node").IsEnabled, "node ready for key rotation");
            Invoke(Find<Button>(nodes, "NodeKey_fixture-node")); await Until(() => Field(nodes, "_dialog") is ContentDialog { IsLoaded: true }, "key rotation confirmation opened");
            dialog = (ContentDialog)Field(nodes, "_dialog")!; var beforeKey = server.Writes; Invoke(DialogButton(dialog, "Rotate key"));
            await Until(() => All(dialog).OfType<PasswordBox>().Any(), "one-time key arrives in private dialog");
            Check(server.Writes == beforeKey + 1 && Find<PasswordBox>(dialog, "NodeIssuedKey").Password == "synthetic-one-time-key", "key is issued once");
            await Capture(dialog, "playback-node-key.png"); Invoke(DialogButton(dialog, "I saved the key")); await Until(() => !dialog.IsLoaded, "key dialog closed");
            Check(All(dialog).OfType<PasswordBox>().All(box => box.Password.Length == 0), "key erased after close");
            Invoke(Find<Button>(nodes, "NodesAdd_proxy")); await Until(() => Field(nodes, "_dialog") is ContentDialog { IsLoaded: true }, "proxy creation dialog opened");
            dialog = (ContentDialog)Field(nodes, "_dialog")!; Find<TextBox>(dialog, "Node_name").Text = "Created proxy"; Find<TextBox>(dialog, "Node_url").Text = "https://proxy.invalid";
            Invoke(DialogButton(dialog, "Save")); await Until(() => !dialog.IsLoaded && server.AddedNode != null, "proxy create saved");
            Invoke(Find<Button>(nodes, "NodeDelete_created-node")); await Until(() => Field(nodes, "_dialog") is ContentDialog { IsLoaded: true }, "delete confirmation opened");
            dialog = (ContentDialog)Field(nodes, "_dialog")!; Invoke(DialogButton(dialog, "Delete")); await Until(() => !dialog.IsLoaded && server.AddedNode == null, "guarded delete accepts empty 204");
            Invoke(Find<Button>(nodes, "NodeRoutingPreset_gpu")); await Until(() => Find<Button>(nodes, "NodeRoutingSave").IsEnabled, "routing preset stages own draft");
            Invoke(Find<Button>(nodes, "NodeRoutingSave")); await Until(() => server.Values.GetValueOrDefault("playback.routing.remux_execution") == "prefer_api" && !Shown(Find<Button>(nodes, "NodeRoutingSave")), "routing preset saved with exact five-key contract");
            AdminShellPage.TryOpenHref("/admin/settings/playback"); await Until(() => inner.Content?.GetType().Name == "AdminPlaybackSettingsPage" && All(inner).OfType<ToggleSwitch>().Any(), "Playback Settings enters the registered page");
            var playback = (FrameworkElement)inner.Content;
            await Until(() => Shown(Find<FrameworkElement>(playback, "PlaybackRow_playback.preview_image_width")), "trickplay capability reveals preview settings");
            Find<ToggleSwitch>(playback, "Playback_allow_4k_transcode").IsOn = false;
            await Until(() => !Shown(Find<FrameworkElement>(playback, "PlaybackRow_playback.prefer_lower_version_transcode")), "4K dependent row hides");
            var mode = Find<ComboBox>(playback, "Playback_markers.mode"); mode.SelectedItem = mode.Items.OfType<ComboBoxItem>().Single(i => (string)i.Tag == "off");
            await Until(() => !Shown(Find<FrameworkElement>(playback, "PlaybackRow_markers.online_storage")), "marker mode updates dependent controls");
            await Capture(outer, "playback-settings.png");
            Find<TextBox>(playback, "Playback_playback.preview_image_width").Text = "320";
            var panel = Field(playback, "_panel")!; Invoke(Find<Button>(outer, "PlaybackSave")); await Until(() => Field(panel, "_dialog") is ContentDialog { IsLoaded: true }, "preview rebuild confirmation opened");
            dialog = (ContentDialog)Field(panel, "_dialog")!; await Capture(dialog, "playback-preview-confirmation.png");
            var beforeSettings = server.Writes; Invoke(DialogButton(dialog, "Save")); await Until(() => !dialog.IsLoaded && server.Writes == beforeSettings + 1, "settings save reaches server once after confirmation");
            Check(server.IfMatch == "\"node-1\"", "settings save uses strong baseline ETag");
            await Until(() => !Shown(Find<Button>(outer, "PlaybackSave")), "first playback save acknowledged");
            server.Conflict = true; Find<TextBox>(playback, "Playback_playback.watched_threshold").Text = "80";
            await Until(() => Find<Button>(outer, "PlaybackSave").IsEnabled, "threshold draft staged");
            var beforeConflict = server.Writes; Invoke(Find<Button>(outer, "PlaybackSave")); await Until(() => StateFlag(playback, "Conflict"), "strong ETag conflict keeps draft for review");
            Check(server.Writes == beforeConflict + 1 && !Find<Button>(outer, "PlaybackSave").IsEnabled, "conflict does not automatically replay");
            server.Conflict = false; Invoke(Find<Button>(playback, "PlaybackReload")); await Until(() => !StateFlag(playback, "Conflict") && !StateFlag(playback, "Loading") && Find<Button>(outer, "PlaybackSave").IsEnabled, "explicit reload preserves draft for review");
            Invoke(Find<Button>(outer, "PlaybackSave")); await Until(() => !Shown(Find<Button>(outer, "PlaybackSave")), "reviewed draft saves after reconciliation");
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
            await Until(() => All(playback).OfType<TextBox>().All(c => c.Text.Length == 0), "authority retirement clears settings inputs");
        }
        finally
        {
            if (outer.Content is AdminShellPage currentShell && Field(currentShell, "_frame") is Frame currentInner && currentInner.Content is IAdminPageSuspension suspension) suspension.SuspendAdminPage();
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(outer.XamlRoot).ToArray()) foreach (var dialog in All(popup.Child).OfType<ContentDialog>()) dialog.Hide();
            var retired = outer.Content as FrameworkElement; outer.Content = null; outer.BackStack.Clear(); outer.ForwardStack.Clear();
            await Until(() => retired?.IsLoaded != true, "shell unloaded before restoring globals");
            window.Content = previousContent; field.SetValue(null, previousServices);
        }
    }
    private static T Find<T>(DependencyObject root, string id) where T : FrameworkElement => All(root).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value);
    private static bool NodeIdle(object page)
    {
        var controller = Field(page, "_controller"); if (controller == null) return false;
        var state = controller.GetType().GetProperty("State")!.GetValue(controller)!;
        return !(bool)state.GetType().GetProperty("Busy")!.GetValue(state)! && !(bool)state.GetType().GetProperty("Loading")!.GetValue(state)!;
    }
    private static bool StateFlag(object page, string name)
    {
        var controller = Field(page, "_controller")!; var state = controller.GetType().GetProperty("State")!.GetValue(controller)!;
        return (bool)state.GetType().GetProperty(name)!.GetValue(state)!;
    }
    private static Button DialogButton(ContentDialog dialog, string text) => All(dialog).OfType<Button>().First(button => button.Content?.ToString() == text);
    private static IEnumerable<DependencyObject> All(DependencyObject root) { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in All(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static bool Shown(FrameworkElement element) { for (DependencyObject? current = element; current is UIElement ui; current = VisualTreeHelper.GetParent(current)) if (ui.Visibility != Visibility.Visible) return false; return true; }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Capture(FrameworkElement root, string name)
    {
        root.UpdateLayout(); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root); Check(bitmap.PixelWidth > 0, name + " rendered");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory)); var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
}
