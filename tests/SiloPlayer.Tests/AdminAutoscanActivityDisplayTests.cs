using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using D = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityDisplay;
using A = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityText;

namespace SiloPlayer.Tests;

/// <summary>
/// The poll inspector and table rules at official 88b825d57e4b2296050fda69eaf619eefbf208d1 (<c>PollEventTable.test.tsx</c>,
/// <c>pollEventDisplay.ts</c>, <c>pagination.utils.ts</c>), all with synthetic values.
/// </summary>
public sealed class AdminAutoscanActivityDisplayTests
{
    private static AdminAutoscanActivityLabels Labels => new(new Dictionary<string, string> { ["1"] = "Synthetic Movies", ["2"] = "Synthetic Shows" },
        new Dictionary<string, AdminAutoscanActivitySource>(), new Dictionary<string, string>(), new Dictionary<string, string> { ["synthetic:files"] = "Synthetic Files" });

    private static AdminAutoscanActivityRun Run(int index, string status = "completed", string? error = null) => new()
    {
        Id = "01RUN" + (index + 1).ToString("000"), LibraryId = index % 2 == 0 ? "1" : "2", Mode = "subtree", Path = "/synthetic/movies/title-" + (index + 1).ToString("000"),
        Trigger = "autoscan", Status = status, ErrorMessage = error, CompletedAt = DateTimeOffset.Parse("2026-11-20T02:00:05Z"), Result = new() { New = 1 },
    };

    private static AdminAutoscanActivityChange Change(int index, string outcome = "queued", string? reason = null, string? runId = "", string? library = "1") => new()
    {
        SourcePath = "/synthetic/incoming/movie-" + (index + 1).ToString("000") + ".mkv", RewrittenPath = "/synthetic/movies/title-" + (index + 1).ToString("000") + "/movie.mkv",
        Outcome = outcome, Reason = reason, LibraryId = library, TargetMode = "subtree", TargetPath = "/synthetic/movies/title-" + (index + 1).ToString("000"),
        ScanRunId = runId == "" ? "01RUN" + (index + 1).ToString("000") : runId,
    };

    private static AdminAutoscanActivityEvent Event(IEnumerable<AdminAutoscanActivityChange>? changes = null, IEnumerable<AdminAutoscanActivityRun>? runs = null, string status = "success") => new()
    {
        Id = "42", PluginId = "synthetic", CapabilityId = "files", StartedAt = DateTimeOffset.Parse("2026-11-20T02:00:00Z"), CompletedAt = DateTimeOffset.Parse("2026-11-20T02:00:02Z"),
        DurationMs = 2000, Status = status, ChangesReturned = 2, ChangesResolved = 2, TargetsClaimed = 2, ScansCreated = 2,
        ScanRuns = (runs ?? [Run(0), Run(1)]).ToList(), Changes = (changes ?? [Change(0), Change(1)]).ToList(),
    };

    [Fact]
    public void PathsSplitIntoAParentPrefixAndAnEmphasizedLeaf()
    {
        Assert.Equal(("/synthetic/media/Show/", "ep01.mkv"), D.SplitDisplayPath("/synthetic/media/Show/ep01.mkv"));
        Assert.Equal(("/synthetic/media/", "Season 01/"), D.SplitDisplayPath("/synthetic/media/Season 01/"));
        Assert.Equal(("D:\\Media\\", "Movie.mkv"), D.SplitDisplayPath("D:\\Media\\Movie.mkv"));
        Assert.Equal(("", "movie.mkv"), D.SplitDisplayPath("movie.mkv"));
        Assert.Equal(("", "/"), D.SplitDisplayPath("/"));
        Assert.Equal(("", ""), D.SplitDisplayPath(null));
    }

    [Fact]
    public void PageWindowAnchorsFirstLastAndCurrentWithEllipsisGaps()
    {
        Assert.Equal(new int?[] { 0, 1, 2 }, D.PageWindow(1, 3));
        Assert.Equal(new int?[] { 0, 1, 2, 3, 4, 5, 6 }, D.PageWindow(6, 7));
        Assert.Equal(new int?[] { 0, null, 4, 5, 6, null, 19 }, D.PageWindow(5, 20));
        Assert.Equal(new int?[] { 0, 1, null, 19 }, D.PageWindow(0, 20));
        Assert.Equal(new int?[] { 0, null, 18, 19 }, D.PageWindow(19, 20));
        Assert.Equal(new int?[] { 0, 1, 2, 3, null, 19 }, D.PageWindow(2, 20));
        Assert.Empty(D.PageWindow(0, 0));
    }

    [Fact]
    public void ClientPagesClampAndSummarizeLikeTheOfficialTablePagination()
    {
        var runs = Enumerable.Range(0, 55).Select(index => Run(index)).ToArray();
        var first = D.Slice(runs, 0, 20);
        Assert.Equal(20, first.Rows.Count); Assert.Equal(3, first.PageCount);
        Assert.Equal("Showing 1–20 of 55 scans", A.T("web.admin.common.pagination.scans", new { from = first.From, to = first.To, total = first.Total, count = first.Total }));
        var last = D.Slice(runs, 99, 20);
        Assert.Equal(2, last.Page); Assert.Equal("01RUN055", last.Rows[^1].Id); Assert.Equal(41, last.From); Assert.Equal(55, last.To);
        var single = D.Slice(runs.Take(1).ToArray(), 0, 20);
        Assert.Equal("Showing 1–1 of 1 scan", A.T("web.admin.common.pagination.scans", new { from = single.From, to = single.To, total = single.Total, count = single.Total }));
        Assert.Equal(0, D.Slice(Array.Empty<AdminAutoscanActivityRun>(), 0, 20).To);
        Assert.Equal("Showing 1–15 of 40 items", A.T("web.ui.pagination.summary", new { from = 1, to = 15, total = 40, count = 40 }));
    }

    [Fact]
    public void TheInspectorOpensOnScansCreatedOnlyWhenNoPathsWereRecorded()
    {
        Assert.Equal("paths", D.InitialTab(Event()));
        Assert.Equal("scans", D.InitialTab(Event(changes: [], runs: [Run(0)])));
        Assert.Equal("paths", D.InitialTab(Event(changes: [], runs: [])));
    }

    [Fact]
    public void DetailsSummaryCountsLinkedRunsAndMarksATruncatedLogAsALowerBound()
    {
        var poll = Event(changes: [Change(0), Change(1, "joined", runId: "01OTHER"), Change(2, "joined", runId: "01OTHER"), Change(3, "joined", runId: "01RUN001")]);
        poll.ChangesReturned = 107; poll.ChangesTruncated = true;
        Assert.Equal(1, D.JoinedRunCount(poll));
        Assert.Equal("107 paths · 3+ linked", D.DetailsSummary(poll));
        var joinedOnly = Event(changes: [Change(1, "joined", runId: "01OTHER")], runs: []); joinedOnly.ChangesReturned = 1;
        Assert.Equal("1 path · 1 linked", D.DetailsSummary(joinedOnly));
        Assert.True(D.HasActivity(joinedOnly));
        var quiet = Event(changes: [], runs: []); quiet.ChangesReturned = 0;
        Assert.Equal("0 paths · 0 linked", D.DetailsSummary(quiet)); Assert.False(D.HasActivity(quiet));
        quiet.ScanRuns = [Run(0)]; Assert.True(D.HasActivity(quiet));
    }

    [Fact]
    public void OutcomeChipsFollowTheOfficialOrderWithUnknownOutcomesLast()
    {
        var counts = D.OutcomeCounts([Change(0, "error"), Change(1, "future"), Change(2, "queued"), Change(3, "ignored"), Change(4, "queued"), Change(5, "brand_new"), Change(6, "unresolved")]);
        Assert.Equal(["queued", "unresolved", "error", "ignored", "future", "brand_new"], counts.Select(entry => entry.Outcome));
        Assert.Equal(2, counts[0].Count);
        Assert.Equal(["running", "completed", "failed"], D.ScanStatusCounts([Run(0, "failed"), Run(1, "running"), Run(2, "completed"), Run(3, "running")]).Select(entry => entry.Status));
        Assert.Contains("future", D.OutcomeCounts([Change(0, "future")]).Select(entry => entry.Outcome));
        Assert.False(D.IsKnownOutcome("future")); Assert.True(D.IsKnownOutcome("joined"));
    }

    [Fact]
    public void PathFiltersMatchPathsRunsReasonsLibrariesAndOutcomes()
    {
        var changes = new[]
        {
            Change(0), Change(1, "unresolved", "no_library_match", null, null),
            Change(2, "ignored", "path_ignored", null, "2"),
        };
        Assert.Equal(["/synthetic/incoming/movie-002.mkv"], D.FilterChanges(changes, "unresolved", "", Labels).Select(change => change.SourcePath));
        Assert.Equal(3, D.FilterChanges(changes, "all", "  ", Labels).Count);
        Assert.Single(D.FilterChanges(changes, "all", "MOVIE-001", Labels));
        Assert.Single(D.FilterChanges(changes, "all", "01run001", Labels));
        Assert.Single(D.FilterChanges(changes, "all", "synthetic shows", Labels));
        Assert.Single(D.FilterChanges(changes, "all", "ignore files exclude", Labels));
        Assert.Single(D.FilterChanges(changes, "all", "no_library_match", Labels));
        Assert.Empty(D.FilterChanges(changes, "queued", "movie-002", Labels));
    }

    [Fact]
    public void ScanRunFiltersMatchStatusIdPathModeLibraryAndError()
    {
        var runs = Enumerable.Range(0, 5).Select(index => Run(index, index == 4 ? "failed" : "completed", index == 4 ? "synthetic storage root offline" : null)).ToArray();
        Assert.Equal(["01RUN005"], D.FilterRuns(runs, "failed", "", Labels).Select(run => run.Id));
        Assert.Equal(["01RUN002"], D.FilterRuns(runs, "all", "01RUN002", Labels).Select(run => run.Id));
        Assert.Equal(5, D.FilterRuns(runs, "all", "subtree scan", Labels).Count);
        Assert.Equal(3, D.FilterRuns(runs, "all", "synthetic movies", Labels).Count);
        Assert.Equal(["01RUN005"], D.FilterRuns(runs, "all", "offline", Labels).Select(run => run.Id));
        Assert.Empty(D.FilterRuns(runs, "running", "", Labels));
    }

    [Fact]
    public void ChangeExplanationsFollowTheOfficialBranchesAndTones()
    {
        var queued = D.ChangeExplanation(Change(0), Labels);
        Assert.Equal("New scan: Subtree scan · /synthetic/movies/title-001 in Synthetic Movies", queued.Text); Assert.Equal(AdminAutoscanTone.Muted, queued.Tone);
        var noLibrary = D.ChangeExplanation(Change(0, library: null), Labels);
        Assert.Equal("New scan: Subtree scan · /synthetic/movies/title-001", noLibrary.Text);
        var followUp = D.ChangeExplanation(Change(0, "joined", "follow_up_scan", null), Labels);
        Assert.StartsWith("A scan of this scope was already running, so it is scanned again when that scan finishes:", followUp.Text);
        Assert.StartsWith("Added to a scan that was already queued:", D.ChangeExplanation(Change(0, "joined"), Labels).Text);
        Assert.Equal("Already requested within the debounce window, so no new scan was requested (/synthetic/movies/title-001).", D.ChangeExplanation(Change(0, "suppressed"), Labels).Text);
        var bare = Change(0, "suppressed"); bare.TargetPath = null;
        Assert.Equal("Already requested within the debounce window, so no new scan was requested.", D.ChangeExplanation(bare, Labels).Text);
        var unresolved = D.ChangeExplanation(Change(0, "unresolved", "no_library_match"), Labels);
        Assert.Equal(AdminAutoscanTone.Warning, unresolved.Tone); Assert.EndsWith("(Synthetic Movies)", unresolved.Text); Assert.StartsWith("No Silo library folder contains this path.", unresolved.Text);
        var unknown = Change(0, "unresolved", "future_reason", null, null); unknown.Detail = "A newer server's detail";
        Assert.Equal("A newer server's detail", D.ChangeExplanation(unknown, Labels).Text);
        unknown.Detail = null; Assert.Equal("future_reason", D.ChangeExplanation(unknown, Labels).Text);
        unknown.Reason = null; Assert.Equal("This path was not scanned.", D.ChangeExplanation(unknown, Labels).Text);
        var failed = Change(0, "error", "resolve_failed", null, null); failed.Detail = "synthetic detail";
        Assert.Equal(("Resolving the path failed: synthetic detail.", AdminAutoscanTone.Danger), (D.ChangeExplanation(failed, Labels).Text, D.ChangeExplanation(failed, Labels).Tone));
        failed.Detail = null; Assert.Equal("Resolving the path failed.", D.ChangeExplanation(failed, Labels).Text);
        failed.Reason = "enqueue_failed"; Assert.Equal("The scan could not be queued.", D.ChangeExplanation(failed, Labels).Text);
        failed.Reason = null; Assert.Equal("Processing failed.", D.ChangeExplanation(failed, Labels).Text);
    }

    [Fact]
    public void ResultSummariesKeepTheOfficialTonesAndWordings()
    {
        Assert.Null(D.SummarizeResult(null));
        Assert.Equal(("Skipped — overlapping scan in progress", AdminAutoscanTone.Warning), (D.SummarizeResult(new() { Skipped = 1, New = 5 })!.Text, D.SummarizeResult(new() { Skipped = 1 })!.Tone));
        var changes = D.SummarizeResult(new() { New = 1, Updated = 2 })!; Assert.Equal("1 new · 2 updated", changes.Text); Assert.Equal(AdminAutoscanTone.Normal, changes.Tone);
        Assert.Equal(AdminAutoscanTone.Danger, D.SummarizeResult(new() { New = 1, Errors = 2 })!.Tone);
        var none = D.SummarizeResult(new() { Unchanged = 1234 })!; Assert.Equal("No changes · " + AdminText.FormatNumber(1234) + " unchanged", none.Text); Assert.Equal(AdminAutoscanTone.Muted, none.Tone);
        Assert.Equal("No changes", D.SummarizeResult(new())!.Text);
    }

    [Fact]
    public void RunningPollsCountUpFromTheirStartAndFinishedOnesShowTheirRecordedDuration()
    {
        var poll = Event(status: "running");
        Assert.Equal("1m 5s", D.EventDuration(poll, poll.StartedAt.AddSeconds(65)));
        Assert.Equal("0ms", D.EventDuration(poll, poll.StartedAt.AddSeconds(-5)));
        Assert.Equal(poll.StartedAt, D.EventTime(poll));
        poll.Status = "success"; Assert.Equal("2s", D.EventDuration(poll, poll.StartedAt.AddHours(1))); Assert.Equal(poll.CompletedAt, D.EventTime(poll));
        Assert.Equal("1.5s", A.Duration(1500)); Assert.Equal("999ms", A.Duration(999)); Assert.Equal("1m 0s", A.Duration(59_999 + 1));
    }

    [Fact]
    public void AReReadThatChangedNothingHasTheSameContentKeyAndEveryShownChangeHasAnother()
    {
        // A refresh deserializes the rows again; the page keeps its controls mounted only when these keys say nothing changed.
        var key = D.ContentKey(Event());
        Assert.Equal(key, D.ContentKey(Event()));
        Assert.Equal(D.RowsKey(new[] { Event(), Event(status: "running") }), D.RowsKey(new[] { Event(), Event(status: "running") }));
        Assert.NotEqual(D.RowsKey(new[] { Event() }), D.RowsKey(new[] { Event(), Event() }));
        var edits = new Action<AdminAutoscanActivityEvent>[]
        {
            poll => poll.Status = "error", poll => poll.ErrorMessage = "synthetic failure", poll => poll.DurationMs++, poll => poll.ScansReused++, poll => poll.ChangesTruncated = true,
            poll => poll.ScanRunsOmitted = 3, poll => poll.ScanRuns![0].Status = "running", poll => poll.ScanRuns![1].Result = new() { New = 2 }, poll => poll.ScanRuns![1].Result = null,
            poll => poll.ScanRuns!.RemoveAt(1), poll => poll.Changes![0].Outcome = "joined", poll => poll.Changes![1].Detail = "synthetic detail", poll => poll.Changes!.RemoveAt(0),
        };
        foreach (var edit in edits)
        {
            var edited = Event(); edit(edited);
            Assert.NotEqual(key, D.ContentKey(edited));
        }
        // A value cannot move from one field into the next unnoticed.
        var withPath = Run(0); withPath.Path = "a"; withPath.ErrorMessage = null;
        var withError = Run(0); withError.Path = null; withError.ErrorMessage = "a";
        Assert.NotEqual(D.ContentKey(withPath), D.ContentKey(withError));
        static AdminAutoscanActivityScan Scan(string status) => new() { Id = "scan-1", LibraryId = "1", Mode = "file", Trigger = "autoscan", Status = status, EventStatus = "success" };
        Assert.Equal(D.RowsKey(new[] { Scan("completed") }), D.RowsKey(new[] { Scan("completed") }));
        Assert.NotEqual(D.RowsKey(new[] { Scan("completed") }), D.RowsKey(new[] { Scan("failed") }));
        Assert.NotEqual(D.RowsKey(new[] { Scan("completed") }), D.RowsKey(new[] { Scan("completed"), Scan("completed") }));
        var linked = Scan("completed"); linked.EventStatus = "error";
        Assert.NotEqual(D.RowsKey(new[] { Scan("completed") }), D.RowsKey(new[] { linked }));
    }

    [Fact]
    public void EveryTextTheNewPageUsesResolvesToAMessageAndNotItsKey()
    {
        string[] keys =
        [
            "web.ui.pagination.first", "web.ui.pagination.previous", "web.ui.pagination.next", "web.ui.pagination.last", "web.ui.pagination.rows", "web.ui.pagination.label",
            "web.ui.select.searchPlaceholder", "web.ui.select.noMatches", "common.tryAgain", "web.admin.common.pagination.polls", "web.admin.common.pagination.scans",
            "page.title", "page.subtitle", "stats.active", "stats.queued", "stats.runningScans", "stats.polling", "stats.latestPoll", "refreshLabel", "polls.title", "polls.description",
            "queue.title", "queue.description", "queue.empty", "queue.pendingDetails", "queue.cancelLabel", "history.scansTitle", "history.pollsTitle", "history.scans", "history.polls",
            "history.searchScans", "history.searchPolls", "history.clearSearch", "history.reset", "history.loadFailed", "history.emptyFiltered", "history.emptyScans", "history.emptyPolls",
            "details.paths", "details.scansCreated", "changes.none", "changes.notRecorded", "runs.none", "runs.noneJoined", "delivery.webhook", "filter.allStatuses",
            "column.status", "column.library", "column.source", "column.scope", "column.result", "column.time", "column.poll", "column.counts", "column.details", "column.duration", "column.progress", "column.action",
        ];
        foreach (var key in keys)
        {
            var text = A.T(key, new { count = 2, count_text = "2", from = 1, to = 2, total = 2, event_type = "x" });
            Assert.NotEqual(key, text); Assert.DoesNotContain("web.admin.autoscan", text); Assert.False(string.IsNullOrWhiteSpace(text), key);
        }
        Assert.Equal("Poll log", A.T("history.pollsTitle")); Assert.Equal("Last page", A.T("web.ui.pagination.last")); Assert.Equal("Try again", A.T("common.tryAgain"));
    }

    [Fact]
    public void CountsInTheLiveHeaderAreNotGroupedButResultCountsAre()
    {
        Assert.Equal("1234 changes", A.RawCount("metrics.changes", 1234));
        Assert.Equal("1234 active", A.RawCount("activeCount", 1234));
        Assert.Equal(AdminText.FormatNumber(1234) + " new", A.Count("result.new", 1234));
    }
}
