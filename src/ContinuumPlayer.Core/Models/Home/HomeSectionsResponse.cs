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
    public List<MediaItem> Items { get; set; } = [];
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
    public double? PositionSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public string? ProgressUpdatedAt { get; set; }
    public string? ItemSource { get; set; }
}
public class OverlaySummary
{
    public string Resolution { get; set; } = "";
    public string Audio { get; set; } = "";
    public string ReleaseType { get; set; } = "";
}
public class UserState
{
    public bool Played { get; set; }
    public bool IsFavorite { get; set; }
    public bool InWatchlist { get; set; }
}
