namespace ContinuumPlayer.Core.Models.Admin;

public class AdminPlaybackHistoryItem
{
    public string SessionId { get; set; } = "";
    public int UserId { get; set; }
    public string Username { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string MediaItemId { get; set; } = "";
    public int MediaFileId { get; set; }
    public string MediaTitle { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string PlayMethod { get; set; } = "";
    public string StartedAt { get; set; } = "";
    public string EndedAt { get; set; } = "";
    public double WatchedSeconds { get; set; }
    public double? DurationSeconds { get; set; }
    public bool Completed { get; set; }
}
