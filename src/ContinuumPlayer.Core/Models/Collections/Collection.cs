namespace ContinuumPlayer.Core.Models.Collections;

public class Collection
{
    public string Id { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string CreatorProfileId { get; set; } = "";
    public string Name { get; set; } = "";
    public string CollectionType { get; set; } = "manual";
    public bool IsShared { get; set; }
    public List<string> AllowedProfileIds { get; set; } = [];
    public QueryDefinition? QueryDefinition { get; set; }
    public Dictionary<string, object>? SortConfig { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class CollectionsResponse
{
    public List<Collection> Collections { get; set; } = [];
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
