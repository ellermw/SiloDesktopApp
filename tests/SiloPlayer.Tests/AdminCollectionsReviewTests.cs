using System.Net;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Regressions for the seven review findings on the Collections family. Contracts: official Silo Server/WebUI at 217ff25a.</summary>
public sealed class AdminCollectionsReviewTests
{
    private static HttpResponseMessage Tagged(string json, string tag = "\"r1\"") => AdminCollectionsTests.Tagged(json, tag);

    private static HttpResponseMessage ListReply(HttpRequestMessage request) => request.RequestUri!.AbsolutePath switch
    {
        "/api/v2/admin/collections/capabilities" => Tagged("{\"state\":\"available\",\"groups\":false,\"imports\":true,\"import_sources\":[\"mdblist\",\"tmdb\"],\"mdblist_search\":true}"),
        _ => Tagged("{\"items\":[],\"page\":{\"has_more\":false}}")
    };

    // 1 - A create answered without a usable identity may have written. It is an unconfirmed outcome that blocks writes until the list is re-read.
    [Theory]
    [InlineData("{\"id\":12345,\"title\":\"Numeric\"}")]
    [InlineData("{\"title\":\"No identity\"}")]
    [InlineData("{\"id\":\"\",\"title\":\"Blank\"}")]
    [InlineData("{\"id\":{\"nested\":1}}")]
    public async Task MalformedCreateIdentityIsAnUnknownOutcomeThatBlocksFurtherWrites(string reply)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.EditAsync(null, "manual", "1"); controller.Draft!["title"] = "Unconfirmed";
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Post ? Tagged(reply) : ListReply(request));
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => controller.SaveAsync());
        Assert.True(controller.OutcomeUnknown);
        Assert.Null(controller.CreatedId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SaveAsync());
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("POST ")));
        // A fresh draft says nothing about the lost create; only the canonical list does.
        await controller.EditAsync(null, "manual", "1");
        Assert.True(controller.OutcomeUnknown);
        await controller.LoadAsync("1", false);
        Assert.False(controller.OutcomeUnknown);
    }

    [Fact]
    public async Task MalformedImportedIdentityIsAnUnknownOutcome()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        kit.Http.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Post ? Tagged("{\"collection\":{\"id\":77}}") : ListReply(request));
        await controller.LoadAsync(null, false);
        await controller.EditAsync(null, "synced", "1");
        controller.Draft!["title"] = "Imported"; controller.Draft["source_config"] = new JsonObject { ["mode"] = "mdblist_json", ["url"] = "https://mdblist.com/lists/a/b/json" };
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => controller.SaveAsync());
        Assert.True(controller.OutcomeUnknown);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SaveAsync());
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("POST ")));
    }

    // 2 - A confirmed create keeps its identity through a failed canonical read, through Reload as well as Save.
    [Fact]
    public async Task ConfirmedCreateIdentitySurvivesReloadAfterFailedCanonicalRead()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.EditAsync(null, "manual", "1"); controller.Draft!["title"] = "Known";
        var reads = 0;
        kit.Http.Respond = (r, _) =>
        {
            if (r.Method == HttpMethod.Post) return Task.FromResult(Tagged("{\"id\":\"known\",\"title\":\"Known\"}"));
            if (++reads <= 2) throw new HttpRequestException("Synthetic read failure");
            return Task.FromResult(Tagged("{\"id\":\"known\",\"title\":\"Known\",\"library_ids\":[\"1\"],\"collection_type\":\"manual\"}"));
        };
        await Assert.ThrowsAsync<HttpRequestException>(() => controller.SaveAsync());
        Assert.Equal("known", controller.CreatedId); Assert.Equal("Known", controller.Draft!["title"]!.ToString());
        // The editor's Reload button used to start a blank draft and forget the identity.
        await Assert.ThrowsAsync<HttpRequestException>(() => controller.ReloadEditorAsync("manual", "1"));
        Assert.Equal("known", controller.CreatedId);
        await controller.ReloadEditorAsync("manual", "1");
        Assert.Equal("known", controller.Editor!.Value["id"]!.ToString());
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("POST ")));
    }

    [Fact]
    public async Task ReloadWithoutAConfirmedCreateStillStartsAFreshDraft()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.EditAsync(null, "manual", "1"); controller.Draft!["title"] = "Typed";
        await controller.ReloadEditorAsync("manual", "1");
        Assert.Equal("", controller.Draft!["title"]!.ToString()); Assert.Null(controller.CreatedId);
        Assert.Empty(kit.Http.Requests);
    }

    // 3 - A starter-pack job belongs to the authority that queued it.
    [Fact]
    public async Task AuthorityRetirementDropsTheStarterJobIdentity()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        controller.BeginStarterJob("job-1"); Assert.Equal("job-1", controller.StarterJobId); Assert.Equal(1, controller.CountStarterJobRead());
        kit.SetRole("user");
        for (var i = 0; i < 100 && controller.StarterJobId != null; i++) await Task.Delay(10);
        Assert.Null(controller.StarterJobId); Assert.Equal(0, controller.StarterJobReads);
        kit.SetRole("admin"); controller.Open();
        Assert.Null(controller.StarterJobId);
    }

    [Fact]
    public void SuspendedPageDropsTheStarterJobIdentity()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        controller.BeginStarterJob("job-2"); controller.Suspend();
        Assert.Null(controller.StarterJobId);
    }

    // 4 - A smart collection's rules keep their own libraries; Where it shows never rewrites them.
    [Fact]
    public async Task SmartRulesKeepTheirOwnLibrariesWhenWhereItShowsDiffers()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.EditAsync(null, "smart", "1");
        Assert.Equal(1, controller.Draft!["query_definition"]!["library_ids"]![0]!.GetValue<int>());
        controller.Draft["title"] = "Smart"; controller.Draft["library_ids"] = new JsonArray("1");
        controller.Draft["query_definition"] = JsonNode.Parse("{\"library_ids\":[2,3],\"match\":\"all\",\"groups\":[{\"match\":\"all\",\"rules\":[{\"field\":\"year\",\"op\":\"gte\",\"value\":2000}]}]}");
        kit.Http.Respond = (request, _) => Task.FromResult(Tagged("{\"id\":\"smart\",\"title\":\"Smart\",\"library_ids\":[\"1\"],\"collection_type\":\"smart\"}"));
        await controller.SaveAsync();
        var body = JsonNode.Parse(kit.Http.Bodies.First(b => b.Length > 0))!;
        Assert.Equal("1", body["library_ids"]![0]!.GetValue<string>());
        Assert.Equal([2, 3], body["query_definition"]!["library_ids"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray());
    }

    [Fact]
    public async Task EditingRuleLibrariesNeverTouchesWhereItShows()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.EditAsync(null, "smart", "1");
        controller.SetRuleLibraries(["4", "5"]);
        Assert.Equal([4, 5], controller.Draft!["query_definition"]!["library_ids"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray());
        Assert.Equal("1", controller.Draft["library_ids"]![0]!.GetValue<string>());
        controller.SetRuleLibraries([]);
        Assert.Empty(controller.Draft["query_definition"]!["library_ids"]!.AsArray());
        Assert.Throws<ArgumentException>(() => controller.SetRuleLibraries(["not-a-library"]));
    }

    // 5 - The admin preview answers { items: [{ content_id, title, type }], total }.
    [Fact]
    public async Task PreviewReadsTheAdminEnvelopeAndRejectsOthers()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var api = new AdminCollectionsApi(kit.Client); var reply = "{\"items\":[{\"content_id\":\"m1\",\"title\":\"One\",\"type\":\"movie\"}],\"page\":{\"has_more\":false},\"total\":40}";
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged(reply));
        var preview = await api.PreviewAsync(owner, new JsonObject { ["library_ids"] = new JsonArray(2), ["groups"] = new JsonArray() });
        Assert.Equal(40, preview.Total); Assert.Equal("One", Assert.Single(preview.Items).Title);
        var body = JsonNode.Parse(kit.Http.Bodies.Single())!;
        Assert.Equal(24, body["limit"]!.GetValue<int>()); Assert.Equal(2, body["query_definition"]!["library_ids"]![0]!.GetValue<int>());
        Assert.Equal("POST /api/v2/admin/collections/preview", kit.Http.Requests.Single());
        foreach (var wrong in new[] { "{\"items\":[{\"content_id\":\"m1\",\"title\":\"One\"}]}", "{\"total\":3}", "{\"data\":{\"items\":[],\"total\":0}}", "{\"items\":{},\"total\":1}", "{\"items\":[],\"total\":\"3\"}" })
            Assert.Throws<InvalidDataException>(() => AdminCollectionsApi.ParsePreview(JsonNode.Parse(wrong)!.AsObject()));
    }

    // 6 - A stored default sort wins over the rules' Order, so changing Order clears it and keeps other settings.
    [Fact]
    public async Task ChangingSmartSortClearsTheStoredDefaultSortOnly()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.EditAsync(null, "smart", "1");
        controller.Draft!["sort_config"] = JsonNode.Parse("{\"field\":\"title\",\"order\":\"asc\",\"keep\":true}");
        Assert.NotNull(controller.StoredDefaultSort);
        controller.ClearStoredDefaultSort();
        Assert.Null(controller.StoredDefaultSort);
        Assert.True(controller.Draft["sort_config"]!["keep"]!.GetValue<bool>());
        Assert.Null(controller.Draft["sort_config"]!["field"]); Assert.Null(controller.Draft["sort_config"]!["order"]);
        Assert.Contains("sort_config", controller.ChangedDraftFields());
        Assert.Empty(AdminCollectionsController.WithoutDefaultSort(null));
        Assert.Empty(AdminCollectionsController.WithoutDefaultSort(new JsonObject { ["field"] = "year", ["order"] = "desc" }));
    }

    // 7 - With the MDBList capability on, the editor searches MDBList through the viewer discovery route.
    [Fact]
    public async Task MdbListSearchUsesTheCapabilityAndTheDiscoveryEnvelope()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var api = new AdminCollectionsApi(kit.Client);
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/capabilities")
            ? Tagged("{\"state\":\"available\",\"imports\":true,\"import_sources\":[\"mdblist\"],\"mdblist_search\":true}")
            : Tagged("{\"configured\":true,\"items\":[{\"id\":\"12\",\"user_id\":\"3\",\"user_name\":\"someone\",\"name\":\"Top watched\",\"slug\":\"top\",\"description\":\"d\",\"media_type\":\"movie\",\"items\":100,\"likes\":9,\"url\":\"https://mdblist.com/lists/someone/top\"}],\"page\":{\"has_more\":false}}"));
        Assert.True((await api.CapabilitiesAsync(owner)).MdblistSearch);
        var found = await api.MdbListSearchAsync(owner, " top watched ", default);
        Assert.Equal("GET /api/v2/collections/import/mdblist/search?q=top%20watched", kit.Http.Requests.Last());
        var hit = Assert.Single(found.Lists); Assert.True(found.Configured); Assert.Equal("Top watched", hit.Name);
        Assert.Equal("https://mdblist.com/lists/someone/top/json", AdminCollectionsApi.MdbListFeed(hit));
        Assert.Equal("", AdminCollectionsApi.MdbListFeed(hit with { Url = "https://evil.invalid/lists/a/b" }));
        Assert.Equal("", AdminCollectionsApi.MdbListFeed(hit with { Url = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => api.MdbListSearchAsync(owner, "  ", default));
        Assert.Throws<InvalidDataException>(() => AdminCollectionsApi.ParseMdbListSearch(JsonNode.Parse("{\"lists\":[]}")!.AsObject()));
    }
}
