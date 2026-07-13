namespace SiloPlayer.Core.Models.Admin;

public sealed class AutoscanSettings
{
    public bool Enabled { get; set; }
    public int DefaultPollIntervalSeconds { get; set; } = 300;
    public int DebounceSeconds { get; set; } = 10;
}

public sealed class AutoscanConnection
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string? RequestIntegrationId { get; set; }
    public bool HasApiKey { get; set; }
}

public sealed class AutoscanConnectionInput
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "sonarr";
    public string? BaseUrl { get; set; }
    public string? ApiKeyRef { get; set; }
    public string? RequestIntegrationId { get; set; }
}

public sealed class AutoscanConnectionsResponse { public List<AutoscanConnection> Connections { get; set; } = []; }
public sealed class AutoscanPathRewrite { public string From { get; set; } = ""; public string To { get; set; } = ""; }

public sealed class AutoscanSource
{
    public string Id { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string CapabilityId { get; set; } = "";
    public string? ConnectionId { get; set; }
    public bool Enabled { get; set; }
    public string DeliveryMode { get; set; } = "poll";
    public int? PollIntervalSeconds { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastError { get; set; }
    public List<AutoscanPathRewrite> PathRewrites { get; set; } = [];
    public Dictionary<string, string> SourceConfig { get; set; } = [];
    public string Label { get; set; } = "";
    public bool WebhookConfigured { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecretSuffix { get; set; }
    public DateTimeOffset? WebhookLastReceivedAt { get; set; }
    public DateTimeOffset? WebhookLastErrorAt { get; set; }
    public string? WebhookLastErrorMessage { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConnectionDisplay => string.IsNullOrWhiteSpace(ConnectionId) ? "— No connection —" : ConnectionId;
    [System.Text.Json.Serialization.JsonIgnore]
    public string PollIntervalDisplay => PollIntervalSeconds is > 0 ? $"{PollIntervalSeconds} sec" : "Default";
    [System.Text.Json.Serialization.JsonIgnore]
    public string PathRewriteDisplay => $"Path rewrites{(PathRewrites.Count > 0 ? $" ({PathRewrites.Count})" : "")}";
    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceSettingsDisplay => $"{(string.IsNullOrWhiteSpace(Label) ? "Source" : Label)} paths & ignores";
    [System.Text.Json.Serialization.JsonIgnore]
    public string LastRunDisplay => LastError != null ? "Error" : LastRunAt.HasValue ? LastRunAt.Value.LocalDateTime.ToString("g") : "Never";
}

public class AutoscanSourceInput
{
    public string? ConnectionId { get; set; }
    public bool Enabled { get; set; }
    public string DeliveryMode { get; set; } = "poll";
    public int? PollIntervalSeconds { get; set; }
    public List<AutoscanPathRewrite> PathRewrites { get; set; } = [];
    public Dictionary<string, string> SourceConfig { get; set; } = [];
    public string Label { get; set; } = "";
}

public sealed class AutoscanSourceCreateInput : AutoscanSourceInput
{
    public string PluginId { get; set; } = "";
    public string CapabilityId { get; set; } = "";
}

public sealed class AutoscanSourcesResponse { public List<AutoscanSource> Sources { get; set; } = []; }
public sealed class AutoscanAvailableSource { public string PluginId { get; set; } = ""; public string CapabilityId { get; set; } = ""; public string DisplayName { get; set; } = ""; }
public sealed class AutoscanAvailableSourcesResponse { public List<AutoscanAvailableSource> Plugins { get; set; } = []; }
public sealed class AutoscanConnectionTestResult { public bool Ok { get; set; } public string? Version { get; set; } public string? Error { get; set; } }
public sealed class AutoscanProposedRewrite { public string From { get; set; } = ""; public string To { get; set; } = ""; public int MatchDepth { get; set; } }
public sealed class AutoscanAmbiguousRoot { public string Root { get; set; } = ""; public List<string> Candidates { get; set; } = []; }
public sealed class AutoscanRewriteSuggestions { public List<AutoscanProposedRewrite> Proposed { get; set; } = []; public List<string> Unmatched { get; set; } = []; public List<AutoscanAmbiguousRoot> Ambiguous { get; set; } = []; public List<string> Covered { get; set; } = []; }

public sealed class AutoscanStatus
{
    public bool Enabled { get; set; }
    public List<AutoscanStatusSource> Sources { get; set; } = [];
    public List<AutoscanRunningPoll> RunningPolls { get; set; } = [];
    public int ActiveScans { get; set; }
    public int AcceptedScans { get; set; }
    public int RunningScans { get; set; }
    public DateTimeOffset? LatestEventAt { get; set; }
}
public sealed class AutoscanStatusSource { public string Id { get; set; } = ""; public string PluginId { get; set; } = ""; public string CapabilityId { get; set; } = ""; public string? ConnectionId { get; set; } public bool Enabled { get; set; } public string Label { get; set; } = ""; public DateTimeOffset? LastRunAt { get; set; } public string? LastError { get; set; } }
public sealed class AutoscanRunningPoll { public long Id { get; set; } public string? SourceId { get; set; } public string PluginId { get; set; } = ""; public string CapabilityId { get; set; } = ""; public DateTimeOffset StartedAt { get; set; } public long ElapsedMs { get; set; } public string? MarkerBefore { get; set; } }

public sealed class AutoscanEvent
{
    public long Id { get; set; }
    public string? SourceId { get; set; }
    public string PluginId { get; set; } = "";
    public string CapabilityId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long DurationMs { get; set; }
    public string Status { get; set; } = "";
    public string? DeliveryMode { get; set; }
    public string? ProviderEventType { get; set; }
    public int ChangesReturned { get; set; }
    public int ChangesResolved { get; set; }
    public int TargetsClaimed { get; set; }
    public int ScansCreated { get; set; }
    public int ScansReused { get; set; }
    public int ScansSuppressed { get; set; }
    public string? ErrorMessage { get; set; }
    public List<AutoscanEventScanRun> ScanRuns { get; set; } = [];
}
public sealed class AutoscanEventScanRun { public string Id { get; set; } = ""; public int LibraryId { get; set; } public string Mode { get; set; } = ""; public string? Path { get; set; } public string Trigger { get; set; } = ""; public string Status { get; set; } = ""; public DateTimeOffset? RequestedAt { get; set; } public DateTimeOffset? StartedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } public string? ErrorMessage { get; set; } }
public sealed class AutoscanEventsResponse { public List<AutoscanEvent> Events { get; set; } = []; public int Total { get; set; } public int Limit { get; set; } public int Offset { get; set; } }

public sealed class AutoscanScan { public string Id { get; set; } = ""; public int LibraryId { get; set; } public string Mode { get; set; } = ""; public string? Path { get; set; } public string Trigger { get; set; } = ""; public string Status { get; set; } = ""; public string? ErrorMessage { get; set; } public DateTimeOffset? RequestedAt { get; set; } public DateTimeOffset? StartedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } public long? AutoscanEventId { get; set; } public string? SourceId { get; set; } public string? PluginId { get; set; } public string? CapabilityId { get; set; } }
public sealed class AutoscanScansResponse { public List<AutoscanScan> Scans { get; set; } = []; public int Total { get; set; } public int Limit { get; set; } public int Offset { get; set; } }
