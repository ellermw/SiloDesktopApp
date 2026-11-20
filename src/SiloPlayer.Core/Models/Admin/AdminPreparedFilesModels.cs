using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminDownloadStorage
{
    public List<AdminDownloadStorageLocation> Locations { get; set; } = null!;
    public int CacheHours { get; set; }
    public int DiskCeilingPercent { get; set; }
    public long DefaultBudgetBytes { get; set; }
    public int StaleDeviceDays { get; set; }
    public int PreparingJobs { get; set; }
    public long FreedLast30DaysBytes { get; set; }
}

public sealed class AdminDownloadStorageLocation
{
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? NodeId { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Online { get; set; }
    public DateTimeOffset? LastHealthCheck { get; set; }
    public string Dir { get; set; } = "";
    public string DirSource { get; set; } = "";
    public string PendingDir { get; set; } = "";
    public AdminDownloadStorageUsage? Usage { get; set; }
    public int InUseFiles { get; set; }
    public long InUseBytes { get; set; }
    public int CachedFiles { get; set; }
    public long CachedBytes { get; set; }
    public int WaitingDownloads { get; set; }
    public long StaleWaitingBytes { get; set; }
    public int UntrackedFiles { get; set; }
    public long UntrackedBytes { get; set; }
    public DateTimeOffset? ReconciledAt { get; set; }
    public long BudgetBytes { get; set; }
    public string BudgetSource { get; set; } = "";
    public int CleanupBacklog { get; set; }
    public bool StorageFull { get; set; }
    public bool ReplicasDisagree { get; set; }
}

public sealed class AdminDownloadStorageUsage
{
    public DateTimeOffset MeasuredAt { get; set; }
    public int Files { get; set; }
    public long Bytes { get; set; }
    public int PartialFiles { get; set; }
    public long PartialBytes { get; set; }
    public long OtherBytes { get; set; }
    [JsonPropertyName("fs_used_bytes")]
    public long FSUsedBytes { get; set; }
    [JsonPropertyName("fs_total_bytes")]
    public long FSTotalBytes { get; set; }
    public string FSType { get; set; } = "";
    public bool SharesScratch { get; set; }
    public bool Ephemeral { get; set; }
    public bool Stale { get; set; }
    public string Error { get; set; } = "";
}

public sealed class AdminPreparedFile
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public string Format { get; set; } = "";
    public string Location { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string MediaFileId { get; set; } = "";
    public string? LibraryId { get; set; }
    public string ContentId { get; set; } = "";
    public string EpisodeId { get; set; } = "";
    public string Title { get; set; } = "";
    public string MediaType { get; set; } = "";
    public int? Year { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string EpisodeTitle { get; set; } = "";
    public string Container { get; set; } = "";
    public string VideoCodec { get; set; } = "";
    public string AudioCodec { get; set; } = "";
    public string Resolution { get; set; } = "";
    public int? BitrateKbps { get; set; }
    public long Bytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int Waiting { get; set; }
    public int Downloading { get; set; }
    public int Finished { get; set; }
    public int StaleWaiting { get; set; }
    public AdminDownloadWaitingDevice? OldestWaiting { get; set; }
}

public sealed class AdminDownloadWaitingDevice
{
    public string DeviceName { get; set; } = "";
    public string Username { get; set; } = "";
    public DateTimeOffset? LastSeenAt { get; set; }
}

public sealed class AdminDownloadPreparationList
{
    public AdminDownloadPreparationCounts Counts { get; set; } = null!;
    public List<AdminDownloadPreparation> Items { get; set; } = null!;
}

public sealed class AdminDownloadPreparationCounts
{
    public int Running { get; set; }
    public int Queued { get; set; }
    public int Retrying { get; set; }
    public int Paused { get; set; }
    public int FailedRecent { get; set; }
}

public sealed class AdminDownloadPreparation
{
    public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public string Format { get; set; } = "";
    public int? QueuePosition { get; set; }
    public string LogSessionId { get; set; } = "";
    public string MediaFileId { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string EpisodeId { get; set; } = "";
    public string MediaTitle { get; set; } = "";
    public string MediaType { get; set; } = "";
    public string SeriesName { get; set; } = "";
    public string EpisodeName { get; set; } = "";
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public AdminDownloadPreparationSource Source { get; set; } = null!;
    public AdminDownloadPreparationOutput Output { get; set; } = null!;
    public AdminDownloadPreparationWorker? Worker { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public string Error { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public AdminDownloadPreparationProgress? Progress { get; set; }
    public bool ProgressUnavailable { get; set; }
    public List<AdminDownloadPreparationRequester> Requesters { get; set; } = null!;
}

public sealed class AdminDownloadPreparationSource
{
    public string Container { get; set; } = "";
    public string VideoCodec { get; set; } = "";
    public string Resolution { get; set; } = "";
    public bool Hdr { get; set; }
    public List<AdminDownloadPreparationAudioTrack> AudioTracks { get; set; } = null!;
    public long FileSize { get; set; }
    public double DurationSeconds { get; set; }
    public int? BitrateKbps { get; set; }
}

public sealed class AdminDownloadPreparationAudioTrack
{
    public string Codec { get; set; } = "";
    public int? Channels { get; set; }
    public string Language { get; set; } = "";
}

public sealed class AdminDownloadPreparationOutput
{
    public string Container { get; set; } = "";
    public string VideoCodec { get; set; } = "";
    public string AudioCodec { get; set; } = "";
    public string Resolution { get; set; } = "";
    public int? BitrateKbps { get; set; }
    public string ToneMapMode { get; set; } = "";
    public string ToneMapSourceKind { get; set; } = "";
    public bool AllAudioTracks { get; set; }
}

public sealed class AdminDownloadPreparationWorker
{
    public string Kind { get; set; } = "";
    public string? NodeId { get; set; }
    public string Name { get; set; } = "";
}

public sealed class AdminDownloadPreparationProgress
{
    public double EncodedSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public double Speed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AdminDownloadPreparationRequester
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DevicePlatform { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class AdminDownloadDevice
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Platform { get; set; } = "";
    public DateTimeOffset? LastSeenAt { get; set; }
    public bool Stale { get; set; }
    public int Copies { get; set; }
    public int Finished { get; set; }
    public int Waiting { get; set; }
    public int Failed { get; set; }
    public int Revoked { get; set; }
    public long BytesOnDevice { get; set; }
    public int Monitors { get; set; }
}

public sealed class AdminDownloadEntry
{
    public string Id { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string EpisodeId { get; set; } = "";
    public string Title { get; set; } = "";
    public string MediaType { get; set; } = "";
    public AdminDownloadEntryEpisode? Episode { get; set; }
    public string Status { get; set; } = "";
    public string Quality { get; set; } = "";
    public string EffectiveQuality { get; set; } = "";
    public string DeliveryFormat { get; set; } = "";
    public int TargetBitrateKbps { get; set; }
    public long FileSize { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? StatusEventAt { get; set; }
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public DateTimeOffset? RevokedAt { get; set; }
    public string RevokedReason { get; set; } = "";
    public string? CreatedClientIp { get; set; }
    public string? LastClientIp { get; set; }
    public string Location { get; set; } = "";
    public string LocationName { get; set; } = "";
}

public sealed class AdminDownloadEntryEpisode
{
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string Title { get; set; } = "";
}

public sealed class AdminDownloadStorageEvent
{
    public string Id { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Location { get; set; } = "";
    public string LocationName { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public int Count { get; set; }
    public long Bytes { get; set; }
    public List<string> Titles { get; set; } = null!;
    public string Detail { get; set; } = "";
    public AdminDownloadStorageUser? Actor { get; set; }
    public AdminDownloadStorageUser? Account { get; set; }
}

public sealed class AdminDownloadStorageUser { public string Id { get; set; } = ""; public string Username { get; set; } = ""; }
public sealed class AdminPreparationActionResponse { public List<AdminPreparationActionResult> Results { get; set; } = null!; }
public sealed class AdminPreparationActionResult { public string Id { get; set; } = ""; public string Outcome { get; set; } = ""; }
public sealed class AdminPreparedFileDeleteResponse { public List<AdminPreparedFileDeleteResult> Results { get; set; } = null!; }
public sealed class AdminPreparedFileDeleteResult { public string Id { get; set; } = ""; public string Outcome { get; set; } = ""; public long Bytes { get; set; } }
public sealed class AdminStorageCleanupResult { public long FreedBytes { get; set; } }
public sealed class AdminUntrackedDeleteResult { public int Files { get; set; } public long Bytes { get; set; } public int FailedFiles { get; set; } }
public sealed class AdminDownloadRevokeResult { public int Revoked { get; set; } public long Bytes { get; set; } public int PausedMonitors { get; set; } public List<string> DownloadIds { get; set; } = null!; }

public sealed class AdminPreparedFilesNode
{
    [JsonPropertyName("config_etag")]
    public string ConfigETag { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Healthy { get; set; }
    public int ActiveJobs { get; set; }
    public int EgressKbps { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? DownloadArtifactDirOverride { get; set; }
    public long? DownloadArtifactMaxBytesOverride { get; set; }
    public List<string> EnvManaged { get; set; } = [];
}

/// <summary>The changed fields of a node location edit; unchanged fields are never sent.</summary>
public sealed record AdminNodeLocationChange(bool DirectoryChanged, string? Directory, bool BudgetChanged, long? BudgetBytes)
{
    public bool HasChanges => DirectoryChanged || BudgetChanged;
}

public sealed record AdminDownloadRevokeTarget
{
    public IReadOnlyList<string>? Ids { get; init; }
    public string? UserId { get; init; }
    public string? ProfileId { get; init; }
    public string? DeviceId { get; init; }
    public bool PauseMonitors { get; init; }
    public static AdminDownloadRevokeTarget ForIds(IReadOnlyList<string> ids) => new() { Ids = ids };
    public static AdminDownloadRevokeTarget ForDevice(AdminDownloadDevice device, bool pauseMonitors) => new()
        { UserId = device.UserId, ProfileId = device.ProfileId, DeviceId = device.DeviceId, PauseMonitors = pauseMonitors };
}

public static partial class AdminPreparedFilesContract
{
    public const int PageSize = 50;
    public const int MaxActionIds = 500;
    public const int PreparationLimit = 200;
    public static bool IsLocation(string? value) => value != null && LocationPattern().IsMatch(value);
    public static bool IsId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 1024 && !value.Any(char.IsControl);
    public static bool IsArtifactId(string? value) => value != null && ArtifactIdPattern().IsMatch(value);
    [GeneratedRegex("^(server|node:[1-9][0-9]*)$", RegexOptions.CultureInvariant)] private static partial Regex LocationPattern();
    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)] private static partial Regex ArtifactIdPattern();
}

public sealed record AdminPreparedFileQuery
{
    public string? Location { get; init; }
    public string? State { get; init; }
    public string? Format { get; init; }
    public string? LibraryId { get; init; }
    public string? Search { get; init; }
    public string Sort { get; init; } = "size";
    public AdminPreparedFileQuery Normalize() => this with
    {
        Location = AdminPreparedFilesContract.IsLocation(Location) ? Location : null,
        State = State is "in_use" or "cached" or "expired" ? State : null,
        Format = Format is "remux" or "transcode" ? Format : null,
        LibraryId = PositiveId(LibraryId), Search = Trim(Search, 200),
        Sort = Sort is "last_used" or "created" ? Sort : "size",
    };
    internal string ToQuery(string? cursor) => AdminPreparedQuery.Build(("location", Location), ("state", State), ("format", Format),
        ("library_id", LibraryId), ("q", Search), ("sort", Sort), ("limit", AdminPreparedFilesContract.PageSize.ToString()), ("cursor", cursor));
    internal static string? PositiveId(string? value) => value?.Trim() is { Length: > 0 } v && v[0] != '0' && v.All(char.IsAsciiDigit) ? v : null;
    internal static string? Trim(string? value, int max) { var v = value?.Trim(); return string.IsNullOrEmpty(v) ? null : v[..Math.Min(v.Length, max)]; }
}

public sealed record AdminDownloadDeviceQuery
{
    public string? Search { get; init; }
    public bool Stale { get; init; }
    public string? Platform { get; init; }
    public string Sort { get; init; } = "last_seen";
    public AdminDownloadDeviceQuery Normalize() => this with { Search = AdminPreparedFileQuery.Trim(Search, 200), Platform = AdminPreparedFileQuery.Trim(Platform, 64), Sort = Sort == "size" ? "size" : "last_seen" };
    internal string ToQuery(string? cursor) => AdminPreparedQuery.Build(("q", Search), ("stale", Stale ? "true" : null), ("platform", Platform),
        ("sort", Sort), ("limit", AdminPreparedFilesContract.PageSize.ToString()), ("cursor", cursor));
}

public sealed record AdminDownloadEntryQuery(string UserId, string ProfileId, string DeviceId)
{
    internal string ToQuery(string? cursor) => AdminPreparedQuery.Build(("user_id", UserId), ("profile_id", ProfileId), ("device_id", DeviceId),
        ("limit", AdminPreparedFilesContract.PageSize.ToString()), ("cursor", cursor));
}

public sealed record AdminDownloadHistoryQuery
{
    public string? Reason { get; init; }
    public string? Location { get; init; }
    public int Days { get; init; } = 30;
    public AdminDownloadHistoryQuery Normalize() => this with
    {
        Reason = AdminPreparedFileQuery.Trim(Reason, 32),
        Location = Location == "device" || AdminPreparedFilesContract.IsLocation(Location) ? Location : null,
        Days = Days is >= 0 and <= 90 ? Days : 30,
    };
    internal string ToQuery(string? cursor) => AdminPreparedQuery.Build(("reason", Reason), ("location", Location), ("days", Days.ToString()),
        ("limit", AdminPreparedFilesContract.PageSize.ToString()), ("cursor", cursor));
}

internal static class AdminPreparedQuery
{
    internal static string Build(params (string Key, string? Value)[] values) => "?" + string.Join('&', values.Where(x => !string.IsNullOrEmpty(x.Value))
        .Select(x => x.Key + "=" + Uri.EscapeDataString(x.Value!)));
}
