namespace ContinuumPlayer.Core.Models.Requests;

public class RequestFeatureStatus
{
    public bool RequestsEnabled { get; set; }
}

public class RequestState
{
    public string Status { get; set; } = "";
    public bool Requestable { get; set; }
    public string Reason { get; set; } = "";
    public string RequestId { get; set; } = "";
}

public class RequestMediaResult
{
    public string MediaType { get; set; } = "";
    public int TmdbId { get; set; }
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public string Overview { get; set; } = "";
    public string PosterPath { get; set; } = "";
    public string BackdropPath { get; set; } = "";
    public string ReleaseDate { get; set; } = "";
    public double? Popularity { get; set; }
    public double? VoteAverage { get; set; }
    public string Availability { get; set; } = "";
    public string? LibraryContentId { get; set; }
    public RequestState Request { get; set; } = new();
}

public class RequestMediaPage
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalResults { get; set; }
    public List<RequestMediaResult> Results { get; set; } = [];
}

public class RequestDiscoverySection : RequestMediaPage
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
}

public class RequestDiscoveryResponse
{
    public List<RequestDiscoverySection> Sections { get; set; } = [];
}

public class DiscoverBrandCard
{
    public int? TmdbId { get; set; }
    public string Slug { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string? GradientFrom { get; set; }
    public string? GradientTo { get; set; }
    public bool? SeriesSupported { get; set; }
}

public class DiscoverStudiosResponse
{
    public List<DiscoverBrandCard> Studios { get; set; } = [];
}

public class DiscoverNetworksResponse
{
    public List<DiscoverBrandCard> Networks { get; set; } = [];
}

public class DiscoverGenresResponse
{
    public List<DiscoverBrandCard> Genres { get; set; } = [];
}

public class DiscoverBrowseResponse
{
    public string Kind { get; set; } = "";
    public string Slug { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string MediaType { get; set; } = "";
    public string Sort { get; set; } = "";
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public List<RequestMediaResult> Results { get; set; } = [];
}

public class RequestMediaCastMember
{
    public string Name { get; set; } = "";
    public string? Character { get; set; }
    public string? ProfilePath { get; set; }
    public int Order { get; set; }
}

public class RequestMediaDetail
{
    public string MediaType { get; set; } = "";
    public int TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public int? TvdbId { get; set; }
    public string Title { get; set; } = "";
    public string? OriginalTitle { get; set; }
    public string? Tagline { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? ReleaseDate { get; set; }
    public int? Year { get; set; }
    public int? Runtime { get; set; }
    public List<string>? Genres { get; set; }
    public double? VoteAverage { get; set; }
    public int? VoteCount { get; set; }
    public string? Status { get; set; }
    public string? Homepage { get; set; }
    public string? ContentRating { get; set; }
    public List<string>? ProductionCompanies { get; set; }
    public int? NumberOfSeasons { get; set; }
    public int? NumberOfEpisodes { get; set; }
    public string? FirstAirDate { get; set; }
    public string? LastAirDate { get; set; }
    public List<string>? Networks { get; set; }
    public List<RequestMediaCastMember>? Cast { get; set; }
    public string? Director { get; set; }
    public List<string>? Creators { get; set; }
    public List<RequestMediaResult>? Recommendations { get; set; }
    public string Availability { get; set; } = "";
    public string? LibraryContentId { get; set; }
    public RequestState Request { get; set; } = new();
}

public class CreateMediaRequestInput
{
    public string MediaType { get; set; } = "";
    public int TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public string? ImdbId { get; set; }
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
}

public class MediaRequest
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string MediaType { get; set; } = "";
    public int TmdbId { get; set; }
    public int? TvdbId { get; set; }
    public string ImdbId { get; set; } = "";
    public string Title { get; set; } = "";
    public int? Year { get; set; }
    public string Overview { get; set; } = "";
    public string PosterPath { get; set; } = "";
    public string BackdropPath { get; set; } = "";
    public string Status { get; set; } = "";
    public string Outcome { get; set; } = "";
    public int? RequestedByUserId { get; set; }
    public string RequestedByProfileId { get; set; } = "";
    public bool? IsAnime { get; set; }
    public List<RequestTarget>? Targets { get; set; }
    public string IntegrationKind { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string ExternalStatus { get; set; } = "";
    public string? LibraryContentId { get; set; }
    public string LastError { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string ApprovedAt { get; set; } = "";
    public string CompletedAt { get; set; } = "";
}

public class MediaRequestsListResponse
{
    public List<MediaRequest> Requests { get; set; } = [];
}

public class RequestTarget
{
    public int Id { get; set; }
    public string RequestId { get; set; } = "";
    public string? IntegrationId { get; set; }
    public string? IntegrationKind { get; set; }
    public string? InstanceName { get; set; }
    public string Quality { get; set; } = "";
    public bool IsAnime { get; set; }
    public string? ExternalId { get; set; }
    public string? ExternalStatus { get; set; }
    public string Status { get; set; } = "";
    public string? LastError { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public class RequestSettings
{
    public bool RequestsEnabled { get; set; }
    public int GlobalMaxRequests { get; set; }
    public int GlobalWindowDays { get; set; }
    public bool GlobalAutoApprovalEnabled { get; set; }
    public bool ForceDualQuality { get; set; }
    public string UpdatedAt { get; set; } = "";
}

public class RequestUserLimit
{
    public int UserId { get; set; }
    public string LimitMode { get; set; } = "";
    public int? MaxRequests { get; set; }
    public int? WindowDays { get; set; }
    public string ApprovalMode { get; set; } = "";
    public string? UpdatedAt { get; set; }
}

public class RequestIntegration
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "";
    public string? ApiKeyRef { get; set; }
    public bool? HasApiKey { get; set; }
    public string? CapabilityId { get; set; }
    public int? InstallationId { get; set; }
    public List<string>? SupportedMediaTypes { get; set; }
    public Dictionary<string, object?>? PluginConfig { get; set; }
    public string? LastCheckAt { get; set; }
    public string? LastCheckStatus { get; set; }
    public string? LastCheckError { get; set; }
    public string? UpdatedAt { get; set; }
}

public class RequestSchemaOption
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

public class RequestIntegrationsResponse
{
    public List<RequestIntegration> Integrations { get; set; } = [];
}

public class LoadRequestIntegrationOptionsRequest
{
    public string? Kind { get; set; }
    public string BaseUrl { get; set; } = "";
    public string? ApiKeyRef { get; set; }
    public string? CapabilityId { get; set; }
    public int? InstallationId { get; set; }
    public Dictionary<string, object?>? PluginConfig { get; set; }
}
