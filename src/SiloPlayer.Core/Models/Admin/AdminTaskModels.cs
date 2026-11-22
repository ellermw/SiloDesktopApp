namespace SiloPlayer.Core.Models.Admin;

/// <summary>Runtime states a task reports on the responding process (official <c>AdminTask.state</c>).</summary>
public static class AdminTaskStates
{
    public const string Idle = "idle";
    public const string Running = "running";
    public const string Cancelling = "cancelling";

    public static bool IsActive(string state) => state is Running or Cancelling;
}

/// <summary>Trigger types the schedule editor offers (official <c>TriggerType</c>).</summary>
public static class AdminTaskTriggerTypes
{
    public const string Interval = "interval";
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Startup = "startup";

    public static IReadOnlyList<string> All { get; } = [Interval, Daily, Weekly, Startup];
}

/// <summary>One schedule trigger. Absent fields are null and are omitted from a write.</summary>
public sealed record AdminTaskTrigger(string Type, long? IntervalMs, string? TimeOfDay, int? DayOfWeek, long? MaxRuntimeMs);

/// <summary>One step of a multi-step run such as <c>database_maintenance</c>. Error text stays in server logs.</summary>
public sealed record AdminTaskStep(string Key, string Name, string Status);

/// <summary>The typed <c>contribute_markers</c> result; the server exposes no other result data.</summary>
public sealed record AdminTaskMarkerResult(int Submitted, int Skipped, int Invalid, int Failed, int RetryAfterSeconds);

/// <summary>
/// A finished run. Persisted history entries carry an opaque <see cref="Id"/>; a live
/// last-execution summary has none. <see cref="ErrorMessage"/> is the server's safe text.
/// </summary>
public sealed record AdminTaskExecution(string? Id, string TaskKey, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, string Status,
    long DurationMs, string? ErrorMessage, IReadOnlyList<AdminTaskStep> Steps, AdminTaskMarkerResult? Result);

/// <summary>A registered task and its runtime state on the responding process.</summary>
public sealed record AdminTaskInfo(string Key, string Name, string Description, string Category, string State, double Progress, bool ManualOnly,
    AdminTaskExecution? LastExecution, IReadOnlyList<AdminTaskTrigger> Triggers, DateTimeOffset? NextRunAt, DateTimeOffset? StartedAt)
{
    public bool IsActive => AdminTaskStates.IsActive(State);
}

/// <summary>The persisted schedule and its strong caller-bound validator. The validator is never printed.</summary>
public sealed record AdminTaskSchedule(string TaskKey, IReadOnlyList<AdminTaskTrigger> Triggers, string ETag)
{
    public override string ToString() => $"{nameof(AdminTaskSchedule)} {{ TaskKey = {TaskKey}, Triggers = {Triggers.Count} }}";
}

/// <summary>One page of persisted history. The cursor is caller-bound and never printed.</summary>
public sealed record AdminTaskHistoryPage(IReadOnlyList<AdminTaskExecution> Items, string? NextCursor, bool HasMore)
{
    public override string ToString() => $"{nameof(AdminTaskHistoryPage)} {{ Items = {Items.Count}, HasMore = {HasMore} }}";
}

public sealed record AdminTaskMetricCount(string Reason, int Count);
public sealed record AdminTaskAttemptBucket(string Label, int Count);

/// <summary>
/// A refresh-debt sample. <see cref="LastError"/> is the server's fixed safe summary, never a raw provider error.
/// <see cref="Title"/> is the target's own title (a season's or an episode's, without its series); a season or episode that
/// still exists also carries <see cref="SeriesTitle"/> (empty when absent), <see cref="SeasonNumber"/> and, for an
/// episode, <see cref="EpisodeNumber"/>, so the title is named in the interface language.
/// </summary>
public sealed record AdminTaskDebtSample(string TargetType, string ContentId, string Title, string Type, long ReasonMask,
    DateTimeOffset NextRefreshAt, DateTimeOffset? LastAttemptAt, int AttemptCount, string LastError,
    string SeriesTitle = "", int? SeasonNumber = null, int? EpisodeNumber = null);

/// <summary>Bounded <c>refresh_metadata</c> queue metrics (official <c>AdminTaskMetrics</c>).</summary>
public sealed record AdminTaskMetrics(int Total, int Due, int Leased, DateTimeOffset? OldestDueAt, DateTimeOffset? OldestLeaseExpiresAt,
    IReadOnlyList<AdminTaskMetricCount> ReasonCounts, IReadOnlyList<AdminTaskAttemptBucket> AttemptBuckets,
    IReadOnlyList<AdminTaskDebtSample> DueSamples, IReadOnlyList<AdminTaskDebtSample> RecentErrors);
