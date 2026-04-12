namespace ContinuumPlayer.Core.Models.Catalog;

/// <summary>
/// Wire model for <c>GET /api/v1/calendar?start=&amp;end=&amp;filter=&amp;library_id=</c>.
/// Mirrors the web client's <c>CalendarResponse</c> shape:
/// <c>{ "events": [{ "date": "YYYY-MM-DD", "items": [...] }] }</c>.
/// </summary>
public class CalendarResponse
{
    public List<CalendarDay> Events { get; set; } = [];
}

public class CalendarDay
{
    public string Date { get; set; } = "";
    public List<CalendarEvent> Items { get; set; } = [];
}

public class CalendarEvent
{
    public string ContentId { get; set; } = "";
    /// <summary>"movie", "episode", or "season_premiere".</summary>
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? EpisodeTitle { get; set; }
    public string? SeriesId { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string AirDate { get; set; } = "";
    public string? AirTime { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
    public List<string> Badges { get; set; } = [];
}
