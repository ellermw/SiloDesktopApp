using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

public class LibraryProviderChainEntry
{
    [JsonPropertyName("plugin_installation_id")]
    public int PluginInstallationId { get; set; }
    [JsonPropertyName("capability_id")]
    public string CapabilityId { get; set; } = "";
    [JsonPropertyName("provider_slug")]
    public string ProviderSlug { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; }
}

public class LibraryProviderChainResponse
{
    public Dictionary<string, List<LibraryProviderChainEntry>> Levels { get; set; } = new();
}

public class SetLibraryChainEntry
{
    [JsonPropertyName("plugin_installation_id")]
    public int PluginInstallationId { get; set; }
    [JsonPropertyName("capability_id")]
    public string CapabilityId { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; }
}

public class FilesystemBrowseEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

public class FilesystemBrowseResponse
{
    public string Path { get; set; } = "/";
    public string Parent { get; set; } = "/";
    public List<FilesystemBrowseEntry> Entries { get; set; } = [];
}

public class SetLibraryChainRequest
{
    public Dictionary<string, List<SetLibraryChainEntry>> Levels { get; set; } = new();
}
