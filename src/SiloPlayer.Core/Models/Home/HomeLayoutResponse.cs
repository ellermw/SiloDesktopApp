namespace SiloPlayer.Core.Models.Home;
public class HomeLayoutResponse
{
    public List<HomeSection> Sections { get; set; } = [];
}
public class HomeSection : SiloPlayer.Core.Localization.IServerTitled, System.Text.Json.Serialization.IJsonOnDeserialized
{
    public string Id { get; set; } = "";
    public string SectionType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? TitleKey { get; set; }
    public Dictionary<string, string>? TitleArgs { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? KeyedTitle { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayTitle => SiloPlayer.Core.Localization.SectionTitles.Display(this);
    void System.Text.Json.Serialization.IJsonOnDeserialized.OnDeserialized()
    {
        TitleKey = SiloPlayer.Core.Localization.SectionTitles.Normalize(TitleKey);
        KeyedTitle = TitleKey == null ? null : Title;
    }
    public bool Featured { get; set; }
    public int ItemLimit { get; set; }
    public bool IsCustom { get; set; }
    public bool Customized { get; set; }
}
