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
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Services;
using SiloPlayer.Views.Admin;
using Windows.Graphics.Imaging;
using Windows.Storage;

/// <summary>Authored, unrun source candidate. Real registration, shell and Frame; synthetic data only.</summary>
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
        private readonly Dictionary<string, JsonObject> _rows = new()
        {
            ["a"] = JsonNode.Parse("""{"id":"a","scope":"home","library_id":null,"position":0,"section_type":"recently_added","title":"Synthetic first","enabled":true,"featured":false,"item_limit":20,"web_only":false,"config":{"future_option":{"keep":true}}}""")!.AsObject(),
            ["b"] = JsonNode.Parse("""{"id":"b","scope":"home","library_id":null,"position":1,"section_type":"favorites","title":"Synthetic disabled","enabled":false,"featured":false,"item_limit":20,"web_only":false,"config":{}}""")!.AsObject(),
            ["la"] = JsonNode.Parse("""{"id":"la","scope":"library","library_id":"1","position":0,"section_type":"recently_added","title":"Synthetic library row","enabled":true,"featured":false,"item_limit":20,"web_only":false,"config":{}}""")!.AsObject()
        };
        private List<string> _order = ["a", "b"];
        private readonly Dictionary<string, List<string>> _libraryOrders = new() { ["1"] = ["la"], ["2"] = [] };
        private int _revision = 1, _created;
        internal bool ConflictNext, LoseNextCreate;
        private string Tag => "\"sections-" + _revision + "\"";
        private static readonly string[] Defaults = ["defaults.ui.next_up_mode", "defaults.ui.next_up_new_releases_first"];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            var tag = request.Headers.IfMatch.FirstOrDefault()?.ToString();
            var libraryScope = request.RequestUri.Query.Contains("scope=library", StringComparison.Ordinal);
            var libraryId = request.RequestUri.Query.Contains("library_id=2", StringComparison.Ordinal) ? "2" : "1";
            var order = libraryScope ? _libraryOrders[libraryId] : _order;
            Requests.Add((request.Method.Method, request.RequestUri.PathAndQuery, tag, body));
            if (path == "/api/v2/admin/sections/capabilities") return Json("""{"available":true,"preview":true,"reset_profiles":true,"web_only":true}""");
            if (path == "/api/v2/sections/recipes") return Json("""{"categories":[{"category":"library_staples","recipes":[{"type":"recently_added","category":"library_staples","presets":[{"key":"recent","display_name":"Recently added","description_short":"The latest titles in your library.","default_params":{}}]},{"type":"collection","category":"custom","presets":[{"key":"collection","display_name":"A collection","description_short":"Titles from a collection.","default_params":{}}]}]}]}""");
            if (path == "/api/v2/admin/sections/preview") return Json("""{"items":[{"content_id":"movie:synthetic","title":"Synthetic preview title"}],"total_count":1}""");
            if (path == "/api/v2/admin/collections") return Json("""{"items":[{"id":"c1","title":"Synthetic collection","visibility":"visible"}],"groups":[]}""");
            if (path == "/api/v2/libraries") return Json("""{"items":[{"id":"1","name":"Synthetic Movies","type":"movie"},{"id":"2","name":"Synthetic second movies","type":"movie"}]}""");
            if (path == "/api/v2/admin/settings/effective") return Json("""{"defaults.ui.next_up_mode":"\"combined\"","defaults.ui.next_up_new_releases_first":"false"}""");
            if (path == "/api/v2/admin/settings" && request.Method == HttpMethod.Put)
                return Json("""{"values":{"defaults.ui.next_up_mode":"\"separate\"","defaults.ui.next_up_new_releases_first":"false"},"restart_required":false}""");
            if (path == "/api/v2/admin/sections/order")
            {
                if (request.Method == HttpMethod.Put)
                {
                    if (tag != Tag) return Conflict();
                    var ids = JsonNode.Parse(body)!["ordered_ids"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
                    if (ids.Count != order.Count || ids.Distinct().Count() != ids.Count || ids.Any(id => !order.Contains(id))) return Conflict();
                    order.Clear(); order.AddRange(ids); _revision++;
                }
                return Json(JsonSerializer.Serialize(new { scope = libraryScope ? "library" : "home", library_id = libraryScope ? libraryId : null, ordered_ids = order }));
            }
            if (path == "/api/v2/admin/sections/defaults")
            {
                if (tag != Tag) return Conflict(); _revision++;
                return Json(JsonSerializer.Serialize(new { scope = libraryScope ? "library" : "home", library_id = libraryScope ? libraryId : null, ordered_ids = order }));
            }
            if (path == "/api/v2/admin/sections/bulk")
            {
                var payload = JsonNode.Parse(body)!.AsObject(); var ids = payload["library_ids"]!.AsArray();
                foreach (var node in ids)
                {
                    var id = "copy" + ++_created; var row = payload.DeepClone().AsObject(); row.Remove("library_ids");
                    var target = node!.GetValue<string>(); row["id"] = id; row["library_id"] = target; row["position"] = _libraryOrders[target].Count;
                    _rows[id] = row; _libraryOrders[target].Add(id);
                }
                _revision++; return Json("{\"created\":" + ids.Count + "}");
            }
            if (path == "/api/v2/admin/sections")
            {
                if (request.Method == HttpMethod.Post)
                {
                    var row = JsonNode.Parse(body)!.AsObject(); var id = "new" + ++_created;
                    row["id"] = id; if (row["scope"]?.ToString() == "home") { row["library_id"] = null; _order.Add(id); } else _libraryOrders[row["library_id"]!.GetValue<string>()].Add(id);
                    _rows[id] = row; _revision++;
                    if (LoseNextCreate) { LoseNextCreate = false; throw new HttpRequestException("Synthetic lost answer after apply"); }
                    return Json(row.ToJsonString());
                }
                return Json("{\"items\":[" + string.Join(",", order.Select(id => _rows[id].ToJsonString())) + "]}");
            }
            if (path.StartsWith("/api/v2/admin/sections/", StringComparison.Ordinal))
            {
                var id = path.Split('/').Last(); if (!_rows.TryGetValue(id, out var row)) return new(HttpStatusCode.NotFound);
                if (request.Method == HttpMethod.Get) return Json(row.ToJsonString());
                if (ConflictNext) { ConflictNext = false; _revision++; row["title"] = "Changed on server"; return Conflict(); }
                if (tag != Tag) return Conflict();
                if (request.Method == HttpMethod.Delete) { _rows.Remove(id); _order.Remove(id); foreach (var list in _libraryOrders.Values) list.Remove(id); _revision++; return new(HttpStatusCode.NoContent); }
                if (request.Method == HttpMethod.Patch)
                { foreach (var pair in JsonNode.Parse(body)!.AsObject()) row[pair.Key] = pair.Value?.DeepClone(); _revision++; return Json(row.ToJsonString()); }
            }
            return Json(path switch
            {
                "/api/v2/admin/system/build" => """{"display":"synthetic","revision":"fixture","available":true}""",
                "/api/v2/admin/server/status" => """{"restart_required":false,"restart_mark_count":0,"restart_requested":false}""",
                "/api/v2/admin/downloads/preparations" => """{"counts":{"running":0,"queued":0,"retrying":0,"paused":0,"failed_recent":0},"items":[]}""",
                _ => """{"items":[],"page":{"has_more":false}}"""
            });
        }
        private HttpResponseMessage Json(string body)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.ETag = EntityTagHeaderValue.Parse(Tag); return response;
        }
        private static HttpResponseMessage Conflict() => new(HttpStatusCode.PreconditionFailed)
        { Content = new StringContent("""{"type":"https://silo.invalid/problems/stale","title":"Rows changed","status":412}""", Encoding.UTF8, "application/problem+json") };
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
            Check(AdminShellPage.CanOpen(new(AdminRoute.HomeSections)), "Home Sections is registered by production hook");
            Check(!AdminShellPage.CanOpen(new(AdminRoute.Collections)), "this hook does not register Collections");
            Check(AdminShellPage.Open(new(AdminRoute.HomeSections)), "real shell opens Home Sections");
            await Until(() => outer.Content is AdminShellPage);
            var shell = (AdminShellPage)outer.Content;
            var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            await Until(() => inner.Content is AdminHomeSectionsPage { Controller.CanWrite: true }); page = (AdminHomeSectionsPage)inner.Content;
            Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "in-place Frame has no slide transition");
            await Until(() => Texts(page).Contains("Synthetic disabled")); Check(Texts(page).Contains("Synthetic first"), "official row list includes shown and disabled rows");
            foreach (var width in new[] { 1440, 1024, 800 })
            { await Resize(window, width); await Capture(window, "home-sections-list-" + width + ".png"); }

            Invoke(Find<Button>(page, "HomeSectionsAdd")); await Until(() => Find<Button>(page, "HomeSectionKind_recently_added") != null);
            Invoke(Find<Button>(page, "HomeSectionKind_recently_added")); await Until(() => Find<TextBox>(page, "HomeSectionTitle") != null);
            Find<TextBox>(page, "HomeSectionTitle")!.Text = "Synthetic new row";
            Invoke(Find<Button>(page, "HomeSectionPreview")); await Until(() => Texts(page).Contains("Synthetic preview title"));
            Check(server.Requests.Count(item => item.Path == "/api/v2/admin/sections/preview") == 1, "draft preview sends one bounded request");
            Invoke(Find<Button>(page, "HomeSectionSearchGo_libraries")); await Until(() => Find<Button>(page, "HomeSectionChoice_libraries_1") != null);
            Invoke(Find<Button>(page, "HomeSectionChoice_libraries_1"));
            await Capture(window, "home-sections-add-editor-800.png");
            Invoke(Find<Button>(page, "HomeSectionSave")); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null && page.Controller!.CanWrite);
            var create = server.Requests.Single(item => item.Method == "POST" && item.Path == "/api/v2/admin/sections");
            Check(JsonNode.Parse(create.Body)!["config"]!["filter_library_ids"]![0]!.GetValue<int>() == 1, "add source selector writes the actual library filter");

            await Menu(page, "Synthetic first", "Move down"); await Until(() => server.Requests.Any(item => item.Method == "PUT" && item.Path.StartsWith("/api/v2/admin/sections/order")) && page.Controller!.CanWrite);
            var reorder = server.Requests.Last(item => item.Method == "PUT" && item.Path.StartsWith("/api/v2/admin/sections/order"));
            Check(reorder.Tag != null && JsonNode.Parse(reorder.Body)!["ordered_ids"]!.AsArray().Count == 3, "reorder submits complete membership and a strong snapshot validator");
            await Menu(page, "Synthetic first", "Edit row…"); await Until(() => Find<TextBox>(page, "HomeSectionTitle") != null);
            Find<TextBox>(page, "HomeSectionTitle")!.Text = "Should conflict"; server.ConflictNext = true;
            Invoke(Find<Button>(page, "HomeSectionSave")); await Until(() => page.Controller!.State.ReviewRequired);
            Check(!Find<Button>(page, "HomeSectionSave")!.IsEnabled, "412 disables save until review");
            Check(server.Requests.Count(item => item.Method == "PATCH") == 1, "412 is not replayed");
            await Capture(window, "home-sections-edit-conflict.png");
            // Cancel the dirty editor through its real discard confirmation.
            Invoke(Find<Button>(page, "HomeSectionCancel")); await Dialog(window, "Discard");
            await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null);
            Invoke(Find<Button>(page, "HomeSectionsReload")); await Until(() => page.Controller!.CanWrite);

            await Menu(page, "Synthetic new row", "Delete row…"); await Dialog(window, "Delete");
            await Until(() => page.Controller!.CanWrite && !Texts(page).Contains("Synthetic new row"));
            Check(server.Requests.Count(item => item.Method == "DELETE") == 1, "confirmed delete is guarded and sent once");

            var pages = Find<ComboBox>(page, "HomeSectionsPageSelector")!;
            pages.SelectedItem = pages.Items.OfType<ComboBoxItem>().First(item => item.Tag is AdminHomeSectionScope { LibraryId: "1" });
            await Until(() => page.Controller!.CanWrite && page.Controller.State.Snapshot?.Scope.LibraryId == "1" && Texts(page).Contains("Synthetic library row"));
            Check(server.Requests.Any(item => item.Path == "/api/v2/admin/sections?scope=library&library_id=1"), "library-page tab reads its exact scope");
            Invoke(Find<Button>(page, "HomeSectionsAdd")); await Until(() => Find<Button>(page, "HomeSectionKind_recently_added") != null);
            Invoke(Find<Button>(page, "HomeSectionKind_recently_added")); await Until(() => Find<ListView>(page, "HomeSectionExtraCopies") != null);
            Find<TextBox>(page, "HomeSectionTitle")!.Text = "Synthetic library bulk";
            var copies = Find<ListView>(page, "HomeSectionExtraCopies")!; copies.SelectedItems.Add(copies.Items.OfType<AdminHomeSectionChoice>().First(item => item.Id == "2"));
            Invoke(Find<Button>(page, "HomeSectionSave")); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null && page.Controller!.CanWrite);
            var bulk = server.Requests.Single(item => item.Path == "/api/v2/admin/sections/bulk");
            Check(JsonNode.Parse(bulk.Body)!["library_ids"]!.AsArray().Count == 2 && !JsonNode.Parse(bulk.Body)!["featured"]!.GetValue<bool>(), "add-to-library pages is one bulk write with non-hero copies");
            pages.SelectedItem = pages.Items.OfType<ComboBoxItem>().First(item => item.Tag is AdminHomeSectionScope { Kind: "home" });
            await Until(() => page.Controller!.CanWrite && page.Controller.State.Snapshot?.Scope.Kind == "home");

            Invoke(Find<Button>(page, "HomeSectionsAdd")); await Until(() => Find<Button>(page, "HomeSectionKind_recently_added") != null);
            Invoke(Find<Button>(page, "HomeSectionKind_recently_added")); await Until(() => Find<TextBox>(page, "HomeSectionTitle") != null);
            Find<TextBox>(page, "HomeSectionTitle")!.Text = "Unknown applied row"; server.LoseNextCreate = true;
            Invoke(Find<Button>(page, "HomeSectionSave")); await Until(() => page.Controller!.State.OutcomeUnknown);
            Check(server.Requests.Count(item => item.Method == "POST" && item.Path == "/api/v2/admin/sections") == 2, "lost create answer is never replayed");
            Check(!Find<Button>(page, "HomeSectionSave")!.IsEnabled, "unknown outcome blocks duplicate creation");

            auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic-admin", Role = "user" }); await Until(() => Find<FrameworkElement>(page, "HomeSectionEditor") == null);
            Check(page.Controller?.State.Snapshot == null, "authority retirement clears rows and editor");
            page.SuspendAdminPage(); outer.Navigate(typeof(Page)); await Until(() => !page.IsLoaded && VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)window.Content).XamlRoot).Count == 0);
        }
        finally
        {
            page?.SuspendAdminPage();
            if (outer != null) { outer.Navigate(typeof(Page)); await Until(() => page?.IsLoaded != true); }
            if (window.Content is FrameworkElement root && root.XamlRoot != null)
                await Until(() => VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Count == 0);
            window.Content = oldContent; global.SetValue(null, oldServices);
        }
        Program.Log("PASS: Home Sections native workflow fixture completed against synthetic data only.");
    }
    private static void Check(bool condition, string message) { Program.Log((condition ? "PASS: " : "FAIL: ") + message); if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> condition)
    { for (var i = 0; i < 240 && !condition(); i++) await Task.Delay(25); if (!condition()) throw new TimeoutException("Home Sections fixture state did not settle."); }
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static T? Find<T>(DependencyObject root, string id) where T : FrameworkElement => Tree(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
    private static HashSet<string> Texts(DependencyObject root) => Tree(root).OfType<TextBlock>().Select(text => text.Text).ToHashSet();
    private static void Invoke(Button? button)
    { Check(button is { IsEnabled: true, IsLoaded: true }, "invoked control is enabled and loaded"); ((IInvokeProvider)new ButtonAutomationPeer(button!).GetPattern(PatternInterface.Invoke)!).Invoke(); }
    private static async Task Menu(AdminHomeSectionsPage page, string row, string command)
    {
        var button = Tree(page).OfType<Button>().First(item => AutomationProperties.GetName(item) == "Actions for " + row); Invoke(button);
        MenuFlyoutItem? item = null;
        await Until(() => (item = VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(popup => Tree(popup.Child)).OfType<MenuFlyoutItem>().FirstOrDefault(entry => entry.Text == command)) != null);
        ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item!).GetPattern(PatternInterface.Invoke)!).Invoke();
    }
    private static async Task Dialog(Window window, string primary)
    {
        Button? button = null; var root = (FrameworkElement)window.Content;
        await Until(() => (button = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany(popup => Tree(popup.Child)).OfType<Button>()
            .FirstOrDefault(item => item.Content?.ToString() == primary && item.IsEnabled)) != null);
        Invoke(button); await Until(() => !VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany(popup => Tree(popup.Child)).OfType<ContentDialog>().Any());
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
        var bitmap = new RenderTargetBitmap(); await bitmap.RenderAsync(root); Check(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0, "capture rendered " + name);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(Program.ResultDirectory));
        var file = await folder.CreateFileAsync(name, CreationCollisionOption.ReplaceExisting); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, (await bitmap.GetPixelsAsync()).ToArray()); await encoder.FlushAsync();
    }
}
