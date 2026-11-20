using System.Net;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

// Authored before implementation. Source-only lane: these tests have NOT run.
public sealed class AdminCollectionsTests
{
    internal static HttpResponseMessage Tagged(string json, string tag = "\"r1\"")
    {
        var response = AdminMonitorKit.Json(json);
        response.Headers.TryAddWithoutValidation("ETag", tag);
        return response;
    }

    [Fact]
    public async Task BoardRejectsMixedLibraryRevisionsAndNeverWrites()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var reads = 0;
        kit.Http.Respond = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("collection-groups/order")
            ? Tagged("{\"ordered_ids\":[\"g\",\"ungrouped\"],\"has_more\":false}", ++reads == 1 ? "\"a\"" : "\"b\"")
            : r.RequestUri.AbsolutePath.EndsWith("/collection-groups") ? Tagged("{\"items\":[{\"id\":\"g\",\"kind\":\"regular\"}]}")
            : r.RequestUri.AbsolutePath == "/api/v2/admin/collections" ? Tagged("{\"items\":[],\"groups\":[]}")
            : Tagged("{\"ordered_ids\":[],\"has_more\":false}"));
        await Assert.ThrowsAsync<AdminCollectionChangedException>(() => new AdminCollectionsApi(kit.Client).BoardAsync(lease, "1"));
        Assert.All(kit.Http.Requests, r => Assert.StartsWith("GET ", r));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("W/\"r1\"")]
    [InlineData("")]
    public async Task EditorRequiresStrongValidator(string tag)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged("{\"id\":\"c\",\"title\":\"Test\"}", tag));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminCollectionsApi(kit.Client).GetAsync(lease, "c"));
    }

    [Fact]
    public async Task GuardedSaveSendsOnceAndDoesNotRefreshOnUnauthorized()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (r, _) =>
        {
            if (r.Method == HttpMethod.Get) return Task.FromResult(Tagged("{\"id\":\"c\",\"title\":\"Test\"}"));
            Assert.Equal("\"r1\"", r.Headers.GetValues("If-Match").Single());
            return Task.FromResult(AdminMonitorKit.Json("{\"type\":\"unauthorized\",\"title\":\"Expired\",\"status\":401}", HttpStatusCode.Unauthorized));
        };
        var api = new AdminCollectionsApi(kit.Client);
        var snapshot = await api.GetAsync(lease, "c");
        await Assert.ThrowsAsync<ApiException>(() => api.SaveAsync(lease, snapshot, new JsonObject { ["title"] = "Changed" }));
        Assert.Equal(2, kit.Http.Requests.Count);
    }

    [Fact]
    public async Task PartialItemOrderCannotReplaceMembership()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged("{\"ordered_ids\":[\"m\"],\"has_more\":true}"));
        var api = new AdminCollectionsApi(kit.Client);
        var order = await api.ItemOrderAsync(lease, "c");
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.ReorderItemsAsync(lease, "c", order, ["m"]));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task CollectionChangeBeforeDeleteRemovesNoRows()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var reads = 0;
        kit.Http.Respond = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("/sections")
            ? Tagged("{\"items\":[{\"id\":\"s\",\"title\":\"Row\"}]}")
            : Tagged("{\"id\":\"c\",\"title\":\"Test\"}", ++reads == 1 ? "\"a\"" : "\"b\""));
        var api = new AdminCollectionsApi(kit.Client);
        var plan = await api.PrepareDeleteAsync(lease, "c", true);
        await Assert.ThrowsAsync<AdminCollectionChangedException>(() => api.DeleteAsync(lease, plan, true));
        Assert.All(kit.Http.Requests, r => Assert.StartsWith("GET ", r));
    }

    [Fact]
    public async Task SharedRowKeepsOtherReferencesAndUnrelatedConfig()
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var lease = authority.Capture();
        var collectionReads = 0;
        kit.Http.Respond = async (r, ct) =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (r.Method == HttpMethod.Patch)
            {
                Assert.Equal("\"s1\"", r.Headers.GetValues("If-Match").Single());
                var body = JsonNode.Parse(await r.Content!.ReadAsStringAsync(ct))!;
                Assert.Equal("keep", body["config"]!["marker"]!.GetValue<string>());
                Assert.Equal("other", body["config"]!["collections"]![0]!["library_collection_id"]!.GetValue<string>());
                Assert.Single(body["config"]!["collections"]!.AsArray());
                return Tagged("{\"id\":\"s\"}");
            }
            if (r.Method == HttpMethod.Delete)
            {
                Assert.Equal("/api/v2/admin/collections/c", path);
                Assert.Equal("\"r2\"", r.Headers.GetValues("If-Match").Single());
                return new(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/sections")) return Tagged("{\"items\":[{\"id\":\"s\",\"title\":\"Shared row\"}]}");
            if (path.Contains("/sections/")) return Tagged("{\"id\":\"s\",\"section_type\":\"collection_posters\",\"config\":{\"marker\":\"keep\",\"collections\":[{\"library_collection_id\":\"c\"},{\"library_collection_id\":\"other\"}]}}", "\"s1\"");
            return Tagged("{\"id\":\"c\",\"title\":\"Test\"}", ++collectionReads < 3 ? "\"r1\"" : "\"r2\"");
        };
        var api = new AdminCollectionsApi(kit.Client);
        await api.DeleteAsync(lease, await api.PrepareDeleteAsync(lease, "c", true), true);
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("PATCH ")));
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("DELETE ")));
    }

    [Fact]
    public async Task RetiredEditorCannotWriteAfterPermissionChanges()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged("{\"id\":\"c\",\"title\":\"Test\"}"));
        var api = new AdminCollectionsApi(kit.Client); var observed = await api.GetAsync(owner, "c");
        kit.SetRole("user"); kit.SetRole("admin"); using var successor = authority.Capture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.SaveAsync(successor, observed, new() { ["title"] = "Forbidden stale draft" }));
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task LostCreateReplyRequiresReconciliationAndIsNotReplayed()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.EditAsync(null, "manual", "1"); controller.Draft!["title"] = "Unknown fixture";
        kit.Http.Respond = (_, _) => throw new HttpRequestException("Synthetic lost reply");
        await Assert.ThrowsAsync<HttpRequestException>(() => controller.SaveAsync());
        Assert.True(controller.OutcomeUnknown);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SaveAsync());
        Assert.Single(kit.Http.Requests);
    }

    [Fact]
    public async Task ConfirmedCreateWithFailedCanonicalReadDoesNotCreateAgain()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await controller.EditAsync(null, "manual", "1"); controller.Draft!["title"] = "Known fixture";
        var reads = 0;
        kit.Http.Respond = (r, _) =>
        {
            if (r.Method == HttpMethod.Post) return Task.FromResult(Tagged("{\"id\":\"known\",\"title\":\"Known fixture\"}"));
            if (++reads == 1) throw new HttpRequestException("Synthetic read failure");
            return Task.FromResult(Tagged("{\"id\":\"known\",\"title\":\"Known fixture\",\"library_ids\":[\"1\"],\"collection_type\":\"manual\"}"));
        };
        await Assert.ThrowsAsync<HttpRequestException>(() => controller.SaveAsync());
        Assert.Equal("known", await controller.SaveAsync());
        Assert.Single(kit.Http.Requests.Where(r => r.StartsWith("POST ")));
    }

    [Fact]
    public void RawQueryLibraryIdsRemainNumbersWhileV2SelectionUsesStrings()
    {
        var outer = new JsonArray("1", "2");
        var inner = AdminCollectionsApi.RuleLibraryIds(outer);
        Assert.Equal(1, inner[0]!.GetValue<int>());
        Assert.Equal("1", outer[0]!.GetValue<string>());
    }

    [Fact]
    public async Task RowConflictStopsBeforeCollectionDelete()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (r, _) => Task.FromResult(r.Method == HttpMethod.Delete
            ? AdminMonitorKit.Json("{\"type\":\"conflict\",\"status\":412}", HttpStatusCode.PreconditionFailed)
            : r.RequestUri!.AbsolutePath.EndsWith("/sections") ? Tagged("{\"items\":[{\"id\":\"s\"}]}")
            : r.RequestUri.AbsolutePath.Contains("/sections/") ? Tagged("{\"id\":\"s\",\"section_type\":\"collection\",\"config\":{\"library_collection_id\":\"c\"}}")
            : Tagged("{\"id\":\"c\",\"title\":\"Test\"}"));
        var api = new AdminCollectionsApi(kit.Client); var plan = await api.PrepareDeleteAsync(owner, "c", true);
        await Assert.ThrowsAsync<ApiException>(() => api.DeleteAsync(owner, plan, true));
        Assert.DoesNotContain("DELETE /api/v2/admin/collections/c", kit.Http.Requests);
    }

    [Fact]
    public async Task ConflictReviewAdoptsFreshValidatorAndOnlyChosenLocalFieldsWithoutWriting()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        var version = 1;
        kit.Http.Respond = (_, _) => Task.FromResult(Tagged(version == 1
            ? "{\"id\":\"c\",\"title\":\"Base\",\"description\":\"Base description\",\"library_ids\":[\"1\"]}"
            : "{\"id\":\"c\",\"title\":\"Remote\",\"description\":\"Remote description\",\"library_ids\":[\"1\"]}", $"\"r{version}\""));
        await controller.EditAsync("c"); controller.Draft!["title"] = "Local"; controller.Draft["description"] = "Local description";
        version = 2;
        var latest = await new AdminCollectionsApi(kit.Client).GetAsync(controller.Owner!, "c");
        controller.Rebase(latest, new HashSet<string> { "title" });
        Assert.Equal("Local", controller.Draft!["title"]!.ToString());
        Assert.Equal("Remote description", controller.Draft["description"]!.ToString());
        Assert.Equal("\"r2\"", controller.Editor!.ETag); Assert.True(controller.Dirty);
        Assert.All(kit.Http.Requests, r => Assert.StartsWith("GET ", r));
    }

    [Fact]
    public async Task SupersededReadIsCanceledAndCannotReplaceLatestList()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var capabilityReads = 0; CancellationToken retiredToken = default;
        kit.Http.Respond = (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/capabilities"))
            {
                if (++capabilityReads == 1) { retiredToken = token; started.TrySetResult(); return pending.Task; }
                return Task.FromResult(Tagged("{\"groups\":false}"));
            }
            return Task.FromResult(Tagged(request.RequestUri.AbsolutePath == "/api/v2/libraries" ? "{\"items\":[],\"page\":{\"has_more\":false}}" : "{\"items\":[{\"id\":\"latest\",\"title\":\"Latest\"}]}"));
        };
        var retired = controller.LoadAsync(null, false); await started.Task;
        await controller.LoadAsync(null, false); Assert.True(retiredToken.IsCancellationRequested);
        pending.SetResult(Tagged("{\"groups\":true}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retired);
        Assert.Equal("latest", Assert.Single(controller.List!.Items).Id);
    }

    [Fact]
    public async Task PublicArtworkNeverReceivesSiloAuthorization()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (request, _) =>
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        };
        var bytes = await new AdminCollectionsApi(kit.Client).ArtworkBytesAsync(owner, "https://assets.fixture.invalid/poster.png?signature=synthetic", default);
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
    }

    [Fact]
    public void ChartChoicesFollowOfficialMediaAndTimeWindowConditions()
    {
        var chart = new JsonObject { ["preset"] = "now_playing", ["media_type"] = "tv", ["time_window"] = "week", ["limit"] = 50 };
        AdminCollectionsController.NormalizeChart(chart);
        Assert.Equal("movie", chart["media_type"]!.ToString()); Assert.Null(chart["time_window"]); Assert.Equal(50, chart["limit"]!.GetValue<int>());
        chart["preset"] = "airing_today"; AdminCollectionsController.NormalizeChart(chart); Assert.Equal("tv", chart["media_type"]!.ToString());
        chart["preset"] = "trending"; chart["media_type"] = "all"; AdminCollectionsController.NormalizeChart(chart);
        Assert.Equal("all", chart["media_type"]!.ToString()); Assert.Equal("day", chart["time_window"]!.ToString());
    }
}
