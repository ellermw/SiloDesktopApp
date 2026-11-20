using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
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

/// <summary>
/// Manage > Libraries > Collections through production registration, the real Admin shell and its nested Frame, against a
/// synthetic stateful server only. Each group below drives real page states (loading, error, empty, populated), every dialog and
/// every mutation, including failed and unknown-outcome mutations. Groups report independently so one failure does not hide the others.
/// </summary>
internal static class AdminCollectionsNativeFixture
{
    private static readonly List<string> Failures = [];
    private static void Check(bool value, string text)
    {
        Program.Log((value ? "PASS: " : "FAIL: ") + text);
        if (!value) Failures.Add(text);
    }
    private static async Task Until(Func<bool> condition, string label, int milliseconds = 8000)
    {
        for (var i = 0; i < milliseconds / 25 && !condition(); i++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Timed out: " + label);
    }
    private static async Task Step(string name, Func<Task> body)
    {
        Program.Log("GROUP: " + name);
        try { await body(); }
        catch (Exception error) { Check(false, name + " threw " + error.GetType().Name + ": " + error.Message); }
    }
    private sealed class Realtime : IAdminRealtimeChannel
    {
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived { add { } remove { } }
        public event Action<string, string, JsonElement>? EventReceived { add { } remove { } }
        public event Action<WebSocketState>? StateChanged { add { } remove { } }
        public IDisposable Subscribe(params string[] channels) => new Release();
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = JsonDocument.Parse("[]").RootElement.Clone(); return channel == "scans"; }
        private sealed class Release : IDisposable { public void Dispose() { } }
    }
    private sealed class Server : HttpMessageHandler
    {
        public readonly ConcurrentQueue<string> Requests = new();
        public readonly ConcurrentQueue<(string Method, string Path, string Body, string? IfMatch)> Log = new();
        public Dictionary<string, JsonObject> Collections = Fresh();
        public readonly List<JsonObject> Groups = [JsonNode.Parse("""{"id":"g1","library_id":"1","name":"Fixture shelf","kind":"regular","default_sort_mode":"manual","sort_order":0}""")!.AsObject()];
        public readonly List<string> GroupOrder = ["g1", "ungrouped"];
        public JsonObject Row = JsonNode.Parse("""{"id":"s1","title":"Shared fixture row","section_type":"collection_posters","config":{"keep":"yes","collections":[{"library_collection_id":"c1"},{"library_collection_id":"other"}]}}""")!.AsObject();
        /// <summary>Returns a response to answer a request itself (a failure, a lost reply, a malformed body), or null to let the server answer.</summary>
        public Func<string, string, Task<HttpResponseMessage?>>? Intercept;
        public TaskCompletionSource? Hold;
        public bool EmptyList, MdbListSearch = true, JobTerminal;
        public int JobPolls;
        private int _revision = 1, _created;
        public static Dictionary<string, JsonObject> Fresh() => new()
        {
            ["c1"] = JsonNode.Parse("""{"id":"c1","title":"Fixture manual","collection_type":"manual","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":1,"group_id":null,"sort_order":0,"row_count":1,"home_row_count":1,"featured":false,"description":"Synthetic only","query_definition":{},"sort_config":{},"source_config":{}}""")!.AsObject(),
            ["c2"] = JsonNode.Parse("""{"id":"c2","title":"Fixture smart","collection_type":"smart","library_id":"1","library_ids":["1"],"visibility":"hidden","item_count":2,"group_id":"g1","sort_order":0,"featured":false,"description":"","query_definition":{"library_ids":[1],"match":"all","groups":[{"match":"all","rules":[{"field":"year","op":"gte","value":2000}]}]},"sort_config":{"field":"title","order":"asc","keep":true},"source_config":{}}""")!.AsObject(),
            ["c3"] = JsonNode.Parse("""{"id":"c3","title":"Fixture synced","collection_type":"mdblist","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":3,"group_id":null,"sort_order":1,"featured":false,"description":"","last_sync_status":"failed","last_sync_message":"Synthetic","source_url":"https://mdblist.com/lists/a/b/json","query_definition":{},"sort_config":{},"source_config":{"mode":"mdblist_json","url":"https://mdblist.com/lists/a/b/json"}}""")!.AsObject()
        };
        public IEnumerable<(string Method, string Path, string Body, string? IfMatch)> Sent(string method, string pathStart) => Log.Where(l => l.Method == method && l.Path.StartsWith(pathStart, StringComparison.Ordinal));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; var method = request.Method.Method;
            var text = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Enqueue(method + " " + request.RequestUri.PathAndQuery);
            Log.Enqueue((method, request.RequestUri.PathAndQuery, text, request.Headers.IfMatch.SingleOrDefault()?.ToString()));
            if (Intercept != null && await Intercept(method, request.RequestUri.PathAndQuery) is { } injected) return injected;
            var body = text.Length == 0 || !text.TrimStart().StartsWith('{') ? new JsonObject() : JsonNode.Parse(text)!.AsObject();
            var mutating = request.Method != HttpMethod.Get;
            var guarded = request.Method == HttpMethod.Patch || request.Method == HttpMethod.Delete && !path.Contains("/items/") || path.EndsWith("/order") && mutating;
            if (guarded && request.Headers.IfMatch.SingleOrDefault()?.ToString() != $"\"r{_revision}\"") return Reply(new() { ["type"] = "conflict", ["status"] = 412 }, HttpStatusCode.PreconditionFailed);
            if (path == "/api/v2/libraries") return Reply(JsonNode.Parse("""{"items":[{"id":"1","name":"Fixture movies","type":"movies","paths":["/synthetic"],"enabled":true}],"page":{"has_more":false}}""")!.AsObject());
            if (path == "/api/v2/admin/collections/capabilities")
            {
                if (Hold != null) { var hold = Hold; Hold = null; await hold.Task; }
                return Reply(JsonNode.Parse($$"""{"state":"available","groups":true,"imports":true,"import_sources":["mdblist","tmdb","tmdb_list"],"artwork":true,"item_reorder":true,"section_references":true,"template_summaries":true,"mdblist_search":{{MdbListSearch.ToString().ToLowerInvariant()}}}""")!.AsObject());
            }
            if (path == "/api/v2/admin/collections")
            {
                if (request.Method == HttpMethod.Post)
                {
                    var id = "new" + ++_created; var item = (JsonObject)body.DeepClone(); item["id"] = id; item["library_id"] = "1"; item["item_count"] = 0; item["group_id"] = null; item["featured"] = false; item["sort_order"] = 9; Collections[id] = item; _revision++; return Reply(item, HttpStatusCode.Created);
                }
                return Reply(new() { ["items"] = new JsonArray(EmptyList ? [] : Collections.Values.Select(v => v.DeepClone()).ToArray()), ["groups"] = new JsonArray(), ["page"] = new JsonObject { ["has_more"] = false } });
            }
            if (path == "/api/v2/admin/collections/import/mdblist")
            {
                var id = "imp" + ++_created;
                var item = JsonNode.Parse($$"""{"id":"{{id}}","title":{{JsonValue.Create(body["title"]?.ToString() ?? "")!.ToJsonString()}},"collection_type":"mdblist","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":0,"group_id":null,"sort_order":9,"featured":false,"description":"","query_definition":{},"sort_config":{},"source_config":{"mode":"mdblist_json","url":{{JsonValue.Create(body["url"]?.ToString() ?? "")!.ToJsonString()}}}}""")!.AsObject();
                Collections[id] = item; _revision++; return Reply(new() { ["collection"] = item.DeepClone() }, HttpStatusCode.Created);
            }
            if (path == "/api/v2/collections/import/mdblist/search")
                return Reply(JsonNode.Parse("""{"configured":true,"items":[{"id":"12","user_id":"3","user_name":"someone","name":"Top watched fixture","slug":"top","description":"Synthetic list","media_type":"movie","items":100,"likes":9,"url":"https://mdblist.com/lists/someone/top"}],"page":{"has_more":false}}""")!.AsObject());
            if (path == "/api/v2/admin/collections/templates")
                return Reply(JsonNode.Parse("""{"categories":[{"id":"movies","title":"Movies","templates":[{"id":"t1","title":"Fixture template","description":"Synthetic template","source":"tmdb","tmdb":{"preset":"popular","media_type":"movie"},"default_limit":40,"default_sync_schedule":"0 3 * * *"}]}]}""")!.AsObject());
            if (path == "/api/v2/admin/collections/template-bundles")
                return Reply(JsonNode.Parse("""{"bundles":[{"id":"pack1","title":"Fixture pack","templates":[{"id":"t1","title":"Fixture template"}]}]}""")!.AsObject());
            if (path.StartsWith("/api/v2/admin/collections/template-bundles/"))
            {
                if (path.EndsWith("/apply-job")) return Reply(new() { ["id"] = "job1", ["state"] = "queued", ["terminal"] = false }, HttpStatusCode.Accepted);
                return Reply(JsonNode.Parse("""{"created":[{"template_title":"Fixture template","library_name":"Fixture movies","reason":"would create"}]}""")!.AsObject());
            }
            if (path == "/api/v2/admin/collection-jobs/job1")
            {
                JobPolls++;
                return Reply(JsonNode.Parse($$"""{"id":"job1","state":"{{(JobTerminal ? "succeeded" : "running")}}","terminal":{{JobTerminal.ToString().ToLowerInvariant()}},"template_result":{"created":[{"template_title":"Fixture template","library_name":"Fixture movies","reason":"created"}]}}""")!.AsObject());
            }
            if (path == "/api/v2/admin/libraries/1/collection-groups")
            {
                if (request.Method == HttpMethod.Post)
                {
                    var shelf = JsonNode.Parse($$"""{"id":"g{{Groups.Count + 1}}","library_id":"1","name":{{JsonValue.Create(body["name"]?.ToString() ?? "")!.ToJsonString()}},"kind":"regular","default_sort_mode":"{{body["default_sort_mode"]}}","sort_order":{{Groups.Count}}}""")!.AsObject();
                    Groups.Add(shelf); GroupOrder.Insert(GroupOrder.Count - 1, shelf["id"]!.ToString()); _revision++; return Reply(shelf, HttpStatusCode.Created);
                }
                return Reply(new() { ["items"] = new JsonArray(Groups.Select(g => g.DeepClone()).ToArray()) });
            }
            if (path.EndsWith("collection-groups/order"))
            {
                if (mutating) { GroupOrder.Clear(); GroupOrder.AddRange(body["ordered_ids"]!.AsArray().Select(v => v!.ToString())); _revision++; }
                return Reply(new() { ["ordered_ids"] = new JsonArray(GroupOrder.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()), ["has_more"] = false });
            }
            if (path.StartsWith("/api/v2/admin/collection-groups/") && path.EndsWith("/collections/order"))
            {
                var group = path.Split('/')[5];
                if (mutating)
                {
                    foreach (var id in body["ordered_ids"]!.AsArray().Select(v => v!.ToString())) Collections[id]["group_id"] = group == "ungrouped" ? null : group;
                    _revision++;
                }
                return Reply(new() { ["ordered_ids"] = new JsonArray(Collections.Where(p => (p.Value["group_id"]?.ToString() ?? "ungrouped") == group).Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray()), ["has_more"] = false });
            }
            if (path.StartsWith("/api/v2/admin/collection-groups/"))
            {
                var id = path.Split('/')[5]; var shelf = Groups.FirstOrDefault(g => g["id"]!.ToString() == id);
                if (shelf == null) return Reply(new() { ["status"] = 404 }, HttpStatusCode.NotFound);
                if (request.Method == HttpMethod.Patch) { foreach (var pair in body) shelf[pair.Key] = pair.Value?.DeepClone(); _revision++; }
                if (request.Method == HttpMethod.Delete)
                {
                    Groups.Remove(shelf); GroupOrder.Remove(id); foreach (var c in Collections.Values.Where(c => c["group_id"]?.ToString() == id)) c["group_id"] = null; _revision++;
                    return new(HttpStatusCode.NoContent);
                }
                return Reply(shelf);
            }
            if (path == "/api/v2/admin/collections/preview") return Reply(JsonNode.Parse("""{"items":[{"content_id":"m1","title":"Preview fixture","type":"movie"}],"page":{"has_more":false},"total":1}""")!.AsObject());
            if (path == "/api/v2/catalog") return Reply(JsonNode.Parse("""{"items":[{"content_id":"m1","title":"Fixture title","type":"movie"}],"page":{"has_more":false}}""")!.AsObject());
            if (path.EndsWith("/sections") && path.StartsWith("/api/v2/admin/collections/")) return Reply(JsonNode.Parse("""{"items":[{"id":"s1","title":"Shared fixture row","scope":"home","enabled":true}]}""")!.AsObject());
            if (path == "/api/v2/admin/sections/s1")
            {
                if (mutating && request.Method == HttpMethod.Patch) { Row["config"] = body["config"]!.DeepClone(); _revision++; }
                return Reply(Row);
            }
            if (path.StartsWith("/api/v2/admin/collections/"))
            {
                var parts = path.Split('/'); var id = parts[5];
                if (parts.Length > 6)
                {
                    if (path.EndsWith("/items/order")) return Reply(JsonNode.Parse("""{"ordered_ids":["m1"],"has_more":false}""")!.AsObject());
                    if (path.EndsWith("/items")) return Reply(JsonNode.Parse("""{"items":[{"collection_id":"c1","media_item_id":"m1","title":"Fixture title","position":0}],"page":{"has_more":false}}""")!.AsObject());
                    if (path.EndsWith("/sync")) return Reply(new() { ["status"] = "success" });
                    return new(HttpStatusCode.NoContent);
                }
                if (request.Method == HttpMethod.Delete) { Collections.Remove(id); _revision++; return new(HttpStatusCode.NoContent); }
                if (Collections.TryGetValue(id, out var item))
                {
                    if (request.Method == HttpMethod.Patch) { foreach (var pair in body) item[pair.Key] = pair.Value?.DeepClone(); _revision++; }
                    return Reply(item);
                }
                return Reply(new() { ["status"] = 404 }, HttpStatusCode.NotFound);
            }
            return Reply(path switch
            {
                "/api/v2/admin/system/build" => JsonNode.Parse("""{"display":"fixture","available":true}""")!.AsObject(),
                "/api/v2/admin/server/status" => new() { ["restart_required"] = false },
                _ => new() { ["items"] = new JsonArray(), ["page"] = new JsonObject { ["has_more"] = false } }
            });
        }
        public HttpResponseMessage Reply(JsonObject body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("ETag", $"\"r{_revision}\""); return response;
        }
        public static HttpResponseMessage Plain(JsonObject body, HttpStatusCode status) => new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }
    internal static async Task RunAsync(Window window)
    {
        var server = new Server(); using var http = new HttpClient(server); var client = new SiloApiClient(http); client.SetBaseUrl("https://collections.fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Role = "admin", Username = "synthetic" }); auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth); var monitor = new AdminMonitorApi(client); using var store = new ServerActivityStore(monitor, authority, new Realtime());
        var navigation = new NavigationService();
        await using var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(monitor).AddSingleton(store)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory)).AddAdminCollections().BuildServiceProvider();
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousServices = field.GetValue(null); var previousContent = window.Content; Frame? outer = null; AdminCollectionsPage? page = null;
        field.SetValue(null, services);
        try
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            Check(AdminShellPage.CanOpen(new(AdminRoute.Collections)), "production registration makes Collections openable");
            Check(AdminRouteCatalog.IsValid(AdminRouteRequest.CollectionNew()) && AdminRouteCatalog.IsValid(AdminRouteRequest.CollectionEditor("c1")), "collections new and edit descendants are valid routes");
            AdminShellPage shell = null!; Frame inner = null!;
            AdminCollectionsPage Current() => (AdminCollectionsPage)inner.Content;

            // ── Loading, then populated ─────────────────────────────────────────
            await Step("loading and list", async () =>
            {
                server.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Check(AdminShellPage.Open(new(AdminRoute.Collections)), "Collections opens the real admin shell");
                await Until(() => outer.Content is AdminShellPage, "shell");
                shell = (AdminShellPage)outer.Content; inner = (Frame)Field(shell, "_frame")!;
                await Until(() => inner.Content is AdminCollectionsPage, "page");
                page = Current();
                await Until(() => Texts(page).Contains("Loading collections…"), "loading state");
                Check(true, "loading state is shown while the server holds its first answer");
                Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Collections swaps without entrance transitions");
                server.Hold!.TrySetResult(); server.Hold = null;
                await Until(() => Find<Button>(page, "CollectionEdit_c1") != null, "populated list");
                Check(!server.Requests.Any(r => r.Contains("/catalog?")), "opening Collections does not walk a media catalog");
                Check(Texts(page).Contains("Collections") && Texts(page).Any(t => t.StartsWith("Server collections everyone can browse")), "official title and subtitle render");
                Check(Texts(page).Any(t => t.Contains("Fixture smart")) || Find<Button>(page, "CollectionEdit_c2") != null, "every fixture collection renders");
                Check(Texts(page).Contains("Sync failed"), "failed sync state shows on the synced row");
                await CaptureAsync(shell, "admin-collections-list-1440.png");
            });
            if (page == null) throw new InvalidOperationException("Collections page never opened.");

            // ── Error and empty states ──────────────────────────────────────────
            await Step("list error then retry", async () =>
            {
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "GET" && target == "/api/v2/admin/collections" ? Server.Plain(new() { ["status"] = 404 }, HttpStatusCode.NotFound) : null);
                await Click(Find<Button>(page, "CollectionsRefresh"));
                await Until(() => Find<Button>(page, "CollectionsRetry") != null, "error state with Retry");
                Check(Texts(Current()).Any(t => t.Contains("Couldn’t load collections")), "failed list read shows the official load-failed title");
                Check(Find<ComboBox>(page, "CollectionsLibraryRecovery") != null, "failed list keeps a library recovery picker");
                server.Intercept = null;
                await Click(Find<Button>(page, "CollectionsRetry"));
                await Until(() => Find<Button>(page, "CollectionEdit_c1") != null, "retry repopulates");
            });
            await Step("empty list", async () =>
            {
                server.EmptyList = true;
                await Click(Find<Button>(page, "CollectionsRefresh"));
                await Until(() => Find<Button>(page, "CollectionsEmptyStarterPack") != null, "empty state");
                Check(Texts(Current()).Contains("No collections yet"), "empty list says No collections yet");
                server.EmptyList = false;
                await Click(Find<Button>(page, "CollectionsRefresh"));
                await Until(() => Find<Button>(page, "CollectionEdit_c1") != null, "list returns");
            });

            // ── Filters and selection ───────────────────────────────────────────
            await Step("filters and bulk selection", async () =>
            {
                var kind = Find<ComboBox>(page, "CollectionsKind")!; kind.SelectedItem = kind.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "smart");
                await Until(() => Find<Button>(page, "CollectionEdit_c1") == null && Find<Button>(page, "CollectionEdit_c2") != null, "kind filter");
                await Click(Find<Button>(page, "CollectionsClear")); await Until(() => Find<Button>(page, "CollectionEdit_c1") != null, "clear filters");
                await Click(Find<Button>(page, "CollectionsSelect")); await Until(() => Find<CheckBox>(page, "CollectionSelect_c2") != null, "select mode");
                Toggle(Find<CheckBox>(page, "CollectionSelect_c2")!);
                await Until(() => Texts(page).Contains("1 selected"), "selection count");
                await Click(Find<Button>(page, "CollectionsBulkShow")); await Until(() => Dialog(page) != null, "bulk confirm");
                var confirm = Dialog(page)!; Check(Texts(confirm).Any(t => t.Contains("1 selected")), "bulk confirmation names the selection");
                await Click(Tree(confirm).OfType<Button>().First(b => b.Content?.ToString() == "Show on tabs"));
                await Until(() => Dialog(page) == null && server.Collections["c2"]["visibility"]!.ToString() == "visible", "bulk show applied");
                Check(server.Sent("PATCH", "/api/v2/admin/collections/c2").Any(l => l.IfMatch?.StartsWith("\"r") == true), "bulk visibility change is guarded by the strong validator");
                await Click(Find<Button>(page, "CollectionsSelect"));
            });

            // ── Mutations: success, failure and unknown outcome ────────────────
            await Step("visibility failure and unknown-outcome gate", async () =>
            {
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "PATCH" && target.StartsWith("/api/v2/admin/collections/c3") ? Server.Plain(new() { ["status"] = 500 }, HttpStatusCode.InternalServerError) : null);
                var toggle = Find<ToggleSwitch>(page, "CollectionVisible_c3")!; toggle.IsOn = false;
                await Until(() => Texts(Current()).Any(t => t.Contains("The server could not complete the collection request.")), "failed write message");
                Check(server.Collections["c3"]["visibility"]!.ToString() == "visible", "a failed visibility write changes nothing");
                Check(Find<ToggleSwitch>(page, "CollectionVisible_c3")!.IsOn, "toggle snaps back after a failed write");
                var sent = server.Sent("PATCH", "/api/v2/admin/collections/").Count();
                Find<ToggleSwitch>(page, "CollectionVisible_c3")!.IsOn = false; await Task.Delay(300);
                Check(server.Sent("PATCH", "/api/v2/admin/collections/").Count() == sent, "after an uncertain write no further write is sent until the list is re-read");
                server.Intercept = null;
                await Click(Find<Button>(page, "CollectionsRefresh")); await Until(() => Find<Button>(page, "CollectionEdit_c1") != null, "reload clears the barrier");
                Find<ToggleSwitch>(page, "CollectionVisible_c3")!.IsOn = false;
                await Until(() => server.Collections["c3"]["visibility"]!.ToString() == "hidden", "hide applies after reconciliation");
                Find<ToggleSwitch>(page, "CollectionVisible_c3")!.IsOn = true;
                await Until(() => server.Collections["c3"]["visibility"]!.ToString() == "visible", "show applies");
            });
            await Step("sync now", async () =>
            {
                await Click(Find<Button>(page, "CollectionSync_c3"));
                await Until(() => server.Requests.Contains("POST /api/v2/admin/collections/c3/sync"), "sync posted once");
                Check(server.Requests.Count(r => r == "POST /api/v2/admin/collections/c3/sync") == 1, "sync is sent once");
            });

            // ── Dialogs: new picker, templates, starter packs ───────────────────
            await Step("new collection picker and templates", async () =>
            {
                await Click(Find<Button>(page, "CollectionsNew")); await Until(() => Dialog(page) != null, "new picker");
                var picker = Dialog(page)!;
                Check(Find<Button>(picker, "CollectionsNew_manual") != null && Find<Button>(picker, "CollectionsNew_smart") != null && Find<Button>(picker, "CollectionsNew_synced") != null, "picker offers Manual, Smart and Synced list");
                await Click(Find<Button>(picker, "CollectionsTemplates"));
                await Until(() => Dialog(page) != null && Find<Button>(Dialog(page)!, "CollectionTemplate_t1") != null, "template catalog dialog", 12000);
                await CaptureAsync(Dialog(page)!, "admin-collections-templates-dialog.png");
                await Click(Find<Button>(Dialog(page)!, "CollectionTemplate_t1"));
                await Until(() => Dialog(page) == null && Find<TextBox>(page, "CollectionName") != null, "template seeds a real editor");
                Check(Find<TextBox>(page, "CollectionName")!.Text == "Fixture template", "template title seeds the editor draft");
                Check(shell.CurrentRequest.Segment(0) == "new", "template editor uses the /new route");
                // Leave the seeded draft: discard and go back.
                await Click(Find<Button>(page, "CollectionEditorDiscard"));
                Check(!page.Controller.Dirty, "Discard clears the template draft");
                await Click(Find<Button>(page, "CollectionEditorBack"));
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<Button>(p, "CollectionEdit_c1") != null, "back to list");
                page = Current();
            });
            await Step("starter pack dialog, queued job and bounded observation", async () =>
            {
                await Click(Find<Button>(page, "CollectionsStarterPacks")); await Until(() => Dialog(page) != null, "starter pack dialog");
                var dialog = Dialog(page)!;
                Check(!dialog.IsPrimaryButtonEnabled, "Add starter pack waits for a reviewed preview");
                Toggle(Find<CheckBox>(dialog, "CollectionPackLibrary_1")!);
                await Click(Find<Button>(dialog, "CollectionPackPreview")); await Until(() => dialog.IsPrimaryButtonEnabled, "preview enabled the add button");
                Check(Texts(dialog).Any(t => t.Contains("created: 1")), "dry-run review lists what would be created");
                server.JobTerminal = false; server.JobPolls = 0;
                await Click(Tree(dialog).OfType<Button>().First(b => b.Content?.ToString() == "Add starter pack"));
                await Until(() => Dialog(page) == null && page.Controller.StarterJobId == "job1", "job accepted and remembered");
                Check(Texts(page).Any(t => t.Contains("queued work, not completion")), "an accepted job is not presented as completed work");
                await Until(() => server.JobPolls >= 1, "first bounded poll", 6000);
                Check(page.Controller.StarterJobId == "job1", "a running job stays remembered while it is observed");
                server.JobTerminal = true;
                await Until(() => page.Controller.StarterJobId == null, "terminal job observed", 8000);
                await Until(() => Texts(Current()).Any(t => t.StartsWith("created: 1")), "job result summary");
                Check(Find<Button>(page, "CollectionEdit_c1") != null, "list is reloaded after the job finishes");
            });

            // ── Arrange: shelves, move, pin, viewer preview ─────────────────────
            await Step("arrange board and shelf dialogs", async () =>
            {
                server.Collections["c3"]["visibility"] = "hidden";
                var view = Find<ComboBox>(page, "CollectionsView")!; view.SelectedItem = view.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "arrange");
                await Until(() => Find<Button>(page, "CollectionMove_c1") != null, "arrange board");
                Check(Find<Button>(page, "CollectionHandle_c1")?.CanDrag == true, "Arrange wires a real native drag handle");
                await Click(Find<Button>(page, "CollectionsViewerPreview"));
                Check(Find<Button>(page, "CollectionBoardEdit_c3") == null && Find<Button>(page, "CollectionBoardEdit_c2") != null, "viewer preview omits hidden collections");
                await Click(Find<Button>(page, "CollectionsViewerPreview"));
                server.Collections["c3"]["visibility"] = "visible";
                await Click(Find<Button>(page, "CollectionMove_c1")); await Until(() => Dialog(page) != null, "move dialog");
                var moving = Dialog(page)!; var destination = Find<ComboBox>(moving, "CollectionMoveShelf")!;
                destination.SelectedItem = destination.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "g1"); moving.Hide();
                await Until(() => Dialog(page) == null, "move canceled");
                Check(server.Collections["c1"]["group_id"] == null, "canceled Move sends no change");
                await Click(Find<Button>(page, "CollectionMove_c1")); await Until(() => Dialog(page) != null, "move dialog again");
                moving = Dialog(page)!; destination = Find<ComboBox>(moving, "CollectionMoveShelf")!;
                destination.SelectedItem = destination.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "g1");
                await Click(Tree(moving).OfType<Button>().First(b => b.Content?.ToString() == "Move"));
                await Until(() => Dialog(page) == null && server.Collections["c1"]["group_id"]?.ToString() == "g1", "move applied with the complete destination order");
                Check(server.Sent("PUT", "/api/v2/admin/collection-groups/g1/collections/order").Any(l => l.IfMatch?.StartsWith("\"r") == true && l.Body.Contains("c2")), "Move sends the complete guarded destination order");
                await Click(Find<Button>(page, "CollectionPin_c1")); await Until(() => server.Collections["c1"]["featured"]!.GetValue<bool>(), "pin applied");
                await Click(Find<Button>(page, "CollectionsAddShelf")); await Until(() => Dialog(page) != null, "add shelf dialog");
                var shelf = Dialog(page)!; Find<TextBox>(shelf, "CollectionShelfName")!.Text = "Second shelf";
                await Until(() => shelf.IsPrimaryButtonEnabled, "shelf name enables Add");
                await Click(Tree(shelf).OfType<Button>().First(b => b.Content?.ToString() == "Add shelf"));
                await Until(() => Dialog(page) == null && server.Groups.Count == 2, "shelf created");
                await CaptureAsync(Current(), "admin-collections-arrange-1440.png");
                await Click(Find<Button>(page, "CollectionShelfRename_g2")); await Until(() => Dialog(page) != null, "rename shelf dialog");
                var rename = Dialog(page)!; Find<TextBox>(rename, "CollectionShelfName")!.Text = "Renamed shelf";
                await Until(() => rename.IsPrimaryButtonEnabled, "rename enabled");
                await Click(Tree(rename).OfType<Button>().First(b => b.Content?.ToString() == "Save"));
                await Until(() => Dialog(page) == null && server.Groups[1]["name"]!.ToString() == "Renamed shelf", "shelf renamed with its validator");
                await Click(Find<Button>(page, "CollectionShelfDelete_g2")); await Until(() => Dialog(page) != null, "delete shelf dialog");
                await Click(Tree(Dialog(page)!).OfType<Button>().First(b => b.Content?.ToString() == "Delete shelf"));
                await Until(() => Dialog(page) == null && server.Groups.Count == 1, "shelf deleted");
                // A failed move is reported and leaves the board untouched.
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "PUT" && target.Contains("/collections/order") ? Server.Plain(new() { ["status"] = 500 }, HttpStatusCode.InternalServerError) : null);
                await Click(Find<Button>(page, "CollectionMove_c3")); await Until(() => Dialog(page) != null, "failing move dialog");
                var failing = Dialog(page)!; var target = Find<ComboBox>(failing, "CollectionMoveShelf")!; target.SelectedItem = target.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "g1");
                await Click(Tree(failing).OfType<Button>().First(b => b.Content?.ToString() == "Move"));
                await Until(() => Dialog(page) == null && Texts(Current()).Any(t => t.Contains("could not complete") || t.Contains("outcome is unknown")), "failed move message");
                Check(server.Collections["c3"]["group_id"] == null, "failed move changes nothing");
                server.Intercept = null;
                await Click(Find<Button>(page, "CollectionsRefresh")); await Until(() => Find<Button>(page, "CollectionMove_c3") != null, "board reload");
                var back = Find<ComboBox>(page, "CollectionsView")!; back.SelectedItem = back.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "list");
                await Until(() => Find<Button>(page, "CollectionEdit_c1") != null, "list view again");
            });

            // ── Delete dialog with a shared row, then rows dialogs ──────────────
            await Step("delete with shared row, cancel and confirm", async () =>
            {
                await Click(Find<Button>(page, "CollectionDelete_c1")); await Until(() => Dialog(page) != null, "delete dialog");
                var deletion = Dialog(page)!; Check(!deletion.IsPrimaryButtonEnabled, "used collection requires explicit row-removal consent");
                Check(Texts(deletion).Any(t => t.Contains("Shared fixture row")), "delete shows actual rows before confirmation");
                await CaptureAsync(deletion, "admin-collections-delete-dialog.png"); deletion.Hide(); await Until(() => Dialog(page) == null, "delete canceled");
                Check(server.Collections.ContainsKey("c1"), "cancel delete preserves the collection");
                await Click(Find<Button>(page, "CollectionDelete_c1")); await Until(() => Dialog(page) != null, "delete dialog again");
                deletion = Dialog(page)!; Toggle(Find<CheckBox>(deletion, "CollectionDeleteRows")!);
                await Until(() => deletion.IsPrimaryButtonEnabled, "consent enables delete");
                await Click(Tree(deletion).OfType<Button>().First(b => b.Content?.ToString() == "Delete collection"));
                await Until(() => Dialog(page) == null && !server.Collections.ContainsKey("c1"), "delete applied");
                Check(server.Row["config"]!["collections"]!.AsArray().Count == 1 && server.Row["config"]!["collections"]![0]!["library_collection_id"]!.ToString() == "other", "confirmed delete preserves the other collection in a shared row");
                Check(server.Row["config"]!["keep"]!.ToString() == "yes", "row removal preserves unrelated config");
            });

            // ── Smart editor: rule libraries, stored sort, preview, conflict ────
            await Step("smart editor rules, stored sort, preview and save", async () =>
            {
                Check(AdminShellPage.Open(AdminRouteRequest.CollectionEditor("c2").WithQuery("libraryId", "1")), "official smart editor descendant opens");
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<Button>(p, "CollectionPreview") != null, "smart editor");
                page = Current();
                Check(Texts(page).Any(t => t.StartsWith("A saved default sort, title, ascending")), "stored default sort is explained");
                await Until(() => Texts(page).Contains("Preview fixture"), "live preview renders without a click", 6000);
                Check(Texts(page).Any(t => t.Contains("Preview shows unsaved changes")), "smart preview states it reflects unsaved changes");
                await CaptureAsync(shell, "admin-collections-smart-editor-1440.png");
                var previewBody = JsonNode.Parse(server.Sent("POST", "/api/v2/admin/collections/preview").Last().Body)!;
                Check(previewBody["limit"]!.GetValue<int>() == 24 && previewBody["query_definition"]!["library_ids"]![0]!.GetValue<int>() == 1, "preview sends 24 titles and the numeric rule libraries");
                // Rule libraries are independent of Where it shows.
                var rule = Find<CheckBox>(page, "CollectionRuleLibrary_1")!; Check(rule.IsChecked == true, "rule libraries show the saved scope");
                Toggle(rule);
                Check(page.Controller.Draft!["query_definition"]!["library_ids"]!.AsArray().Count == 0, "unticking the rule library leaves the rules open to every library");
                Check(page.Controller.Draft["library_ids"]![0]!.ToString() == "1", "rule library edits never change Where it shows");
                Toggle(rule);
                // Changing Order clears the overriding stored default.
                await Click(Find<Button>(page, "CollectionClearStoredSort"));
                Check(page.Controller.StoredDefaultSort == null && page.Controller.Draft["sort_config"]!["keep"]!.GetValue<bool>(), "clearing the stored sort keeps other sort settings");
                Check(!Texts(page).Any(t => t.StartsWith("A saved default sort")), "stored sort line disappears once cleared");
                // A failed preview never blocks saving.
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(target.EndsWith("/collections/preview") ? Server.Plain(new() { ["status"] = 500 }, HttpStatusCode.InternalServerError) : null);
                await Click(Find<Button>(page, "CollectionPreview")); await Until(() => Texts(page).Any(t => t.Contains("The preview didn’t load")), "failed preview message");
                Check(Find<Button>(page, "CollectionEditorSave")!.IsEnabled, "a failed preview leaves Save enabled");
                server.Intercept = null;
                Find<TextBox>(page, "CollectionName")!.Text = "Fixture smart renamed";
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => server.Collections["c2"]["title"]!.ToString() == "Fixture smart renamed", "smart save applied");
                var patch = JsonNode.Parse(server.Sent("PATCH", "/api/v2/admin/collections/c2").Last().Body)!;
                Check(patch["library_ids"]![0]!.ToString() == "1" && patch["query_definition"]!["library_ids"]![0]!.GetValue<int>() == 1, "save keeps Where it shows and the rule libraries separate");
                Check(patch["sort_config"]!["field"] == null && patch["sort_config"]!["keep"]!.GetValue<bool>(), "save sends the cleared default sort");
            });
            await Step("stale editor conflict review writes nothing", async () =>
            {
                await Until(() => Find<TextBox>(page, "CollectionName") != null, "editor");
                // Another administrator changes it behind the editor.
                server.Collections["c2"]["description"] = "Changed elsewhere"; server.Intercept = null;
                typeof(Server).GetField("_revision", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(server, 40);
                Find<TextBox>(page, "CollectionName")!.Text = "My local title";
                var writes = server.Sent("PATCH", "/api/v2/admin/collections/c2").Count();
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => Dialog(page) != null, "conflict review dialog");
                Check(server.Sent("PATCH", "/api/v2/admin/collections/c2").Count() == writes + 1, "the stale save was sent once and rejected");
                var review = Dialog(page)!; Check(Find<CheckBox>(review, "CollectionConflict_title") != null, "conflict review lists the changed field by name");
                Toggle(Find<CheckBox>(review, "CollectionConflict_title")!);
                await Click(Tree(review).OfType<Button>().First(b => b.Content?.ToString() == "Review selected changes"));
                await Until(() => Dialog(page) == null && Find<TextBox>(page, "CollectionName")!.Text == "My local title", "review keeps the chosen local field");
                Check(server.Sent("PATCH", "/api/v2/admin/collections/c2").Count() == writes + 1, "conflict review sends no write");
                Check(page.Controller.Dirty, "reviewed draft is dirty until the user saves again");
                await Click(Find<Button>(page, "CollectionEditorDiscard"));
            });

            // ── Create flows: unknown outcome, malformed identity, confirmed create, MDBList ─
            await Step("manual create: lost reply, malformed identity, failed canonical read", async () =>
            {
                Check(AdminShellPage.Open(AdminRouteRequest.CollectionNew().WithQuery("libraryId", "1").WithQuery("type", "manual").WithQuery("view", "list")), "manual create descendant opens");
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<TextBox>(p, "CollectionName") != null, "manual create editor");
                page = Current(); Find<TextBox>(page, "CollectionName")!.Text = "Created fixture";
                Check(page.RequiresLeaveConfirmation && shell.RequiresLeaveConfirmation, "editor draft reaches shell departure guard");
                // Lost reply: unknown outcome, no replay, Save disabled until the list is checked.
                server.Intercept = (method, target) => method == "POST" && target == "/api/v2/admin/collections" ? throw new HttpRequestException("Synthetic lost reply") : Task.FromResult<HttpResponseMessage?>(null);
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => Find<Button>(page, "CollectionEditorCheckList") != null, "unknown-outcome recovery");
                Check(!Find<Button>(page, "CollectionEditorSave")!.IsEnabled, "Save is disabled while the create outcome is unknown");
                Check(server.Requests.Count(r => r == "POST /api/v2/admin/collections") == 1, "the create was sent once and never replayed");
                server.Intercept = null;
                await Click(Find<Button>(page, "CollectionEditorCheckList")); await Until(() => Find<Button>(page, "CollectionEditorSave")?.IsEnabled == true, "list re-read re-enables Save");
                Check(Texts(Current()).Any(t => t.StartsWith("No collection with that name exists")), "the list check reports what exists");
                // Malformed identity: still an unknown outcome.
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "POST" && target == "/api/v2/admin/collections" ? Server.Plain(new() { ["id"] = 123, ["title"] = "Created fixture" }, HttpStatusCode.Created) : null);
                Find<TextBox>(page, "CollectionName")!.Text = "Created fixture";
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => !Find<Button>(page, "CollectionEditorSave")!.IsEnabled, "malformed identity is an unknown outcome");
                Check(server.Requests.Count(r => r == "POST /api/v2/admin/collections") == 2, "malformed identity is not replayed");
                server.Intercept = null;
                await Click(Find<Button>(page, "CollectionEditorCheckList")); await Until(() => Find<Button>(page, "CollectionEditorSave")?.IsEnabled == true, "re-read re-enables Save again");
                // Confirmed create whose canonical read then fails: identity survives Reload and nothing is created again.
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "GET" && target.StartsWith("/api/v2/admin/collections/new") ? Server.Plain(new() { ["status"] = 404 }, HttpStatusCode.NotFound) : null);
                Find<TextBox>(page, "CollectionName")!.Text = "Created fixture";
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => page.Controller.CreatedId != null, "create confirmed");
                await Until(() => Texts(Current()).Any(t => t.Contains("it will not be created again")), "confirmed-create read-failure message");
                var created = page.Controller.CreatedId!; var posts = server.Requests.Count(r => r == "POST /api/v2/admin/collections");
                server.Intercept = null;
                await Click(Find<Button>(page, "CollectionEditorReload")); await Until(() => Dialog(page) != null, "reload asks before discarding the draft");
                await Click(Tree(Dialog(page)!).OfType<Button>().First(b => b.Content?.ToString() == "Discard"));
                await Until(() => page.Controller.Editor?.Value["id"]?.ToString() == created, "Reload loads the confirmed collection by identity");
                Check(shell.CurrentRequest.Segment(0) == created, "reloading a confirmed create replaces /new with its identity");
                Check(server.Requests.Count(r => r == "POST /api/v2/admin/collections") == posts, "Reload after a failed canonical read never creates the collection again");
                await CaptureAsync(shell, "admin-collections-manual-editor-1440.png");
                // Titles dialog on the created collection.
                await Click(Find<Button>(page, "CollectionAddTitles")); await Until(() => Dialog(page) != null, "title picker");
                var picker = Dialog(page)!; Find<TextBox>(picker, "CollectionTitleSearch")!.Text = "Fixture";
                await Click(Find<Button>(picker, "CollectionTitlesSearchButton")); await Until(() => Find<CheckBox>(picker, "CollectionTitlePick_m1") != null, "title result");
                Check(server.Requests.Any(r => r.StartsWith("GET /api/v2/catalog?") && r.Contains("limit=30")), "title search is bounded to 30 results");
                picker.Hide(); await Until(() => Dialog(page) == null, "picker closed");
                await Click(Find<Button>(page, "CollectionEditorBack"));
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<Button>(p, "CollectionsNew") != null, "back to list");
                page = Current();
            });
            await Step("synced list editor: MDBList search capability", async () =>
            {
                Check(AdminShellPage.Open(AdminRouteRequest.CollectionNew().WithQuery("libraryId", "1").WithQuery("type", "synced").WithQuery("source", "mdblist")), "synced create descendant opens");
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<TextBox>(p, "CollectionListUrl") != null, "synced editor");
                page = Current();
                var search = Find<TextBox>(page, "CollectionMdbListSearch"); Check(search != null, "capability-enabled editor offers MDBList search");
                search!.Text = "top";
                await Click(Find<Button>(page, "CollectionMdbListSearchButton"));
                await Until(() => Find<Button>(page, "CollectionMdbListPick_12") != null, "MDBList results");
                Check(server.Requests.Any(r => r == "GET /api/v2/collections/import/mdblist/search?q=top"), "search uses the official discovery route");
                await Click(Find<Button>(page, "CollectionMdbListPick_12"));
                await Until(() => Find<TextBox>(page, "CollectionListUrl")!.Text == "https://mdblist.com/lists/someone/top/json", "pick fills the list's own JSON feed");
                Check(Find<TextBox>(page, "CollectionName")!.Text == "Top watched fixture", "pick fills the name where none was typed");
                Check(Find<ComboBox>(page, "CollectionDefaultSort") != null, "synced list offers a default sort");
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => server.Requests.Contains("POST /api/v2/admin/collections/import/mdblist"), "import posted");
                var imported = JsonNode.Parse(server.Sent("POST", "/api/v2/admin/collections/import/mdblist").Last().Body)!;
                Check(imported["url"]!.ToString() == "https://mdblist.com/lists/someone/top/json", "import follows the picked list's JSON feed");
                await Until(() => shell.CurrentRequest.Segment(0)?.StartsWith("imp") == true, "create replaced /new in place");
                await CaptureAsync(shell, "admin-collections-synced-editor-1440.png");
                // Capability off: the box is replaced by the official explanation.
                server.MdbListSearch = false;
                Check(AdminShellPage.Open(AdminRouteRequest.CollectionNew().WithQuery("libraryId", "1").WithQuery("type", "synced").WithQuery("source", "mdblist")), "second synced create opens");
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<TextBox>(p, "CollectionListUrl") != null, "second synced editor");
                page = Current();
                Check(Find<TextBox>(page, "CollectionMdbListSearch") == null && Texts(page).Any(t => t.StartsWith("Searching MDBList needs an API key")), "without the capability the search box is replaced by its explanation");
                server.MdbListSearch = true;
                page.Controller.Discard(); await Click(Find<Button>(page, "CollectionEditorBack"));
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<Button>(p, "CollectionsNew") != null, "back to list");
                page = Current();
            });

            // ── Rows dialogs and responsive capture ─────────────────────────────
            await Step("rows that show it and add as a row", async () =>
            {
                Check(AdminShellPage.Open(AdminRouteRequest.CollectionEditor("c3").WithQuery("libraryId", "1")), "synced editor opens");
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<Button>(p, "CollectionRows") != null, "editor with rows button");
                page = Current();
                await Click(Find<Button>(page, "CollectionRows")); await Until(() => Dialog(page) != null, "rows dialog");
                Check(Texts(Dialog(page)!).Any(t => t.Contains("Shared fixture row")), "rows dialog lists the row that shows the collection");
                Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "rows dialog closed");
                await Click(Find<Button>(page, "CollectionAddAsRow")); await Until(() => Dialog(page) != null, "add as row dialog");
                Check(Find<Button>(Dialog(page)!, "CollectionAddRow_home") != null, "Add as a row offers Home and each library page");
                Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "add as row closed");
                await Click(Find<Button>(page, "CollectionEditorBack"));
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<Button>(p, "CollectionsNew") != null, "back to list");
                page = Current();
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 600)); await Task.Delay(150);
                await CaptureAsync(shell, "admin-collections-list-800.png");
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            });

            // ── Authority retirement removes private data ───────────────────────
            await Step("authority retirement with a running starter-pack job", async () =>
            {
                server.JobTerminal = false; server.JobPolls = 0;
                await Click(Find<Button>(page, "CollectionsStarterPacks")); await Until(() => Dialog(page) != null, "starter pack dialog again");
                var dialog = Dialog(page)!; Toggle(Find<CheckBox>(dialog, "CollectionPackLibrary_1")!);
                await Click(Find<Button>(dialog, "CollectionPackPreview")); await Until(() => dialog.IsPrimaryButtonEnabled, "preview ready");
                await Click(Tree(dialog).OfType<Button>().First(b => b.Content?.ToString() == "Add starter pack"));
                await Until(() => Dialog(page) == null && page.Controller.StarterJobId == "job1", "second job accepted");
                await Until(() => server.JobPolls >= 1, "second job polled", 6000);
                var polls = server.JobPolls;
                auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user", Username = "synthetic" });
                await Until(() => page.Controller.List == null && Find<Button>(page, "CollectionsAuthorityReload") != null, "authority retirement");
                Check(page.Controller.StarterJobId == null, "authority retirement drops the starter-pack job identity");
                await Task.Delay(3500);
                Check(server.JobPolls <= polls + 1, "a retired page never polls the old job again");
                Check(Find<Button>(page, "CollectionEdit_c2") == null, "authority retirement removes private data and commands");
            });
        }
        finally
        {
            // Close and await dialogs and the nested page before restoring process globals.
            if (page != null) { Dialog(page)?.Hide(); page.SuspendAdminPage(); }
            outer?.Navigate(typeof(Page));
            if (page != null) await Until(() => !page.IsLoaded && Dialog(page) == null, "page unload");
            window.Content = previousContent; field.SetValue(null, previousServices);
        }
        if (Failures.Count > 0) throw new InvalidOperationException("Collections fixture failures: " + string.Join("; ", Failures));
        Program.Log("PASS: Collections native fixture completed; synthetic data/current DPI only.");
    }
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static ContentDialog? Dialog(AdminCollectionsPage page) => Field(page, "_dialog") as ContentDialog;
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static T? Find<T>(DependencyObject root, string id) where T : DependencyObject => Tree(root).OfType<T>().FirstOrDefault(v => AutomationProperties.GetAutomationId(v) == id);
    private static List<string> Texts(DependencyObject root) => Tree(root).OfType<TextBlock>().Select(t => t.Text).ToList();
    private static void Toggle(CheckBox box) => (FrameworkElementAutomationPeer.CreatePeerForElement(box) as IToggleProvider ?? throw new InvalidOperationException("Checkbox has no toggle peer")).Toggle();
    private static async Task Click(Button? button)
    {
        Check(button is { IsLoaded: true, IsEnabled: true }, "native command is rendered and enabled: " + (button == null ? "missing" : AutomationProperties.GetAutomationId(button)));
        if (button == null) throw new InvalidOperationException("Missing command.");
        button.StartBringIntoView();
        (FrameworkElementAutomationPeer.CreatePeerForElement(button) as IInvokeProvider ?? throw new InvalidOperationException("Button has no invoke peer")).Invoke(); await Task.Delay(60);
    }
    private static async Task CaptureAsync(FrameworkElement element, string name)
    {
        element.UpdateLayout(); await Task.Delay(100); var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(element);
        Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, name + " renders");
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
}
