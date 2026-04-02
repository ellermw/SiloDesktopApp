namespace ContinuumPlayer.Core.Models.Plugins;

public class PluginInstallation
{
    public int Id { get; set; }
    public int? RepositoryId { get; set; }
    public string PluginId { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public bool Enabled { get; set; }
    public List<PluginCapability> Capabilities { get; set; } = [];
    public List<PluginConfigSchema> GlobalConfigSchema { get; set; } = [];
    public List<PluginConfigSchema> UserConfigSchema { get; set; } = [];
    public List<PluginRoute> Routes { get; set; } = [];
    public List<PluginAsset> Assets { get; set; } = [];
    public Dictionary<string, object>? Metadata { get; set; }
    public List<PluginConfigValue> GlobalConfigs { get; set; } = [];
    public List<PluginAuthBinding> AuthBindings { get; set; } = [];
    public List<PluginTaskBinding> TaskBindings { get; set; } = [];
    public List<PluginAnalyzerBinding> AnalyzerBindings { get; set; } = [];
    public string UpdatePolicy { get; set; } = "";
    public string? AvailableVersion { get; set; }
    public List<string>? LegacyMetadataImportTypes { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

public class PluginInstallationsResponse
{
    public List<PluginInstallation> Installations { get; set; } = [];
}

public class InstallPluginRequest
{
    public int? RepositoryId { get; set; }
    public string? PluginId { get; set; }
    public string? Version { get; set; }
    public string? ArchiveUrl { get; set; }
}

public class UpdatePluginInstallationRequest
{
    public bool? Enabled { get; set; }
    public string? UpdatePolicy { get; set; }
}

public class SavePluginConfigRequest
{
    public string Key { get; set; } = "";
    public Dictionary<string, object> Value { get; set; } = new();
}

public class PluginConfigValue
{
    public string Key { get; set; } = "";
    public Dictionary<string, object> Value { get; set; } = new();
}
