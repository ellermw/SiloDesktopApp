namespace SiloPlayer.Core.Models.Home;

public class SettingsSectionEntry
{
    public string Id { get; set; } = "";
    public string SectionType { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Featured { get; set; }
    public int ItemLimit { get; set; }
    public bool Hidden { get; set; }
    public bool IsCustom { get; set; }
    public bool Customized { get; set; }
    public int Position { get; set; }
    public Dictionary<string, object>? Config { get; set; }
}

public class SettingsSectionsResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("items")]
    public List<SettingsSectionEntry> Sections { get; set; } = [];
}
