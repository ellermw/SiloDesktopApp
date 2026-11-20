using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed partial class AdminAutoscanActivityTests
{
    internal const string Scan = """{"id":"scan-1","library_id":"7","mode":"file","trigger":"autoscan","status":"completed"}""";
    internal const string Poll = """{"id":"11","plugin_id":"synthetic","capability_id":"files","started_at":"2026-11-17T12:00:00Z","completed_at":"2026-11-17T12:00:01Z","duration_ms":1000,"status":"success","delivery_mode":"webhook","changes_returned":1,"changes_resolved":1,"targets_claimed":1,"scans_created":1,"scans_reused":0,"scans_suppressed":0,"scan_runs":[],"changes":[{"source_path":"/incoming/movie.mkv","rewritten_path":"/synthetic/movie.mkv","outcome":"future_outcome","reason":"future_reason","library_id":"7"}],"changes_truncated":false}""";
    internal static string Page(string row, string? next = null) =>
        "{\"items\":[" + row + "],\"total\":75,\"page\":{\"has_more\":" + (next == null ? "false" : "true") + (next == null ? "" : ",\"next_cursor\":\"" + next + "\"") + "}}";

    [Fact]
    public async Task ExactSchemasPreserveStringIdentitiesAndAdditiveChangeOutcomes()
    {
        using var harness = new Harness(); using var lease = harness.Authority.Capture();
        var scans = await harness.Api.ReadScansAsync(lease, new());
        var polls = await harness.Api.ReadEventsAsync(lease, new());
        Assert.Equal("7", Assert.Single(scans.Items).LibraryId);
        var change = Assert.Single(Assert.Single(polls.Items).Changes!);
        Assert.Equal("future_outcome", change.Outcome); Assert.Equal("future_reason", change.Reason);
        Assert.Equal("/synthetic/movie.mkv", change.RewrittenPath);
    }

    [Theory]
    [InlineData("{\"items\":[],\"total\":0}")]
    [InlineData("{\"items\":[],\"total\":0,\"page\":{}}")]
    [InlineData("{\"items\":[],\"total\":-1,\"page\":{\"has_more\":false}}")]
    [InlineData("{\"items\":[],\"total\":0,\"page\":{\"has_more\":true}}")]
    [InlineData("{\"items\":null,\"total\":0,\"page\":{\"has_more\":false}}")]
    public async Task MalformedPageIsRejected(string json)
    {
        using var harness = new Harness(); harness.Wire.Reply = _ => Task.FromResult(Json(json));
        using var lease = harness.Authority.Capture();
        await Assert.ThrowsAnyAsync<Exception>(() => harness.Api.ReadScansAsync(lease, new()));
    }

    [Theory]
    [InlineData("\"library_id\":\"7\"", "\"library_id\":\"07\"")]
    [InlineData("\"library_id\":\"7\"", "\"library_id\":\"9007199254740992\"")]
    [InlineData("\"status\":\"completed\"", "\"status\":\"future\"")]
    [InlineData("\"mode\":\"file\"", "\"mode\":\"future\"")]
    public async Task InvalidScanIdentityOrStateIsRejected(string before, string after)
    {
        using var harness = new Harness(); harness.Wire.Reply = _ => Task.FromResult(Json(Page(Scan.Replace(before, after))));
        using var lease = harness.Authority.Capture();
        await Assert.ThrowsAnyAsync<Exception>(() => harness.Api.ReadScansAsync(lease, new()));
    }

    [Fact]
    public async Task DuplicateOversizedAndSelfContinuingPagesAreRejected()
    {
        using var harness = new Harness(); using var lease = harness.Authority.Capture();
        foreach (var json in new[] { Page(Scan + "," + Scan), Page(string.Join(',', Enumerable.Repeat(Scan, 26))), Page(Scan, "same") })
        {
            harness.Wire.Reply = _ => Task.FromResult(Json(json));
            await Assert.ThrowsAnyAsync<Exception>(() => harness.Api.ReadScansAsync(lease, new(), "same"));
        }
    }

    internal static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    [Fact]
    public void ExactLabelsPluralizationAndResultsDoNotPresentMissingOutcomesAsNoChanges()
    {
        Assert.Equal("1 path", AdminAutoscanActivityText.Count("details.pathCount", 1));
        Assert.Equal("2 paths", AdminAutoscanActivityText.Count("details.pathCount", 2));
        Assert.Equal("Showing 1–25 of 75 scans", AdminAutoscanActivityText.T("web.admin.common.pagination.scans", new { from = 1, to = 25, total = 75, count = 75 }));
        Assert.Equal("-", AdminAutoscanActivityText.Result(null));
        Assert.Contains("overlapping", AdminAutoscanActivityText.Result(new() { Skipped = 1 }));
        Assert.Contains("storage offline", AdminAutoscanActivityText.Result(new() { MissingSkippedProtected = 1 }));
        Assert.DoesNotContain("synthetic-secret", AdminAutoscanActivityText.Safe("request failed https://fixture.invalid/path?signature=synthetic-secret"));
    }

    [Fact]
    public void ResultPartsFollowTheOfficialOrderAndReasonsAreKnown()
    {
        var all = AdminAutoscanActivityText.Result(new() { New = 1, Updated = 2, Missing = 3, FilesDeleted = 4, ItemsDeleted = 5, MembershipsRemoved = 6, MissingSkippedProtected = 7, Errors = 8 });
        string[] order = ["1 new", "2 updated", "3 missing", "4 files removed", "5 items removed", "6 titles removed from library", "7 kept (storage offline)", "8 errors"];
        Assert.Equal(string.Join(" · ", order), all);
        Assert.Equal("The library's ignore files exclude this file, so it was not scanned.", AdminAutoscanActivityText.ReasonText("path_ignored"));
        Assert.Null(AdminAutoscanActivityText.ReasonText("future_reason"));
        Assert.Equal("12 changes", AdminAutoscanActivityText.RawCount("metrics.changes", 12));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("9007199254740992")]
    public void NumericIdentitiesMatchOfficialJavascriptSafeIntegerBoundary(string value) => Assert.False(AdminAutoscanActivityApi.NumericId(value));

    [Theory]
    [InlineData("\"opaque\"")]
    [InlineData("\"9007199254740992\"")]
    [InlineData("12")]
    public async Task RunningPollIdentityUsesThePublishedDecimalStringSchema(string identity)
    {
        using var harness = new Harness(); using var lease = harness.Authority.Capture();
        harness.Wire.Reply = _ => Task.FromResult(Json("{\"enabled\":true,\"active_scans\":0,\"accepted_scans\":0,\"running_scans\":0,\"sources\":[],\"running_polls\":[{\"id\":" + identity + ",\"elapsed_ms\":0}]}"));
        await Assert.ThrowsAnyAsync<Exception>(() => harness.Api.ReadStatusAsync(lease));
    }

    [Fact]
    public void HistoryRequestsUseOnlyOfficialFilterAndCursorParameters()
    {
        var path = AdminAutoscanActivityApi.HistoryPath(AdminAutoscanHistory.Polls, new("scan-1", "success", 25), "signed-cursor");
        Assert.Contains("q=scan-1", path); Assert.DoesNotContain("source_id", path);
        Assert.Contains("cursor=signed-cursor", path); Assert.Contains("status=success", path);
        Assert.DoesNotContain("sort=", path); Assert.DoesNotContain("offset=", path);
    }

    [Fact]
    public void SourceNamesUseExplicitLabelsWebhookProviderAndPollingConnections()
    {
        var source = new AdminAutoscanActivitySource { Id = "source-1", PluginId = "synthetic", CapabilityId = "files", ConnectionId = "connection-1" };
        var labels = new AdminAutoscanActivityLabels(new Dictionary<string, string>(), new Dictionary<string, AdminAutoscanActivitySource> { [source.Id] = source },
            new Dictionary<string, string> { ["connection-1"] = "Synthetic Host" }, new Dictionary<string, string> { ["synthetic:files"] = "Synthetic Files" });
        Assert.Equal("Synthetic Files · via Synthetic Host", AdminAutoscanActivityText.Source(source.Id, source.PluginId, source.CapabilityId, labels));
        source.DeliveryMode = "webhook"; source.SourceConfig = new() { ["webhook_provider"] = "radarr" };
        Assert.Equal("Radarr", AdminAutoscanActivityText.Source(source.Id, source.PluginId, source.CapabilityId, labels));
        source.Label = "  Explicit Label  ";
        Assert.Equal("Explicit Label", AdminAutoscanActivityText.Source(source.Id, source.PluginId, source.CapabilityId, labels));
        Assert.Equal("Synthetic Files", AdminAutoscanActivityText.Source("deleted-source", source.PluginId, source.CapabilityId, labels));
        Assert.Equal("synthetic", AdminAutoscanActivityText.Source(null, "synthetic", null, labels, true));
        Assert.Equal("files", AdminAutoscanActivityText.Source(null, null, "files", labels, true));
    }
}
