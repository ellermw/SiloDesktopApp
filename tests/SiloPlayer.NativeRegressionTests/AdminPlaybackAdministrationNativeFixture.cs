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
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>Real production registration/shell/Frame; compiles before implementation for coordinator RED.</summary>
internal static class AdminPlaybackAdministrationNativeFixture
{
    private static void Check(bool value, string label) { Program.Log((value ? "PASS: " : "FAIL: ") + label); if (!value) throw new InvalidOperationException(label); }
    private static Func<string>? Diagnose;
    private static async Task Until(Func<bool> predicate, string label)
    {
        for (var i = 0; i < 240 && !predicate(); i++) await Task.Delay(25);
        if (!predicate()) { try { Program.Log("DIAGNOSTIC: " + Diagnose?.Invoke()); } catch (Exception failure) { Program.Log("DIAGNOSTIC failed: " + failure.GetType().Name); } }
        Check(predicate(), label);
    }
    private sealed class Server : HttpMessageHandler
    {
        internal int Writes, TaskRuns; internal string? IfMatch, LastBody; internal bool Conflict;
        internal bool RejectNodePut, DropNodePutAck, FailNodesList, EmptyNodes, DropTaskRunAck, RefuseTaskRun, RunMarksRunning, HeldTasksStarted;
        internal TaskCompletionSource? HoldTasks, HoldReprobe;
        internal string? AddedNode;
        internal string Tasks = """{"items":[{"key":"sync_markers","name":"Sync online markers","state":"idle","last_execution":{"task_key":"sync_markers","status":"failed","completed_at":"2026-11-01T10:00:00Z","error_message":"Synthetic provider unreachable"}},{"key":"detect_intro_markers","name":"Detect markers on this server","state":"idle","last_execution":{"task_key":"detect_intro_markers","status":"cancelled","completed_at":"2026-11-01T11:00:00Z"}},{"key":"contribute_markers","name":"Share detected markers","state":"idle","last_execution":{"task_key":"contribute_markers","status":"completed","completed_at":"2026-11-01T12:00:00Z","result_data":{"submitted":1200,"skipped":3,"invalid":0,"failed":2}}}]}""";
        internal readonly Dictionary<string, string> Values = new() { ["playback.hw_accel"] = "auto", ["playback.transcode_enabled"] = "true", ["allow_4k_transcode"] = "true", ["markers.mode"] = "both", ["markers.online_storage"] = "stored", ["playback.preview_image_width"] = "300", ["playback.trickplay_interval_seconds"] = "10" };
        internal string Node = """{"id":"fixture-node","name":"Synthetic GPU","type":"transcode","url":"https://worker.invalid","enabled":true,"healthy":true,"group":"rack-a","active_jobs":2,"max_jobs":4,"egress_kbps":0,"max_bandwidth_kbps":0,"drain_requested":false,"drain_state":"none","key_issued":true,"config_etag":"\"node-1\"","capabilities":{"resolved":"qsv","render_devices":["/dev/dri/renderD128"],"transport_features":["trickplay_extract_v2"]},"network_access":{"synthetic-overlay":{"state":"connected","hostname":"synthetic-node"}}}""";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; object body = new { };
            if (request.Method == HttpMethod.Put)
            {
                Writes++; IfMatch = request.Headers.IfMatch.SingleOrDefault()?.ToString(); LastBody = await request.Content!.ReadAsStringAsync(ct);
                if (Conflict) return Json(new { type = "precondition_failed", status = 412 }, HttpStatusCode.PreconditionFailed);
                if (!path.EndsWith("/settings") && RejectNodePut) return Json(new { type = "validation_failed", status = 422 }, HttpStatusCode.UnprocessableEntity);
                if (!path.EndsWith("/settings") && DropNodePutAck) throw new HttpRequestException("synthetic lost acknowledgement");
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
                if (path.EndsWith("/run"))
                {
                    TaskRuns++; if (DropTaskRunAck) throw new HttpRequestException("synthetic lost acknowledgement");
                    if (RefuseTaskRun) return Json(new { type = "conflict", status = 409 }, HttpStatusCode.Conflict);
                    var key = path.Split('/')[^2];
                    if (RunMarksRunning)
                    {
                        var tasks = JsonNode.Parse(Tasks)!; foreach (var item in tasks["items"]!.AsArray()) if (item!["key"]!.GetValue<string>() == key) item["state"] = "running";
                        Tasks = tasks.ToJsonString();
                    }
                    return Json(new { key, name = key, state = "running" });
                }
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
                // A held re-probe is the node still probing its hardware: the request was received and has not answered.
                if (path.EndsWith("/reprobe")) { var hold = HoldReprobe; HoldReprobe = null; if (hold != null) await hold.Task.WaitAsync(ct); }
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
            else if (path.EndsWith("/nodes") && FailNodesList) return Json(new { type = "internal", status = 500 }, HttpStatusCode.InternalServerError);
            else if (path.EndsWith("/nodes") && EmptyNodes) return JsonText("{\"items\":[]}");
            else if (path.EndsWith("/nodes")) return JsonText("{\"items\":[" + Node + (AddedNode == null ? "" : "," + AddedNode) + "]}");
            else if (path.EndsWith("/admin/tasks"))
            {
                // A held answer is the state at the moment the request was received: it goes stale if a run lands while it is parked.
                var snapshot = Tasks; var hold = HoldTasks; HoldTasks = null;
                if (hold != null) { HeldTasksStarted = true; await hold.Task.WaitAsync(ct); }
                return JsonText(snapshot);
            }
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
            Diagnose = () =>
            {
                var current = inner.Content as FrameworkElement; var builder = new StringBuilder("writes=" + server.Writes + " dialogOpen=" + (current != null && Field(current, "_dialog") != null));
                if (current != null)
                {
                    
                    if (Field(current, "_dialog") is ContentDialog open) builder.Append(" dialog=[").Append(string.Join(" | ", All(open).OfType<TextBlock>().Where(t => t.Text.Length > 0).Select(t => t.Text).Take(40))).Append(']');
                    if (Field(current, "_controller") is { } controller) { var state = controller.GetType().GetProperty("State")!.GetValue(controller)!; builder.Append(" state=").Append(string.Join(",", new[] { "Busy", "Loading", "Loaded", "ReviewRequired" }.Select(name => name + "=" + state.GetType().GetProperty(name)!.GetValue(state)))); }
                }
                return builder.ToString();
            };
            Invoke(Find<Button>(nodes, "NodeCheck_fixture-node")); await Until(() => server.Writes == 1, "health check sent once");
            await Until(() => NodeIdle(nodes) && Find<Button>(nodes, "NodeEdit_fixture-node").IsEnabled, "health reconciliation finished");
            var beforeCommand = server.Writes; Invoke(Find<Button>(nodes, "NodeReprobe_fixture-node")); await Until(() => server.Writes == beforeCommand + 1 && NodeIdle(nodes) && !Reprobing(nodes), "reprobe sent once and reconciled");
            // A re-probe that takes minutes is pending for its own node only: the page is not busy, the other actions stay usable and the admin can leave.
            await Until(() => NodeIdle(nodes) && !Reprobing(nodes) && Find<Button>(nodes, "NodeReprobe_fixture-node").IsEnabled, "re-probe button ready again");
            var heldProbe = new TaskCompletionSource(); server.HoldReprobe = heldProbe; beforeCommand = server.Writes; Invoke(Find<Button>(nodes, "NodeReprobe_fixture-node"));
            await Until(() => server.Writes == beforeCommand + 1 && Reprobing(nodes) && !Find<Button>(nodes, "NodeReprobe_fixture-node").IsEnabled && Find<Button>(nodes, "NodeCheck_fixture-node").IsEnabled && Find<Button>(nodes, "NodeEdit_fixture-node").IsEnabled, "a pending re-probe holds back only its own button");
            Check(!StateFlag(nodes, "Busy") && await ((IAdminNavigationGuard)nodes).CanLeaveAsync(), "a pending re-probe neither holds the page busy nor refuses navigation");
            heldProbe.SetResult(); await Until(() => NodeIdle(nodes) && !Reprobing(nodes) && Find<Button>(nodes, "NodeReprobe_fixture-node").IsEnabled, "the re-probe answer releases its button");
            Check(server.Writes == beforeCommand + 1, "the pending re-probe was sent once");
            await Until(() => DrainLabel(nodes) == "Drain Synthetic GPU", "drain action rendered"); beforeCommand = server.Writes; Invoke(Find<Button>(nodes, "NodeDrain_fixture-node")); await Until(() => server.Writes == beforeCommand + 1 && NodeIdle(nodes) && server.Node.Contains("draining"), "drain acknowledgement reconciled");
            await Until(() => DrainLabel(nodes) == "Resume Synthetic GPU", "resume action rendered after the drain reload"); beforeCommand = server.Writes; Invoke(Find<Button>(nodes, "NodeDrain_fixture-node")); await Until(() => server.Writes == beforeCommand + 1 && NodeIdle(nodes) && !server.Node.Contains("draining"), "resume acknowledgement reconciled");
            await OpenDialog(nodes, Find<Button>(inner, "NodeEdit_fixture-node"), "edit dialog opened");
            var dialog = (ContentDialog)Field(nodes, "_dialog")!;
            Check(All(dialog).OfType<ComboBox>().Any(c => AutomationProperties.GetAutomationId(c) == "Node_source_access"), "source access capability shows local/core selector");
            Check(Find<ComboBox>(dialog, "Node_hw_accel_override").Items.OfType<ComboBoxItem>().Select(item => (string)item.Tag).SequenceEqual(new[] { "inherit", "auto", "qsv", "vaapi", "nvenc", "none" }), "node acceleration override offers the server's enum without VideoToolbox");
            await Capture(dialog, "playback-node-editor.png"); Find<TextBox>(dialog, "Node_name").Text = "Synthetic GPU edited"; await Task.Delay(300);
            Invoke(DialogButton(dialog, "Save")); await Until(() => !dialog.IsLoaded, "editor saved and closed");
            Check(server.IfMatch == "\"node-1\"", "node editor sends captured configuration validator");
            using (var update = JsonDocument.Parse(server.LastBody!)) Check(update.RootElement.GetProperty("source_access").GetString() == "local", "typed node editor sends source access");
            await Until(() => Find<Button>(nodes, "NodeKey_fixture-node").IsEnabled, "node ready for key rotation");
            await OpenDialog(nodes, Find<Button>(nodes, "NodeKey_fixture-node"), "key rotation confirmation opened");
            dialog = (ContentDialog)Field(nodes, "_dialog")!; var beforeKey = server.Writes; Invoke(DialogButton(dialog, "Rotate key"));
            await Until(() => All(dialog).OfType<PasswordBox>().Any(), "one-time key arrives in private dialog");
            Check(server.Writes == beforeKey + 1 && Find<PasswordBox>(dialog, "NodeIssuedKey").Password == "synthetic-one-time-key", "key is issued once");
            await Capture(dialog, "playback-node-key.png"); Invoke(DialogButton(dialog, "I saved the key")); await Until(() => !dialog.IsLoaded, "key dialog closed");
            Check(All(dialog).OfType<PasswordBox>().All(box => box.Password.Length == 0), "key erased after close");
            await OpenDialog(nodes, Find<Button>(nodes, "NodesAdd_proxy"), "proxy creation dialog opened");
            dialog = (ContentDialog)Field(nodes, "_dialog")!; Find<TextBox>(dialog, "Node_name").Text = "Created proxy"; Find<TextBox>(dialog, "Node_url").Text = "https://proxy.invalid"; await Task.Delay(300);
            Invoke(DialogButton(dialog, "Save")); await Until(() => !dialog.IsLoaded && server.AddedNode != null, "proxy create saved");
            await Until(() => Field(nodes, "_dialog") == null && NodeIdle(nodes) && All(nodes).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "NodeDelete_created-node" && b.IsEnabled), "created proxy is settled and its editor fully closed before delete");
            await OpenDialog(nodes, Find<Button>(nodes, "NodeDelete_created-node"), "delete confirmation opened");
            dialog = (ContentDialog)Field(nodes, "_dialog")!; Invoke(DialogButton(dialog, "Delete")); await Until(() => !dialog.IsLoaded && server.AddedNode == null, "guarded delete accepts empty 204");
            Invoke(Find<Button>(nodes, "NodeRoutingPreset_gpu")); await Until(() => Find<Button>(nodes, "NodeRoutingSave").IsEnabled, "routing preset stages own draft");
            Invoke(Find<Button>(nodes, "NodeRoutingSave")); await Until(() => server.Values.GetValueOrDefault("playback.routing.remux_execution") == "prefer_api" && !Shown(Find<Button>(nodes, "NodeRoutingSave")), "routing preset saved with exact five-key contract");
            // Review finding 1: a rejected or unconfirmed enabled update returns the switch to the saved value.
            await Until(() => NodeIdle(nodes) && Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node").IsEnabled, "node switch ready");
            var enabledSwitch = Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node"); Check(enabledSwitch.IsOn, "enabled switch shows the saved enabled node");
            server.RejectNodePut = true; var beforeReject = server.Writes; enabledSwitch.IsOn = false;
            await Until(() => server.Writes == beforeReject + 1 && NodeIdle(nodes), "disable sent once and rejected");
            await Until(() => Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node").IsOn, "rejected update restores the enabled switch to its saved value");
            await Until(() => All(nodes).OfType<TextBlock>().Any(t => t.Text.Contains("refused")), "rejection is reported to the admin"); Check(!StateFlag(nodes, "ReviewRequired"), "validation rejection needs no review");
            server.RejectNodePut = false; server.DropNodePutAck = true; var beforeLost = server.Writes; Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node").IsOn = false;
            await Until(() => server.Writes == beforeLost + 1 && NodeIdle(nodes), "disable with lost acknowledgement sent once");
            await Until(() => Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node").IsOn, "unknown outcome keeps the saved switch value");
            await Until(() => StateFlag(nodes, "ReviewRequired") && !Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node").IsEnabled, "unknown outcome blocks further writes until reload");
            server.DropNodePutAck = false; Invoke(Find<Button>(nodes, "NodesReload")); await Until(() => !StateFlag(nodes, "ReviewRequired") && NodeIdle(nodes), "explicit reload reconciles the unknown outcome");
            Check(server.Writes == beforeLost + 1, "unknown outcome was never replayed");
            // A 412 on the enabled switch is a conflict: the switch returns to the saved value, writes stay blocked, nothing is replayed, and a reload recovers.
            server.Conflict = true; var beforePrecondition = server.Writes; Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node").IsOn = false;
            await Until(() => server.Writes == beforePrecondition + 1 && NodeIdle(nodes), "disable answered with a 412 conflict sent once");
            await Until(() => Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node").IsOn, "conflict keeps the saved enabled value showing");
            await Until(() => StateFlag(nodes, "ReviewRequired") && !Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node").IsEnabled, "conflict blocks further node writes until reload");
            await Until(() => All(nodes).OfType<TextBlock>().Any(t => t.Text.Contains("changed on the server")) && Shown(Find<Button>(nodes, "NodesReload")), "conflict is reported with a reload action");
            server.Conflict = false; Invoke(Find<Button>(nodes, "NodesReload")); await Until(() => !StateFlag(nodes, "ReviewRequired") && NodeIdle(nodes) && Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node").IsEnabled, "reload after the conflict restores node controls");
            Check(server.Writes == beforePrecondition + 1, "conflict was never replayed");
            // A failed background refresh keeps the rows on screen, shows the error and keeps polling (official react-query page); it does not lock the controls.
            server.FailNodesList = true; await ((AdminPlaybackNodesController)Field(nodes, "_controller")!).ReloadAsync();
            await Until(() => Shown(Find<Button>(nodes, "NodesReload")) && All(nodes).OfType<TextBlock>().Any(t => t.Text.Contains("could not be refreshed")), "node list refresh failure shows the error with a reload action");
            Check(!StateFlag(nodes, "ReviewRequired") && Find<ToggleSwitch>(nodes, "NodeEnabled_fixture-node").IsEnabled && NodeIdle(nodes), "refresh failure keeps the loaded node and its controls usable");
            server.FailNodesList = false; server.EmptyNodes = true; Invoke(Find<Button>(nodes, "NodesReload")); await Until(() => !StateFlag(nodes, "ReviewRequired") && NodeIdle(nodes) && !Shown(Find<Button>(nodes, "NodesReload")), "reload after failure");
            await Until(() => All(nodes).OfType<TextBlock>().Any(t => t.Text == "No transcode nodes yet. Add one to move transcoding off this server."), "empty transcode section is shown");
            await Capture(outer, "playback-nodes-empty.png"); server.EmptyNodes = false;
            AdminShellPage.TryOpenHref("/admin/settings/playback"); await Until(() => inner.Content?.GetType().Name == "AdminPlaybackSettingsPage" && All(inner).OfType<ToggleSwitch>().Any(), "Playback Settings enters the registered page");
            var playback = (FrameworkElement)inner.Content;
            await Until(() => Shown(Find<FrameworkElement>(playback, "PlaybackRow_playback.preview_image_width")), "trickplay capability reveals preview settings");
            // Review finding 2: the last execution status of each marker task is rendered as the official badge and result line.
            await Until(() => All(playback).Any(c => c is FrameworkElement e && AutomationProperties.GetAutomationId(e) == "PlaybackTaskStatus_sync_markers"), "marker task status badges render");
            string BadgeText(string key) => All(Find<Border>(playback, "PlaybackTaskStatus_" + key)).OfType<TextBlock>().Single().Text;
            Check(BadgeText("sync_markers") == "failed" && BadgeText("detect_intro_markers") == "cancelled" && BadgeText("contribute_markers") == "Completed with errors", "badge shows the failed, cancelled and completed-with-errors last executions");
            Check(Find<TextBlock>(playback, "PlaybackTaskResult_contribute_markers").Text.Replace(' ', ' ') == "Last result: 1,200 submitted, 3 skipped, 2 failed", "contribute result line summarises the last run");
            Check(Find<TextBlock>(playback, "PlaybackTaskResult_sync_markers").Text.StartsWith("Last run: "), "other tasks show the last run date");
            Check(ToolTipService.GetToolTip(Find<Border>(playback, "PlaybackTaskStatus_sync_markers")) as string == "Synthetic provider unreachable", "failed run shows the server error as the badge tooltip");
            await Capture(Find<StackPanel>(playback, "PlaybackMarkerTasks"), "playback-settings-marker-tasks.png");
            var beforeRun = server.TaskRuns; Invoke(Find<Button>(playback, "PlaybackTaskRun_sync_markers")); await Until(() => server.TaskRuns == beforeRun + 1, "marker task run sent once");
            server.DropTaskRunAck = true; beforeRun = server.TaskRuns; await Until(() => Find<Button>(playback, "PlaybackTaskRun_detect_intro_markers").IsEnabled, "second task can run");
            Invoke(Find<Button>(playback, "PlaybackTaskRun_detect_intro_markers")); await Until(() => server.TaskRuns == beforeRun + 1 && !Find<Button>(playback, "PlaybackTaskRun_detect_intro_markers").IsEnabled, "lost acknowledgement leaves the task marked unconfirmed and is not replayed");
            server.DropTaskRunAck = false;
            // A definite 4xx refusal means nothing started: the row is not locked as unconfirmed and can be retried.
            server.RefuseTaskRun = true; beforeRun = server.TaskRuns; await Until(() => Find<Button>(playback, "PlaybackTaskRun_contribute_markers").IsEnabled, "third task can run");
            Invoke(Find<Button>(playback, "PlaybackTaskRun_contribute_markers")); await Until(() => server.TaskRuns == beforeRun + 1, "refused run sent once");
            await Until(() => Find<Button>(playback, "PlaybackTaskRun_contribute_markers").IsEnabled, "a refused run leaves the task runnable instead of unconfirmed");
            Check(server.TaskRuns == beforeRun + 1, "refused run was never replayed"); server.RefuseTaskRun = false;
            // A task poll sent before a run must not flip the running task back to idle when its older answer arrives.
            typeof(AdminPlaybackSettingsPage).GetField("_focused", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(playback, true); typeof(AdminPlaybackSettingsPage).GetField("_visible", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(playback, true);
            await Until(() => Find<Button>(playback, "PlaybackTaskRun_sync_markers").IsEnabled, "first task is idle and can run again");
            server.RunMarksRunning = true; var heldPoll = new TaskCompletionSource(); server.HoldTasks = heldPoll;
            var poll = (Task)typeof(AdminPlaybackSettingsPage).GetMethod("RefreshTasks", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(playback, null)!;
            await Until(() => server.HeldTasksStarted, "task poll is in flight and answered idle");
            var pendingTasks = (IReadOnlyCollection<string>)Field(Field(playback, "_panel")!, "_pendingTasks")!;
            beforeRun = server.TaskRuns; Invoke(Find<Button>(playback, "PlaybackTaskRun_sync_markers")); await Until(() => server.TaskRuns == beforeRun + 1 && pendingTasks.Count == 0 && !Find<Button>(playback, "PlaybackTaskRun_sync_markers").IsEnabled, "run lands, is reconciled with the server and the row shows running");
            heldPoll.SetResult(); await poll;
            await Task.Delay(300); Check(!Find<Button>(playback, "PlaybackTaskRun_sync_markers").IsEnabled, "the older idle poll answer does not flip the running task back to idle");
            server.RunMarksRunning = false;
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
            await Until(() => !Find<Button>(outer, "PlaybackSave").IsEnabled, "conflicted draft cannot be saved again until reviewed"); Check(server.Writes == beforeConflict + 1, "conflict does not automatically replay");
            server.Conflict = false; Invoke(Find<Button>(playback, "PlaybackReload")); await Until(() => !StateFlag(playback, "Conflict") && !StateFlag(playback, "Loading") && Find<Button>(outer, "PlaybackSave").IsEnabled, "explicit reload preserves draft for review");
            Invoke(Find<Button>(outer, "PlaybackSave")); await Until(() => !Shown(Find<Button>(outer, "PlaybackSave")), "reviewed draft saves after reconciliation");
            // A hidden row's invalid draft must not keep blocking Save: buffer-ahead hides when throttling goes off.
            var throttle = Find<ToggleSwitch>(playback, "Playback_enable_transcode_throttle"); Check(throttle.IsOn && Shown(Find<FrameworkElement>(playback, "PlaybackRow_transcode_throttle_seconds")), "throttling defaults on and shows buffer ahead");
            Find<TextBox>(playback, "Playback_transcode_throttle_seconds").Text = "5";
            await Until(() => Shown(Find<Button>(outer, "PlaybackSave")) && !Find<Button>(outer, "PlaybackSave").IsEnabled, "invalid buffer ahead blocks Save");
            throttle.IsOn = false;
            await Until(() => !Shown(Find<FrameworkElement>(playback, "PlaybackRow_transcode_throttle_seconds")) && Find<Button>(outer, "PlaybackSave").IsEnabled, "hiding the row drops its unseen error so the throttle change can be saved");
            Check(Find<ComboBox>(playback, "Playback_playback.hw_accel").Items.OfType<ComboBoxItem>().Any(item => (string)item.Tag == "videotoolbox"), "cluster-wide acceleration still offers VideoToolbox");
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
    private static string? DrainLabel(FrameworkElement page) => All(page).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "NodeDrain_fixture-node") is { IsEnabled: true } button ? AutomationProperties.GetName(button) : null;
    /// <summary>
    /// A click is dropped while the previous dialog is still closing, so it is repeated until the next dialog is open. It is only repeated while no dialog is open
    /// (opening one writes nothing), which is what made the step after the editor closes flaky.
    /// </summary>
    private static async Task OpenDialog(object page, Button button, string label)
    {
        for (var attempt = 0; attempt < 40 && Field(page, "_dialog") is not ContentDialog { IsLoaded: true }; attempt++)
        {
            if (Field(page, "_dialog") == null && button.IsEnabled) Invoke(button);
            for (var wait = 0; wait < 20 && Field(page, "_dialog") is not ContentDialog { IsLoaded: true }; wait++) await Task.Delay(25);
        }
        Check(Field(page, "_dialog") is ContentDialog { IsLoaded: true }, label);
    }
    private static bool NodeIdle(object page)
    {
        var controller = Field(page, "_controller"); if (controller == null) return false;
        var state = controller.GetType().GetProperty("State")!.GetValue(controller)!;
        return !(bool)state.GetType().GetProperty("Busy")!.GetValue(state)! && !(bool)state.GetType().GetProperty("Loading")!.GetValue(state)!;
    }
    private static bool Reprobing(object page) => Field(page, "_controller") is AdminPlaybackNodesController controller && controller.State.Reprobing.Count > 0;
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
