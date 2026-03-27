namespace ContinuumPlayer.Core.Models.Catalog;

public class ProgressResponse
{
    public List<ProgressItem> Progress { get; set; } = [];
}

public class ProgressItem
{
    public string MediaItemId { get; set; } = "";
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public bool Completed { get; set; }
    public string UpdatedAt { get; set; } = "";
}
