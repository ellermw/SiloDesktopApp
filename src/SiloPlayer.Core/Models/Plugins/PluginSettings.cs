namespace SiloPlayer.Core.Models.Plugins;

public class PluginSettingsSummary
{
    [System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; set; }
    public string PluginId { get; set; } = "";
    public string Version { get; set; } = "";
    public List<PluginConfigSchema> UserConfigSchema { get; set; } = [];
    public List<PluginRoute> Routes { get; set; } = [];
    public List<PluginAsset> Assets { get; set; } = [];
    public string? Category { get; set; }
}

public class PluginSettingsListResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public List<PluginSettingsSummary> Installations { get; set; } = [];
}

public class PluginSettingsDetailResponse
{
    public PluginSettingsSummary Installation { get; set; } = new();
    public Dictionary<string, string> Values { get; set; } = new();
}

public class UpdatePluginSettingsRequest
{
    public Dictionary<string, string> Values { get; set; } = new();
}
