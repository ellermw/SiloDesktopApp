using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminHomeSectionsControllerTests
{
    private static HttpResponseMessage ProblemResponse(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/problem+json") };

    private static async Task<(AdminMonitorKit Kit, AdminMonitorAuthority Authority, AdminHomeSectionsController Controller, AdminHomeSectionsFake Server)> OpenAsync(Action<AdminHomeSectionsFake>? seed = null)
    {
        var kit = AdminMonitorKit.Create(); var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var server = new AdminHomeSectionsFake().AddRow("a", "First").AddRow("b", "Second", enabled: false); seed?.Invoke(server);
        kit.Http.Respond = server.HandleAsync;
        var controller = new AdminHomeSectionsController(new(kit.Client), authority);
        await controller.OpenAsync(); Assert.True(controller.CanWrite);
        return (kit, authority, controller, server);
    }

    [Fact]
    public async Task UnknownWriteLocksMutationUntilExplicitReviewReload()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        var session = await controller.OpenEditAsync("a");
        server.Intercept = (request, _) => request.Method == HttpMethod.Patch ? throw new HttpRequestException("lost") : null;
        Assert.False(await controller.SaveAsync(session!, AdminHomeSectionDraft.From(session!.Row)));
        Assert.True(controller.State.ReviewRequired); Assert.True(controller.State.OutcomeUnknown); Assert.Equal(AdminHomeSectionsFailure.Unknown, controller.State.Failure);
        Assert.False(controller.CanWrite); Assert.NotNull(controller.State.Snapshot);
        var writes = server.Writes.Count;
        Assert.False(await controller.SaveAsync(session, AdminHomeSectionDraft.From(session.Row)));
        Assert.Equal(writes, server.Writes.Count);
        server.Intercept = null; await controller.ReloadAsync();
        Assert.True(controller.CanWrite); Assert.False(controller.State.ReviewRequired);
    }

    [Fact]
    public async Task DefiniteRefusalShowsServerDetailAndAllowsCorrectionAndRetry()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        var session = (await controller.OpenEditAsync("a"))!; var draft = AdminHomeSectionDraft.From(session.Row); draft.Title = "Renamed";
        var refused = true;
        server.Intercept = (request, _) => refused && request.Method == HttpMethod.Patch
            ? ProblemResponse(HttpStatusCode.UnprocessableEntity, AdminHomeSectionsFake.Problem(422, "Title is not allowed.", errors: ("title_taken", "Choose another title."))) : null;
        Assert.False(await controller.SaveAsync(session, draft));
        Assert.Equal(AdminHomeSectionsFailure.Refused, controller.State.Failure); Assert.False(controller.State.ReviewRequired); Assert.False(controller.State.OutcomeUnknown);
        Assert.Contains("Title is not allowed.", controller.State.Error); Assert.Contains("Choose another title.", controller.State.Error);
        Assert.True(controller.CanWrite);
        refused = false; draft.Title = "Fixed";
        Assert.True(await controller.SaveAsync(session, draft));
        Assert.Equal("Fixed", server.Rows["a"]["title"]!.GetValue<string>());
        Assert.Equal(2, server.Writes.Count(write => write.Method == "PATCH"));
    }

    [Fact]
    public async Task StaleVersionRequiresReviewAndIsNeverReplayed()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        var session = (await controller.OpenEditAsync("a"))!; server.Versions["a"]++;
        Assert.False(await controller.SaveAsync(session, AdminHomeSectionDraft.From(session.Row)));
        Assert.Equal(AdminHomeSectionsFailure.Conflict, controller.State.Failure); Assert.True(controller.State.ReviewRequired); Assert.False(controller.State.OutcomeUnknown);
        Assert.Single(server.Writes);
        await controller.ReloadAsync(); Assert.True(controller.CanWrite);
    }

    [Fact]
    public async Task ReadbackFailureAfterAWriteIsUnknownNotRefused()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        var session = (await controller.OpenEditAsync("a"))!; var written = false;
        server.Intercept = (request, _) =>
        {
            if (request.Method == HttpMethod.Patch) { written = true; return null; }
            return written && request.RequestUri!.AbsolutePath == "/api/v2/admin/sections/order" ? ProblemResponse(HttpStatusCode.Forbidden, AdminHomeSectionsFake.Problem(403, "No.", "forbidden")) : null;
        };
        Assert.False(await controller.SaveAsync(session, AdminHomeSectionDraft.From(session.Row)));
        Assert.Equal(AdminHomeSectionsFailure.Unknown, controller.State.Failure); Assert.True(controller.State.ReviewRequired);
    }

    [Fact]
    public async Task QuickSwitchSendsOnlyThatFieldOverTheVersionJustRead()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        Assert.True(await controller.QuickAsync(controller.State.Snapshot!.Rows[1], shown: true));
        var write = server.Writes.Single(); Assert.Equal("PATCH", write.Method); Assert.Equal("{\"enabled\":true}", write.Body); Assert.Equal("\"b-1\"", write.IfMatch);
        Assert.True(await controller.QuickAsync(controller.State.Snapshot!.Rows[0], hero: true));
        Assert.Equal("{\"featured\":true}", server.Writes.Last().Body);
    }

    [Fact]
    public async Task QuickSwitchOnARowThatChangedSinceItWasShownWritesNothing()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        var shown = controller.State.Snapshot!.Rows[0]; server.Rows["a"]["title"] = "Renamed elsewhere"; server.Versions["a"]++;
        Assert.False(await controller.QuickAsync(shown, shown: false));
        Assert.Empty(server.Writes); Assert.Equal(AdminHomeSectionsFailure.Conflict, controller.State.Failure); Assert.True(controller.State.ReviewRequired);
    }

    [Fact]
    public async Task BatchTurnOnReportsEachRowAndContinuesPastARefusedOne()
    {
        var (kit, authority, controller, server) = await OpenAsync(seed => seed.AddRow("c", "Third", enabled: false).AddRow("d", "Fourth", enabled: false)); using var _k = kit; using var _a = authority; using var _c = controller;
        server.Intercept = (request, _) => request.Method == HttpMethod.Patch && request.RequestUri!.AbsolutePath.EndsWith("/c")
            ? ProblemResponse(HttpStatusCode.UnprocessableEntity, AdminHomeSectionsFake.Problem(422, "Cannot turn this on.")) : null;
        server.Rows["a"]["enabled"] = true;
        var result = await controller.SetShownManyAsync(["a", "b", "c", "d"], true);
        Assert.NotNull(result);
        Assert.Equal(AdminHomeSectionsRowOutcome.Skipped, result!.Rows.Single(row => row.Id == "a").Outcome);
        Assert.Equal(new[] { "b", "d" }, result.ChangedIds);
        var failure = Assert.Single(result.Failures); Assert.Equal("c", failure.Id); Assert.Equal(AdminHomeSectionsRowOutcome.Failed, failure.Outcome); Assert.Contains("Cannot turn this on.", failure.Message);
        Assert.True(controller.CanWrite); Assert.All(server.Writes, write => Assert.Equal("{\"enabled\":true}", write.Body));
    }

    [Fact]
    public async Task BatchStopsAtAnUnknownOutcomeAndLocks()
    {
        var (kit, authority, controller, server) = await OpenAsync(seed => seed.AddRow("c", "Third", enabled: false)); using var _k = kit; using var _a = authority; using var _c = controller;
        server.Intercept = (request, _) => request.Method == HttpMethod.Patch && request.RequestUri!.AbsolutePath.EndsWith("/b") ? throw new HttpRequestException("lost") : null;
        var result = await controller.SetShownManyAsync(["b", "c"], true);
        Assert.Equal(AdminHomeSectionsRowOutcome.Unknown, result!.Rows[0].Outcome); Assert.Equal(AdminHomeSectionsRowOutcome.NotAttempted, result.Rows[1].Outcome);
        Assert.Single(server.Writes); Assert.False(controller.CanWrite); Assert.True(controller.State.OutcomeUnknown);
        // The row nothing was sent for is counted apart, never reported as a row that could not be saved.
        Assert.Equal("b", Assert.Single(result.Failures).Id); Assert.Equal(1, result.NotAttempted);
        Assert.Equal("1 more row was not attempted and is unchanged.", AdminHomeSectionsText.NotAttempted(1));
        Assert.Equal("3 more rows were not attempted and are unchanged.", AdminHomeSectionsText.NotAttempted(3)); Assert.Equal("", AdminHomeSectionsText.NotAttempted(0));
    }

    [Fact]
    public async Task BatchSkipsRowsThatChangedSinceTheyWereShown()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        server.Rows["b"]["item_limit"] = 40; server.Versions["b"]++;
        var result = await controller.SetShownManyAsync(["b"], true);
        Assert.Equal(AdminHomeSectionsRowOutcome.Changed, result!.Rows.Single().Outcome); Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task DeleteTreatsAnAlreadyGoneRowAsDeletedAndReportsAStaleOneWithoutStoppingTheRest()
    {
        var (kit, authority, controller, server) = await OpenAsync(seed => seed.AddRow("c", "Third").AddRow("d", "Fourth")); using var _k = kit; using var _a = authority; using var _c = controller;
        var plan = await controller.PrepareDeletesAsync(["a", "b", "c", "d"]);
        server.Versions["b"]++; server.Rows.Remove("c"); server.Orders["home"].Remove("c");
        var result = await controller.DeleteAsync(plan);
        Assert.Equal(new[] { "a", "c", "d" }, result.DeletedIds.OrderBy(id => id)); Assert.Equal(new[] { "b" }, result.FailedIds);
        Assert.True(result.Conflict); Assert.False(result.Unknown); Assert.True(controller.CanWrite);
        Assert.Equal(new[] { "b" }, controller.State.Snapshot!.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task DeleteMissingAtReviewTimeIsCountedAsGone()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        server.Rows.Remove("b");
        var plan = await controller.PrepareDeletesAsync(["a", "b"]);
        Assert.Equal(new[] { "b" }, plan.AlreadyGone); Assert.Single(plan.Sessions);
    }

    [Fact]
    public async Task DeleteStopsAtAnUnknownOutcome()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        var plan = await controller.PrepareDeletesAsync(["a", "b"]);
        server.Intercept = (request, _) => request.Method == HttpMethod.Delete ? throw new HttpRequestException("lost") : null;
        var result = await controller.DeleteAsync(plan);
        Assert.True(result.Unknown); Assert.Equal(new[] { "a", "b" }, result.FailedIds); Assert.Single(server.Writes); Assert.False(controller.CanWrite);
    }

    [Fact]
    public async Task RestoreSendsResetProfilesBodyAndRestoresOtherPagesOverTheirOwnVersionsReportingEachFailure()
    {
        var (kit, authority, controller, server) = await OpenAsync(seed => seed.AddRow("la", "Lib", scope: "library", library: "7")); using var _k = kit; using var _a = authority; using var _c = controller;
        server.Orders["library:8"] = []; server.Orders["library:9"] = [];
        server.Intercept = (request, _) => request.Method == HttpMethod.Put && request.RequestUri!.Query.Contains("library_id=8")
            ? ProblemResponse(HttpStatusCode.UnprocessableEntity, AdminHomeSectionsFake.Problem(422, "Cannot restore this page.")) : null;
        var result = await controller.RestoreAsync(controller.State.Snapshot!, true,
            [new("library", "7"), new("library", "8"), new("library", "9")]);
        Assert.True(result.PrimaryRestored);
        var failure = Assert.Single(result.Failures); Assert.Equal("8", failure.LibraryId); Assert.False(failure.Unknown); Assert.Contains("Cannot restore this page.", failure.Message);
        var puts = server.Writes.Where(write => write.Path.StartsWith("/api/v2/admin/sections/defaults")).ToArray();
        Assert.Equal(4, puts.Length);
        Assert.All(puts, write => Assert.Equal("{\"reset_profiles\":true}", write.Body));
        Assert.Contains(puts, write => write.Path.EndsWith("scope=library&library_id=9"));
        Assert.True(controller.CanWrite);
    }

    [Fact]
    public async Task RestoreStalePrimaryVersionStopsBeforeAnyOtherPage()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        var reviewed = controller.State.Snapshot!; server.OrderVersion++;
        var result = await controller.RestoreAsync(reviewed, false, [new("library", "8")]);
        Assert.False(result.PrimaryRestored); Assert.Single(server.Writes); Assert.Equal(AdminHomeSectionsFailure.Conflict, controller.State.Failure);
    }

    [Fact]
    public async Task RestoreUnknownOnAnotherPageLocksAndLeavesLaterPagesUntouched()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        server.Orders["library:8"] = []; server.Orders["library:9"] = [];
        server.Intercept = (request, _) => request.Method == HttpMethod.Put && request.RequestUri!.Query.Contains("library_id=8") ? throw new HttpRequestException("lost") : null;
        var result = await controller.RestoreAsync(controller.State.Snapshot!, false, [new("library", "8"), new("library", "9")]);
        Assert.True(result.PrimaryRestored); Assert.True(result.Failures[0].Unknown); Assert.False(result.Failures[1].Unknown);
        Assert.DoesNotContain(server.Writes, write => write.Path.Contains("library_id=9")); Assert.True(controller.State.OutcomeUnknown);
    }

    [Fact]
    public async Task CreateSendsTheBottomPositionAndAnUnusableAnswerIsUnknown()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        var draft = new AdminHomeSectionDraft { SectionType = "recently_added", Title = "New row", ItemLimit = 30 };
        Assert.True(await controller.SaveAsync(null, draft));
        var body = JsonNode.Parse(server.Writes.Single().Body)!;
        Assert.Equal(2, body["position"]!.GetValue<int>()); Assert.Equal("home", body["scope"]!.GetValue<string>()); Assert.Null(body["library_id"]);
        Assert.Equal(new[] { "new3" }, controller.State.NewIds);
        server.Intercept = (request, _) => request.Method == HttpMethod.Post ? new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("""{"scope":"home"}""") } : null;
        Assert.False(await controller.SaveAsync(null, draft));
        Assert.Equal(AdminHomeSectionsFailure.Unknown, controller.State.Failure); Assert.True(controller.State.ReviewRequired);
        Assert.Equal(2, server.Writes.Count);
    }

    [Fact]
    public async Task LibraryCreateWithCopiesIsOneBulkWriteAndAnUnknownBulkSaysWhereToLook()
    {
        var kit = AdminMonitorKit.Create(); using var _k = kit; using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var server = new AdminHomeSectionsFake().AddRow("la", "Lib", scope: "library", library: "7"); kit.Http.Respond = server.HandleAsync;
        using var controller = new AdminHomeSectionsController(new(kit.Client), authority); await controller.OpenAsync(new("library", "7"));
        var draft = new AdminHomeSectionDraft { SectionType = "recently_added", Title = "Everywhere" };
        server.Intercept = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/bulk") ? throw new HttpRequestException("lost") : null;
        Assert.False(await controller.SaveAsync(null, draft, ["8", "9"]));
        var body = JsonNode.Parse(server.Writes.Single().Body)!;
        Assert.Equal(new[] { "7", "8", "9" }, body["library_ids"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.False(body["featured"]!.GetValue<bool>()); Assert.Null(body["web_only"]); Assert.Null(body["position"]);
        Assert.Contains("other library pages", controller.State.Error);
    }

    [Fact]
    public async Task SaveSendsSavedValuesPreservesUnknownConfigAndRefusesAStoredZeroLimitLocally()
    {
        var (kit, authority, controller, server) = await OpenAsync(seed => seed.AddRow("h", "Hero", featured: true, webOnly: true, limit: 0, config: """{"future":{"keep":[1,2]},"filter_library_ids":[3]}""")); using var _k = kit; using var _a = authority; using var _c = controller;
        var session = (await controller.OpenEditAsync("h"))!; var draft = AdminHomeSectionDraft.From(session.Row);
        Assert.Equal(0, draft.ItemLimit); Assert.True(draft.Featured); Assert.True(draft.WebOnly);
        Assert.False(await controller.SaveAsync(session, draft)); Assert.Empty(server.Writes); Assert.Equal(AdminHomeSectionsFailure.Refused, controller.State.Failure); Assert.True(controller.CanWrite);
        draft.ItemLimit = 20; Assert.True(await controller.SaveAsync(session, draft));
        var body = JsonNode.Parse(server.Writes.Single().Body)!;
        Assert.Null(body["web_only"]); Assert.Null(body["scope"]); Assert.Null(body["position"]); Assert.True(body["enabled"]!.GetValue<bool>());
        Assert.Equal("[1,2]", body["config"]!["future"]!["keep"]!.ToJsonString()); Assert.Equal("[3]", body["config"]!["filter_library_ids"]!.ToJsonString());
    }

    [Fact]
    public async Task WebOnlyIsSentOnlyWhenAHeroChoiceChanged()
    {
        var (kit, authority, controller, server) = await OpenAsync(seed => seed.AddRow("h", "Hero", featured: true)); using var _k = kit; using var _a = authority; using var _c = controller;
        var session = (await controller.OpenEditAsync("h"))!; var draft = AdminHomeSectionDraft.From(session.Row); draft.WebOnly = true;
        Assert.True(await controller.SaveAsync(session, draft));
        Assert.True(JsonNode.Parse(server.Writes.Single().Body)!["web_only"]!.GetValue<bool>());
        var again = (await controller.OpenEditAsync("h"))!; var second = AdminHomeSectionDraft.From(again.Row); second.Title = "Hero 2";
        Assert.True(await controller.SaveAsync(again, second));
        Assert.Null(JsonNode.Parse(server.Writes.Last().Body)!["web_only"]);
    }

    [Fact]
    public async Task RetirementDropsRowsAndCannotReopenAStaleEditor()
    {
        var (kit, authority, controller, server) = await OpenAsync(); using var _k = kit; using var _a = authority; using var _c = controller;
        var session = await controller.OpenEditAsync("a");
        authority.Retire(); Assert.Null(controller.State.Snapshot); Assert.False(controller.CanWrite);
        await controller.SaveAsync(session!, AdminHomeSectionDraft.From(session!.Row));
        Assert.Empty(server.Writes);
    }

    [Fact]
    public async Task ACatalogFailureKeepsTheRowsAndCanBeRetriedWithoutReloadingThePage()
    {
        var kit = AdminMonitorKit.Create(); using var _k = kit; using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var server = new AdminHomeSectionsFake().AddRow("a", "First"); kit.Http.Respond = server.HandleAsync; var fail = true;
        server.Intercept = (request, _) => fail && request.RequestUri!.AbsolutePath == "/api/v2/sections/recipes" ? new HttpResponseMessage(HttpStatusCode.InternalServerError) : null;
        using var controller = new AdminHomeSectionsController(new(kit.Client), authority); await controller.OpenAsync();
        Assert.True(controller.CanWrite); Assert.True(controller.State.CatalogFailed); Assert.Null(controller.State.Catalog);
        fail = false; await controller.ReloadCatalogAsync();
        Assert.False(controller.State.CatalogFailed); Assert.NotNull(controller.State.Catalog); Assert.Single(controller.State.Snapshot!.Rows);
    }

    [Fact]
    public async Task AFailedPageReadIsVisibleAndReloadRecovers()
    {
        var kit = AdminMonitorKit.Create(); using var _k = kit; using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var server = new AdminHomeSectionsFake().AddRow("a", "First"); kit.Http.Respond = server.HandleAsync; var fail = true;
        server.Intercept = (request, _) => fail && request.RequestUri!.AbsolutePath == "/api/v2/admin/sections" ? ProblemResponse(HttpStatusCode.InternalServerError, AdminHomeSectionsFake.Problem(500, "Database is down.", "internal-error")) : null;
        using var controller = new AdminHomeSectionsController(new(kit.Client), authority); await controller.OpenAsync();
        Assert.Equal(AdminHomeSectionsFailure.Load, controller.State.Failure); Assert.NotNull(controller.State.Error); Assert.Null(controller.State.Snapshot); Assert.False(controller.CanWrite);
        fail = false; await controller.ReloadAsync(); Assert.True(controller.CanWrite); Assert.Null(controller.State.Error);
    }

    [Fact]
    public async Task ASlowEarlierReadCannotOverwriteALaterPageSelection()
    {
        var kit = AdminMonitorKit.Create(); using var _k = kit; using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var server = new AdminHomeSectionsFake().AddRow("a", "Home row").AddRow("la", "Library row", scope: "library", library: "7"); kit.Http.Respond = server.HandleAsync;
        var gate = new TaskCompletionSource(); var homeReads = 0;
        server.Intercept = (request, _) => null;
        kit.Http.Respond = async (request, ct) =>
        {
            if (request.RequestUri!.Query.Contains("scope=home") && request.RequestUri.AbsolutePath == "/api/v2/admin/sections" && Interlocked.Increment(ref homeReads) == 1) await gate.Task;
            return await server.HandleAsync(request, ct);
        };
        using var controller = new AdminHomeSectionsController(new(kit.Client), authority);
        var slow = controller.OpenAsync(); await Task.Delay(100);
        await controller.OpenAsync(new("library", "7")); gate.SetResult(); await slow;
        Assert.Equal("library", controller.State.Snapshot!.Scope.Kind); Assert.Equal(new[] { "la" }, controller.State.Snapshot.Rows.Select(row => row.Id));
    }
}
