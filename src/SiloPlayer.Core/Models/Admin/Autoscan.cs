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
    [System.Text.Json.Serialization.JsonIgnore]
    public string KindDisplay => string.IsNullOrWhiteSpace(Kind) ? "—" : char.ToUpperInvariant(Kind[0]) + Kind[1..].ToLowerInvariant();
    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceDisplay => string.IsNullOrWhiteSpace(RequestIntegrationId) ? "Own" : "Reused from Requests";
    [System.Text.Json.Serialization.JsonIgnore]
    public string UrlDisplay => string.IsNullOrWhiteSpace(BaseUrl) ? "—" : BaseUrl;
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
    public string SourceDisplayName { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string SourceDetail => $"{PluginId} · {CapabilityId}";
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConnectionDisplay { get; set; } = "— No connection —";
    [System.Text.Json.Serialization.JsonIgnore]
    public string PollIntervalText => PollIntervalSeconds is > 0 ? PollIntervalSeconds.Value.ToString() : "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string PollIntervalHelp { get; set; } = "Floor only - values below the global default poll interval have no effect.";
    [System.Text.Json.Serialization.JsonIgnore]
    public string PathRewriteDisplay => $"Path rewrites{(PathRewrites.Count > 0 ? $" ({PathRewrites.Count})" : "")}";
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsWebhook => DeliveryMode.Equals("webhook", StringComparison.OrdinalIgnoreCase) || PluginId.Equals("silo.autoscan.arr-webhook", StringComparison.OrdinalIgnoreCase);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCephFs => PluginId.Equals("silo.autoscan.cephfs", StringComparison.OrdinalIgnoreCase) || CapabilityId.Equals("cephfs", StringComparison.OrdinalIgnoreCase);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasLastError => !string.IsNullOrWhiteSpace(IsWebhook ? WebhookLastErrorMessage ?? LastError : LastError);
    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusTitle => HasLastError ? "Error" : StatusTimestamp.HasValue ? "OK" : IsWebhook ? "No deliveries yet" : "Not run yet";
    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusDetail => HasLastError
        ? IsWebhook ? WebhookLastErrorMessage ?? LastError ?? "Delivery failed" : LastError ?? "Poll failed"
        : StatusTimestamp.HasValue ? RelativeTime(StatusTimestamp.Value) : "";
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset? StatusTimestamp => IsWebhook ? WebhookLastReceivedAt : LastRunAt;
    [System.Text.Json.Serialization.JsonIgnore]
    public string MovieFlatPaths => SourceConfig.TryGetValue("movie_flat_paths", out var value) ? value : "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string TvFlatPaths => SourceConfig.TryGetValue("tv_flat_paths", out var value) ? value : "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Exclusions => SourceConfig.TryGetValue("exclusions", out var value) ? value : "";

    private static string RelativeTime(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.Now - timestamp.ToLocalTime();
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed.TotalSeconds < 60) return "just now";
        if (elapsed.TotalMinutes < 60) return $"{Math.Max(1, (int)elapsed.TotalMinutes)}m ago";
        if (elapsed.TotalHours < 24) return $"{Math.Max(1, (int)elapsed.TotalHours)}h ago";
        return elapsed.TotalDays < 7 ? $"{Math.Max(1, (int)elapsed.TotalDays)}d ago" : timestamp.ToLocalTime().ToString("MMM d");
    }
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
    [System.Text.Json.Serialization.JsonIgnore] public string SourceDisplayName { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string TimestampDisplay => (CompletedAt ?? StartedAt).ToLocalTime().ToString("MMM d, hh:mm tt");
    [System.Text.Json.Serialization.JsonIgnore] public string DurationDisplay => DurationMs < 1000 ? $"{DurationMs}ms" : TimeSpan.FromMilliseconds(DurationMs).TotalMinutes >= 1 ? $"{(int)TimeSpan.FromMilliseconds(DurationMs).TotalMinutes}m {TimeSpan.FromMilliseconds(DurationMs).Seconds}s" : $"{TimeSpan.FromMilliseconds(DurationMs).TotalSeconds:0.0}s";
}
public sealed class AutoscanEventScanRun { public string Id { get; set; } = ""; public int LibraryId { get; set; } public string Mode { get; set; } = ""; public string? Path { get; set; } public string Trigger { get; set; } = ""; public string Status { get; set; } = ""; public DateTimeOffset? RequestedAt { get; set; } public DateTimeOffset? StartedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } public string? ErrorMessage { get; set; } }
public sealed class AutoscanEventsResponse { public List<AutoscanEvent> Events { get; set; } = []; public int Total { get; set; } public int Limit { get; set; } public int Offset { get; set; } }

public sealed class AutoscanScan
{
    public string Id { get; set; } = ""; public int LibraryId { get; set; } public string Mode { get; set; } = ""; public string? Path { get; set; } public string Trigger { get; set; } = ""; public string Status { get; set; } = ""; public string? ErrorMessage { get; set; } public DateTimeOffset? RequestedAt { get; set; } public DateTimeOffset? StartedAt { get; set; } public DateTimeOffset? CompletedAt { get; set; } public long? AutoscanEventId { get; set; } public string? SourceId { get; set; } public string? PluginId { get; set; } public string? CapabilityId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string LibraryDisplayName { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string SourceDisplayName { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string ScopeDisplay => string.Equals(Mode, "file", StringComparison.OrdinalIgnoreCase) ? "Single file scan" : string.Equals(Mode, "path", StringComparison.OrdinalIgnoreCase) || string.Equals(Mode, "subtree", StringComparison.OrdinalIgnoreCase) ? "Subtree scan" : string.IsNullOrWhiteSpace(Mode) ? "Library scan" : $"{char.ToUpperInvariant(Mode[0])}{Mode[1..]} scan";
    [System.Text.Json.Serialization.JsonIgnore] public string RequestedAtDisplay => (RequestedAt ?? StartedAt ?? CompletedAt)?.ToLocalTime().ToString("MMM d, hh:mm tt") ?? "—";
    [System.Text.Json.Serialization.JsonIgnore] public string PollDisplay => string.IsNullOrWhiteSpace(ErrorMessage) ? "Success" : "Failed";
}
public sealed class AutoscanScansResponse { public List<AutoscanScan> Scans { get; set; } = []; public int Total { get; set; } public int Limit { get; set; } public int Offset { get; set; } }
