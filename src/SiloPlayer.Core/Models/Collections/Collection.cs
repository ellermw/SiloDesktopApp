namespace SiloPlayer.Core.Models.Collections;

public class Collection
{
    public string Id { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string CreatorProfileId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string CollectionType { get; set; } = "manual";
    public bool IsShared { get; set; }
    public List<string> AllowedProfileIds { get; set; } = [];
    public QueryDefinition? QueryDefinition { get; set; }
    public DisplayQueryDefinition? DisplayQueryDefinition { get; set; }
    public Dictionary<string, object>? SortConfig { get; set; }
    public int SortOrder { get; set; }
    public string? GroupId { get; set; }
    public string? SourceUrl { get; set; }
    public Dictionary<string, object>? SourceConfig { get; set; }
    public string? SyncSchedule { get; set; }
    public string? NextSyncAt { get; set; }
    public string? LastSyncAt { get; set; }
    public string? LastSyncStatus { get; set; }
    public string? LastSyncMessage { get; set; }
    public int ItemCount { get; set; }
    public bool IncludeInServerCollections { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class CollectionsResponse
{
    public List<Collection> Collections { get; set; } = [];
    public List<CollectionGroup> Groups { get; set; } = [];
}

public class CollectionGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string DefaultSortMode { get; set; } = "manual";
    public int SortOrder { get; set; }
}

public class ServerCollectionsResponse
{
    public List<ServerCollectionsLibrary> Libraries { get; set; } = [];
}

public class ServerCollectionsLibrary
{
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = "";
    public int TotalCount { get; set; }
    public List<ServerCollectionSummary> Collections { get; set; } = [];
}

public class ServerCollectionSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public int ItemCount { get; set; }
    public bool Featured { get; set; }
    public string? CreatorProfileId { get; set; }
}

public class ReorderCollectionsRequest
{
    public List<string> OrderedIds { get; set; } = [];
    public string? GroupId { get; set; }
}

public class ReorderCollectionGroupsRequest
{
    public List<string> OrderedIds { get; set; } = [];
}

public class QueryDefinition
{
    public List<int> LibraryIds { get; set; } = [];
    public string? MediaScope { get; set; }
    public string Match { get; set; } = "all";
    public List<QueryGroup> Groups { get; set; } = [];
    public QuerySort? Sort { get; set; }
    public int? Limit { get; set; }
}

public class QueryGroup
{
    public string Match { get; set; } = "all";
    public List<QueryRule> Rules { get; set; } = [];
}

public class DisplayQueryDefinition
{
    public string Match { get; set; } = "all";
    public List<QueryGroup> Groups { get; set; } = [];
}

public class CollectionCapabilitiesResponse
{
    public List<string> DisplayFilterFields { get; set; } = [];
    public CollectionDisplayFilterPresets DisplayFilterPresets { get; set; } = new();
    public bool CollectionDefaultSort { get; set; }
    public bool CollectionSortPreferences { get; set; }
    public bool EffectiveCollectionSort { get; set; }
}

public class CollectionDisplayFilterPresets
{
    public List<string> Watched { get; set; } = [];
    public List<string> Media { get; set; } = [];
}

public class QueryRule
{
    public string Field { get; set; } = "";
    public string Op { get; set; } = "";
    public object? Value { get; set; }
}

public class QuerySort
{
    public string Field { get; set; } = "added_at";
    public string Order { get; set; } = "desc";
}
