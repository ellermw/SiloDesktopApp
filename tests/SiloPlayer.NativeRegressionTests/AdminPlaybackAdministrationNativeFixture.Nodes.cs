using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;

/// <summary>
/// Nodes refresh against official 8ae4f4f5a: every node kind, environment-managed and deployment-managed nodes, key creation and rotation, delete,
/// refused and unknown outcomes, doubled activations and teardown. Its own server, client and shell: nothing here shares state with the Playback settings steps.
/// </summary>
internal static partial class AdminPlaybackAdministrationNativeFixture
{
    private static void TryInvoke(Button button) { if (button.IsEnabled) Invoke(button); }
    private const string NodeKey = "synthetic-one-time-key-";
    private sealed class NodesServer : HttpMessageHandler
    {
        internal readonly List<(string Method, string Path, string Body, string? IfMatch)> Requests = [];
        internal readonly Dictionary<string, JsonObject> Rows = new(StringComparer.Ordinal);
        internal bool RefusePut, DropPut, RefuseDelete, DropDelete, RefuseKey, DropKey, FailList, EmptyList; internal int Keys, Versions = 1; internal TaskCompletionSource? HoldCheck, HoldEffective, HoldKey; internal string KeySuffix = "";
        internal int Count(string method, string suffix) => Requests.Count(request => request.Method == method && request.Path.EndsWith(suffix, StringComparison.Ordinal));
        internal string BodyOf(string method, string suffix) => Requests.Last(request => request.Method == method && request.Path.EndsWith(suffix, StringComparison.Ordinal)).Body;
        internal static JsonObject Row(string id, string name, string type, string extra = "") => JsonNode.Parse($$"""
            {"id":"{{id}}","name":"{{JsonEncodedText.Encode(name)}}","type":"{{type}}","url":"https://{{id}}.invalid","enabled":true,"healthy":true,"active_jobs":1,"max_jobs":4,"egress_kbps":1500,"max_bandwidth_kbps":{{(type == "proxy" ? 8000 : 0)}},"group":"rack-a","last_health_check":"{{DateTimeOffset.UtcNow:O}}","created_at":"2026-11-01T00:00:00Z","drain_requested":false,"drain_state":"none","config_etag":"\"{{id}}-v1\""{{extra}}}
            """)!.AsObject();
        internal NodesServer()
        {
            foreach (var row in new[]
            {
                Row("n-proxy", "Plain proxy", "proxy", ",\"public_url\":\"https://public.invalid\",\"deployment_managed\":false,\"env_managed\":[]"),
                Row("n-proxy-env", "Env proxy", "proxy", ",\"public_url\":\"https://public-env.invalid\",\"deployment_managed\":false,\"env_managed\":[\"public_url\",\"max_bandwidth_kbps\"]"),
                Row("n-plain", "Plain GPU", "transcode", ",\"key_issued\":true,\"deployment_managed\":false,\"env_managed\":[],\"source_access\":\"local\",\"capabilities\":{\"resolved\":\"qsv\",\"render_devices\":[\"/dev/dri/renderD128\"],\"render_device_details\":[{\"path\":\"/dev/dri/renderD128\",\"description\":\"Synthetic GPU\"}],\"detected_backends\":[{\"backend\":\"qsv\",\"verified\":true,\"device\":\"/dev/dri/renderD128\"}]},\"last_stats\":{\"sampled_at\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"system\":{\"cpu_pct\":12,\"mem_used_mb\":512,\"mem_total_mb\":2048,\"net_rx_bps\":1000000,\"net_tx_bps\":2000},\"gpu\":[{\"device\":\"/dev/dri/renderD128\",\"source\":\"i915\",\"video_busy_pct\":33,\"sessions\":1}]}"),
                Row("n-env", "Env GPU", "transcode", ",\"key_issued\":true,\"deployment_managed\":false,\"env_managed\":[\"name\",\"group\",\"max_jobs\",\"hw_device_override\"],\"hw_device_override\":\"/dev/dri/renderD128\",\"source_access\":\"local\",\"capabilities\":{\"resolved\":\"qsv\",\"render_device_details\":[{\"path\":\"/dev/dri/renderD128\",\"description\":\"Synthetic GPU\"}]}"),
                Row("n-deploy", "Deployment GPU", "transcode", ",\"key_issued\":true,\"deployment_managed\":true,\"env_managed\":[],\"source_access\":\"local\""),
                Row("n-legacy", "Legacy\u202E GPU\nname", "transcode", ",\"key_issued\":false,\"source_access\":\"local\""),
            }) Rows[row["id"]!.GetValue<string>()] = row;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries); var id = segments.Length >= 5 && segments[3] == "nodes" ? segments[4] : null;
            if (request.Method != HttpMethod.Get) Requests.Add((request.Method.Method, path, body, request.Headers.IfMatch.SingleOrDefault()?.ToString()));
            if (path.EndsWith("/effective")) { var hold = HoldEffective; if (hold != null) await hold.Task.WaitAsync(ct); return Text("{\"playback.hw_accel\":\"auto\",\"server.public_url\":\"https://core.invalid:8443\"}"); }
            if (path.EndsWith("/sensitive-status")) return Text("{\"configured\":[],\"managed_by_env\":[]}");
            if (path.EndsWith("/restart-keys")) return Text("{\"keys\":[],\"prefixes\":[]}");
            if (path.EndsWith("/nodes/capabilities")) return Text("{\"allowed\":true,\"state\":\"available\",\"revision\":\"1\",\"drain\":true,\"node_keys\":true,\"source_access\":true,\"summary\":true,\"env_declarations\":true,\"deployment_nodes\":true}");
            if (request.Method == HttpMethod.Get && path.EndsWith("/nodes"))
            {
                if (FailList) return Text("{\"type\":\"internal\",\"status\":500}", HttpStatusCode.InternalServerError);
                return Text("{\"items\":[" + (EmptyList ? "" : string.Join(",", Rows.Values.Select(row => row.ToJsonString()))) + "]}");
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/nodes"))
            {
                var created = JsonNode.Parse(body)!.AsObject(); created["id"] = "n-created"; created["enabled"] = true; created["healthy"] = false; created["key_issued"] = false; created["config_etag"] = "\"n-created-v1\""; Rows["n-created"] = created;
                return Text(created.ToJsonString(), HttpStatusCode.Created, "\"n-created-v1\"");
            }
            if (id == null || !Rows.TryGetValue(id, out var row)) return Text("{\"items\":[]}");
            if (request.Method == HttpMethod.Put)
            {
                if (DropPut) throw new HttpRequestException("synthetic lost acknowledgement");
                if (RefusePut) return Text("{\"type\":\"https://silo.invalid/problems/validation_failed\",\"status\":422,\"title\":\"Invalid\",\"detail\":\"Set by the node\\u2019s environment. Change it there and restart the node.\",\"errors\":[{\"location\":\"body.name\",\"code\":\"managed_by_environment\",\"detail\":\"Set by the node\\u2019s environment. Change it there and restart the node.\"}]}", HttpStatusCode.UnprocessableEntity);
                foreach (var property in JsonNode.Parse(body)!.AsObject().ToArray()) row[property.Key] = property.Value?.DeepClone();
                var tag = "\"" + id + "-v" + ++Versions + "\""; row["config_etag"] = tag; return Text(row.ToJsonString(), HttpStatusCode.OK, tag);
            }
            if (request.Method == HttpMethod.Delete)
            {
                if (DropDelete) throw new HttpRequestException("synthetic lost acknowledgement");
                if (RefuseDelete) return Text("{\"type\":\"https://silo.invalid/problems/node_deployment_managed\",\"status\":409,\"title\":\"Node managed by deployment configuration\",\"detail\":\"This node is set by the server\\u2019s deployment configuration. Remove it there and restart the API servers to manage it here.\"}", HttpStatusCode.Conflict);
                Rows.Remove(id); return new(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/key"))
            {
                Keys++; var holdKey = HoldKey; if (holdKey != null) await holdKey.Task.WaitAsync(ct); if (DropKey) throw new HttpRequestException("synthetic lost acknowledgement");
                if (RefuseKey) return Text("{\"type\":\"https://silo.invalid/problems/conflict\",\"status\":409,\"title\":\"Conflict\",\"detail\":\"Only transcode nodes take a node key.\"}", HttpStatusCode.Conflict);
                row["key_issued"] = true; return Text("{\"node_key\":\"" + NodeKey + Keys + KeySuffix + "\"}", HttpStatusCode.Created);
            }
            if (path.EndsWith("/check")) { var hold = HoldCheck; if (hold != null) await hold.Task.WaitAsync(ct); }
            if (path.EndsWith("/check")) return Text("{\"healthy\":true,\"active_jobs\":1,\"egress_kbps\":0,\"health_persisted\":true}");
            return Text("{\"items\":[]}");
        }
        private static HttpResponseMessage Text(string json, HttpStatusCode status = HttpStatusCode.OK, string? etag = null)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") }; // Settings reads are strongly validated; node answers pass their own validator.
            if (status is HttpStatusCode.OK or HttpStatusCode.Created) response.Headers.ETag = new(etag ?? "\"fixture-settings\""); return response;
        }
    }

    // A check whose failure must not hide the others: it logs FAIL and the run is ended after the block (used for the erasure checks, so each can be shown to fail on its own).
    private static readonly List<string> SoftFailures = [];
    private static void SoftCheck(bool value, string label) { Program.Log((value ? "PASS: " : "FAIL: ") + label); if (!value) SoftFailures.Add(label); }
    private static void ThrowSoftFailures() { if (SoftFailures.Count > 0) throw new InvalidOperationException("Failed: " + string.Join("; ", SoftFailures)); }
    private sealed class WithoutService(IServiceProvider inner, Type hidden) : IServiceProvider { public object? GetService(Type serviceType) => serviceType == hidden ? null : inner.GetService(serviceType); }
    private static IEnumerable<DependencyObject> AllExceptPasswordInput(DependencyObject root) { yield return root; if (root is PasswordBox) yield break; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in AllExceptPasswordInput(VisualTreeHelper.GetChild(root, i))) yield return child; }
    /// <summary>True when any text, text box, button label, dialog title, tooltip or automation property under <paramref name="root"/> holds the secret. The masked field itself is the one place the key may be.</summary>
    private static bool HoldsText(DependencyObject root, string secret)
    {
        foreach (var element in AllExceptPasswordInput(root))
        {
            var texts = new List<string?>();
            if (element is TextBlock block) texts.Add(block.Text);
            if (element is TextBox box) texts.Add(box.Text);
            if (element is ContentControl { Content: string content }) texts.Add(content);
            if (element is ContentDialog dialog) { texts.Add(dialog.Title as string); texts.Add(dialog.PrimaryButtonText); texts.Add(dialog.CloseButtonText); }
            if (element is FrameworkElement fe) { texts.Add(ToolTipService.GetToolTip(fe) as string); texts.Add(AutomationProperties.GetName(fe)); texts.Add(AutomationProperties.GetHelpText(fe)); texts.Add(AutomationProperties.GetFullDescription(fe)); texts.Add(AutomationProperties.GetItemStatus(fe)); texts.Add(AutomationProperties.GetLocalizedControlType(fe)); }
            if (texts.Any(text => text?.Contains(secret, StringComparison.Ordinal) == true)) return true;
        }
        return false;
    }
    /// <summary>The clipboard text, read back for a copy action (null when there is none or it cannot be read within about two seconds).</summary>
    private static async Task<string?> ReadClipboard()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try { var view = Clipboard.GetContent(); if (view.Contains(StandardDataFormats.Text)) return (await view.GetTextAsync()).Replace("\r\n", "\n"); return null; }
            catch (Exception) { await Task.Delay(100); }
        }
        return null;
    }
    private static async Task ClearClipboard() { for (var attempt = 0; attempt < 20; attempt++) { try { Clipboard.Clear(); return; } catch (Exception) { await Task.Delay(100); } } }

    private static async Task NodesRefreshAsync(Window window)
    {
        SoftFailures.Clear();
        using var server = new NodesServer(); using var http = new HttpClient(server); var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth); var navigation = new NavigationService();
        var collection = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(new AdminMonitorApi(client)).AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new LoggingDisclosurePreferences(Program.ResultDirectory)).AddSingleton(new SettingsService(Path.Combine(Program.ResultDirectory, "nodes-settings")));
        typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Services.AdminConfigureServices")!.GetMethod("Add", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, [collection]);
        await using var services = collection.BuildServiceProvider(); var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = field.GetValue(null); var previousContent = window.Content; var outer = new Frame();
        try
        {
            field.SetValue(null, services); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000)); window.Content = outer; navigation.Frame = outer;
            AdminShellPage.TryOpenHref("/admin/nodes"); await Until(() => outer.Content is AdminShellPage, "[nodes refresh] Nodes enters the production shell");
            var shell = (AdminShellPage)outer.Content; var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(shell)!;
            await Until(() => inner.Content?.GetType().Name == "AdminNodesPage" && All(inner).OfType<TextBlock>().Any(t => t.Text == "Plain GPU") && NodeIdle(inner.Content), "[nodes refresh] every node kind renders");
            var nodes = (FrameworkElement)inner.Content; var texts = () => All(nodes).OfType<TextBlock>().Select(t => t.Text).ToArray();
            Button Btn(string id) => Find<Button>(nodes, id);
            bool Has(string id) => All(nodes).OfType<FrameworkElement>().Any(c => AutomationProperties.GetAutomationId(c) == id);
            Diagnose = () => "writes=" + server.Requests.Count + " texts=" + string.Join(" | ", texts().Where(t => t.Length > 0).Take(30));

            // ---- each node kind is listed in its section, with the state of its configuration ----
            foreach (var id in new[] { "n-proxy", "n-proxy-env", "n-plain", "n-env", "n-deploy", "n-legacy" }) Check(Has("NodeEdit_" + id) && Has("NodeDelete_" + id) && Has("NodeCheck_" + id) && Has("NodeEnabled_" + id), "[nodes refresh] " + id + " has its actions");
            Check(!Has("NodeKey_n-proxy") && !Has("NodeReprobe_n-proxy") && Has("NodeReprobe_n-plain") && Has("NodeDrain_n-plain"), "[nodes refresh] a proxy has no key, re-probe or drain; a transcode node has them");
            Check(texts().Contains("Proxy Nodes") && texts().Contains("Transcode Nodes") && texts().Contains("Routing"), "[nodes refresh] both sections and the routing section are titled");
            Check(texts().Contains("Plain GPU") && texts().Contains("Deployment GPU") && texts().Contains("Env proxy") && Has("NodeEnabled_n-legacy"), "[nodes refresh] proxy, transcode, environment-managed, deployment-managed and legacy nodes are all listed");
            foreach (var width in new[] { 1440, 1024, 800, 560 })
            {
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 1000)); await Task.Delay(400);
                await Capture(outer, "nodes-refresh-list-" + width + ".png"); await Capture((FrameworkElement)nodes.GetType().GetProperty("ScrollContent")!.GetValue(nodes)!, "nodes-refresh-full-" + width + ".png");
            }
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000)); await Task.Delay(300);

            // ---- deployment-managed node: key line and delete follow the official wording and offer nothing the server refuses ----
            Check(texts().Contains("Key set by the server\u2019s deployment configuration"), "[nodes refresh] a deployment-managed node says its key is set by the deployment");
            Check(!Has("NodeKey_n-deploy") && Has("NodeKey_n-plain") && Has("NodeKey_n-env"), "[nodes refresh] Issue/Rotate is hidden only for the deployment-managed node");
            var delete = Btn("NodeDelete_n-deploy"); Check(delete.IsEnabled && delete.Opacity < 1 && ToolTipService.GetToolTip(delete) as string == AdminPlaybackNodesController.DeploymentManagedNote, "[nodes refresh] its delete stays focusable, looks inactive and carries the official note");
            Check(AutomationProperties.GetHelpText(delete) == AdminPlaybackNodesController.DeploymentManagedNote, "[nodes refresh] the note is also its automation help text");
            // What UI Automation reports (a screen reader announces the item status after the name) says the delete is unavailable and why, while the button stays focusable.
            var deletePeer = new ButtonAutomationPeer(delete);
            Check(deletePeer.GetItemStatus() == AdminPlaybackNodesController.DeploymentManagedNote && deletePeer.GetFullDescription() == AdminPlaybackNodesController.DeploymentManagedNote && deletePeer.GetHelpText() == AdminPlaybackNodesController.DeploymentManagedNote && deletePeer.GetName().StartsWith("Delete ") && delete.IsTabStop, "[nodes refresh] the managed delete reports the note as item status and full description, keeps its name and stays focusable");
            Check(AutomationProperties.GetItemStatus(Btn("NodeDelete_n-plain")).Length == 0, "[nodes refresh] an ordinary delete reports no such status");
            var requests = server.Requests.Count; Invoke(delete); await Until(() => texts().Contains(AdminPlaybackNodesController.DeploymentManagedNote), "[nodes refresh] pressing delete shows the reason");
            Check(Field(nodes, "_dialog") == null && server.Requests.Count == requests, "[nodes refresh] no confirmation opens and nothing is sent for a deployment-managed delete");

            // ---- environment-managed node: declared fields are read-only, noted, and never sent ----
            await OpenDialog(nodes, () => Btn("NodeEdit_n-env"), "[nodes refresh] env-managed editor opened");
            var dialog = (ContentDialog)Field(nodes, "_dialog")!;
            Check(new[] { "Node_name", "Node_group", "Node_maxTranscodes" }.All(id => !Find<TextBox>(dialog, id).IsEnabled), "[nodes refresh] declared fields are read-only");
            Check(Find<TextBox>(dialog, "Node_url").IsEnabled && Find<ComboBox>(dialog, "Node_source_access").IsEnabled, "[nodes refresh] fields the node does not declare stay editable");
            Check(All(dialog).OfType<TextBlock>().Count(t => t.Text == "Set by this node\u2019s environment. Change it there and restart the node.") == 4, "[nodes refresh] each declared field carries the official note");
            Check(All(dialog).OfType<ToggleSwitch>().All(toggle => !toggle.IsEnabled) && !All(dialog).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "NodeDevicesInherit"), "[nodes refresh] the device switches are disabled and Use cluster default is hidden");
            Check(Find<TextBox>(dialog, "Node_url").FlowDirection == FlowDirection.LeftToRight, "[nodes refresh] the URL field reads left to right");
            await Capture(dialog, "nodes-refresh-editor-env.png");
            Find<TextBox>(dialog, "Node_url").Text = "https://moved.invalid"; await Task.Delay(200); var before = server.Count("PUT", "/n-env");
            Invoke(DialogButton(dialog, "Save")); await Until(() => !dialog.IsLoaded && server.Count("PUT", "/n-env") == before + 1, "[nodes refresh] env-managed edit saved once");
            using (var update = JsonDocument.Parse(server.BodyOf("PUT", "/n-env")))
            {
                var keys = update.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
                Check(!keys.Intersect(new[] { "name", "group", "max_jobs", "hw_device_override" }).Any() && keys.Contains("url") && keys.Contains("max_bandwidth_kbps") && keys.Contains("hw_accel_override"), "[nodes refresh] declared fields are not in the request body, the others are");
            }
            await Until(() => NodeIdle(nodes) && Btn("NodeEdit_n-env").IsEnabled, "[nodes refresh] settled after the env-managed save");

            // ---- env-managed proxy ----
            await OpenDialog(nodes, () => Btn("NodeEdit_n-proxy-env"), "[nodes refresh] env-managed proxy editor opened"); dialog = (ContentDialog)Field(nodes, "_dialog")!;
            Check(!Find<TextBox>(dialog, "Node_publicUrl").IsEnabled && !Find<TextBox>(dialog, "Node_maxEgress").IsEnabled && Find<TextBox>(dialog, "Node_group").IsEnabled, "[nodes refresh] a proxy's declared public URL and egress limit are read-only");
            before = server.Count("PUT", "/n-proxy-env"); Invoke(DialogButton(dialog, "Save")); await Until(() => !dialog.IsLoaded && server.Count("PUT", "/n-proxy-env") == before + 1, "[nodes refresh] env-managed proxy saved");
            using (var update = JsonDocument.Parse(server.BodyOf("PUT", "/n-proxy-env"))) Check(!update.RootElement.TryGetProperty("public_url", out _) && !update.RootElement.TryGetProperty("max_bandwidth_kbps", out _) && update.RootElement.TryGetProperty("group", out _), "[nodes refresh] the proxy body omits its declared fields");
            await Until(() => NodeIdle(nodes) && Btn("NodeEdit_n-proxy-env").IsEnabled, "[nodes refresh] settled after the proxy save");

            // ---- an older server (no managed fields): the whole form is editable and sent ----
            await OpenDialog(nodes, () => Btn("NodeEdit_n-legacy"), "[nodes refresh] legacy editor opened"); dialog = (ContentDialog)Field(nodes, "_dialog")!;
            Check(All(dialog).OfType<Control>().Where(c => AutomationProperties.GetAutomationId(c).StartsWith("Node_")).All(c => c.IsEnabled) && !All(dialog).OfType<TextBlock>().Any(t => t.Text.StartsWith("Set by this node")), "[nodes refresh] with no managed fields nothing is read-only");
            // A refused write keeps the draft and shows the server's reason.
            server.RefusePut = true; Find<TextBox>(dialog, "Node_group").Text = "rack-z"; await Task.Delay(200); before = server.Count("PUT", "/n-legacy");
            Invoke(DialogButton(dialog, "Save")); await Until(() => server.Count("PUT", "/n-legacy") == before + 1 && NodeIdle(nodes), "[nodes refresh] refused edit sent once");
            await Until(() => All(dialog).OfType<TextBlock>().Any(t => t.Text == "Set by the node\u2019s environment. Change it there and restart the node."), "[nodes refresh] the refusal shows the server's reason in the dialog");
            Check(dialog.IsLoaded && Find<TextBox>(dialog, "Node_group").Text == "rack-z" && !StateFlag(nodes, "ReviewRequired") && Field(nodes, "_dialog") == dialog, "[nodes refresh] the draft stays and the refusal needs no review");
            server.RefusePut = false; await Until(() => DialogButton(dialog, "Save").IsEnabled, "[nodes refresh] Save is available again after a refusal");
            Invoke(DialogButton(dialog, "Save")); await Until(() => !dialog.IsLoaded && server.Count("PUT", "/n-legacy") == before + 2, "[nodes refresh] the corrected draft saves");
            using (var update = JsonDocument.Parse(server.BodyOf("PUT", "/n-legacy"))) Check(new[] { "name", "url", "group", "max_jobs", "max_bandwidth_kbps", "hw_accel_override", "hw_device_override", "source_access" }.All(name => update.RootElement.TryGetProperty(name, out _)), "[nodes refresh] an unmanaged node's save sends the whole form");
            await Until(() => NodeIdle(nodes) && Btn("NodeEdit_n-legacy").IsEnabled, "[nodes refresh] settled after the legacy save");

            // ---- create: the key is offered, shown once and gone after the dialog closes ----
            await OpenDialog(nodes, () => Btn("NodesAdd_transcode"), "[nodes refresh] create dialog opened"); dialog = (ContentDialog)Field(nodes, "_dialog")!;
            Find<TextBox>(dialog, "Node_name").Text = "Created GPU"; Find<TextBox>(dialog, "Node_url").Text = "http://10.0.0.5:8082"; await Task.Delay(300);
            before = server.Count("POST", "/nodes"); Invoke(DialogButton(dialog, "Save")); await Until(() => !dialog.IsLoaded && server.Count("POST", "/nodes") == before + 1, "[nodes refresh] node created");
            await Until(() => Field(nodes, "_dialog") is ContentDialog { IsLoaded: true } && All((ContentDialog)Field(nodes, "_dialog")!).OfType<Button>().Any(b => b.Content?.ToString() == "Issue key"), "[nodes refresh] the key dialog is offered for the new node");
            dialog = (ContentDialog)Field(nodes, "_dialog")!; var keys0 = server.Keys; var keyButton = DialogButton(dialog, "Issue key");
            bool DialogHas(string text) => All(dialog).OfType<TextBlock>().Any(t => t.Text == text); bool DialogStarts(string text) => All(dialog).OfType<TextBlock>().Any(t => t.Text.StartsWith(text, StringComparison.Ordinal));
            Check(!DialogHas(AdminPlaybackAdministrationText.T("web.admin.nodes.key.rotateDrainHint")), "[nodes refresh] an issue offer has no drain hint");
            // The server-address read is held so the dialog is observed while it is still being built: the key is already on screen then.
            services.GetRequiredService<AdminServerSettingsStore>().Invalidate(); var holdEffective = new TaskCompletionSource(); server.HoldEffective = holdEffective;
            Invoke(keyButton); TryInvoke(keyButton);
            await Until(() => All(dialog).OfType<PasswordBox>().Any(), "[nodes refresh] the key arrives in the dialog");
            var keyBox = Find<PasswordBox>(dialog, "NodeIssuedKey"); var shownKey = keyBox.Password;
            Check(dialog.CloseButtonText == "I saved the key" && dialog.PrimaryButtonText.Length == 0 && (dialog.Title as string) == AdminPlaybackAdministrationText.T("web.admin.nodes.key.dialogTitle", new { name = "Created GPU" }), "[nodes refresh] while the dialog is still being built its close button already reads I saved the key");
            dialog.Hide(); await Task.Delay(300);
            Check(dialog.IsLoaded && keyBox.Password == shownKey && Field(nodes, "_dialog") == dialog, "[nodes refresh] a close while the dialog is still being built is refused and keeps the shown key");
            holdEffective.SetResult(); server.HoldEffective = null;
            await Until(() => All(dialog).OfType<TextBox>().Any(box => AutomationProperties.GetAutomationId(box) == "NodeStartSettings"), "[nodes refresh] the key dialog finishes building"); await Task.Delay(300);
            var coreBox = Find<TextBox>(dialog, "NodeCoreUrl"); var startBox = Find<TextBox>(dialog, "NodeStartSettings"); var copySettings = Find<Button>(dialog, "NodeCopySettings");
            Check(server.Keys == keys0 + 1 && shownKey == NodeKey + server.Keys, "[nodes refresh] a doubled activation sends one key request and the key is shown once");
            Check(coreBox.Text == "https://core.invalid:8443", "[nodes refresh] the server address is prefilled from the server's public URL");
            await Capture(dialog, "nodes-refresh-key.png");
            Check(startBox.Text.Contains("••••") && !startBox.Text.Contains(shownKey), "[nodes refresh] the startup settings show the key masked");
            // The key is in no text, text box, tooltip, button or automation property of the open dialog (outside the masked field), nor anywhere in the page.
            Check(HoldsText(dialog, "SILO_CORE_URL=") && !HoldsText(dialog, shownKey) && !HoldsText(nodes, shownKey), "[nodes refresh] the key is in no text, tooltip or automation property of the open dialog or the page");
            // Copy settings and Copy key put exactly the visible values on the clipboard (the fixture reads each back and clears it).
            coreBox.Text = "http://10.0.0.2:8080/"; await Task.Delay(200);
            Check(copySettings.IsEnabled && DialogHas(AdminPlaybackAdministrationText.T("web.admin.nodes.key.settingsHelp")), "[nodes refresh] a plain address enables Copy settings");
            await ClearClipboard(); Invoke(copySettings);
            Check(await ReadClipboard() == "SILO_CORE_URL=http://10.0.0.2:8080\nSILO_NODE_KEY=" + shownKey, "[nodes refresh] Copy settings puts exactly the two visible lines on the clipboard");
            await ClearClipboard(); Invoke(Find<Button>(dialog, "NodeCopyKey"));
            Check(await ReadClipboard() == shownKey, "[nodes refresh] Copy key puts exactly the shown key on the clipboard"); await ClearClipboard();
            // A refused copy says why.
            coreBox.Text = "http://10.0.0.2:8080‎"; await Task.Delay(200);
            Check(!copySettings.IsEnabled && DialogStarts("The server address holds an invisible character"), "[nodes refresh] an address with an invisible character disables Copy settings and says why");
            coreBox.Text = "ftp://10.0.0.2"; await Task.Delay(200);
            Check(!copySettings.IsEnabled && DialogStarts("Enter a plain http or https address"), "[nodes refresh] an address that is not plain http or https disables Copy settings and says why");
            coreBox.Text = ""; await Task.Delay(200);
            Check(!copySettings.IsEnabled && DialogHas(AdminPlaybackAdministrationText.T("web.admin.nodes.key.coreUrlMissing")), "[nodes refresh] no address disables Copy settings with the official line");
            coreBox.Text = "http://10.0.0.2:8080"; await Task.Delay(200);
            Invoke(DialogButton(dialog, "I saved the key")); await Until(() => !dialog.IsLoaded, "[nodes refresh] key dialog closed"); await Task.Delay(500);
            // The objects are kept from before the close, because the dialog's own tree is emptied when it closes: these assert on the controls that held the key.
            SoftCheck(keyBox.Password.Length == 0, "[nodes refresh] the masked key box is empty after the dialog closes");
            SoftCheck(startBox.Text.Length == 0, "[nodes refresh] the startup settings box is empty after the dialog closes");
            SoftCheck(coreBox.Text.Length == 0, "[nodes refresh] the server address box is empty after the dialog closes");
            Check(!All(nodes).OfType<PasswordBox>().Any(box => box.Password.Length > 0) && !HoldsText(nodes, shownKey), "[nodes refresh] no key is kept in the page after the dialog closes");
            await Until(() => NodeIdle(nodes) && Has("NodeKey_n-created"), "[nodes refresh] the created node lists with its key");

            // ---- rotate: destructive, one request, refused and unknown outcomes ----
            await OpenDialog(nodes, () => Btn("NodeKey_n-plain"), "[nodes refresh] rotate confirmation opened"); dialog = (ContentDialog)Field(nodes, "_dialog")!;
            var rotate = DialogButton(dialog, "Rotate key"); Check(rotate.Background is SolidColorBrush { Color: var red } && red == Windows.UI.Color.FromArgb(255, 0xDC, 0x26, 0x26), "[nodes refresh] Rotate key uses the official destructive fill #dc2626");
            await Capture(dialog, "nodes-refresh-rotate.png");
            bool DialogShows(string text) => All(dialog).OfType<TextBlock>().Any(x => x.Text == text && Shown(x));
            Check(DialogShows(AdminPlaybackAdministrationText.T("web.admin.nodes.key.rotateDrainHint")) && (dialog.Title as string) == AdminPlaybackAdministrationText.T("web.admin.nodes.key.rotateTitle", new { name = "Plain GPU" }), "[nodes refresh] a rotation confirmation shows the rotate title and the drain hint");
            var heldKey = new TaskCompletionSource(); server.HoldKey = heldKey; server.RefuseKey = true; keys0 = server.Keys; Invoke(rotate); await Until(() => server.Keys == keys0 + 1, "[nodes refresh] the rotation request is in flight");
            Check(!DialogShows(AdminPlaybackAdministrationText.T("web.admin.nodes.key.rotateDrainHint")) && DialogShows(AdminPlaybackAdministrationText.T("web.admin.nodes.key.issuing")) && (dialog.Title as string) == AdminPlaybackAdministrationText.T("web.admin.nodes.key.rotateTitle", new { name = "Plain GPU" }), "[nodes refresh] while the request runs the drain hint is hidden and the issuing text is shown");
            heldKey.SetResult(); server.HoldKey = null;
            await Until(() => server.Keys == keys0 + 1 && NodeIdle(nodes), "[nodes refresh] refused rotation sent once");
            await Until(() => All(dialog).OfType<TextBlock>().Any(t => t.Text == "Only transcode nodes take a node key."), "[nodes refresh] a refused key shows the server's reason");
            Check(!StateFlag(nodes, "ReviewRequired") && !All(dialog).OfType<PasswordBox>().Any() && DialogButton(dialog, "Close") != null && server.Keys == keys0 + 1, "[nodes refresh] a refusal needs no review, shows no key and is not replayed");
            Check((dialog.Title as string) == AdminPlaybackAdministrationText.T("web.admin.nodes.key.dialogTitle", new { name = "Plain GPU" }), "[nodes refresh] after a refusal the title is the plain key title");
            Invoke(DialogButton(dialog, "Close")); await Until(() => !dialog.IsLoaded, "[nodes refresh] refused key dialog closed"); server.RefuseKey = false;
            await Until(() => NodeIdle(nodes) && Btn("NodeKey_n-plain").IsEnabled, "[nodes refresh] ready to rotate again");
            await OpenDialog(nodes, () => Btn("NodeKey_n-plain"), "[nodes refresh] second rotate confirmation opened"); dialog = (ContentDialog)Field(nodes, "_dialog")!;
            server.DropKey = true; keys0 = server.Keys; Invoke(DialogButton(dialog, "Rotate key")); await Until(() => server.Keys == keys0 + 1 && NodeIdle(nodes), "[nodes refresh] lost rotation sent once");
            await Until(() => StateFlag(nodes, "ReviewRequired") && All(dialog).OfType<TextBlock>().Any(t => t.Text.StartsWith("Could not confirm a new key for Plain GPU")), "[nodes refresh] an unknown outcome is reported and blocks further writes");
            Invoke(DialogButton(dialog, "Close")); await Until(() => !dialog.IsLoaded, "[nodes refresh] unknown key dialog closed"); server.DropKey = false;
            Check(!Btn("NodeKey_n-plain").IsEnabled && server.Keys == keys0 + 1, "[nodes refresh] the key action stays blocked and nothing was replayed");
            Invoke(Btn("NodesReload")); await Until(() => !StateFlag(nodes, "ReviewRequired") && NodeIdle(nodes) && Btn("NodeKey_n-plain").IsEnabled, "[nodes refresh] reading the nodes again releases the block"); Check(server.Keys == keys0 + 1, "[nodes refresh] the reload sent no key request");

            // ---- a key that holds an invisible character is shown but refused for both copies, with the reason ----
            server.KeySuffix = "\u200E"; await Until(() => NodeIdle(nodes) && Btn("NodeKey_n-plain").IsEnabled, "[nodes refresh] ready for an invisible-character key");
            await OpenDialog(nodes, () => Btn("NodeKey_n-plain"), "[nodes refresh] rotate confirmation before an invisible-character key"); dialog = (ContentDialog)Field(nodes, "_dialog")!;
            Invoke(DialogButton(dialog, "Rotate key")); await Until(() => All(dialog).OfType<TextBox>().Any(box => AutomationProperties.GetAutomationId(box) == "NodeStartSettings"), "[nodes refresh] the invisible-character key dialog finishes building"); await Task.Delay(300);
            Check(Find<PasswordBox>(dialog, "NodeIssuedKey").Password.EndsWith("\u200E") && !Find<Button>(dialog, "NodeCopySettings").IsEnabled && DialogStarts("This key holds an invisible character"), "[nodes refresh] Copy settings is off and says the key holds an invisible character");
            await ClearClipboard(); Invoke(Find<Button>(dialog, "NodeCopyKey")); await Task.Delay(300);
            Check(DialogHas("This key holds an invisible character, so it cannot be copied exactly.") && string.IsNullOrEmpty(await ReadClipboard()), "[nodes refresh] Copy key refuses the key, says why and puts nothing on the clipboard");
            Invoke(DialogButton(dialog, "I saved the key")); await Until(() => !dialog.IsLoaded, "[nodes refresh] invisible-character key dialog closed"); server.KeySuffix = "";

            // ---- the row changed under the open confirmation: nothing is sent, the dialog says so and offers to read again ----
            await Until(() => NodeIdle(nodes) && Btn("NodeKey_n-plain").IsEnabled, "[nodes refresh] ready for a changed row");
            await OpenDialog(nodes, () => Btn("NodeKey_n-plain"), "[nodes refresh] rotate confirmation before the row changes"); dialog = (ContentDialog)Field(nodes, "_dialog")!;
            server.Rows["n-plain"]["config_etag"] = "\"n-plain-v9\""; await ((AdminPlaybackNodesController)Field(nodes, "_controller")!).ReloadAsync(); await Until(() => NodeIdle(nodes), "[nodes refresh] the changed row is read");
            keys0 = server.Keys; Invoke(DialogButton(dialog, "Rotate key"));
            await Until(() => DialogStarts("This node changed or another action was running"), "[nodes refresh] a rotation of a changed row says nothing was sent");
            Check(server.Keys == keys0 && !StateFlag(nodes, "ReviewRequired") && !DialogStarts("Could not confirm") && DialogButton(dialog, AdminPlaybackAdministrationText.T("common.retry")).IsEnabled && (dialog.Title as string) == AdminPlaybackAdministrationText.T("web.admin.nodes.key.dialogTitle", new { name = "Plain GPU" }), "[nodes refresh] nothing was sent, nothing needs review, and the dialog offers to read the nodes again");
            Invoke(DialogButton(dialog, AdminPlaybackAdministrationText.T("common.retry"))); await Until(() => !dialog.IsLoaded, "[nodes refresh] reading again closes the dialog");
            await Until(() => NodeIdle(nodes) && Btn("NodeKey_n-plain").IsEnabled, "[nodes refresh] the page is usable again after reading again"); Check(server.Keys == keys0, "[nodes refresh] reading again sent no key request");

            // ---- delete: neutralized name, destructive confirmation, one request on exactly that node; refused and unknown outcomes ----
            await OpenDialog(nodes, () => Btn("NodeDelete_n-legacy"), "[nodes refresh] delete confirmation opened"); dialog = (ContentDialog)Field(nodes, "_dialog")!;
            Check(All(dialog).OfType<TextBlock>().Any(t => t.Text.Contains("Legacy GPU name")) && !All(dialog).OfType<TextBlock>().Any(t => t.Text.Contains('\u202E') || t.Text.Contains('\n')), "[nodes refresh] the confirmation shows the node name as plain text");
            var confirm = DialogButton(dialog, "Delete"); Check(confirm.Background is SolidColorBrush { Color: var fill } && fill == Windows.UI.Color.FromArgb(255, 0xDC, 0x26, 0x26), "[nodes refresh] Delete uses the official destructive fill #dc2626");
            server.RefuseDelete = true; before = server.Count("DELETE", "/n-legacy"); Invoke(confirm); await Until(() => server.Count("DELETE", "/n-legacy") == before + 1 && NodeIdle(nodes), "[nodes refresh] refused delete sent once");
            await Until(() => All(dialog).OfType<TextBlock>().Any(t => t.Text.StartsWith("This node is set by the server\u2019s deployment configuration")), "[nodes refresh] a refused delete shows the server's reason"); Check(dialog.IsLoaded && !StateFlag(nodes, "ReviewRequired"), "[nodes refresh] the confirmation stays and nothing needs review");
            server.RefuseDelete = false; server.DropDelete = true; await Until(() => DialogButton(dialog, "Delete").IsEnabled, "[nodes refresh] Delete is available again");
            Invoke(DialogButton(dialog, "Delete")); await Until(() => server.Count("DELETE", "/n-legacy") == before + 2 && StateFlag(nodes, "ReviewRequired"), "[nodes refresh] a lost delete is an unknown outcome");
            Check(!DialogButton(dialog, "Delete").IsEnabled, "[nodes refresh] the unknown outcome blocks a repeat"); Invoke(DialogButton(dialog, "Cancel")); await Until(() => !dialog.IsLoaded, "[nodes refresh] delete confirmation closed"); server.DropDelete = false;
            Invoke(Btn("NodesReload")); await Until(() => !StateFlag(nodes, "ReviewRequired") && NodeIdle(nodes), "[nodes refresh] reload after the unknown delete");
            await OpenDialog(nodes, () => Btn("NodeDelete_n-legacy"), "[nodes refresh] delete confirmation reopened"); dialog = (ContentDialog)Field(nodes, "_dialog")!;
            before = server.Count("DELETE", "/n-legacy"); var deleteButton = DialogButton(dialog, "Delete"); Invoke(deleteButton); TryInvoke(deleteButton);
            await Until(() => !dialog.IsLoaded && !server.Rows.ContainsKey("n-legacy"), "[nodes refresh] the confirmed node is deleted"); await Task.Delay(300);
            Check(server.Count("DELETE", "/n-legacy") == before + 1 && server.Requests.Last(r => r.Method == "DELETE").IfMatch is { } match && match.StartsWith("\"n-legacy-v"), "[nodes refresh] a doubled confirmation sends one delete with the captured validator");

            // ---- a doubled activation of a command sends one request ----
            await Until(() => NodeIdle(nodes) && Btn("NodeCheck_n-plain").IsEnabled, "[nodes refresh] ready to check"); before = server.Count("POST", "/n-plain/check");
            var holdCheck = new TaskCompletionSource(); server.HoldCheck = holdCheck; Invoke(Btn("NodeCheck_n-plain")); TryInvoke(Btn("NodeCheck_n-plain")); await Until(() => server.Count("POST", "/n-plain/check") == before + 1, "[nodes refresh] the first check is in flight"); await Task.Delay(300);
            TryInvoke(Btn("NodeCheck_n-plain")); await Task.Delay(300); Check(server.Count("POST", "/n-plain/check") == before + 1, "[nodes refresh] a repeated activation while the check is in flight sends nothing"); holdCheck.SetResult(); server.HoldCheck = null; await Until(() => NodeIdle(nodes), "[nodes refresh] check answered"); await Task.Delay(300);
            Check(server.Count("POST", "/n-plain/check") == before + 1, "[nodes refresh] a doubled health check sends one request");

            // ---- states: empty, failed read with a working Retry ----
            server.EmptyList = true; await ((AdminPlaybackNodesController)Field(nodes, "_controller")!).ReloadAsync();
            await Until(() => texts().Contains("No transcode nodes yet. Add one to move transcoding off this server.") && texts().Any(t => t.StartsWith("No proxy nodes yet")), "[nodes refresh] the empty sections are shown");
            await Capture(outer, "nodes-refresh-empty-1440.png"); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(560, 1000)); await Task.Delay(400); await Capture(outer, "nodes-refresh-empty-560.png"); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000)); server.EmptyList = false;
            server.FailList = true; await ((AdminPlaybackNodesController)Field(nodes, "_controller")!).ReloadAsync();
            await Until(() => Shown(Btn("NodesReload")) && texts().Any(t => t.Contains("could not be refreshed")), "[nodes refresh] a failed read shows its error with Retry");
            await Capture(outer, "nodes-refresh-failed-1440.png"); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(560, 1000)); await Task.Delay(400); await Capture(outer, "nodes-refresh-failed-560.png"); window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 1000));
            server.FailList = false; Invoke(Btn("NodesReload")); await Until(() => !Shown(Btn("NodesReload")) && NodeIdle(nodes) && Has("NodeEdit_n-plain"), "[nodes refresh] Retry reads the nodes again");

            // ---- a card that cannot be drawn: the page is not left half built, the reason stays with Reload, and Reload recovers ----
            // Only n-plain keeps a parsable health-check time, so only its card reaches the date formatter; taking the settings service away makes that one card throw (after the proxy section is already drawn).
            var checkedAt = server.Rows.Where(r => r.Key != "n-plain").ToDictionary(r => r.Key, r => r.Value["last_health_check"]?.GetValue<string>());
            foreach (var pair in server.Rows.Where(r => r.Key != "n-plain")) pair.Value["last_health_check"] = null;
            var fullServices = field.GetValue(null); field.SetValue(null, new WithoutService(services, typeof(SettingsService)));
            try
            {
                await ((AdminPlaybackNodesController)Field(nodes, "_controller")!).ReloadAsync();
                await Until(() => texts().Any(t => t.Contains("could not be displayed")) && Shown(Btn("NodesReload")), "[nodes refresh] a card that cannot be drawn shows the error with Reload");
                Check(!All(nodes).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b).StartsWith("NodeEdit_") || AutomationProperties.GetAutomationId(b).StartsWith("NodeDelete_")) && Field(nodes, "_rendered") == null, "[nodes refresh] no stale card or action remains and the window will be drawn again");
                await ((AdminPlaybackNodesController)Field(nodes, "_controller")!).ReloadAsync(); await Task.Delay(300);
                Check(texts().Any(t => t.Contains("could not be displayed")) && Shown(Btn("NodesReload")) && !Has("NodeEdit_n-plain"), "[nodes refresh] a later refresh does not erase the render error while the card still cannot be drawn");
            }
            finally { field.SetValue(null, fullServices); foreach (var (id, value) in checkedAt) server.Rows[id]["last_health_check"] = value; }
            Invoke(Btn("NodesReload")); await Until(() => !texts().Any(t => t.Contains("could not be displayed")) && !Shown(Btn("NodesReload")) && NodeIdle(nodes) && Has("NodeEdit_n-plain") && Has("NodeEdit_n-proxy"), "[nodes refresh] Reload draws the page again and clears the error");

            // ---- teardown: sign-out drops an open key dialog and every key and draft ----
            await OpenDialog(nodes, () => Btn("NodeKey_n-plain"), "[nodes refresh] key dialog before sign-out"); dialog = (ContentDialog)Field(nodes, "_dialog")!;
            Invoke(DialogButton(dialog, "Rotate key")); await Until(() => All(dialog).OfType<TextBox>().Any(box => AutomationProperties.GetAutomationId(box) == "NodeStartSettings"), "[nodes refresh] key shown before sign-out"); await Task.Delay(300);
            // The controls that hold the key are kept, because the dialog's own tree is emptied when it closes.
            var outKey = Find<PasswordBox>(dialog, "NodeIssuedKey"); var outSettings = Find<TextBox>(dialog, "NodeStartSettings"); var outCore = Find<TextBox>(dialog, "NodeCoreUrl");
            Check(outKey.Password.Length > 0 && outSettings.Text.Length > 0 && outCore.Text.Length > 0, "[nodes refresh] the key, the settings and the address are in the dialog before sign-out");
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" });
            await Until(() => !dialog.IsLoaded, "[nodes refresh] sign-out closes the key dialog"); await Task.Delay(500);
            SoftCheck(outKey.Password.Length == 0, "[nodes refresh] sign-out erases the masked key box");
            SoftCheck(outSettings.Text.Length == 0 && outCore.Text.Length == 0, "[nodes refresh] sign-out erases the settings and address boxes");
            await Until(() => !All(nodes).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b).StartsWith("NodeEdit_")), "[nodes refresh] sign-out clears the node list");
            ((IAdminPageSuspension)nodes).SuspendAdminPage();
            Check(Field(nodes, "_controller") == null && Field(nodes, "_dialog") == null && !All(nodes).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b).StartsWith("NodeEdit_")), "[nodes refresh] leaving the page drops the controller, dialogs and cards");
            await ClearClipboard(); ThrowSoftFailures();
        }
        finally
        {
            if (outer.Content is AdminShellPage currentShell && Field(currentShell, "_frame") is Frame currentInner && currentInner.Content is IAdminPageSuspension suspension) suspension.SuspendAdminPage();
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(outer.XamlRoot).ToArray()) foreach (var open in All(popup.Child).OfType<ContentDialog>()) open.Hide();
            var retired = outer.Content as FrameworkElement; outer.Content = null; outer.BackStack.Clear(); outer.ForwardStack.Clear();
            await Until(() => retired?.IsLoaded != true, "[nodes refresh] shell unloaded before restoring globals");
            window.Content = previousContent; field.SetValue(null, previousServices);
        }
    }
}
