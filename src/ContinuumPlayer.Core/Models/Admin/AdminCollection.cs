namespace ContinuumPlayer.Core.Models.Admin;

public class LibraryCollection
{
    public string Id { get; set; } = "";
    public int LibraryId { get; set; }
    public List<int> LibraryIds { get; set; } = [];
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CollectionType { get; set; } = "manual";
    public string Visibility { get; set; } = "visible";
    public int SortOrder { get; set; }
    public bool Featured { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public string? SourceUrl { get; set; }
    public string LastSyncStatus { get; set; } = "idle";
    public string LastSyncMessage { get; set; } = "";
    public string? LastSyncAt { get; set; }
    public int ItemCount { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class CreateLibraryCollectionRequest
{
    public int? LibraryId { get; set; }
    public List<int>? LibraryIds { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? CollectionType { get; set; }
    public string? Visibility { get; set; }
    public int? SortOrder { get; set; }
    public bool? Featured { get; set; }
    public string? SourceUrl { get; set; }
}

public class LibraryCollectionSyncRun
{
    public string Id { get; set; } = "";
    public string CollectionId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public int ItemsAdded { get; set; }
    public int ItemsRemoved { get; set; }
    public int ItemsMatched { get; set; }
    public int ItemsUnmatched { get; set; }
    public List<string> Warnings { get; set; } = [];
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string CreatedAt { get; set; } = "";
}

public class AdminCollectionsResponse
{
    public List<LibraryCollection> Collections { get; set; } = [];
}
