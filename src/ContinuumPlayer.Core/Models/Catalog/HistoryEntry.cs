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

public class HistoryResponse
{
    public List<HistoryEntry> Items { get; set; } = [];
    public int Total { get; set; }
    public bool HasMore { get; set; }
}
