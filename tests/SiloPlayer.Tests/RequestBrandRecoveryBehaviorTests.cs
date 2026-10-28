using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class RequestBrandRecoveryBehaviorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TitleDiscoveryFailurePreservesOnlyKnownLegacyBrandRows(bool configuredLayout)
    {
        using var wire = new Wire { FailGenre = false, FailDiscovery = true, ConfiguredLayout = configuredLayout };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://request-brands.invalid");
        var vm = new RequestsViewModel(new RequestsApi(client));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotNull(vm.DiscoveryError);
        Assert.Empty(vm.DiscoverySections);
        Assert.Equal(configuredLayout ? [] : new[] { "studios", "networks", "genres" }, vm.DiscoveryRows.Select(row => row.Key));
        if (configuredLayout)
        {
            Assert.Empty(vm.Studios); Assert.Empty(vm.Networks); Assert.Empty(vm.Genres);
            Assert.DoesNotContain(wire.Reads, path => path.EndsWith("/studios") || path.EndsWith("/networks") || path.EndsWith("/genres"));
        }
        else
        {
            Assert.Single(vm.Studios); Assert.Single(vm.Networks); Assert.Single(vm.Genres);
            await vm.RetryDiscoveryAsync();
            Assert.Equal(new[] { "studios", "networks", "genres" }, vm.DiscoveryRows.Select(row => row.Key));
            Assert.Single(vm.Genres);
        }
    }

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
        public bool FailDiscovery;
        public bool ConfiguredLayout;
        public List<string> Reads = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; Reads.Add(path);
            if (path.EndsWith("/genres") && FailGenre || path.EndsWith("/discover") && FailDiscovery)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
            var json = path.EndsWith("/status") ? $"{{\"requests_enabled\":true,\"discover_rows_supported\":{ConfiguredLayout.ToString().ToLowerInvariant()}}}" : path.EndsWith("/studios") || path.EndsWith("/networks") || path.EndsWith("/genres")
                ? "{\"items\":[{\"slug\":\"fixture\",\"display_name\":\"Fixture brand\"}]}" : "{\"items\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
