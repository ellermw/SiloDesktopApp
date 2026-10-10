using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CollectionPageLoadingBehaviorTests
{
    [Fact]
    public async Task FailedServerRefreshRetainsPreviouslyLoadedCards()
    {
        using var wire = new Wire(); var client = wire.Client();
        var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client), requestClient: client);
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.Single(vm.ServerLibraries);
        wire.FailServer = true;
        await vm.LoadCollectionsCommand.ExecuteAsync(null);
        Assert.Single(vm.ServerLibraries);
        Assert.False(vm.IsLoadingServerCollections);
    }

    [Fact]
    public async Task LateServerResponseFromPreviousProfileCannotPopulateCurrentPage()
    {
        using var wire = new Wire { HoldServer = true }; var client = wire.Client(); client.SetProfile("old");
        var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client), requestClient: client);
        var load = vm.LoadCollectionsCommand.ExecuteAsync(null);
        await wire.ServerStarted.Task;
        client.SetProfile("current");
        wire.Server.SetResult(Wire.Success(Wire.ServerBody));
        await load;
        Assert.Empty(vm.ServerLibraries);
    }

    private sealed class Wire : HttpMessageHandler
    {
        public const string ServerBody = "{\"libraries\":[{\"library_id\":1,\"library_name\":\"Movies\",\"total_count\":1,\"collections\":[{\"id\":\"one\",\"title\":\"One\"}]}]}";
        public bool FailServer, HoldServer;
        public TaskCompletionSource ServerStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Server { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SiloApiClient Client() { var result = new SiloApiClient(new HttpClient(this)); result.SetBaseUrl("https://collection-page.invalid"); return result; }
        public static HttpResponseMessage Success(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/server"))
            {
                ServerStarted.TrySetResult();
                if (HoldServer) return Server.Task;
                if (FailServer) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"unavailable\"}") });
                return Task.FromResult(Success(ServerBody));
            }
            return Task.FromResult(Success("{\"items\":[],\"groups\":[],\"import_sources\":[],\"item_reorder\":true}"));
        }
    }
}
