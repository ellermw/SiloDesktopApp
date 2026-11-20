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

// AUTHORED, NOT RUN: source-only worker. Exercises real registration/shell/Frame and modal lifetimes.
internal static class AdminCollectionsNativeFixture
{
    private static void Check(bool value, string text)
    {
        Program.Log((value ? "PASS: " : "FAIL: ") + text);
        if (!value) throw new InvalidOperationException(text);
    }
    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 240 && !condition(); i++) await Task.Delay(25);
        if (!condition()) throw new InvalidOperationException("Collections fixture prerequisite timed out.");
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
        public ConcurrentQueue<string> Requests { get; } = new();
        public readonly Dictionary<string, JsonObject> Collections = new()
        {
            ["c1"] = JsonNode.Parse("""{"id":"c1","title":"Fixture manual","collection_type":"manual","library_id":"1","library_ids":["1"],"visibility":"visible","item_count":1,"group_id":null,"sort_order":0,"row_count":1,"description":"Synthetic only","query_definition":{},"sort_config":{},"source_config":{}}""")!.AsObject(),
            ["c2"] = JsonNode.Parse("""{"id":"c2","title":"Fixture smart","collection_type":"smart","library_id":"1","library_ids":["1"],"visibility":"hidden","item_count":2,"group_id":"g1","sort_order":0,"description":"","query_definition":{"match":"all","groups":[{"match":"all","rules":[{"field":"year","op":"gte","value":2000}]}]},"sort_config":{},"source_config":{}}""")!.AsObject()
        };
        public JsonObject Row = JsonNode.Parse("""{"id":"s1","title":"Shared fixture row","section_type":"collection_posters","config":{"keep":"yes","collections":[{"library_collection_id":"c1"},{"library_collection_id":"other"}]}}""")!.AsObject();
        private int _revision = 1, _created;
        public bool FailList;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; var query = request.RequestUri.Query;
            Requests.Enqueue(request.Method + " " + request.RequestUri.PathAndQuery);
            var body = request.Content == null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject();
            var mutating = request.Method != HttpMethod.Get;
            var guarded = request.Method == HttpMethod.Patch || request.Method == HttpMethod.Delete && !path.Contains("/items/") || path.EndsWith("/order") && mutating;
            if (guarded && request.Headers.IfMatch.SingleOrDefault()?.ToString() != $"\"r{_revision}\"") return Reply(new() { ["type"] = "conflict", ["status"] = 412 }, HttpStatusCode.PreconditionFailed);
            if (path == "/api/v2/libraries") return Reply(JsonNode.Parse("""{"items":[{"id":"1","name":"Fixture movies","type":"movies","paths":["/synthetic"],"enabled":true}],"page":{"has_more":false}}""")!.AsObject());
            if (path == "/api/v2/admin/collections/capabilities") return Reply(JsonNode.Parse("""{"state":"available","groups":true,"imports":true,"import_sources":["mdblist","tmdb","tmdb_list"],"artwork":true,"item_reorder":true,"section_references":true,"template_summaries":true}""")!.AsObject());
            if (path == "/api/v2/admin/collections")
            {
                if (request.Method == HttpMethod.Post)
                {
                    var id = "new" + ++_created; var item = (JsonObject)body.DeepClone(); item["id"] = id; item["library_id"] = "1"; Collections[id] = item; _revision++; return Reply(item, HttpStatusCode.Created);
                }
                if (FailList) return Reply(new(), HttpStatusCode.ServiceUnavailable);
                return Reply(new() { ["items"] = new JsonArray(Collections.Values.Select(v => v.DeepClone()).ToArray()), ["groups"] = new JsonArray(), ["page"] = new JsonObject { ["has_more"] = false } });
            }
            if (path == "/api/v2/admin/libraries/1/collection-groups") return Reply(JsonNode.Parse("""{"items":[{"id":"g1","library_id":"1","name":"Fixture shelf","kind":"regular","default_sort_mode":"manual","sort_order":0}]}""")!.AsObject());
            if (path.EndsWith("collection-groups/order")) return Reply(JsonNode.Parse("""{"ordered_ids":["g1","ungrouped"],"has_more":false}""")!.AsObject());
            if (path.Contains("/collection-groups/") && path.EndsWith("/collections/order"))
            {
                var group = path.Split('/')[5];
                if (mutating)
                {
                    foreach (var id in body["ordered_ids"]!.AsArray().Select(v => v!.ToString())) Collections[id]["group_id"] = group == "ungrouped" ? null : group;
                    _revision++;
                }
                return Reply(new() { ["ordered_ids"] = new JsonArray(Collections.Where(p => (p.Value["group_id"]?.ToString() ?? "ungrouped") == group).Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray()), ["has_more"] = false });
            }
            if (path == "/api/v2/admin/collections/preview") return Reply(JsonNode.Parse("""{"items":[{"content_id":"m1","title":"Preview fixture","type":"movie"}],"total":1}""")!.AsObject());
            if (path == "/api/v2/catalog") return Reply(JsonNode.Parse("""{"items":[{"content_id":"m1","title":"Fixture title","type":"movie"}],"page":{"has_more":false}}""")!.AsObject());
            if (path.EndsWith("/sections") && path.StartsWith("/api/v2/admin/collections/")) return Reply(JsonNode.Parse("""{"items":[{"id":"s1","title":"Shared fixture row","scope":"home","enabled":true}]}""")!.AsObject());
            if (path == "/api/v2/admin/sections/s1")
            {
                if (mutating) { Row["config"] = body["config"]!.DeepClone(); _revision++; }
                return Reply(Row);
            }
            if (path.StartsWith("/api/v2/admin/collections/"))
            {
                var parts = path.Split('/'); var id = parts[5];
                if (parts.Length > 6)
                {
                    if (path.EndsWith("/items/order")) return Reply(JsonNode.Parse("""{"ordered_ids":["m1"],"has_more":false}""")!.AsObject());
                    if (path.EndsWith("/items")) return Reply(JsonNode.Parse("""{"items":[{"collection_id":"c1","media_item_id":"m1","title":"Fixture title","position":0}],"page":{"has_more":false}}""")!.AsObject());
                    return new(HttpStatusCode.NoContent);
                }
                if (request.Method == HttpMethod.Delete) { Collections.Remove(id); _revision++; return new(HttpStatusCode.NoContent); }
                if (Collections.TryGetValue(id, out var item))
                {
                    if (request.Method == HttpMethod.Patch) { foreach (var pair in body) item[pair.Key] = pair.Value?.DeepClone(); _revision++; }
                    return Reply(item);
                }
            }
            return Reply(path switch
            {
                "/api/v2/admin/system/build" => JsonNode.Parse("""{"display":"fixture","available":true}""")!.AsObject(),
                "/api/v2/admin/server/status" => new() { ["restart_required"] = false },
                _ => new() { ["items"] = new JsonArray(), ["page"] = new JsonObject { ["has_more"] = false } }
            });
        }
        private HttpResponseMessage Reply(JsonObject body, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("ETag", $"\"r{_revision}\""); return response;
        }
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
            Check(AdminShellPage.Open(new(AdminRoute.Collections)), "Collections opens the real admin shell");
            await Until(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content; var inner = (Frame)Field(shell, "_frame")!;
            await Until(() => inner.Content is AdminCollectionsPage p && p.Controller.List != null && Find<Button>(p, "CollectionEdit_c1") != null);
            page = (AdminCollectionsPage)inner.Content;
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "Collections swaps without entrance transitions");
            Check(!server.Requests.Any(r => r.Contains("/catalog?")), "opening Collections does not walk a media catalog");
            await CaptureAsync(shell, "admin-collections-list-1440.png");
            var view = Find<ComboBox>(page, "CollectionsView")!; view.SelectedItem = view.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "arrange");
            await Until(() => Find<Button>(page, "CollectionMove_c1") != null);
            Check(Find<Button>(page, "CollectionHandle_c1")?.CanDrag == true, "Arrange wires a real native drag handle");
            await Click(Find<Button>(page, "CollectionsViewerPreview"));
            Check(Find<Button>(page, "CollectionBoardEdit_c2") == null, "viewer preview omits hidden collections");
            await Click(Find<Button>(page, "CollectionsViewerPreview"));
            await Click(Find<Button>(page, "CollectionMove_c1")); await Until(() => Dialog(page) != null);
            var moving = Dialog(page)!; var destination = Find<ComboBox>(moving, "CollectionMoveShelf")!;
            destination.SelectedItem = destination.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == "g1"); moving.Hide();
            await Until(() => Dialog(page) == null);
            Check(server.Collections["c1"]["group_id"] == null, "canceled Move sends no change");
            await CaptureAsync(shell, "admin-collections-arrange-1440.png");
            Check(AdminShellPage.Open(AdminRouteRequest.CollectionEditor("c2")), "official editor descendant opens");
            await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<Button>(p, "CollectionPreview") != null);
            page = (AdminCollectionsPage)inner.Content;
            await Click(Find<Button>(page, "CollectionPreview")); await Until(() => Texts(page).Contains("Preview fixture"));
            Check(Texts(page).Any(t => t.Contains("Preview shows unsaved changes")), "smart editor shows bounded unsaved preview");
            await CaptureAsync(shell, "admin-collections-smart-editor-1440.png");
            Check(AdminShellPage.Open(AdminRouteRequest.CollectionNew().WithQuery("libraryId", "1").WithQuery("type", "manual").WithQuery("view", "list")), "manual create descendant opens");
            await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<TextBox>(p, "CollectionName") != null);
            page = (AdminCollectionsPage)inner.Content; Find<TextBox>(page, "CollectionName")!.Text = "Created fixture";
            Check(page.RequiresLeaveConfirmation && shell.RequiresLeaveConfirmation, "editor draft reaches shell departure guard");
            await Click(Find<Button>(page, "CollectionEditorSave"));
            await Until(() => shell.CurrentRequest.Segment(0) == "new1" && Find<Button>(page, "CollectionEditorSave")?.IsEnabled == true);
            Check(server.Collections.ContainsKey("new1"), "real create writes a usable collection and replaces /new in place");
            await Click(Find<Button>(page, "CollectionAddTitles")); await Until(() => Dialog(page) != null);
            var picker = Dialog(page)!; Find<TextBox>(picker, "CollectionTitleSearch")!.Text = "Fixture";
            await Click(Find<Button>(picker, "CollectionTitlesSearchButton")); await Until(() => Find<CheckBox>(picker, "CollectionTitlePick_m1") != null);
            Check(Find<CheckBox>(picker, "CollectionTitlePick_m1") != null, "bounded title search renders a native pick"); picker.Hide(); await Until(() => Dialog(page) == null);
            await Click(Find<Button>(page, "CollectionEditorBack"));
            await Until(() => inner.Content is AdminCollectionsPage p && p != page && Find<Button>(p, "CollectionDelete_c1") != null);
            page = (AdminCollectionsPage)inner.Content;
            await Click(Find<Button>(page, "CollectionDelete_c1")); await Until(() => Dialog(page) != null);
            var deletion = Dialog(page)!; Check(!deletion.IsPrimaryButtonEnabled, "used collection requires explicit row-removal consent");
            Check(Texts(deletion).Any(t => t.Contains("Shared fixture row")), "delete shows actual rows before confirmation");
            await CaptureAsync(deletion, "admin-collections-delete-dialog.png"); deletion.Hide(); await Until(() => Dialog(page) == null);
            Check(server.Collections.ContainsKey("c1"), "cancel delete preserves the collection");
            await Click(Find<Button>(page, "CollectionDelete_c1")); await Until(() => Dialog(page) != null);
            deletion = Dialog(page)!;
            var remove = Find<CheckBox>(deletion, "CollectionDeleteRows")!;
            (FrameworkElementAutomationPeer.CreatePeerForElement(remove) as IToggleProvider ?? throw new InvalidOperationException("Checkbox has no toggle peer")).Toggle();
            await Until(() => deletion.IsPrimaryButtonEnabled);
            await Click(Tree(deletion).OfType<Button>().First(b => b.Content?.ToString() == "Delete collection"));
            await Until(() => Dialog(page) == null && !server.Collections.ContainsKey("c1"));
            Check(server.Row["config"]!["collections"]!.AsArray().Count == 1 && server.Row["config"]!["collections"]![0]!["library_collection_id"]!.ToString() == "other", "native confirmed delete preserves the other collection in a shared row");
            Check(server.Row["config"]!["keep"]!.ToString() == "yes", "native row removal preserves unrelated config");
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 600)); await Task.Delay(100);
            await CaptureAsync(shell, "admin-collections-list-800.png");
            auth.SetCurrentUser(new UserInfo { Id = "1", Role = "user", Username = "synthetic" });
            await Until(() => page.Controller.List == null && Find<Button>(page, "CollectionsAuthorityReload") != null);
            Check(Find<Button>(page, "CollectionEdit_c2") == null, "authority retirement removes private data and commands");
        }
        finally
        {
            // Close and await dialogs and the nested page before restoring process globals.
            if (page != null) { Dialog(page)?.Hide(); page.SuspendAdminPage(); }
            outer?.Navigate(typeof(Page));
            if (page != null) await Until(() => !page.IsLoaded && Dialog(page) == null);
            window.Content = previousContent; field.SetValue(null, previousServices);
        }
        Program.Log("PASS: Collections authored native fixture completed; synthetic data/current DPI only.");
    }
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static ContentDialog? Dialog(AdminCollectionsPage page) => Field(page, "_dialog") as ContentDialog;
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static T? Find<T>(DependencyObject root, string id) where T : DependencyObject => Tree(root).OfType<T>().FirstOrDefault(v => AutomationProperties.GetAutomationId(v) == id);
    private static string[] Texts(DependencyObject root) => Tree(root).OfType<TextBlock>().Select(t => t.Text).ToArray();
    private static async Task Click(Button? button)
    {
        Check(button is { IsLoaded: true, IsEnabled: true }, "native command is rendered and enabled"); button!.StartBringIntoView();
        (FrameworkElementAutomationPeer.CreatePeerForElement(button) as IInvokeProvider ?? throw new InvalidOperationException("Button has no invoke peer")).Invoke(); await Task.Delay(50);
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
