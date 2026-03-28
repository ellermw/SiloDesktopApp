namespace ContinuumPlayer.Core.Models.Admin;

public class AdminStats
{
    public int TotalItems { get; set; }
    public int TotalFiles { get; set; }
    public int TotalUsers { get; set; }
    public int TotalMovies { get; set; }
    public int TotalShows { get; set; }
    public int ActiveStreams { get; set; }
    public long TotalStorageBytes { get; set; }
}
