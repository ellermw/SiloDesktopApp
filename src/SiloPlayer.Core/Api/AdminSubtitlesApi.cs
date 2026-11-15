using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Libraries › Subtitle Files (official WebUI <c>api/v2/adminSubtitles.ts</c>,
/// <c>adminSubtitleMetadata.ts</c>, <c>adminSubtitleBytes.ts</c>,
/// <c>adminSubtitleDelete.ts</c> at fd0d8c6c). All operations are
/// acting-admin; every call takes the lease it runs under. Manage-only and
/// deliberately separate from <see cref="AdminMonitorApi"/>. Subtitle content
/// and timing are immutable here; provider configuration belongs to Configure.
/// </summary>
public sealed class AdminSubtitlesApi(SiloApiClient client)
{
    /// <summary>Most bytes a stored subtitle download may hold before it is refused (uploads are capped far below this).</summary>
    public const int MaxDownloadBytes = 16 * 1024 * 1024;

    /// <summary>Uploader picker bounds: 200 accounts per page, at most 5 pages / 1,000 accounts retained.</summary>
    public static readonly AdminWalkLimits UploaderLimits = new(pageSize: 200, maxPages: 5, maxItems: 1000);

    private static readonly AdminCollectionScope UploaderScope = new("admin-users");

    /// <summary>
    /// One page of stored subtitles. The response is validated like the WebUI:
    /// a page envelope, <c>has_more</c> with a fresh non-repeated cursor and at
    /// least one row, no cursor at the end, decimal row ids, no duplicates and
    /// no more rows than requested. Anything else is an invalid list.
    /// </summary>
    public async Task<AdminStoredSubtitleCollection> ListAsync(MonitorAuthorityLease lease, AdminSubtitleListQuery query, string? cursor = null,
        CancellationToken ct = default)
    {
        var normalized = query.Normalize();
        if (cursor != null && !AdminPaging.IsValidCursor(cursor)) throw new ArgumentException("Invalid continuation cursor.", nameof(cursor));
        var response = await client.AdminSubtitleSendAsync<AdminStoredSubtitleCollection>(lease, HttpMethod.Get,
            "/api/v2/admin/subtitles" + normalized.ToQueryString(cursor), ct: ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        var body = response.Body;
        Validate(body, normalized.PageSize, cursor);
        return body;
    }

    internal static void Validate(AdminStoredSubtitleCollection body, int limit, string? cursor)
    {
        var page = body.Page ?? throw new AdminListInconsistentException(AdminListProblem.InvalidPage);
        body.Items ??= [];
        if (body.Items.Count > limit) throw new AdminListInconsistentException(AdminListProblem.OversizedPage);
        if (page.HasMore)
        {
            if (body.Items.Count == 0 || !AdminPaging.IsValidCursor(page.NextCursor)) throw new AdminListInconsistentException(AdminListProblem.MissingContinuation);
            if (string.Equals(page.NextCursor, cursor, StringComparison.Ordinal)) throw new AdminListInconsistentException(AdminListProblem.RepeatedContinuation);
        }
        else if (!string.IsNullOrEmpty(page.NextCursor)) throw new AdminListInconsistentException(AdminListProblem.InvalidPage);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in body.Items)
        {
            if (item == null || !AdminSubtitleIds.IsId(item.Id) || !AdminSubtitleIds.IsId(item.MediaFileId))
                throw new AdminListInconsistentException(AdminListProblem.MissingIdentity);
            if (!ids.Add(item.Id)) throw new AdminListInconsistentException(AdminListProblem.DuplicateIdentity);
        }
        if (body.Total < 0 || body.Uploads < 0 || body.ProviderDownloads < 0) throw new AdminListInconsistentException(AdminListProblem.InvalidPage);
    }

    /// <summary>
    /// The current metadata and its strong edit validator. The record must be
    /// the same subtitle on the same media file as the row it was opened from.
    /// </summary>
    public async Task<AdminVersioned<AdminSubtitleMetadata>> GetMetadataAsync(MonitorAuthorityLease lease, AdminStoredSubtitle row, CancellationToken ct = default)
    {
        RequireRowId(row);
        var response = await client.AdminSubtitleSendAsync<AdminSubtitleMetadata>(lease, HttpMethod.Get, "/api/v2/admin/subtitles/" + row.Id, ct: ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        RequireIdentity(response.Body, row);
        if (!AdminValidators.IsStrong(response.ETag)) throw new AdminSubtitleValidatorException();
        return new(response.Body, response.ETag);
    }

    /// <summary>
    /// Merges the changed fields with <c>If-Match</c> set to the exact
    /// validator that was read. A 412 surfaces as a failure for explicit
    /// review; it is never rebased or replayed.
    /// </summary>
    public async Task<AdminVersioned<AdminSubtitleMetadata>> UpdateMetadataAsync(MonitorAuthorityLease lease, AdminStoredSubtitle row,
        AdminVersioned<AdminSubtitleMetadata> current, AdminSubtitlePatch patch, CancellationToken ct = default)
    {
        RequireRowId(row);
        RequireIdentity(current.Value, row);
        if (patch.IsEmpty) throw new ArgumentException("Supply at least one metadata field.", nameof(patch));
        if (patch.Language != null && !AdminSubtitleIds.IsPatchLanguage(patch.Language)) throw new ArgumentException("Invalid subtitle language.", nameof(patch));
        if (patch.ReleaseName is { Length: > AdminSubtitlePatch.MaxReleaseNameLength }) throw new ArgumentException("Release name is too long.", nameof(patch));
        if (!current.CanWrite) throw new AdminSubtitleValidatorException();
        var response = await client.AdminSubtitleSendAsync<AdminSubtitleMetadata>(lease, HttpMethod.Patch, "/api/v2/admin/subtitles/" + row.Id,
            patch.ToBody(), current.ETag, ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        RequireIdentity(response.Body, row);
        if (!AdminValidators.IsStrong(response.ETag)) throw new AdminSubtitleValidatorException();
        return new(response.Body, response.ETag);
    }

    /// <summary>Deletes the reviewed record with its exact validator; 204 is the only success.</summary>
    public async Task DeleteAsync(MonitorAuthorityLease lease, AdminStoredSubtitle row, AdminVersioned<AdminSubtitleMetadata> reviewed, CancellationToken ct = default)
    {
        RequireRowId(row);
        RequireIdentity(reviewed.Value, row);
        if (!reviewed.CanWrite) throw new AdminSubtitleValidatorException();
        await client.AdminSubtitleDeleteAsync(lease, "/api/v2/admin/subtitles/" + row.Id, reviewed.ETag!, ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
    }

    /// <summary>The complete stored attachment, bounded to <see cref="MaxDownloadBytes"/>, held in memory for a checked publish.</summary>
    public async Task<byte[]> DownloadAsync(MonitorAuthorityLease lease, AdminStoredSubtitle row, CancellationToken ct = default)
    {
        RequireRowId(row);
        var bytes = await client.AdminSubtitleDownloadAsync(lease, "/api/v2/admin/subtitles/" + row.Id + "/download", MaxDownloadBytes, ct).ConfigureAwait(false);
        lease.ThrowIfRetired();
        return bytes;
    }

    /// <summary>
    /// Uploader choices: a bounded walk of admin accounts (never the WebUI's
    /// unbounded full walk). A truncated read is shown as such, never cached
    /// as complete, and never used for anything but this filter.
    /// </summary>
    public Task<AdminListRead<AdminSubtitleUploader>> ListUploadersAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        AdminCollectionWalk.ReadAsync(lease, UploaderScope, UploaderLimits, async (cursor, limit, token) =>
            (await client.AdminSubtitleSendAsync<ApiCollectionPage<AdminSubtitleUploader>>(lease, HttpMethod.Get,
                AdminPaging.WithPage("/api/v2/admin/users", limit, cursor), ct: token).ConfigureAwait(false)).Body,
            user => AdminSubtitleIds.IsId(user.Id) ? user.Id : null, ct);

    private static void RequireRowId(AdminStoredSubtitle row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!AdminSubtitleIds.IsId(row.Id) || !AdminSubtitleIds.IsId(row.MediaFileId)) throw new ArgumentException("Invalid subtitle ID.", nameof(row));
    }

    private static void RequireIdentity(AdminSubtitleMetadata body, AdminStoredSubtitle row)
    {
        if (!string.Equals(body.Id, row.Id, StringComparison.Ordinal) || !string.Equals(body.MediaFileId, row.MediaFileId, StringComparison.Ordinal))
            throw new AdminSubtitleIdentityException();
    }
}

/// <summary>The response named a different subtitle or media file than the row (WebUI <c>subtitleIdentityChanged</c>).</summary>
public sealed class AdminSubtitleIdentityException() : IOException("Subtitle identity changed. Reload the list.");

/// <summary>No strong validator came with the record (WebUI <c>subtitleValidatorRequired</c>); writes stay disabled.</summary>
public sealed class AdminSubtitleValidatorException() : IOException("A strong subtitle edit validator is required. Reopen the editor.");
