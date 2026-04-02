using System.Text.Json.Serialization;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Models.Playback;

namespace ContinuumPlayer.Core.Models.Catalog;

public class MediaItemDetail
{
    public string ContentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SortTitle { get; set; }
    public string? OriginalTitle { get; set; }
    public int Year { get; set; }
    public string Overview { get; set; } = "";
    public string? Tagline { get; set; }
    public int Runtime { get; set; }
    public string? ContentRating { get; set; }
    public List<string> Genres { get; set; } = [];
    public double? RatingTmdb { get; set; }
    public double? RatingImdb { get; set; }
    public int? RatingRtCritic { get; set; }
    public int? RatingRtAudience { get; set; }
    public string? ImdbId { get; set; }
    public string? TmdbId { get; set; }
    public string? TvdbId { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropThumbhash { get; set; }
    public string? LogoUrl { get; set; }
    public List<CastMember> Cast { get; set; } = [];
    public List<CrewMember> Crew { get; set; } = [];
    public List<string> Studios { get; set; } = [];
    public List<string> Networks { get; set; } = [];
    public List<string> Countries { get; set; } = [];
    public string? ReleaseDate { get; set; }
    public string? FirstAirDate { get; set; }
    public string? LastAirDate { get; set; }
    public int? SeasonCount { get; set; }
    public int? EpisodeCount { get; set; }
    public string? SeriesId { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public bool? IsSpecials { get; set; }
    public OverlaySummary? OverlaySummary { get; set; }
    public List<FileVersion> Versions { get; set; } = [];
    [JsonPropertyName("user_data")]
    public ItemDetailUserData? UserData { get; set; }
}

public class ItemDetailUserData
{
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public bool IsInProgress { get; set; }
    public int WatchedCount { get; set; }
    public int UnplayedCount { get; set; }
    public int InProgressCount { get; set; }
    public bool Played { get; set; }
    public int? LastFileId { get; set; }
    public string? LastResolution { get; set; }
    public bool? LastHdr { get; set; }
    public string? LastCodecVideo { get; set; }
}

public class CastMember
{
    public string? PersonId { get; set; }
    public string Name { get; set; } = "";
    public string? Character { get; set; }
    public int Order { get; set; }
    public string? PhotoUrl { get; set; }
    public string? PhotoThumbhash { get; set; }
    public string? TmdbId { get; set; }
    public string? TvdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? PlexGuid { get; set; }
}

public class CrewMember
{
    public string Name { get; set; } = "";
    public string Job { get; set; } = "";
    public string? PersonId { get; set; }
    public string? PhotoUrl { get; set; }
    public string? PhotoThumbhash { get; set; }
    public string? TmdbId { get; set; }
}
