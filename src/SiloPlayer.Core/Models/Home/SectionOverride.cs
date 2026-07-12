using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Home;

public class SectionOverride
{
    public string? Id { get; set; }
    public string? SectionId { get; set; }
    public int? Position { get; set; }
    public bool? Hidden { get; set; }
    public string? SectionType { get; set; }
    public string? Title { get; set; }
    public bool? Featured { get; set; }
    public int? ItemLimit { get; set; }
    public Dictionary<string, object>? Config { get; set; }
    public bool? Removed { get; set; }
    public bool? IsUserAdded { get; set; }
    public string? UserSectionType { get; set; }
    public Dictionary<string, object>? UserConfig { get; set; }
    public string? UserTitle { get; set; }
}

public class SaveOverridesRequest
{
    public string Scope { get; set; } = "";
    public string? LibraryId { get; set; }
    public List<SectionOverride> Overrides { get; set; } = [];
}

public class ProfileSectionOverridesResponse
{
    public List<RawSectionOverride> Overrides { get; set; } = [];
}

/// <summary>The profile override read endpoint currently emits Go field names
/// verbatim. Keep this distinct from the snake_case PUT wire model so a load/save
/// round trip cannot erase recipe configuration.</summary>
public class RawSectionOverride
{
    [JsonPropertyName("ID")] public string Id { get; set; } = "";
    [JsonPropertyName("SectionID")] public string SectionId { get; set; } = "";
    [JsonPropertyName("Position")] public int? Position { get; set; }
    [JsonPropertyName("Hidden")] public bool Hidden { get; set; }
    [JsonPropertyName("Removed")] public bool Removed { get; set; }
    [JsonPropertyName("SectionType")] public string SectionType { get; set; } = "";
    [JsonPropertyName("Title")] public string Title { get; set; } = "";
    [JsonPropertyName("Featured")] public bool? Featured { get; set; }
    [JsonPropertyName("ItemLimit")] public int? ItemLimit { get; set; }
    [JsonPropertyName("Config")] public string Config { get; set; } = "";
    [JsonPropertyName("IsUserAdded")] public bool IsUserAdded { get; set; }
    [JsonPropertyName("UserSectionType")] public string UserSectionType { get; set; } = "";
    [JsonPropertyName("UserConfig")] public string UserConfig { get; set; } = "";
    [JsonPropertyName("UserTitle")] public string UserTitle { get; set; } = "";
}

public class SidebarPin
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}
