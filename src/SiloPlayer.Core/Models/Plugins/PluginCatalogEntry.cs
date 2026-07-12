namespace SiloPlayer.Core.Models.Plugins;

public class PluginCatalogEntry
{
    public int RepositoryId { get; set; }
    public string PluginId { get; set; } = "";
    public string Version { get; set; } = "";
    public string ArchiveUrl { get; set; } = "";
    public string SourceKind { get; set; } = "external";
    public string RepositoryName { get; set; } = "";
    public string? RepoUrl { get; set; }
    public PluginPresentation? Presentation { get; set; }
    public List<PluginCapability> Capabilities { get; set; } = [];
    public List<PluginConfigSchema> GlobalConfigSchema { get; set; } = [];
    public List<PluginConfigSchema> UserConfigSchema { get; set; } = [];
    public List<PluginRoute> Routes { get; set; } = [];
    public List<PluginAsset> Assets { get; set; } = [];
    public Dictionary<string, object>? Metadata { get; set; }
}

public class PluginPresentation
{
    public string DisplayName { get; set; } = "";
    public string Summary { get; set; } = "";
    public string DescriptionMarkdown { get; set; } = "";
    public string SetupMarkdown { get; set; } = "";
    public string HomepageUrl { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string SupportUrl { get; set; } = "";
    public string ChangelogUrl { get; set; } = "";
    public string PublisherName { get; set; } = "";
    public string PublisherUrl { get; set; } = "";
    public string LicenseSpdx { get; set; } = "";
}

public class PluginCatalogSettings
{
    public bool IncludeApprovedCommunityPlugins { get; set; }
    public int ApprovedCommunityPluginCount { get; set; }
    public int InstalledCommunityPluginCount { get; set; }
    public int MigratedPluginCount { get; set; }
    public bool CommunityUpdatesPaused { get; set; }
}
