using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;

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

    [Fact]
    public async Task GetCatalogAsync_EncodesCurrentGuidedRulesWithOperatorsAndTypedValues()
    {
        var handler = new CaptureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        await new CatalogApi(client).GetCatalogAsync(
            43,
            extraRules:
            [
                new QueryRule { Field = "rating_imdb", Op = "gte", Value = 7.5 },
                new QueryRule { Field = "hdr", Op = "is", Value = true },
            ]);

        var query = Uri.UnescapeDataString(handler.LastUri!.Query);
        Assert.Contains("groups[0][rules][0][field]=rating_imdb", query);
        Assert.Contains("groups[0][rules][0][op]=gte", query);
        Assert.Contains("groups[0][rules][0][value]=7.5", query);
        Assert.Contains("groups[0][rules][1][field]=hdr", query);
        Assert.Contains("groups[0][rules][1][value]=true", query);
    }

    [Fact]
    public async Task GetCatalogAsync_EncodesAdvancedAnyMatchMode()
    {
        var handler = new CaptureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        await new CatalogApi(client).GetCatalogAsync(
            43,
            extraRules:
            [
                new QueryRule { Field = "genre", Op = "contains", Value = "science" },
                new QueryRule { Field = "year", Op = "gte", Value = 2020 },
            ],
            extraRulesMatch: "any");

        var query = Uri.UnescapeDataString(handler.LastUri!.Query);
        Assert.Contains("groups[0][match]=any", query);
        Assert.Contains("groups[0][rules][0][field]=genre", query);
        Assert.Contains("groups[0][rules][1][field]=year", query);
    }

    [Fact]
    public async Task GetCatalogAsync_EncodesAdvancedGroupsOuterMatchAndRangeValues()
    {
        var handler = new CaptureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        await new CatalogApi(client).GetCatalogAsync(
            43,
            queryGroups:
            [
                new QueryGroup
                {
                    Match = "all",
                    Rules = [new QueryRule { Field = "year", Op = "between", Value = new[] { 1990, 1999 } }]
                },
                new QueryGroup
                {
                    Match = "any",
                    Rules = [new QueryRule { Field = "hdr", Op = "is", Value = true }]
                }
            ],
            queryGroupsMatch: "any");

        var query = Uri.UnescapeDataString(handler.LastUri!.Query);
        Assert.Contains("match=any", query);
        Assert.Contains("groups[0][rules][0][value][0]=1990", query);
        Assert.Contains("groups[0][rules][0][value][1]=1999", query);
        Assert.Contains("groups[1][match]=any", query);
        Assert.Contains("groups[1][rules][0][field]=hdr", query);
    }

    [Fact]
    public async Task GetAudiobookGroupsAsync_UsesCurrentGroupedBrowseContract()
    {
        var handler = new CaptureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        await new CatalogApi(client).GetAudiobookGroupsAsync(
            52, "author", "count", "King", offset: 60, includeTotal: false);

        var query = Uri.UnescapeDataString(handler.LastUri!.Query);
        Assert.Equal("/api/v1/catalog/audiobook-groups", handler.LastUri.AbsolutePath);
        Assert.Contains("library_id=52", query);
        Assert.Contains("group_by=author", query);
        Assert.Contains("sort=count", query);
        Assert.Contains("q=King", query);
        Assert.Contains("offset=60", query);
        Assert.Contains("include_total=false", query);
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
