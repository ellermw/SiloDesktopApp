using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CollectionEditorPositionBehaviorTests
{
    [Fact]
    public async Task ExistingEditorAppendsAfterSparsePositionsWithoutReadingMembershipAgain()
    {
        using var server = new CollectionWire(("a", 0), ("b", 5));
        var client = server.Client();
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = ExistingEditor(client, auth);

        await vm.AddManualItemCommand.ExecuteAsync(Title("x"));

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(new[] { ("x", 6) }, server.PositionWrites);
        Assert.DoesNotContain("GET /api/v2/collections/c1/items", server.Requests);
        Assert.Equal(6, vm.ManualItems.Single(item => item.MediaItemId == "x").Position);
        Assert.Equal(new[] { "a", "b", "x" }, server.Order);
    }

    [Fact]
    public async Task RemovingAndUndoingAnAddedItemUsesItsActualServerPosition()
    {
        using var server = new CollectionWire(("a", 0), ("b", 5));
        var client = server.Client();
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = ExistingEditor(client, auth);
        await vm.AddManualItemCommand.ExecuteAsync(Title("x"));
        var added = vm.ManualItems.Single(item => item.MediaItemId == "x");

        await vm.RemoveManualItemCommand.ExecuteAsync(added);
        Assert.Null(vm.ErrorMessage);
        Assert.DoesNotContain(vm.ManualItems, item => item.MediaItemId == "x");
        Assert.Equal(6, vm.LastRemovedItem!.Position);
        await vm.UndoRemoveManualItemCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.LastRemovedItem);
        Assert.Equal(new[] { ("x", 6), ("x", 6) }, server.PositionWrites);
        Assert.Equal(new[] { "a", "b", "x" }, vm.ManualItems.Select(item => item.MediaItemId));
        Assert.Equal(vm.ManualItems.Select(item => item.MediaItemId), server.Order);
    }

    [Fact]
    public async Task NewManualCollectionSendsStagedOrderAtSuccessivePositionsWithoutPerItemReads()
    {
        using var server = new CollectionWire();
        var client = server.Client();
        using var auth = new AuthService(client, new AuthApi(client));
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth)
        { Name = "Position fixture", CollectionType = "manual" };
        foreach (var id in new[] { "a", "b", "x" })
            await vm.AddManualItemCommand.ExecuteAsync(Title(id));
        await vm.MoveManualItemAsync(2, 0);
        Assert.Empty(server.Requests);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.IsEditing);
        Assert.Equal("c1", vm.CollectionId);
        Assert.Equal(1, server.CreateCount);
        Assert.Equal(new[] { ("x", 0), ("a", 1), ("b", 2) }, server.PositionWrites);
        Assert.DoesNotContain("GET /api/v2/collections/c1/items", server.Requests);
        Assert.Equal(new[] { "x", "a", "b" }, server.Order);
    }

    private static MediaItem Title(string id) => new() { ContentId = id, Title = id, Type = "movie" };

    private static CollectionEditorViewModel ExistingEditor(SiloApiClient client, AuthService auth)
    {
        var vm = new CollectionEditorViewModel(new CollectionsApi(client), new CatalogApi(client), new SettingsApi(client), auth)
        { CollectionId = "c1", CollectionType = "manual", IsEditing = true };
        vm.ManualItems.Add(new CollectionItem { MediaItemId = "a", ContentId = "a", Position = 0 });
        vm.ManualItems.Add(new CollectionItem { MediaItemId = "b", ContentId = "b", Position = 5 });
        return vm;
    }

    private sealed class CollectionWire(params (string Id, int Position)[] initial) : HttpMessageHandler
    {
        private readonly List<(string Id, int Position)> _items = [.. initial];
        private int _revision = 1;
        public List<string> Requests { get; } = [];
        public List<(string Id, int Position)> PositionWrites { get; } = [];
        public int CreateCount { get; private set; }
        public string[] Order => [.. _items.OrderBy(item => item.Position).Select(item => item.Id)];

        public SiloApiClient Client()
        {
            var client = new SiloApiClient(new HttpClient(this));
            client.SetBaseUrl("https://editor-position.invalid");
            return client;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            if (request.Method == HttpMethod.Post && path == "/api/v2/collections")
            {
                CreateCount++;
                return Json(new { id = "c1", name = "Position fixture", collection_type = "manual" });
            }
            if (request.Method == HttpMethod.Get && path == "/api/v2/collections/c1/items")
                return Json(new { items = _items.Select(item => new { media_item_id = item.Id, position = item.Position }), page = new { has_more = false } });
            if (request.Method == HttpMethod.Get && path == "/api/v2/collections/c1/items/order")
                return Json(new { ordered_ids = Order, has_more = false });
            if (request.Method == HttpMethod.Put && path == "/api/v2/collections/c1/items/order")
            {
                if (request.Headers.IfMatch.SingleOrDefault()?.Tag != $"\"r{_revision}\"")
                    return new(HttpStatusCode.PreconditionFailed);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var order = body.RootElement.GetProperty("ordered_ids").EnumerateArray().Select(id => id.GetString()!).ToList();
                for (var index = 0; index < _items.Count; index++)
                    _items[index] = (_items[index].Id, order.IndexOf(_items[index].Id));
                _revision++;
                return Json(new { ordered_ids = Order });
            }
            if (path.StartsWith("/api/v2/collections/c1/items/", StringComparison.Ordinal))
            {
                var id = path[(path.LastIndexOf('/') + 1)..];
                if (request.Method == HttpMethod.Put)
                {
                    using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    var position = body.RootElement.GetProperty("position").GetInt32();
                    PositionWrites.Add((id, position));
                    _items.RemoveAll(item => item.Id == id);
                    _items.Add((id, position));
                    _revision++;
                    return new(HttpStatusCode.NoContent);
                }
                if (request.Method == HttpMethod.Delete)
                {
                    _items.RemoveAll(item => item.Id == id);
                    _revision++;
                    return new(HttpStatusCode.NoContent);
                }
            }
            return new(HttpStatusCode.NotFound);
        }

        private HttpResponseMessage Json(object body)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };
            response.Headers.ETag = new($"\"r{_revision}\"");
            return response;
        }
    }
}
