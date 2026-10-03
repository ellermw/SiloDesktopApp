using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Tests;

public sealed class CatalogLargeQueryBehaviorTests
{
    [Fact]
    public async Task LargeStructuredQueryUsesSupportedPostAndRetainsScopeWindowAndBasicRules()
    {
        using var wire = new Wire(); var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://large-query.invalid");
        await new CatalogApi(client).GetCatalogAsync(11, sort: "title", order: "asc", genre: "Drama", yearMin: "1980", q: "query", type: "video", limit: 100, offset: 100,
            includeTotal: false, snapshot: "window", source: "user_collection", queryGroups: [new() { Rules = [new() { Field = "actor", Op = "is", Value = new string('a', 33000) }] }], queryGroupsMatch: "any", collectionId: "collection", queryLimit: 150);
        Assert.Equal(HttpMethod.Post, wire.Method); Assert.Equal("/api/v2/catalog/query", wire.Path);
        using var body = JsonDocument.Parse(wire.Body!); var root = body.RootElement;
        Assert.Equal("11", root.GetProperty("library_id").GetString()); Assert.Equal("video", root.GetProperty("type").GetString()); Assert.Equal("window", root.GetProperty("cursor").GetString());
        Assert.Equal(100, root.GetProperty("limit").GetInt32()); Assert.Equal(100, root.GetProperty("seek").GetInt32()); Assert.Equal(150, root.GetProperty("query_limit").GetInt32()); Assert.True(root.GetProperty("skip_total").GetBoolean());
        Assert.Equal("collection", root.GetProperty("collection_id").GetString());
        // GET's plain filters are ANDed outside grouped match=any. Preserve that
        // expression when translating into the POST-only structured body.
        Assert.Equal("all", root.GetProperty("match").GetString());
        Assert.Contains(root.GetProperty("groups").EnumerateArray(), group => group.GetProperty("rules").EnumerateArray().Any(rule => rule.GetProperty("field").GetString() == "genre"));
    }
    [Fact]
    public async Task LargeJsonQueryUsesTheValidatedAdjacentCursorWithoutSeek()
    {
        using var wire = new Wire(); var client = new SiloApiClient(new HttpClient(wire)); client.SetBaseUrl("https://large-query.invalid");
        await new CatalogApi(client).GetCatalogAsync(11, limit: 100, offset: 100, snapshot: "root-window", nextCursor: "next-100",
            queryGroups: [new() { Rules = [new() { Field = "actor", Op = "is", Value = new string('a', 33000) }] }]);
        Assert.Equal(HttpMethod.Post, wire.Method); using var body = JsonDocument.Parse(wire.Body!);
        Assert.Equal("next-100", body.RootElement.GetProperty("cursor").GetString()); Assert.False(body.RootElement.TryGetProperty("seek", out _));
    }
    private sealed class Wire : HttpMessageHandler
    {
        public HttpMethod? Method; public string? Path; public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Method = request.Method; Path = request.RequestUri!.AbsolutePath; Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct); return new(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[],\"page\":{\"has_more\":false}}") }; }
    }
}
