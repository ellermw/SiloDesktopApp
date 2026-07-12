using System.Text.Json.Serialization;

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
    // System.Text.Json's snake-case policy does not place an underscore before
    // a numeric suffix (24h). The API does, so these require explicit names.
    [JsonPropertyName("sync_runs_24h")]
    public long SyncRuns24h { get; set; }
    [JsonPropertyName("sync_errors_24h")]
    public long SyncErrors24h { get; set; }
    [JsonPropertyName("imported_watched_24h")]
    public long ImportedWatched24h { get; set; }
    [JsonPropertyName("imported_progress_24h")]
    public long ImportedProgress24h { get; set; }
    [JsonPropertyName("exported_watched_24h")]
    public long ExportedWatched24h { get; set; }
    public long PendingExports { get; set; }
    public long FailedExports { get; set; }
    public long OpenScrobbles { get; set; }
    [JsonPropertyName("scrobbles_24h")]
    public long Scrobbles24h { get; set; }
}
