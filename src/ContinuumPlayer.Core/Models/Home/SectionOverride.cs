namespace ContinuumPlayer.Core.Models.Home;

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
}

public class SaveOverridesRequest
{
    public string Scope { get; set; } = "";
    public string? LibraryId { get; set; }
    public List<SectionOverride> Overrides { get; set; } = [];
}

public class ProfileSectionOverridesResponse
{
    public List<SectionOverride> Overrides { get; set; } = [];
}

public class SidebarPin
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
}
