using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>
/// The diagnostics panels below the library list, the server directory
/// browser and the library translation prewarm. Every listing is one bounded,
/// validated cursor page; the panels page on demand and never walk a whole table.
/// </summary>
public sealed partial class AdminLibrariesApi
{
    /// <summary>WebUI <c>UNMATCHED_PAGE_SIZE</c>.</summary>
    public const int UnmatchedPageSize = 10;
    /// <summary>WebUI <c>METADATA_MATCH_QUEUE_PAGE_SIZE</c> (server maximum 50).</summary>
    public const int MatchQueuePageSize = 10;
    /// <summary>WebUI <c>LIBRARY_ROOTS_PAGE_LIMIT</c>, <c>STALE_MEDIA_IDS_PAGE_LIMIT</c> and the skipped-roots page.</summary>
    public const int DiagnosticsPageSize = 50;
    /// <summary>WebUI folder browser page size.</summary>
    public const int BrowsePageSize = 50;
    public const int MaxSearchLength = 512;
    public const int MaxContentIdLength = 256;

    private static readonly Regex TranslationLanguagePattern = new(@"^(?:[A-Za-z]{2,3}(?:[-_][0-9A-Za-z]{1,8})*|[XxIi](?:[-_][0-9A-Za-z]{1,8})+)$", RegexOptions.CultureInvariant);

    // ── Unmatched items ─────────────────────────────────────────────────

    public async Task<AdminTotalCursorPage<AdminUnmatchedItem>> ListUnmatchedAsync(MonitorAuthorityLease lease, string? search, bool ignored, string? cursor,
        CancellationToken ct = default)
    {
        var path = Query("/api/v2/libraries/unmatched-items", ("limit", Number(UnmatchedPageSize)), ("q", Search(search)),
            ("ignored", ignored ? "true" : null), ("cursor", Cursor(cursor)));
        var body = await client.AdminLibrarySendAsync<AdminTotalPage<AdminUnmatchedItem>>(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        return Total(body, UnmatchedPageSize, cursor, item => string.IsNullOrEmpty(item.ContentId) ? null : item.ContentId);
    }

    /// <summary>Ignores (PUT) or restores (DELETE) an unmatched movie or series; 204 is the only success.</summary>
    public Task SetUnmatchedIgnoredAsync(MonitorAuthorityLease lease, string contentId, bool ignored, CancellationToken ct = default)
    {
        RequireContentId(contentId);
        return client.AdminLibrarySendNoContentAsync(lease, ignored ? HttpMethod.Put : HttpMethod.Delete,
            "/api/v2/libraries/unmatched-items/" + Uri.EscapeDataString(contentId) + "/ignore", null, ct);
    }

    // ── Matcher queues ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<AdminMatchQueueStatus>> ListMatchQueuesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await client.AdminLibrarySendAsync<AdminMatchQueueStatusCollection>(lease, HttpMethod.Get, "/api/v2/libraries/metadata-match-queue", ct: ct).ConfigureAwait(false);
        var items = body.Items ?? [];
        if (items.Count > LibraryLimits.MaxItems) throw new AdminListInconsistentException(AdminListProblem.OversizedPage);
        if (items.Any(item => !AdminLibraryIds.IsId(item?.LibraryId))) throw new AdminListInconsistentException(AdminListProblem.MissingIdentity);
        if (items.Select(item => item.LibraryId).Distinct(StringComparer.Ordinal).Count() != items.Count) throw new AdminListInconsistentException(AdminListProblem.DuplicateIdentity);
        return items;
    }

    public async Task<AdminMatchQueuePage> GetMatchQueueDetailAsync(MonitorAuthorityLease lease, string libraryId, string? cursor, CancellationToken ct = default)
    {
        RequireId(libraryId);
        var path = Query("/api/v2/libraries/" + libraryId + "/metadata-match-queue", ("limit", Number(MatchQueuePageSize)), ("cursor", Cursor(cursor)));
        var body = await client.AdminLibrarySendAsync<AdminMatchQueueDetail>(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        if (!string.Equals(body.LibraryId, libraryId, StringComparison.Ordinal)) throw new AdminLibraryIdentityException();
        body.Movies ??= []; body.Series ??= []; body.RawFiles ??= [];
        // The limit applies per list; each list is bounded by it.
        if (body.Movies.Count > MatchQueuePageSize || body.Series.Count > MatchQueuePageSize || body.RawFiles.Count > MatchQueuePageSize)
            throw new AdminListInconsistentException(AdminListProblem.OversizedPage);
        var rows = body.Movies.Count + body.Series.Count + body.RawFiles.Count;
        string? next = null;
        if (body.Page?.HasMore == true)
        {
            if (rows == 0 || !AdminPaging.IsValidCursor(body.Page.NextCursor)) throw new AdminListInconsistentException(AdminListProblem.MissingContinuation);
            if (string.Equals(body.Page.NextCursor, cursor, StringComparison.Ordinal)) throw new AdminListInconsistentException(AdminListProblem.RepeatedContinuation);
            next = body.Page.NextCursor;
        }
        return new AdminMatchQueuePage(body, next);
    }

    public Task<AdminMatchQueueAction> RetryMatchQueueAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
    {
        RequireId(libraryId);
        return client.AdminLibrarySendAsync<AdminMatchQueueAction>(lease, HttpMethod.Post, "/api/v2/libraries/" + libraryId + "/metadata-match-queue/retry", ct: ct);
    }

    // ── Roots ───────────────────────────────────────────────────────────

    public async Task<AdminTotalCursorPage<AdminLibraryRoot>> ListRootsAsync(MonitorAuthorityLease lease, string libraryId, string? state, string? search,
        string? cursor, CancellationToken ct = default)
    {
        RequireId(libraryId);
        var path = Query("/api/v2/libraries/roots", ("library_id", libraryId), ("limit", Number(DiagnosticsPageSize)), ("state", state),
            ("q", Search(search)), ("cursor", Cursor(cursor)));
        var body = await client.AdminLibrarySendAsync<AdminTotalPage<AdminLibraryRoot>>(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        var page = Total(body, DiagnosticsPageSize, cursor, root => string.IsNullOrEmpty(root.RootPath) ? null : root.LibraryId + "\n" + root.RootPath);
        if (page.Items.Any(root => !string.Equals(root.LibraryId, libraryId, StringComparison.Ordinal))) throw new AdminLibraryIdentityException();
        return page;
    }

    /// <summary><c>PUT /api/v2/libraries/roots/override</c> with only the forced values that are set.</summary>
    public Task SetRootOverrideAsync(MonitorAuthorityLease lease, AdminRootOverrideWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        RequireId(write.LibraryId);
        if (string.IsNullOrEmpty(write.RootPath) || write.RootPath.Length > MaxPathLength) throw new ArgumentException("Invalid root path.", nameof(write));
        if (write.ForcedType is not (null or "movie" or "series")) throw new ArgumentException("Invalid forced type.", nameof(write));
        var body = new Dictionary<string, object?> { ["library_id"] = write.LibraryId, ["root_path"] = write.RootPath };
        void Add(string name, string? value) { if (!string.IsNullOrEmpty(value)) body[name] = value; }
        Add("forced_type", write.ForcedType);
        Add("forced_title", write.ForcedTitle);
        if (write.ForcedYear is { } year) body["forced_year"] = year;
        Add("forced_tmdb_id", write.ForcedTmdbId);
        Add("forced_imdb_id", write.ForcedImdbId);
        Add("forced_tvdb_id", write.ForcedTvdbId);
        Add("note", write.Note);
        return client.AdminLibrarySendNoContentAsync(lease, HttpMethod.Put, "/api/v2/libraries/roots/override", body, ct);
    }

    public Task DeleteRootOverrideAsync(MonitorAuthorityLease lease, string libraryId, string rootPath, CancellationToken ct = default)
    {
        RequireId(libraryId);
        if (string.IsNullOrEmpty(rootPath) || rootPath.Length > MaxPathLength) throw new ArgumentException("Invalid root path.", nameof(rootPath));
        return client.AdminLibrarySendNoContentAsync(lease, HttpMethod.Delete,
            Query("/api/v2/libraries/roots/override", ("library_id", libraryId), ("root_path", rootPath)), null, ct);
    }

    public async Task<AdminTotalCursorPage<AdminSkippedRoot>> ListSkippedRootsAsync(MonitorAuthorityLease lease, string? search, string? cursor,
        CancellationToken ct = default)
    {
        var path = Query("/api/v2/libraries/skipped-roots", ("limit", Number(DiagnosticsPageSize)), ("q", Search(search)), ("cursor", Cursor(cursor)));
        var body = await client.AdminLibrarySendAsync<AdminTotalPage<AdminSkippedRoot>>(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        return Total(body, DiagnosticsPageSize, cursor, root => string.IsNullOrEmpty(root.RootPath) ? null : root.LibraryId + "\n" + root.RootPath);
    }

    public async Task<AdminTotalCursorPage<AdminStaleMediaId>> ListStaleIdsAsync(MonitorAuthorityLease lease, string? search, string? cursor,
        CancellationToken ct = default)
    {
        var path = Query("/api/v2/libraries/stale-ids", ("limit", Number(DiagnosticsPageSize)), ("q", Search(search)), ("cursor", Cursor(cursor)));
        var body = await client.AdminLibrarySendAsync<AdminTotalPage<AdminStaleMediaId>>(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        return Total(body, DiagnosticsPageSize, cursor, row => string.IsNullOrEmpty(row.ContentId) ? null : row.ContentId + "\n" + row.Provider);
    }

    // ── Server directory browsing ───────────────────────────────────────

    /// <summary>One page of a server directory, read-only (<c>GET /api/v2/admin/filesystem/browse</c>).</summary>
    public async Task<AdminBrowsePage> BrowseAsync(MonitorAuthorityLease lease, string path, string? namePrefix, string? cursor, int limit = BrowsePageSize,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxPathLength) throw new ArgumentException("Invalid folder path.", nameof(path));
        var size = AdminPaging.ClampLimit(limit, 200);
        var request = Query("/api/v2/admin/filesystem/browse", ("path", path), ("name_prefix", string.IsNullOrEmpty(namePrefix) ? null : namePrefix),
            ("limit", Number(size)), ("cursor", Cursor(cursor)));
        var body = await client.AdminLibrarySendAsync<AdminFilesystemPage>(lease, HttpMethod.Get, request, ct: ct).ConfigureAwait(false);
        var page = ValidatePage(body.Items, body.Page, size, cursor, entry => string.IsNullOrEmpty(entry.Path) ? null : entry.Path);
        return new AdminBrowsePage(body.Path ?? "", body.Parent ?? "", page.Items, page.NextCursor);
    }

    // ── Translation prewarm ─────────────────────────────────────────────

    public async Task<AdminMetadataAiCapability> GetMetadataAiCapabilityAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        await client.AdminLibrarySendAsync<AdminMetadataAiCapability>(lease, HttpMethod.Get, "/api/v2/capabilities/metadata-ai", ct: ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<AdminTranslationJob>> ListTranslationJobsAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
    {
        RequireId(libraryId);
        var body = await client.AdminLibrarySendAsync<AdminTranslationJobs>(lease, HttpMethod.Get,
            "/api/v2/admin/libraries/" + libraryId + "/metadata-translation/jobs", ct: ct).ConfigureAwait(false);
        var jobs = body.Jobs ?? [];
        if (jobs.Count > 200) throw new AdminListInconsistentException(AdminListProblem.OversizedPage);
        if (jobs.Any(job => string.IsNullOrEmpty(job?.Id))) throw new AdminListInconsistentException(AdminListProblem.MissingIdentity);
        return jobs;
    }

    /// <summary>Starts (or joins) the prewarm for one language: 202 with the job, never completion.</summary>
    public async Task<AdminTranslationJob> StartTranslationAsync(MonitorAuthorityLease lease, string libraryId, string targetLanguage, CancellationToken ct = default)
    {
        RequireId(libraryId);
        if (string.IsNullOrEmpty(targetLanguage) || targetLanguage.Length > 16 || !TranslationLanguagePattern.IsMatch(targetLanguage))
            throw new ArgumentException("Invalid target language.", nameof(targetLanguage));
        var job = await client.AdminLibrarySendAsync<AdminTranslationJob>(lease, HttpMethod.Post, "/api/v2/admin/libraries/" + libraryId + "/metadata-translation",
            new Dictionary<string, object?> { ["target_language"] = targetLanguage }, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(job.Id)) throw new InvalidDataException("The server answered without a translation job.");
        return job;
    }

    public Task CancelTranslationAsync(MonitorAuthorityLease lease, string libraryId, string jobId, CancellationToken ct = default)
    {
        RequireId(libraryId);
        if (string.IsNullOrWhiteSpace(jobId) || jobId.Length > 256) throw new ArgumentException("Invalid job ID.", nameof(jobId));
        return client.AdminLibrarySendNoContentAsync(lease, HttpMethod.Post,
            "/api/v2/admin/libraries/" + libraryId + "/metadata-translation/jobs/" + Uri.EscapeDataString(jobId) + "/cancel", null, ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static AdminTotalCursorPage<T> Total<T>(AdminTotalPage<T> body, int limit, string? cursor, Func<T, string?> identity)
    {
        if (body.Total < 0) throw new AdminListInconsistentException(AdminListProblem.InvalidPage);
        var page = ValidatePage(body.Items, body.Page, limit, cursor, identity);
        return new AdminTotalCursorPage<T>(page.Items, page.NextCursor, body.Total);
    }

    /// <summary>Trimmed search text, omitted when blank (WebUI sends <c>q</c> only when set).</summary>
    internal static string? Search(string? search)
    {
        var trimmed = search?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > MaxSearchLength ? trimmed[..MaxSearchLength] : trimmed;
    }

    private static string? Cursor(string? cursor)
    {
        if (cursor == null) return null;
        if (!AdminPaging.IsValidCursor(cursor)) throw new ArgumentException("Invalid continuation cursor.", nameof(cursor));
        return cursor;
    }

    private static void RequireContentId(string? contentId)
    {
        if (string.IsNullOrWhiteSpace(contentId) || contentId.Length > MaxContentIdLength) throw new ArgumentException("Invalid content ID.", nameof(contentId));
    }
}

/// <summary>One validated cursor page with the filtered total for the panel header.</summary>
public sealed record AdminTotalCursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, long Total)
{
    public bool HasMore => NextCursor != null;
}

/// <summary>One validated matcher-queue page.</summary>
public sealed record AdminMatchQueuePage(AdminMatchQueueDetail Detail, string? NextCursor);

/// <summary>One validated server-directory page.</summary>
public sealed record AdminBrowsePage(string Path, string Parent, IReadOnlyList<AdminFilesystemEntry> Entries, string? NextCursor);

/// <summary>The root-override dialog's write (WebUI <c>UpsertLibraryRootOverrideRequest</c>).</summary>
public sealed record AdminRootOverrideWrite(string LibraryId, string RootPath)
{
    public string? ForcedType { get; init; }
    public string? ForcedTitle { get; init; }
    public int? ForcedYear { get; init; }
    public string? ForcedTmdbId { get; init; }
    public string? ForcedImdbId { get; init; }
    public string? ForcedTvdbId { get; init; }
    public string? Note { get; init; }
}
