using System.Net.Http.Headers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AdminHomeSectionsApiTests
{
    private const string Order = """{"scope":"home","library_id":null,"ordered_ids":["a","b"]}""";
    private const string Rows = """{"items":[{"id":"b","scope":"home","library_id":null,"title":"Disabled","section_type":"recently_added","enabled":false},{"id":"a","scope":"home","library_id":null,"title":"First","section_type":"recently_added","enabled":true}]}""";

    private static HttpResponseMessage Tagged(string body, string tag = "\"r1\"")
    {
        var response = AdminMonitorKit.Json(body);
        response.Headers.TryAddWithoutValidation("ETag", tag);
        return response;
    }

    [Fact]
    public async Task ListFencesMembershipAndIncludesDisabledRowsInServerOrder()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/order") ? Tagged(Order) : AdminMonitorKit.Json(Rows));
        var snapshot = await new AdminHomeSectionsApi(kit.Client).ReadAsync(lease);
        Assert.Equal(new[] { "a", "b" }, snapshot.Rows.Select(row => row.Id));
        Assert.False(snapshot.Rows[1].Enabled);
        Assert.Equal(new[] { "GET /api/v2/admin/sections/order?scope=home", "GET /api/v2/admin/sections?scope=home", "GET /api/v2/admin/sections/order?scope=home" }, kit.Http.Requests);
    }

    [Fact]
    public async Task ChangedOrderOrIncompleteMembershipCannotAuthorizeReorder()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var orderReads = 0;
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/order")
            ? Tagged(Order, ++orderReads == 1 ? "\"r1\"" : "\"r2\"") : AdminMonitorKit.Json(Rows));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminHomeSectionsApi(kit.Client).ReadAsync(lease));
        Assert.Equal(3, kit.Http.Requests.Count);
    }

    [Theory]
    [InlineData("""{"items":[]}""")]
    [InlineData("""{"items":[],"page":{"has_more":true,"next_cursor":"next"}}""")]
    [InlineData("""{}""")]
    public async Task IncompleteListsCannotAuthorizeWholeOrderWrites(string rows)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/order") ? Tagged(Order) : AdminMonitorKit.Json(rows));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminHomeSectionsApi(kit.Client).ReadAsync(lease));
        Assert.DoesNotContain(kit.Http.Requests, request => request.StartsWith("PUT "));
    }

    [Theory]
    [InlineData("W/\"r1\"")]
    [InlineData("*")]
    public async Task WeakOrWildcardValidatorsCannotAuthorizeReorder(string etag)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged(Order, etag));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminHomeSectionsApi(kit.Client).ReadAsync(lease));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ReorderRequiresPermutationAndSendsReviewedValidatorOnce()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        string? sentTag = null;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                sentTag = request.Headers.IfMatch.Single().ToString();
                return Task.FromResult(Tagged(Order, "\"r2\""));
            }
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/order") ? Tagged(Order) : AdminMonitorKit.Json(Rows));
        };
        var api = new AdminHomeSectionsApi(kit.Client);
        var snapshot = await api.ReadAsync(lease);
        await Assert.ThrowsAsync<ArgumentException>(() => api.ReorderAsync(lease, snapshot, ["a"]));
        Assert.Equal(3, kit.Http.Requests.Count);
        await api.ReorderAsync(lease, snapshot, ["b", "a"]);
        Assert.Equal("\"r1\"", sentTag);
        Assert.Equal("PUT /api/v2/admin/sections/order?scope=home", kit.Http.Requests.Last());
        Assert.Equal("{\"ordered_ids\":[\"b\",\"a\"]}", kit.Http.Bodies.Last());
    }

    [Fact]
    public async Task ValueSuggestionsSendOneLibraryIdsKeyPerLibrary()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"values":[],"matches":[]}"""));
        var api = new AdminHomeSectionsApi(kit.Client); var caps = new AdminHomeRuleCapabilities { FacetValueSearch = true };
        await api.RuleValuesAsync(lease, "audio_language", "language", "", "movie", ["1", "2"], caps);
        Assert.Equal("GET /api/v2/catalog/filters?source=query&type=movie&library_ids=1&library_ids=2", kit.Http.Requests.Last());
        await api.RuleValuesAsync(lease, "genre", "facet", "dr", "movie", ["1", "2"], caps);
        Assert.Equal("GET /api/v2/catalog/filters/search?source=query&facet=genre&q=dr&limit=50&type=movie&library_ids=1&library_ids=2", kit.Http.Requests.Last());
        // A server without facet value search refuses library_ids, so none is sent.
        await api.RuleValuesAsync(lease, "genre", "facet", "dr", "movie", ["1", "2"], new AdminHomeRuleCapabilities());
        Assert.Equal("GET /api/v2/catalog/filters/search?source=query&facet=genre&q=dr&limit=50&type=movie", kit.Http.Requests.Last());
    }

    [Fact]
    public async Task RetiredAuthorityCannotUsePreviouslyReadSnapshot()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/order") ? Tagged(Order) : AdminMonitorKit.Json(Rows));
        var api = new AdminHomeSectionsApi(kit.Client);
        var snapshot = await api.ReadAsync(lease);
        authority.Retire();
        using var successor = authority.Capture();
        await Assert.ThrowsAsync<MonitorAuthorityRetiredException>(() => api.ReorderAsync(successor, snapshot, ["b", "a"]));
        Assert.Equal(3, kit.Http.Requests.Count);
    }
}
