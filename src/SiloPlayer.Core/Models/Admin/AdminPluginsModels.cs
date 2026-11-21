using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

// Wire models of the official internal/apiv2/admin_plugin_{installations,repositories,configuration,lifecycle,
// catalog_settings,uploads,provisioning}.go at 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e. Read-only unless noted:
// only the *Write / *Install types below are ever serialized into a request.

/// <summary>One installation (id set) or one catalog entry (id empty) as the official page lists them.</summary>
public sealed class AdminPlugin
{
    public string Id { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string Version { get; set; } = "";
    public string? RepositoryId { get; set; }
    public string SourceKind { get; set; } = "";
    public string? RepositoryName { get; set; }
    public string? RepoUrl { get; set; }
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; }
    public string? UpdatePolicy { get; set; }
    public string? AvailableVersion { get; set; }
    public bool UpdatesPaused { get; set; }
    public bool ConnectionCheckSupported { get; set; }
    public AdminPluginRuntime? Runtime { get; set; }
    public AdminPluginSignature? Signature { get; set; }
    public AdminPluginUpdateFailure? UpdateFailure { get; set; }
    public AdminPluginInfo? Presentation { get; set; }
    public AdminPluginOwnership? EnvManaged { get; set; }
    public List<AdminPluginCapabilityInfo> Capabilities { get; set; } = [];
    public List<AdminPluginSchema> GlobalConfigSchema { get; set; } = [];
    public List<AdminPluginConfig> GlobalConfigs { get; set; } = [];
    public List<AdminPluginAuthBinding> AuthBindings { get; set; } = [];
    public List<AdminPluginTaskBinding> TaskBindings { get; set; } = [];
    public List<AdminPluginRoute> Routes { get; set; } = [];
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class AdminPluginInfo
{
    public string DisplayName { get; set; } = "";
    public string Summary { get; set; } = "";
    public string DescriptionMarkdown { get; set; } = "";
    public string SetupMarkdown { get; set; } = "";
    public string PublisherName { get; set; } = "";
    public string PublisherUrl { get; set; } = "";
    public string LicenseSpdx { get; set; } = "";
    public string HomepageUrl { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SupportUrl { get; set; } = "";
    public string ChangelogUrl { get; set; } = "";
}

public sealed class AdminPluginRuntime
{
    public bool Resident { get; set; }
    public string State { get; set; } = "";
    public int RestartCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastStartedAt { get; set; }
    public DateTimeOffset? NextRestartAt { get; set; }
}

public sealed class AdminPluginSignature
{
    public string State { get; set; } = "";
    public string? SourceRepo { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}

public sealed class AdminPluginUpdateFailure
{
    public string Version { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset? FailedAt { get; set; }
}

/// <summary>What the deployment owns on an installation: its package pin and, per entry or capability, owned fields.</summary>
public sealed class AdminPluginOwnership
{
    public string? VersionPin { get; set; }
    public Dictionary<string, List<string>> ConfigFields { get; set; } = [];
    public Dictionary<string, List<string>> BindingFields { get; set; } = [];
}

public sealed class AdminPluginRoute
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Method { get; set; } = "";
    public string Access { get; set; } = "";
    public bool Navigable { get; set; }
    public string NavigationKind { get; set; } = "";
    public string NavigationLabel { get; set; } = "";
    public bool StaticAsset { get; set; }
}

public sealed class AdminPluginCapabilityInfo
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Subscriptions { get; set; } = [];
    public string? SignInMode { get; set; }
    public List<string>? EnvManaged { get; set; }
    public AdminPluginTrigger? DefaultSchedule { get; set; }
}

/// <summary>A task trigger as plugin task bindings and manifest default schedules carry it.</summary>
public sealed class AdminPluginTrigger
{
    public string Type { get; set; } = "";
    public long? IntervalMs { get; set; }
    public string? TimeOfDay { get; set; }
    public int? DayOfWeek { get; set; }
    public long? MaxRuntimeMs { get; set; }
}

public sealed class AdminPluginSchema
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string JsonSchema { get; set; } = "";
    public bool Required { get; set; }
    public AdminPluginForm? AdminForm { get; set; }
}

public sealed class AdminPluginForm
{
    public List<AdminPluginField> Fields { get; set; } = [];
    public List<AdminPluginFormSection> Sections { get; set; } = [];
    public string? SubmitLabel { get; set; }
}

public sealed class AdminPluginFormSection
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool Collapsible { get; set; }
    public bool CollapsedDefault { get; set; }
    public List<string> FieldKeys { get; set; } = [];
    public List<AdminPluginCondition> ShowWhen { get; set; } = [];
}

public sealed class AdminPluginCondition
{
    public string Field { get; set; } = "";
    [JsonPropertyName("equals")] public List<string> Values { get; set; } = [];
}

public sealed class AdminPluginField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public string Control { get; set; } = "TEXT";
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public bool Secret { get; set; }
    public bool Multiline { get; set; }
    public int Rows { get; set; }
    public bool DynamicOptions { get; set; }
    public string? ExclusiveGroupField { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement DefaultValue { get; set; }
    public List<AdminPluginOption> Options { get; set; } = [];
    public List<AdminPluginCondition> ShowWhen { get; set; } = [];
    public AdminPluginFieldValidation? Validation { get; set; }
}

public sealed class AdminPluginFieldValidation
{
    public bool HasMin { get; set; }
    public double Min { get; set; }
    public bool HasMax { get; set; }
    public double Max { get; set; }
    public string? Pattern { get; set; }
    public int MinLength { get; set; }
    public int MaxLength { get; set; }
}

public sealed class AdminPluginOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>A saved global configuration entry: public fields only; secrets are only listed by name.</summary>
public sealed class AdminPluginConfig
{
    public string Key { get; set; } = "";
    public Dictionary<string, JsonElement> Value { get; set; } = [];
    public List<string> ConfiguredSecrets { get; set; } = [];
}

/// <summary>Request body of PUT/POST .../config and .../config/test.</summary>
public sealed class AdminPluginConfigWrite
{
    public string Key { get; set; } = "";
    public Dictionary<string, JsonElement> Value { get; set; } = [];
    public List<string> ClearSecrets { get; set; } = [];
}

public sealed class AdminPluginAuthBinding
{
    public string CapabilityId { get; set; } = "";
    public bool Enabled { get; set; }
    public int DisplayOrder { get; set; }
    public bool AutoProvision { get; set; } = true;
    public bool DefaultLogin { get; set; }
    public string? CallbackMode { get; set; }
    public bool CallbackPending { get; set; }
    public List<string>? EnvManaged { get; set; }
}

public sealed class AdminPluginTaskBinding
{
    public string CapabilityId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public Dictionary<string, JsonElement> Trigger { get; set; } = [];
}

public sealed class AdminPluginRepository
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
    public string SourceKind { get; set; } = "";
    public bool Managed { get; set; }
    public DateTimeOffset? LastFetchedAt { get; set; }
}

public sealed class AdminPluginCatalogSettings { [JsonRequired] public bool IncludeApprovedCommunityPlugins { get; set; } }

public sealed class AdminPluginCatalogStatus
{
    public int ApprovedCommunityPluginCount { get; set; }
    public int InstalledCommunityPluginCount { get; set; }
    public int MigratedPluginCount { get; set; }
    public bool CommunityUpdatesPaused { get; set; }
}

/// <summary>Request body of POST /installations: one catalog version. The official page never installs from a bare archive URL.</summary>
public sealed class AdminPluginInstall
{
    public string RepositoryId { get; set; } = "";
    public string PluginId { get; set; } = "";
    public string Version { get; set; } = "";
}

public sealed class AdminPluginCheck { public bool Success { get; set; } public string Message { get; set; } = ""; }
public sealed class AdminPluginRestartResult { public bool RestartRequired { get; set; } }

public sealed class AdminPluginProvisionStatus
{
    public string PluginId { get; set; } = "";
    public string Version { get; set; } = "";
    public string State { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class AdminPluginUploadSession
{
    public string UploadId { get; set; } = "";
    public string Filename { get; set; } = "";
    public long SizeBytes { get; set; }
    public int ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public int ReceivedChunks { get; set; }
    public long ReceivedBytes { get; set; }
    public bool Complete { get; set; }
}

/// <summary>The lifecycle requests one installation accepts besides an update-policy change.</summary>
public enum AdminPluginCommand { Enable, Disable, Update, Restart, Uninstall }
