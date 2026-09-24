using System.Text.Json.Serialization;
using System.Text.Json;

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
    [JsonPropertyName("items")]
    public List<RawSectionOverride> Overrides { get; set; } = [];
}

/// <summary>The v2 read endpoint emits snake_case fields and structured recipe
/// configuration. String accessors preserve the editor's existing JSON editing API.</summary>
public class RawSectionOverride
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("section_id")] public string SectionId { get; set; } = "";
    [JsonPropertyName("position")] public int? Position { get; set; }
    [JsonPropertyName("hidden")] public bool Hidden { get; set; }
    [JsonPropertyName("removed")] public bool Removed { get; set; }
    [JsonPropertyName("section_type")] public string SectionType { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("featured")] public bool? Featured { get; set; }
    [JsonPropertyName("item_limit")] public int? ItemLimit { get; set; }
    [JsonIgnore] public string Config { get; set; } = "";
    [JsonPropertyName("config")] public JsonElement? ConfigDocument
    {
        get => string.IsNullOrEmpty(Config) ? null : JsonSerializer.Deserialize<JsonElement>(Config);
        set => Config = value?.GetRawText() ?? "";
    }
    [JsonPropertyName("is_user_added")] public bool IsUserAdded { get; set; }
    [JsonPropertyName("user_section_type")] public string UserSectionType { get; set; } = "";
    [JsonIgnore] public string UserConfig { get; set; } = "";
    [JsonPropertyName("user_config")] public JsonElement? UserConfigDocument
    {
        get => string.IsNullOrEmpty(UserConfig) ? null : JsonSerializer.Deserialize<JsonElement>(UserConfig);
        set => UserConfig = value?.GetRawText() ?? "";
    }
    [JsonPropertyName("user_title")] public string UserTitle { get; set; } = "";
}

public class SidebarPin
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}
