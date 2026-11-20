using SiloPlayer.Core.Models.Admin;
using A = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityText;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>How a scan-result or change explanation is toned (88b825d57 <c>ScanResultText</c>, <c>ChangeExplanation</c>).</summary>
public enum AdminAutoscanTone { Muted, Normal, Warning, Danger }

public sealed record AdminAutoscanResultSummary(string Text, AdminAutoscanTone Tone);

/// <summary>One page of a client-side list with its clamped index (88b825d57 <c>TablePagination</c>).</summary>
public sealed record AdminAutoscanPageSlice<T>(IReadOnlyList<T> Rows, int Page, int PageCount, long Total, long From, long To);

/// <summary>
/// Pure display rules of the Autoscan Activity page, ported from the official
/// <c>pollEventDisplay.ts</c>, <c>PollActivityBadges.tsx</c>, <c>PollEventInspectorSheet.tsx</c> and
/// <c>pagination.utils.ts</c> at 88b825d57e4b2296050fda69eaf619eefbf208d1. Nothing here reads a clock
/// or formats a date, so it can be tested without the app.
/// </summary>
public static class AdminAutoscanActivityDisplay
{
    public const string NoValue = "-";
    public const string Separator = " · ";
    public const int QueuePageSize = 8;
    public const int DefaultHistoryPageSize = 25;
    public const int DefaultPathsPageSize = 15;
    public const int DefaultScansPageSize = 20;
    public static readonly IReadOnlyList<int> PathsPageSizes = [15, 25, 50];
    public static readonly IReadOnlyList<int> ScansPageSizes = [20, 50, 100];

    /// <summary>The inspector's outcome chip order; an outcome a newer server adds follows these.</summary>
    public static readonly IReadOnlyList<string> OutcomeOrder = ["queued", "joined", "unresolved", "error", "suppressed", "ignored"];
    public static readonly IReadOnlyList<string> ScanStatusOrder = ["running", "accepted", "completed", "failed", "cancelled"];

    public static bool IsKnownOutcome(string outcome) => OutcomeOrder.Contains(outcome);

    /// <summary>A compact run of page indices (null is an ellipsis) anchoring the first, last, current and neighbouring pages.</summary>
    public static IReadOnlyList<int?> PageWindow(int current, int pageCount)
    {
        var last = pageCount - 1;
        if (pageCount <= 7) return Enumerable.Range(0, Math.Max(0, pageCount)).Select(index => (int?)index).ToArray();
        var anchors = new[] { 0, last, current, current - 1, current + 1 }.Where(page => page >= 0 && page <= last).Distinct().Order().ToArray();
        var output = new List<int?>(); int? previous = null;
        foreach (var page in anchors)
        {
            if (previous != null && page - previous > 1) output.Add(null);
            output.Add(page); previous = page;
        }
        return output;
    }

    /// <summary>The rows of one client-side page, with the page clamped into range as the official view does.</summary>
    public static AdminAutoscanPageSlice<T> Slice<T>(IReadOnlyList<T> source, int page, int pageSize)
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(source.Count / (double)pageSize));
        var safe = Math.Min(Math.Max(page, 0), pageCount - 1);
        var rows = source.Skip(safe * pageSize).Take(pageSize).ToArray();
        return new(rows, safe, pageCount, source.Count, source.Count == 0 ? 0 : (long)safe * pageSize + 1, Math.Min((long)(safe + 1) * pageSize, source.Count));
    }

    /// <summary>Splits a path into its parent prefix and emphasized final segment.</summary>
    public static (string Dir, string Leaf) SplitDisplayPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return ("", "");
        var trimmed = path.TrimEnd('/', '\\');
        if (trimmed.Length == 0) return ("", path);
        var slash = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return slash < 0 ? ("", path) : (path[..(slash + 1)], path[(slash + 1)..]);
    }

    /// <summary>Runs the event's changes joined that another event created.</summary>
    public static int JoinedRunCount(AdminAutoscanActivityEvent poll)
    {
        var created = (poll.ScanRuns ?? []).Select(run => run.Id).ToHashSet(StringComparer.Ordinal);
        return (poll.Changes ?? []).Where(change => change.Outcome == "joined" && !string.IsNullOrEmpty(change.ScanRunId) && !created.Contains(change.ScanRunId))
            .Select(change => change.ScanRunId!).Distinct(StringComparer.Ordinal).Count();
    }

    public static string DetailsSummary(AdminAutoscanActivityEvent poll)
    {
        var linked = (poll.ScanRuns?.Count ?? 0) + JoinedRunCount(poll);
        // Joined runs are only known from recorded changes, so a truncated log can omit some; the count is then a lower bound.
        var paths = A.Count("details.pathCount", poll.ChangesReturned);
        var linkedText = A.T("details.linkedCount", new { count = linked, count_text = A.Number(linked) + (poll.ChangesTruncated ? "+" : "") });
        return paths + Separator + linkedText;
    }

    public static bool HasActivity(AdminAutoscanActivityEvent poll) => poll.ChangesReturned > 0 || (poll.ScanRuns?.Count ?? 0) > 0;

    /// <summary>The duration column: a running poll counts up from its start; otherwise the recorded duration.</summary>
    public static string EventDuration(AdminAutoscanActivityEvent poll, DateTimeOffset now) =>
        A.Duration(poll.Status == "running" ? Math.Max(0, (long)(now - poll.StartedAt).TotalMilliseconds) : poll.DurationMs);

    public static DateTimeOffset EventTime(AdminAutoscanActivityEvent poll) => poll.Status == "running" ? poll.StartedAt : poll.CompletedAt;

    public static DateTimeOffset? RunTime(AdminAutoscanActivityRun run) => run.CompletedAt ?? run.StartedAt ?? run.RequestedAt;

    public static AdminAutoscanResultSummary? SummarizeResult(AdminAutoscanActivityResult? result)
    {
        if (result == null) return null;
        if (result.Skipped > 0) return new(A.T("result.skipped"), AdminAutoscanTone.Warning);
        var parts = new List<string>();
        void Add(string key, long count) { if (count > 0) parts.Add(A.Count("result." + key, count)); }
        Add("new", result.New); Add("updated", result.Updated); Add("missing", result.Missing);
        Add("filesRemoved", result.FilesDeleted); Add("itemsRemoved", result.ItemsDeleted); Add("titlesRemoved", result.MembershipsRemoved);
        Add("keptOffline", result.MissingSkippedProtected); Add("errors", result.Errors);
        if (parts.Count == 0)
            return new(result.Unchanged > 0 ? A.Count("result.noChangesUnchanged", result.Unchanged) : A.T("result.noChanges"), AdminAutoscanTone.Muted);
        return new(string.Join(Separator, parts), result.Errors > 0 ? AdminAutoscanTone.Danger : AdminAutoscanTone.Normal);
    }

    public static string ChangeScopeLabel(AdminAutoscanActivityChange change) => A.T("change.scope", new
    {
        mode = string.IsNullOrEmpty(change.TargetMode) ? A.T("change.scanFallback") : A.Mode(change.TargetMode),
        path = string.IsNullOrEmpty(change.TargetPath) ? A.T("entireLibrary") : change.TargetPath,
    });

    /// <summary>The official <c>ChangeExplanation</c> text and tone (error red, unresolved amber, otherwise muted).</summary>
    public static AdminAutoscanResultSummary ChangeExplanation(AdminAutoscanActivityChange change, AdminAutoscanActivityLabels labels)
    {
        var library = string.IsNullOrEmpty(change.LibraryId) ? null : A.Library(change.LibraryId, labels);
        switch (change.Outcome)
        {
            case "queued":
            case "joined":
                var key = change.Outcome == "queued" ? "queued" : change.Reason == "follow_up_scan" ? "followUp" : "joined";
                var scope = ChangeScopeLabel(change);
                return new(A.T("change." + key + (library == null ? "" : "InLibrary"), new { scope, library }), AdminAutoscanTone.Muted);
            case "suppressed":
                return new(A.T(string.IsNullOrEmpty(change.TargetPath) ? "change.suppressed" : "change.suppressedPath", new { path = change.TargetPath }), AdminAutoscanTone.Muted);
            case "error":
                if (change.Reason == "resolve_failed")
                    return new(A.T(string.IsNullOrEmpty(change.Detail) ? "change.resolveFailed" : "change.resolveFailedDetail", new { detail = change.Detail }), AdminAutoscanTone.Danger);
                return new(A.ReasonText(change.Reason) ?? (string.IsNullOrEmpty(change.Detail) ? A.T("change.processingFailed") : change.Detail), AdminAutoscanTone.Danger);
            default:
                var text = A.ReasonText(change.Reason) ?? (!string.IsNullOrEmpty(change.Detail) ? change.Detail : !string.IsNullOrEmpty(change.Reason) ? change.Reason : A.T("change.notScanned"));
                return new(library == null ? text : A.T("change.withLibrary", new { text, library }),
                    change.Outcome == "unresolved" ? AdminAutoscanTone.Warning : AdminAutoscanTone.Muted);
        }
    }

    /// <summary>The outcome chips: each outcome present with its count, known outcomes first in the official order.</summary>
    public static IReadOnlyList<(string Outcome, int Count)> OutcomeCounts(IEnumerable<AdminAutoscanActivityChange> changes)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal); var firstSeen = new List<string>();
        foreach (var change in changes)
        {
            if (!counts.TryAdd(change.Outcome, 1)) counts[change.Outcome]++; else firstSeen.Add(change.Outcome);
        }
        return OutcomeOrder.Where(counts.ContainsKey).Concat(firstSeen.Where(outcome => !IsKnownOutcome(outcome))).Select(outcome => (outcome, counts[outcome])).ToArray();
    }

    public static IReadOnlyList<(string Status, int Count)> ScanStatusCounts(IEnumerable<AdminAutoscanActivityRun> runs)
    {
        var counts = runs.GroupBy(run => run.Status, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return ScanStatusOrder.Where(counts.ContainsKey).Select(status => (status, counts[status])).ToArray();
    }

    public static IReadOnlyList<AdminAutoscanActivityChange> FilterChanges(IReadOnlyList<AdminAutoscanActivityChange> changes, string outcome, string search, AdminAutoscanActivityLabels labels)
    {
        var needle = search.Trim().ToLowerInvariant();
        bool Has(string? value) => value != null && value.ToLowerInvariant().Contains(needle, StringComparison.Ordinal);
        return changes.Where(change =>
        {
            if (outcome != "all" && change.Outcome != outcome) return false;
            if (needle.Length == 0) return true;
            return Has(change.SourcePath) || Has(change.RewrittenPath) || Has(change.TargetPath) || Has(change.ScanRunId) || Has(change.Detail) || Has(change.Reason) ||
                Has(A.ReasonText(change.Reason)) || Has(string.IsNullOrEmpty(change.LibraryId) ? "" : A.Library(change.LibraryId, labels));
        }).ToArray();
    }

    public static IReadOnlyList<AdminAutoscanActivityRun> FilterRuns(IReadOnlyList<AdminAutoscanActivityRun> runs, string status, string search, AdminAutoscanActivityLabels labels)
    {
        var needle = search.Trim().ToLowerInvariant();
        bool Has(string? value) => value != null && value.ToLowerInvariant().Contains(needle, StringComparison.Ordinal);
        return runs.Where(run =>
        {
            if (status != "all" && run.Status != status) return false;
            if (needle.Length == 0) return true;
            return Has(run.Id) || Has(run.Path) || Has(run.Mode) || Has(A.Mode(run.Mode)) || Has(A.Library(run.LibraryId, labels)) || Has(run.ErrorMessage);
        }).ToArray();
    }

    /// <summary>The inspector opens on Scans created only when the event recorded no paths but did create runs.</summary>
    public static string InitialTab(AdminAutoscanActivityEvent poll) =>
        (poll.Changes?.Count ?? 0) == 0 && (poll.ScanRuns?.Count ?? 0) > 0 ? "scans" : "paths";

    /// <summary>The queue's live-scan ordering: running first, then mode (library, file, subtree), then path.</summary>
    public static int ModeRank(string? mode) => mode switch { "library" => 0, "file" => 1, "subtree" => 2, _ => 3 };
}
