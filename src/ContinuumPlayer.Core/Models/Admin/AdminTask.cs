namespace ContinuumPlayer.Core.Models.Admin;

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
