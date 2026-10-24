using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class Oct24ServerCollectionTests
{
    [Fact]
    public void LibraryInvalidationNotifiesCollectionSurface()
    {
        using var wire = new Wire(); var api = new CatalogApi(wire.Client());
        var notification = typeof(CatalogApi).GetEvent("LibraryCacheInvalidated"); Assert.NotNull(notification);
        var called = 0; Action listener = () => called++;
        notification.AddEventHandler(api, listener); api.InvalidateLibraryCache();
        Assert.Equal(1, called);
        notification.RemoveEventHandler(api, listener); api.InvalidateLibraryCache(); Assert.Equal(1, called);
    }
    private static async Task More(CollectionsViewModel vm, int id)
    {
        var method = typeof(CollectionsViewModel).GetMethod("LoadServerLibraryAsync"); Assert.NotNull(method);
        await (Task)method.Invoke(vm, new object[] { id, CancellationToken.None })!;
    }
    private static JsonElement Cards(CollectionsViewModel vm)
    {
        var method = typeof(CollectionsViewModel).GetMethod("GetServerCollectionCards"); Assert.NotNull(method);
        return JsonSerializer.SerializeToElement(method.Invoke(vm, new object?[] { null }));
    }

    [Fact]
    public async Task AllStartsWithEveryFirstPageAndLoadsRemainingLibraryCardsWithoutDuplicates()
    {
        using var wire = new Wire(); var client = wire.Client();
        var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client), requestClient: client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        await More(vm, 1); await More(vm, 2);
        var cards = Cards(vm).EnumerateArray().ToArray();
        Assert.Equal(45, cards.Length);
        Assert.Equal("shared", cards[0].GetProperty("Collection").GetProperty("Id").GetString());
        Assert.Equal(JsonValueKind.Null, cards[0].GetProperty("Library").ValueKind);
        Assert.Contains(cards, card => card.GetProperty("Collection").GetProperty("Id").GetString() == "two-24");
    }

    [Fact]
    public async Task FailedRefreshPreservesEarlierRestAndFirstPageWhileRetryRemainsPossible()
    {
        using var wire = new Wire(); var client = wire.Client();
        var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client), requestClient: client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null); await More(vm, 1);
        var count = Cards(vm).GetArrayLength(); wire.FailRest = true;
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.Equal(count, Cards(vm).GetArrayLength());
        await More(vm, 2); Assert.Equal(count, Cards(vm).GetArrayLength());
        wire.FailRest = false; await More(vm, 2); Assert.Equal(45, Cards(vm).GetArrayLength());
    }

    [Fact]
    public async Task LateLibraryRestCannotPopulateAnotherProfile()
    {
        Assert.NotNull(typeof(CollectionsViewModel).GetMethod("LoadServerLibraryAsync"));
        using var wire = new Wire(); var client = wire.Client();
        var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client), requestClient: client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null); wire.Hold = true;
        var pending = More(vm, 1); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        client.SetProfile("next"); wire.Release.SetResult(); await pending;
        wire.Hold = false; await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.Equal(35, Cards(vm).GetArrayLength());
    }

    [Fact]
    public async Task CanceledPageOwnerRetainsFirstPagesAndReloadCanRetryItsRest()
    {
        using var wire = new Wire(); var client = wire.Client();
        var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client), requestClient: client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null); wire.Hold = true;
        var pending = More(vm, 1); await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        vm.CancelServerCollectionLoads(); wire.Release.SetResult(); await pending;
        Assert.Equal(35, Cards(vm).GetArrayLength());
        wire.Hold = false; await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.Equal(40, Cards(vm).GetArrayLength());
    }

    private sealed class Wire : HttpMessageHandler
    {
        public bool FailRest, Hold;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://collections-oct24.fixture.invalid"); return client; }
        private static object[] Entries(string prefix, int count) => Enumerable.Range(0, count).Select(i => (object)new { id = i < 5 ? i == 0 ? "shared" : "shared-" + i : prefix + i, title = prefix + i, item_count = i }).ToArray();
        private static HttpResponseMessage Ok(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/server")) return Ok(new { libraries = new[] {
                new { library_id = 1, library_name = "One", total_count = 25, collections = Entries("one-", 20) },
                new { library_id = 2, library_name = "Two", total_count = 25, collections = Entries("two-", 20) } } });
            if (path.StartsWith("/api/v2/library/"))
            {
                if (Hold) { Started.TrySetResult(); await Release.Task; }
                if (FailRest) return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"unavailable\"}") };
                return Ok(new { collections = Entries(path.Contains("/1/") ? "one-" : "two-", 25) });
            }
            return Ok(new { items = Array.Empty<object>(), groups = Array.Empty<object>(), import_sources = Array.Empty<string>() });
        }
    }
}
