using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminMonitorLogsStateTests
{
    [Theory]
    [InlineData("192.0.2.1/32", "192.0.2.1")]
    [InlineData("2001:db8::1/128", "2001:db8::1")]
    [InlineData("proxy/not-a-mask", "proxy/not-a-mask")]
    [InlineData("", "-")]
    public void ClientAddressOnlyRemovesNumericMaskSuffix(string value, string expected)
        => Assert.Equal(expected, LogsPresentation.ClientIp(value));

    [Fact]
    public void LiveRowsAreDeduplicatedNewestFirstAndBounded()
    {
        var buffer = new LogsLiveBuffer(3);
        buffer.Snapshot([Entry("3"), Entry("2"), Entry("2"), Entry("1")]);
        buffer.Append(Entry("2") with { Message = "updated" });
        buffer.Append(Entry("4"));
        Assert.Equal(["4", "2", "3"], buffer.Rows.Select(row => row.Id));
        Assert.Equal("updated", buffer.Rows[1].Message);
    }

    [Fact]
    public void FramesRespectStreamAndRejectLossyBridgeIdentifiers()
    {
        Assert.Null(LogsSocketFrame.Parse("""{"type":"append","stream":"audit","entry":{"id":1}}""", "app"));
        Assert.Null(LogsSocketFrame.Parse("""{"type":"append","stream":"app","entry":{"id":9007199254740992}}""", "app"));
        var frame = LogsSocketFrame.Parse("""{"type":"append","stream":"app","entry":{"id":42,"user_id":12,"timestamp":"2026-11-14T12:00:00Z","level":"info","component":"scanner","message":"Scanning"}}""", "app");
        Assert.Equal("42", frame!.Entry!.Id);
        Assert.Equal("12", frame.Entry.UserId);
    }

    [Fact]
    public void FiltersOmitAllSentinelsAndNeverLeakAppFiltersToAudit()
    {
        var filters = new LogsQuery { Level = "all", Component = "all", Message = "needle & value", RequestId = "r/1", Method = "post", Action = "user.updated" };
        Assert.Equal("?request_id=r%2F1&q=needle%20%26%20value&limit=50", filters.ToQuery("app", limit: 50));
        Assert.Equal("?request_id=r%2F1&method=POST&action=user.updated&limit=50", filters.ToQuery("audit", limit: 50));
    }

    [Fact]
    public void PlaybackSummaryCountsFetchedMatchingRowsOnly()
    {
        var rows = new[] { Entry("3") with { PlaybackSessionId = "p", Component = "ffmpeg", NodeId = "node-a" }, Entry("2") with { PlaybackSessionId = "other" }, Entry("1") with { PlaybackSessionId = "p" } };
        var summary = LogsPlaybackSummary.From("p", rows, []);
        Assert.Equal(2, summary.ApplicationCount);
        Assert.Equal(1, summary.FFmpegCount);
        Assert.Equal(["node-a"], summary.Nodes);
    }

    [Theory]
    [InlineData("profile", "pin", "true", "true", "changed")]
    [InlineData("profile", "pin", "true", "false", "cleared")]
    [InlineData("access_group", "library_ids", null, "null", "absent → all libraries")]
    [InlineData("api_key", "scopes", "[]", "[\"read\"]", "full access → read")]
    [InlineData("profile", "pin", null, "true", "absent → set")]
    [InlineData("user", "max_streams", "{ \"a\": 1 }", "true", "{\"a\":1} → true")]
    public void AuditChangesPreserveAbsentNullAndPinSemantics(string target, string field, string? before, string? after, string expected)
    {
        Assert.Equal(expected, LogsPresentation.ChangeSummary(new() { Field = field, Before = before, After = after }, target));
    }

    [Fact]
    public void CursorHistoryStartsFreshAndRejectsReusedContinuation()
    {
        var history = new LogsCursorHistory();
        history.Start();
        Assert.Null(history.Cursor);
        Assert.Equal(1, history.Page);
        history.Older("opaque");
        Assert.Throws<InvalidDataException>(() => history.Older("opaque"));
        Assert.Equal(2, history.Page);
        history.Newer();
        Assert.Null(history.Cursor);
        history.Clear();
        Assert.False(history.IsHistory);
    }

    [Fact]
    public void DiagnosticFilterUsesExplicitApplyAndNormalizesIdentityAndDates()
    {
        var draft = new DiagnosticsQuery { UserId = " 12 ", Platform = "all", ShortId = " ab-c ", From = "2026-11-14T10:00:00-06:00" };
        var applied = draft.Apply();
        Assert.Equal("?user_id=12&from=2026-11-14T16%3A00%3A00.0000000%2B00%3A00&short_id=ab-c&limit=25", applied.ToQuery());
        Assert.Throws<ArgumentException>(() => (draft with { UserId = "1e2" }).Apply());
        Assert.Throws<ArgumentException>(() => (draft with { From = "invalid" }).Apply());
        Assert.Equal("Unknown Platform", DiagnosticsPresentation.Platform("unknown-platform"));
    }

    private static LogsOperationalEntry Entry(string id) => new() { Id = id, Timestamp = DateTimeOffset.Parse("2026-11-14T12:00:00Z"), Level = "info", Component = "scanner", Message = "Log " + id };
}
