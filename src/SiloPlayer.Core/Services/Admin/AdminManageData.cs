using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Bounded admin list contracts. A server page envelope is not a bound on
/// rows, bytes or pages: every Manage read states its own page size, page cap
/// and retained-row cap, validates the server's continuation, and never treats
/// a truncated list as complete.
/// </summary>
public static class AdminPaging
{
    /// <summary>OpenAPI <c>cursor maxLength</c> for every cursor-paged admin list.</summary>
    public const int MaxCursorLength = 8192;

    /// <summary>Clamps a requested page size into an operation's declared bounds.</summary>
    public static int ClampLimit(int requested, int maximum, int minimum = 1)
    {
        if (minimum < 1 || maximum < minimum) throw new ArgumentOutOfRangeException(nameof(maximum));
        return Math.Clamp(requested, minimum, maximum);
    }

    /// <summary>Appends <c>limit</c> and an escaped <c>cursor</c> to a path that may already carry a query.</summary>
    public static string WithPage(string path, int limit, string? cursor)
    {
        if (limit < 1) throw new ArgumentOutOfRangeException(nameof(limit));
        if (cursor != null && !IsValidCursor(cursor)) throw new ArgumentException("Invalid continuation cursor.", nameof(cursor));
        var builder = new StringBuilder(path).Append(path.Contains('?') ? '&' : '?').Append("limit=").Append(limit);
        if (cursor != null) builder.Append("&cursor=").Append(Uri.EscapeDataString(cursor));
        return builder.ToString();
    }

    public static bool IsValidCursor(string? cursor) => !string.IsNullOrEmpty(cursor) && cursor.Length <= MaxCursorLength;

    /// <summary>
    /// Validates one page: an envelope and page info must exist, the server may
    /// not return more rows than asked for, and <c>has_more</c> needs a usable
    /// cursor. A cursor sent with <c>has_more=false</c> is ignored.
    /// </summary>
    public static AdminCursorPage<T> ReadPage<T>(ApiCollectionPage<T>? envelope, int requestedLimit)
    {
        if (envelope?.Page == null) throw new AdminListInconsistentException(AdminListProblem.InvalidPage);
        var items = envelope.Items ?? [];
        if (items.Count > requestedLimit) throw new AdminListInconsistentException(AdminListProblem.OversizedPage);
        if (!envelope.Page.HasMore) return new AdminCursorPage<T>(items, null);
        if (!IsValidCursor(envelope.Page.NextCursor)) throw new AdminListInconsistentException(AdminListProblem.MissingContinuation);
        return new AdminCursorPage<T>(items, envelope.Page.NextCursor);
    }
}

/// <summary>One validated page; <see cref="NextCursor"/> is null at the end.</summary>
public sealed record AdminCursorPage<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    public bool HasMore => NextCursor != null;
}

public enum AdminListProblem { InvalidPage, OversizedPage, MissingContinuation, RepeatedContinuation, MissingIdentity, DuplicateIdentity }

/// <summary>The server's list answer was inconsistent; nothing read in this walk may be published.</summary>
public sealed class AdminListInconsistentException(AdminListProblem problem)
    : Exception($"The admin list response was inconsistent ({problem}). Reload and try again.")
{
    public AdminListProblem Problem { get; } = problem;
}

/// <summary>Explicit walk bounds: page size sent, most pages requested, most rows retained.</summary>
public sealed record AdminWalkLimits
{
    public AdminWalkLimits(int pageSize, int maxPages, int maxItems)
    {
        if (pageSize < 1 || maxPages < 1 || maxItems < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        PageSize = pageSize; MaxPages = maxPages; MaxItems = maxItems;
    }

    public int PageSize { get; }
    public int MaxPages { get; }
    public int MaxItems { get; }
}

public enum AdminListCompleteness { Complete, TruncatedByPages, TruncatedByItems }

/// <summary>
/// A bounded list read. A truncated read is truthful partial data for display
/// or a picker that says to narrow the search; only <see cref="Snapshot"/>
/// (present when complete) may feed a whole-set or whole-order write.
/// </summary>
public sealed class AdminListRead<T>
{
    internal AdminListRead(IReadOnlyList<T> items, AdminListCompleteness completeness, AdminCompleteSnapshot<T>? snapshot)
    {
        Items = items; Completeness = completeness; Snapshot = snapshot;
    }

    public IReadOnlyList<T> Items { get; }
    public AdminListCompleteness Completeness { get; }
    public bool IsComplete => Completeness == AdminListCompleteness.Complete;
    public AdminCompleteSnapshot<T>? Snapshot { get; }
}

/// <summary>
/// Every row of a collection, read under one authority. It can only be built
/// by a walk that reached the server's end, so an API that replaces a whole
/// set or order can demand this type instead of a list. Check
/// <see cref="IsCurrentFor"/> again before sending.
/// </summary>
public sealed record AdminCollectionScope(string Resource, string? Filter = null)
{
    public bool IsWholeCollection => Filter is null;
}

public sealed class AdminCompleteSnapshot<T>
{
    internal AdminCompleteSnapshot(IReadOnlyList<T> items, long epoch, string accountId, AdminCollectionScope scope)
    {
        Items = items; Epoch = epoch; AccountId = accountId; Scope = scope;
    }

    public IReadOnlyList<T> Items { get; }
    public AdminCollectionScope Scope { get; }
    public long Epoch { get; }
    public string AccountId { get; }

    /// <summary>Whether this snapshot was read under <paramref name="lease"/>'s still-current authority.</summary>
    public bool IsCurrentFor(MonitorAuthorityLease lease, AdminCollectionScope scope) =>
        scope.IsWholeCollection && Scope == scope && lease.IsCurrent && lease.Epoch == Epoch && string.Equals(lease.AccountId, AccountId, StringComparison.Ordinal);
}

/// <summary>
/// Walks a cursor-paged admin collection within explicit bounds, checking the
/// lease before every send and after every response. Repeated or missing
/// continuations, blank or duplicate identities and oversized pages fail the
/// whole walk (nothing partial is returned for them); reaching a page or row
/// cap returns a truthful truncated read.
/// </summary>
public static class AdminCollectionWalk
{
    public static async Task<AdminListRead<T>> ReadAsync<T>(MonitorAuthorityLease lease, AdminCollectionScope scope, AdminWalkLimits limits,
        Func<string?, int, CancellationToken, Task<ApiCollectionPage<T>?>> fetchPage, Func<T, string?> identity,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Resource);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        var token = linked.Token;
        var items = new List<T>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var page = 0; ; page++)
        {
            if (page >= limits.MaxPages) return new AdminListRead<T>(items.ToArray(), AdminListCompleteness.TruncatedByPages, null);
            lease.ThrowIfRetired();
            token.ThrowIfCancellationRequested();
            var envelope = await fetchPage(cursor, limits.PageSize, token).ConfigureAwait(false);
            lease.ThrowIfRetired();
            token.ThrowIfCancellationRequested();
            var current = AdminPaging.ReadPage(envelope, limits.PageSize);
            foreach (var item in current.Items)
            {
                var id = identity(item);
                if (string.IsNullOrEmpty(id)) throw new AdminListInconsistentException(AdminListProblem.MissingIdentity);
                if (!identities.Add(id)) throw new AdminListInconsistentException(AdminListProblem.DuplicateIdentity);
                if (items.Count >= limits.MaxItems) return new AdminListRead<T>(items.ToArray(), AdminListCompleteness.TruncatedByItems, null);
                items.Add(item);
            }
            if (!current.HasMore)
            {
                var all = Array.AsReadOnly(items.ToArray());
                return new AdminListRead<T>(all, AdminListCompleteness.Complete, scope.IsWholeCollection ? new AdminCompleteSnapshot<T>(all, lease.Epoch, lease.AccountId, scope) : null);
            }
            if (!cursors.Add(current.NextCursor!)) throw new AdminListInconsistentException(AdminListProblem.RepeatedContinuation);
            cursor = current.NextCursor;
        }
    }
}

/// <summary>
/// User-driven cursor pagination (Next/Previous) for one list. The scope
/// names everything a cursor depends on (authority, filters, page size); a
/// different scope restarts from the first page synchronously, before the
/// next request is built, so an old cursor is never sent with new filters.
/// </summary>
public sealed record AdminCursorPosition(string? Scope, long Revision, string? Cursor);

public sealed class AdminCursorPager(int maxDepth = 1000)
{
    private readonly List<string?> _cursors = [null];
    private long _revision;
    public AdminCursorPosition Capture() => new(Scope, _revision, Cursor);

    public string? Scope { get; private set; }
    public int PageIndex => _cursors.Count - 1;
    /// <summary>The cursor to request the current page with (null for the first page).</summary>
    public string? Cursor => _cursors[^1];
    public bool CanGoBack => _cursors.Count > 1;

    /// <summary>Sets the scope; returns true (and restarts at the first page) when it changed.</summary>
    public bool SetScope(string scope)
    {
        if (string.Equals(scope, Scope, StringComparison.Ordinal)) return false;
        Scope = scope;
        _revision++;
        _cursors.Clear();
        _cursors.Add(null);
        return true;
    }

    /// <summary>Moves to the page after the current one; false for a missing, oversized or repeated cursor or past the depth cap.</summary>
    public bool TryAdvance(AdminCursorPosition request, string? nextCursor)
    {
        if (request != Capture()) return false;
        if (!AdminPaging.IsValidCursor(nextCursor) || _cursors.Count > maxDepth || _cursors.Contains(nextCursor)) return false;
        _revision++;
        _cursors.Add(nextCursor);
        return true;
    }

    public bool Back()
    {
        if (_cursors.Count <= 1) return false;
        _revision++;
        _cursors.RemoveAt(_cursors.Count - 1);
        return true;
    }

    /// <summary>
    /// A scope key from the lease's authority, the page size and every filter
    /// value as the request will send it (trim before calling where upstream trims).
    /// </summary>
    public static string ScopeOf(MonitorAuthorityLease lease, int pageSize, params string?[] filters)
    {
        var builder = new StringBuilder().Append(lease.Epoch).Append('|').Append(lease.AccountId.Length).Append(':').Append(lease.AccountId)
            .Append('|').Append(pageSize);
        foreach (var filter in filters)
            builder.Append('|').Append(filter == null ? "-" : filter.Length + ":" + filter);
        return builder.ToString();
    }
}

/// <summary>
/// A resource as read with its validator. Guarded writes send exactly this
/// strong tag as <c>If-Match</c>; a missing, weak or wildcard tag permits
/// display only (disable the write and offer Reload). Never reuse a list
/// validator as a detail validator, fabricate one, or retry a 412 automatically.
/// </summary>
public sealed record AdminVersioned<T>(T Value, string? ETag)
{
    public bool CanWrite => AdminValidators.IsStrong(ETag);
}

public static class AdminValidators
{
    public static bool IsStrong(string? etag) =>
        EntityTagHeaderValue.TryParse(etag, out var parsed) && !parsed.IsWeak && parsed.Tag != "*";

    /// <summary>Returns the strong validator or throws before anything is sent.</summary>
    public static string RequireStrong(string? etag) =>
        IsStrong(etag) ? etag! : throw new InvalidOperationException("This resource has no strong validator. Reload before saving.");
}

/// <summary>Distinct admin failure states a Manage page presents differently (audit §3: never collapse them).</summary>
public enum AdminFailureKind
{
    /// <summary>The captured admin authority retired; discard silently, never reauthorize.</summary>
    Retired,
    Cancelled,
    Unauthorized,
    Forbidden,
    ProfileVerificationRequired,
    DemoRestricted,
    CapabilityUnavailable,
    NotFound,
    Conflict,
    /// <summary>412: the validator is stale; keep the draft and offer Reload/review.</summary>
    PreconditionFailed,
    /// <summary>428: the write needed a validator.</summary>
    PreconditionRequired,
    Validation,
    RateLimited,
    InvalidResponse,
    Transport,
    Server,
    Unknown,
}

public static class AdminFailures
{
    /// <summary>Classifies a failed admin read or write. Pass the lease it ran under to tell retirement from cancellation.</summary>
    public static AdminFailureKind Classify(Exception error, MonitorAuthorityLease? lease = null)
    {
        switch (error)
        {
            case MonitorAuthorityRetiredException: return AdminFailureKind.Retired;
            case OperationCanceledException when error.InnerException is TimeoutException: return AdminFailureKind.Transport;
            case OperationCanceledException: return lease is { IsCurrent: false } ? AdminFailureKind.Retired : AdminFailureKind.Cancelled;
            case ApiException api: return ClassifyApi(api);
            case AdminListInconsistentException or AdminNetworkAccessLimitException: return AdminFailureKind.InvalidResponse;
            case InvalidDataException or JsonException: return AdminFailureKind.InvalidResponse;
            case HttpRequestException or IOException: return AdminFailureKind.Transport;
            default: return AdminFailureKind.Unknown;
        }
    }

    private static AdminFailureKind ClassifyApi(ApiException error)
    {
        var code = error.ProblemType ?? error.ErrorCode;
        switch (code)
        {
            case "profile_verification_required" or "profile_unverified": return AdminFailureKind.ProfileVerificationRequired;
            case "demo_restricted": return AdminFailureKind.DemoRestricted;
            case "capability_disabled" or "capability_not_configured" or "capability_unsupported":
                return AdminFailureKind.CapabilityUnavailable;
            case "dependency_unavailable": return AdminFailureKind.Server;
            case "precondition_failed": return AdminFailureKind.PreconditionFailed;
            case "precondition_required": return AdminFailureKind.PreconditionRequired;
        }
        return error.StatusCode switch
        {
            401 => AdminFailureKind.Unauthorized,
            403 => AdminFailureKind.Forbidden,
            404 => AdminFailureKind.NotFound,
            409 => AdminFailureKind.Conflict,
            412 => AdminFailureKind.PreconditionFailed,
            428 => AdminFailureKind.PreconditionRequired,
            400 or 422 => AdminFailureKind.Validation,
            429 => AdminFailureKind.RateLimited,
            501 => AdminFailureKind.CapabilityUnavailable,
            >= 500 => AdminFailureKind.Server,
            _ => AdminFailureKind.Unknown,
        };
    }
}
