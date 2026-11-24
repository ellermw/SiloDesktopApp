using System.Text.Json.Serialization;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Models.Home;

/// <summary>
/// GET /api/v2/profile/sections/baseline (getProfileSectionBaseline): one page's
/// pre-override server sections for the acting profile. Home layout export records
/// <see cref="SectionTokens"/>; import compares them with <see cref="VisibleSections"/>.
/// </summary>
public sealed class ProfileSectionBaseline
{
    [JsonPropertyName("section_tokens")] public List<string> SectionTokens { get; set; } = [];
    [JsonPropertyName("visible_sections")] public List<ProfileSectionBaselineRow> VisibleSections { get; set; } = [];
}

/// <summary>One server row of a baseline. Its title is the server's own text, shown through the optional catalog key.</summary>
public sealed class ProfileSectionBaselineRow : IServerTitled, IJsonOnDeserialized
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("section_type")] public string SectionType { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("title_key")] public string? TitleKey { get; set; }
    [JsonPropertyName("title_args")] public Dictionary<string, string>? TitleArgs { get; set; }
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonIgnore] public string? KeyedTitle { get; set; }
    [JsonIgnore] public string DisplayTitle => SectionTitles.Display(this);
    void IJsonOnDeserialized.OnDeserialized()
    {
        TitleKey = SectionTitles.Normalize(TitleKey);
        KeyedTitle = TitleKey == null ? null : Title;
    }
}
