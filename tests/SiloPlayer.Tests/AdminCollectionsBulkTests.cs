using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The delete paths the Collections page's confirmations rely on: what each refusal does, that an unknown outcome stops the next change
/// and says why, and that a validator changed after the confirmation refuses that collection only. Contracts: official Silo Server/WebUI at 02a9d1d3e.
/// </summary>
public sealed class AdminCollectionsBulkTests
{
    private static HttpResponseMessage Collection(string id, string tag) => AdminCollectionsTests.Tagged($"{{\"id\":\"{id}\",\"title\":\"Title {id}\",\"collection_type\":\"manual\",\"visibility\":\"visible\"}}", tag);

    private static HttpResponseMessage Problem(HttpStatusCode status) => AdminMonitorKit.Json($"{{\"type\":\"conflict\",\"title\":\"Refused\",\"status\":{(int)status}}}", status);

    // A single delete the server refuses: the admin sees why and may retry. Nothing is replayed.
    [Theory]
    [InlineData(HttpStatusCode.Conflict, "The collection changed or is used by a section. Reload and review it.")]
    [InlineData(HttpStatusCode.PreconditionFailed, "It changed since you opened this. Reload and review before trying again.")]
    public async Task ASingleDeleteRefusalIsDefiniteSendsOnceAndIsNotAnUnknownOutcome(HttpStatusCode status, string message)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        kit.Http.Respond = (r, _) => Task.FromResult(r.Method == HttpMethod.Delete ? Problem(status) : Collection("c", "\"r1\""));
        var api = new AdminCollectionsApi(kit.Client);
        var plan = await api.PrepareDeleteAsync(controller.Owner!, "c", false);
        var error = await Assert.ThrowsAsync<ApiException>(() => controller.CommandAsync(async lease => { await api.DeleteAsync(lease, plan, false); return true; }));
        Assert.Equal((int)status, error.StatusCode);
        Assert.Equal(message, AdminCollectionsController.Message(error));
        Assert.False(controller.OutcomeUnknown, "a definite refusal does not block the next change");
        Assert.Single(kit.Http.Requests, request => request == "DELETE /api/v2/admin/collections/c");
    }

    // Bulk delete: a 409 is kept, a 404 counts as deleted, a 412 and anything else is a failure. The classification and the sentence are the official ones.
    [Theory]
    [InlineData(HttpStatusCode.Conflict, AdminBulkDeleteOutcome.Kept, false)]
    [InlineData(HttpStatusCode.NotFound, AdminBulkDeleteOutcome.Deleted, false)]
    [InlineData(HttpStatusCode.NotFound, AdminBulkDeleteOutcome.Deleted, true)]   // the first read of the collection finds it already gone
    [InlineData(HttpStatusCode.PreconditionFailed, AdminBulkDeleteOutcome.Failed, false)]
    public async Task ABulkDeleteClassifiesWhatTheServerAnswers(HttpStatusCode status, AdminBulkDeleteOutcome expected, bool onTheRead)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var planned = false;
        kit.Http.Respond = (r, _) => Task.FromResult(
            r.Method == HttpMethod.Delete && !onTheRead ? Problem(status)
            : r.Method == HttpMethod.Get && onTheRead && planned ? Problem(status)
            : Collection("c", "\"r1\""));
        var api = new AdminCollectionsApi(kit.Client);
        var plan = await api.PrepareDeleteAsync(owner, "c", false); planned = true;
        var error = await Assert.ThrowsAnyAsync<ApiException>(() => api.DeleteAsync(owner, plan, false));
        Assert.Equal(expected, AdminCollectionBulk.ClassifyDeleteFailure(error));
        var deleted = expected == AdminBulkDeleteOutcome.Deleted ? 1 : 0; var kept = expected == AdminBulkDeleteOutcome.Kept ? 1 : 0;
        var failures = expected == AdminBulkDeleteOutcome.Failed ? new[] { AdminCollectionCopy.FailureLine("Title c", AdminCollectionsController.Message(error)) } : [];
        var text = AdminCollectionCopy.BulkDeleteResult(1, deleted, kept, failures);
        Assert.Equal(expected switch
        {
            AdminBulkDeleteOutcome.Deleted => "Deleted 1 collection.",
            AdminBulkDeleteOutcome.Kept => "Deleted 0 collections. Kept 1 collection in use by home or library sections.",
            _ => "Failed to delete 1 collection. Title c: It changed since you opened this. Reload and review before trying again.",
        }, text);
    }

    // The validators a bulk delete read are per collection. One that changed after the confirmation is refused; the others are deleted.
    [Fact]
    public async Task AValidatorThatChangedAfterTheConfirmationRefusesThatCollectionOnly()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var tags = new Dictionary<string, string> { ["a"] = "\"a1\"", ["b"] = "\"b1\"", ["c"] = "\"c1\"" };
        var deleted = new List<string>();
        kit.Http.Respond = (r, _) =>
        {
            var id = r.RequestUri!.AbsolutePath.Split('/')[^1];
            if (r.Method != HttpMethod.Delete) return Task.FromResult(Collection(id, tags[id]));
            Assert.Equal(tags[id], r.Headers.GetValues("If-Match").Single());
            deleted.Add(id); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        };
        var api = new AdminCollectionsApi(kit.Client);
        var plans = new List<AdminCollectionDeletePlan>();
        foreach (var id in tags.Keys) plans.Add(await api.PrepareDeleteAsync(owner, id, false));
        tags["b"] = "\"b2\""; // another administrator changed b after the confirmation named it
        var done = 0; var kept = 0; var failures = new List<string>();
        foreach (var plan in plans)
        {
            try { await api.DeleteAsync(owner, plan, false); done++; }
            catch (Exception error)
            {
                var outcome = AdminCollectionBulk.ClassifyDeleteFailure(error);
                if (outcome == AdminBulkDeleteOutcome.Deleted) done++; else if (outcome == AdminBulkDeleteOutcome.Kept) kept++;
                else failures.Add(AdminCollectionCopy.FailureLine(plan.Collection.Value["title"]?.ToString(), AdminCollectionsController.Message(error)));
            }
        }
        Assert.Equal(["a", "c"], deleted);
        Assert.Equal("Deleted 2 of 3 collections. Title b: It changed since you opened this. Reload and review before trying again.", AdminCollectionCopy.BulkDeleteResult(3, done, kept, failures));
    }

    // An unknown outcome blocks the next change, whichever page action asks for it, and the refusal says why. The change is never run.
    [Fact]
    public async Task AGateRefusalAfterAnUnknownOutcomeRunsNothingAndNamesTheReason()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminCollectionsController(new(kit.Client), new(kit.Client), authority); controller.Open();
        await Assert.ThrowsAsync<HttpRequestException>(() => controller.CommandAsync<bool>(_ => throw new HttpRequestException("lost reply")));
        Assert.True(controller.OutcomeUnknown);
        var ran = 0;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.CommandAsync(_ => { ran++; return Task.FromResult(true); }));
            // The page shows exactly this sentence and does not write a result over it.
            Assert.Equal("The outcome is unknown. Reload and review before making another change.", AdminCollectionsController.Message(refusal));
        }
        Assert.Equal(0, ran);
    }
}
