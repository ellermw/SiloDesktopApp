namespace SiloPlayer.Core.Models.Catalog;

public sealed class LibraryCollection
{
    public string Id { get; set; } = "";
    public int LibraryId { get; set; }
    public string Title { get; set; } = "";
    public string CollectionType { get; set; } = "manual";
    public Dictionary<string, object>? SourceConfig { get; set; }
    public string Visibility { get; set; } = "visible";
    public int SortOrder { get; set; }
    public string? GroupId { get; set; }
    public bool Featured { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public int ItemCount { get; set; }
    /// <summary>Curated description and widest stored backdrop (empty when none); the collection page header reads these.</summary>
    public string? Description { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropThumbhash { get; set; }
}

public sealed class LibraryTabCollection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public int ItemCount { get; set; }
    public bool Featured { get; set; }
    public string? CreatorProfileId { get; set; }
}

public sealed class LibraryTabGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "regular";
    public string SortMode { get; set; } = "manual";
    public int SortOrder { get; set; }
    public List<LibraryTabCollection> Collections { get; set; } = [];
}

public sealed class LibraryTabUngrouped
{
    public int SortOrder { get; set; } = 9999;
    public List<LibraryTabCollection> Collections { get; set; } = [];
}

public sealed class LibraryTabResponse
{
    public int LibraryId { get; set; }
    public List<LibraryCollection> Collections { get; set; } = [];
    public List<LibraryTabGroup> Groups { get; set; } = [];
    public LibraryTabUngrouped? Ungrouped { get; set; }
}
