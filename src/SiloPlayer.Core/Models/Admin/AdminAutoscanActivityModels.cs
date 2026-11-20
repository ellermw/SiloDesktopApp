using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

public enum AdminAutoscanHistory { Scans, Polls }

public sealed record AdminAutoscanActivityFilter(string Query = "", string Status = "", int Limit = 25);

public sealed class AdminAutoscanActivityPageInfo
{
    [JsonRequired] public bool HasMore { get; set; }
    public string? NextCursor { get; set; }
}

public sealed class AdminAutoscanActivityPage<T>
{
    [JsonRequired] public List<T>? Items { get; set; }
    [JsonRequired] public AdminAutoscanActivityPageInfo? Page { get; set; }
    [JsonRequired] public long Total { get; set; }
}

public sealed record AdminAutoscanActivityWindow<T>(IReadOnlyList<T> Items, long Total, string? NextCursor);

public sealed class AdminAutoscanActivityResult
{
    public long New { get; set; }
    public long Updated { get; set; }
    public long Unchanged { get; set; }
    public long Missing { get; set; }
    public long MissingSkippedProtected { get; set; }
    public long FilesDeleted { get; set; }
    public long ItemsDeleted { get; set; }
    public long MembershipsRemoved { get; set; }
    public long Errors { get; set; }
    public long Skipped { get; set; }
}

public class AdminAutoscanActivityRun
{
    public string Id { get; set; } = "";
    public string LibraryId { get; set; } = "";
    public string Mode { get; set; } = "";
    public string? Path { get; set; }
    public string Trigger { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public AdminAutoscanActivityResult? Result { get; set; }
}

public sealed class AdminAutoscanActivityScan : AdminAutoscanActivityRun
{
    public string? AutoscanEventId { get; set; }
    public string? SourceId { get; set; }
    public string? PluginId { get; set; }
    public string? CapabilityId { get; set; }
    public string? EventStatus { get; set; }
    public DateTimeOffset? EventCompletedAt { get; set; }
}

public sealed class AdminAutoscanActivityChange
{
    public string SourcePath { get; set; } = "";
    public string RewrittenPath { get; set; } = "";
    public string? Scope { get; set; }
    public string Outcome { get; set; } = "";
    public string? Reason { get; set; }
    public string? Detail { get; set; }
    public string? LibraryId { get; set; }
    public string? TargetMode { get; set; }
    public string? TargetPath { get; set; }
    public string? ScanRunId { get; set; }
}

public sealed class AdminAutoscanActivityEvent
{
    public string Id { get; set; } = "";
    public string? SourceId { get; set; }
    public string PluginId { get; set; } = "";
    public string CapabilityId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public long DurationMs { get; set; }
    public string Status { get; set; } = "";
    public string? DeliveryMode { get; set; }
    public string? ProviderEventType { get; set; }
    public long ChangesReturned { get; set; }
    public long ChangesResolved { get; set; }
    public long TargetsClaimed { get; set; }
    public long ScansCreated { get; set; }
    public long ScansReused { get; set; }
    public long ScansSuppressed { get; set; }
    public string? ErrorMessage { get; set; }
    public List<AdminAutoscanActivityRun>? ScanRuns { get; set; }
    public List<AdminAutoscanActivityChange>? Changes { get; set; }
    public bool ChangesTruncated { get; set; }
}

public sealed class AdminAutoscanActivityPoll
{
    public string Id { get; set; } = "";
    public string? SourceId { get; set; }
    public string PluginId { get; set; } = "";
    public string CapabilityId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public long ElapsedMs { get; set; }
}

public sealed class AdminAutoscanActivityStatus
{
    [JsonRequired] public bool Enabled { get; set; }
    [JsonRequired] public List<AdminAutoscanActivitySource>? Sources { get; set; }
    [JsonRequired] public List<AdminAutoscanActivityPoll>? RunningPolls { get; set; }
    [JsonRequired] public long ActiveScans { get; set; }
    [JsonRequired] public long AcceptedScans { get; set; }
    [JsonRequired] public long RunningScans { get; set; }
    public DateTimeOffset? LatestEventAt { get; set; }
}

public sealed class AdminAutoscanActivitySource
{
    public string Id { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string CapabilityId { get; set; } = "";
    public string? ConnectionId { get; set; }
    public string? Label { get; set; }
    public string? DeliveryMode { get; set; }
    public Dictionary<string, string>? SourceConfig { get; set; }
}

public sealed class AdminAutoscanActivityConnection
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class AdminAutoscanActivityPlugin
{
    public string PluginId { get; set; } = "";
    public string CapabilityId { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed record AdminAutoscanActivityLabels(
    IReadOnlyDictionary<string, string> Libraries,
    IReadOnlyDictionary<string, AdminAutoscanActivitySource> Sources,
    IReadOnlyDictionary<string, string> Connections,
    IReadOnlyDictionary<string, string> Plugins,
    bool Truncated = false)
{
    public static AdminAutoscanActivityLabels Empty { get; } = new(
        new Dictionary<string, string>(), new Dictionary<string, AdminAutoscanActivitySource>(),
        new Dictionary<string, string>(), new Dictionary<string, string>());
}
