namespace SiloPlayer.Core.Models.Admin;

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
    public string? GroupId { get; set; }
    public bool Featured { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropThumbhash { get; set; }
    public string? SourceUrl { get; set; }
    public Dictionary<string, object>? QueryDefinition { get; set; }
    public Dictionary<string, object>? SortConfig { get; set; }
    public Dictionary<string, object>? SourceConfig { get; set; }
    public string LastSyncStatus { get; set; } = "idle";
    public string LastSyncMessage { get; set; } = "";
    public string? LastSyncAt { get; set; }
    public string? SyncSchedule { get; set; }
    public string? NextSyncAt { get; set; }
    public string? ManagementMode { get; set; }
    public string? ManagementSource { get; set; }
    public string? ManagementKey { get; set; }
    public int ItemCount { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class CreateLibraryCollectionRequest
{
    public int? LibraryId { get; set; }
    public List<int>? LibraryIds { get; set; }
    public string? Slug { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? CollectionType { get; set; }
    public string? Visibility { get; set; }
    public int? SortOrder { get; set; }
    public string? GroupId { get; set; }
    public bool? Featured { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public string? PosterSourceUrl { get; set; }
    public string? BackdropSourceUrl { get; set; }
    public string? SourceUrl { get; set; }
    public Dictionary<string, object>? QueryDefinition { get; set; }
    public Dictionary<string, object>? SortConfig { get; set; }
    public Dictionary<string, object>? SourceConfig { get; set; }
    public string? SyncSchedule { get; set; }
}

public class UpdateLibraryCollectionRequest
{
    public int? LibraryId { get; set; }
    public List<int>? LibraryIds { get; set; }
    public string? Slug { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? CollectionType { get; set; }
    public string? Visibility { get; set; }
    public int? SortOrder { get; set; }
    public string? GroupId { get; set; }
    public bool? Featured { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public string? PosterSourceUrl { get; set; }
    public string? BackdropSourceUrl { get; set; }
    public string? SourceUrl { get; set; }
    public Dictionary<string, object>? QueryDefinition { get; set; }
    public Dictionary<string, object>? SortConfig { get; set; }
    public Dictionary<string, object>? SourceConfig { get; set; }
}

public class ImportMDBListCollectionRequest
{
    public int LibraryId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Url { get; set; } = "";
    public int? Limit { get; set; }
    public bool? Featured { get; set; }
    public string? PosterSourceUrl { get; set; }
    public string? BackdropSourceUrl { get; set; }
}

public class ImportMDBListCollectionResponse
{
    public LibraryCollection Collection { get; set; } = new();
    public LibraryCollectionSyncRun? SyncRun { get; set; }
}

public class ImportTMDBCollectionRequest
{
    public int LibraryId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Preset { get; set; } = "";
    public string? TimeWindow { get; set; }
    public string MediaType { get; set; } = "";
    public int? Limit { get; set; }
    public bool? Featured { get; set; }
    public string? PosterSourceUrl { get; set; }
    public string? BackdropSourceUrl { get; set; }
}

public class ImportTMDBCollectionResponse
{
    public LibraryCollection Collection { get; set; } = new();
    public LibraryCollectionSyncRun? SyncRun { get; set; }
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
    public List<LibraryCollectionGroup> Groups { get; set; } = [];
}

public class ImportTraktCollectionRequest
{
    public int LibraryId { get; set; }
    public List<int>? LibraryIds { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Preset { get; set; } = "";
    public string MediaType { get; set; } = "";
    public int? Limit { get; set; }
    public bool Featured { get; set; }
    public string? ManagementMode { get; set; }
    public string? ManagementSource { get; set; }
    public string? ManagementKey { get; set; }
}

public class ImportTraktCollectionResponse
{
    public LibraryCollection Collection { get; set; } = new();
    public LibraryCollectionSyncRun? SyncRun { get; set; }
}

public class BulkCreateSectionsResponse
{
    public int Created { get; set; }
}

public class LibraryCollectionGroup
{
    public string Id { get; set; } = "";
    public int LibraryId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Kind { get; set; } = "regular";
    public string DefaultSortMode { get; set; } = "manual";
    public int SortOrder { get; set; }
}

public class LibraryCollectionGroupListResponse
{
    public List<LibraryCollectionGroup> Groups { get; set; } = [];
    public int UngroupedSortOrder { get; set; } = 9999;
}

public class CreateLibraryCollectionGroupRequest
{
    public string Name { get; set; } = "";
    public string? Slug { get; set; }
    public string? DefaultSortMode { get; set; }
}

public class UpdateLibraryCollectionGroupRequest
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? DefaultSortMode { get; set; }
}

public class ReorderLibraryCollectionGroupsRequest
{
    public List<string> Ids { get; set; } = [];
}

public class ReorderLibraryCollectionsInGroupRequest
{
    public List<string> Ids { get; set; } = [];
}

public class ReorderAdminCollectionsRequest
{
    public int LibraryId { get; set; }
    public List<string> OrderedIds { get; set; } = [];
    public string? GroupId { get; set; }
}

public class LibraryTabCollection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public int ItemCount { get; set; }
    public bool Featured { get; set; }
    public string? CreatorProfileId { get; set; }
}

public class LibraryTabGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "regular";
    public string SortMode { get; set; } = "manual";
    public int SortOrder { get; set; }
    public List<LibraryTabCollection> Collections { get; set; } = [];
}

public class LibraryTabUngrouped
{
    public int SortOrder { get; set; } = 9999;
    public List<LibraryTabCollection> Collections { get; set; } = [];
}

public class LibraryTabResponse
{
    public int LibraryId { get; set; }

    /// <summary>
    /// Legacy flat admin collection list. Present for older desktop clients and
    /// used as a fallback when grouped tab data is unavailable.
    /// </summary>
    public List<LibraryCollection> Collections { get; set; } = [];

    public List<LibraryTabGroup> Groups { get; set; } = [];
    public LibraryTabUngrouped? Ungrouped { get; set; }
}
