using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

/// <summary>Configuration and observations are distinct: only ConfigETag guards edits (official 217ff25).</summary>
public sealed class AdminPlaybackNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public string? PublicUrl { get; set; }
    [JsonPropertyName("config_etag")] public string? ConfigETag { get; set; }
    public bool Enabled { get; set; }
    public bool Healthy { get; set; }
    public int ActiveJobs { get; set; }
    public string? Group { get; set; }
    public int? MaxJobs { get; set; }
    public int? MaxBandwidthKbps { get; set; }
    public int EgressKbps { get; set; }
    public string? LastHealthCheck { get; set; }
    public string? CreatedAt { get; set; }
    public bool? DrainRequested { get; set; }
    public string? DrainState { get; set; }
    public bool? KeyIssued { get; set; }
    public string? SourceAccess { get; set; }
    public string? HwAccelOverride { get; set; }
    public string? HwDeviceOverride { get; set; }
    public string? CapabilityDrift { get; set; }
    public string? CapabilitiesHash { get; set; }
    public string? AdvertisedCapabilitiesHash { get; set; }
    public string? CapabilitiesRefreshedAt { get; set; }
    public string[] PhysicalGpuKeys { get; set; } = [];
    public AdminPlaybackHardware? Capabilities { get; set; }
    public AdminPlaybackNodeStats? LastStats { get; set; }
    public Dictionary<string, AdminPlaybackNetworkStatus>? NetworkAccess { get; set; }
    public override string ToString() => nameof(AdminPlaybackNode);
}
public sealed record AdminPlaybackNodeWindow(IReadOnlyList<AdminPlaybackNode> Items, bool HasMore);
public sealed class AdminPlaybackNodeCapabilities
{
    public bool Allowed { get; set; }
    public string State { get; set; } = "unsupported";
    public bool Drain { get; set; }
    public bool NodeKeys { get; set; }
    public bool SourceAccess { get; set; }
    public bool Summary { get; set; }
    public string Revision { get; set; } = "";
    public bool Available => Allowed && State == "available";
}
public sealed class AdminPlaybackNetworkStatus
{
    public string State { get; set; } = "";
    public string? Hostname { get; set; }
    public string? Origin { get; set; }
    public string? UpdatedAt { get; set; }
}
public sealed class AdminPlaybackHardware
{
    public string Resolved { get; set; } = "";
    public string? Source { get; set; }
    public string[]? RenderDevices { get; set; }
    public AdminPlaybackDevice[]? RenderDeviceDetails { get; set; }
    public AdminPlaybackBackend[]? DetectedBackends { get; set; }
    public string[]? TransportFeatures { get; set; }
    public AdminPlaybackHardwareNode[]? Nodes { get; set; }
    public AdminPlaybackToneMap[]? ToneMapCapabilities { get; set; }
}
public sealed class AdminPlaybackDevice { public string Path { get; set; } = ""; public string Description { get; set; } = ""; }
public sealed class AdminPlaybackBackend
{
    public string Backend { get; set; } = ""; public bool Verified { get; set; } public bool Skipped { get; set; }
    public string? Device { get; set; } public string? Error { get; set; }
}
public sealed class AdminPlaybackToneMap { public string Mode { get; set; } = ""; public string? Backend { get; set; } }
public sealed class AdminPlaybackHardwareNode
{
    public string NodeName { get; set; } = ""; public string NodeUrl { get; set; } = "";
    public string? Error { get; set; } public string[]? RenderDevices { get; set; }
}
public sealed class AdminPlaybackNodeStats
{
    public string? SampledAt { get; set; }
    public DashboardSystemStats? System { get; set; }
    public DashboardGpuStats[]? Gpu { get; set; }
    public AdminPlaybackNodeBuild? Build { get; set; }
    public DashboardResourceAttribution? Attribution { get; set; }
}
public sealed class AdminPlaybackNodeBuild { public string? Display { get; set; } public bool? Dirty { get; set; } }
public sealed class AdminPlaybackNodeCommand
{
    public string? NodeId { get; set; }
    public bool? Healthy { get; set; }
    public bool? HealthPersisted { get; set; }
    public int? ActiveJobs { get; set; }
    public int? EgressKbps { get; set; }
    public string? Status { get; set; }
    public string? Resolved { get; set; }
    public string? CapabilityHash { get; set; }
    public bool? CapabilitiesRefreshed { get; set; }
    public bool? DrainRequested { get; set; }
    public string? DrainState { get; set; }
    public override string ToString() => nameof(AdminPlaybackNodeCommand);
}
public sealed class AdminPlaybackTask
{
    public string Key { get; set; } = ""; public string Name { get; set; } = ""; public string? Description { get; set; }
    public string State { get; set; } = ""; public AdminPlaybackTaskExecution? LastExecution { get; set; }
}
public sealed class AdminPlaybackTaskExecution
{
    public string? CompletedAt { get; set; } public string? Status { get; set; }
    public Dictionary<string, System.Text.Json.JsonElement>? ResultData { get; set; }
}
public sealed record AdminPlaybackSupport(bool Trickplay, bool DetectionKindSettings, AdminPlaybackHardware? Hardware,
    AdminPlaybackNodeWindow? Nodes, long? PreviewsToRemake, IReadOnlyList<AdminPlaybackTask> Tasks);
