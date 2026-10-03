namespace SiloPlayer.Core.Models.Collections;

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
    public int? DefaultSortOrder { get; set; }
    public string? DefaultSyncSchedule { get; set; }
    public string? PosterPath { get; set; }
    public bool RequiresProfile { get; set; }
    public bool Featured { get; set; }
    public List<string> Tags { get; set; } = [];
    public CollectionTemplateTmdbSpec? Tmdb { get; set; }
    public CollectionTemplateTraktSpec? Trakt { get; set; }
    public CollectionTemplateMdblistSpec? Mdblist { get; set; }
    public CollectionTemplateTmdbListSpec? TmdbList { get; set; }
    public CollectionTemplateTmdbCollectionSpec? TmdbCollection { get; set; }
    public CollectionTemplateTmdbDiscoverSpec? TmdbDiscover { get; set; }
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

public sealed class CollectionTemplateTmdbListSpec { public string Url { get; set; } = ""; }

public sealed class CollectionTemplateTmdbCollectionSpec
{
    public int CollectionId { get; set; }
}

public sealed class CollectionTemplateTmdbDiscoverSpec
{
    public string MediaType { get; set; } = "movie";
    public List<int> WithGenres { get; set; } = [];
    public List<int> WithoutGenres { get; set; } = [];
    public string SortBy { get; set; } = "popularity.desc";
    public int? VoteCountGte { get; set; }
    public double? VoteAverageGte { get; set; }
    public string? ReleaseDateGte { get; set; }
    public string? ReleaseDateLte { get; set; }
    public List<string> Certifications { get; set; } = [];
    public string? CertificationLte { get; set; }
    public int? WithRuntimeGte { get; set; }
    public int? WithRuntimeLte { get; set; }
    public string? OriginalLanguage { get; set; }
}

public sealed class CollectionTemplateBundleCatalog
{
    public List<CollectionTemplateBundle> Bundles { get; set; } = [];
}

public sealed class CollectionTemplateBundle
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> TemplateIds { get; set; } = [];
}

public sealed class ApplyCollectionTemplateBundleRequest
{
    public List<int> LibraryIds { get; set; } = [];
    public bool? DryRun { get; set; }
    public bool? DeleteExisting { get; set; }
    public ApplyCollectionTemplateBundleFeaturedRequest? Featured { get; set; }
}

public sealed class ApplyCollectionTemplateBundleFeaturedRequest
{
    public ApplyCollectionTemplateBundleHomeFeaturedRequest? Home { get; set; }
    public Dictionary<string, string>? Libraries { get; set; }
}

public sealed class ApplyCollectionTemplateBundleHomeFeaturedRequest
{
    public int LibraryId { get; set; }
    public string TemplateId { get; set; } = "";
}

public sealed class CollectionTemplateBundleApplyEntry
{
    public string TemplateId { get; set; } = "";
    public string TemplateTitle { get; set; } = "";
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = "";
    public string? CollectionId { get; set; }
    public string? Reason { get; set; }
}

public sealed class CollectionTemplateBundleCollectionEntry
{
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = "";
    public string? CollectionId { get; set; }
    public string? CollectionTitle { get; set; }
    public string? Reason { get; set; }
}

public sealed class CollectionTemplateBundleFeaturedEntry
{
    public string Surface { get; set; } = "";
    public int? LibraryId { get; set; }
    public string? LibraryName { get; set; }
    public string TemplateId { get; set; } = "";
    public string TemplateTitle { get; set; } = "";
    public string? CollectionId { get; set; }
    public string? SectionId { get; set; }
    public string? Reason { get; set; }
}

public sealed class ApplyCollectionTemplateBundleResponse
{
    public string BundleId { get; set; } = "";
    public bool DryRun { get; set; }
    public bool? DeleteExisting { get; set; }
    public List<CollectionTemplateBundleCollectionEntry> Deleted { get; set; } = [];
    public List<CollectionTemplateBundleCollectionEntry> DeleteSkipped { get; set; } = [];
    public List<CollectionTemplateBundleCollectionEntry> DeleteFailed { get; set; } = [];
    public List<CollectionTemplateBundleApplyEntry> Created { get; set; } = [];
    public List<CollectionTemplateBundleApplyEntry> Skipped { get; set; } = [];
    public List<CollectionTemplateBundleApplyEntry> Failed { get; set; } = [];
    public List<CollectionTemplateBundleApplyEntry> SyncQueued { get; set; } = [];
    public List<CollectionTemplateBundleFeaturedEntry> Featured { get; set; } = [];
    public List<CollectionTemplateBundleFeaturedEntry> FeaturedFailed { get; set; } = [];
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
    public string? PosterUrl { get; set; }
    public DisplayQueryDefinition? DisplayQueryDefinition { get; set; }
    public Dictionary<string, object>? SortConfig { get; set; }
}

public sealed class ImportUserMDBListCollectionRequest : ImportUserCollectionRequest
{
    public string Url { get; set; } = "";
}

public sealed class ImportUserTMDBListCollectionRequest : ImportUserCollectionRequest { public string Url { get; set; } = ""; }

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
