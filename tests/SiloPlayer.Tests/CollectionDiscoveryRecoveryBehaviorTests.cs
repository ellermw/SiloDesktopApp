using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CollectionDiscoveryRecoveryBehaviorTests
{
    [Fact]
    public async Task OlderTopResponseCannotReplaceNewerSearch()
    {
        using var wire = new Wire(); var client = wire.Client(); var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client));
        var old = vm.LoadTopMDBListAsync(); var current = vm.SearchMDBListAsync("current");
        wire.Search.SetResult(Wire.Success("Current")); await current;
        wire.Top.SetResult(Wire.Success("Old top")); await old;
        Assert.Equal("Current", Assert.Single(vm.MdblistResults).Name);
    }
    [Fact]
    public async Task OlderTopFailureCannotClearNewerSearchPendingOrPublishError()
    {
        using var wire = new Wire(); var client = wire.Client(); var vm = new CollectionsViewModel(new CollectionsApi(client), new CatalogApi(client));
        var old = vm.LoadTopMDBListAsync(); var current = vm.SearchMDBListAsync("current");
        wire.Top.SetResult(new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"old top unavailable\"}") }); await old;
        Assert.True(vm.IsSearchingMdblist); Assert.Null(vm.TemplateErrorMessage);
        wire.Search.SetResult(Wire.Success("Current")); await current;
    }
    private sealed class Wire : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage> Top { get; } = new(); public TaskCompletionSource<HttpResponseMessage> Search { get; } = new();
        public SiloApiClient Client() { var client = new SiloApiClient(new HttpClient(this)); client.SetBaseUrl("https://collection-discovery.invalid"); return client; }
        public static HttpResponseMessage Success(string title) => new(HttpStatusCode.OK) { Content = new StringContent($"{{\"configured\":true,\"items\":[{{\"id\":1,\"name\":\"{title}\"}}]}}") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => request.RequestUri!.AbsolutePath.EndsWith("/top") ? Top.Task : Search.Task;
    }
}
