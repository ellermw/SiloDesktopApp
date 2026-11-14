using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

// Wire types for the Admin Monitor Dashboard, mirroring the named schemas in
// the pinned contracts/api/v2/openapi.json. REST identifiers stay strings,
// counters and bytes stay 64-bit, and optional readings stay nullable so an
// absent value is never shown as a measured zero.

public sealed class DashboardPageInfo
{
    [JsonPropertyName("has_more")] public bool HasMore { get; set; }
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; set; }
}

public sealed class DashboardCollection<T>
{
    [JsonPropertyName("items")] public List<T>? Items { get; set; }
    [JsonPropertyName("page")] public DashboardPageInfo? Page { get; set; }
}

public sealed class DashboardCapabilities
{
    [JsonPropertyName("revision")] public string? Revision { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("allowed")] public bool Allowed { get; set; }
    [JsonPropertyName("server_layouts")] public bool ServerLayouts { get; set; }
    [JsonPropertyName("timeseries")] public bool Timeseries { get; set; }
    [JsonPropertyName("playback_activity")] public bool PlaybackActivity { get; set; }
    [JsonPropertyName("top_activity")] public bool TopActivity { get; set; }
    [JsonPropertyName("health")] public bool Health { get; set; }
    [JsonPropertyName("log_level_list")] public bool LogLevelList { get; set; }
    [JsonPropertyName("watch_providers")] public bool WatchProviders { get; set; }
    [JsonPropertyName("downloads_stats")] public bool DownloadsStats { get; set; }
    [JsonPropertyName("egress_totals")] public bool EgressTotals { get; set; }
    [JsonPropertyName("egress_by_source")] public bool EgressBySource { get; set; }
    [JsonPropertyName("api_clients")] public bool ApiClients { get; set; }
}

public sealed class DashboardStats
{
    [JsonPropertyName("total_items")] public long TotalItems { get; set; }
    [JsonPropertyName("total_files")] public long TotalFiles { get; set; }
    [JsonPropertyName("total_users")] public long TotalUsers { get; set; }
    [JsonPropertyName("total_movies")] public long TotalMovies { get; set; }
    [JsonPropertyName("total_movie_files")] public long TotalMovieFiles { get; set; }
    [JsonPropertyName("total_shows")] public long TotalShows { get; set; }
    [JsonPropertyName("total_show_files")] public long TotalShowFiles { get; set; }
    [JsonPropertyName("advisory_titles")] public long AdvisoryTitles { get; set; }
    [JsonPropertyName("active_streams")] public long ActiveStreams { get; set; }
    [JsonPropertyName("total_storage_bytes")] public long TotalStorageBytes { get; set; }
    [JsonPropertyName("watch_providers")] public List<DashboardWatchProvider>? WatchProviders { get; set; }
}

public sealed class DashboardWatchProvider
{
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("registered")] public bool Registered { get; set; }
    [JsonPropertyName("connected_profiles")] public long ConnectedProfiles { get; set; }
    [JsonPropertyName("enabled_profiles")] public long EnabledProfiles { get; set; }
    [JsonPropertyName("export_enabled_profiles")] public long ExportEnabledProfiles { get; set; }
    [JsonPropertyName("scrobble_enabled_profiles")] public long ScrobbleEnabledProfiles { get; set; }
    [JsonPropertyName("exporting")] public bool Exporting { get; set; }
    [JsonPropertyName("scrobbling")] public bool Scrobbling { get; set; }
    [JsonPropertyName("imported_watched_24h")] public long ImportedWatched24h { get; set; }
    [JsonPropertyName("imported_progress_24h")] public long ImportedProgress24h { get; set; }
    [JsonPropertyName("exported_watched_24h")] public long ExportedWatched24h { get; set; }
    [JsonPropertyName("failed_exports")] public long FailedExports { get; set; }
    [JsonPropertyName("pending_exports")] public long PendingExports { get; set; }
    [JsonPropertyName("open_scrobbles")] public long OpenScrobbles { get; set; }
    [JsonPropertyName("scrobbles_24h")] public long Scrobbles24h { get; set; }
    [JsonPropertyName("sync_errors_24h")] public long SyncErrors24h { get; set; }
    [JsonPropertyName("sync_runs_24h")] public long SyncRuns24h { get; set; }
    [JsonPropertyName("last_sync_completed_at")] public DateTimeOffset? LastSyncCompletedAt { get; set; }
}

public sealed class DashboardTimeseries
{
    [JsonPropertyName("resolution_seconds")] public long ResolutionSeconds { get; set; }
    [JsonPropertyName("from")] public DateTimeOffset? From { get; set; }
    [JsonPropertyName("to")] public DateTimeOffset? To { get; set; }
    [JsonPropertyName("oldest_sample_at")] public DateTimeOffset? OldestSampleAt { get; set; }
    /// <summary>Null when the server predates window totals (capability egress_totals).</summary>
    [JsonPropertyName("egress_bytes")] public long? EgressBytes { get; set; }
    [JsonPropertyName("download_egress_bytes")] public long? DownloadEgressBytes { get; set; }
    [JsonPropertyName("egress_sources")] public List<DashboardEgressSource>? EgressSources { get; set; }
    [JsonPropertyName("points")] public List<DashboardTimeseriesPoint>? Points { get; set; }
}

public sealed class DashboardEgressSource
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("node_id")] public string? NodeId { get; set; }
    [JsonPropertyName("egress_bytes")] public long? EgressBytes { get; set; }
}

public sealed class DashboardTimeseriesPoint
{
    [JsonPropertyName("t")] public DateTimeOffset T { get; set; }
    [JsonPropertyName("streams")] public long Streams { get; set; }
    [JsonPropertyName("direct")] public long Direct { get; set; }
    [JsonPropertyName("remux")] public long Remux { get; set; }
    [JsonPropertyName("transcode")] public long Transcode { get; set; }
    [JsonPropertyName("egress_kbps")] public long EgressKbps { get; set; }
    [JsonPropertyName("download_egress_kbps")] public long? DownloadEgressKbps { get; set; }
    [JsonPropertyName("egress_by_source")] public Dictionary<string, long>? EgressBySource { get; set; }
}

public sealed class DashboardPlaybackActivity
{
    [JsonPropertyName("hours")] public long Hours { get; set; }
    [JsonPropertyName("bucket_seconds")] public long BucketSeconds { get; set; }
    [JsonPropertyName("from")] public DateTimeOffset? From { get; set; }
    [JsonPropertyName("to")] public DateTimeOffset? To { get; set; }
    [JsonPropertyName("buckets")] public List<DashboardPlaybackBucket>? Buckets { get; set; }
    [JsonPropertyName("profiles_active_24h")] public long? ProfilesActive24h { get; set; }
    [JsonPropertyName("reliability")] public DashboardPlaybackReliability? Reliability { get; set; }
}

public sealed class DashboardPlaybackBucket
{
    [JsonPropertyName("hour")] public DateTimeOffset Hour { get; set; }
    [JsonPropertyName("direct")] public long Direct { get; set; }
    [JsonPropertyName("remux")] public long Remux { get; set; }
    [JsonPropertyName("transcode")] public long Transcode { get; set; }
}

public sealed class DashboardPlaybackReliability
{
    [JsonPropertyName("sessions_started")] public long SessionsStarted { get; set; }
    [JsonPropertyName("transcode_starts")] public long TranscodeStarts { get; set; }
    [JsonPropertyName("finalized_sessions")] public long FinalizedSessions { get; set; }
    [JsonPropertyName("completed_sessions")] public long CompletedSessions { get; set; }
    [JsonPropertyName("completion_rate")] public double CompletionRate { get; set; }
    [JsonPropertyName("unique_profiles")] public long UniqueProfiles { get; set; }
}

public sealed class DashboardTopActivity
{
    [JsonPropertyName("days")] public long Days { get; set; }
    [JsonPropertyName("limit")] public long Limit { get; set; }
    [JsonPropertyName("titles")] public List<DashboardTopTitle>? Titles { get; set; }
    [JsonPropertyName("profiles")] public List<DashboardTopProfile>? Profiles { get; set; }
}

public sealed class DashboardTopTitle
{
    [JsonPropertyName("media_item_id")] public string MediaItemId { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("media_type")] public string MediaType { get; set; } = "";
    [JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonPropertyName("total_seconds")] public long TotalSeconds { get; set; }
}

public sealed class DashboardTopProfile
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("profile_id")] public string ProfileId { get; set; } = "";
    [JsonPropertyName("profile_name")] public string ProfileName { get; set; } = "";
    [JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonPropertyName("total_seconds")] public long TotalSeconds { get; set; }
}

public sealed class DashboardDownloadsStats
{
    [JsonPropertyName("users_with_downloads")] public long UsersWithDownloads { get; set; }
    [JsonPropertyName("active_downloads")] public long ActiveDownloads { get; set; }
    [JsonPropertyName("total_bytes")] public long TotalBytes { get; set; }
    [JsonPropertyName("downloads_started_24h")] public long DownloadsStarted24h { get; set; }
    [JsonPropertyName("downloads_completed_24h")] public long DownloadsCompleted24h { get; set; }
    [JsonPropertyName("limit")] public long Limit { get; set; }
    [JsonPropertyName("top_users")] public List<DashboardDownloadsUser>? TopUsers { get; set; }
}

public sealed class DashboardDownloadsUser
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("downloads")] public long Downloads { get; set; }
    [JsonPropertyName("total_bytes")] public long TotalBytes { get; set; }
}

public sealed class DashboardApiClients
{
    [JsonPropertyName("hours")] public long Hours { get; set; }
    [JsonPropertyName("from")] public DateTimeOffset? From { get; set; }
    [JsonPropertyName("total_requests")] public long TotalRequests { get; set; }
    [JsonPropertyName("clients")] public List<DashboardApiClient>? Clients { get; set; }
}

public sealed class DashboardApiClient
{
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("device_name")] public string DeviceName { get; set; } = "";
    [JsonPropertyName("device_platform")] public string DevicePlatform { get; set; } = "";
    [JsonPropertyName("user_agent")] public string UserAgent { get; set; } = "";
    [JsonPropertyName("requests")] public long Requests { get; set; }
    [JsonPropertyName("refused")] public long Refused { get; set; }
    [JsonPropertyName("peak_hour_requests")] public long PeakHourRequests { get; set; }
    [JsonPropertyName("first_hour")] public DateTimeOffset? FirstHour { get; set; }
    [JsonPropertyName("last_hour")] public DateTimeOffset? LastHour { get; set; }
    [JsonPropertyName("session_active")] public bool SessionActive { get; set; }
}

public sealed class DashboardApiClientEndpoints
{
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
    [JsonPropertyName("hours")] public long Hours { get; set; }
    [JsonPropertyName("endpoints")] public List<DashboardApiClientEndpoint>? Endpoints { get; set; }
}

public sealed class DashboardApiClientEndpoint
{
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("route")] public string Route { get; set; } = "";
    [JsonPropertyName("requests")] public long Requests { get; set; }
    [JsonPropertyName("refused")] public long Refused { get; set; }
}

public sealed class DashboardServerStatus
{
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("restart_required")] public bool RestartRequired { get; set; }
    [JsonPropertyName("restart_requested")] public bool RestartRequested { get; set; }
    [JsonPropertyName("restart_mark_count")] public long RestartMarkCount { get; set; }
    [JsonPropertyName("restart_required_reason")] public string? RestartRequiredReason { get; set; }
    [JsonPropertyName("restart_required_reasons")] public List<string>? RestartRequiredReasons { get; set; }
    [JsonPropertyName("health")] public DashboardServerHealth? Health { get; set; }
}

public sealed class DashboardServerHealth
{
    [JsonPropertyName("postgres")] public DashboardHealthComponent? Postgres { get; set; }
    [JsonPropertyName("redis")] public DashboardHealthComponent? Redis { get; set; }
    [JsonPropertyName("errors_24h")] public long Errors24h { get; set; }
    [JsonPropertyName("warnings_24h")] public long Warnings24h { get; set; }
}

public sealed class DashboardHealthComponent
{
    [JsonPropertyName("configured")] public bool Configured { get; set; }
    [JsonPropertyName("ok")] public bool? Ok { get; set; }
    [JsonPropertyName("latency_ms")] public double? LatencyMs { get; set; }
}

public sealed class DashboardBuildInfo
{
    [JsonPropertyName("display")] public string Display { get; set; } = "";
    [JsonPropertyName("revision")] public string Revision { get; set; } = "";
    [JsonPropertyName("dirty")] public bool Dirty { get; set; }
    [JsonPropertyName("build_number")] public long BuildNumber { get; set; }
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("built_at")] public DateTimeOffset? BuiltAt { get; set; }
    [JsonPropertyName("vcs_time")] public DateTimeOffset? VcsTime { get; set; }
}

public sealed class DashboardResourceCapabilities
{
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("allowed")] public bool Allowed { get; set; }
    [JsonPropertyName("instance_attribution")] public bool InstanceAttribution { get; set; }
    [JsonPropertyName("process_resources")] public bool ProcessResources { get; set; }
    [JsonPropertyName("cgroup_resources")] public bool CgroupResources { get; set; }
    [JsonPropertyName("sample_freshness")] public bool SampleFreshness { get; set; }
}

public sealed class DashboardSystemResources
{
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("stale")] public bool Stale { get; set; }
    [JsonPropertyName("sampled_at")] public DateTimeOffset? SampledAt { get; set; }
    [JsonPropertyName("system")] public DashboardSystemStats? System { get; set; }
    [JsonPropertyName("gpu")] public List<DashboardGpuStats>? Gpu { get; set; }
    [JsonPropertyName("attribution")] public DashboardResourceAttribution? Attribution { get; set; }
}

public sealed class DashboardSystemStats
{
    [JsonPropertyName("cpu_pct")] public double? CpuPct { get; set; }
    [JsonPropertyName("cores")] public double? Cores { get; set; }
    [JsonPropertyName("load1")] public double? Load1 { get; set; }
    [JsonPropertyName("mem_used_mb")] public double? MemUsedMb { get; set; }
    [JsonPropertyName("mem_total_mb")] public double? MemTotalMb { get; set; }
    [JsonPropertyName("net_rx_bps")] public double? NetRxBps { get; set; }
    [JsonPropertyName("net_tx_bps")] public double? NetTxBps { get; set; }
    [JsonPropertyName("disks")] public List<DashboardDiskStats>? Disks { get; set; }
}

public sealed class DashboardDiskStats
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("used_gb")] public double? UsedGb { get; set; }
    [JsonPropertyName("total_gb")] public double? TotalGb { get; set; }
    [JsonPropertyName("unavailable")] public bool? Unavailable { get; set; }
    [JsonPropertyName("stale")] public bool? Stale { get; set; }
    [JsonPropertyName("scratch")] public bool? Scratch { get; set; }
}

public sealed class DashboardGpuStats
{
    [JsonPropertyName("device")] public string? Device { get; set; }
    [JsonPropertyName("vendor")] public string? Vendor { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("sessions")] public double? Sessions { get; set; }
    [JsonPropertyName("video_busy_pct")] public double? VideoBusyPct { get; set; }
    [JsonPropertyName("render_busy_pct")] public double? RenderBusyPct { get; set; }
    [JsonPropertyName("total_busy_pct")] public double? TotalBusyPct { get; set; }
    [JsonPropertyName("vram_used_mb")] public double? VramUsedMb { get; set; }
    [JsonPropertyName("vram_total_mb")] public double? VramTotalMb { get; set; }
}

public sealed class DashboardResourceAttribution
{
    [JsonPropertyName("instance_id")] public string? InstanceId { get; set; }
    [JsonPropertyName("sample_interval_seconds")] public double? SampleIntervalSeconds { get; set; }
    [JsonPropertyName("sample_duration_seconds")] public double? SampleDurationSeconds { get; set; }
    [JsonPropertyName("cpu")] public DashboardResourceSource? Cpu { get; set; }
    [JsonPropertyName("memory")] public DashboardResourceSource? Memory { get; set; }
    [JsonPropertyName("network")] public DashboardResourceSource? Network { get; set; }
    [JsonPropertyName("load")] public DashboardResourceSource? Load { get; set; }
    [JsonPropertyName("process")] public DashboardProcessStats? Process { get; set; }
}

public sealed class DashboardResourceSource
{
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
}

public sealed class DashboardProcessStats
{
    [JsonPropertyName("resident_bytes")] public long? ResidentBytes { get; set; }
    [JsonPropertyName("heap_live_bytes")] public long? HeapLiveBytes { get; set; }
}

public sealed class DashboardNode
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("group")] public string? Group { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("healthy")] public bool Healthy { get; set; }
    [JsonPropertyName("active_jobs")] public long ActiveJobs { get; set; }
    [JsonPropertyName("max_jobs")] public long? MaxJobs { get; set; }
    [JsonPropertyName("egress_kbps")] public long EgressKbps { get; set; }
    [JsonPropertyName("max_bandwidth_kbps")] public long? MaxBandwidthKbps { get; set; }
    [JsonPropertyName("drain_requested")] public bool DrainRequested { get; set; }
    [JsonPropertyName("drain_state")] public string? DrainState { get; set; }
    [JsonPropertyName("last_health_check")] public DateTimeOffset? LastHealthCheck { get; set; }
    [JsonPropertyName("last_stats")] public DashboardNodeStats? LastStats { get; set; }
}

public sealed class DashboardNodeStats
{
    [JsonPropertyName("sampled_at")] public DateTimeOffset? SampledAt { get; set; }
    [JsonPropertyName("system")] public DashboardSystemStats? System { get; set; }
    [JsonPropertyName("gpu")] public List<DashboardGpuStats>? Gpu { get; set; }
    [JsonPropertyName("attribution")] public DashboardResourceAttribution? Attribution { get; set; }
    [JsonPropertyName("build")] public DashboardNodeBuild? Build { get; set; }
}

public sealed class DashboardNodeBuild
{
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("display")] public string? Display { get; set; }
    [JsonPropertyName("revision")] public string? Revision { get; set; }
    [JsonPropertyName("build_number")] public long? BuildNumber { get; set; }
    [JsonPropertyName("built_at")] public string? BuiltAt { get; set; }
}

public sealed class DashboardLibrary
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("paths")] public List<string>? Paths { get; set; }
    [JsonPropertyName("poster_url")] public string? PosterUrl { get; set; }
    [JsonPropertyName("sort_order")] public long SortOrder { get; set; }
}

public sealed class DashboardUser
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("is_owner")] public bool IsOwner { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("last_active_at")] public DateTimeOffset? LastActiveAt { get; set; }
}

public sealed class DashboardAutoscanStatus
{
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
    [JsonPropertyName("running_scans")] public long? RunningScans { get; set; }
    [JsonPropertyName("accepted_scans")] public long? AcceptedScans { get; set; }
    [JsonPropertyName("active_scans")] public long? ActiveScans { get; set; }
    [JsonPropertyName("latest_event_at")] public DateTimeOffset? LatestEventAt { get; set; }
    [JsonPropertyName("running_polls")] public List<JsonElement>? RunningPolls { get; set; }
}

public sealed class DashboardScanHistoryPage
{
    [JsonPropertyName("items")] public List<DashboardScanRecord>? Items { get; set; }
    [JsonPropertyName("page")] public DashboardPageInfo? Page { get; set; }
    [JsonPropertyName("total")] public long Total { get; set; }
}

public sealed class DashboardScanRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("library_id")] public string LibraryId { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("trigger")] public string Trigger { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("completed_at")] public DateTimeOffset? CompletedAt { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
}

/// <summary>
/// An active scan from the <c>scans</c> event channel. The bridge sends a
/// numeric library id while REST uses strings, so the adapter normalizes it
/// losslessly to the REST string identity.
/// </summary>
public sealed record DashboardActiveScan(
    string Id,
    string LibraryId,
    string Mode,
    string? Path,
    string Trigger,
    string Status,
    DateTimeOffset? StartedAt,
    long? TotalFiles,
    long? FilesProcessed,
    string? Message)
{
    public bool IsActive => Status is "accepted" or "running";
}

public sealed class DashboardLogEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("timestamp")] public DateTimeOffset? Timestamp { get; set; }
    [JsonPropertyName("level")] public string Level { get; set; } = "";
    [JsonPropertyName("component")] public string Component { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

/// <summary>The fields of AdminPlaybackSession that the dashboard renders.</summary>
public sealed class DashboardSession
{
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = "";
    [JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("profile_id")] public string ProfileId { get; set; } = "";
    [JsonPropertyName("profile_name")] public string? ProfileName { get; set; }
    [JsonPropertyName("media_file_id")] public string MediaFileId { get; set; } = "";
    [JsonPropertyName("media_title")] public string MediaTitle { get; set; } = "";
    [JsonPropertyName("media_type")] public string MediaType { get; set; } = "";
    [JsonPropertyName("content_id")] public string? ContentId { get; set; }
    [JsonPropertyName("poster_url")] public string? PosterUrl { get; set; }
    [JsonPropertyName("series_name")] public string? SeriesName { get; set; }
    [JsonPropertyName("season_number")] public long? SeasonNumber { get; set; }
    [JsonPropertyName("episode_number")] public long? EpisodeNumber { get; set; }
    [JsonPropertyName("episode_name")] public string? EpisodeName { get; set; }
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("is_paused")] public bool IsPaused { get; set; }
    [JsonPropertyName("has_playback_control")] public bool HasPlaybackControl { get; set; }
    [JsonPropertyName("play_method")] public string? PlayMethod { get; set; }
    [JsonPropertyName("effective_play_method")] public string? EffectivePlayMethod { get; set; }
    [JsonPropertyName("video_decision")] public string? VideoDecision { get; set; }
    [JsonPropertyName("audio_decision")] public string? AudioDecision { get; set; }
    [JsonPropertyName("transcode_audio")] public bool TranscodeAudio { get; set; }
    [JsonPropertyName("is_jellyfin_client")] public bool? IsJellyfinClient { get; set; }
    [JsonPropertyName("client_label")] public string? ClientLabel { get; set; }
    [JsonPropertyName("client_name")] public string? ClientName { get; set; }
    [JsonPropertyName("client_version")] public string? ClientVersion { get; set; }
    [JsonPropertyName("client_user_agent")] public string? ClientUserAgent { get; set; }
    [JsonPropertyName("stream_location")] public string? StreamLocation { get; set; }
    [JsonPropertyName("routing_network_provider")] public string? RoutingNetworkProvider { get; set; }
    [JsonPropertyName("routing_workload")] public string? RoutingWorkload { get; set; }
    [JsonPropertyName("routing_execution")] public string? RoutingExecution { get; set; }
    [JsonPropertyName("routing_execution_node_id")] public string? RoutingExecutionNodeId { get; set; }
    [JsonPropertyName("routing_execution_node_name")] public string? RoutingExecutionNodeName { get; set; }
    [JsonPropertyName("routing_egress")] public string? RoutingEgress { get; set; }
    [JsonPropertyName("routing_egress_node_id")] public string? RoutingEgressNodeId { get; set; }
    [JsonPropertyName("routing_egress_node_name")] public string? RoutingEgressNodeName { get; set; }
    [JsonPropertyName("reporting_node")] public string? ReportingNode { get; set; }
    [JsonPropertyName("node_display_name")] public string? NodeDisplayName { get; set; }
}

/// <summary>Only the settings keys the attention notice reads; the full map is never exposed.</summary>
public sealed record DashboardAttentionSettings(IReadOnlyDictionary<string, string> Values, string ETag);

public sealed class DashboardSensitiveStatus
{
    [JsonPropertyName("configured")] public List<string>? Configured { get; set; }
}

public sealed class DashboardBranding
{
    [JsonPropertyName("storage_available")] public bool StorageAvailable { get; set; }
}

public sealed class DashboardSearchStatus
{
    [JsonPropertyName("active_provider")] public string? ActiveProvider { get; set; }
    [JsonPropertyName("degraded")] public bool Degraded { get; set; }
    [JsonPropertyName("degraded_reason")] public string? DegradedReason { get; set; }
    [JsonPropertyName("meilisearch")] public DashboardSearchMeili? Meilisearch { get; set; }
}

public sealed class DashboardSearchMeili
{
    [JsonPropertyName("configured")] public bool Configured { get; set; }
    [JsonPropertyName("healthy")] public bool Healthy { get; set; }
}

public sealed class DashboardSettingValue
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

public sealed class DashboardScanStart
{
    [JsonPropertyName("library_id")] public string LibraryId { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
}

public sealed class DashboardScanCancel
{
    [JsonPropertyName("library_id")] public string LibraryId { get; set; } = "";
    [JsonPropertyName("cancelled")] public long Cancelled { get; set; }
}

public sealed class DashboardTask
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
}

/// <summary>The server's layout read: a client-owned document (or null) and its strong validator.</summary>
public sealed record DashboardServerLayout(JsonElement? Layout, DateTimeOffset? UpdatedAt, string ETag);

/// <summary>A conditional layout read: either unchanged (304) or a new representation.</summary>
public sealed record DashboardLayoutRead(bool NotModified, DashboardServerLayout? Value);
