using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class RequestBrandRecoveryBehaviorTests
{
    [Fact]
    public async Task FailedGenreRailRetainsOtherDiscoveryAndRetriesOnlyItsEndpoint()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-brands.invalid");
        var vm = new RequestsViewModel(new RequestsApi(client));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.BrandErrors.ContainsKey("genre"));
        Assert.Single(vm.Studios); Assert.Single(vm.Networks);
        Assert.Null(vm.DiscoveryError); Assert.Null(vm.MineError);
        var reads = wire.Reads.ToArray(); wire.FailGenre = false;
        await vm.RetryBrandAsync("genre");
        Assert.False(vm.BrandErrors.ContainsKey("genre")); Assert.Single(vm.Genres);
        Assert.Equal(reads.Length + 1, wire.Reads.Count);
        Assert.Equal("/api/v2/requests/discover/genres", wire.Reads.Last());
    }

    private sealed class Wire : HttpMessageHandler
    {
        public bool FailGenre = true;
        public List<string> Reads = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; Reads.Add(path);
            if (path.EndsWith("/genres") && FailGenre)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
            var json = path.EndsWith("/status") ? "{\"requests_enabled\":true}" : path.EndsWith("/studios") || path.EndsWith("/networks") || path.EndsWith("/genres")
                ? "{\"items\":[{\"slug\":\"fixture\",\"display_name\":\"Fixture brand\"}]}" : "{\"items\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
