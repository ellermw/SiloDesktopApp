using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

public enum AdminRealtimeConnectionState { Connecting, Live, Disconnected }

public sealed record ServerActivityStreamRow(ActivityMethodMeta Meta, int Count, string Text);

/// <summary>A running task; <see cref="Percent"/> is null for indeterminate progress (spinner + "Running").</summary>
public sealed record ServerActivityTaskRow(string Key, string Name, double? Percent, string? PercentText);

public sealed record ServerActivityScanRow(string Id, string LibraryName, bool Running, string StatusText, string DetailText, string? ProgressText);

public sealed record ServerActivityPreparationRow(string Id, string Title, double? Percent, string? PercentText, string RunningLabel,
    string DetailText, string ProgressLabel);

public sealed record ServerActivityPreparationsView(int ActiveCount, IReadOnlyList<ServerActivityPreparationRow> Running,
    string? WaitingText, string? PausedText, string? FailedText, string? EmptyText);

/// <summary>Everything the trigger and popover render, derived from a store snapshot.</summary>
public sealed record ServerActivityView(
    int TotalActive,
    bool CountIsLowerBound,
    string BadgeText,
    string AccessibleName,
    bool ShowEmptyState,
    int StreamCount,
    IReadOnlyList<ServerActivityStreamRow> Streams,
    IReadOnlyList<ServerActivityTaskRow> Tasks,
    int ScanCount,
    string ScanChipText,
    IReadOnlyList<ServerActivityScanRow> Scans,
    string? MoreScansText,
    ServerActivityPreparationsView? Preparations,
    string? ConnectionText);

/// <summary>
/// Pure presentation of WebUI <c>ServerActivity.tsx</c>: the badge total
/// (sessions + running tasks incl. hidden + accepted/running scans +
/// running/queued/retrying preparations), "99+" cap, "N+" lower bound when the
/// scan snapshot holds 500 or more entries, 25 scan rows, 3 preparation rows,
/// and the empty state that never hides recent preparation failures.
/// </summary>
public static class ServerActivityPresentation
{
    public const int MaxBadgeCount = 99;
    public const int ActiveScanSnapshotLimit = 500;
    public const int MaxScanRows = 25;
    public const int MaxPreparationRows = 3;
    public static readonly TimeSpan ConnectionProblemDelay = TimeSpan.FromSeconds(4);
    private const string DetailSeparator = " · ";

    public static bool IsActiveScan(AdminIndicatorScanRun scan) => scan.Status is "accepted" or "running";

    public static long ActivePreparationCount(AdminIndicatorPreparationCounts? counts) =>
        counts == null ? 0 : counts.Running + counts.Queued + counts.Retrying;

    public static string FormatBadgeCount(long count, bool lowerBound = false)
    {
        if (count > MaxBadgeCount) return $"{MaxBadgeCount}+";
        return lowerBound ? $"{count}+" : count.ToString(CultureInfo.InvariantCulture);
    }

    public static ServerActivityView Build(ServerActivitySnapshot snapshot)
    {
        var sessions = snapshot.Sessions;
        var runningTasks = snapshot.Tasks.Where(task => task.State == "running").ToArray();
        var activeScans = snapshot.Scans.Where(IsActiveScan).ToArray();
        var preparations = snapshot.Preparations;
        var activePreparations = ActivePreparationCount(preparations?.Counts);
        var total = (int)Math.Min(int.MaxValue, sessions.Count + runningTasks.Length + activeScans.Length + activePreparations);
        var lowerBound = activeScans.Length >= ActiveScanSnapshotLimit;
        var accessible = total > 0
            ? AdminText.T(lowerBound ? "web.admin.common.serverActivity.trigger.atLeast" : "web.admin.common.serverActivity.trigger.active",
                new Dictionary<string, object?> { ["count"] = total, ["count_text"] = total })
            : AdminText.T("web.admin.common.serverActivity.trigger.idle");

        var streams = AdminActivityListState.MethodCounts(sessions)
            .Select(method => new ServerActivityStreamRow(method.Meta, method.Count,
                AdminText.T("web.admin.common.serverActivity.streamCount", new Dictionary<string, object?>
                {
                    ["count"] = method.Count,
                    ["count_text"] = method.Count,
                    ["method"] = method.Meta.Label,
                })))
            .ToArray();

        var tasks = runningTasks.Select(task =>
        {
            double? percent = task.Progress > 0 ? ClampTaskProgress(task.Progress) : null;
            return new ServerActivityTaskRow(task.Key, task.Name, percent, percent == null ? null : FormatTaskProgress(task.Progress));
        }).ToArray();

        var visibleScans = activeScans.Take(MaxScanRows).Select(scan => ScanRow(scan, snapshot.LibraryNames)).ToArray();
        var hiddenScans = activeScans.Length - visibleScans.Length;
        var moreScans = hiddenScans > 0
            ? AdminText.T(lowerBound ? "web.admin.common.serverActivity.moreScansAtLeast" : "web.admin.common.serverActivity.moreScans",
                AdminText.Count(hiddenScans))
            : null;

        var empty = total == 0 && snapshot.ScansLoaded && (preparations?.Counts.FailedRecent ?? 0) == 0;
        string? connection = snapshot.Connection switch
        {
            AdminRealtimeConnectionState.Live => null,
            AdminRealtimeConnectionState.Connecting => AdminText.T("web.admin.common.serverActivity.connecting"),
            _ => AdminText.T("web.admin.common.serverActivity.disconnected"),
        };

        return new ServerActivityView(
            total,
            lowerBound,
            FormatBadgeCount(total, lowerBound),
            accessible,
            empty,
            sessions.Count,
            streams,
            tasks,
            activeScans.Length,
            FormatBadgeCount(activeScans.Length, lowerBound),
            visibleScans,
            moreScans,
            preparations == null ? null : Preparations(preparations),
            connection);
    }

    public static double ClampTaskProgress(double progress) =>
        double.IsFinite(progress) ? Math.Min(100, Math.Max(0, progress)) : 0;

    /// <summary>Integer percent, or one decimal never rounded up to a finished-looking 100.</summary>
    public static string FormatTaskProgress(double progress)
    {
        var clamped = ClampTaskProgress(progress);
        if (clamped == Math.Floor(clamped))
            return AdminText.T("web.admin.tasks.lib.taskProgress.percent", new { percent = clamped.ToString("0", CultureInfo.CurrentCulture) });
        var shown = Math.Round(clamped, 1) >= 100 ? 99.9 : clamped;
        return AdminText.T("web.admin.tasks.lib.taskProgress.percent", new { percent = shown.ToString("0.0", CultureInfo.CurrentCulture) });
    }

    private static ServerActivityScanRow ScanRow(AdminIndicatorScanRun scan, IReadOnlyDictionary<string, string> libraries)
    {
        var name = libraries.TryGetValue(scan.LibraryId, out var known)
            ? known
            : AdminText.T("web.admin.common.serverActivity.libraryFallback", new { id = scan.LibraryId });
        var running = scan.Status == "running";
        var mode = scan.Mode switch
        {
            "library" => AdminText.T("web.admin.common.serverActivity.scanMode.library"),
            "subtree" => AdminText.T("web.admin.common.serverActivity.scanMode.subtree"),
            "file" => AdminText.T("web.admin.common.serverActivity.scanMode.file"),
            _ => scan.Mode,
        };
        var detail = string.IsNullOrEmpty(scan.Path) ? mode : mode + DetailSeparator + scan.Path;
        return new ServerActivityScanRow(scan.Id, name, running,
            running ? AdminText.T("web.admin.common.serverActivity.scanning") : AdminText.T("web.admin.common.serverActivity.scanQueued"),
            detail, FormatScanProgress(scan));
    }

    public static string? FormatScanProgress(AdminIndicatorScanRun scan)
    {
        var result = scan.Result;
        if (result == null) return null;
        if (result.TotalFiles is > 0 && result.FilesProcessed is > 0)
        {
            var percent = (int)Math.Max(0, Math.Min(100, Math.Round(result.FilesProcessed.Value * 100d / result.TotalFiles.Value,
                MidpointRounding.AwayFromZero)));
            return AdminText.T("web.admin.common.serverActivity.scanProgress", new
            {
                message = result.Message ?? AdminText.T("web.admin.common.serverActivity.processingFiles"),
                processed = AdminText.FormatNumber(result.FilesProcessed.Value),
                total = AdminText.FormatNumber(result.TotalFiles.Value),
                percent,
            });
        }
        return string.IsNullOrEmpty(result.Message) ? null : result.Message;
    }

    private static ServerActivityPreparationsView Preparations(AdminIndicatorPreparationList list)
    {
        var counts = list.Counts;
        var running = list.Items.Where(item => item.State == "running").ToArray();
        var visible = running.Take(MaxPreparationRows).Select(PreparationRow).ToArray();
        var hiddenRunning = running.Length - visible.Length;
        var waiting = new List<string>();
        if (hiddenRunning > 0) waiting.Add(AdminText.T("web.admin.common.serverActivity.moreRunning",
            new Dictionary<string, object?> { ["count"] = hiddenRunning, ["count_text"] = hiddenRunning }));
        if (counts.Queued > 0) waiting.Add(AdminText.T("web.admin.common.serverActivity.queued", AdminText.Count(counts.Queued)));
        if (counts.Retrying > 0) waiting.Add(AdminText.T("web.admin.common.serverActivity.retrying", AdminText.Count(counts.Retrying)));
        var paused = counts.Paused > 0 ? AdminText.T("web.admin.common.serverActivity.paused", AdminText.Count(counts.Paused)) : null;
        var failed = counts.FailedRecent > 0 ? AdminText.T("web.admin.common.serverActivity.failedRecent", AdminText.Count(counts.FailedRecent)) : null;
        var empty = visible.Length == 0 && waiting.Count == 0 && paused == null && failed == null
            ? AdminText.T("web.admin.common.serverActivity.noPreparations")
            : null;
        return new ServerActivityPreparationsView((int)Math.Min(int.MaxValue, ActivePreparationCount(counts)), visible,
            waiting.Count > 0 ? string.Join(" · ", waiting) : null, paused, failed, empty);
    }

    private static bool IsEpisode(AdminIndicatorPreparation p) =>
        !string.IsNullOrEmpty(p.SeriesName) && p.SeasonNumber != null && p.EpisodeNumber != null;

    public static string PreparationCompactTitle(AdminIndicatorPreparation p)
    {
        if (IsEpisode(p))
        {
            var code = AdminText.T("web.admin.downloads.preparation.episodeCode", new
            {
                season = p.SeasonNumber!.Value.ToString("00", CultureInfo.InvariantCulture),
                episode = p.EpisodeNumber!.Value.ToString("00", CultureInfo.InvariantCulture),
            });
            return AdminText.T("web.admin.downloads.preparation.compactEpisodeTitle", new { series = p.SeriesName ?? "", code });
        }
        return string.IsNullOrEmpty(p.MediaTitle)
            ? AdminText.T("web.admin.downloads.preparation.fileFallback", new { id = p.MediaFileId })
            : p.MediaTitle;
    }

    public static double? PreparationPercent(AdminIndicatorPreparation p)
    {
        if (p.Progress is not { DurationSeconds: > 0 } progress) return null;
        return Math.Max(0, Math.Min(100, progress.EncodedSeconds / progress.DurationSeconds * 100));
    }

    public static double? PreparationRemainingSeconds(AdminIndicatorPreparation p)
    {
        if (p.Progress is not { DurationSeconds: > 0, Speed: > 0 } progress) return null;
        return Math.Max(0, progress.DurationSeconds - progress.EncodedSeconds) / progress.Speed;
    }

    public static string FormatRemaining(double? seconds)
    {
        if (seconds == null) return "";
        if (seconds < 60) return AdminText.T("web.admin.downloads.preparation.remaining.underOneMinute");
        var minutes = (long)Math.Round(seconds.Value / 60, MidpointRounding.AwayFromZero);
        if (minutes < 60) return AdminText.T("web.admin.downloads.preparation.remaining.minutes", new { minutes });
        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0
            ? AdminText.T("web.admin.downloads.preparation.remaining.hours", new { hours, hours_text = hours })
            : AdminText.T("web.admin.downloads.preparation.remaining.hoursMinutes", new { hours, hours_text = hours, minutes = rest });
    }

    public static string FormatPreparationKind(AdminIndicatorPreparation p)
    {
        if (p.Format == "remux") return AdminText.T("web.admin.downloads.preparation.kind.remux");
        var target = p.Output.VideoCodec == "copy"
            ? ""
            : string.Join(" ", new[] { p.Output.Resolution, AdminActivityPresentation.FormatCodec(p.Output.VideoCodec) }
                .Where(part => !string.IsNullOrEmpty(part)));
        return target.Length > 0
            ? AdminText.T("web.admin.downloads.preparation.kind.transcodeTarget", new { target })
            : AdminText.T("web.admin.downloads.preparation.kind.transcode");
    }

    public static string? PreparationWorkerName(AdminIndicatorPreparation p)
    {
        if (p.Worker == null) return null;
        if (p.Worker.Kind == "node")
            return !string.IsNullOrEmpty(p.Worker.Name) ? p.Worker.Name
                : !string.IsNullOrEmpty(p.Worker.NodeId)
                    ? AdminText.T("web.admin.downloads.preparation.worker.nodeWithId", new { id = p.Worker.NodeId })
                    : AdminText.T("web.admin.downloads.preparation.worker.node");
        return !string.IsNullOrEmpty(p.Worker.Name) ? p.Worker.Name : AdminText.T("web.admin.downloads.preparation.worker.localServer");
    }

    private static ServerActivityPreparationRow PreparationRow(AdminIndicatorPreparation p)
    {
        var percent = PreparationPercent(p);
        var detail = string.Join(" · ", new[] { FormatPreparationKind(p), PreparationWorkerName(p), FormatRemaining(PreparationRemainingSeconds(p)) }
            .Where(part => !string.IsNullOrEmpty(part)));
        var title = PreparationCompactTitle(p);
        return new ServerActivityPreparationRow(p.Id, title, percent,
            percent == null ? null : AdminText.T("web.admin.common.serverActivity.percent", new { percent = (int)Math.Floor(percent.Value) }),
            AdminText.T(p.Format == "remux" ? "web.admin.downloads.preparation.running.remuxing" : "web.admin.downloads.preparation.running.encoding"),
            detail,
            AdminText.T("web.admin.common.serverActivity.progressLabel", new { title }));
    }
}
