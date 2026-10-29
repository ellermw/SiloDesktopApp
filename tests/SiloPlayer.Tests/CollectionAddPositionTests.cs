using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class CollectionAddPositionTests
{
    [Fact]
    public async Task AddAtAKnownPositionMakesOneRequestAndKeepsTheOrderBaselineCurrent()
    {
        var server = new CollectionServer(("a", 0), ("b", 5));
        var api = new CollectionsApi(server.Client());
        await api.GetCollectionItemsAsync("c1"); // The editor's load remembers the order baseline.
        server.Requests.Clear();

        await api.AddCollectionItemAtAsync("c1", "x", 6);

        Assert.Equal(["PUT /api/v2/collections/c1/items/x"], server.Requests);
        Assert.Equal(6, server.LastPosition);
        // The baseline now matches the server's [a, b, x]; a stale one would refuse the reorder (412).
        await api.ReorderCollectionItemsAsync("c1", ["x", "a", "b"]);
        Assert.Equal(["x", "a", "b"], server.Order);
    }

    [Fact]
    public async Task AddAtAKnownPositionWithoutABaselineStillReorders()
    {
        var server = new CollectionServer();
        var api = new CollectionsApi(server.Client());

        await api.AddCollectionItemAtAsync("c1", "x", 0);
        await api.AddCollectionItemAtAsync("c1", "y", 1);
        await api.ReorderCollectionItemsAsync("c1", ["y", "x"]);

        Assert.DoesNotContain("GET /api/v2/collections/c1/items", server.Requests);
        Assert.Equal(["y", "x"], server.Order);
    }

    [Fact]
    public void EditorAddsDeriveTheirPositionFromLoadedItems()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "src", "SiloPlayer", "App.xaml.cs"))) root = root.Parent;
        var source = File.ReadAllText(Path.Combine(root!.FullName, "src", "SiloPlayer", "ViewModels", "CollectionEditorViewModel.cs"));
        var add = source[source.IndexOf("private async Task AddManualItemAsync(", StringComparison.Ordinal)..];
        add = add[..add.IndexOf("[RelayCommand]", StringComparison.Ordinal)];
        Assert.Contains("ManualItems.Max(existing => existing.Position) + 1", add, StringComparison.Ordinal);
        Assert.Contains("AddCollectionItemAtAsync(CollectionId, item.ContentId, position)", add, StringComparison.Ordinal);
        Assert.Contains("Position = position", add, StringComparison.Ordinal);
        Assert.DoesNotContain("AddCollectionItemAsync(", add, StringComparison.Ordinal);
    }

    private sealed class CollectionServer : HttpMessageHandler
    {
        private readonly List<(string Id, int Position)> _items;
        private int _revision = 1;
        public List<string> Requests { get; } = [];
        public int LastPosition { get; private set; } = -1;
        public string[] Order => [.. _items.OrderBy(i => i.Position).ThenBy(i => i.Id, StringComparer.Ordinal).Select(i => i.Id)];

        public CollectionServer(params (string, int)[] items) => _items = [.. items];

        public SiloApiClient Client()
        {
            var client = new SiloApiClient(new HttpClient(this));
            client.SetBaseUrl("https://collections.invalid");
            return client;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            if (request.Method == HttpMethod.Get && path == "/api/v2/collections/c1/items")
                return Json(new { items = _items.Select(i => new { media_item_id = i.Id, position = i.Position }), page = new { has_more = false } });
            if (request.Method == HttpMethod.Get && path == "/api/v2/collections/c1/items/order")
            {
                var response = Json(new { ordered_ids = Order, has_more = false });
                response.Headers.ETag = new($"\"r{_revision}\"");
                return response;
            }
            if (request.Method == HttpMethod.Put && path == "/api/v2/collections/c1/items/order")
            {
                if (request.Headers.IfMatch.SingleOrDefault()?.Tag != $"\"r{_revision}\"") return new(HttpStatusCode.PreconditionFailed);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var ids = body.RootElement.GetProperty("ordered_ids").EnumerateArray().Select(v => v.GetString()!).ToList();
                for (var i = 0; i < _items.Count; i++) _items[i] = (_items[i].Id, ids.IndexOf(_items[i].Id));
                _revision++;
                return Json(new { ordered_ids = Order });
            }
            if (request.Method == HttpMethod.Put && path.StartsWith("/api/v2/collections/c1/items/", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                LastPosition = body.RootElement.GetProperty("position").GetInt32();
                _items.Add((path[(path.LastIndexOf('/') + 1)..], LastPosition));
                _revision++;
                return new(HttpStatusCode.NoContent);
            }
            return new(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
    }
}
