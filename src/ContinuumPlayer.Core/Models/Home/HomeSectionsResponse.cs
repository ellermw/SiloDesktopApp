using System.Collections.ObjectModel;

namespace ContinuumPlayer.Core.Models.Home;
public class HomeSectionsResponse
{
    public List<HomeSectionWithItems> Sections { get; set; } = [];
}
public class HomeSectionWithItems
{
    public string Id { get; set; } = "";
    public string SectionType { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Featured { get; set; }
    public int ItemLimit { get; set; }
    public int TotalCount { get; set; }
    public bool IsCustom { get; set; }
    public bool Customized { get; set; }
    // ObservableCollection so in-place mutations (e.g. removing an item from
    // Continue Watching when it's marked watched) propagate to the UI without
    // requiring a section swap + full row rebuild.
    public ObservableCollection<MediaItem> Items { get; set; } = [];
}
public class MediaItem
{
    public string ContentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public List<string> Genres { get; set; } = [];
    public string Status { get; set; } = "";
    public string Overview { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public string? BackdropUrl { get; set; }
    public string? BackdropThumbhash { get; set; }
    public string? LogoUrl { get; set; }
    public OverlaySummary? OverlaySummary { get; set; }
    public UserState? UserState { get; set; }
    public string? SeriesId { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public double? RatingImdb { get; set; }
    /// <summary>TMDB rating on a 0-10 scale. Used by the card overlay system.</summary>
    public double? RatingTmdb { get; set; }
    /// <summary>Rotten Tomatoes critic score on a 0-100 scale.</summary>
    public int? RatingRtCritic { get; set; }
    /// <summary>Rotten Tomatoes audience score on a 0-100 scale.</summary>
    public int? RatingRtAudience { get; set; }
    /// <summary>ISO 639-1 original language code (e.g. "en", "fr"). Rendered
    /// uppercase as a card overlay badge when enabled in user prefs.</summary>
    public string? OriginalLanguage { get; set; }
    public double? PositionSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public string? ProgressUpdatedAt { get; set; }
    public string? ItemSource { get; set; }
    /// <summary>Server-attached badges (e.g. "season_premiere") shown as pills on
    /// section item cards. Source: HomeSectionItem.badges[] on the server.</summary>
    public List<string>? Badges { get; set; }
}
public class OverlaySummary
{
    public string Resolution { get; set; } = "";
    /// <summary>HDR / Dolby Vision format string (e.g. "HDR10", "DV HDR10").</summary>
    public string Hdr { get; set; } = "";
    public string Audio { get; set; } = "";
    public string ReleaseType { get; set; } = "";
}
public class UserState
{
    public bool Played { get; set; }
    public bool IsFavorite { get; set; }
    public bool InWatchlist { get; set; }
}
