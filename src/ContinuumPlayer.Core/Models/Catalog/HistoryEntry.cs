namespace ContinuumPlayer.Core.Models.Catalog;

public class HistoryEntry
{
    public string ContentId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public int Year { get; set; }
    public string WatchedAt { get; set; } = "";
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public bool Completed { get; set; }
    public string? PosterUrl { get; set; }
    public string? PosterThumbhash { get; set; }
}

// NOTE: Server GET /history returns {"items": [...]}, same shape as itemsListResponse.
// The server does NOT include "total" or "has_more" fields — they default to 0/false.
// The items are MediaItem-shaped objects (resolved from history entries).
public class HistoryResponse
{
    public List<HistoryEntry> Items { get; set; } = [];
    public int Total { get; set; }
    public bool HasMore { get; set; }
}
