using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
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
            snapshot: "opaque-signed-window-cursor",
            source: "history");

        var query = Uri.UnescapeDataString(handler.LastUri!.Query);
        Assert.Contains("source=history", query);
        Assert.Contains("limit=60", query);
        Assert.Contains("seek=120", query);
        Assert.Contains("cursor=opaque-signed-window-cursor", query);
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

        using var groups = ReadGroups(handler.LastUri!);
        Assert.Equal("all", groups.RootElement[0].GetProperty("match").GetString());
        var rules = groups.RootElement[0].GetProperty("rules");
        Assert.Equal("country", rules[0].GetProperty("field").GetString());
        Assert.Equal("United States", rules[0].GetProperty("value").GetString());
        Assert.Equal("resolution", rules[1].GetProperty("field").GetString());
        Assert.Equal("2160p", rules[1].GetProperty("value").GetString());
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

        using var groups = ReadGroups(handler.LastUri!);
        var rules = groups.RootElement[0].GetProperty("rules");
        Assert.Equal("rating_imdb", rules[0].GetProperty("field").GetString());
        Assert.Equal("gte", rules[0].GetProperty("op").GetString());
        Assert.Equal(7.5, rules[0].GetProperty("value").GetDouble());
        Assert.Equal("hdr", rules[1].GetProperty("field").GetString());
        Assert.True(rules[1].GetProperty("value").GetBoolean());
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

        using var groups = ReadGroups(handler.LastUri!);
        Assert.Equal("any", groups.RootElement[0].GetProperty("match").GetString());
        var rules = groups.RootElement[0].GetProperty("rules");
        Assert.Equal("genre", rules[0].GetProperty("field").GetString());
        Assert.Equal("year", rules[1].GetProperty("field").GetString());
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
        using var groups = ReadGroups(handler.LastUri!);
        var range = groups.RootElement[0].GetProperty("rules")[0].GetProperty("value");
        Assert.Equal(1990, range[0].GetInt32());
        Assert.Equal(1999, range[1].GetInt32());
        Assert.Equal("any", groups.RootElement[1].GetProperty("match").GetString());
        Assert.Equal("hdr", groups.RootElement[1].GetProperty("rules")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task GetFiltersAsync_UsesV2SupportedScopeWithoutSearchText()
    {
        var handler = new CaptureHandler();
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        await new CatalogApi(client).GetFiltersAsync(
            source: "query",
            q: "Here Comes The Boom",
            type: "video");

        var query = Uri.UnescapeDataString(handler.LastUri!.Query);
        Assert.Equal("/api/v2/catalog/filters", handler.LastUri.AbsolutePath);
        Assert.Contains("source=query", query);
        Assert.DoesNotContain("q=", query);
        Assert.Contains("type=video", query);
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
        Assert.Equal("/api/v2/catalog/audiobook-groups", handler.LastUri.AbsolutePath);
        Assert.Contains("library_id=52", query);
        Assert.Contains("group_by=author", query);
        Assert.Contains("sort=count", query);
        Assert.Contains("q=King", query);
        Assert.DoesNotContain("offset=", query);
        Assert.Contains("skip_total=true", query);
    }

    private static JsonDocument ReadGroups(Uri uri)
        => JsonDocument.Parse(QueryHelpers.ParseQuery(uri.Query)["groups"].ToString());

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
