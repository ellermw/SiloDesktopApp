namespace ContinuumPlayer.Core.Models.Collections;

public sealed class CollectionTemplateCatalog
{
    public List<CollectionTemplateCategory> Categories { get; set; } = [];
}

public sealed class CollectionTemplateCategory
{
    public string Category { get; set; } = "";
    public string Label { get; set; } = "";
    public List<CollectionTemplate> Templates { get; set; } = [];
}

public sealed class CollectionTemplate
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Category { get; set; } = "";
    public string Source { get; set; } = "";
    public string MediaKind { get; set; } = "";
    public int DefaultLimit { get; set; }
    public string? DefaultSyncSchedule { get; set; }
    public bool RequiresProfile { get; set; }
    public bool Featured { get; set; }
    public List<string> Tags { get; set; } = [];
    public CollectionTemplateTmdbSpec? Tmdb { get; set; }
    public CollectionTemplateTraktSpec? Trakt { get; set; }
    public CollectionTemplateMdblistSpec? Mdblist { get; set; }
}

public sealed class CollectionTemplateTmdbSpec
{
    public string Preset { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string? TimeWindow { get; set; }
}

public sealed class CollectionTemplateTraktSpec
{
    public string Preset { get; set; } = "";
    public string MediaType { get; set; } = "";
}

public sealed class CollectionTemplateMdblistSpec
{
    public string Url { get; set; } = "";
}

public sealed class ImportUserCollectionResponse
{
    public Collection Collection { get; set; } = new();
    public UserCollectionSyncResult? Sync { get; set; }
}

public sealed class UserCollectionSyncResult
{
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public int ItemsMatched { get; set; }
    public int ItemsUnmatched { get; set; }
    public string StartedAt { get; set; } = "";
    public string CompletedAt { get; set; } = "";
}

public class ImportUserCollectionRequest
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? Limit { get; set; }
    public string? SyncSchedule { get; set; }
    public bool? IsShared { get; set; }
    public List<int>? LibraryIds { get; set; }
}

public sealed class ImportUserMDBListCollectionRequest : ImportUserCollectionRequest
{
    public string Url { get; set; } = "";
}

public sealed class ImportUserTMDBCollectionRequest : ImportUserCollectionRequest
{
    public string Preset { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string? TimeWindow { get; set; }
}

public sealed class ImportUserTraktCollectionRequest : ImportUserCollectionRequest
{
    public string Preset { get; set; } = "";
    public string MediaType { get; set; } = "";
}

public sealed class MDBListDiscoveryResponse
{
    public bool Configured { get; set; }
    public List<MDBListListSummary> Lists { get; set; } = [];
}

public sealed class MDBListListSummary
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public string Mediatype { get; set; } = "";
    public int Items { get; set; }
    public int Likes { get; set; }
    public string? Url { get; set; }
}
