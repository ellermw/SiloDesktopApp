using SiloPlayer.Core.Models.Plugins;

namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminAutoscanSettings
{
    public bool Enabled { get; set; }
    public int DefaultPollIntervalSeconds { get; set; }
    public int DebounceSeconds { get; set; }
}

public sealed class AdminAutoscanSettingsResult
{
    public AdminAutoscanSettings? Settings { get; set; }
    public string RescheduleState { get; set; } = "";
}

public sealed class AdminAutoscanPathRewrite
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class AdminAutoscanSource
{
    public string Id { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string CapabilityId { get; set; } = "";
    public string? ConnectionId { get; set; }
    public bool Enabled { get; set; }
    public string DeliveryMode { get; set; } = "";
    public int? PollIntervalSeconds { get; set; }
    public List<AdminAutoscanPathRewrite> PathRewrites { get; set; } = [];
    public Dictionary<string, string> SourceConfig { get; set; } = [];
    public string Label { get; set; } = "";
    public string? LastRunAt { get; set; }
    public string? LastError { get; set; }
    public bool WebhookConfigured { get; set; }
    public string? WebhookUrl { get; set; }
    public string? WebhookSecretSuffix { get; set; }
    public string? WebhookLastReceivedAt { get; set; }
    public string? WebhookLastErrorAt { get; set; }
    public string? WebhookLastErrorMessage { get; set; }
}

public sealed class AdminAutoscanSourceWrite
{
    public string? ConnectionId { get; set; }
    public bool Enabled { get; set; }
    public string DeliveryMode { get; set; } = "poll";
    public int? PollIntervalSeconds { get; set; }
    public List<AdminAutoscanPathRewrite> PathRewrites { get; set; } = [];
    public Dictionary<string, string> SourceConfig { get; set; } = [];
    public string Label { get; set; } = "";
}

public sealed class AdminAutoscanAvailableSource
{
    public string PluginId { get; set; } = "";
    public string CapabilityId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public AdminAutoscanDescriptor? Descriptor { get; set; }
}

public sealed class AdminAutoscanDescriptor
{
    public List<string> DeliveryModes { get; set; } = [];
    public string Connection { get; set; } = "optional";
    public List<string> ConnectionKinds { get; set; } = [];
    public bool EmitsNativePaths { get; set; }
    public string Summary { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public PluginAdminForm? ConfigForm { get; set; }
}

public sealed class AdminAutoscanConnection
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string? RequestIntegrationId { get; set; }
    public bool HasApiKey { get; set; }
}

public sealed class AdminAutoscanTriggerReceipt
{
    public string Key { get; set; } = "";
    public string State { get; set; } = "";
    public string ExecutionScope { get; set; } = "";
}

// Draft credentials are never returned by the connection reader or retained in page state.
public class AdminAutoscanConnectionTest
{
    public string? ConnectionId { get; set; }
    public string? RequestIntegrationId { get; set; }
    public string BaseUrl { get; set; } = "";
    public string ApiKeyRef { get; set; } = "";
}
public sealed class AdminAutoscanConnectionCreate : AdminAutoscanConnectionTest
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
}
public sealed class AdminAutoscanConnectionCheck
{
    public bool Ok { get; set; }
    public string? Version { get; set; }
    public string? Error { get; set; }
}
public sealed class AdminAutoscanProposedRewrite
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int MatchDepth { get; set; }
}
public sealed class AdminAutoscanAmbiguousRoot
{
    public string Root { get; set; } = "";
    public List<string> Candidates { get; set; } = [];
}
public sealed class AdminAutoscanRewriteSuggestions
{
    public List<AdminAutoscanProposedRewrite> Proposed { get; set; } = [];
    public List<string> Unmatched { get; set; } = [];
    public List<AdminAutoscanAmbiguousRoot> Ambiguous { get; set; } = [];
    public List<string> Covered { get; set; } = [];
}
public sealed class AdminAutoscanRequestIntegration
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public Dictionary<string, System.Text.Json.JsonElement> PluginConfig { get; set; } = [];
}
