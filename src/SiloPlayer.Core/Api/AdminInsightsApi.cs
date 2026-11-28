using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>The server has no Insights endpoint for this read (an older build, or one the deployment does not serve).</summary>
public sealed class AdminInsightsUnsupportedException(string path) : Exception("This server does not provide Insights for this view.")
{
    /// <summary>The path that was refused, without its query.</summary>
    public string Path { get; } = path;
}

/// <summary>
/// Admin › Monitor › Insights reads (official Silo c59798116: <c>web/src/hooks/queries/admin/insights.ts</c>,
/// <c>internal/apiv2/admin_insights*.go</c>). Every call runs under the caller's captured <see cref="MonitorAuthorityLease"/>:
/// checked before sending and after the response, never recaptured, so a read that outlives its account, server or profile
/// publishes nothing. All reads are GETs and are never replayed by this class.
/// <para>
/// The official page caches by an authority-scoped key; here the caller owns that cache and keys it by lease, which retires with
/// the same account, server, profile and PIN changes. <c>refresh=true</c> is sent only when the caller asks (see
/// <see cref="AdminInsightsRefreshGate"/>) and asks the server to drop its cached aggregates; it does not belong in a cache key.
/// </para>
/// <para>
/// A read may take seconds on a large history (all-time), so every call has an explicit deadline, <see cref="ReadDeadline"/>:
/// longer than the 30 seconds the WebUI gives a read, shorter than the 100 seconds the shared HTTP client would allow, and a
/// timeout is a <see cref="TaskCanceledException"/> carrying a <see cref="TimeoutException"/>, as the shared client reports one.
/// </para>
/// A 404, 405 or 501 (or a <c>capability_unsupported</c> problem) means the server does not serve the view and surfaces as
/// <see cref="AdminInsightsUnsupportedException"/>; the one exception is the title read, where a <c>not_found</c> problem is
/// the answer "no such title" and returns null.
/// </summary>
public sealed class AdminInsightsApi(SiloApiClient client, TimeSpan? readDeadline = null)
{
    public const string BasePath = "/api/v2/admin/insights";

    /// <summary>The whole-call deadline of one read (retries included); a constructor argument replaces it, which is for tests.</summary>
    public static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(90);

    /// <summary>Accounts per People page on the Insights page (<c>INSIGHTS_PEOPLE_PAGE_SIZE</c>).</summary>
    public const int PeoplePageSize = 50;
    public const int PeopleMaxLimit = 100;
    public const int PeopleMaxOffset = 10_000;
    public const int PeopleMaxSearchLength = 128;
    /// <summary>Rows per Titles list: the page asks for 10, the dashboard and a person's panel for 8; the server allows 1 to 50.</summary>
    public const int TitlesPageLimit = 10;
    public const int TitlesMaxLimit = 50;
    /// <summary>The Devices matrix floor: the page's default and its "5+ sessions" choice; "All" is 1 and the server allows up to 1000.</summary>
    public const int DefaultMinSessions = 5;
    public const int MaxMinSessions = 1000;
    public const int MaxTzLength = 128;
    public const int MaxProfileIdLength = 1024;
    public const int MaxContentIdLength = 1024;

    // ── Paths (public and static so the request shape is testable without a server) ──

    public static string CapabilitiesPath() => BasePath + "/capabilities";

    public static string ActivityPath(AdminInsightsWindow window, bool refresh = false)
        => Build("/activity", window, Scope.Account, refresh);

    public static string TitlesPath(AdminInsightsWindow window, AdminInsightsTitleSort sort = AdminInsightsTitleSort.Plays,
        int limit = TitlesPageLimit, bool refresh = false)
        => Build("/titles", window, Scope.Account, refresh,
            ("sort", sort.Wire()), ("limit", AdminPaging.ClampLimit(limit, TitlesMaxLimit).ToString(CultureInfo.InvariantCulture)));

    /// <summary>One movie or series. The server does not cache this read, so there is no refresh flag.</summary>
    public static string TitlePath(string contentId, AdminInsightsWindow window)
    {
        if (string.IsNullOrWhiteSpace(contentId) || contentId.Length > MaxContentIdLength)
            throw new ArgumentException("Expected a title identifier.", nameof(contentId));
        return Build("/titles/" + Uri.EscapeDataString(contentId), window, Scope.Account, refresh: false);
    }

    public static string LibraryPath(AdminInsightsWindow window, bool refresh = false)
        => Build("/library", window, Scope.Account, refresh);

    /// <summary>People takes the library but no account or profile: it lists the accounts.</summary>
    public static string PeoplePath(AdminInsightsWindow window, AdminInsightsPeopleQuery? query = null, bool refresh = false)
    {
        query ??= new AdminInsightsPeopleQuery();
        var search = query.Search?.Trim();
        if (search is { Length: > PeopleMaxSearchLength }) throw new ArgumentException("Search text is too long.", nameof(query));
        var extra = new List<(string, string)>(5);
        if (!string.IsNullOrEmpty(search)) extra.Add(("search", search));
        extra.Add(("sort", query.Sort.Wire()));
        extra.Add(("dir", query.Descending ? "desc" : "asc"));
        extra.Add(("limit", AdminPaging.ClampLimit(query.Limit, PeopleMaxLimit).ToString(CultureInfo.InvariantCulture)));
        extra.Add(("offset", Math.Clamp(query.Offset, 0, PeopleMaxOffset).ToString(CultureInfo.InvariantCulture)));
        return Build("/people", window, Scope.Library, refresh, extra.ToArray());
    }

    public static string DevicesPath(AdminInsightsWindow window, int minSessions = DefaultMinSessions, bool refresh = false)
        => Build("/devices", window, Scope.Account, refresh,
            ("min_sessions", AdminPaging.ClampLimit(minSessions, MaxMinSessions).ToString(CultureInfo.InvariantCulture)));

    /// <summary>Bandwidth is server-wide: the range and time zone only.</summary>
    public static string BandwidthPath(AdminInsightsWindow window, bool refresh = false)
        => Build("/bandwidth", window, Scope.None, refresh);

    // ── Reads ────────────────────────────────────────────────

    /// <summary>
    /// What the server serves. Null when it has no capabilities endpoint at all (older than Insights). The official pages do not call
    /// this; reading it first lets a page say "not available" instead of failing every tab.
    /// </summary>
    public async Task<AdminInsightsCapabilities?> GetCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct = default)
    {
        try { return await ReadAsync<AdminInsightsCapabilities>(lease, CapabilitiesPath(), ct).ConfigureAwait(false); }
        catch (AdminInsightsUnsupportedException) { return null; }
    }

    public Task<AdminInsightsActivity> GetActivityAsync(MonitorAuthorityLease lease, AdminInsightsWindow window, bool refresh = false,
        CancellationToken ct = default)
        => ReadAsync<AdminInsightsActivity>(lease, ActivityPath(window, refresh), ct);

    public Task<AdminInsightsTitles> GetTitlesAsync(MonitorAuthorityLease lease, AdminInsightsWindow window,
        AdminInsightsTitleSort sort = AdminInsightsTitleSort.Plays, int limit = TitlesPageLimit, bool refresh = false,
        CancellationToken ct = default)
        => ReadAsync<AdminInsightsTitles>(lease, TitlesPath(window, sort, limit, refresh), ct);

    /// <summary>
    /// One movie or series over the window, or null when it no longer exists (a <c>not_found</c> problem). An episode id answers
    /// 422, which stays an <see cref="ApiException"/>: read its series instead.
    /// </summary>
    public async Task<AdminInsightsTitle?> GetTitleAsync(MonitorAuthorityLease lease, string contentId, AdminInsightsWindow window,
        CancellationToken ct = default)
    {
        var path = TitlePath(contentId, window);
        try { return await ReadAsync<AdminInsightsTitle>(lease, path, ct, titleNotFoundIsNull: true).ConfigureAwait(false); }
        catch (TitleNotFoundSignal) { return null; }
    }

    public Task<AdminInsightsLibrary> GetLibraryAsync(MonitorAuthorityLease lease, AdminInsightsWindow window, bool refresh = false,
        CancellationToken ct = default)
        => ReadAsync<AdminInsightsLibrary>(lease, LibraryPath(window, refresh), ct);

    /// <summary>
    /// One People page. Walk it with <see cref="AdminInsightsRules.NextPeopleOffset"/> and merge with
    /// <see cref="AdminInsightsRules.MergePeopleAccounts"/>; the response repeats <c>total_accounts</c> and <c>top_profiles</c> every page.
    /// </summary>
    public Task<AdminInsightsPeople> GetPeopleAsync(MonitorAuthorityLease lease, AdminInsightsWindow window,
        AdminInsightsPeopleQuery? query = null, bool refresh = false, CancellationToken ct = default)
        => ReadAsync<AdminInsightsPeople>(lease, PeoplePath(window, query, refresh), ct);

    public Task<AdminInsightsDevices> GetDevicesAsync(MonitorAuthorityLease lease, AdminInsightsWindow window,
        int minSessions = DefaultMinSessions, bool refresh = false, CancellationToken ct = default)
        => ReadAsync<AdminInsightsDevices>(lease, DevicesPath(window, minSessions, refresh), ct);

    public Task<AdminInsightsBandwidth> GetBandwidthAsync(MonitorAuthorityLease lease, AdminInsightsWindow window, bool refresh = false,
        CancellationToken ct = default)
        => ReadAsync<AdminInsightsBandwidth>(lease, BandwidthPath(window, refresh), ct);

    // ── Transport ────────────────────────────────────────────

    private sealed class TitleNotFoundSignal : Exception;

    private async Task<T> ReadAsync<T>(MonitorAuthorityLease lease, string path, CancellationToken ct, bool titleNotFoundIsNull = false)
        where T : class
    {
        lease.ThrowIfRetired();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lease.Token, ct);
        linked.CancelAfter(readDeadline ?? ReadDeadline);
        T? body;
        try
        {
            body = await client.SendRequestAsync<T?>(lease.Context, HttpMethod.Get, path, null, linked.Token).ConfigureAwait(false);
        }
        catch (ApiException error) when (titleNotFoundIsNull && error.StatusCode == 404 && error.ProblemType == "not_found")
        {
            lease.ThrowIfRetired();
            throw new TitleNotFoundSignal();
        }
        catch (ApiException error) when (error.StatusCode is 404 or 405 or 501 || error.ProblemType == "capability_unsupported")
        {
            lease.ThrowIfRetired();
            throw new AdminInsightsUnsupportedException(path.Split('?', 2)[0]);
        }
        catch (JsonException)
        {
            lease.ThrowIfRetired();
            throw new InvalidDataException("The server sent an unusable Insights response.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && lease.IsCurrent && linked.IsCancellationRequested)
        {
            // Our deadline, not the caller and not a retired lease: report it as a transport timeout.
            throw new TaskCanceledException("The Insights read timed out.", new TimeoutException());
        }
        lease.ThrowIfRetired();
        ct.ThrowIfCancellationRequested();
        if (body == null) throw new InvalidDataException("The server sent an empty Insights response.");
        ValidateResponse(body);
        lease.ThrowIfRetired();
        ct.ThrowIfCancellationRequested();
        return body;
    }

    private static readonly ConcurrentDictionary<Type, (PropertyInfo Property, bool AllowsNull)[]> ResponseProperties = new();

    // JsonRequired checks presence, not explicit null. Use the Insights DTOs' nullability for members;
    // their collections never allow null elements, including optional breakdowns when supplied.
    private static void ValidateResponse(object value)
    {
        if (value is IList list)
        {
            foreach (var element in list)
                ValidateResponse(element ?? throw new InvalidDataException("The server sent an unusable Insights response."));
            return;
        }
        var type = value.GetType();
        if (type.Namespace != typeof(AdminInsightsActivity).Namespace) return;
        foreach (var (property, allowsNull) in ResponseProperties.GetOrAdd(type, static model =>
        {
            var nullability = new NullabilityInfoContext();
            return model.GetProperties().Where(property => !property.PropertyType.IsValueType)
                .Select(property => (property, nullability.Create(property).ReadState == NullabilityState.Nullable)).ToArray();
        }))
        {
            if (property.GetValue(value) is { } member) ValidateResponse(member);
            else if (!allowsNull) throw new InvalidDataException("The server sent an unusable Insights response.");
        }
    }

    // ── Query construction ───────────────────────────────────

    private enum Scope
    {
        /// <summary>Range and time zone only.</summary>
        None,
        /// <summary>Plus the library.</summary>
        Library,
        /// <summary>Plus the library, account and profile.</summary>
        Account,
    }

    private static string Build(string subpath, AdminInsightsWindow window, Scope scope, bool refresh, params (string Name, string Value)[] extra)
    {
        ArgumentNullException.ThrowIfNull(window);
        var query = new StringBuilder();
        void Add(string name, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            query.Append(query.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }

        Add("range", window.Range.Wire());
        var tz = string.IsNullOrWhiteSpace(window.Tz) ? "UTC" : window.Tz.Trim();
        if (tz.Length > MaxTzLength) throw new ArgumentException("Time zone is too long.", nameof(window));
        Add("tz", tz);
        if (window.Range == AdminInsightsRange.Custom)
        {
            Add("from_date", RequireDate(window.FromDate, "from"));
            Add("to_date", RequireDate(window.ToDate, "to"));
        }
        if (scope >= Scope.Library) Add("library_id", PositiveId(window.LibraryId, "library"));
        if (scope >= Scope.Account)
        {
            var user = PositiveId(window.UserId, "account");
            var profile = string.IsNullOrWhiteSpace(window.ProfileId) ? null : window.ProfileId.Trim();
            if (profile != null && user == null) throw new ArgumentException("A profile needs its account.", nameof(window));
            if (profile is { Length: > MaxProfileIdLength }) throw new ArgumentException("Profile id is too long.", nameof(window));
            Add("user_id", user);
            Add("profile_id", profile);
        }
        foreach (var (name, value) in extra) Add(name, value);
        if (refresh) Add("refresh", "true");
        return BasePath + subpath + query;
    }

    private static string RequireDate(string? value, string name)
    {
        var trimmed = value?.Trim();
        if (trimmed == null || !DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException($"A custom range needs a {name} date (YYYY-MM-DD).", nameof(value));
        return trimmed;
    }

    private static string? PositiveId(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : throw new ArgumentException($"Expected a positive {name} identifier.", nameof(value));
    }
}
