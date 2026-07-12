namespace SiloPlayer.Core.Models.Admin;

public class TaskInfo
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string State { get; set; } = "idle";
    public double Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public ExecutionResult? LastExecution { get; set; }
    public List<TriggerConfig> Triggers { get; set; } = [];
    public string? NextRunAt { get; set; }
}

public class TriggerConfig
{
    public string Type { get; set; } = "";
    public long? IntervalMs { get; set; }
    public string? TimeOfDay { get; set; }
    public int? DayOfWeek { get; set; }
    public long? MaxRuntimeMs { get; set; }
}

public class ExecutionResult
{
    public int Id { get; set; }
    public string TaskKey { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public string CompletedAt { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object>? ResultData { get; set; }
    public long DurationMs { get; set; }
}

// NOTE: GET /admin/tasks returns a bare JSON array, not a wrapper object.
// GET /admin/tasks/{key}/history also returns a bare JSON array.
// Both are deserialized directly as List<T> in AdminApi.

// GET /admin/tasks/{key}/metrics — only meaningful for refresh_metadata task
public class MetadataRefreshMetrics
{
    public int Total { get; set; }
    public int Due { get; set; }
    public int Leased { get; set; }
    public string? OldestDueAt { get; set; }
    public string? OldestLeaseExpiresAt { get; set; }
    public List<MetadataRefreshReasonCount> ReasonCounts { get; set; } = [];
    public List<MetadataRefreshAttemptBucket> AttemptBuckets { get; set; } = [];
    public List<MetadataRefreshDebtSample> DueSamples { get; set; } = [];
    public List<MetadataRefreshDebtSample> RecentErrors { get; set; } = [];
}

public class MetadataRefreshReasonCount
{
    public string Reason { get; set; } = "";
    public int Count { get; set; }
}

public class MetadataRefreshAttemptBucket
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public class MetadataRefreshDebtSample
{
    public string ContentId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public int ReasonMask { get; set; }
    public string NextRefreshAt { get; set; } = "";
    public string? LastAttemptAt { get; set; }
    public int AttemptCount { get; set; }
    public string LastError { get; set; } = "";
}
