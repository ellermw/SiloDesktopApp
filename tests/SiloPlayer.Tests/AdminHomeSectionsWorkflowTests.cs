using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminHomeSectionsWorkflowTests
{
    private const string Row = """{"id":"a","scope":"home","library_id":null,"title":"First","section_type":"recently_added","item_limit":20,"enabled":true,"config":{"unknown_future":{"keep":true}}}""";
    private static HttpResponseMessage Tagged(string body, string tag = "\"row-1\"")
    {
        var result = AdminMonitorKit.Json(body); result.Headers.ETag = EntityTagHeaderValue.Parse(tag); return result;
    }

    [Fact]
    public async Task RowSavePreservesUnknownConfigAndUsesReviewedRowVersion()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture(); var api = new AdminHomeSectionsApi(kit.Client);
        kit.Http.Respond = (request, _) => Task.FromResult(Tagged(Row));
        var session = await api.GetAsync(lease, "a"); var draft = AdminHomeSectionDraft.From(session.Row); draft.Title = "Renamed";
        await api.SaveAsync(lease, session, draft);
        Assert.Equal("PATCH /api/v2/admin/sections/a", kit.Http.Requests.Last());
        var body = JsonNode.Parse(kit.Http.Bodies.Last()!)!;
        Assert.True(body["config"]!["unknown_future"]!["keep"]!.GetValue<bool>());
        Assert.Equal("Renamed", body["title"]!.GetValue<string>());
        Assert.Null(body["scope"]); Assert.Null(body["library_id"]);
    }

    [Fact]
    public async Task LostCreateReplyIsUnknownAndIsNeverReplayed()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture(); var api = new AdminHomeSectionsApi(kit.Client);
        kit.Http.Respond = (_, _) => throw new HttpRequestException("synthetic lost response");
        await Assert.ThrowsAsync<AdminHomeSectionsUnknownException>(() => api.CreateAsync(lease,
            new AdminHomeSectionDraft { SectionType = "recently_added", Title = "New" }, 0, false));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ConflictIsExplicitAndDoesNotFetchANewerVersionOrReplay()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture(); var api = new AdminHomeSectionsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged(Row)); var session = await api.GetAsync(lease, "a");
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.PreconditionFailed)
            { Content = new StringContent("""{"type":"stale","title":"Changed","status":412}""") });
        await Assert.ThrowsAsync<AdminHomeSectionsConflictException>(() => api.DeleteAsync(lease, session));
        Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Theory]
    [InlineData("library", "a", "\"row-1\"")]
    [InlineData("home", "other", "\"row-1\"")]
    [InlineData("home", "a", "W/\"row-1\"")]
    public async Task WrongScopeIdentityOrWeakValidatorCannotArmEditor(string scope, string id, string tag)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture(); var api = new AdminHomeSectionsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged(Row.Replace("\"home\"", "\"" + scope + "\"").Replace("\"id\":\"a\"", "\"id\":\"" + id + "\""), tag));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.GetAsync(lease, "a"));
    }

    [Fact]
    public async Task CollectionPickerReadsOneBoundedPageOnly()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture(); var api = new AdminHomeSectionsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"id":"7","title":"Synthetic"}],"page":{"has_more":true,"next_cursor":"next"}}"""));
        var page = await api.ChoicesAsync(lease, "collections");
        Assert.Single(page.Items); Assert.True(page.Page!.HasMore); Assert.Single(kit.Http.Requests);
        Assert.Equal("GET /api/v2/admin/collections", kit.Http.Requests.Single());
    }

    [Fact]
    public void CollectionRowsCannotBecomeHeroesAndDraftBoundsAreEnforced()
    {
        var draft = new AdminHomeSectionDraft { SectionType = "collection_posters", Title = "Collections", Featured = true };
        draft.Config["collections"] = new JsonArray(new JsonObject { ["library_collection_id"] = "7" });
        Assert.Throws<ArgumentException>(() => draft.Validate());
        draft.Featured = false; draft.ItemLimit = 501; Assert.Throws<ArgumentException>(() => draft.Validate());
        draft.ItemLimit = 20; draft.Validate();
    }

    [Fact]
    public async Task LibraryPageUsesStringIdAndRejectsHomeRowsInItsSnapshot()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture(); var api = new AdminHomeSectionsApi(kit.Client); var scope = new AdminHomeSectionScope("library", "7");
        kit.Http.Respond = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/order")
            ? Tagged("""{"scope":"library","library_id":"7","ordered_ids":["a"]}""") : AdminMonitorKit.Json("{\"items\":[" + Row + "]}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ReadAsync(lease, scope: scope));
        Assert.All(kit.Http.Requests, request => Assert.Contains("scope=library&library_id=7", request));
    }

    [Fact]
    public async Task LibraryCreateOmitsHomeIdentityAndCopiesAreNeverHeroes()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture(); var api = new AdminHomeSectionsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged(Row.Replace("\"scope\":\"home\",\"library_id\":null", "\"scope\":\"library\",\"library_id\":\"7\"")));
        var draft = new AdminHomeSectionDraft { SectionType = "recently_added", Title = "Library row", Featured = true };
        await api.CreateAsync(lease, draft, 4, true, scope: new("library", "7"));
        var body = JsonNode.Parse(kit.Http.Bodies.Last()!)!; Assert.Equal("7", body["library_id"]!.GetValue<string>()); Assert.Equal(4, body["position"]!.GetValue<int>());
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"created":2}"""));
        await api.BulkCopyAsync(lease, draft, ["7", "8"]);
        body = JsonNode.Parse(kit.Http.Bodies.Last()!)!; Assert.False(body["featured"]!.GetValue<bool>()); Assert.Null(body["web_only"]); Assert.Null(body["position"]);
        Assert.Equal("POST /api/v2/admin/sections/bulk", kit.Http.Requests.Last());
    }

    [Theory]
    [InlineData("02-29", "03-01", "https://mdblist.com/lists/synthetic/list", true)]
    [InlineData("02-30", "03-01", "https://mdblist.com/lists/synthetic/list", false)]
    [InlineData("12-24", "01-02", "https://mdblist.com/lists/synthetic/list", true)]
    [InlineData("01-01", "01-02", "https://user:secret@mdblist.com/lists/synthetic/list", false)]
    [InlineData("01-01", "01-02", "https://example.invalid/lists/synthetic/list", false)]
    public void SeasonalDatesAndPublicSourceAreValidated(string start, string end, string url, bool valid)
    {
        var draft = new AdminHomeSectionDraft { SectionType = "seasonal_themed", Title = "Seasonal" };
        draft.Config["enabled_themes"] = new JsonArray();
        draft.Config["custom_holidays"] = new JsonArray(new JsonObject { ["name"] = "Synthetic holiday", ["start"] = start, ["end"] = end, ["list_url"] = url });
        if (valid) draft.Validate(); else Assert.Throws<ArgumentException>(() => draft.Validate());
    }

    [Fact]
    public async Task OversizedMetadataPickerDoesNotBecomeAnUnboundedWalk()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture(); var api = new AdminHomeSectionsApi(kit.Client);
        var items = new JsonArray(); for (var i = 0; i < 2001; i++) items.Add(new JsonObject { ["id"] = i.ToString(), ["title"] = "Synthetic" });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(new JsonObject { ["items"] = items }.ToJsonString()));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ChoicesAsync(lease, "collections")); Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task PreviewUsesCurrentLibraryAndNeverMoreThanTwelveItems()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture(); var api = new AdminHomeSectionsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[],"total_count":0}"""));
        await api.PreviewAsync(lease, new() { SectionType = "recently_added", Title = "Synthetic", ItemLimit = 100 }, scope: new("library", "7"));
        var body = JsonNode.Parse(kit.Http.Bodies.Single()!)!; Assert.Equal("7", body["library_id"]!.GetValue<string>()); Assert.Equal(12, body["item_limit"]!.GetValue<int>());
    }

    [Fact]
    public void VariantChangePreservesLibrariesAndUnknownTuning()
    {
        var draft = new AdminHomeSectionDraft { SectionType = "trending_on_server", Title = "Trending" };
        draft.Config["window"] = "24h"; draft.Config["future_option"] = "keep"; draft.Config["filter_library_ids"] = new JsonArray(7);
        draft.ApplyVariant("tr_7d");
        Assert.Equal("7d", draft.Config["window"]!.ToString()); Assert.Equal("keep", draft.Config["future_option"]!.ToString()); Assert.Equal(7, draft.Config["filter_library_ids"]![0]!.GetValue<int>());
    }

    [Fact]
    public async Task AcceptedOrOversizedErrorIsUnknownAndCannotTriggerReplay()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture(); var api = new AdminHomeSectionsApi(kit.Client);
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{}") });
        await Assert.ThrowsAsync<AdminHomeSectionsUnknownException>(() => api.CreateAsync(lease, new() { SectionType = "recently_added", Title = "Synthetic" }, 0, false));
        Assert.Single(kit.Http.Requests);
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(new string('x', 4 * 1024 * 1024 + 1)) });
        await Assert.ThrowsAsync<AdminHomeSectionsUnknownException>(() => api.CreateAsync(lease, new() { SectionType = "recently_added", Title = "Synthetic" }, 0, false));
        Assert.Equal(2, kit.Http.Requests.Count);
    }
}
