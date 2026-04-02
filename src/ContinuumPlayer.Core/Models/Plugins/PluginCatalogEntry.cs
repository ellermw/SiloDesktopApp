namespace ContinuumPlayer.Core.Models.Plugins;

public class PluginCatalogEntry
{
    public int RepositoryId { get; set; }
    public string PluginId { get; set; } = "";
    public string Version { get; set; } = "";
    public string ArchiveUrl { get; set; } = "";
    public List<PluginCapability> Capabilities { get; set; } = [];
    public List<PluginConfigSchema> GlobalConfigSchema { get; set; } = [];
    public List<PluginConfigSchema> UserConfigSchema { get; set; } = [];
    public List<PluginRoute> Routes { get; set; } = [];
    public List<PluginAsset> Assets { get; set; } = [];
    public Dictionary<string, object>? Metadata { get; set; }
}