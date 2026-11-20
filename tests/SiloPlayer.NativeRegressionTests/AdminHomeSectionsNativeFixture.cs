using System.Net;
using System.Net.Http.Headers;
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
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>Real registration, shell and Frame against a synthetic Silo server. Drives loading, error, empty and populated states, every dialog, and refused, stale and unknown writes.</summary>
internal static class AdminHomeSectionsNativeFixture
{
    private sealed class Channel : IAdminRealtimeChannel
    {
        public WebSocketState CurrentState => WebSocketState.Open;
        public event Action<string, JsonElement>? SnapshotReceived;
        public event Action<string, string, JsonElement>? EventReceived;
        public event Action<WebSocketState>? StateChanged;
        public IDisposable Subscribe(params string[] channels) => new Empty();
        public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot) { snapshot = default; return false; }
        private sealed class Empty : IDisposable { public void Dispose() { } }
    }

    private sealed class Server : HttpMessageHandler
    {
        internal readonly List<(string Method, string Path, string? Tag, string Body)> Requests = [];
        internal readonly Dictionary<string, JsonObject> Rows = new();
        internal readonly Dictionary<string, List<string>> Orders = new() { ["home"] = ["a", "b", "c"], ["library:1"] = ["la"], ["library:2"] = [], ["library:3"] = [] };
        private readonly Dictionary<string, int> _versions = new();
        private int _order = 1, _created, _settings = 1;
        internal TaskCompletionSource? GateOrder;
        internal bool FailList, FailNextDelete412, LoseNextPatch, ConflictNextPatch, LoseNextCreate, RefuseNextCreate, RefuseNextSetting, RefuseDefaultsFor2, RefuseNextPatchCount;
        internal string? RefusePatchDetail;
        internal string NextUpMode = "\"combined\"", NewFirst = "false";

        internal Server()
        {
            Add("a", "Synthetic first", "recently_added", true, "home", null, """{"future_option":{"keep":true}}""");
            Add("b", "Synthetic disabled", "favorites", false, "home", null, "{}");
            Add("c", "Synthetic collection row", "collection", true, "home", null, """{"library_collection_id":"c1"}""");
            Add("la", "Synthetic library row", "recently_added", true, "library", "1", "{}");
        }
        private void Add(string id, string title, string type, bool enabled, string scope, string? library, string config)
        {
            Rows[id] = JsonNode.Parse($$"""{"id":"{{id}}","scope":"{{scope}}","library_id":{{(library == null ? "null" : "\"" + library + "\"")}},"position":{{Rows.Count}},"section_type":"{{type}}","title":"{{title}}","enabled":{{enabled.ToString().ToLowerInvariant()}},"featured":false,"item_limit":20,"web_only":false,"config":{{config}}}""")!.AsObject();
            _versions[id] = 1;
        }
        private string OrderTag => "\"order-" + _order + "\"";
        private string RowTag(string id) => "\"" + id + "-" + _versions[id] + "\"";
        private static string KeyOf(HttpRequestMessage request) => request.RequestUri!.Query.Contains("scope=library") ? "library:" + (request.RequestUri.Query.Contains("library_id=2") ? "2" : request.RequestUri.Query.Contains("library_id=3") ? "3" : "1") : "home";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            var tag = request.Headers.IfMatch.FirstOrDefault()?.ToString();
            if (request.Method != HttpMethod.Get || path.EndsWith("/settings/effective")) Requests.Add((request.Method.Method, request.RequestUri.PathAndQuery, tag, body));
            else Requests.Add(("GET", request.RequestUri.PathAndQuery, tag, ""));
            var key = KeyOf(request); var order = Orders[key];
            if (path == "/api/v2/admin/sections/capabilities") return Json("""{"available":true,"preview":true,"reset_profiles":true,"web_only":true}""");
            if (path == "/api/v2/sections/recipes")
                return Json("""
                {"categories":[{"category":"library_staples","recipes":[ {"type":"recently_added","category":"library_staples","presets":[{"key":"recent","display_name":"Recently added","description_short":"The latest titles.","default_params":{}}]}, {"type":"trending_on_server","category":"popular","presets":[{"key":"tr_24h","display_name":"Trending on this server","description_short":"x","default_params":{"window":"24h"}},{"key":"tr_7d","display_name":"Trending this week","description_short":"x","default_params":{"window":"7d"}}]}, {"type":"collection","category":"custom","presets":[{"key":"collection","display_name":"A collection","description_short":"A collection","default_params":{}}]}, {"type":"next_up","category":"x","presets":[{"key":"n","display_name":"Next up","description_short":"x","default_params":{}}]}]}]}
                """);
            if (path == "/api/v2/admin/sections/preview") return Json("""{"items":[{"content_id":"movie:synthetic","title":"Synthetic preview title"}],"total_count":1}""");
            if (path == "/api/v2/admin/collections") return Json("""{"items":[{"id":"c1","title":"Synthetic collection","collection_type":"manual","visibility":"visible"}]}""");
            if (path == "/api/v2/admin/collections/c1") return Json("""{"id":"c1","title":"Synthetic collection","visibility":"visible"}""");
            if (path.StartsWith("/api/v2/admin/collections/", StringComparison.Ordinal)) return Problem(404, "Not found.");
            if (path == "/api/v2/libraries") return Json("""{"items":[{"id":"1","name":"Synthetic Movies","type":"movie"},{"id":"2","name":"Synthetic second movies","type":"movie"},{"id":"3","name":"Synthetic Shows","type":"series"}]}""");
            if (path == "/api/v2/admin/settings/effective") return Json($$"""{"defaults.ui.next_up_mode":{{JsonSerializer.Serialize(NextUpMode)}},"defaults.ui.next_up_new_releases_first":{{JsonSerializer.Serialize(NewFirst)}}}""", "\"settings-" + _settings + "\"");
            if (path == "/api/v2/admin/settings" && request.Method == HttpMethod.Put)
            {
                if (RefuseNextSetting) { RefuseNextSetting = false; return Problem(422, "Next Up mode is not available."); }
                var values = JsonNode.Parse(body)!["values"]!.AsObject();
                if (values["defaults.ui.next_up_mode"] is { } mode) NextUpMode = mode.GetValue<string>();
                if (values["defaults.ui.next_up_new_releases_first"] is { } first) NewFirst = first.GetValue<string>();
                _settings++;
                return Json($$"""{"values":{"defaults.ui.next_up_mode":{{JsonSerializer.Serialize(NextUpMode)}},"defaults.ui.next_up_new_releases_first":{{JsonSerializer.Serialize(NewFirst)}}},"restart_required":false}""", "\"settings-" + _settings + "\"");
            }
            if (path == "/api/v2/admin/sections/order")
            {
                if (GateOrder != null) { var gate = GateOrder; GateOrder = null; await gate.Task; }
                if (request.Method == HttpMethod.Put)
                {
                    if (tag != OrderTag) return Problem(412, "Rows changed.");
                    var ids = JsonNode.Parse(body)!["ordered_ids"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
                    if (ids.Count != order.Count || ids.Distinct().Count() != ids.Count || ids.Any(id => !order.Contains(id))) return Problem(422, "Order must contain every row.");
                    order.Clear(); order.AddRange(ids); _order++;
                }
                return Json(JsonSerializer.Serialize(new { scope = key == "home" ? "home" : "library", library_id = key == "home" ? null : key[8..], ordered_ids = order }), OrderTag);
            }
            if (path == "/api/v2/admin/sections/defaults")
            {
                if (tag != OrderTag) return Problem(412, "Rows changed.");
                if (RefuseDefaultsFor2 && key == "library:2") return Problem(422, "This page cannot be restored.");
                _order++; return Json(JsonSerializer.Serialize(new { scope = key == "home" ? "home" : "library", library_id = key == "home" ? null : key[8..], ordered_ids = order }), OrderTag);
            }
            if (path == "/api/v2/admin/sections/bulk")
            {
                var payload = JsonNode.Parse(body)!.AsObject(); var ids = payload["library_ids"]!.AsArray();
                foreach (var node in ids) { var id = "copy" + ++_created; var row = payload.DeepClone().AsObject(); var target = node!.GetValue<string>(); row.Remove("library_ids"); row["id"] = id; row["library_id"] = target; row["scope"] = "library"; row["position"] = Orders["library:" + target].Count; row["web_only"] = false; Rows[id] = row; _versions[id] = 1; Orders["library:" + target].Add(id); }
                _order++; return Json("{\"created\":" + ids.Count + "}");
            }
            if (path == "/api/v2/admin/sections")
            {
                if (request.Method == HttpMethod.Post)
                {
                    if (RefuseNextCreate) { RefuseNextCreate = false; return Problem(422, "That row name is not allowed.", ("name_taken", "Choose a different row name.")); }
                    var row = JsonNode.Parse(body)!.AsObject(); var id = "new" + ++_created; row["id"] = id; row["web_only"] ??= false;
                    var target = row["scope"]!.GetValue<string>() == "home" ? "home" : "library:" + row["library_id"]!.GetValue<string>();
                    if (row["scope"]!.GetValue<string>() == "home") row["library_id"] = null;
                    Rows[id] = row; _versions[id] = 1; Orders[target].Add(id); _order++;
                    if (LoseNextCreate) { LoseNextCreate = false; throw new HttpRequestException("Synthetic lost answer after apply"); }
                    return Json(row.ToJsonString(), null, HttpStatusCode.Created);
                }
                if (FailList) return Problem(500, "Synthetic list failure.", null, "internal-error");
                return Json("{\"items\":[" + string.Join(",", order.Select(id => Rows[id].ToJsonString())) + "]}");
            }
            if (path.StartsWith("/api/v2/admin/sections/", StringComparison.Ordinal))
            {
                var id = path.Split('/').Last(); if (!Rows.TryGetValue(id, out var row)) return Problem(404, "Row not found.");
                if (request.Method == HttpMethod.Get) return Json(row.ToJsonString(), RowTag(id));
                if (request.Method == HttpMethod.Patch)
                {
                    if (LoseNextPatch) { LoseNextPatch = false; throw new HttpRequestException("Synthetic lost answer"); }
                    if (RefusePatchDetail != null) { var detail = RefusePatchDetail; RefusePatchDetail = null; return Problem(422, detail); }
                    if (ConflictNextPatch) { ConflictNextPatch = false; _versions[id]++; row["title"] = "Changed on server"; row["item_limit"] = (row["item_limit"]!.GetValue<int>() == 35 ? 36 : 35); _order++; return Problem(412, "Rows changed."); }
                    if (tag != RowTag(id)) return Problem(412, "Rows changed.");
                    foreach (var pair in JsonNode.Parse(body)!.AsObject()) row[pair.Key] = pair.Value?.DeepClone();
                    _versions[id]++; _order++; return Json(row.ToJsonString(), RowTag(id));
                }
                if (request.Method == HttpMethod.Delete)
                {
                    if (FailNextDelete412) { FailNextDelete412 = false; _versions[id]++; return Problem(412, "Rows changed."); }
                    if (tag != RowTag(id)) return Problem(412, "Rows changed.");
                    Rows.Remove(id); foreach (var list in Orders.Values) list.Remove(id); _order++; return new(HttpStatusCode.NoContent);
                }
            }
            return Json(path switch
            {
                "/api/v2/admin/system/build" => """{"display":"synthetic","revision":"fixture","available":true}""",
                "/api/v2/admin/server/status" => """{"restart_required":false,"restart_mark_count":0,"restart_requested":false}""",
                "/api/v2/admin/downloads/preparations" => """{"counts":{"running":0,"queued":0,"retrying":0,"paused":0,"failed_recent":0},"items":[]}""",
                _ => """{"items":[],"page":{"has_more":false}}"""
            });
        }
        private static HttpResponseMessage Json(string body, string? tag = null, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (tag != null) response.Headers.ETag = EntityTagHeaderValue.Parse(tag); return response;
        }
        private HttpResponseMessage Json(string body) => Json(body, "\"order-" + _order + "\"");
        private static HttpResponseMessage Problem(int status, string detail, (string Code, string Detail)? field = null, string type = "problem") => new((HttpStatusCode)status)
        {
            Content = new StringContent(new JsonObject
            {
                ["type"] = "https://silo.invalid/docs/api/v2/problems/" + type, ["title"] = detail, ["status"] = status, ["detail"] = detail,
                ["errors"] = field == null ? new JsonArray() : new JsonArray(new JsonObject { ["location"] = "body.title", ["code"] = field.Value.Code, ["detail"] = field.Value.Detail })
            }.ToJsonString(), Encoding.UTF8, "application/problem+json")
        };
    }

    internal static async Task RunAsync(Window window)
    {
        using var server = new Server(); using var http = new HttpClient(server); var client = new SiloApiClient(http); client.SetBaseUrl("https://fixture.invalid");
        using var auth = new AuthService(client, new AuthApi(client)); auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
        auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        auth.SelectProfile("p1", null, new Profile { Id = "p1", Name = "Synthetic primary", IsPrimary = true });
        using var authority = new AdminMonitorAuthority(client, auth); var monitor = new AdminMonitorApi(client);
        using var store = new ServerActivityStore(monitor, authority, new Channel()); var navigation = new NavigationService();
        var services = new ServiceCollection().AddSingleton(client).AddSingleton(auth).AddSingleton(authority).AddSingleton(monitor).AddSingleton(store)
            .AddSingleton(navigation).AddSingleton(new ToastService()).AddSingleton(new SettingsService(Program.ResultDirectory)).AddAdminHomeSections();
        await using var provider = services.BuildServiceProvider();
        var global = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldServices = global.GetValue(null); var oldContent = window.Content; Frame? outer = null; AdminHomeSectionsPage? page = null;
        try
        {
            global.SetValue(null, provider); outer = new Frame(); window.Content = outer; navigation.Frame = outer;
            Check(AdminShellPage.CanOpen(new(AdminRoute.HomeSections)), "Home sections is registered by the production hook");
            Check(!AdminShellPage.CanOpen(new(AdminRoute.Collections)), "this hook does not register Collections");
            Check(AdminNavigation.SearchGroups("rails", AdminShellPage.CreateRegistry().IsRegistered).SelectMany(group => group.Pages).Any(entry => entry.Route == AdminRoute.HomeSections), "the registered page is reachable through the Admin navigation search");

            // ── Loading, then a failed read with a visible Reload ──
            var gate = new TaskCompletionSource(); server.GateOrder = gate; server.FailList = true;
            Check(AdminShellPage.Open(new(AdminRoute.HomeSections)), "the real shell opens Home sections");
            await Until(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content; var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminHomeSectionsPage); page = (AdminHomeSectionsPage)inner.Content;
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "in-place Frame has no slide transition");
            await Until(() => Texts(page).Contains("Loading rows…")); Check(true, "the loading state says the rows are loading");
            gate.SetResult();
            await Until(() => Find<Button>(page, "HomeSectionsReload") != null);
            Check(Texts(page).Any(text => text.Contains("Synthetic list failure")), "a failed read shows the server's own problem text");
            Check(page.Controller!.State.Snapshot == null && !page.Controller.CanWrite, "a failed read offers no writes");
            server.FailList = false; await Click(page, "HomeSectionsReload"); await Until(() => page.Controller!.CanWrite);
            Check(Find<Button>(page, "HomeSectionsReload") == null, "Reload clears the failed-read alert");

            // ── Populated ──
            await Until(() => Texts(page).Contains("Synthetic disabled")); Check(Texts(page).Contains("Synthetic first"), "the list shows enabled and disabled rows in server order");
            Check(Texts(page).Contains("3 rows · 2 on"), "the summary counts rows and rows that are on");
            Check(Texts(page).Any(text => text.StartsWith("Newest movies and episodes from all libraries · 20 titles", StringComparison.Ordinal)), "a row reads as the official one-line description");
            Check(Texts(page).Any(text => text.Contains("is off. Nobody sees this row.")), "an off row collapses to the official off text");
            await Until(() => Texts(page).Any(text => text.StartsWith("The Synthetic collection collection", StringComparison.Ordinal)), "the collection row names its collection");
            Check(Find<ComboBox>(page, "HomeSectionsPageSelector")!.Items.Count == 4, "the page switcher lists Home and each library");
            Check(Find<Button>(page, "HomeSectionsAdd")!.IsEnabled, "Add row is enabled once the page can change");
            foreach (var width in new[] { 1440, 1024, 800 }) { await Resize(window, width); await Capture(window, "home-sections-list-" + width + ".png"); }
            await Resize(window, 1280);

            // ── Quick switch: applied, refused, stale and unknown ──
            await Switch(page, "Synthetic disabled", server); await Until(() => server.Requests.Any(item => item.Method == "PATCH" && item.Path.EndsWith("/b")) && page.Controller!.CanWrite);
            var enable = server.Requests.Last(item => item.Method == "PATCH"); Check(enable.Body == "{\"enabled\":true}" && enable.Tag == "\"b-1\"", "turning a row on sends only enabled, over the version just read");
            Check(Texts(page).Contains("3 rows · 3 on"), "the summary follows the saved value");
            server.RefusePatchDetail = "Cannot change this row right now."; var patches = server.Requests.Count(item => item.Method == "PATCH");
            await Switch(page, "Synthetic first", server); await Until(() => Texts(page).Any(text => text.Contains("Cannot change this row right now.")));
            Check(page.Controller!.CanWrite && !page.Controller.State.ReviewRequired, "a definite refusal keeps the page writable");
            await Until(() => Tree(page).OfType<ToggleSwitch>().Any(item => AutomationProperties.GetName(item) == "Synthetic first is on for everyone" && item.IsOn && item.IsLoaded), "switch back on"); Check(true, "the switch follows the saved value after a refusal");
            Check(server.Requests.Count(item => item.Method == "PATCH") == patches + 1, "the refused change was sent once");
            server.ConflictNextPatch = true; await Switch(page, "Synthetic first", server);
            await Until(() => page.Controller!.State.Failure == AdminHomeSectionsFailure.Conflict);
            Check(Texts(page).Any(text => text.StartsWith("These rows changed since you opened this page.", StringComparison.Ordinal)), "a stale version shows the official conflict banner");
            Check(!page.Controller.CanWrite && Find<Button>(page, "HomeSectionsReload") != null, "a stale version blocks writes until Reload");
            await Click(page, "HomeSectionsReload"); await Until(() => page.Controller!.CanWrite && Texts(page).Contains("Changed on server"));
            server.LoseNextPatch = true; patches = server.Requests.Count(item => item.Method == "PATCH");
            await Switch(page, "Synthetic disabled", server); await Until(() => page.Controller!.State.OutcomeUnknown);
            Check(!page.Controller.CanWrite && server.Requests.Count(item => item.Method == "PATCH") == patches + 1, "an unknown outcome is never replayed and blocks writes");
            Check(Texts(page).Any(text => text.StartsWith("The outcome is unknown.", StringComparison.Ordinal)), "the unknown outcome is explained");
            await Click(page, "HomeSectionsReload"); await Until(() => page.Controller!.CanWrite);

            // ── Row menu: hero and ordering ──
            await Menu(page, "Synthetic collection row", "Move to top"); await Until(() => server.Requests.Any(item => item.Method == "PUT" && item.Path.StartsWith("/api/v2/admin/sections/order")) && page.Controller!.CanWrite);
            var reorder = server.Requests.Last(item => item.Method == "PUT" && item.Path.StartsWith("/api/v2/admin/sections/order"));
            Check(reorder.Tag != null && JsonNode.Parse(reorder.Body)!["ordered_ids"]!.AsArray().Select(node => node!.GetValue<string>()).SequenceEqual(new[] { "c", "a", "b" }), "move to top submits the complete order over the reviewed version");
            await Menu(page, "Changed on server", "Use as hero banner"); await Until(() => server.Requests.Last().Method == "GET" && server.Requests.Any(item => item.Body == "{\"featured\":true}") && page.Controller!.CanWrite);
            Check(Texts(page).Contains("Hero banner"), "the hero row shows its tag");

            // ── Select mode and a batch with one refused row ──
            await PageMenu(page, "Select rows"); await Until(() => Find<CheckBox>(page, "HomeSectionsSelectAll") != null);
            Check(Tree(page).OfType<CheckBox>().Count(box => AutomationProperties.GetName(box).StartsWith("Select ", StringComparison.Ordinal)) >= 3, "select mode shows a labelled checkbox on each row");
            foreach (var title in new[] { "Changed on server", "Synthetic disabled" }) Toggle(Tree(page).OfType<CheckBox>().First(box => AutomationProperties.GetName(box) == "Select " + title));
            await Until(() => Texts(page).Contains("2 selected"));
            server.RefusePatchDetail = null; var before = server.Requests.Count;
            await Click(page, "HomeSectionsSelectTurnOff"); await Until(() => page.Controller!.CanWrite && server.Requests.Skip(before).Count(item => item.Method == "PATCH") == 2);
            Check(server.Requests.Skip(before).Where(item => item.Method == "PATCH").All(item => item.Body == "{\"enabled\":false}"), "turning rows off sends only enabled for each row");
            await Click(page, "HomeSectionsSelectDone"); await Until(() => Find<CheckBox>(page, "HomeSectionsSelectAll") == null);

            // ── Delete: stale then confirmed ──
            await Menu(page, "Synthetic collection row", "Delete row…"); var deleteDialog = await DialogTexts(window, "Delete Synthetic collection row?");
            Check(deleteDialog.Any(text => text.StartsWith("The row goes away from Home for everyone.", StringComparison.Ordinal)) && deleteDialog.Contains("This can't be undone."), "the delete dialog says what goes and that it cannot be undone");
            server.FailNextDelete412 = true; await Press(window, "Delete row", closes: false);
            await Until(() => OpenDialogTexts(window).Contains("This row changed since you opened it. Reload it before deleting."));
            Check(server.Requests.Count(item => item.Method == "DELETE") == 1, "a stale delete is sent once and keeps the dialog");
            await Press(window, "Reload row", closes: false); await Until(() => OpenDialogHasButton(window, "Delete row"));
            await Press(window, "Delete row"); await Until(() => page.Controller!.CanWrite && !Texts(page).Contains("Synthetic collection row"));
            Check(server.Requests.Count(item => item.Method == "DELETE") == 2, "the reviewed delete is sent once more only after the explicit reload");

            // ── Restore: toggles, per-page results ──
            await PageMenu(page, "Restore defaults…"); var restore = await DialogTexts(window, "Restore Home to the default rows?");
            Check(restore.Any(text => text.Contains("replaced by Silo's default rows")) && restore.Contains("Resetting profiles isn't available on this server.") == false, "the restore dialog names what is replaced");
            ToggleByName(window, "Also restore every library page"); ToggleByName(window, "Also reset every profile's Home and library pages");
            await Until(() => OpenDialogTexts(window).Contains("Restore Home and every library page to the default rows?"));
            server.RefuseDefaultsFor2 = true; before = server.Requests.Count; await Press(window, "Restore defaults");
            await Until(() => page.Controller!.CanWrite && server.Requests.Skip(before).Count(item => item.Method == "PUT" && item.Path.StartsWith("/api/v2/admin/sections/defaults")) == 4);
            Check(server.Requests.Skip(before).Where(item => item.Path.StartsWith("/api/v2/admin/sections/defaults")).All(item => item.Body == "{\"reset_profiles\":true}"), "every restore sends reset_profiles and its own page version");
            server.RefuseDefaultsFor2 = false;

            // ── Add row: pick, preview, refused, unknown ──
            await Click(page, "HomeSectionsAdd"); await Until(() => Find<Button>(page, "HomeSectionKind_recently_added") != null);
            Check(Find<Button>(page, "HomeSectionKind_next_up") == null, "kinds no longer offered for new rows are not in the picker");
            Check(Find<Button>(page, "HomeSectionKind_trending_on_server") != null && Find<Button>(page, "HomeSectionKind_collection") != null, "the picker lists the catalog's kinds");
            await Click(page, "HomeSectionKind_recently_added"); await Until(() => Find<TextBox>(page, "HomeSectionTitle") != null);
            await Until(() => Texts(page).Contains("Synthetic preview title") || Tree(page).OfType<TextBlock>().Any(text => TextOf(text).Contains("titles match") || TextOf(text).Contains("title matches")));
            await SetTitle(page, "Synthetic new row"); await Capture(window, "home-sections-add-editor-800.png");
            server.RefuseNextCreate = true; await Click(page, "HomeSectionSave");
            await Until(() => Texts(page).Any(text => text.Contains("That row name is not allowed.")));
            Check(page.Controller!.CanWrite && Find<Button>(page, "HomeSectionSave")!.IsEnabled, "a refused create shows the server's detail and lets the admin correct and retry");
            await SetTitle(page, "Synthetic new row 2"); await Click(page, "HomeSectionSave"); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null && page.Controller!.CanWrite);
            var create = server.Requests.Last(item => item.Method == "POST" && item.Path == "/api/v2/admin/sections"); var created = JsonNode.Parse(create.Body)!;
            Check(created["scope"]!.GetValue<string>() == "home" && created["library_id"] == null && created["title"]!.GetValue<string>() == "Synthetic new row 2" && created["position"]!.GetValue<int>() == 2, "create sends the home scope, the name and the bottom position");
            Check(server.Requests.Count(item => item.Method == "POST" && item.Path == "/api/v2/admin/sections") == 2, "the refused create and the corrected one are separate writes");

            await Click(page, "HomeSectionsAdd"); await Until(() => Find<Button>(page, "HomeSectionKind_recently_added") != null);
            await Click(page, "HomeSectionKind_recently_added"); await Until(() => Find<TextBox>(page, "HomeSectionTitle") != null);
            await SetTitle(page, "Unknown applied row"); server.LoseNextCreate = true; var posts = server.Requests.Count(item => item.Method == "POST");
            await Click(page, "HomeSectionSave"); await Until(() => page.Controller!.State.OutcomeUnknown);
            await Until(() => Find<Button>(page, "HomeSectionSave") is { IsEnabled: false }, "save disabled");
            Check(server.Requests.Count(item => item.Method == "POST") == posts + 1 && !Find<Button>(page, "HomeSectionSave")!.IsEnabled, "a lost create answer is never replayed and blocks a duplicate");
            await Click(page, "HomeSectionReviewUnknown"); await Dialog(window, "Discard"); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null && page.Controller!.CanWrite);
            Check(Texts(page).Contains("Unknown applied row"), "reloading after an unknown create shows the row the server kept");

            // ── Edit row: stale save, then merge ──
            await Menu(page, "Changed on server", "Edit row…"); await Until(() => Find<TextBox>(page, "HomeSectionTitle") != null);
            Check(Texts(page).Any(text => text.StartsWith("Changes apply to everyone on Home.", StringComparison.Ordinal)), "Edit row says who the change reaches");
            await SetTitle(page, "Edited first"); server.ConflictNextPatch = true;
            await Click(page, "HomeSectionSave"); await Until(() => Find<Button>(page, "HomeSectionReloadRow") != null);
            Check(Texts(page).Contains("This row changed since you opened it. Your changes are kept. Reload the row to see what changed, then save again."), "a stale save keeps the draft and explains it");
            await Capture(window, "home-sections-edit-conflict.png");
            await Click(page, "HomeSectionReloadRow"); await Until(() => Texts(page).Any(text => text.StartsWith("Changed elsewhere:", StringComparison.Ordinal)) && page.Controller!.CanWrite);
            Check(Find<TextBox>(page, "HomeSectionTitle")!.Text == "Edited first", "reloading keeps the fields the admin changed");
            await Click(page, "HomeSectionSave"); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null && page.Controller!.CanWrite);
            var saved = JsonNode.Parse(server.Requests.Last(item => item.Method == "PATCH" && item.Body.Contains("Edited first")).Body)!;
            Check(saved["title"]!.GetValue<string>() == "Edited first" && saved["config"]!["future_option"]!["keep"]!.GetValue<bool>() && saved["scope"] == null && !saved["enabled"]!.GetValue<bool>() && saved["web_only"] == null, "saving sends the saved values (including the row staying off), keeps unknown config and sends no scope or web_only");

            // ── Next Up defaults ──
            Check(Tree(page).OfType<ToggleSwitch>().Any(item => AutomationProperties.GetAutomationId(item) == "HomeSectionsNewEpisodesFirst"), "the Next Up defaults card shows its controls");
            server.RefuseNextSetting = true; Toggle(Find<ToggleSwitch>(page, "HomeSectionsNewEpisodesFirst")!);
            await Until(() => Texts(page).Any(text => text.Contains("Settings request failed.")), "refused default message"); Check(!Find<ToggleSwitch>(page, "HomeSectionsNewEpisodesFirst")!.IsOn, "a refused default leaves the control on its saved value");
            Toggle(Find<ToggleSwitch>(page, "HomeSectionsNewEpisodesFirst")!); await Until(() => server.NewFirst == "true" && Find<ToggleSwitch>(page, "HomeSectionsNewEpisodesFirst")?.IsEnabled == true);
            Check(server.Requests.Last(item => item.Method == "PUT" && item.Path == "/api/v2/admin/settings").Body.Contains("\"defaults.ui.next_up_new_releases_first\":\"true\""), "a default saves as soon as it changes, over the settings version");

            // ── A library page: empty state, add-to-pages, copies ──
            var pages = Find<ComboBox>(page, "HomeSectionsPageSelector")!;
            pages.SelectedItem = pages.Items.OfType<ComboBoxItem>().First(item => item.Tag is AdminHomeSectionScope { LibraryId: "2" });
            await Until(() => page.Controller!.CanWrite && page.Controller.State.Snapshot?.Scope.LibraryId == "2");
            await Until(() => Texts(page).Contains("No rows on Synthetic second movies yet.") && Texts(page).Contains("These rows show above the full Synthetic second movies grid."), "empty library page");
            Check(Texts(page).Contains("No rows on Synthetic second movies yet."), "an empty page says it has no rows"); Check(Texts(page).Contains("These rows show above the full Synthetic second movies grid."), "a library page notes where its rows show");
            pages.SelectedItem = pages.Items.OfType<ComboBoxItem>().First(item => item.Tag is AdminHomeSectionScope { LibraryId: "1" });
            await Until(() => page.Controller!.CanWrite && page.Controller.State.Snapshot?.Scope.LibraryId == "1" && Texts(page).Contains("Synthetic library row"));
            Check(server.Requests.Any(item => item.Path == "/api/v2/admin/sections?scope=library&library_id=1"), "the library page reads its exact scope");
            await Menu(page, "Synthetic library row", "Add to other libraries…"); await DialogTexts(window, "Add Synthetic library row to other libraries");
            ToggleByName(window, "Synthetic second movies"); Check(Tree(PopupRoot(window)).OfType<CheckBox>().Any(box => box.Content?.ToString() == "Synthetic Movies · already here" && !box.IsEnabled), "the page the row is on shows as already here");
            await Press(window, "Add to 1 page"); await Until(() => page.Controller!.CanWrite && server.Requests.Any(item => item.Path == "/api/v2/admin/sections/bulk"));
            var copy = JsonNode.Parse(server.Requests.Last(item => item.Path == "/api/v2/admin/sections/bulk").Body)!;
            Check(copy["library_ids"]!.AsArray().Select(node => node!.GetValue<string>()).SequenceEqual(new[] { "2" }) && !copy["featured"]!.GetValue<bool>() && copy["web_only"] == null, "copying a row is one bulk write of a non-hero copy");
            await Click(page, "HomeSectionsAdd"); await Until(() => Find<Button>(page, "HomeSectionKind_recently_added") != null);
            await Click(page, "HomeSectionKind_recently_added"); await Until(() => Find<ListView>(page, "HomeSectionExtraCopies") != null);
            await SetTitle(page, "Synthetic library bulk"); var copies = Find<ListView>(page, "HomeSectionExtraCopies")!;
            copies.SelectedItems.Add(copies.Items.OfType<AdminHomeSectionChoice>().First(item => item.Id == "2"));
            await Click(page, "HomeSectionSave"); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null && page.Controller!.CanWrite);
            var bulk = JsonNode.Parse(server.Requests.Last(item => item.Path == "/api/v2/admin/sections/bulk").Body)!;
            Check(bulk["library_ids"]!.AsArray().Select(node => node!.GetValue<string>()).SequenceEqual(new[] { "1", "2" }) && bulk["title"]!.GetValue<string>() == "Synthetic library bulk", "Add row with other pages is one bulk write including this page");

            // ── The Collections link contract ──
            Check(AdminShellPage.Open(new(AdminRoute.HomeSections, new Dictionary<string, string> { ["page"] = "1", ["add"] = "collection:library:c1", ["return"] = "/admin/collections/c1" })), "an Add as a row link opens the page");
            await Until(() => inner.Content is AdminHomeSectionsPage next && !ReferenceEquals(next, page) && Find<TextBox>(next, "HomeSectionTitle") != null);
            page = (AdminHomeSectionsPage)inner.Content; Check(page.Controller!.State.Snapshot?.Scope.LibraryId == "1", "?page= opens the library page");
            Check(Find<TextBox>(page, "HomeSectionTitle")!.Text == "Synthetic collection", "Add as a row opens Add row on the collection, named after it");
            Check(Find<Button>(page, "HomeSectionBack")!.Content?.ToString() != "Back to Synthetic collection", "the return link is honoured only where the collection page can open");
            await Click(page, "HomeSectionSave"); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null && page.Controller!.CanWrite);
            var linked = JsonNode.Parse(server.Requests.Last(item => item.Method == "POST" && item.Path == "/api/v2/admin/sections").Body)!;
            Check(linked["scope"]!.GetValue<string>() == "library" && linked["library_id"]!.GetValue<string>() == "1" && linked["section_type"]!.GetValue<string>() == "collection" && linked["config"]!["library_collection_id"]!.GetValue<string>() == "c1", "the linked collection is added to the linked page as a collection row");
            Check(AdminShellPage.Open(new(AdminRoute.HomeSections, new Dictionary<string, string> { ["page"] = "1", ["edit"] = "la" })), "an Open row link opens the page");
            await Until(() => inner.Content is AdminHomeSectionsPage next && !ReferenceEquals(next, page) && Find<TextBox>(next, "HomeSectionTitle") != null);
            page = (AdminHomeSectionsPage)inner.Content; Check(Find<TextBox>(page, "HomeSectionTitle")!.Text == "Synthetic library row", "Open row opens Edit row on that row");
            await Click(page, "HomeSectionCancel"); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null);
            Check(AdminShellPage.Open(new(AdminRoute.HomeSections, new Dictionary<string, string> { ["page"] = "home", ["add"] = "collection:user:x" })), "an invalid link still opens the page");
            await Until(() => inner.Content is AdminHomeSectionsPage next && !ReferenceEquals(next, page) && next.Controller?.CanWrite == true); page = (AdminHomeSectionsPage)inner.Content;
            await Task.Delay(300); Check(Find<FrameworkElement>(page, "HomeSectionEditor") == null, "a personal collection link opens nothing on the admin page");
            Check(AdminShellPage.Open(new(AdminRoute.HomeSections, new Dictionary<string, string> { ["page"] = "home", ["edit"] = "missing-row" })), "a link to a missing row still opens the page");
            await Until(() => inner.Content is AdminHomeSectionsPage next && !ReferenceEquals(next, page) && next.Controller?.CanWrite == true); page = (AdminHomeSectionsPage)inner.Content;
            await Task.Delay(300); Check(Find<FrameworkElement>(page, "HomeSectionEditor") == null, "a link to a missing row opens nothing");

            // ── Authority retirement ──
            await Click(page, "HomeSectionsAdd"); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") != null);
            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" }); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null);
            Check(page.Controller?.State.Snapshot == null, "authority retirement clears rows and closes the editor");
            page.SuspendAdminPage(); outer.Navigate(typeof(Page)); await Until(() => !page.IsLoaded && VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)window.Content).XamlRoot).Count == 0);
        }
        finally
        {
            page?.SuspendAdminPage();
            if (outer != null) { outer.Navigate(typeof(Page)); await Until(() => page?.IsLoaded != true); }
            if (window.Content is FrameworkElement root && root.XamlRoot != null) await Until(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Count == 0);
            window.Content = oldContent; global.SetValue(null, oldServices);
        }
        Program.Log("PASS: Home sections native workflow fixture completed against synthetic data only.");
    }

    private static void Check(bool condition, string message) { Program.Log((condition ? "PASS: " : "FAIL: ") + message); if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> condition, string? what = null)
    { for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(25); if (!condition()) throw new TimeoutException("Home sections fixture state did not settle" + (what == null ? "." : ": " + what)); }
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static T? Find<T>(DependencyObject root, string id) where T : FrameworkElement => Tree(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
    private static string TextOf(TextBlock text) => text.Inlines.Count > 0 ? string.Concat(text.Inlines.OfType<Microsoft.UI.Xaml.Documents.Run>().Select(run => run.Text)) : text.Text;
    private static HashSet<string> Texts(DependencyObject root) => Tree(root).OfType<TextBlock>().Select(TextOf).ToHashSet();
    private static void Invoke(Button? button)
    { Check(button is { IsEnabled: true, IsLoaded: true }, "invoked control is enabled and loaded"); ((IInvokeProvider)new ButtonAutomationPeer(button!).GetPattern(PatternInterface.Invoke)!).Invoke(); }
    // TextChanged is raised after the assignment returns; wait for the page to see it before the next action.
    private static async Task SetTitle(DependencyObject root, string text)
    { Find<TextBox>(root, "HomeSectionTitle")!.Text = text; await Task.Delay(250); }
    private static async Task Click(DependencyObject root, string id)
    {
        await Until(() => Find<Button>(root, id) is { IsEnabled: true, IsLoaded: true }, "button " + id);
        Invoke(Find<Button>(root, id));
    }
    private static void Toggle(ToggleSwitch toggle) { Check(toggle is { IsEnabled: true, IsLoaded: true }, "toggled switch is enabled and loaded"); ((IToggleProvider)new ToggleSwitchAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)!).Toggle(); }
    private static void Toggle(CheckBox box) { Check(box is { IsEnabled: true, IsLoaded: true }, "toggled checkbox is enabled and loaded"); ((IToggleProvider)new CheckBoxAutomationPeer(box).GetPattern(PatternInterface.Toggle)!).Toggle(); }
    private static DependencyObject PopupRoot(Window window) => OpenPopupChildren(window).FirstOrDefault() ?? (DependencyObject)window.Content;
    private static IEnumerable<FrameworkElement> OpenPopupChildren(Window window) =>
        VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)window.Content).XamlRoot).Select(popup => popup.Child).OfType<FrameworkElement>();
    private static HashSet<string> OpenDialogTexts(Window window) => OpenPopupChildren(window).SelectMany(child => Tree(child)).OfType<TextBlock>().Select(TextOf).ToHashSet();
    private static bool OpenDialogHasButton(Window window, string label) => OpenPopupChildren(window).SelectMany(child => Tree(child)).OfType<Button>().Any(button => button.Content?.ToString() == label && button.IsEnabled);
    private static async Task<HashSet<string>> DialogTexts(Window window, string title)
    {
        await Until(() => OpenDialogTexts(window).Contains(title), "dialog " + title);
        return OpenDialogTexts(window);
    }
    private static void ToggleByName(Window window, string name)
    {
        var target = OpenPopupChildren(window).SelectMany(child => Tree(child)).OfType<FrameworkElement>().First(element => AutomationProperties.GetName(element) == name || (element as CheckBox)?.Content?.ToString() == name);
        if (target is ToggleSwitch toggle) Toggle(toggle); else if (target is CheckBox box) Toggle(box); else throw new InvalidOperationException("No toggle named " + name);
    }
    private static async Task Press(Window window, string primary, bool closes = true)
    {
        Button? button = null;
        await Until(() => (button = OpenPopupChildren(window).SelectMany(child => Tree(child)).OfType<Button>().FirstOrDefault(item => item.Content?.ToString() == primary && item.IsEnabled)) != null, "dialog button " + primary);
        Invoke(button);
        if (closes) await Until(() => !OpenPopupChildren(window).SelectMany(child => Tree(child)).OfType<ContentDialog>().Any());
        // The dialog task completes after its closing animation; give the page time to observe it before the next dialog.
        if (closes) await Task.Delay(600);
    }
    private static async Task Dialog(Window window, string primary) => await Press(window, primary);
    private static async Task Switch(AdminHomeSectionsPage page, string row, Server server)
    {
        ToggleSwitch? label = null;
        await Until(() => (label = Tree(page).OfType<ToggleSwitch>().FirstOrDefault(item => AutomationProperties.GetName(item).StartsWith(row + " is ", StringComparison.Ordinal) && item.IsLoaded && item.IsEnabled)) != null, "switch " + row);
        Toggle(label!); await Task.Yield();
    }
    private static async Task Menu(AdminHomeSectionsPage page, string row, string command)
    {
        Button? button = null;
        await Until(() => (button = Tree(page).OfType<Button>().FirstOrDefault(item => AutomationProperties.GetName(item) == "More for " + row && item.IsEnabled && item.IsLoaded)) != null, "row menu " + row);
        Invoke(button);
        await PickMenuItem(page, command);
    }
    private static async Task PageMenu(AdminHomeSectionsPage page, string command) { await Click(page, "HomeSectionsMore"); await PickMenuItem(page, command); }
    private static async Task PickMenuItem(AdminHomeSectionsPage page, string command)
    {
        MenuFlyoutItem? item = null;
        await Until(() => (item = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(popup => Tree(popup.Child)).OfType<MenuFlyoutItem>().FirstOrDefault(entry => entry.Text == command && entry.IsEnabled)) != null, "menu item " + command);
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item!).GetPattern(PatternInterface.Invoke)!).Invoke();
    }
    private static async Task Resize(Window window, int width)
    {
        await Until(() => window.Content is FrameworkElement { XamlRoot: not null });
        var scale = ((FrameworkElement)window.Content).XamlRoot.RasterizationScale;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
        await Until(() => Math.Abs(((FrameworkElement)window.Content).ActualWidth - width) < 2);
    }
    private static async Task Capture(Window window, string name)
    {
        var root = (FrameworkElement)window.Content; root.UpdateLayout(); await Task.Delay(100);
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(root); Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "capture rendered " + name);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
}
