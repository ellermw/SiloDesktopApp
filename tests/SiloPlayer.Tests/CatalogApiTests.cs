using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class CatalogApiTests
{
    [Fact]
    public async Task GetCatalogAsync_SendsPersonalSourceAndPaginationSnapshot()
    {
        var handler = new CaptureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        await new CatalogApi(client).GetCatalogAsync(
            null,
            limit: 60,
            offset: 120,
            snapshot: "2026-07-11T12:34:56Z",
            source: "history");

        var query = Uri.UnescapeDataString(handler.LastUri!.Query);
        Assert.Contains("source=history", query);
        Assert.Contains("limit=60", query);
        Assert.Contains("offset=120", query);
        Assert.Contains("snapshot=2026-07-11T12:34:56Z", query);
        Assert.DoesNotContain("sort=", query);
        Assert.DoesNotContain("order=", query);
    }

    [Fact]
    public async Task GetCatalogAsync_UsesQueryDefinitionRulesForTechnicalFacets()
    {
        var handler = new CaptureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        await new CatalogApi(client).GetCatalogAsync(
            null,
            country: "United States",
            resolution: "2160p");

        var query = Uri.UnescapeDataString(handler.LastUri!.Query);
        Assert.Contains("groups[0][match]=all", query);
        Assert.Contains("groups[0][rules][0][field]=country", query);
        Assert.Contains("groups[0][rules][0][value]=United States", query);
        Assert.Contains("groups[0][rules][1][field]=resolution", query);
        Assert.Contains("groups[0][rules][1][value]=2160p", query);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"items\":[],\"total\":0,\"total_exact\":true,\"has_more\":false}")
            });
        }
    }
}
