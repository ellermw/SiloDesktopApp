using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

// Wire types for the admin Insights reads (GET /api/v2/admin/insights/*), copied from the
// official Silo schema at c59798116 (web/src/api/v2/schema.ts, components.schemas.AdminInsights*).
//
// Conventions
//  - Every wire name is explicit. int64 is long, double is double, identifiers stay strings,
//    RFC 3339 instants stay strings (as the history rows do) so an unparsable value is shown as
//    sent; AdminInsightsRules.TryParseInstant reads one.
//  - A field the schema requires is [JsonRequired]: an absent field refuses the whole response
//    (nullable ones still accept an explicit null). A missing count is never read as zero.
//  - The only exceptions are the fields the capabilities document marks as "breakdowns"
//    (per-bucket active accounts and profiles, end_reasons, least_finished, client_versions,
//    protocol_buckets, subtitles, audio, top_profiles): a server without them still answers, so
//    they default to empty and AdminInsightsCapabilities.Breakdowns says whether to trust that.
//  - Collections default to empty lists and never hold null elements by contract.

/// <summary>GET /admin/insights/capabilities. Not called by the official pages; useful for gating.</summary>
public sealed class AdminInsightsCapabilities
{
    [JsonRequired, JsonPropertyName("allowed")] public bool Allowed { get; set; }
    [JsonRequired, JsonPropertyName("available")] public bool Available { get; set; }
    /// <summary>Activity per-bucket accounts/profiles and end_reasons; Titles least_finished; Devices client_versions, protocol_buckets, subtitles, audio; People top_profiles.</summary>
    [JsonRequired, JsonPropertyName("breakdowns")] public bool Breakdowns { get; set; }
    /// <summary>The Library read is served.</summary>
    [JsonRequired, JsonPropertyName("library")] public bool Library { get; set; }
    [JsonRequired, JsonPropertyName("node_egress_daily")] public bool NodeEgressDaily { get; set; }
    /// <summary>Activity, Devices, Titles and title detail accept profile_id together with user_id.</summary>
    [JsonRequired, JsonPropertyName("profile_filter")] public bool ProfileFilter { get; set; }
    [JsonRequired, JsonPropertyName("revision")] public string Revision { get; set; } = "";
    /// <summary><c>available</c>, <c>disabled</c>, <c>not_configured</c> or <c>unsupported</c>; unknown values are possible.</summary>
    [JsonRequired, JsonPropertyName("state")] public string State { get; set; } = "";
    /// <summary>The Titles leaderboard and title detail are served.</summary>
    [JsonRequired, JsonPropertyName("titles")] public bool Titles { get; set; }
}

/// <summary>Sessions behind an answer, shared by activity, people and devices.</summary>
public sealed class AdminInsightsCoverage
{
    [JsonRequired, JsonPropertyName("measured_sessions")] public long MeasuredSessions { get; set; }
    [JsonRequired, JsonPropertyName("oldest_history_at")] public string? OldestHistoryAt { get; set; }
    [JsonRequired, JsonPropertyName("oldest_measured_at")] public string? OldestMeasuredAt { get; set; }
    [JsonRequired, JsonPropertyName("oldest_metric_at")] public string? OldestMetricAt { get; set; }
    [JsonRequired, JsonPropertyName("oldest_route_facts_at")] public string? OldestRouteFactsAt { get; set; }
    [JsonRequired, JsonPropertyName("route_facts_sessions")] public long RouteFactsSessions { get; set; }
    [JsonRequired, JsonPropertyName("total_sessions")] public long TotalSessions { get; set; }
}

/// <summary>A <c>{key, sessions}</c> pair (end reasons, codecs, resolutions, languages and other breakdowns).</summary>
public sealed class AdminInsightsLabeledCount
{
    [JsonRequired, JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
}

/// <summary>A <c>{key, plays, titles}</c> triple (genres, content ratings, decades).</summary>
public sealed class AdminInsightsLabeledPlays
{
    [JsonRequired, JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    /// <summary>Distinct played titles behind the plays.</summary>
    [JsonRequired, JsonPropertyName("titles")] public long Titles { get; set; }
}

// ── activity ─────────────────────────────────────────────────

/// <summary>GET /admin/insights/activity.</summary>
public sealed class AdminInsightsActivity
{
    /// <summary><c>day</c>, <c>week</c> or <c>month</c>.</summary>
    [JsonRequired, JsonPropertyName("bucket_unit")] public string BucketUnit { get; set; } = "";
    [JsonRequired, JsonPropertyName("buckets")] public List<AdminInsightsActivityBucket> Buckets { get; set; } = [];
    [JsonRequired, JsonPropertyName("coverage")] public AdminInsightsCoverage Coverage { get; set; } = new();
    /// <summary>Sessions by end reason (stopped, terminated, expired, aborted, replaced; anything else is unknown). A breakdown.</summary>
    [JsonPropertyName("end_reasons")] public List<AdminInsightsLabeledCount> EndReasons { get; set; } = [];
    [JsonRequired, JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonRequired, JsonPropertyName("heatmap")] public List<AdminInsightsHeatmapCell> Heatmap { get; set; } = [];
    [JsonRequired, JsonPropertyName("peak_streams")] public List<AdminInsightsPeakStreamsPoint> PeakStreams { get; set; } = [];
    [JsonRequired, JsonPropertyName("summary")] public AdminInsightsActivitySummary Summary { get; set; } = new();
    [JsonRequired, JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonRequired, JsonPropertyName("tz")] public string Tz { get; set; } = "";
}

public sealed class AdminInsightsActivityBucket
{
    /// <summary>Distinct accounts with a session that started in the bucket. A breakdown: absent means unknown, not zero.</summary>
    [JsonPropertyName("active_accounts")] public long? ActiveAccounts { get; set; }
    /// <summary>Distinct profiles with a session that started in the bucket. A breakdown.</summary>
    [JsonPropertyName("active_profiles")] public long? ActiveProfiles { get; set; }
    [JsonRequired, JsonPropertyName("bucket_start")] public string BucketStart { get; set; } = "";
    [JsonRequired, JsonPropertyName("direct")] public long Direct { get; set; }
    [JsonRequired, JsonPropertyName("episodes")] public long Episodes { get; set; }
    /// <summary>Local bucket start date (YYYY-MM-DD in the requested tz).</summary>
    [JsonRequired, JsonPropertyName("label_date")] public string LabelDate { get; set; } = "";
    [JsonRequired, JsonPropertyName("movies")] public long Movies { get; set; }
    [JsonRequired, JsonPropertyName("other_media")] public long OtherMedia { get; set; }
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("remux")] public long Remux { get; set; }
    [JsonRequired, JsonPropertyName("transcode")] public long Transcode { get; set; }
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

public sealed class AdminInsightsActivitySummary
{
    [JsonRequired, JsonPropertyName("completed_plays")] public long CompletedPlays { get; set; }
    /// <summary>0..1.</summary>
    [JsonRequired, JsonPropertyName("completion_rate")] public double CompletionRate { get; set; }
    [JsonRequired, JsonPropertyName("direct_plays")] public long DirectPlays { get; set; }
    [JsonRequired, JsonPropertyName("episodes")] public long Episodes { get; set; }
    [JsonRequired, JsonPropertyName("measured_watch_seconds")] public long MeasuredWatchSeconds { get; set; }
    [JsonRequired, JsonPropertyName("movies")] public long Movies { get; set; }
    [JsonRequired, JsonPropertyName("other_media")] public long OtherMedia { get; set; }
    [JsonRequired, JsonPropertyName("peak_concurrent_at")] public string? PeakConcurrentAt { get; set; }
    [JsonRequired, JsonPropertyName("peak_concurrent_streams")] public long PeakConcurrentStreams { get; set; }
    /// <summary>Plays that started in the window.</summary>
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("remux_plays")] public long RemuxPlays { get; set; }
    /// <summary>Playback sessions that started in the window.</summary>
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
    [JsonRequired, JsonPropertyName("transcode_plays")] public long TranscodePlays { get; set; }
    [JsonRequired, JsonPropertyName("unique_accounts")] public long UniqueAccounts { get; set; }
    [JsonRequired, JsonPropertyName("unique_profiles")] public long UniqueProfiles { get; set; }
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

/// <summary>One cell of the weekday x hour grid: <c>day_of_week</c> 0 is Monday, 6 Sunday; <c>hour</c> is 0..23 in the requested tz.</summary>
public sealed class AdminInsightsHeatmapCell
{
    [JsonRequired, JsonPropertyName("day_of_week")] public long DayOfWeek { get; set; }
    [JsonRequired, JsonPropertyName("hour")] public long Hour { get; set; }
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

/// <summary>Peak concurrent streams for one UTC day.</summary>
public sealed class AdminInsightsPeakStreamsPoint
{
    /// <summary>UTC calendar day (YYYY-MM-DD).</summary>
    [JsonRequired, JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonRequired, JsonPropertyName("peak_direct")] public long PeakDirect { get; set; }
    [JsonRequired, JsonPropertyName("peak_remux")] public long PeakRemux { get; set; }
    [JsonRequired, JsonPropertyName("peak_streams")] public long PeakStreams { get; set; }
    [JsonRequired, JsonPropertyName("peak_streams_at")] public string? PeakStreamsAt { get; set; }
    [JsonRequired, JsonPropertyName("peak_streams_direct")] public long PeakStreamsDirect { get; set; }
    [JsonRequired, JsonPropertyName("peak_streams_remux")] public long PeakStreamsRemux { get; set; }
    [JsonRequired, JsonPropertyName("peak_streams_transcode")] public long PeakStreamsTranscode { get; set; }
    [JsonRequired, JsonPropertyName("peak_transcode")] public long PeakTranscode { get; set; }
}

// ── titles ───────────────────────────────────────────────────

/// <summary>GET /admin/insights/titles.</summary>
public sealed class AdminInsightsTitles
{
    [JsonRequired, JsonPropertyName("from")] public string From { get; set; } = "";
    /// <summary>Titles with at least 3 plays and one left unfinished, lowest completion first. A breakdown.</summary>
    [JsonPropertyName("least_finished")] public List<AdminInsightsTitleRow> LeastFinished { get; set; } = [];
    [JsonRequired, JsonPropertyName("libraries")] public List<AdminInsightsLibraryRow> Libraries { get; set; } = [];
    [JsonRequired, JsonPropertyName("movies")] public List<AdminInsightsTitleRow> Movies { get; set; } = [];
    [JsonRequired, JsonPropertyName("shows")] public List<AdminInsightsTitleRow> Shows { get; set; } = [];
    /// <summary>The ranking the rows were sorted by: <c>plays</c>, <c>watch_time</c> or <c>viewers</c>.</summary>
    [JsonRequired, JsonPropertyName("sort")] public string Sort { get; set; } = "";
    [JsonRequired, JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonRequired, JsonPropertyName("tz")] public string Tz { get; set; } = "";
}

public sealed class AdminInsightsTitleRow
{
    [JsonRequired, JsonPropertyName("completed_plays")] public long CompletedPlays { get; set; }
    [JsonRequired, JsonPropertyName("content_id")] public string ContentId { get; set; } = "";
    /// <summary>Series: distinct episodes with a play; 0 for movies.</summary>
    [JsonRequired, JsonPropertyName("episodes")] public long Episodes { get; set; }
    /// <summary><c>movie</c> or <c>series</c>.</summary>
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonRequired, JsonPropertyName("last_played_at")] public string? LastPlayedAt { get; set; }
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("title")] public string Title { get; set; } = "";
    /// <summary>Distinct household profiles with a play.</summary>
    [JsonRequired, JsonPropertyName("viewers")] public long Viewers { get; set; }
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
    [JsonRequired, JsonPropertyName("year")] public long? Year { get; set; }
}

public sealed class AdminInsightsLibraryRow
{
    [JsonRequired, JsonPropertyName("library_id")] public string LibraryId { get; set; } = "";
    [JsonRequired, JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("viewers")] public long Viewers { get; set; }
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

// ── one title ────────────────────────────────────────────────

/// <summary>GET /admin/insights/titles/{content_id}. An episode id answers 422.</summary>
public sealed class AdminInsightsTitle
{
    [JsonRequired, JsonPropertyName("bucket_unit")] public string BucketUnit { get; set; } = "";
    [JsonRequired, JsonPropertyName("buckets")] public List<AdminInsightsPlaysBucket> Buckets { get; set; } = [];
    [JsonRequired, JsonPropertyName("content_id")] public string ContentId { get; set; } = "";
    /// <summary>Movie runtime in seconds; null for series or when unknown.</summary>
    [JsonRequired, JsonPropertyName("duration_seconds")] public double? DurationSeconds { get; set; }
    [JsonRequired, JsonPropertyName("episodes")] public List<AdminInsightsTitleEpisode> Episodes { get; set; } = [];
    /// <summary>More than 500 episodes had a play; <see cref="Episodes"/> holds the first 500.</summary>
    [JsonRequired, JsonPropertyName("episodes_truncated")] public bool EpisodesTruncated { get; set; }
    [JsonRequired, JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonRequired, JsonPropertyName("summary")] public AdminInsightsTitleSummary Summary { get; set; } = new();
    [JsonRequired, JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonRequired, JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonRequired, JsonPropertyName("tz")] public string Tz { get; set; } = "";
    /// <summary>Profiles with a play in the window, most recent first.</summary>
    [JsonRequired, JsonPropertyName("viewers")] public List<AdminInsightsTitleViewer> Viewers { get; set; } = [];
    /// <summary>More than 100 profiles played the title; <see cref="Viewers"/> holds the most recent 100.</summary>
    [JsonRequired, JsonPropertyName("viewers_truncated")] public bool ViewersTruncated { get; set; }
    [JsonRequired, JsonPropertyName("year")] public long? Year { get; set; }
}

public sealed class AdminInsightsPlaysBucket
{
    [JsonRequired, JsonPropertyName("bucket_start")] public string BucketStart { get; set; } = "";
    [JsonRequired, JsonPropertyName("label_date")] public string LabelDate { get; set; } = "";
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

public sealed class AdminInsightsTitleEpisode
{
    [JsonRequired, JsonPropertyName("content_id")] public string ContentId { get; set; } = "";
    [JsonRequired, JsonPropertyName("episode_number")] public long EpisodeNumber { get; set; }
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("season_number")] public long SeasonNumber { get; set; }
    [JsonRequired, JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonRequired, JsonPropertyName("viewers")] public long Viewers { get; set; }
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

public sealed class AdminInsightsTitleSummary
{
    [JsonRequired, JsonPropertyName("completed_plays")] public long CompletedPlays { get; set; }
    [JsonRequired, JsonPropertyName("direct_plays")] public long DirectPlays { get; set; }
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("remux_plays")] public long RemuxPlays { get; set; }
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
    [JsonRequired, JsonPropertyName("transcode_plays")] public long TranscodePlays { get; set; }
    [JsonRequired, JsonPropertyName("viewers")] public long Viewers { get; set; }
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

public sealed class AdminInsightsTitleViewer
{
    [JsonRequired, JsonPropertyName("completed_plays")] public long CompletedPlays { get; set; }
    /// <summary>Movies: furthest position a session in the window reached; null for series.</summary>
    [JsonRequired, JsonPropertyName("furthest_position_seconds")] public double? FurthestPositionSeconds { get; set; }
    /// <summary>Series: the episode of the profile's latest play; absent for movies.</summary>
    [JsonPropertyName("last_episode")] public AdminInsightsEpisodeRef? LastEpisode { get; set; }
    [JsonRequired, JsonPropertyName("last_played_at")] public string? LastPlayedAt { get; set; }
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("profile_id")] public string ProfileId { get; set; } = "";
    [JsonRequired, JsonPropertyName("profile_name")] public string ProfileName { get; set; } = "";
    [JsonRequired, JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonRequired, JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

public sealed class AdminInsightsEpisodeRef
{
    [JsonRequired, JsonPropertyName("content_id")] public string ContentId { get; set; } = "";
    [JsonRequired, JsonPropertyName("episode_number")] public long EpisodeNumber { get; set; }
    [JsonRequired, JsonPropertyName("season_number")] public long SeasonNumber { get; set; }
    [JsonRequired, JsonPropertyName("title")] public string Title { get; set; } = "";
}

// ── library ──────────────────────────────────────────────────

/// <summary>GET /admin/insights/library.</summary>
public sealed class AdminInsightsLibrary
{
    /// <summary>Up to 15 content ratings by plays.</summary>
    [JsonRequired, JsonPropertyName("content_ratings")] public List<AdminInsightsLabeledPlays> ContentRatings { get; set; } = [];
    /// <summary>Plays by release decade: key is the decade's first year, oldest first, <c>unknown</c> last.</summary>
    [JsonRequired, JsonPropertyName("decades")] public List<AdminInsightsLabeledPlays> Decades { get; set; } = [];
    [JsonRequired, JsonPropertyName("from")] public string From { get; set; } = "";
    /// <summary>Up to 20 genres by plays; a title counts in each of its genres.</summary>
    [JsonRequired, JsonPropertyName("genres")] public List<AdminInsightsLabeledPlays> Genres { get; set; } = [];
    /// <summary>Libraries with movies or series, most titles first.</summary>
    [JsonRequired, JsonPropertyName("libraries")] public List<AdminInsightsLibraryCoverage> Libraries { get; set; } = [];
    [JsonRequired, JsonPropertyName("oldest_history_at")] public string? OldestHistoryAt { get; set; }
    /// <summary>Up to 20 titles added in the window with no play yet, newest first.</summary>
    [JsonRequired, JsonPropertyName("recently_added_unplayed")] public List<AdminInsightsUnplayedTitle> RecentlyAddedUnplayed { get; set; } = [];
    [JsonRequired, JsonPropertyName("summary")] public AdminInsightsLibrarySummary Summary { get; set; } = new();
    /// <summary>Keys <c>day</c>, <c>week</c>, <c>month</c>, <c>later</c>, <c>not_yet</c>; <c>sessions</c> holds the title count.</summary>
    [JsonRequired, JsonPropertyName("time_to_first_play")] public List<AdminInsightsLabeledCount> TimeToFirstPlay { get; set; } = [];
    [JsonRequired, JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonRequired, JsonPropertyName("tz")] public string Tz { get; set; } = "";
}

public sealed class AdminInsightsLibraryCoverage
{
    [JsonRequired, JsonPropertyName("added_titles")] public long AddedTitles { get; set; }
    [JsonRequired, JsonPropertyName("library_id")] public string LibraryId { get; set; } = "";
    [JsonRequired, JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonRequired, JsonPropertyName("never_started")] public long NeverStarted { get; set; }
    /// <summary>0..1.</summary>
    [JsonRequired, JsonPropertyName("played_share")] public double PlayedShare { get; set; }
    [JsonRequired, JsonPropertyName("played_titles")] public long PlayedTitles { get; set; }
    [JsonRequired, JsonPropertyName("titles")] public long Titles { get; set; }
}

public sealed class AdminInsightsLibrarySummary
{
    /// <summary>Titles added in the window that have a play since, up to now.</summary>
    [JsonRequired, JsonPropertyName("added_played")] public long AddedPlayed { get; set; }
    /// <summary>Titles first seen in the window.</summary>
    [JsonRequired, JsonPropertyName("added_titles")] public long AddedTitles { get; set; }
    /// <summary>Median days from being added to the first play, over titles added in the window that have one.</summary>
    [JsonRequired, JsonPropertyName("median_days_to_first_play")] public double? MedianDaysToFirstPlay { get; set; }
    /// <summary>Titles with no playback session at all, up to now.</summary>
    [JsonRequired, JsonPropertyName("never_started")] public long NeverStarted { get; set; }
    [JsonRequired, JsonPropertyName("played_share")] public double PlayedShare { get; set; }
    /// <summary>Titles with a play in the window; a series counts when any episode has one.</summary>
    [JsonRequired, JsonPropertyName("played_titles")] public long PlayedTitles { get; set; }
    /// <summary>Distinct movies and series in scope.</summary>
    [JsonRequired, JsonPropertyName("titles")] public long Titles { get; set; }
}

public sealed class AdminInsightsUnplayedTitle
{
    [JsonRequired, JsonPropertyName("added_at")] public string AddedAt { get; set; } = "";
    [JsonRequired, JsonPropertyName("content_id")] public string ContentId { get; set; } = "";
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonRequired, JsonPropertyName("library_id")] public string LibraryId { get; set; } = "";
    [JsonRequired, JsonPropertyName("library_name")] public string LibraryName { get; set; } = "";
    [JsonRequired, JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonRequired, JsonPropertyName("year")] public long? Year { get; set; }
}

// ── people ───────────────────────────────────────────────────

/// <summary>GET /admin/insights/people. One page of accounts; <c>total_accounts</c> counts every account.</summary>
public sealed class AdminInsightsPeople
{
    [JsonRequired, JsonPropertyName("accounts")] public List<AdminInsightsPersonAccount> Accounts { get; set; } = [];
    [JsonRequired, JsonPropertyName("active_accounts")] public long ActiveAccounts { get; set; }
    [JsonRequired, JsonPropertyName("active_profiles")] public long ActiveProfiles { get; set; }
    [JsonRequired, JsonPropertyName("coverage")] public AdminInsightsCoverage Coverage { get; set; } = new();
    [JsonRequired, JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonRequired, JsonPropertyName("live_accounts")] public long LiveAccounts { get; set; }
    [JsonRequired, JsonPropertyName("to")] public string To { get; set; } = "";
    /// <summary>Up to 10 profiles across all accounts with the most plays, whatever the sort, search or page. A breakdown.</summary>
    [JsonPropertyName("top_profiles")] public List<AdminInsightsTopProfile> TopProfiles { get; set; } = [];
    [JsonRequired, JsonPropertyName("total_accounts")] public long TotalAccounts { get; set; }
    [JsonRequired, JsonPropertyName("total_watch_seconds")] public long TotalWatchSeconds { get; set; }
    [JsonRequired, JsonPropertyName("tz")] public string Tz { get; set; } = "";
}

public sealed class AdminInsightsPersonAccount
{
    [JsonRequired, JsonPropertyName("completed_plays")] public long CompletedPlays { get; set; }
    [JsonRequired, JsonPropertyName("completion_rate")] public double CompletionRate { get; set; }
    [JsonRequired, JsonPropertyName("direct_plays")] public long DirectPlays { get; set; }
    [JsonRequired, JsonPropertyName("direct_rate")] public double DirectRate { get; set; }
    [JsonRequired, JsonPropertyName("episodes")] public long Episodes { get; set; }
    [JsonRequired, JsonPropertyName("is_live")] public bool IsLive { get; set; }
    [JsonRequired, JsonPropertyName("last_seen_at")] public string? LastSeenAt { get; set; }
    [JsonRequired, JsonPropertyName("measured_sessions")] public long MeasuredSessions { get; set; }
    /// <summary>Share of the account's sessions with measured watch time (0..1).</summary>
    [JsonRequired, JsonPropertyName("measured_share")] public double MeasuredShare { get; set; }
    [JsonRequired, JsonPropertyName("movies")] public long Movies { get; set; }
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("profiles")] public List<AdminInsightsPersonProfile> Profiles { get; set; } = [];
    [JsonRequired, JsonPropertyName("remux_plays")] public long RemuxPlays { get; set; }
    [JsonRequired, JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
    /// <summary>Client with the most sessions in the window.</summary>
    [JsonRequired, JsonPropertyName("top_client")] public string? TopClient { get; set; }
    [JsonRequired, JsonPropertyName("transcode_plays")] public long TranscodePlays { get; set; }
    [JsonRequired, JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonRequired, JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

public sealed class AdminInsightsPersonProfile
{
    [JsonRequired, JsonPropertyName("completed_plays")] public long CompletedPlays { get; set; }
    [JsonRequired, JsonPropertyName("completion_rate")] public double CompletionRate { get; set; }
    [JsonRequired, JsonPropertyName("direct_plays")] public long DirectPlays { get; set; }
    [JsonRequired, JsonPropertyName("is_live")] public bool IsLive { get; set; }
    [JsonRequired, JsonPropertyName("last_seen_at")] public string? LastSeenAt { get; set; }
    [JsonRequired, JsonPropertyName("measured_sessions")] public long MeasuredSessions { get; set; }
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("profile_id")] public string ProfileId { get; set; } = "";
    [JsonRequired, JsonPropertyName("profile_name")] public string ProfileName { get; set; } = "";
    [JsonRequired, JsonPropertyName("remux_plays")] public long RemuxPlays { get; set; }
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
    [JsonRequired, JsonPropertyName("transcode_plays")] public long TranscodePlays { get; set; }
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

public sealed class AdminInsightsTopProfile
{
    [JsonRequired, JsonPropertyName("plays")] public long Plays { get; set; }
    [JsonRequired, JsonPropertyName("profile_id")] public string ProfileId { get; set; } = "";
    [JsonRequired, JsonPropertyName("profile_name")] public string ProfileName { get; set; } = "";
    [JsonRequired, JsonPropertyName("user_id")] public string UserId { get; set; } = "";
    [JsonRequired, JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonRequired, JsonPropertyName("watch_seconds")] public long WatchSeconds { get; set; }
}

// ── devices ──────────────────────────────────────────────────

/// <summary>GET /admin/insights/devices.</summary>
public sealed class AdminInsightsDevices
{
    [JsonRequired, JsonPropertyName("adaptation_reasons")] public List<AdminInsightsAdaptationReason> AdaptationReasons { get; set; } = [];
    /// <summary>Sessions with recorded audio facts. A breakdown.</summary>
    [JsonPropertyName("audio")] public AdminInsightsAudioUsage? Audio { get; set; }
    /// <summary>Bucket size of <see cref="ProtocolBuckets"/>.</summary>
    [JsonRequired, JsonPropertyName("bucket_unit")] public string BucketUnit { get; set; } = "";
    /// <summary>Up to 50 reported client versions with the most sessions. A breakdown.</summary>
    [JsonPropertyName("client_versions")] public List<AdminInsightsClientVersionRow> ClientVersions { get; set; } = [];
    /// <summary>Video codec column order of the matrix cells.</summary>
    [JsonRequired, JsonPropertyName("codecs")] public List<string> Codecs { get; set; } = [];
    [JsonRequired, JsonPropertyName("coverage")] public AdminInsightsCoverage Coverage { get; set; } = new();
    [JsonRequired, JsonPropertyName("delivered_resolutions")] public List<AdminInsightsLabeledCount> DeliveredResolutions { get; set; } = [];
    [JsonRequired, JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonRequired, JsonPropertyName("hw_accel_breakdown")] public List<AdminInsightsLabeledCount> HwAccelBreakdown { get; set; } = [];
    /// <summary>Client and platform pairs with at least <c>min_sessions</c> sessions, at most 200.</summary>
    [JsonRequired, JsonPropertyName("matrix")] public List<AdminInsightsClientMatrixRow> Matrix { get; set; } = [];
    /// <summary>Sessions per bucket from Silo's own clients and from Jellyfin-protocol clients. A breakdown.</summary>
    [JsonPropertyName("protocol_buckets")] public List<AdminInsightsProtocolBucket> ProtocolBuckets { get; set; } = [];
    [JsonRequired, JsonPropertyName("resolution_changes")] public List<AdminInsightsResolutionChange> ResolutionChanges { get; set; } = [];
    [JsonRequired, JsonPropertyName("source_resolutions")] public List<AdminInsightsLabeledCount> SourceResolutions { get; set; } = [];
    [JsonRequired, JsonPropertyName("subtitle_burn_in_codecs")] public List<AdminInsightsLabeledCount> SubtitleBurnInCodecs { get; set; } = [];
    /// <summary>Subtitle usage. A breakdown.</summary>
    [JsonPropertyName("subtitles")] public AdminInsightsSubtitleUsage? Subtitles { get; set; }
    [JsonRequired, JsonPropertyName("summary")] public AdminInsightsDevicesSummary Summary { get; set; } = new();
    [JsonRequired, JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonRequired, JsonPropertyName("tone_map_breakdown")] public List<AdminInsightsLabeledCount> ToneMapBreakdown { get; set; } = [];
    /// <summary>Up to 10 clients ranked by transcoded sessions, whatever <c>min_sessions</c> is.</summary>
    [JsonRequired, JsonPropertyName("top_transcoders")] public List<AdminInsightsTranscoderRow> TopTranscoders { get; set; } = [];
    [JsonRequired, JsonPropertyName("tz")] public string Tz { get; set; } = "";
}

public sealed class AdminInsightsDevicesSummary
{
    [JsonRequired, JsonPropertyName("direct_rate")] public double DirectRate { get; set; }
    [JsonRequired, JsonPropertyName("direct_sessions")] public long DirectSessions { get; set; }
    [JsonRequired, JsonPropertyName("hdr_tonemap_hw_sessions")] public long HdrTonemapHwSessions { get; set; }
    [JsonRequired, JsonPropertyName("hdr_tonemap_sessions")] public long HdrTonemapSessions { get; set; }
    [JsonRequired, JsonPropertyName("hdr_tonemap_sw_sessions")] public long HdrTonemapSwSessions { get; set; }
    [JsonRequired, JsonPropertyName("hw_transcode_sessions")] public long HwTranscodeSessions { get; set; }
    [JsonRequired, JsonPropertyName("remux_sessions")] public long RemuxSessions { get; set; }
    [JsonRequired, JsonPropertyName("route_facts_sessions")] public long RouteFactsSessions { get; set; }
    [JsonRequired, JsonPropertyName("subtitle_burn_in_sessions")] public long SubtitleBurnInSessions { get; set; }
    [JsonRequired, JsonPropertyName("sw_transcode_sessions")] public long SwTranscodeSessions { get; set; }
    [JsonRequired, JsonPropertyName("total_sessions")] public long TotalSessions { get; set; }
    [JsonRequired, JsonPropertyName("transcode_sessions")] public long TranscodeSessions { get; set; }
}

public sealed class AdminInsightsClientMatrixRow
{
    [JsonRequired, JsonPropertyName("cells")] public List<AdminInsightsClientCodecCell> Cells { get; set; } = [];
    [JsonRequired, JsonPropertyName("client_name")] public string ClientName { get; set; } = "";
    [JsonRequired, JsonPropertyName("device_platform")] public string DevicePlatform { get; set; } = "";
    [JsonRequired, JsonPropertyName("direct_rate")] public double DirectRate { get; set; }
    [JsonRequired, JsonPropertyName("direct_sessions")] public long DirectSessions { get; set; }
    [JsonRequired, JsonPropertyName("is_jellyfin_client")] public bool IsJellyfinClient { get; set; }
    [JsonRequired, JsonPropertyName("remux_sessions")] public long RemuxSessions { get; set; }
    [JsonRequired, JsonPropertyName("total_sessions")] public long TotalSessions { get; set; }
    [JsonRequired, JsonPropertyName("transcode_sessions")] public long TranscodeSessions { get; set; }
}

public sealed class AdminInsightsClientCodecCell
{
    [JsonRequired, JsonPropertyName("direct_rate")] public double DirectRate { get; set; }
    [JsonRequired, JsonPropertyName("direct_sessions")] public long DirectSessions { get; set; }
    [JsonRequired, JsonPropertyName("remux_sessions")] public long RemuxSessions { get; set; }
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
    [JsonRequired, JsonPropertyName("source_video_codec")] public string SourceVideoCodec { get; set; } = "";
    [JsonRequired, JsonPropertyName("transcode_sessions")] public long TranscodeSessions { get; set; }
}

public sealed class AdminInsightsTranscoderRow
{
    [JsonRequired, JsonPropertyName("client_name")] public string ClientName { get; set; } = "";
    [JsonRequired, JsonPropertyName("device_platform")] public string DevicePlatform { get; set; } = "";
    [JsonRequired, JsonPropertyName("is_jellyfin_client")] public bool IsJellyfinClient { get; set; }
    [JsonRequired, JsonPropertyName("total_sessions")] public long TotalSessions { get; set; }
    [JsonRequired, JsonPropertyName("transcode_sessions")] public long TranscodeSessions { get; set; }
}

public sealed class AdminInsightsAdaptationReason
{
    [JsonRequired, JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
    [JsonRequired, JsonPropertyName("share_of_adapted")] public double ShareOfAdapted { get; set; }
}

/// <summary>Sessions delivered at a lower resolution class than their source.</summary>
public sealed class AdminInsightsResolutionChange
{
    /// <summary>Source resolution class.</summary>
    [JsonRequired, JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
    /// <summary>Lower delivered resolution class.</summary>
    [JsonRequired, JsonPropertyName("to")] public string To { get; set; } = "";
}

public sealed class AdminInsightsClientVersionRow
{
    [JsonRequired, JsonPropertyName("client_name")] public string ClientName { get; set; } = "";
    [JsonRequired, JsonPropertyName("client_version")] public string ClientVersion { get; set; } = "";
    [JsonRequired, JsonPropertyName("device_platform")] public string DevicePlatform { get; set; } = "";
    [JsonRequired, JsonPropertyName("is_jellyfin_client")] public bool IsJellyfinClient { get; set; }
    [JsonRequired, JsonPropertyName("last_seen_at")] public string? LastSeenAt { get; set; }
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
}

public sealed class AdminInsightsProtocolBucket
{
    [JsonRequired, JsonPropertyName("bucket_start")] public string BucketStart { get; set; } = "";
    [JsonRequired, JsonPropertyName("jellyfin_sessions")] public long JellyfinSessions { get; set; }
    [JsonRequired, JsonPropertyName("label_date")] public string LabelDate { get; set; } = "";
    [JsonRequired, JsonPropertyName("native_sessions")] public long NativeSessions { get; set; }
}

public sealed class AdminInsightsSubtitleUsage
{
    /// <summary>Subtitled sessions by delivery: <c>embedded</c>, <c>sidecar</c>, <c>hls_rendition</c> or <c>burn_in</c>.</summary>
    [JsonRequired, JsonPropertyName("deliveries")] public List<AdminInsightsLabeledCount> Deliveries { get; set; } = [];
    [JsonRequired, JsonPropertyName("forced_sessions")] public long ForcedSessions { get; set; }
    [JsonRequired, JsonPropertyName("hearing_impaired_sessions")] public long HearingImpairedSessions { get; set; }
    /// <summary>Subtitled sessions by language (ISO 639-1 where one exists, <c>unknown</c> when untagged).</summary>
    [JsonRequired, JsonPropertyName("languages")] public List<AdminInsightsLabeledCount> Languages { get; set; } = [];
    /// <summary>Sessions with route facts, the ones that record their subtitle.</summary>
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
    [JsonRequired, JsonPropertyName("subtitled_sessions")] public long SubtitledSessions { get; set; }
}

public sealed class AdminInsightsAudioUsage
{
    /// <summary>Sessions by audio handling: <c>direct</c>, <c>remux</c> or <c>transcode</c>.</summary>
    [JsonRequired, JsonPropertyName("decisions")] public List<AdminInsightsLabeledCount> Decisions { get; set; } = [];
    /// <summary>Sessions by delivered channel layout: <c>mono</c>, <c>stereo</c>, <c>surround</c> or <c>unknown</c>.</summary>
    [JsonRequired, JsonPropertyName("delivered_channels")] public List<AdminInsightsLabeledCount> DeliveredChannels { get; set; } = [];
    [JsonRequired, JsonPropertyName("languages")] public List<AdminInsightsLabeledCount> Languages { get; set; } = [];
    /// <summary>Sessions with route facts, the ones that record their audio track.</summary>
    [JsonRequired, JsonPropertyName("sessions")] public long Sessions { get; set; }
}

// ── bandwidth ────────────────────────────────────────────────

/// <summary>GET /admin/insights/bandwidth. Daily points are UTC days even when <c>tz</c> differs.</summary>
public sealed class AdminInsightsBandwidth
{
    [JsonRequired, JsonPropertyName("from")] public string From { get; set; } = "";
    /// <summary>True when any stream node is registered or contributed bandwidth in the window.</summary>
    [JsonRequired, JsonPropertyName("has_remote_nodes")] public bool HasRemoteNodes { get; set; }
    [JsonRequired, JsonPropertyName("oldest_metric_at")] public string? OldestMetricAt { get; set; }
    [JsonRequired, JsonPropertyName("oldest_route_facts_at")] public string? OldestRouteFactsAt { get; set; }
    /// <summary>Daily UTC points in ascending date order; quiet days are omitted.</summary>
    [JsonRequired, JsonPropertyName("points")] public List<AdminInsightsBandwidthPoint> Points { get; set; } = [];
    /// <summary>Per-source totals (API first, then stream nodes by egress descending).</summary>
    [JsonRequired, JsonPropertyName("sources")] public List<AdminInsightsEgressSource> Sources { get; set; } = [];
    [JsonRequired, JsonPropertyName("summary")] public AdminInsightsBandwidthSummary Summary { get; set; } = new();
    [JsonRequired, JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonRequired, JsonPropertyName("tz")] public string Tz { get; set; } = "";
}

public sealed class AdminInsightsBandwidthPoint
{
    [JsonRequired, JsonPropertyName("api_egress_bytes")] public long ApiEgressBytes { get; set; }
    /// <summary>Daily egress in bytes keyed by <c>sources[].key</c> (<c>api</c> or <c>node:&lt;id&gt;</c>).</summary>
    [JsonRequired, JsonPropertyName("bytes_by_source")] public Dictionary<string, long> BytesBySource { get; set; } = new(StringComparer.Ordinal);
    /// <summary>UTC calendar day (YYYY-MM-DD).</summary>
    [JsonRequired, JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonRequired, JsonPropertyName("download_egress_bytes")] public long DownloadEgressBytes { get; set; }
    [JsonRequired, JsonPropertyName("egress_bytes")] public long EgressBytes { get; set; }
    [JsonRequired, JsonPropertyName("peak_download_kbps")] public long PeakDownloadKbps { get; set; }
    [JsonRequired, JsonPropertyName("peak_egress_kbps")] public long PeakEgressKbps { get; set; }
    /// <summary>Daily peak minute egress in kbps keyed by <c>sources[].key</c>.</summary>
    [JsonRequired, JsonPropertyName("peak_kbps_by_source")] public Dictionary<string, long> PeakKbpsBySource { get; set; } = new(StringComparer.Ordinal);
    [JsonRequired, JsonPropertyName("playback_egress_bytes")] public long PlaybackEgressBytes { get; set; }
}

public sealed class AdminInsightsBandwidthSummary
{
    [JsonRequired, JsonPropertyName("active_days")] public long ActiveDays { get; set; }
    [JsonRequired, JsonPropertyName("api_egress_bytes")] public long ApiEgressBytes { get; set; }
    [JsonRequired, JsonPropertyName("avg_daily_bytes")] public long AvgDailyBytes { get; set; }
    [JsonRequired, JsonPropertyName("download_egress_bytes")] public long DownloadEgressBytes { get; set; }
    /// <summary>Sessions started in the window classified local (a count of sessions, not bytes).</summary>
    [JsonRequired, JsonPropertyName("local_sessions")] public long LocalSessions { get; set; }
    [JsonRequired, JsonPropertyName("node_egress_bytes")] public long NodeEgressBytes { get; set; }
    [JsonRequired, JsonPropertyName("peak_download_kbps")] public long PeakDownloadKbps { get; set; }
    [JsonRequired, JsonPropertyName("peak_egress_at")] public string? PeakEgressAt { get; set; }
    [JsonRequired, JsonPropertyName("peak_egress_kbps")] public long PeakEgressKbps { get; set; }
    [JsonRequired, JsonPropertyName("playback_egress_bytes")] public long PlaybackEgressBytes { get; set; }
    /// <summary>Sessions started in the window classified remote (a count of sessions, not bytes).</summary>
    [JsonRequired, JsonPropertyName("remote_sessions")] public long RemoteSessions { get; set; }
    [JsonRequired, JsonPropertyName("total_egress_bytes")] public long TotalEgressBytes { get; set; }
    [JsonRequired, JsonPropertyName("unknown_location_sessions")] public long UnknownLocationSessions { get; set; }
}

public sealed class AdminInsightsEgressSource
{
    [JsonRequired, JsonPropertyName("egress_bytes")] public long EgressBytes { get; set; }
    /// <summary>The key used in <c>bytes_by_source</c> and <c>peak_kbps_by_source</c>: <c>api</c> or <c>node:&lt;node_id&gt;</c>.</summary>
    [JsonRequired, JsonPropertyName("key")] public string Key { get; set; } = "";
    /// <summary><c>api</c> (all API server processes together) or <c>node</c>.</summary>
    [JsonRequired, JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonRequired, JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>The stream node identifier when <see cref="Kind"/> is <c>node</c>; absent otherwise.</summary>
    [JsonPropertyName("node_id")] public string? NodeId { get; set; }
    /// <summary><c>proxy</c> or <c>transcode</c> when known; null for the API or a removed node without a recorded type.</summary>
    [JsonRequired, JsonPropertyName("node_type")] public string? NodeType { get; set; }
    [JsonRequired, JsonPropertyName("peak_egress_kbps")] public long PeakEgressKbps { get; set; }
    /// <summary>Fraction of the total served by this source, 0..1.</summary>
    [JsonRequired, JsonPropertyName("share_of_total")] public double ShareOfTotal { get; set; }
}
