namespace SiloPlayer.Core.Models.Home;

public class SettingsSectionEntry : SiloPlayer.Core.Localization.IServerTitled, System.Text.Json.Serialization.IJsonOnDeserialized
{
    public string Id { get; set; } = "";
    public string SectionType { get; set; } = "";
    /// <summary>The stored title an editor edits and saves; show <see cref="DisplayTitle"/>.</summary>
    public string Title { get; set; } = "";
    public string? DefaultTitle { get; set; }
    public string? TitleKey { get; set; }
    public Dictionary<string, string>? TitleArgs { get; set; }
    /// <summary>Catalog key for <see cref="DefaultTitle"/>, the administrator's title a renamed row can return to.</summary>
    public string? DefaultTitleKey { get; set; }
    public Dictionary<string, string>? DefaultTitleArgs { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? KeyedTitle { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? KeyedDefaultTitle { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayTitle => SiloPlayer.Core.Localization.SectionTitles.Display(this);
    [System.Text.Json.Serialization.JsonIgnore] public string? DisplayDefaultTitle => DefaultTitle == null ? null
        : SiloPlayer.Core.Localization.SectionTitles.Display(DefaultTitle, DefaultTitleKey, DefaultTitleArgs, KeyedDefaultTitle);
    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
    {
        TitleKey = SiloPlayer.Core.Localization.SectionTitles.Normalize(TitleKey);
        KeyedTitle = TitleKey == null ? null : Title;
        DefaultTitleKey = SiloPlayer.Core.Localization.SectionTitles.Normalize(DefaultTitleKey);
        KeyedDefaultTitle = DefaultTitleKey == null ? null : DefaultTitle;
    }
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
