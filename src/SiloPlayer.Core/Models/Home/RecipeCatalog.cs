namespace SiloPlayer.Core.Models.Home;

public class RecipeCatalogResponse
{
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
    public string DisplayName { get; set; } = "";
    public string Icon { get; set; } = "";
    public string DescriptionShort { get; set; } = "";
    public string? DescriptionLong { get; set; }
    public Dictionary<string, object> DefaultParams { get; set; } = [];
}
