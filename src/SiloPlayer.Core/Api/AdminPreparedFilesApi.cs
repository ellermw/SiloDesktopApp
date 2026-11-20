using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Official admin prepared-file contracts pinned to Server/WebUI 217ff25a.</summary>
public sealed class AdminPreparedFilesApi(SiloApiClient client)
{
    public async Task<AdminDownloadStorage> GetStorageAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var storage = (await client.AdminPreparedFilesSendAsync<AdminDownloadStorage>(lease, HttpMethod.Get,
            "/api/v2/admin/downloads/storage", ct: ct).ConfigureAwait(false)).Body;
        ValidateStorage(storage);
        return storage;
    }

    public async Task<AdminDownloadPreparationList> ListPreparationsAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var list = (await client.AdminPreparedFilesSendAsync<AdminDownloadPreparationList>(lease, HttpMethod.Get,
            "/api/v2/admin/downloads/preparations?limit=" + AdminPreparedFilesContract.PreparationLimit, ct: ct).ConfigureAwait(false)).Body;
        if (list.Items == null || list.Counts == null || list.Items.Count > AdminPreparedFilesContract.PreparationLimit)
            throw new AdminListInconsistentException(AdminListProblem.OversizedPage);
        if (list.Counts.Running < 0 || list.Counts.Queued < 0 || list.Counts.Retrying < 0 || list.Counts.Paused < 0 || list.Counts.FailedRecent < 0)
            throw new InvalidDataException("Invalid preparation counts.");
        ValidateDistinct(list.Items, x => x.Id, ValidatePreparation);
        return list;
    }

    public async Task<AdminCursorPage<AdminPreparedFile>> ListFilesAsync(MonitorAuthorityLease lease, AdminPreparedFileQuery query,
        string? cursor, CancellationToken ct = default)
    {
        var normalized = query.Normalize();
        ValidateCursor(cursor);
        var body = (await client.AdminPreparedFilesSendAsync<ApiCollectionPage<AdminPreparedFile>>(lease, HttpMethod.Get,
            "/api/v2/admin/downloads/storage/files" + normalized.ToQuery(cursor), ct: ct).ConfigureAwait(false)).Body;
        return ValidatePage(body, cursor, x => x.Id, ValidateFile);
    }

    public async Task<AdminCursorPage<AdminDownloadDevice>> ListDevicesAsync(MonitorAuthorityLease lease, AdminDownloadDeviceQuery query,
        string? cursor, CancellationToken ct = default)
    {
        var normalized = query.Normalize();
        ValidateCursor(cursor);
        var body = (await client.AdminPreparedFilesSendAsync<ApiCollectionPage<AdminDownloadDevice>>(lease, HttpMethod.Get,
            "/api/v2/admin/downloads/devices" + normalized.ToQuery(cursor), ct: ct).ConfigureAwait(false)).Body;
        return ValidatePage(body, cursor, DeviceKey, ValidateDevice);
    }

    public async Task<AdminCursorPage<AdminDownloadEntry>> ListEntriesAsync(MonitorAuthorityLease lease, AdminDownloadEntryQuery query,
        string? cursor, CancellationToken ct = default)
    {
        if (AdminPreparedFileQuery.PositiveId(query.UserId) == null || string.IsNullOrWhiteSpace(query.ProfileId) || string.IsNullOrWhiteSpace(query.DeviceId))
            throw new ArgumentException("A complete device owner is required.", nameof(query));
        ValidateCursor(cursor);
        var body = (await client.AdminPreparedFilesSendAsync<ApiCollectionPage<AdminDownloadEntry>>(lease, HttpMethod.Get,
            "/api/v2/admin/downloads/entries" + query.ToQuery(cursor), ct: ct).ConfigureAwait(false)).Body;
        return ValidatePage(body, cursor, x => x.Id, ValidateEntry);
    }

    public async Task<AdminCursorPage<AdminDownloadStorageEvent>> ListHistoryAsync(MonitorAuthorityLease lease, AdminDownloadHistoryQuery query,
        string? cursor, CancellationToken ct = default)
    {
        var normalized = query.Normalize();
        ValidateCursor(cursor);
        var body = (await client.AdminPreparedFilesSendAsync<ApiCollectionPage<AdminDownloadStorageEvent>>(lease, HttpMethod.Get,
            "/api/v2/admin/downloads/storage/events" + normalized.ToQuery(cursor), ct: ct).ConfigureAwait(false)).Body;
        return ValidatePage(body, cursor, x => x.Id, ValidateEvent);
    }

    public async Task<IReadOnlyList<AdminPreparationActionResult>> RunPreparationActionAsync(MonitorAuthorityLease lease, string action,
        IReadOnlyList<string> ids, CancellationToken ct = default)
    {
        if (action is not ("pause" or "resume" or "cancel")) throw new ArgumentOutOfRangeException(nameof(action));
        var distinct = RequireArtifactIds(ids);
        var results = new List<AdminPreparationActionResult>();
        foreach (var batch in distinct.Chunk(AdminPreparedFilesContract.MaxActionIds))
        {
            var body = (await client.AdminPreparedFilesSendAsync<AdminPreparationActionResponse>(lease, HttpMethod.Post,
                "/api/v2/admin/downloads/preparations/" + action, new Dictionary<string, object?> { ["ids"] = batch }, ct: ct).ConfigureAwait(false)).Body;
            ValidateActionResults(body.Results, batch);
            results.AddRange(body.Results);
        }
        return results;
    }

    public async Task<IReadOnlyList<AdminPreparedFileDeleteResult>> DeleteFilesAsync(MonitorAuthorityLease lease, IReadOnlyList<string> ids,
        bool includeInUse, CancellationToken ct = default)
    {
        var distinct = RequireArtifactIds(ids);
        var results = new List<AdminPreparedFileDeleteResult>();
        foreach (var batch in distinct.Chunk(AdminPreparedFilesContract.MaxActionIds))
        {
            var body = (await client.AdminPreparedFilesSendAsync<AdminPreparedFileDeleteResponse>(lease, HttpMethod.Post,
                "/api/v2/admin/downloads/storage/files/delete",
                new Dictionary<string, object?> { ["ids"] = batch, ["include_in_use"] = includeInUse }, ct: ct).ConfigureAwait(false)).Body;
            var expected = batch.ToHashSet(StringComparer.Ordinal);
            if (body.Results == null || body.Results.Count != expected.Count || body.Results.Any(x => x == null)
                || body.Results.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != body.Results.Count
                || body.Results.Any(x => !AdminPreparedFilesContract.IsArtifactId(x.Id) || !expected.Contains(x.Id)
                || x.Outcome is not ("deleted" or "requeued" or "in_use" or "not_found" or "not_ready" or "failed") || x.Bytes < 0))
                throw new InvalidDataException("Invalid prepared-file deletion result.");
            results.AddRange(body.Results);
        }
        return results;
    }

    public async Task<AdminStorageCleanupResult> CleanupLocationAsync(MonitorAuthorityLease lease, string location, CancellationToken ct = default)
    {
        RequireLocation(location);
        var result = (await client.AdminPreparedFilesSendAsync<AdminStorageCleanupResult>(lease, HttpMethod.Post,
            "/api/v2/admin/downloads/storage/locations/" + Uri.EscapeDataString(location) + "/cleanup", ct: ct).ConfigureAwait(false)).Body;
        if (result.FreedBytes < 0) throw new InvalidDataException("Invalid storage cleanup result.");
        return result;
    }

    public async Task<AdminUntrackedDeleteResult> DeleteUntrackedAsync(MonitorAuthorityLease lease, string location, CancellationToken ct = default)
    {
        RequireLocation(location);
        var result = (await client.AdminPreparedFilesSendAsync<AdminUntrackedDeleteResult>(lease, HttpMethod.Post,
            "/api/v2/admin/downloads/storage/locations/" + Uri.EscapeDataString(location) + "/untracked/delete", ct: ct).ConfigureAwait(false)).Body;
        if (result.Files < 0 || result.Bytes < 0 || result.FailedFiles < 0) throw new InvalidDataException("Invalid untracked deletion result.");
        return result;
    }

    public async Task<AdminDownloadRevokeResult> RevokeAsync(MonitorAuthorityLease lease, AdminDownloadRevokeTarget target,
        string reason, CancellationToken ct = default)
    {
        var trimmed = reason.Trim();
        if (trimmed.Length > 500) trimmed = trimmed[..500];
        if (target.Ids is { Count: > 0 } ids)
        {
            var total = new AdminDownloadRevokeResult { DownloadIds = [] };
            foreach (var batch in RequireIds(ids).Chunk(AdminPreparedFilesContract.MaxActionIds))
            {
                var part = await SendRevokeAsync(lease, new Dictionary<string, object?> { ["ids"] = batch, ["reason"] = trimmed }, ct).ConfigureAwait(false);
                var expected = batch.ToHashSet(StringComparer.Ordinal);
                if (part.DownloadIds.Select(x => x).Distinct(StringComparer.Ordinal).Count() != part.DownloadIds.Count
                    || part.DownloadIds.Any(x => !expected.Contains(x)))
                    throw new InvalidDataException("The revocation acknowledgement names an unrequested download.");
                total.Revoked += part.Revoked; total.Bytes += part.Bytes; total.PausedMonitors += part.PausedMonitors; total.DownloadIds.AddRange(part.DownloadIds);
            }
            return total;
        }
        if (AdminPreparedFileQuery.PositiveId(target.UserId) == null || string.IsNullOrWhiteSpace(target.ProfileId) || string.IsNullOrWhiteSpace(target.DeviceId))
            throw new ArgumentException("A complete device owner is required.", nameof(target));
        var result = await SendRevokeAsync(lease, new Dictionary<string, object?>
        {
            ["user_id"] = target.UserId, ["profile_id"] = target.ProfileId, ["device_id"] = target.DeviceId,
            ["pause_monitors"] = target.PauseMonitors, ["reason"] = trimmed,
        }, ct).ConfigureAwait(false);
        if (result.DownloadIds.Select(x => x).Distinct(StringComparer.Ordinal).Count() != result.DownloadIds.Count
            || result.DownloadIds.Any(x => !AdminPreparedFilesContract.IsId(x)))
            throw new InvalidDataException("Invalid download revocation identities.");
        return result;
    }

    public async Task<IReadOnlyList<AdminPreparedFilesNode>> ListNodesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var read = await AdminCollectionWalk.ReadAsync(lease, new AdminCollectionScope("prepared-files-nodes"),
            new AdminWalkLimits(200, 5, 1000), async (cursor, limit, token) =>
                (await client.AdminPreparedFilesSendAsync<ApiCollectionPage<AdminPreparedFilesNode>>(lease, HttpMethod.Get,
                    AdminPaging.WithPage("/api/v2/admin/nodes", limit, cursor), ct: token).ConfigureAwait(false)).Body,
            x => x.Id, ct).ConfigureAwait(false);
        if (!read.IsComplete) throw new AdminListInconsistentException(AdminListProblem.OversizedPage);
        foreach (var node in read.Items)
            if (AdminPreparedFileQuery.PositiveId(node.Id) == null || !AdminValidators.IsStrong(node.ConfigETag))
                throw new AdminListInconsistentException(AdminListProblem.MissingIdentity);
        return read.Items;
    }

    public async Task<AdminVersioned<AdminPreparedFilesNode>> UpdateNodeLocationAsync(MonitorAuthorityLease lease, AdminPreparedFilesNode node,
        string? directoryOverride, long? budgetOverride, CancellationToken ct = default)
    {
        if (AdminPreparedFileQuery.PositiveId(node.Id) == null) throw new ArgumentException("Invalid node ID.", nameof(node));
        if (!AdminValidators.IsStrong(node.ConfigETag)) throw new AdminValidatorException();
        var dir = string.IsNullOrWhiteSpace(directoryOverride) ? null : directoryOverride.Trim();
        if (dir != null && (!dir.StartsWith('/') || dir.Length > 4096)) throw new ArgumentException("Use an absolute node path.", nameof(directoryOverride));
        if (budgetOverride is < 0) throw new ArgumentOutOfRangeException(nameof(budgetOverride));
        var body = new Dictionary<string, object?>
        {
            ["download_artifact_dir_override"] = dir,
            ["download_artifact_max_bytes_override"] = budgetOverride,
        };
        var response = await client.AdminPreparedFilesSendAsync<AdminPreparedFilesNode>(lease, HttpMethod.Put,
            "/api/v2/admin/nodes/" + node.Id, body, node.ConfigETag, ct).ConfigureAwait(false);
        if (response.Body.Id != node.Id || !AdminValidators.IsStrong(response.ETag) || response.Body.ConfigETag != response.ETag)
            throw new InvalidDataException("The node write acknowledgement is inconsistent.");
        return new(response.Body, response.ETag);
    }

    private async Task<AdminDownloadRevokeResult> SendRevokeAsync(MonitorAuthorityLease lease, Dictionary<string, object?> body, CancellationToken ct)
    {
        var result = (await client.AdminPreparedFilesSendAsync<AdminDownloadRevokeResult>(lease, HttpMethod.Post,
            "/api/v2/admin/downloads/revoke", body, ct: ct).ConfigureAwait(false)).Body;
        if (result.Revoked < 0 || result.Bytes < 0 || result.PausedMonitors < 0 || result.DownloadIds == null)
            throw new InvalidDataException("Invalid download revocation result.");
        return result;
    }

    private static AdminCursorPage<T> ValidatePage<T>(ApiCollectionPage<T> body, string? sentCursor, Func<T, string?> identity, Action<T> validate)
    {
        var page = AdminPaging.ReadPage(body, AdminPreparedFilesContract.PageSize);
        if (page.NextCursor != null && page.NextCursor == sentCursor) throw new AdminListInconsistentException(AdminListProblem.RepeatedContinuation);
        ValidateDistinct(page.Items, identity, validate);
        return page;
    }

    private static void ValidateDistinct<T>(IEnumerable<T> items, Func<T, string?> identity, Action<T> validate)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item == null || !AdminPreparedFilesContract.IsId(identity(item))) throw new AdminListInconsistentException(AdminListProblem.MissingIdentity);
            if (!ids.Add(identity(item)!)) throw new AdminListInconsistentException(AdminListProblem.DuplicateIdentity);
            validate(item);
        }
    }

    private static void ValidateFile(AdminPreparedFile file)
    {
        if (!AdminPreparedFilesContract.IsArtifactId(file.Id) || file.State is not ("in_use" or "cached" or "expired") || file.Format is not ("remux" or "transcode")
            || !AdminPreparedFilesContract.IsLocation(file.Location) || file.Bytes < 0 || file.CreatedAt == default || file.LastUsedAt == default
            || file.Waiting < 0 || file.Downloading < 0 || file.Finished < 0 || file.StaleWaiting < 0)
            throw new InvalidDataException("Invalid prepared-file row.");
    }
    private static void ValidateDevice(AdminDownloadDevice device)
    {
        if (AdminPreparedFileQuery.PositiveId(device.UserId) == null || string.IsNullOrWhiteSpace(device.ProfileId)
            || string.IsNullOrWhiteSpace(device.DeviceId) || device.Copies < 0 || device.BytesOnDevice < 0
            || device.Finished < 0 || device.Waiting < 0 || device.Failed < 0 || device.Revoked < 0 || device.Monitors < 0)
            throw new InvalidDataException("Invalid device-copy row.");
    }
    private static void ValidateEntry(AdminDownloadEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.DeviceId) || entry.FileSize < 0 || entry.CreatedAt == default || entry.UpdatedAt == default)
            throw new InvalidDataException("Invalid download entry.");
    }
    private static void ValidateEvent(AdminDownloadStorageEvent item)
    {
        if (item.Count < 0 || item.Bytes < 0 || item.OccurredAt == default || item.Titles == null || item.Titles.Count > 3
            || (item.Location != "device" && !AdminPreparedFilesContract.IsLocation(item.Location)))
            throw new InvalidDataException("Invalid download history row.");
    }
    private static void ValidatePreparation(AdminDownloadPreparation item)
    {
        if (!AdminPreparedFilesContract.IsArtifactId(item.Id) || item.State is not ("running" or "queued" or "retrying" or "paused" or "failed")
            || item.Format is not ("remux" or "transcode") || item.Requesters == null || item.Source == null || item.Output == null
            || item.Source.AudioTracks == null || item.CreatedAt == default || item.Attempts < 0 || item.MaxAttempts < 0
            || item.Source.FileSize < 0 || item.Source.DurationSeconds < 0)
            throw new InvalidDataException("Invalid preparation row.");
    }
    private static void ValidateStorage(AdminDownloadStorage storage)
    {
        if (storage.Locations == null || storage.CacheHours < 0 || storage.DiskCeilingPercent is < 0 or > 100 || storage.StaleDeviceDays < 0
            || storage.DefaultBudgetBytes < 0 || storage.PreparingJobs < 0 || storage.FreedLast30DaysBytes < 0)
            throw new InvalidDataException("Invalid download storage summary.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in storage.Locations)
            if (location == null || !AdminPreparedFilesContract.IsLocation(location.Key) || !keys.Add(location.Key) || location.Kind is not ("server" or "node")
                || location.InUseBytes < 0 || location.CachedBytes < 0 || location.UntrackedBytes < 0 || location.BudgetBytes < 0
                || location.InUseFiles < 0 || location.CachedFiles < 0 || location.WaitingDownloads < 0 || location.UntrackedFiles < 0
                || location.StaleWaitingBytes < 0 || location.CleanupBacklog < 0
                || location.Usage is { } usage && (usage.Files < 0 || usage.Bytes < 0 || usage.PartialFiles < 0 || usage.PartialBytes < 0
                    || usage.OtherBytes < 0 || usage.FSUsedBytes < 0 || usage.FSTotalBytes < 0 || usage.MeasuredAt == default))
                throw new InvalidDataException("Invalid download storage location.");
    }
    private static string DeviceKey(AdminDownloadDevice device) => device.UserId + "\n" + device.ProfileId + "\n" + device.DeviceId;
    private static void ValidateCursor(string? cursor) { if (cursor != null && !AdminPaging.IsValidCursor(cursor)) throw new ArgumentException("Invalid cursor.", nameof(cursor)); }
    private static void RequireLocation(string location) { if (!AdminPreparedFilesContract.IsLocation(location)) throw new ArgumentException("Invalid storage location.", nameof(location)); }
    private static string[] RequireIds(IReadOnlyList<string> ids)
    {
        var distinct = ids.Where(AdminPreparedFilesContract.IsId).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0 || distinct.Length != ids.Count) throw new ArgumentException("Invalid or duplicate action IDs.", nameof(ids));
        return distinct;
    }
    private static string[] RequireArtifactIds(IReadOnlyList<string> ids)
    {
        var distinct = ids.Where(AdminPreparedFilesContract.IsArtifactId).Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0 || distinct.Length != ids.Count) throw new ArgumentException("Invalid or duplicate prepared-file IDs.", nameof(ids));
        return distinct;
    }
    private static void ValidateActionResults(IReadOnlyList<AdminPreparationActionResult>? results, IReadOnlyList<string> batch)
    {
        var expected = batch.ToHashSet(StringComparer.Ordinal);
        if (results == null || results.Count != expected.Count || results.Any(x => x == null)
            || results.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != results.Count
            || results.Any(x => !AdminPreparedFilesContract.IsArtifactId(x.Id) || !expected.Contains(x.Id)
            || x.Outcome is not ("applied" or "unchanged" or "not_found" or "not_applicable")))
            throw new InvalidDataException("Invalid preparation action result.");
    }
}

public sealed class AdminValidatorException() : IOException("A strong configuration validator is required. Reload before saving.");
