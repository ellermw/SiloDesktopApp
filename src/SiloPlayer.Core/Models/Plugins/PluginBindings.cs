namespace SiloPlayer.Core.Models.Plugins;

public class PluginAuthBinding
{
    public string CapabilityId { get; set; } = "";
    public bool Enabled { get; set; }
    public int DisplayOrder { get; set; }
    public bool AutoProvision { get; set; }
    public bool DefaultLogin { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class PluginTaskBinding
{
    public string CapabilityId { get; set; } = "";
    public bool Enabled { get; set; }
    public Dictionary<string, object>? Trigger { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class PluginAnalyzerBinding
{
    public int MediaFolderId { get; set; }
    public int PluginInstallationId { get; set; }
    public bool Enabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class SavePluginAuthBindingRequest
{
    public string CapabilityId { get; set; } = "";
    public bool Enabled { get; set; }
    public int DisplayOrder { get; set; }
    public bool AutoProvision { get; set; }
    public bool DefaultLogin { get; set; }
}

public class SavePluginTaskBindingRequest
{
    public bool Enabled { get; set; }
    public Dictionary<string, object>? Trigger { get; set; }
}

public class SavePluginAnalyzerBindingsRequest
{
    public List<PluginAnalyzerBindingEntry> Bindings { get; set; } = [];
}

public class PluginAnalyzerBindingEntry
{
    public int MediaFolderId { get; set; }
    public bool Enabled { get; set; }
}

public class PluginLegacyMetadataImportResponse
{
    public int MigratedProviderCount { get; set; }
}

public class PluginTaskBindingUpdateResponse
{
    public bool RestartRequired { get; set; }
}
