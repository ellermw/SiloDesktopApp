namespace SiloPlayer.Core.Models.Admin;

public class AdminStats
{
    public int TotalItems { get; set; }
    public int TotalFiles { get; set; }
    public int TotalUsers { get; set; }
    public int TotalMovies { get; set; }
    public int TotalMovieFiles { get; set; }
    public int TotalShows { get; set; }
    public int TotalShowFiles { get; set; }
    public int ActiveStreams { get; set; }
    public long TotalStorageBytes { get; set; }
    public WatchProviderActivity WatchProviderActivity { get; set; } = new();
}

public class WatchProviderActivity
{
    public long TraktConnectedProfiles { get; set; }
    public long TraktEnabledProfiles { get; set; }
    public long TraktExportEnabled { get; set; }
    public long TraktScrobbleEnabled { get; set; }
    public string? LastSyncCompletedAt { get; set; }
    public long SyncRuns24h { get; set; }
    public long SyncErrors24h { get; set; }
    public long ImportedWatched24h { get; set; }
    public long ImportedProgress24h { get; set; }
    public long ExportedWatched24h { get; set; }
    public long PendingExports { get; set; }
    public long FailedExports { get; set; }
    public long OpenScrobbles { get; set; }
    public long Scrobbles24h { get; set; }
}
