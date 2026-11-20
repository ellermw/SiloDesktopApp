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

    // ── Second review (November 20) ────────────────────────────────────────

    // 2 - A job id belongs to the authority that queued it. An answer that arrives after that authority retired or was replaced stores nothing.
    [Fact]
    public async Task StarterJobIsNotStoredForARetiredOrReplacedAuthority()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        var queuedUnder = controller.Owner!;
        Assert.True(controller.BeginStarterJob(queuedUnder, "job-a")); Assert.Equal("job-a", controller.StarterJobId);
        controller.EndStarterJob();
        controller.Open();
        Assert.False(controller.BeginStarterJob(queuedUnder, "job-b"));
        Assert.Null(controller.StarterJobId);
        var current = controller.Owner!;
        kit.SetRole("user");
        for (var i = 0; i < 100 && current.IsCurrent; i++) await Task.Delay(10);
        Assert.False(controller.BeginStarterJob(current, "job-c"));
        Assert.Null(controller.StarterJobId); Assert.Equal(0, controller.StarterJobReads);
        Assert.False(controller.BeginStarterJob(null, "job-d")); Assert.Null(controller.StarterJobId);
    }

    // 3 - A title added after a removal must go after the last one. The item count ties with the last position once a position was removed.
    [Theory]
    [InlineData("{\"items\":[{\"position\":0},{\"position\":2},{\"position\":3}],\"page\":{\"has_more\":false}}", 3, 4)]
    [InlineData("{\"items\":[{\"position\":0},{\"position\":1}],\"page\":{\"has_more\":false}}", 2, 2)]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":false}}", 0, 0)]
    [InlineData("{\"items\":[],\"page\":{\"has_more\":false}}", 7, 7)]
    // Not fully read: the larger of the collection's count and what was read.
    [InlineData("{\"items\":[{\"position\":0},{\"position\":2}],\"page\":{\"has_more\":true}}", 90, 90)]
    [InlineData("{\"items\":[{\"position\":0},{\"position\":2}],\"page\":{\"has_more\":true}}", 1, 2)]
    // A title with no usable position means the list was not read completely.
    [InlineData("{\"items\":[{\"position\":0},{\"title\":\"x\"}],\"page\":{\"has_more\":false}}", 5, 5)]
    [InlineData("{\"items\":[{\"position\":0}]}", 5, 5)]
    public void NewMemberPositionGoesAfterTheLastOne(string page, int itemCount, int expected) =>
        Assert.Equal(expected, AdminCollectionsApi.NextMemberPosition(JsonNode.Parse(page)!.AsObject(), itemCount));

    // 4 - A rejected request says why. A server fault and an empty problem stay generic.
    [Fact]
    public void RejectedRequestShowsTheServersReasonAndServerFaultsStayGeneric()
    {
        var duplicate = new ApiException("validation", "A collection named Movies already exists.", 422) { ProblemType = "validation", ProblemDetail = "A collection named Movies already exists." };
        Assert.Equal("A collection named Movies already exists.", AdminCollectionsController.Message(duplicate));
        Assert.Equal("The cron schedule is not valid.", AdminCollectionsController.Message(new ApiException("bad", "The cron schedule is not valid.", 400) { ProblemDetail = "The cron schedule is not valid." }));
        Assert.Equal("The server could not complete the collection request.", AdminCollectionsController.Message(new ApiException("unknown", "HTTP 400", 400)));
        Assert.Equal("The server could not complete the collection request.", AdminCollectionsController.Message(new ApiException("boom", "database exploded at 10.0.0.5", 500) { ProblemDetail = "database exploded at 10.0.0.5" }));
        Assert.StartsWith("It changed since you opened this.", AdminCollectionsController.Message(new ApiException("x", "precondition", 412) { ProblemDetail = "precondition" }));
        Assert.StartsWith("Administrator access is required.", AdminCollectionsController.Message(new ApiException("x", "nope", 403) { ProblemDetail = "nope" }));
        var sentence = string.Join(" ", Enumerable.Repeat("again", 200));
        var long422 = AdminCollectionsController.Message(new ApiException("x", sentence, 422) { ProblemDetail = sentence });
        Assert.True(long422.Length <= 301); Assert.EndsWith("…", long422);
    }

    [Fact]
    public async Task ValidationRejectionOfACreateReachesTheEditorAndIsNotAnUnknownOutcome()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.EditAsync(null, "manual", "1"); controller.Draft!["title"] = "Movies";
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"type\":\"https://x.invalid/docs/api/v2/problems/validation\",\"title\":\"Invalid\",\"status\":422,\"detail\":\"A collection named Movies already exists.\"}", HttpStatusCode.UnprocessableEntity));
        var rejected = await Assert.ThrowsAsync<ApiException>(() => controller.SaveAsync());
        Assert.Equal("A collection named Movies already exists.", AdminCollectionsController.Message(rejected));
        Assert.Equal("A collection named Movies already exists.", controller.Error);
        Assert.False(controller.OutcomeUnknown);
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> ImportReplies(bool hideFails) => (r, _) =>
    {
        var path = r.RequestUri!.AbsolutePath;
        if (r.Method == HttpMethod.Post && path.EndsWith("/import/mdblist")) return Task.FromResult(Tagged("{\"collection\":{\"id\":\"imp1\",\"title\":\"Imported\"}}"));
        if (r.Method == HttpMethod.Patch) return Task.FromResult(hideFails ? AdminMonitorKit.Json("{\"status\":500}", HttpStatusCode.InternalServerError) : Tagged("{\"id\":\"imp1\",\"title\":\"Imported\",\"visibility\":\"hidden\"}", "\"r2\""));
        if (path == "/api/v2/admin/collections/imp1") return Task.FromResult(Tagged("{\"id\":\"imp1\",\"title\":\"Imported\",\"library_ids\":[\"1\"],\"collection_type\":\"mdblist\",\"visibility\":\"visible\",\"source_config\":{\"mode\":\"mdblist_json\",\"url\":\"https://mdblist.com/lists/a/b/json\"}}"));
        return Task.FromResult(ListReply(r));
    };

    private static async Task<AdminCollectionsController> OpenImportDraftAsync(AdminMonitorKit kit, AdminMonitorAuthority authority)
    {
        var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.LoadAsync(null, false);
        await controller.EditAsync(null, "synced", "1");
        controller.Draft!["title"] = "Imported"; controller.Draft["source_config"] = new JsonObject { ["mode"] = "mdblist_json", ["url"] = "https://mdblist.com/lists/a/b/json" };
        return controller;
    }

    // 5 - The import exists whether or not hiding it worked. A failed hide is a warning that it still shows, never a lost create.
    [Fact]
    public async Task FailedHideAfterImportKeepsTheCreateAndWarnsThatItStillShows()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = ImportReplies(hideFails: true);
        using var controller = await OpenImportDraftAsync(kit, authority);
        controller.Draft!["visibility"] = "hidden";
        var id = await controller.SaveAsync();
        Assert.Equal("imp1", id); Assert.Equal("imp1", controller.CreatedId);
        Assert.Contains("still shows on Collections tabs", controller.SaveWarning);
        Assert.Equal("imp1", controller.Editor!.Value["id"]!.ToString());
        Assert.False(controller.OutcomeUnknown);
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("POST ")));
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("PATCH ")));
    }

    [Fact]
    public async Task SuccessfulHideAfterImportLeavesNoWarning()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = ImportReplies(hideFails: false);
        using var controller = await OpenImportDraftAsync(kit, authority);
        controller.Draft!["visibility"] = "hidden";
        await controller.SaveAsync();
        Assert.Null(controller.SaveWarning); Assert.Contains(kit.Http.Requests, r => r.StartsWith("PATCH "));
    }

    // 6 - A list link is stored the way the official editor sends it.
    [Theory]
    [InlineData("mdblist", "  https://mdblist.com/lists/u/top?sort=rank#x  ", "https://mdblist.com/lists/u/top")]
    [InlineData("mdblist", "https://mdblist.com/lists/u/top/json/", "https://mdblist.com/lists/u/top/json")]
    [InlineData("mdblist", "https://mdblist.com/lists/u/top///", "https://mdblist.com/lists/u/top")]
    [InlineData("mdblist", "https://mdblist.com/lists/u/top/json", "https://mdblist.com/lists/u/top/json")]
    [InlineData("mdblist", "", "")]
    [InlineData("tmdb_list", "  https://www.themoviedb.org/list/8649937-marvel?language=en#top ", "https://www.themoviedb.org/list/8649937")]
    [InlineData("tmdb_list", "themoviedb.org/list/310/", "https://www.themoviedb.org/list/310")]
    [InlineData("tmdb_list", " 310 ", "https://www.themoviedb.org/list/310")]
    [InlineData("tmdb_list", " https://example.com/list/310 ", "https://example.com/list/310")]
    [InlineData("tmdb_list", "0", "0")]
    public void ListLinksAreCleanedLikeTheOfficialEditor(string source, string typed, string stored) =>
        Assert.Equal(stored, AdminCollectionsApi.CleanListLink(source, typed));

    // 7 - A template's poster goes with the import unless a poster is staged; no other kind of save carries it.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task TemplatePosterIsSentWithTheImportUnlessAPosterIsStaged(bool posterStaged, bool sent)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = ImportReplies(hideFails: false);
        using var controller = await OpenImportDraftAsync(kit, authority);
        controller.Draft!["poster_url"] = " /images/collection-templates/mdblist_imdb_top.jpg ";
        await controller.SaveAsync(posterStaged);
        var import = JsonNode.Parse(kit.Http.Bodies.First(b => b.Length > 0))!;
        Assert.Equal(sent ? "/images/collection-templates/mdblist_imdb_top.jpg" : null, import["poster_url"]?.ToString());
    }

    [Fact]
    public async Task PosterUrlNeverRidesOnAManualCreateOrASave()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.EditAsync(null, "manual", "1"); controller.Draft!["title"] = "Manual"; controller.Draft["poster_url"] = "/images/x.jpg";
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged("{\"id\":\"m1\",\"title\":\"Manual\",\"library_ids\":[\"1\"],\"collection_type\":\"manual\"}"));
        await controller.SaveAsync();
        Assert.DoesNotContain("poster_url", kit.Http.Bodies.First(b => b.Length > 0));
    }
}
