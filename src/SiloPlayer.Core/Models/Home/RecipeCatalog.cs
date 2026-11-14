namespace SiloPlayer.Core.Models.Home;

public class RecipeCatalogResponse
{
    [System.Text.Json.Serialization.JsonIgnore] public bool AllowAdminOnlyRecipes { get; set; }
    public Dictionary<string, List<RecipeDefinition>> Categories { get; set; } = [];
}

public class RecipeDefinition
{
    public string Type { get; set; } = "";
    public string Category { get; set; } = "";
    public List<GalleryPreset> Presets { get; set; } = [];
    public bool AvoidDuplicates { get; set; }
    public bool SupportsRotation { get; set; }
    public bool AdminOnly { get; set; }
}

public class GalleryPreset
{
    public string Key { get; set; } = "";
    /// <summary>The preset's English name. A new row saves this, never the translated label: saved titles are data.</summary>
    public string DisplayName { get; set; } = "";
    public string Icon { get; set; } = "";
    public string DescriptionShort { get; set; } = "";
    public string? DescriptionLong { get; set; }
    public string? DisplayNameKey { get; set; }
    public string? DescriptionShortKey { get; set; }
    public string? DescriptionLongKey { get; set; }
    /// <summary>Display-only label for the gallery.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string DisplayNameText => SiloPlayer.Core.Localization.AppStrings.ServerText(DisplayNameKey, null, DisplayName);
    [System.Text.Json.Serialization.JsonIgnore] public string DescriptionShortText => SiloPlayer.Core.Localization.AppStrings.ServerText(DescriptionShortKey, null, DescriptionShort);
    [System.Text.Json.Serialization.JsonIgnore] public string? DescriptionLongText => DescriptionLong == null ? null
        : SiloPlayer.Core.Localization.AppStrings.ServerText(DescriptionLongKey, null, DescriptionLong);
    public Dictionary<string, object> DefaultParams { get; set; } = [];
}
