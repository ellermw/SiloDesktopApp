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
using Microsoft.UI.Xaml.Input;
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
    private static Func<AdminCollectionsPage?>? Diagnose;
    private static async Task Step(string name, Func<Task> body)
    {
        Program.Log("GROUP: " + name);
        try { await body(); }
        catch (Exception error)
        {
            Check(false, name + " threw " + error.GetType().Name + ": " + error.Message);
            try
            {
                if (Diagnose?.Invoke() is { } page)
                    Program.Log("DIAGNOSTIC: notice='" + Texts(page).FirstOrDefault() + "' invalidRules=" + Field(page, "_invalidRules") + " busy=" + page.Controller.Busy + " unknown=" + page.Controller.OutcomeUnknown + " error='" + page.Controller.Error + "' dialog=" + (Dialog(page) != null));
            }
            catch (Exception) { }
        }
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
        public bool EmptyList, MdbListSearch = true, JobTerminal, ServeActiveJob;
        /// <summary>Collections no row shows: their rows read is empty.</summary>
        public readonly HashSet<string> NoRows = [];
        /// <summary>Stands in for per-collection validators: while set, deleting one collection does not move the others' validator.</summary>
        public bool FrozenRevision;
        /// <summary>Per-collection validators, as the real server keeps them: a change to one collection moves only its own validator.</summary>
        public bool PerCollection;
        public readonly Dictionary<string, int> Revisions = [];
        public void Bump(string id) => Revisions[id] = Revisions.GetValueOrDefault(id, 1) + 1;
        public string LibraryName = "Fixture movies", RowTitle = "Shared fixture row";
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
            string? own = null;
            if (PerCollection && path.StartsWith("/api/v2/admin/collections/", StringComparison.Ordinal) && path.Split('/') is { Length: 6 } own6 && Collections.ContainsKey(own6[5])) own = $"\"p{own6[5]}-{Revisions.GetValueOrDefault(own6[5], 1)}\"";
            if (guarded && request.Headers.IfMatch.SingleOrDefault()?.ToString() != (own ?? $"\"r{_revision}\"")) return Reply(new() { ["type"] = "conflict", ["status"] = 412 }, HttpStatusCode.PreconditionFailed);
            if (path == "/api/v2/libraries")
            {
                var libraries = JsonNode.Parse("""{"items":[{"id":"1","name":"x","type":"movies","paths":["/synthetic"],"enabled":true}],"page":{"has_more":false}}""")!.AsObject();
                libraries["items"]![0]!["name"] = LibraryName; return Reply(libraries);
            }
            if (path == "/api/v2/admin/collections/capabilities")
            {
                if (Hold != null) { var hold = Hold; Hold = null; await hold.Task; }
                var capabilities = JsonNode.Parse("""{"state":"available","groups":true,"imports":true,"import_sources":["mdblist","tmdb","tmdb_list"],"artwork":true,"item_reorder":true,"section_references":true,"template_summaries":true,"mdblist_search":false}""")!.AsObject();
                capabilities["mdblist_search"] = MdbListSearch; return Reply(capabilities);
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
                var item = JsonNode.Parse("""{"collection_type":"mdblist","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":0,"group_id":null,"sort_order":9,"featured":false,"description":"","query_definition":{},"sort_config":{}}""")!.AsObject();
                item["id"] = id; item["title"] = body["title"]?.ToString() ?? ""; item["source_config"] = new JsonObject { ["mode"] = "mdblist_json", ["url"] = body["url"]?.ToString() ?? "" };
                Collections[id] = item; _revision++; return Reply(new() { ["collection"] = item.DeepClone() }, HttpStatusCode.Created);
            }
            if (path == "/api/v2/admin/collections/import/tmdb")
            {
                var id = "imp" + ++_created;
                var item = JsonNode.Parse("""{"collection_type":"tmdb","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":0,"group_id":null,"sort_order":9,"featured":false,"description":"","query_definition":{},"sort_config":{},"source_config":{"mode":"tmdb_preset","preset":"popular","media_type":"movie"}}""")!.AsObject();
                item["id"] = id; item["title"] = body["title"]?.ToString() ?? "";
                Collections[id] = item; _revision++; return Reply(new() { ["collection"] = item.DeepClone() }, HttpStatusCode.Created);
            }
            // Recent starter-pack jobs: answered only while a step wants a still-running job to be rediscovered.
            if (path == "/api/v2/admin/jobs" && ServeActiveJob)
                return Reply(new() { ["items"] = new JsonArray(new JsonObject { ["id"] = "job1", ["state"] = JobTerminal ? "succeeded" : "running", ["terminal"] = JobTerminal }) });
            if (path == "/api/v2/collections/import/mdblist/search")
                return Reply(JsonNode.Parse("""{"configured":true,"items":[{"id":"12","user_id":"3","user_name":"someone","name":"Top watched fixture","slug":"top","description":"Synthetic list","media_type":"movie","items":100,"likes":9,"url":"https://mdblist.com/lists/someone/top"}],"page":{"has_more":false}}""")!.AsObject());
            if (path == "/api/v2/admin/collections/templates")
                return Reply(JsonNode.Parse("""{"categories":[{"id":"movies","title":"Movies","templates":[{"id":"t1","title":"Fixture template","description":"Synthetic template","source":"tmdb","poster_path":"/images/collection-templates/fixture.jpg","tmdb":{"preset":"popular","media_type":"movie"},"default_limit":40,"default_sync_schedule":"0 3 * * *"}]}]}""")!.AsObject());
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
                var job = JsonNode.Parse("""{"id":"job1","template_result":{"created":[{"template_title":"Fixture template","library_name":"Fixture movies","reason":"created"}]}}""")!.AsObject();
                job["state"] = JobTerminal ? "succeeded" : "running"; job["terminal"] = JobTerminal; return Reply(job);
            }
            if (path == "/api/v2/admin/libraries/1/collection-groups")
            {
                if (request.Method == HttpMethod.Post)
                {
                    var shelf = JsonNode.Parse("""{"library_id":"1","kind":"regular","sort_order":0}""")!.AsObject();
                    shelf["id"] = "g" + (Groups.Count + 1); shelf["name"] = body["name"]?.ToString() ?? ""; shelf["default_sort_mode"] = body["default_sort_mode"]?.ToString() ?? "manual"; shelf["sort_order"] = Groups.Count;
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
            if (path.EndsWith("/sections") && path.StartsWith("/api/v2/admin/collections/"))
            {
                var rows = JsonNode.Parse("""{"items":[{"id":"s1","title":"x","scope":"home","enabled":true,"collection_count":2}]}""")!.AsObject();
                rows["items"]![0]!["title"] = RowTitle;
                return Reply(NoRows.Contains(path.Split('/')[5]) ? new JsonObject { ["items"] = new JsonArray() } : rows);
            }
            if (path == "/api/v2/admin/sections/s1")
            {
                if (mutating && request.Method == HttpMethod.Patch)
                {
                    Row["config"] = body["config"]!.DeepClone(); _revision++;
                    // A collection the row no longer shows reports no rows on its own GET from here on.
                    var shown = (Row["config"]?["collections"] as JsonArray)?.Select(r => r?["library_collection_id"]?.ToString()).ToHashSet() ?? [];
                    foreach (var c in Collections.Values.Where(c => c["row_count"] != null && !shown.Contains(c["id"]!.ToString()))) { c["row_count"] = 0; c["home_row_count"] = 0; }
                }
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
                if (request.Method == HttpMethod.Delete) { Collections.Remove(id); if (!FrozenRevision && !PerCollection) _revision++; return new(HttpStatusCode.NoContent); }
                if (Collections.TryGetValue(id, out var item))
                {
                    if (request.Method == HttpMethod.Patch) { foreach (var pair in body) item[pair.Key] = pair.Value?.DeepClone(); if (PerCollection) Bump(id); else _revision++; }
                    return Reply(item, tag: own is null ? null : $"\"p{id}-{Revisions.GetValueOrDefault(id, 1)}\"");
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
        public HttpResponseMessage Reply(JsonObject body, HttpStatusCode status = HttpStatusCode.OK, string? tag = null)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("ETag", tag ?? $"\"r{_revision}\""); return response;
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
            Diagnose = () => inner?.Content as AdminCollectionsPage;
            async Task CaptureWidthsAsync(FrameworkElement target, string stem)
            {
                foreach (var width in new[] { 1024, 800, 560, 1440 })
                {
                    window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900)); await Task.Delay(250);
                    if (width != 1440) await CaptureAsync(target, $"{stem}-{width}.png");
                }
            }

            // ── Loading, then populated ─────────────────────────────────────────
            await Step("loading and list", async () =>
            {
                var hold = server.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Check(AdminShellPage.Open(new(AdminRoute.Collections)), "Collections opens the real admin shell");
                await Until(() => outer.Content is AdminShellPage, "shell");
                shell = (AdminShellPage)outer.Content; inner = (Frame)Field(shell, "_frame")!;
                await Until(() => inner.Content is AdminCollectionsPage, "page");
                page = Current();
                await Until(() => Texts(page).Contains("Loading collections…"), "loading state");
                Check(true, "loading state is shown while the server holds its first answer");
                Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Collections swaps without entrance transitions");
                hold.TrySetResult();
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
                Check(Texts(Current()).Any(t => t.Contains("Couldn't load collections")), "failed list read shows the official load-failed title");
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
                await Until(() => Find<CheckBox>(dialog, "CollectionPackLibrary_1") is { IsLoaded: true }, "pack library loaded"); { var pack = Find<CheckBox>(dialog, "CollectionPackLibrary_1")!; if (pack.IsChecked != true) Toggle(pack); }
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
                var pin = Find<Button>(page, "CollectionPin_c1")!;
                Check(pin.Visibility == Visibility.Visible && pin.Opacity == 1 && pin.IsTabStop && pin.IsEnabled && pin.Focus(FocusState.Keyboard), "the pin action is always drawn, in the tab order and takes keyboard focus, so it needs no hover reveal on touch or keyboard");
                Check(Find<Button>(page, "CollectionBoardEdit_c1")?.Content is TextBlock { Text: "Fixture manual", TextTrimming: TextTrimming.CharacterEllipsis } tile && tile.ReadLocalValue(TextBlock.ForegroundProperty) == DependencyProperty.UnsetValue, "a collection card names its collection, trimmed at the end and in the button's text color");
                await CaptureWidthsAsync(shell, "admin-collections-arrange");
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
                var shelf = Dialog(page)!; await SetText(Find<TextBox>(shelf, "CollectionShelfName")!, "Second shelf");
                await Until(() => shelf.IsPrimaryButtonEnabled, "shelf name enables Add");
                await Click(Tree(shelf).OfType<Button>().First(b => b.Content?.ToString() == "Add shelf"));
                await Until(() => Dialog(page) == null && server.Groups.Count == 2, "shelf created");
                await CaptureAsync(Current(), "admin-collections-arrange-1440.png");
                await Click(Find<Button>(page, "CollectionShelfRename_g2")); await Until(() => Dialog(page) != null, "rename shelf dialog");
                var rename = Dialog(page)!; await SetText(Find<TextBox>(rename, "CollectionShelfName")!, "Renamed shelf");
                await Until(() => rename.IsPrimaryButtonEnabled, "rename enabled");
                await Click(Tree(rename).OfType<Button>().First(b => b.Content?.ToString() == "Save"));
                await Until(() => Dialog(page) == null && server.Groups[1]["name"]!.ToString() == "Renamed shelf", "shelf renamed with its validator");
                await Click(Find<Button>(page, "CollectionShelfDelete_g2")); await Until(() => Dialog(page) != null, "delete shelf dialog");
                var shelfDialog = Dialog(page)!;
                Check(Equals(shelfDialog.Title, "Delete the Renamed shelf shelf?") && Texts(shelfDialog).Any(t => t.StartsWith("It has no collections. Only Fixture movies changes;")), "delete shelf uses the official title and says what happens to its collections");
                await AssertDestructiveAsync(shelfDialog, "delete shelf");
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
                Check(deletion.PrimaryButtonText == "Delete it and change 1 row" && Equals(deletion.Title, "Delete Fixture manual?"), "a row that lists other collections stays: the button says it changes the row");
                Check(Texts(deletion).Contains("It's removed from Fixture movies for everyone. 1 row shows it:") && Texts(deletion).Contains(AdminCollectionCopy.RowRemovesOnly("Shared fixture row")) && Texts(deletion).Contains(AdminCollectionCopy.DeleteFooter), "delete with rows uses the official sentences");
                await AssertDestructiveAsync(deletion, "delete with rows");
                var deletePosts = server.Sent("DELETE", "/api/v2/admin/collections/c1").Count();
                await Click(Tree(deletion).OfType<Button>().First(b => b.Content?.ToString() == "Delete it and change 1 row"));
                await Until(() => Dialog(page) == null && !server.Collections.ContainsKey("c1"), "delete applied");
                Check(server.Row["config"]!["collections"]!.AsArray().Count == 1 && server.Row["config"]!["collections"]![0]!["library_collection_id"]!.ToString() == "other", "confirmed delete preserves the other collection in a shared row");
                Check(server.Row["config"]!["keep"]!.ToString() == "yes", "row removal preserves unrelated config");
                Check(server.Sent("DELETE", "/api/v2/admin/collections/c1").Count() == deletePosts + 1, "the confirmed collection is deleted once");
            });

            // ── Refresh against the official source 02a9d1d3e: plain names, destructive confirmations, bulk delete, hide ──
            await Step("cut-short names keep their start, are never sliced, and every other list transition is cleared", async () =>
            {
                var longName = "Long " + new string('x', 295);
                server.Collections["c8"] = JsonNode.Parse("""{"id":"c8","title":"x","collection_type":"manual","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":2,"group_id":null,"sort_order":8,"featured":false,"description":"","query_definition":{},"sort_config":{},"source_config":{}}""")!.AsObject();
                server.Collections["c8"]["title"] = longName; server.NoRows.Add("c8");
                await Click(Find<Button>(page, "CollectionsRefresh")); await Until(() => Find<Button>(page, "CollectionEdit_c8") != null, "long name row");
                var button = Find<Button>(page, "CollectionEdit_c8")!;
                var block = button.Content as TextBlock;
                Check(block != null && block.Text == longName, "the whole name is kept in the control; the string is never sliced");
                Check(block!.TextTrimming == TextTrimming.CharacterEllipsis && block.TextWrapping == TextWrapping.NoWrap && block.TextReadingOrder == TextReadingOrder.DetectFromContent, "the name is cut by trimming at its end, so its start stays");
                Check(block.ReadLocalValue(TextBlock.ForegroundProperty) == DependencyProperty.UnsetValue, "a name in a button takes the button's text color in every state, so it is never drawn blank");
                Check(button.MaxWidth <= 420 && AutomationProperties.GetName(button) == AdminCollectionNames.Plain(longName) && AutomationProperties.GetName(button).Length <= 81 && ToolTipService.GetToolTip(button) is string tip && tip.Length > 81, "a long name cannot stretch its row; it is announced as its plain, bounded name and the whole name is the tooltip");
                var inRow = Find<Button>(page, "CollectionEdit_c2")!.Content as TextBlock;
                Check(inRow?.Text == "Fixture smart" && inRow.TextTrimming == TextTrimming.CharacterEllipsis, "a short name is shown whole");
                Check(Tree(page).OfType<TextBlock>().Any(t => t.Text == "Sync failed" && t.Foreground is SolidColorBrush { Color: { } red } && red.R >= 0xDC && red.G < 0x80 && t.FontWeight.Weight >= 500), "a failed sync reads in the destructive text color at medium weight");
                // The page swaps lists in place: no list control, no child, item or content transition anywhere.
                Check(!Tree(page).Any(d => d is ListViewBase), "the list is a plain panel, not a ListView with its default entrance, add, delete and reorder transitions");
                Check(Tree(page).OfType<Panel>().All(p => p.ChildrenTransitions == null || p.ChildrenTransitions.Count == 0), "no panel plays child transitions");
                Check(Tree(page).OfType<ItemsControl>().All(c => c.ItemContainerTransitions == null || c.ItemContainerTransitions.Count == 0), "no items control plays item transitions");
                Check(Tree(page).OfType<ContentControl>().All(c => c.ContentTransitions == null || c.ContentTransitions.Count == 0), "no content control plays content transitions");
                server.Collections.Remove("c8");
            });
            await Step("plain names in delete confirmations and the destructive style", async () =>
            {
                string C(int code) => char.ConvertFromUtf32(code);
                // A name with a line break, a direction override and an invisible filler is one plain line in the title.
                server.Collections["c7"] = JsonNode.Parse("""{"id":"c7","title":"x","collection_type":"manual","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":0,"group_id":null,"sort_order":7,"featured":false,"description":"","query_definition":{},"sort_config":{},"source_config":{}}""")!.AsObject();
                server.Collections["c7"]["title"] = "Plain" + C(0x202E) + "\nname" + C(0x200B) + C(0x2028) + "here"; server.NoRows.Add("c7");
                await Click(Find<Button>(page, "CollectionsRefresh")); await Until(() => Find<Button>(page, "CollectionDelete_c7") != null, "plain name row");
                await Click(Find<Button>(page, "CollectionDelete_c7")); await Until(() => Dialog(page) != null, "plain delete dialog");
                var plain = Dialog(page)!;
                Check(Equals(plain.Title, "Delete Plain name here?"), "the delete title is one plain line: control characters, line separators, direction characters and invisible fillers are neutralized");
                Check(Texts(plain).Contains("It's removed from Fixture movies for everyone. This can't be undone."), "delete says which libraries it leaves, in the official sentence");
                Check(plain.PrimaryButtonText == "Delete" && plain.IsPrimaryButtonEnabled, "a collection no row shows deletes with a plain Delete");
                await AssertDestructiveAsync(plain, "single delete");
                var deletes = server.Sent("DELETE", "/api/v2/admin/collections/c7").Count();
                await Click(Tree(plain).OfType<Button>().First(b => b.Content?.ToString() == "Delete"));
                await Until(() => Dialog(page) == null && !server.Collections.ContainsKey("c7"), "plain delete applied");
                Check(server.Sent("DELETE", "/api/v2/admin/collections/c7").Count() == deletes + 1, "the confirmed collection is deleted once");

                // Select mode: only what the confirmation names is deleted. A collection a row shows is kept and named.
                server.Collections["c4"] = JsonNode.Parse("""{"id":"c4","title":"Bulk one","collection_type":"smart","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":1,"group_id":null,"sort_order":4,"featured":false,"description":"","query_definition":{"library_ids":[1],"match":"all","groups":[]},"sort_config":{},"source_config":{}}""")!.AsObject();
                server.Collections["c5"] = JsonNode.Parse("""{"id":"c5","title":"Bulk two","collection_type":"mdblist","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":1,"group_id":null,"sort_order":5,"featured":false,"description":"","query_definition":{},"sort_config":{},"source_config":{"mode":"mdblist_json","url":"https://mdblist.com/lists/a/b/json"}}""")!.AsObject();
                server.Collections["c6"] = JsonNode.Parse("""{"id":"c6","title":"Kept by rows","collection_type":"manual","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":1,"group_id":null,"sort_order":6,"featured":false,"description":"","query_definition":{},"sort_config":{},"source_config":{},"row_count":1,"home_row_count":1}""")!.AsObject();
                server.NoRows.Add("c4"); server.NoRows.Add("c5"); server.FrozenRevision = true;
                await Click(Find<Button>(page, "CollectionsRefresh")); await Until(() => Find<Button>(page, "CollectionEdit_c6") != null, "bulk rows");
                // c6 is shown by a row (row_count 1): hiding it asks first, in the official sentences, and not in the destructive style.
                Find<ToggleSwitch>(page, "CollectionVisible_c6")!.IsOn = false;
                await Until(() => Dialog(page) != null, "hide confirmation");
                var hide = Dialog(page)!;
                Check(Equals(hide.Title, "Hide Kept by rows from Collections tabs?"), "hide confirmation title names the collection");
                Check(Texts(hide).Any(t => t == "It leaves Fixture movies › Collections. 1 row still shows it, but its See all won't open while it's hidden."), "hide confirmation uses the official sentence for the library and the row");
                Check(hide.PrimaryButtonText == "Hide" && hide.PrimaryButtonStyle != AdminCollectionsDestructiveStyle.PrimaryStyle, "Hide is an ordinary confirm, not the destructive red");
                var patches = server.Sent("PATCH", "/api/v2/admin/collections/c6").Count();
                hide.Hide(); await Until(() => Dialog(page) == null, "hide canceled");
                await Until(() => Find<ToggleSwitch>(page, "CollectionVisible_c6")!.IsOn, "toggle returns to the saved value");
                Check(server.Sent("PATCH", "/api/v2/admin/collections/c6").Count() == patches && server.Collections["c6"]["visibility"]!.ToString() == "visible", "a canceled hide writes nothing");
                await Click(Find<Button>(page, "CollectionsSelect")); await Until(() => Find<CheckBox>(page, "CollectionSelect_c4") != null, "select mode");
                foreach (var id in new[] { "c4", "c5", "c6" }) Toggle(Find<CheckBox>(page, "CollectionSelect_" + id)!);
                await Until(() => Texts(page).Contains("3 selected"), "three selected");
                await Click(Find<Button>(page, "CollectionsBulkDelete")); await Until(() => Dialog(page) != null, "bulk delete dialog");
                var bulk = Dialog(page)!;
                Check(Equals(bulk.Title, "Delete 2 collections?"), "the bulk title counts only what will be deleted, with no type for a mix");
                Check(Texts(bulk).Contains("They're removed for everyone. Their titles stay in your libraries."), "bulk delete uses the official description");
                Check(Texts(bulk).Any(t => t == "• 1 is kept because rows use it: Kept by rows."), "the collection a row shows is named as kept");
                Check(Texts(bulk).Contains("This can't be undone.") && bulk.PrimaryButtonText == "Delete 2", "bulk delete says it cannot be undone and counts what it deletes");
                await AssertDestructiveAsync(bulk, "bulk delete");
                bulk.Hide(); await Until(() => Dialog(page) == null, "bulk delete canceled");
                Check(!server.Sent("DELETE", "/api/v2/admin/collections/c").Any(l => l.Path.Contains("/c4") || l.Path.Contains("/c5") || l.Path.Contains("/c6")), "a canceled bulk delete sends nothing");
                await Click(Find<Button>(page, "CollectionsBulkDelete")); await Until(() => Dialog(page) != null, "bulk delete dialog again");
                await Click(Tree(Dialog(page)!).OfType<Button>().First(b => b.Content?.ToString() == "Delete 2"));
                await Until(() => Dialog(page) == null && !server.Collections.ContainsKey("c4") && !server.Collections.ContainsKey("c5"), "bulk delete applied");
                Check(server.Collections.ContainsKey("c6"), "the collection a row shows is kept");
                Check(server.Sent("DELETE", "/api/v2/admin/collections/c4").Count() == 1 && server.Sent("DELETE", "/api/v2/admin/collections/c5").Count() == 1 && !server.Sent("DELETE", "/api/v2/admin/collections/c6").Any(), "each confirmed collection is deleted once and the kept one is never sent");
                await Until(() => Texts(Current()).Any(t => t.StartsWith("Deleted 2 collections.")), "bulk delete result");
                Check(!Texts(Current()).Any(t => t.Contains("Kept 1 collection in use")), "the collection left out before the confirmation is named there, as official, and is not counted again in the result");
                // Only collections that rows show: nothing is deleted and no dialog opens.
                await Until(() => Texts(page).Contains("1 selected") && Find<CheckBox>(page, "CollectionSelect_c6") is { IsChecked: true }, "the deleted collections left the selection; only the kept one stays selected");
                var before = server.Log.Count(l => l.Method == "DELETE");
                await Click(Find<Button>(page, "CollectionsBulkDelete"));
                await Until(() => Texts(Current()).Contains("Rows still use them. Remove the rows first."), "nothing deletable");
                Check(Dialog(page) == null && server.Log.Count(l => l.Method == "DELETE") == before, "a selection every row uses deletes nothing and opens no confirmation");
                await Click(Find<Button>(page, "CollectionsSelect"));
                server.FrozenRevision = false; server.Collections.Remove("c6");
                await Click(Find<Button>(page, "CollectionsRefresh"));
            });
            // ── Review fixes (November 21): a confirmed bulk action that never runs, what show and hide count, official bulk delete results ──
            void Add(string id, string title, string visibility = "visible", int rows = 0)
            {
                var item = JsonNode.Parse("""{"id":"x","title":"x","collection_type":"manual","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":1,"group_id":null,"sort_order":20,"featured":false,"description":"","query_definition":{},"sort_config":{},"source_config":{}}""")!.AsObject();
                item["id"] = id; item["title"] = title; item["visibility"] = visibility;
                if (rows > 0) { item["row_count"] = rows; item["home_row_count"] = rows; } else server.NoRows.Add(id);
                server.Collections[id] = item;
            }
            async Task RefreshAsync(string ready) { await Click(Find<Button>(page, "CollectionsRefresh")); await Until(() => Find<Button>(page, ready) != null, "the list shows " + ready); }
            async Task EnterSelectAsync() { await Click(Find<Button>(page, "CollectionsSelect")); await Until(() => Find<Button>(page, "CollectionsBulkDelete") != null, "select mode"); }
            async Task PickAsync(params string[] ids) { foreach (var id in ids) { Toggle(Find<CheckBox>(page, "CollectionSelect_" + id)!); await Task.Delay(60); } }
            async Task PressAsync(string label) => await Click(Tree(Dialog(page)!).OfType<Button>().First(b => b.Content?.ToString() == label));
            void ClearNotice() => typeof(AdminCollectionsPage).GetMethod("Notice", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, [""]);
            int CountOf(string method) => server.Log.Count(l => l.Method == method);
            int CountOfPath(string method, string path) => server.Log.Count(l => l.Method == method && l.Path == path);

            await Step("an unknown outcome stops bulk delete, hide and show, and the notice says why nothing happened", async () =>
            {
                foreach (var id in new[] { "u1", "u2", "u3" }) Add(id, "Unknown " + id);
                Add("u4", "Unknown u4", "hidden");
                await RefreshAsync("CollectionEdit_u4");
                // A single delete whose reply is lost: an unknown outcome. The admin cancels.
                server.Intercept = (method, target) => method == "DELETE" && target.EndsWith("/u1", StringComparison.Ordinal) ? throw new HttpRequestException("Synthetic lost reply") : Task.FromResult<HttpResponseMessage?>(null);
                await Click(Find<Button>(page, "CollectionDelete_u1")); await Until(() => Dialog(page) != null, "single delete dialog");
                await PressAsync("Delete");
                await Until(() => page.Controller.OutcomeUnknown, "the lost reply is an unknown outcome");
                server.Intercept = null;
                Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "the admin cancels");
                var unknown = "The outcome is unknown. Reload and review before making another change.";
                await EnterSelectAsync(); await PickAsync("u2", "u3"); await Until(() => Texts(page).Contains("2 selected"), "two selected");
                var deletes = CountOf("DELETE"); var patches = CountOf("PATCH");
                // Delete 2: confirmed, never sent; the reason is shown, not a result.
                ClearNotice();
                await Click(Find<Button>(page, "CollectionsBulkDelete")); await Until(() => Dialog(page) != null, "bulk delete confirm");
                Check(Equals(Dialog(page)!.Title, "Delete 2 manual collections?"), "the confirmation names the two collections it would delete");
                await PressAsync("Delete 2");
                await Until(() => Dialog(page) == null && Texts(Current()).Contains(unknown), "delete: the notice says why nothing happened");
                Check(CountOf("DELETE") == deletes && server.Collections.ContainsKey("u2") && server.Collections.ContainsKey("u3"), "delete: no request was sent");
                Check(!Texts(Current()).Any(t => t.StartsWith("Deleted ") || t.StartsWith("Kept ") || t.StartsWith("Failed")), "delete: no result sentence is written over the reason");
                // Hide 2.
                ClearNotice();
                await Click(Find<Button>(page, "CollectionsBulkHide")); await Until(() => Dialog(page) != null, "hide confirm");
                await PressAsync("Hide from tabs");
                await Until(() => Dialog(page) == null && Texts(Current()).Contains(unknown), "hide: the notice says why nothing happened");
                Check(CountOf("PATCH") == patches && server.Collections["u2"]["visibility"]!.ToString() == "visible" && server.Collections["u3"]["visibility"]!.ToString() == "visible", "hide: no request was sent");
                Check(!Texts(Current()).Any(t => t.StartsWith("Hid ") || t.StartsWith("Couldn't")), "hide: no result sentence is written over the reason");
                // Show: u4 is the only hidden collection.
                await PickAsync("u2", "u3", "u4"); await Until(() => Texts(page).Contains("1 selected"), "only u4 is selected");
                ClearNotice();
                await Click(Find<Button>(page, "CollectionsBulkShow")); await Until(() => Dialog(page) != null, "show confirm");
                await PressAsync("Show on tabs");
                await Until(() => Dialog(page) == null && Texts(Current()).Contains(unknown), "show: the notice says why nothing happened");
                Check(CountOf("PATCH") == patches && server.Collections["u4"]["visibility"]!.ToString() == "hidden", "show: no request was sent");
                Check(!Texts(Current()).Any(t => t.StartsWith("Showed ") || t.StartsWith("Couldn't")), "show: no result sentence is written over the reason");
                await Click(Find<Button>(page, "CollectionsSelect"));
                await RefreshAsync("CollectionEdit_u4");
                Check(!page.Controller.OutcomeUnknown, "reading the list again clears the barrier");
                foreach (var id in new[] { "u1", "u2", "u3", "u4" }) server.Collections.Remove(id);
                await RefreshAsync("CollectionEdit_c2");
            });
            await Step("bulk hide and show count, confirm and send only the collections that change", async () =>
            {
                Add("h1", "Hide one"); Add("h2", "Hide two"); Add("h3", "Hide three", "hidden"); Add("h4", "Show four");
                await RefreshAsync("CollectionEdit_h4");
                await EnterSelectAsync(); await PickAsync("h1", "h2", "h3"); await Until(() => Texts(page).Contains("3 selected"), "three selected");
                ClearNotice();
                await Click(Find<Button>(page, "CollectionsBulkHide")); await Until(() => Dialog(page) != null, "hide confirm");
                var confirm = Dialog(page)!;
                Check(Equals(confirm.Title, "Hide 2 collections from Collections tabs?"), "the title counts the two that change, not the three selected");
                Check(Texts(confirm).Contains("2 of 3 selected will change; 1 already hidden."), "the confirmation says one is already hidden");
                await PressAsync("Hide from tabs");
                await Until(() => Dialog(page) == null && server.Collections["h1"]["visibility"]!.ToString() == "hidden" && server.Collections["h2"]["visibility"]!.ToString() == "hidden", "the two that differ are hidden");
                await Until(() => Texts(Current()).Contains("Hid 2 collections from Collections tabs."), "the result counts what changed");
                Check(CountOfPath("PATCH", "/api/v2/admin/collections/h1") == 1 && CountOfPath("PATCH", "/api/v2/admin/collections/h2") == 1 && CountOfPath("PATCH", "/api/v2/admin/collections/h3") == 0, "two requests were sent, none for the collection that was already hidden");
                // Nothing differs: the official text, no dialog, no request.
                await Until(() => Texts(page).Contains("3 selected"), "the selection is kept");
                var requests = server.Log.Count; ClearNotice();
                await Click(Find<Button>(page, "CollectionsBulkHide"));
                await Until(() => Texts(Current()).Contains("The selected collections are already hidden."), "already hidden");
                Check(Dialog(page) == null && server.Log.Count == requests, "hide with none to change opens no dialog and sends no request");
                await PickAsync("h1", "h2", "h3", "h4"); await Until(() => Texts(page).Contains("1 selected"), "only h4 is selected");
                requests = server.Log.Count; ClearNotice();
                await Click(Find<Button>(page, "CollectionsBulkShow"));
                await Until(() => Texts(Current()).Contains("The selected collections are already on Collections tabs."), "already on tabs");
                Check(Dialog(page) == null && server.Log.Count == requests, "show with none to change opens no dialog and sends no request");
                // Mixed show: one hidden, one shown.
                await PickAsync("h3"); await Until(() => Texts(page).Contains("2 selected"), "h3 and h4 selected");
                ClearNotice();
                await Click(Find<Button>(page, "CollectionsBulkShow")); await Until(() => Dialog(page) != null, "show confirm");
                Check(Equals(Dialog(page)!.Title, "Show 1 collection on Collections tabs?") && Texts(Dialog(page)!).Contains("1 of 2 selected will change; 1 already shows."), "the show confirmation counts the one that changes");
                await PressAsync("Show on tabs");
                await Until(() => Dialog(page) == null && server.Collections["h3"]["visibility"]!.ToString() == "visible", "h3 is shown");
                await Until(() => Texts(Current()).Contains("Showed 1 collection on Collections tabs."), "the show result counts what changed");
                Check(CountOfPath("PATCH", "/api/v2/admin/collections/h4") == 0, "the collection that already shows is not sent");
                await Click(Find<Button>(page, "CollectionsSelect"));
                foreach (var id in new[] { "h1", "h2", "h3", "h4" }) server.Collections.Remove(id);
                await RefreshAsync("CollectionEdit_c2");
            });
            await Step("bulk delete counts kept, already gone and refused like the official client; a lost reply and a refused single delete", async () =>
            {
                server.FrozenRevision = true;
                Add("k1", "Refused by a row"); Add("k2", "Already gone"); Add("k3", "Changed one"); Add("k4", "Plain delete"); Add("kr", "Kept row", rows: 1);
                await RefreshAsync("CollectionEdit_kr");
                await EnterSelectAsync(); await PickAsync("k1", "k2", "k3", "k4", "kr"); await Until(() => Texts(page).Contains("5 selected"), "five selected");
                server.Intercept = (method, target) =>
                {
                    if (method != "DELETE") return Task.FromResult<HttpResponseMessage?>(null);
                    if (target.EndsWith("/k1", StringComparison.Ordinal)) return Task.FromResult<HttpResponseMessage?>(Server.Plain(new() { ["type"] = "conflict", ["status"] = 409 }, HttpStatusCode.Conflict));
                    if (target.EndsWith("/k2", StringComparison.Ordinal)) { server.Collections.Remove("k2"); return Task.FromResult<HttpResponseMessage?>(Server.Plain(new() { ["status"] = 404 }, HttpStatusCode.NotFound)); }
                    if (target.EndsWith("/k3", StringComparison.Ordinal)) return Task.FromResult<HttpResponseMessage?>(Server.Plain(new() { ["type"] = "conflict", ["status"] = 412 }, HttpStatusCode.PreconditionFailed));
                    return Task.FromResult<HttpResponseMessage?>(null);
                };
                await Click(Find<Button>(page, "CollectionsBulkDelete")); await Until(() => Dialog(page) != null, "bulk delete confirm");
                Check(Equals(Dialog(page)!.Title, "Delete 4 manual collections?") && Texts(Dialog(page)!).Contains("• 1 is kept because rows use it: Kept row."), "the confirmation counts the four it deletes and names the one a row uses");
                ClearNotice();
                await PressAsync("Delete 4");
                var expected = "Deleted 2 of 4 collections. Kept 1 collection in use by home or library sections. Changed one: It changed since you opened this. Reload and review before trying again.";
                await Until(() => Dialog(page) == null && Texts(Current()).Any(t => t.StartsWith("Deleted ")), "bulk delete result");
                Check(Texts(Current()).Contains(expected), "a 404 counts as deleted, a 409 as kept for rows, a 412 as failed, in the official sentence: " + string.Join(" | ", Texts(Current()).Where(t => t.StartsWith("Deleted "))));
                Check(!server.Collections.ContainsKey("k4") && !server.Collections.ContainsKey("k2") && server.Collections.ContainsKey("k1") && server.Collections.ContainsKey("k3") && server.Collections.ContainsKey("kr"), "only the deleted and the already-gone collections are gone");
                Check(new[] { "k1", "k2", "k3", "k4" }.All(id => CountOfPath("DELETE", "/api/v2/admin/collections/" + id) == 1) && CountOfPath("DELETE", "/api/v2/admin/collections/kr") == 0, "each named collection is sent once and the one a row uses never");
                Check(!Texts(Current()).Any(t => t.Contains("Failed to delete")), "a 409 and a 404 are not reported as failed");
                server.Intercept = null;
                // A lost reply on one of two: that one fails and says the outcome is unknown; the other is deleted.
                await Click(Find<Button>(page, "CollectionsSelect"));
                foreach (var id in new[] { "k1", "k3", "kr" }) server.Collections.Remove(id);
                Add("l1", "Lost one"); Add("l2", "Lost two");
                await RefreshAsync("CollectionEdit_l1");
                await EnterSelectAsync(); await PickAsync("l1", "l2"); await Until(() => Texts(page).Contains("2 selected"), "l1 and l2 selected");
                server.Intercept = (method, target) => method == "DELETE" && target.EndsWith("/l1", StringComparison.Ordinal) ? throw new HttpRequestException("Synthetic lost reply") : Task.FromResult<HttpResponseMessage?>(null);
                await Click(Find<Button>(page, "CollectionsBulkDelete")); await Until(() => Dialog(page) != null, "lost reply confirm");
                await PressAsync("Delete 2");
                await Until(() => Dialog(page) == null && Texts(Current()).Any(t => t.StartsWith("Deleted 1 of 2 collections. Lost one: The outcome is unknown.")), "lost reply result");
                Check(CountOfPath("DELETE", "/api/v2/admin/collections/l1") == 1 && !server.Collections.ContainsKey("l2"), "the lost reply is not replayed and the other collection is deleted");
                server.Intercept = null; server.FrozenRevision = false;
                await Click(Find<Button>(page, "CollectionsSelect"));
                server.Collections.Remove("l1");
                // A single delete the server refuses (409, then 412): the reason is shown, the dialog stays, nothing is replayed, and it is not an unknown outcome.
                Add("e1", "Refused single");
                await RefreshAsync("CollectionEdit_e1");
                foreach (var (status, text) in new[] { (HttpStatusCode.Conflict, "The collection changed or is used by a section."), (HttpStatusCode.PreconditionFailed, "It changed since you opened this.") })
                {
                    var before = CountOfPath("DELETE", "/api/v2/admin/collections/e1");
                    server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "DELETE" && target.EndsWith("/e1", StringComparison.Ordinal) ? Server.Plain(new() { ["type"] = "conflict", ["status"] = (int)status }, status) : null);
                    await Click(Find<Button>(page, "CollectionDelete_e1")); await Until(() => Dialog(page) != null, "single delete dialog");
                    await PressAsync("Delete");
                    await Until(() => Dialog(page) != null && Texts(Dialog(page)!).Any(t => t.Contains(text)), "the refusal's reason is shown (" + (int)status + ")");
                    Check(!page.Controller.OutcomeUnknown && CountOfPath("DELETE", "/api/v2/admin/collections/e1") == before + 1 && server.Collections.ContainsKey("e1"), "a refused single delete (" + (int)status + ") is sent once, keeps the collection and is not an unknown outcome");
                    Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "dialog closed");
                }
                server.Intercept = null; server.Collections.Remove("e1");
                await RefreshAsync("CollectionEdit_c2");
            });
            await Step("a validator that changed after the confirmation refuses that collection only", async () =>
            {
                server.PerCollection = true;
                Add("v1", "Validator one"); Add("v2", "Validator two"); Add("v3", "Validator three");
                await RefreshAsync("CollectionEdit_v3");
                await EnterSelectAsync(); await PickAsync("v1", "v2", "v3"); await Until(() => Texts(page).Contains("3 selected"), "three selected");
                await Click(Find<Button>(page, "CollectionsBulkDelete")); await Until(() => Dialog(page) != null, "bulk delete confirm");
                server.Bump("v2"); // another administrator changed it after the confirmation was drawn
                ClearNotice();
                await PressAsync("Delete 3");
                await Until(() => Dialog(page) == null && Texts(Current()).Any(t => t.StartsWith("Deleted ")), "bulk delete result");
                Check(Texts(Current()).Contains("Deleted 2 of 3 collections. Validator two: It changed since you opened this. Reload and review before trying again."), "the changed collection is refused and named, the others are deleted: " + string.Join(" | ", Texts(Current()).Where(t => t.StartsWith("Deleted "))));
                Check(server.Collections.ContainsKey("v2") && !server.Collections.ContainsKey("v1") && !server.Collections.ContainsKey("v3") && CountOfPath("DELETE", "/api/v2/admin/collections/v2") == 0, "the refused collection stays and is never sent");
                server.PerCollection = false;
                await Click(Find<Button>(page, "CollectionsSelect"));
                server.Collections.Remove("v2");
                await RefreshAsync("CollectionEdit_c2");
            });
            await Step("a starter pack blocks a delete while one runs; the contrast theme is followed while a confirm is open", async () =>
            {
                Add("b1", "Block one"); Add("b2", "Block two");
                await RefreshAsync("CollectionEdit_b2");
                await EnterSelectAsync(); await PickAsync("b1", "b2"); await Until(() => Texts(page).Contains("2 selected"), "two selected");
                var reason = AdminCollectionCopy.StarterPackBlocksDelete;
                TextBlock? Alert(ContentDialog d) => Find<TextBlock>(d, "CollectionsConfirmBlocked");
                // Bulk confirm: open and off while a pack is being added, on again when it finishes, and asked again at the press.
                await Click(Find<Button>(page, "CollectionsBulkDelete")); await Until(() => Dialog(page) != null, "bulk delete confirm");
                var dialog = Dialog(page)!;
                Check(dialog.IsPrimaryButtonEnabled && Alert(dialog)?.Visibility == Visibility.Collapsed, "the confirm is on while no starter pack is being added");
                page.Controller.BeginStarterJob("job-x");
                await Until(() => !dialog.IsPrimaryButtonEnabled, "the confirm goes off while a starter pack is being added");
                Check(Alert(dialog) is { Visibility: Visibility.Visible } alert && alert.Text == reason, "it says the official reason");
                page.Controller.EndStarterJob();
                await Until(() => dialog.IsPrimaryButtonEnabled && Alert(dialog)?.Visibility == Visibility.Collapsed, "the confirm is on again when the pack is done");
                var deletes = CountOf("DELETE");
                page.Controller.BeginStarterJob("job-x");
                await PressAsync("Delete 2"); // before the dialog has looked again: the press asks
                Check(Dialog(page) == dialog && CountOf("DELETE") == deletes && Alert(dialog)?.Text == reason, "a press while a pack is being added keeps the dialog open, sends nothing and says why");
                page.Controller.EndStarterJob();
                dialog.Hide(); await Until(() => Dialog(page) == null, "bulk confirm closed");
                // Single delete: the same.
                await Click(Find<Button>(page, "CollectionDelete_b1")); await Until(() => Dialog(page) != null, "single delete dialog");
                dialog = Dialog(page)!;
                page.Controller.BeginStarterJob("job-x");
                await Until(() => !dialog.IsPrimaryButtonEnabled && Texts(dialog).Contains(reason), "single delete: off with the official reason while a pack is being added");
                page.Controller.EndStarterJob();
                await Until(() => dialog.IsPrimaryButtonEnabled && !Texts(dialog).Contains(reason), "single delete: on again when the pack is done");
                page.Controller.BeginStarterJob("job-x");
                await PressAsync("Delete");
                Check(Dialog(page) == dialog && CountOf("DELETE") == deletes && Texts(dialog).Contains(reason), "single delete: a press while a pack is being added sends nothing and says why");
                page.Controller.EndStarterJob();
                dialog.Hide(); await Until(() => Dialog(page) == null, "single delete closed");
                // The contrast theme is followed while the confirm is open, on any thread, and nothing stays subscribed.
                var original = AdminCollectionsDestructiveStyle.Source; var fake = new FakeContrast();
                AdminCollectionsDestructiveStyle.Source = fake;
                try
                {
                    await Click(Find<Button>(page, "CollectionsBulkDelete")); await Until(() => Dialog(page) != null, "bulk delete confirm for contrast");
                    dialog = Dialog(page)!;
                    Check(ReferenceEquals(dialog.PrimaryButtonStyle, AdminCollectionsDestructiveStyle.PrimaryStyle) && fake.Subscribers == 1, "without a contrast theme the confirm is destructive red and follows the theme");
                    fake.IsHighContrast = true; await Task.Run(fake.Raise);
                    await Until(() => dialog.PrimaryButtonStyle == null, "a switch to a contrast theme returns the open dialog to system colors");
                    fake.IsHighContrast = false; await Task.Run(fake.Raise);
                    await Until(() => ReferenceEquals(dialog.PrimaryButtonStyle, AdminCollectionsDestructiveStyle.PrimaryStyle), "a switch back restores the red");
                    dialog.Hide(); await Until(() => Dialog(page) == null && fake.Subscribers == 0, "closing the dialog leaves nothing subscribed");
                    fake.IsHighContrast = true;
                    await Click(Find<Button>(page, "CollectionsBulkDelete")); await Until(() => Dialog(page) != null, "bulk delete confirm in contrast");
                    dialog = Dialog(page)!;
                    Check(dialog.PrimaryButtonStyle == null, "a dialog opened in a contrast theme keeps system colors");
                    dialog.Hide(); await Until(() => Dialog(page) == null && fake.Subscribers == 0, "contrast dialog closed");
                }
                finally { AdminCollectionsDestructiveStyle.Source = original; }
                await Click(Find<Button>(page, "CollectionsSelect"));
                server.Collections.Remove("b1"); server.Collections.Remove("b2");
                await RefreshAsync("CollectionEdit_c2");
            });
            await Step("names draw on one line, show a placeholder when empty, and the select bar stays in view", async () =>
            {
                server.LibraryName = "Fixture\nmovies​";
                var longRaw = "Long\nname " + new string('y', 300);
                Add("n1", "One\nline name"); Add("n2", "​"); Add("n3", "​", rows: 1); Add("n4", longRaw);
                await RefreshAsync("CollectionEdit_n1");
                var one = (Find<Button>(page, "CollectionEdit_n1")!.Content as TextBlock)!;
                Check(one.Text == "One line name", "a name with line breaks is shown as its plain text: " + one.Text.Replace("\n", "\\n"));
                var reference = (Find<Button>(page, "CollectionEdit_c2")!.Content as TextBlock)!;
                one.Measure(new Windows.Foundation.Size(10000, 10000)); reference.Measure(new Windows.Foundation.Size(10000, 10000));
                Check(Math.Abs(one.DesiredSize.Height - reference.DesiredSize.Height) < 0.5, "the name's block is one line high, as a single-line name's: " + one.DesiredSize.Height + " vs " + reference.DesiredSize.Height);
                TextBlock Copy(string text) => new() { Text = text, FontSize = one.FontSize, FontFamily = one.FontFamily, FontWeight = one.FontWeight, TextWrapping = TextWrapping.NoWrap };
                var rawBlock = Copy("One\nline name"); var plainBlock = Copy("One line name");
                rawBlock.Measure(new Windows.Foundation.Size(10000, 10000)); plainBlock.Measure(new Windows.Foundation.Size(10000, 10000));
                Check(rawBlock.DesiredSize.Height > plainBlock.DesiredSize.Height * 1.5, "the raw name would have drawn on several lines, so the check can fail");
                Check(Texts(page).Contains("Manual · Fixture movies · 1 title") && !Texts(page).Any(t => t.Contains('\n') || t.Contains(' ')), "the library line names the library as one plain line");
                var empty = Find<Button>(page, "CollectionEdit_n2")!;
                Check((empty.Content as TextBlock)?.Text == "Untitled collection" && AutomationProperties.GetName(empty) == "Untitled collection", "a name that shows nothing reads as Untitled collection, drawn and announced");
                var longButton = Find<Button>(page, "CollectionEdit_n4")!;
                Check((longButton.Content as TextBlock)?.Text == AdminCollectionNames.Plain(longRaw, 2000) && AutomationProperties.GetName(longButton) == AdminCollectionNames.Plain(longRaw) && AutomationProperties.GetName(longButton).Length <= 81, "a long name with a line break is one line in the block and a bounded plain announcement");
                await Click(Find<Button>(page, "CollectionDelete_n2")); await Until(() => Dialog(page) != null, "delete dialog for an empty name");
                Check(Equals(Dialog(page)!.Title, "Delete collection?"), "the delete title for an empty name is the official fallback");
                Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "closed");
                Find<ToggleSwitch>(page, "CollectionVisible_n3")!.IsOn = false;
                await Until(() => Dialog(page) != null, "hide confirmation for an empty name");
                Check(Equals(Dialog(page)!.Title, "Hide Untitled collection from Collections tabs?"), "the hide title for an empty name is the official sentence with the placeholder");
                Dialog(page)!.Hide(); await Until(() => Dialog(page) == null && Find<ToggleSwitch>(page, "CollectionVisible_n3")!.IsOn, "hide canceled");
                // Select mode: names announced as plain bounded text; the bar is not part of the scrolling list.
                await EnterSelectAsync();
                Check(AutomationProperties.GetName(Find<CheckBox>(page, "CollectionSelect_n2")!) == "Select Untitled collection" && AutomationProperties.GetName(Find<CheckBox>(page, "CollectionSelect_n4")!) == "Select " + AdminCollectionNames.Plain(longRaw), "a selection box is announced with the plain bounded name");
                Check(AutomationProperties.GetName(Find<ToggleSwitch>(page, "CollectionVisible_n2")!) == "Show Untitled collection on Collections tabs", "a tab switch is announced with the plain name");
                var bar = Find<Border>(page, "CollectionsSelectBar")!; var scroll = (ScrollViewer)Field(page, "_scroll")!;
                Check(bar.Visibility == Visibility.Visible && !Tree(scroll).Contains(bar), "the select bar is outside the scrolling list, so it stays in view while the list scrolls");
                Check(!Find<Button>(page, "CollectionsBulkDelete")!.IsEnabled && !Find<Button>(page, "CollectionsBulkShow")!.IsEnabled && !Find<Button>(page, "CollectionsBulkHide")!.IsEnabled, "with nothing selected the bar's actions are off, as official");
                double Bottom() { bar.UpdateLayout(); return bar.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point(0, bar.ActualHeight)).Y; }
                Check(Math.Abs(Bottom() - page!.ActualHeight) < 1.5, "the bar sits at the foot of the page: " + Bottom() + " of " + page.ActualHeight);
                scroll.ChangeView(null, 100000, null, true); await Task.Delay(250);
                Check(Math.Abs(Bottom() - page.ActualHeight) < 1.5, "after the list scrolls the bar is still at the foot of the page");
                await Click(Find<Button>(page, "CollectionsSelect"));
                Check(Find<Border>(page, "CollectionsSelectBar")!.Visibility == Visibility.Collapsed && Find<Button>(page, "CollectionsBulkDelete") == null, "leaving select mode removes the bar");
                // The empty state names the library as a plain line.
                var combo = Find<ComboBox>(page, "CollectionsLibrary")!; combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "1");
                await Until(() => Find<Button>(page, "CollectionEdit_n1") != null && Find<ComboBox>(page, "CollectionsLibrary")?.SelectedItem is ComboBoxItem { Tag: "1" }, "library view");
                server.EmptyList = true; await Click(Find<Button>(page, "CollectionsRefresh"));
                await Until(() => Texts(page).Contains("No collections in Fixture movies yet"), "the empty state names the library");
                server.EmptyList = false; await Click(Find<Button>(page, "CollectionsRefresh")); await Until(() => Find<Button>(page, "CollectionEdit_n1") != null, "list returns");
                combo = Find<ComboBox>(page, "CollectionsLibrary")!; combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "");
                await Until(() => Find<ComboBox>(page, "CollectionsLibrary")?.SelectedItem is ComboBoxItem { Tag: "" } && Find<Button>(page, "CollectionEdit_n1") != null, "all libraries again");
                server.LibraryName = "Fixture movies";
                foreach (var id in new[] { "n1", "n2", "n3", "n4" }) server.Collections.Remove(id);
                await RefreshAsync("CollectionEdit_c2");
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
                await SetText(Find<TextBox>(page, "CollectionName")!, "Fixture smart renamed");
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => server.Sent("PATCH", "/api/v2/admin/collections/c2").Count() >= 2 || Texts(Current()).FirstOrDefault() == "Collection saved", "smart save sent");
                await Task.Delay(300);
                Program.Log("DIAGNOSTIC: smart PATCH body " + server.Sent("PATCH", "/api/v2/admin/collections/c2").LastOrDefault().Body + " title=" + server.Collections["c2"]["title"]);
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
                await SetText(Find<TextBox>(page, "CollectionName")!, "My local title");
                var writes = server.Sent("PATCH", "/api/v2/admin/collections/c2").Count();
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => Dialog(page) != null, "conflict review dialog");
                Check(server.Sent("PATCH", "/api/v2/admin/collections/c2").Count() == writes + 1, "the stale save was sent once and rejected");
                var review = Dialog(page)!; await Until(() => Find<CheckBox>(review, "CollectionConflict_title") != null, "conflict dialog content");
                Check(true, "conflict review lists the changed field by name");
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
                page = Current(); await SetText(Find<TextBox>(page, "CollectionName")!, "Created fixture");
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
                await SetText(Find<TextBox>(page, "CollectionName")!, "Created fixture");
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => !Find<Button>(page, "CollectionEditorSave")!.IsEnabled, "malformed identity is an unknown outcome");
                Check(server.Requests.Count(r => r == "POST /api/v2/admin/collections") == 2, "malformed identity is not replayed");
                server.Intercept = null;
                await Click(Find<Button>(page, "CollectionEditorCheckList")); await Until(() => Find<Button>(page, "CollectionEditorSave")?.IsEnabled == true, "re-read re-enables Save again");
                // Confirmed create whose canonical read then fails: identity survives Reload and nothing is created again.
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "GET" && target.StartsWith("/api/v2/admin/collections/new") ? Server.Plain(new() { ["status"] = 404 }, HttpStatusCode.NotFound) : null);
                await SetText(Find<TextBox>(page, "CollectionName")!, "Created fixture");
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
                await CaptureWidthsAsync(shell, "admin-collections-manual-editor");
                // Titles dialog on the created collection.
                await Click(Find<Button>(page, "CollectionAddTitles")); await Until(() => Dialog(page) != null, "title picker");
                var picker = Dialog(page)!; await SetText(Find<TextBox>(picker, "CollectionTitleSearch")!, "Fixture");
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
                Check(Find<TextBox>(page, "CollectionListUrl")!.FlowDirection == FlowDirection.LeftToRight && Find<TextBox>(page, "CollectionArtworkUrl_poster")?.FlowDirection == FlowDirection.LeftToRight, "web addresses are typed and shown left to right");
                await SetText(search!, "top");
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
                server.RowTitle = "Shared fixture\nrow​"; server.LibraryName = "Fixture\nmovies​";
                Check(AdminShellPage.Open(AdminRouteRequest.CollectionEditor("c3").WithQuery("libraryId", "1")), "synced editor opens");
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<Button>(p, "CollectionRows") != null, "editor with rows button");
                page = Current();
                await Click(Find<Button>(page, "CollectionRows")); await Until(() => Dialog(page) != null, "rows dialog");
                Check(Texts(Dialog(page)!).Any(t => t.Contains("Shared fixture row")), "rows dialog lists the row that shows the collection");
                Check(Texts(Dialog(page)!).Contains("Shared fixture row · On") && !Texts(Dialog(page)!).Any(t => t.Contains('\n') || t.Contains(' ')), "the rows dialog names each row as one plain line");
                Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "rows dialog closed");
                await Click(Find<Button>(page, "CollectionAddAsRow")); await Until(() => Dialog(page) != null, "add as row dialog");
                Check(Find<Button>(Dialog(page)!, "CollectionAddRow_home") != null, "Add as a row offers Home and each library page");
                Check(Find<Button>(Dialog(page)!, "CollectionAddRow_1")?.Content?.ToString() == "Fixture movies page", "a library page button is labelled with the plain library name");
                server.RowTitle = "Shared fixture row"; server.LibraryName = "Fixture movies";
                Dialog(page)!.Hide(); await Until(() => Dialog(page) == null, "add as row closed");
                await Click(Find<Button>(page, "CollectionEditorBack"));
                await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<Button>(p, "CollectionsNew") != null, "back to list");
                page = Current();
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 600)); await Task.Delay(150);
                await CaptureAsync(shell, "admin-collections-list-800.png");
                window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            });

            // ── Second review (November 20): starter-job observation, create and save paths ─
            async Task OpenAsync(AdminRouteRequest request, string readyId)
            {
                var previous = page;
                Check(AdminShellPage.Open(request), "editor descendant opens: " + request.Href);
                await Until(() => inner.Content is AdminCollectionsPage p && p != previous && Find<TextBox>(p, readyId) != null, "editor " + readyId);
                page = Current();
            }
            async Task BackToListAsync()
            {
                var previous = page;
                await Click(Find<Button>(page, "CollectionEditorBack"));
                await Until(() => inner.Content is AdminCollectionsPage p && p != previous && Find<Button>(p, "CollectionsNew") != null, "back to list");
                page = Current();
            }
            await Step("starter job: a failed read and the bounded cap end observation, Refresh rediscovers it", async () =>
            {
                server.JobTerminal = false; server.JobPolls = 0; server.ServeActiveJob = false;
                await Click(Find<Button>(page, "CollectionsStarterPacks")); await Until(() => Dialog(page) != null, "starter pack dialog");
                var dialog = Dialog(page)!; await Until(() => Find<CheckBox>(dialog, "CollectionPackLibrary_1") is { IsLoaded: true }, "pack library loaded"); { var pack = Find<CheckBox>(dialog, "CollectionPackLibrary_1")!; if (pack.IsChecked != true) Toggle(pack); }
                await Click(Find<Button>(dialog, "CollectionPackPreview")); await Until(() => dialog.IsPrimaryButtonEnabled, "preview ready");
                // The next poll's read fails.
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "GET" && target == "/api/v2/admin/collection-jobs/job1" ? Server.Plain(new() { ["status"] = 500 }, HttpStatusCode.InternalServerError) : null);
                await Click(Tree(dialog).OfType<Button>().First(b => b.Content?.ToString() == "Add starter pack"));
                await Until(() => Dialog(page) == null && (page.Controller.StarterJobId == "job1" || Texts(page).Any(t => t.Contains("not queued again"))), "job accepted");
                await Until(() => page.Controller.StarterJobId == null, "a failed poll ends the observation instead of leaving the id behind", 9000);
                var timer = (DispatcherTimer)Field(page, "_jobTimer")!;
                Check(!timer.IsEnabled, "no poll timer runs after a failed read");
                Check(Texts(page).Any(t => t.Contains("The starter pack was not queued again")), "a failed poll says the pack was not queued again");
                // Refresh rediscovers the still-running job and polls it again.
                server.Intercept = null; server.ServeActiveJob = true; server.JobPolls = 0;
                await Click(Find<Button>(page, "CollectionsRefresh"));
                await Until(() => page.Controller.StarterJobId == "job1", "Refresh rediscovers the running job");
                await Until(() => server.JobPolls >= 1, "the rediscovered job is polled again", 8000);
                // The 120-read cap ends observation the same way.
                while (page.Controller.StarterJobReads < 119) page.Controller.CountStarterJobRead();
                await Until(() => page.Controller.StarterJobId == null, "the read cap ends the observation instead of leaving the id behind", 9000);
                Check(!timer.IsEnabled, "no poll timer runs after the cap");
                Check(Texts(page).Any(t => t.Contains("still running")), "the cap says the pack is still running");
                server.JobPolls = 0;
                await Click(Find<Button>(page, "CollectionsRefresh"));
                await Until(() => page.Controller.StarterJobId == "job1", "Refresh rediscovers the job after the cap");
                await Until(() => server.JobPolls >= 1, "the job after the cap is polled again", 8000);
                server.JobTerminal = true;
                await Until(() => page.Controller.StarterJobId == null, "the finished job is observed", 9000);
                server.ServeActiveJob = false;
                // The editor routes above carry a library, so the list is in Arrange (board) mode.
                await Until(() => Find<Button>(page, "CollectionEdit_c2") != null || Find<Button>(page, "CollectionBoardEdit_c2") != null, "list is reloaded after the job finishes");
            });
            await Step("template poster travels with the import", async () =>
            {
                await Click(Find<Button>(page, "CollectionsNew")); await Until(() => Dialog(page) != null, "new picker");
                await Click(Find<Button>(Dialog(page)!, "CollectionsTemplates"));
                await Until(() => Dialog(page) != null && Find<Button>(Dialog(page)!, "CollectionTemplate_t1") != null, "template catalog dialog", 12000);
                await Click(Find<Button>(Dialog(page)!, "CollectionTemplate_t1"));
                await Until(() => Dialog(page) == null && Find<TextBox>(page, "CollectionName") != null, "template seeds a real editor");
                Check(page.Controller.Draft!["poster_url"]?.ToString() == "/images/collection-templates/fixture.jpg", "the template's poster is carried into the draft");
                var library = Find<CheckBox>(page, "CollectionLibrary_1")!; if (library.IsChecked != true) Toggle(library);
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => server.Sent("POST", "/api/v2/admin/collections/import/tmdb").Any(), "template import posted");
                var import = JsonNode.Parse(server.Sent("POST", "/api/v2/admin/collections/import/tmdb").Last().Body)!;
                Check(import["poster_url"]?.ToString() == "/images/collection-templates/fixture.jpg", "the template import sends the template's poster as poster_url");
                await Until(() => page.Controller.Editor?.Value["id"]?.ToString().StartsWith("imp") == true, "template import saved");
                await BackToListAsync();
            });
            await Step("manual title add goes after the last position", async () =>
            {
                await OpenAsync(AdminRouteRequest.CollectionNew().WithQuery("libraryId", "1").WithQuery("type", "manual").WithQuery("view", "list"), "CollectionName");
                await SetText(Find<TextBox>(page, "CollectionName")!, "Positioned fixture");
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => page.Controller.Editor?.Value["id"]?.ToString().StartsWith("new") == true && Find<Button>(page, "CollectionAddTitles") != null, "manual collection created");
                var id = page.Controller.Editor!.Value["id"]!.ToString();
                await Click(Find<Button>(page, "CollectionAddTitles")); await Until(() => Dialog(page) != null, "title picker");
                var picker = Dialog(page)!; await SetText(Find<TextBox>(picker, "CollectionTitleSearch")!, "Fixture");
                await Click(Find<Button>(picker, "CollectionTitlesSearchButton")); await Until(() => Find<CheckBox>(picker, "CollectionTitlePick_m1") != null, "title result");
                Toggle(Find<CheckBox>(picker, "CollectionTitlePick_m1")!);
                // The collection already holds m1 at position 0 (item_count 0 in this fixture): the new title goes after it.
                await Click(Tree(picker).OfType<Button>().First(b => b.Content?.ToString() == "Add titles"));
                await Until(() => server.Sent("PUT", "/api/v2/admin/collections/" + id + "/items/m1").Any(), "title added");
                var added = JsonNode.Parse(server.Sent("PUT", "/api/v2/admin/collections/" + id + "/items/m1").Last().Body)!;
                Check(added["position"]!.GetValue<int>() == 1, "a new title is placed after the last existing position, not at the item count");
                await Until(() => Dialog(page) == null, "picker closed");
                await BackToListAsync();
            });
            await Step("rejected create shows the reason; the list check reads every library", async () =>
            {
                await OpenAsync(AdminRouteRequest.CollectionNew().WithQuery("libraryId", "1").WithQuery("type", "manual").WithQuery("view", "list"), "CollectionName");
                await SetText(Find<TextBox>(page, "CollectionName")!, "Lost create");
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "POST" && target == "/api/v2/admin/collections"
                    ? Server.Plain(new() { ["type"] = "https://fixture.invalid/docs/api/v2/problems/validation", ["title"] = "Invalid", ["status"] = 422, ["detail"] = "A collection named Lost create already exists." }, HttpStatusCode.UnprocessableEntity) : null);
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => Texts(page).Any(t => t.Contains("A collection named Lost create already exists.")), "the server's reason is shown");
                Check(Find<Button>(page, "CollectionEditorSave")!.IsEnabled && !page.Controller.OutcomeUnknown, "a rejected create is not an unknown outcome");
                // The reply to a create is lost while the page filters by library 1; the collection went to library 2 only.
                server.Intercept = (method, target) =>
                {
                    if (method == "POST" && target == "/api/v2/admin/collections")
                    {
                        server.Collections["lost1"] = JsonNode.Parse("""{"id":"lost1","title":"Lost create","collection_type":"manual","library_id":"2","library_ids":["2"],"visibility":"visible","item_count":0,"group_id":null,"sort_order":9,"featured":false,"description":"","query_definition":{},"sort_config":{},"source_config":{}}""")!.AsObject();
                        throw new HttpRequestException("Synthetic lost reply");
                    }
                    if (method == "GET" && target.StartsWith("/api/v2/admin/collections?library_id=1", StringComparison.Ordinal))
                        return Task.FromResult<HttpResponseMessage?>(server.Reply(new() { ["items"] = new JsonArray(server.Collections.Values.Where(v => v["library_id"]?.ToString() == "1").Select(v => v.DeepClone()).ToArray()), ["groups"] = new JsonArray(), ["page"] = new JsonObject { ["has_more"] = false } }));
                    return Task.FromResult<HttpResponseMessage?>(null);
                };
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => Find<Button>(page, "CollectionEditorCheckList") != null, "unknown-outcome recovery");
                await Click(Find<Button>(page, "CollectionEditorCheckList"));
                await Until(() => Find<Button>(page, "CollectionEditorSave")?.IsEnabled == true, "list re-read re-enables Save");
                Check(Texts(Current()).Any(t => t.Contains("already exists in the list")), "a create that went to another library is found by the list check, so it is not created twice");
                var lists = server.Requests.Where(r => r == "GET /api/v2/admin/collections" || r.StartsWith("GET /api/v2/admin/collections?", StringComparison.Ordinal)).ToArray();
                Check(lists.Length > 0 && lists[^1] == "GET /api/v2/admin/collections", "the list check reads the unfiltered list");
                server.Intercept = null; server.Collections.Remove("lost1");
                page.Controller.Discard(); await BackToListAsync();
            });
            await Step("hide after import: a failure keeps the create and warns; links are cleaned", async () =>
            {
                await OpenAsync(AdminRouteRequest.CollectionNew().WithQuery("libraryId", "1").WithQuery("type", "synced").WithQuery("source", "mdblist"), "CollectionListUrl");
                await SetText(Find<TextBox>(page, "CollectionListUrl")!, " https://mdblist.com/lists/clean/me/json/?x=1#y ");
                await SetText(Find<TextBox>(page, "CollectionName")!, "Hidden import");
                Find<ToggleSwitch>(page, "CollectionEditorVisibility")!.IsOn = false;
                await Until(() => page.Controller.Draft!["visibility"]?.ToString() == "hidden", "the draft is hidden");
                var imports = server.Requests.Count(r => r == "POST /api/v2/admin/collections/import/mdblist");
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "PATCH" && target.StartsWith("/api/v2/admin/collections/imp", StringComparison.Ordinal) ? Server.Plain(new() { ["status"] = 500 }, HttpStatusCode.InternalServerError) : null);
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => Texts(page).Any(t => t.Contains("still shows on Collections tabs")), "the failed hide is a warning that it still shows");
                Check(server.Requests.Count(r => r == "POST /api/v2/admin/collections/import/mdblist") == imports + 1, "the import was sent once");
                var body = JsonNode.Parse(server.Sent("POST", "/api/v2/admin/collections/import/mdblist").Last().Body)!;
                Check(body["url"]?.ToString() == "https://mdblist.com/lists/clean/me/json", "a new list's link is sent without spaces, query, fragment or trailing slash");
                Check(page.Controller.CreatedId != null && page.Controller.Editor != null && !page.Controller.OutcomeUnknown, "the created list is loaded by its identity");
                Check(server.Collections.Values.Any(c => c["title"]?.ToString() == "Hidden import" && c["visibility"]?.ToString() == "visible"), "the list exists and still shows");
                server.Intercept = null;
                await BackToListAsync();
                // A saved list's pasted link is cleaned the same way.
                await OpenAsync(AdminRouteRequest.CollectionEditor("c3").WithQuery("libraryId", "1"), "CollectionListUrl");
                await SetText(Find<TextBox>(page, "CollectionListUrl")!, "  https://mdblist.com/lists/a/other/json/?sort=rank#top  ");
                var patches = server.Sent("PATCH", "/api/v2/admin/collections/c3").Count();
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => server.Sent("PATCH", "/api/v2/admin/collections/c3").Count() > patches, "saved list patched");
                var patch = JsonNode.Parse(server.Sent("PATCH", "/api/v2/admin/collections/c3").Last().Body)!;
                Check(patch["source_url"]?.ToString() == "https://mdblist.com/lists/a/other/json" && patch["source_config"]!["url"]!.ToString() == "https://mdblist.com/lists/a/other/json", "a saved list's pasted link is stored without spaces, query, fragment or trailing slash");
                await Until(() => Texts(page).Any(t => t.StartsWith("Collection saved")), "saved");
                await BackToListAsync();
            });
            await Step("a rejected staged title leaves an editor whose later edits are saved", async () =>
            {
                await OpenAsync(AdminRouteRequest.CollectionNew().WithQuery("libraryId", "1").WithQuery("type", "manual").WithQuery("view", "list"), "CollectionName");
                await SetText(Find<TextBox>(page, "CollectionName")!, "Staged fixture");
                await Click(Find<Button>(page, "CollectionAddTitles")); await Until(() => Dialog(page) != null, "title picker");
                var picker = Dialog(page)!; await SetText(Find<TextBox>(picker, "CollectionTitleSearch")!, "Fixture");
                await Click(Find<Button>(picker, "CollectionTitlesSearchButton")); await Until(() => Find<CheckBox>(picker, "CollectionTitlePick_m1") != null, "title result");
                Toggle(Find<CheckBox>(picker, "CollectionTitlePick_m1")!);
                await Click(Tree(picker).OfType<Button>().First(b => b.Content?.ToString() == "Add titles"));
                await Until(() => Dialog(page) == null && Texts(page).Any(t => t.EndsWith("titles ready to add")), "title staged");
                // The collection is created, then the server rejects its staged title.
                server.Intercept = (method, target) => Task.FromResult<HttpResponseMessage?>(method == "PUT" && target.EndsWith("/items/m1", StringComparison.Ordinal)
                    ? Server.Plain(new() { ["type"] = "https://fixture.invalid/docs/api/v2/problems/validation", ["title"] = "Invalid", ["status"] = 422, ["detail"] = "This title cannot be added." }, HttpStatusCode.UnprocessableEntity) : null);
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => page.Controller.Editor?.Value["id"]?.ToString().StartsWith("new") == true && Find<Button>(page, "CollectionEditorSave")?.Content?.ToString() == "Save", "the editor is rendered again on the created collection");
                var id = page.Controller.Editor!.Value["id"]!.ToString();
                Check(Texts(page).Any(t => t.Contains("This title cannot be added.")), "the rejected title's reason stays shown");
                Check(!page.Controller.OutcomeUnknown && Texts(page).Any(t => t.Contains("Not added:")), "a rejected title is not an unknown outcome and is named as not added");
                server.Intercept = null; var staged = server.Sent("PUT", "/api/v2/admin/collections/" + id + "/items/m1").Count();
                await SetText(Find<TextBox>(page, "CollectionDescription")!, "Edited after the rejected title");
                Check(page.Controller.Dirty && page.Controller.Draft!["description"]?.ToString() == "Edited after the rejected title", "an edit after the rejected title reaches the current draft and counts as unsaved");
                await Click(Find<Button>(page, "CollectionEditorSave"));
                await Until(() => server.Sent("PATCH", "/api/v2/admin/collections/" + id).Any(), "the next Save patches the created collection");
                Check(JsonNode.Parse(server.Sent("PATCH", "/api/v2/admin/collections/" + id).Last().Body)!["description"]?.ToString() == "Edited after the rejected title", "the next Save sends the edit made after the rejected title");
                // The first Save's "Collection saved. Not added: …" notice is still shown. Only the exact text says this Save has finished, after any title it sent.
                await Until(() => Texts(page).Any(t => t == "Collection saved"), "the next Save finishes with nothing left to report");
                Check(server.Sent("PUT", "/api/v2/admin/collections/" + id + "/items/m1").Count() == staged, "the rejected title is not sent again with the next Save");
                await BackToListAsync();
            });

            // ── Authority retirement removes private data ───────────────────────
            await Step("authority retirement with a running starter-pack job", async () =>
            {
                server.JobTerminal = false; server.JobPolls = 0;
                await Click(Find<Button>(page, "CollectionsStarterPacks")); await Until(() => Dialog(page) != null, "starter pack dialog again");
                var dialog = Dialog(page)!; await Until(() => Find<CheckBox>(dialog, "CollectionPackLibrary_1") is { IsLoaded: true }, "pack library loaded"); { var pack = Find<CheckBox>(dialog, "CollectionPackLibrary_1")!; if (pack.IsChecked != true) Toggle(pack); }
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
    /// <summary>The confirm button of a delete is the official destructive fill (#dc2626, white label, the fill at 90% on hover and press), and focus starts on Cancel. A Windows contrast theme keeps system colors.</summary>
    private static async Task AssertDestructiveAsync(ContentDialog dialog, string what)
    {
        // Where focus actually is once the dialog has opened, in a contrast theme as well.
        await Until(() => FocusManager.GetFocusedElement(dialog.XamlRoot) is Button { Name: "CloseButton" }, what + ": focus starts on Cancel");
        Check(true, what + ": focus starts on Cancel (the focused element is the dialog's Cancel button)");
        var probe = new ContentDialog(); var untouched = probe.PrimaryButtonStyle;
        Check(!AdminCollectionsDestructiveStyle.Apply(probe, true) && ReferenceEquals(probe.PrimaryButtonStyle, untouched), what + ": a contrast theme keeps the system colors");
        if (AdminCollectionsDestructiveStyle.HighContrast) { Check(dialog.PrimaryButtonStyle != AdminCollectionsDestructiveStyle.PrimaryStyle, what + ": the host is in a contrast theme, so the dialog keeps system colors"); return; }
        Button? primary = null;
        await Until(() => (primary = Tree(dialog).OfType<Button>().FirstOrDefault(b => b.Name == "PrimaryButton"))?.IsLoaded == true, what + ": primary button");
        var root = Tree(primary!).OfType<Border>().FirstOrDefault(b => b.Name == "Root");
        Check(primary!.Background is SolidColorBrush { Color: { A: 255, R: 0xDC, G: 0x26, B: 0x26 }, Opacity: 1 } && root?.Background is SolidColorBrush { Color: { A: 255, R: 0xDC, G: 0x26, B: 0x26 } }, what + ": the confirm button is drawn in --destructive-fill #dc2626");
        Check(primary.Foreground is SolidColorBrush { Color: { R: 255, G: 255, B: 255 } }, what + ": its label is white");
        foreach (var state in new[] { "PointerOver", "Pressed" })
        {
            Check(VisualStateManager.GoToState(primary, state, false) && root?.Background is SolidColorBrush { Color: { A: 230, R: 0xDC, G: 0x26, B: 0x26 } }, what + ": " + state + " draws the fill at 90%");
            VisualStateManager.GoToState(primary, "Normal", false);
        }
        Check(root?.Background is SolidColorBrush { Color: { A: 255, R: 0xDC, G: 0x26, B: 0x26 } }, what + ": it returns to the full fill");
    }
    /// <summary>A contrast theme the test can change under an open dialog, counting who listens.</summary>
    private sealed class FakeContrast : AdminCollectionsDestructiveStyle.IHighContrastSource
    {
        private Action? _changed;
        public bool IsHighContrast { get; set; }
        public int Subscribers;
        public event Action? Changed { add { _changed += value; Interlocked.Increment(ref Subscribers); } remove { _changed -= value; Interlocked.Decrement(ref Subscribers); } }
        public void Raise() => _changed?.Invoke();
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
    /// <summary>TextBox.TextChanged is raised after the set returns; wait so page handlers have seen the text before the next command.</summary>
    private static async Task SetText(TextBox box, string value) { box.Text = value; await Task.Delay(200); }
    private static void Toggle(CheckBox box) => (FrameworkElementAutomationPeer.CreatePeerForElement(box) as IToggleProvider ?? throw new InvalidOperationException("Checkbox has no toggle peer")).Toggle();
    private static async Task Click(Button? button)
    {
        if (button == null) { Check(false, "native command is rendered and enabled: missing"); throw new InvalidOperationException("Missing command."); }
        for (var i = 0; i < 80 && !button.IsLoaded; i++) await Task.Delay(25);
        Check(button is { IsLoaded: true, IsEnabled: true }, "native command is rendered and enabled: " + AutomationProperties.GetAutomationId(button));
        for (var i = 0; i < 80 && !button.IsLoaded; i++) await Task.Delay(25);
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
