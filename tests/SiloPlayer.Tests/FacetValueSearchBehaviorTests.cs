using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class FacetValueSearchBehaviorTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FacetSearchUsesAdvertisedWireShapeAndLibraryScope(bool ranked)
    {
        using var wire = new Wire(ranked); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://facet-rules.invalid");
        var values = await new CatalogApi(client).SearchFacetAsync("studio", "Param", 12, "series");
        Assert.Equal(new[] { "Paramount" }, values);
        Assert.Contains("limit=50", wire.FacetRequest!.Query);
        Assert.Contains("type=series", wire.FacetRequest.Query);
        Assert.Equal(ranked, wire.FacetRequest.Query.Contains("library_ids=12"));
        Assert.DoesNotContain("library_id=", wire.FacetRequest.Query);
    }

    [Fact]
    public async Task OlderServerDoesNotReceiveEmptyFacetSearch()
    {
        using var wire = new Wire(false); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://facet-rules.invalid");
        Assert.Empty(await new CatalogApi(client).SearchFacetAsync("genre", "", 12, "all"));
        Assert.Null(wire.FacetRequest);
    }

    private sealed class Wire(bool ranked) : HttpMessageHandler
    {
        internal Uri? FacetRequest;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = "{}";
            if (request.RequestUri!.AbsolutePath.EndsWith("/capabilities")) json = ranked ? "{\"facet_value_search\":true}" : "{}";
            if (request.RequestUri.AbsolutePath.EndsWith("/filters/search"))
            {
                FacetRequest = request.RequestUri;
                json = ranked ? "{\"values\":[{\"value\":\"Paramount\",\"count\":8}],\"values_has_more\":false}" : "{\"matches\":[\"Paramount\"],\"has_more\":false}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
