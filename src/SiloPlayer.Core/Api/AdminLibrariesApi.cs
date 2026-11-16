using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Manage › Libraries › Libraries (official WebUI <c>hooks/queries/admin/{libraries,scanControls,
/// libraryTranslation,trickplay}.ts</c> and <c>api/v2/libraries.ts</c> at abd85d0c). Every
/// operation is acting-admin (the routes outside <c>/admin</c> are ClassActingAdmin on the
/// server too) and takes the lease it runs under. Manage-only and separate from
/// <see cref="AdminMonitorApi"/>; nothing here reads or changes local Windows media.
/// </summary>
public sealed partial class AdminLibrariesApi(SiloApiClient client)
{
    /// <summary>The list is one unpaged response; more libraries than this is refused as truncated, never published as complete.</summary>
    public static readonly AdminWalkLimits LibraryLimits = new(pageSize: 1000, maxPages: 1, maxItems: 1000);
    public static readonly AdminCollectionScope LibraryScope = new("libraries");
    /// <summary>WebUI <c>useLibraryRefreshJobs</c> page size.</summary>
    public const int RefreshJobPageSize = 50;
    /// <summary>The server's poster limit (<c>maxPosterBytes</c>, 10 MiB).</summary>
    public const int MaxPosterBytes = 10 * 1024 * 1024;
    public static readonly IReadOnlyList<string> PosterMediaTypes = ["image/jpeg", "image/png", "image/webp"];
    public const int MaxNameLength = 512;
    public const int MaxPaths = 200;
    public const int MaxPathLength = 4096;

    private static readonly Regex MetadataLanguagePattern = new("^(?:[a-z]{2})?$", RegexOptions.CultureInvariant);

    // ── Library list and status ──────────────────────────────────────────

    /// <summary>
    /// Every library in sort order. Ids must be distinct decimals; a response
    /// that says more pages follow is truncated data (reorder stays disabled).
    /// Only a complete read carries the snapshot a whole-order write requires.
    /// </summary>
    public Task<AdminListRead<AdminLibrary>> ListAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        AdminCollectionWalk.ReadAsync(lease, LibraryScope, LibraryLimits, async (_, _, token) =>
        {
            var body = await client.AdminLibrarySendAsync<AdminLibraryCollection>(lease, HttpMethod.Get, "/api/v2/libraries", ct: token).ConfigureAwait(false);
            return new ApiCollectionPage<AdminLibrary> { Items = body.Items ?? [], Page = body.Page ?? new ApiPageInfo() };
        }, library => AdminLibraryIds.IsId(library?.Id) ? library!.Id : null, ct);

    public Task<AdminLibraryCapabilities> GetCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        client.AdminLibrarySendAsync<AdminLibraryCapabilities>(lease, HttpMethod.Get, "/api/v2/libraries/capabilities", ct: ct);

    public async Task<AdminLibraryRealtimeMonitoring> GetRealtimeMonitoringAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await client.AdminLibrarySendAsync<AdminLibraryRealtimeMonitoring>(lease, HttpMethod.Get, "/api/v2/libraries/realtime-monitoring", ct: ct).ConfigureAwait(false);
        body.Libraries ??= [];
        return body;
    }

    /// <summary>Seek-preview progress; a server without seek previews answers with an error the caller treats as "none".</summary>
    public async Task<IReadOnlyList<AdminTrickplayLibrary>> ListTrickplayAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        var body = await client.AdminLibrarySendAsync<AdminTrickplayLibraries>(lease, HttpMethod.Get, "/api/v2/admin/trickplay/libraries", ct: ct).ConfigureAwait(false);
        return body.Items ?? [];
    }

    /// <summary>One page of <c>library_refresh</c> jobs (newest first), validated like every cursor page.</summary>
    public async Task<AdminCursorPage<AdminLibraryJob>> ListRefreshJobsAsync(MonitorAuthorityLease lease, string? cursor, CancellationToken ct = default)
    {
        var path = AdminPaging.WithPage("/api/v2/admin/jobs?kind=library_refresh", RefreshJobPageSize, cursor);
        var body = await client.AdminLibrarySendAsync<AdminLibraryJobCollection>(lease, HttpMethod.Get, path, ct: ct).ConfigureAwait(false);
        return ValidatePage(body.Items, body.Page, RefreshJobPageSize, cursor, job => string.IsNullOrEmpty(job?.Id) ? null : job!.Id);
    }

    // ── Create / update / delete ─────────────────────────────────────────

    /// <summary><c>POST /api/v2/libraries</c> with only the members <c>LibraryCreate</c> declares.</summary>
    public Task<AdminLibrary> CreateAsync(MonitorAuthorityLease lease, AdminLibraryWrite body, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(body);
        Validate(body, creating: true);
        return client.AdminLibrarySendAsync<AdminLibrary>(lease, HttpMethod.Post, "/api/v2/libraries", body.ToCreateBody(), ct);
    }

    /// <summary><c>PATCH /api/v2/libraries/{id}</c>. Not guarded by a validator on the server; sent once.</summary>
    public async Task<AdminLibrary> UpdateAsync(MonitorAuthorityLease lease, string libraryId, AdminLibraryWrite body, CancellationToken ct = default)
    {
        RequireId(libraryId);
        ArgumentNullException.ThrowIfNull(body);
        Validate(body, creating: false);
        var updated = await client.AdminLibrarySendAsync<AdminLibrary>(lease, HttpMethod.Patch, "/api/v2/libraries/" + libraryId, body.ToUpdateBody(), ct).ConfigureAwait(false);
        if (!string.Equals(updated.Id, libraryId, StringComparison.Ordinal)) throw new AdminLibraryIdentityException();
        return updated;
    }

    /// <summary><c>DELETE /api/v2/libraries/{id}</c>: 202 with the deletion job. A receipt is not completion.</summary>
    public Task<AdminLibraryJob> DeleteAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
    {
        RequireId(libraryId);
        return JobAsync(lease, HttpMethod.Delete, "/api/v2/libraries/" + libraryId, null, ct);
    }

    /// <summary>
    /// <c>POST /api/v2/libraries/reorder</c>. The order is a whole-set write, so it
    /// demands a complete, still-current snapshot and an exact permutation of
    /// its ids; anything else is refused before sending.
    /// </summary>
    public Task ReorderAsync(MonitorAuthorityLease lease, AdminCompleteSnapshot<AdminLibrary> snapshot, IReadOnlyList<string> orderedIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(orderedIds);
        if (!snapshot.IsCurrentFor(lease, LibraryScope)) throw new MonitorAuthorityRetiredException();
        var known = snapshot.Items.Select(library => library.Id).ToHashSet(StringComparer.Ordinal);
        if (orderedIds.Count != known.Count || orderedIds.Distinct(StringComparer.Ordinal).Count() != known.Count || !orderedIds.All(known.Contains))
            throw new ArgumentException("The order must name every library exactly once.", nameof(orderedIds));
        var entries = orderedIds.Select((id, index) => (object?)new Dictionary<string, object?> { ["id"] = id, ["position"] = index }).ToList();
        return client.AdminLibrarySendNoContentAsync(lease, HttpMethod.Post, "/api/v2/libraries/reorder",
            new Dictionary<string, object?> { ["entries"] = entries }, ct);
    }

    // ── Row commands ─────────────────────────────────────────────────────

    public async Task<AdminLibraryMountCheck> CheckMountAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
    {
        RequireId(libraryId);
        var result = await client.AdminLibrarySendAsync<AdminLibraryMountCheck>(lease, HttpMethod.Post, "/api/v2/libraries/" + libraryId + "/check-mount", ct: ct).ConfigureAwait(false);
        if (!string.Equals(result.LibraryId, libraryId, StringComparison.Ordinal)) throw new AdminLibraryIdentityException();
        result.Roots ??= [];
        return result;
    }

    public Task<AdminEmptyRootCleanup> ConfirmEmptyRootCleanupAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
    {
        RequireId(libraryId);
        return client.AdminLibrarySendAsync<AdminEmptyRootCleanup>(lease, HttpMethod.Post, "/api/v2/libraries/" + libraryId + "/confirm-empty-root-cleanup", ct: ct);
    }

    /// <summary><c>POST /api/v2/scan</c>: 202 accepted. The scan itself arrives on the <c>scans</c> channel.</summary>
    public async Task<AdminScanStartReceipt> StartScanAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
    {
        RequireId(libraryId);
        var receipt = await client.AdminLibrarySendAsync<AdminScanStartReceipt>(lease, HttpMethod.Post, "/api/v2/scan",
            new Dictionary<string, object?> { ["library_id"] = libraryId }, ct).ConfigureAwait(false);
        if (receipt.Status != "accepted") throw new InvalidDataException("The server did not accept the scan.");
        return receipt;
    }

    /// <summary><c>POST /api/v2/scan/cancel</c>: asks the server to stop this library's queued and running scans.</summary>
    public Task<AdminScanCancelReceipt> CancelScansAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
    {
        RequireId(libraryId);
        return client.AdminLibrarySendAsync<AdminScanCancelReceipt>(lease, HttpMethod.Post, "/api/v2/scan/cancel",
            new Dictionary<string, object?> { ["library_id"] = libraryId }, ct);
    }

    /// <summary>Scan all: runs the <c>scan_libraries</c> scheduled task once.</summary>
    public Task<AdminLibraryTaskReceipt> ScanAllAsync(MonitorAuthorityLease lease, CancellationToken ct = default) =>
        client.AdminLibrarySendAsync<AdminLibraryTaskReceipt>(lease, HttpMethod.Post, "/api/v2/admin/tasks/scan_libraries/run", ct: ct);

    /// <summary><c>POST /api/v2/libraries/{id}/refresh-metadata</c> (quick or full): 202 with the refresh job.</summary>
    public Task<AdminLibraryJob> RefreshMetadataAsync(MonitorAuthorityLease lease, string libraryId, string mode, CancellationToken ct = default)
    {
        RequireId(libraryId);
        if (mode is not ("quick" or "full")) throw new ArgumentOutOfRangeException(nameof(mode));
        return JobAsync(lease, HttpMethod.Post, "/api/v2/libraries/" + libraryId + "/refresh-metadata", new Dictionary<string, object?> { ["mode"] = mode }, ct);
    }

    /// <summary><c>POST /api/v2/library-jobs/{job_id}/cancel</c>: the answer may still be running (canceling).</summary>
    public Task<AdminLibraryJob> CancelJobAsync(MonitorAuthorityLease lease, string jobId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobId) || jobId.Length > 256) throw new ArgumentException("Invalid job ID.", nameof(jobId));
        return JobAsync(lease, HttpMethod.Post, "/api/v2/library-jobs/" + Uri.EscapeDataString(jobId) + "/cancel", null, ct, expectedId: jobId);
    }

    private async Task<AdminLibraryJob> JobAsync(MonitorAuthorityLease lease, HttpMethod method, string path, object? body, CancellationToken ct, string? expectedId = null)
    {
        var job = await client.AdminLibrarySendAsync<AdminLibraryJob>(lease, method, path, body, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(job.Id) || string.IsNullOrEmpty(job.State)) throw new InvalidDataException("The server answered without a job.");
        if (expectedId != null && !string.Equals(job.Id, expectedId, StringComparison.Ordinal)) throw new InvalidDataException("The server answered with a different job.");
        return job;
    }

    // ── Provider chains ─────────────────────────────────────────────────

    public async Task<AdminProviderChains> GetProvidersAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
    {
        RequireId(libraryId);
        return Normalize(await client.AdminLibrarySendAsync<AdminProviderChains>(lease, HttpMethod.Get, "/api/v2/libraries/" + libraryId + "/providers", ct: ct).ConfigureAwait(false));
    }

    /// <summary>The chain the server would seed for a new library of this type (WebUI never re-derives defaults).</summary>
    public async Task<AdminProviderChains> GetProviderDefaultsAsync(MonitorAuthorityLease lease, string libraryType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(libraryType) || libraryType.Length > 64) throw new ArgumentException("Invalid library type.", nameof(libraryType));
        return Normalize(await client.AdminLibrarySendAsync<AdminProviderChains>(lease, HttpMethod.Get,
            "/api/v2/libraries/provider-defaults?library_type=" + Uri.EscapeDataString(libraryType), ct: ct).ConfigureAwait(false));
    }

    /// <summary>
    /// <c>PUT /api/v2/libraries/{id}/providers</c>: every edited level, remote and
    /// local entries alike, with priorities in list order (WebUI <c>buildProviderChainBody</c>).
    /// </summary>
    public Task SetProvidersAsync(MonitorAuthorityLease lease, string libraryId, IReadOnlyDictionary<string, IReadOnlyList<AdminProviderChainItem>> levels,
        CancellationToken ct = default)
    {
        RequireId(libraryId);
        ArgumentNullException.ThrowIfNull(levels);
        var body = levels.Select(level => (object?)new Dictionary<string, object?>
        {
            ["content_level"] = level.Key,
            ["entries"] = level.Value.Select((item, index) => (object?)new Dictionary<string, object?>
            {
                ["plugin_installation_id"] = item.PluginInstallationId,
                ["capability_id"] = item.CapabilityId,
                ["priority"] = index,
                ["enabled"] = item.Enabled,
            }).ToList(),
        }).ToList();
        return client.AdminLibrarySendNoContentAsync(lease, HttpMethod.Put, "/api/v2/libraries/" + libraryId + "/providers",
            new Dictionary<string, object?> { ["levels"] = body }, ct);
    }

    private static AdminProviderChains Normalize(AdminProviderChains chains)
    {
        chains.Levels ??= [];
        foreach (var level in chains.Levels) level.Entries ??= [];
        return chains;
    }

    // ── Poster ────────────────────────────────────────────────────────────

    /// <summary>Uploads a JPEG, PNG or WebP poster of at most 10 MiB and answers the library with its new poster URL.</summary>
    public async Task<AdminLibrary> UploadPosterAsync(MonitorAuthorityLease lease, string libraryId, byte[] poster, string mediaType, CancellationToken ct = default)
    {
        RequireId(libraryId);
        ArgumentNullException.ThrowIfNull(poster);
        if (poster.Length == 0 || poster.Length > MaxPosterBytes) throw new AdminLibraryPosterException(poster.Length == 0);
        if (!PosterMediaTypes.Contains(mediaType)) throw new ArgumentException("Unsupported poster type.", nameof(mediaType));
        var extension = mediaType switch { "image/png" => "png", "image/webp" => "webp", _ => "jpg" };
        var library = await client.AdminLibraryPutPosterAsync<AdminLibrary>(lease, "/api/v2/libraries/" + libraryId + "/poster", poster, mediaType,
            "poster." + extension, ct).ConfigureAwait(false);
        if (!string.Equals(library.Id, libraryId, StringComparison.Ordinal)) throw new AdminLibraryIdentityException();
        return library;
    }

    public Task DeletePosterAsync(MonitorAuthorityLease lease, string libraryId, CancellationToken ct = default)
    {
        RequireId(libraryId);
        return client.AdminLibrarySendNoContentAsync(lease, HttpMethod.Delete, "/api/v2/libraries/" + libraryId + "/poster", null, ct);
    }

    // ── Validation helpers ────────────────────────────────────────────────

    private static void Validate(AdminLibraryWrite body, bool creating)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > MaxNameLength) throw new ArgumentException("A library name is required.", nameof(body));
        if (body.Paths.Count == 0 || body.Paths.Count > MaxPaths || body.Paths.Any(path => string.IsNullOrWhiteSpace(path) || path.Length > MaxPathLength))
            throw new ArgumentException("At least one folder path is required.", nameof(body));
        if (string.IsNullOrWhiteSpace(body.Type) || body.Type.Length > 64) throw new ArgumentException("A library type is required.", nameof(body));
        if (body.MetadataLanguage != null && !MetadataLanguagePattern.IsMatch(body.MetadataLanguage)) throw new ArgumentException("Invalid metadata language.", nameof(body));
        if (body.BoxSetMinMovies is < 2 or > 10) throw new ArgumentException("Box set size must be 2 to 10.", nameof(body));
        _ = creating;
    }

    internal static void RequireId(string? id)
    {
        if (!AdminLibraryIds.IsId(id)) throw new ArgumentException("Invalid library ID.", nameof(id));
    }

    /// <summary>
    /// Validates one cursor page like the WebUI expects: no more rows than
    /// requested, a fresh non-repeated cursor (and at least one row) with
    /// <c>has_more</c>, no duplicate identities. A missing page block means the end.
    /// </summary>
    internal static AdminCursorPage<T> ValidatePage<T>(List<T>? items, ApiPageInfo? page, int limit, string? requestCursor, Func<T, string?> identity)
    {
        items ??= [];
        if (items.Count > limit) throw new AdminListInconsistentException(AdminListProblem.OversizedPage);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = item == null ? null : identity(item);
            if (string.IsNullOrEmpty(id)) throw new AdminListInconsistentException(AdminListProblem.MissingIdentity);
            if (!ids.Add(id)) throw new AdminListInconsistentException(AdminListProblem.DuplicateIdentity);
        }
        if (page?.HasMore != true) return new AdminCursorPage<T>(items, null);
        if (items.Count == 0 || !AdminPaging.IsValidCursor(page.NextCursor)) throw new AdminListInconsistentException(AdminListProblem.MissingContinuation);
        if (string.Equals(page.NextCursor, requestCursor, StringComparison.Ordinal)) throw new AdminListInconsistentException(AdminListProblem.RepeatedContinuation);
        return new AdminCursorPage<T>(items, page.NextCursor);
    }

    internal static string Query(string path, params (string Name, string? Value)[] values)
    {
        var builder = new StringBuilder(path);
        var first = !path.Contains('?');
        foreach (var (name, value) in values)
        {
            if (value == null) continue;
            builder.Append(first ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
            first = false;
        }
        return builder.ToString();
    }

    internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Library identifiers: opaque positive decimals without leading zeros.</summary>
public static class AdminLibraryIds
{
    public static bool IsId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 19 && id[0] is >= '1' and <= '9' && id.All(c => c is >= '0' and <= '9');
}

/// <summary>One provider in an edited level (WebUI <c>LevelChainItem</c>).</summary>
public sealed record AdminProviderChainItem(string PluginInstallationId, string CapabilityId, string ProviderSlug, bool Enabled, bool Local);

/// <summary>
/// The library editor's body (WebUI <c>CreateLibraryRequest</c>). Create and
/// update bodies differ: create omits <c>enabled</c> and
/// <c>auto_translate_metadata</c>, which <c>LibraryCreate</c> does not declare
/// (v2 rejects unknown members). Optional members stay absent when null; they
/// are never sent as JSON null.
/// </summary>
public sealed record AdminLibraryWrite
{
    public string Name { get; init; } = "";
    public IReadOnlyList<string> Paths { get; init; } = [];
    public string Type { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public string? MetadataLanguage { get; init; }
    public bool AutoTranslateMetadata { get; init; }
    public bool ChapterThumbnailsEnabled { get; init; }
    public bool IntroDetectionEnabled { get; init; }
    public IReadOnlyList<string> TrailerKinds { get; init; } = [];
    public bool RealtimeMonitoring { get; init; } = true;
    /// <summary>Sent only when it changes (servers without seek previews reject the member).</summary>
    public bool? TrickplayEnabled { get; init; }
    public bool? BoxSetsEnabled { get; init; }
    public int? BoxSetMinMovies { get; init; }
    public bool? BoxSetsInSearch { get; init; }

    internal Dictionary<string, object?> ToCreateBody()
    {
        var body = new Dictionary<string, object?>
        {
            ["paths"] = Paths.ToList(), ["type"] = Type, ["name"] = Name,
        };
        if (MetadataLanguage != null) body["metadata_language"] = MetadataLanguage;
        body["chapter_thumbnails_enabled"] = ChapterThumbnailsEnabled;
        body["intro_detection_enabled"] = IntroDetectionEnabled;
        AddOptional(body);
        body["trailer_kinds"] = TrailerKinds.ToList();
        body["realtime_monitoring"] = RealtimeMonitoring;
        return body;
    }

    internal Dictionary<string, object?> ToUpdateBody()
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = Name, ["paths"] = Paths.ToList(), ["type"] = Type, ["enabled"] = Enabled,
        };
        if (MetadataLanguage != null) body["metadata_language"] = MetadataLanguage;
        body["auto_translate_metadata"] = AutoTranslateMetadata;
        body["chapter_thumbnails_enabled"] = ChapterThumbnailsEnabled;
        body["intro_detection_enabled"] = IntroDetectionEnabled;
        body["trailer_kinds"] = TrailerKinds.ToList();
        body["realtime_monitoring"] = RealtimeMonitoring;
        AddOptional(body);
        return body;
    }

    private void AddOptional(Dictionary<string, object?> body)
    {
        if (TrickplayEnabled is { } trickplay) body["trickplay_enabled"] = trickplay;
        if (BoxSetsEnabled is { } boxSets) body["box_sets_enabled"] = boxSets;
        if (BoxSetMinMovies is { } minimum) body["box_set_min_movies"] = minimum;
        if (BoxSetsInSearch is { } inSearch) body["box_sets_in_search"] = inSearch;
    }
}

/// <summary>A response named a different library than the one acted on.</summary>
public sealed class AdminLibraryIdentityException() : IOException("Library identity changed. Reload the list.");

/// <summary>The chosen poster is empty or larger than the server accepts.</summary>
public sealed class AdminLibraryPosterException(bool empty) : IOException(empty ? "The poster file is empty." : "The poster exceeds the 10 MiB limit.")
{
    public bool Empty { get; } = empty;
}
