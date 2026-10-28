using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class RequestDiscoverLayoutBehaviorTests
{
    [Fact]
    public async Task DiscoveryRetainsExplicitLayoutIncludingBrandAndUnknownRows()
    {
        using var wire = new Wire("""
        {"row_order":[{"key":"genres","type":"genres","title":"Choose a genre"},
          {"key":"r_abc123","type":"tmdb_list","title":"Our picks"},
          {"key":"future","type":"future_source","title":"Future"}],
         "items":[{"key":"r_abc123","title":"Our picks","page":1,"total_pages":1,"total_results":5,"results":[]}]}
        """);
        var response = await new RequestsApi(wire.Client).GetDiscoveryAsync();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response));
        Assert.True(json.RootElement.TryGetProperty("RowOrder", out var rows), "The viewer API discarded row_order.");
        Assert.Equal(new[] { "genres", "r_abc123", "future" }, rows.EnumerateArray().Select(row => row.GetProperty("Key").GetString()));
        Assert.Single(response.Sections);
    }

    [Fact]
    public async Task AdvertisedLayoutPagesOpaqueRowsWithoutRetryingDeletedRowsOnLegacyRoute()
    {
        using var wire = new Wire("{\"requests_enabled\":true,\"discover_rows_supported\":true}", "{\"title\":\"Picks\",\"results\":[]}", "{}");
        var api = new RequestsApi(wire.Client);
        await api.GetStatusAsync();
        await api.GetDiscoverySectionAsync("r_opaque_42", 3);
        Assert.Equal("/api/v2/requests/discover/rows/r_opaque_42?page=3", wire.Paths[1]);
        wire.Status = HttpStatusCode.NotFound;
        await Assert.ThrowsAsync<ApiException>(() => api.GetDiscoverySectionAsync("r_deleted", 1));
        Assert.Equal(3, wire.Paths.Count);
    }

    [Theory]
    [InlineData(null, 1, 4, 2)]
    [InlineData(null, 4, 4, null)]
    [InlineData(null, 499, 1000, 500)]
    [InlineData(null, 500, 1000, null)]
    [InlineData(4, 1, 5, 4)]
    [InlineData(6, 1, 5, null)]
    [InlineData(1, 1, 5, null)]
    [InlineData(0, 1, 5, null)]
    [InlineData(-1, 1, 5, null)]
    public void ContinuationDistinguishesAbsentCursorFromInvalidOrFinishedCursor(int? cursor, int page, int pages, int? expected)
        => Assert.Equal(expected, RequestDiscoveryPaging.Next(new() { NextPage = cursor, TotalPages = pages }, page));

    [Fact]
    public async Task LinkedViewerModelUsesOnlyConfiguredRowsInServerOrderAndLoadsOnlyVisibleBrands()
    {
        using var wire = new LayoutWire("""
        {"row_order":[{"key":"genres","type":"genres","title":"Pick a genre"},
        {"key":"r_picks","type":"tmdb_list","title":"Our list"},
        {"key":"future","type":"future_source","title":"Future"},
        {"key":"r_empty","type":"tmdb_chart","title":"Empty"},
        {"key":"networks","type":"networks","title":"Channels"}],
        "items":[{"key":"r_picks","title":"Our list","total_pages":1,"total_results":8,"results":[{"tmdb_id":1}]},
        {"key":"r_empty","results":[]},{"key":"hidden","results":[{"tmdb_id":2}]},
        {"key":"future","results":[{"tmdb_id":3}]}]}
        """);
        var vm = new RequestsViewModel(new RequestsApi(wire.Client));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.DoesNotContain("/api/v2/requests/discover/studios", wire.Paths);
        Assert.Contains("/api/v2/requests/discover/genres", wire.Paths);
        Assert.Equal(new[] { "genres", "r_picks", "networks" }, vm.DiscoveryRows.Select(row => row.Key));
        Assert.Equal(new[] { "r_picks" }, vm.DiscoverySections.Select(section => section.Key));
    }

    [Theory]
    [InlineData("{\"row_order\":[],\"items\":[]}", 0)]
    [InlineData("{\"items\":[]}", 3)]
    public async Task ExplicitEmptyLayoutDoesNotResurrectBrandsWhileOlderServerKeepsDefaultBrands(string json, int brands)
    {
        using var wire = new LayoutWire(json);
        var vm = new RequestsViewModel(new RequestsApi(wire.Client));
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Equal(brands, wire.Paths.Count(path => path.EndsWith("/studios") || path.EndsWith("/networks") || path.EndsWith("/genres")));
    }

    [Fact]
    public async Task CachedViewerModelReloadsForANewServerInsteadOfReturningPreviousLayout()
    {
        using var wire = new LayoutWire("{\"row_order\":[],\"items\":[]}");
        var vm = new RequestsViewModel(new RequestsApi(wire.Client));
        await vm.LoadCommand.ExecuteAsync(null);
        var reads = wire.Paths.Count;
        wire.Client.SetBaseUrl("https://different-viewer.invalid");
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(wire.Paths.Count > reads, "The cached Requests model returned the previous server's state.");
    }

    [Fact]
    public void ExploreAllUsesUnfilteredSourceTotalsAndKeepsTheTrimmedRail()
    {
        var section = new RequestDiscoverySection { TotalPages = 1, TotalResults = 8, Results = [new() { TmdbId = 1 }] };
        Assert.True(RequestDiscoveryLayout.HasExploreAll(section));
        Assert.Single(section.Results);
        section.TotalResults = 1; Assert.False(RequestDiscoveryLayout.HasExploreAll(section));
        section.TotalPages = 2; Assert.True(RequestDiscoveryLayout.HasExploreAll(section));
    }

    [Fact]
    public async Task OlderServerKeepsBuiltInRouteAndChangingOwnerCannotReuseNewRouteCapability()
    {
        using var wire = new Wire("{\"requests_enabled\":true,\"discover_rows_supported\":true}", "{\"results\":[]}");
        var api = new RequestsApi(wire.Client); await api.GetStatusAsync();
        wire.Client.SetBaseUrl("https://older-server.invalid");
        await api.GetDiscoverySectionAsync("trending_movies", 2);
        Assert.Equal("/api/v2/requests/discover/trending_movies?page=2", wire.Paths.Last());
    }

    private sealed class LayoutWire : HttpMessageHandler
    {
        private readonly string _layout;
        public SiloApiClient Client { get; }
        public List<string> Paths { get; } = [];
        public LayoutWire(string layout)
        { _layout = layout; Client = new(new HttpClient(this)); Client.SetBaseUrl("https://requests.invalid"); }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath; Paths.Add(path);
            var json = path.EndsWith("/status") ? $"{{\"requests_enabled\":true,\"discover_rows_supported\":{(_layout.Contains("row_order") ? "true" : "false")}}}" : path.EndsWith("/discover") ? _layout : "{\"items\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        private readonly Queue<string> _replies;
        public SiloApiClient Client { get; }
        public List<string> Paths { get; } = [];
        public HttpStatusCode Status = HttpStatusCode.OK;
        public Wire(params string[] replies)
        {
            _replies = new(replies); Client = new(new HttpClient(this)); Client.SetBaseUrl("https://requests.invalid");
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(_replies.Dequeue()) });
        }
    }
}
