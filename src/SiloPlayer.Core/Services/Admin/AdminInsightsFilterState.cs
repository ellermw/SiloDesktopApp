using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>The seven Insights tabs in tab-strip order (official <c>INSIGHTS_TABS</c>).</summary>
public enum AdminInsightsTab
{
    /// <summary>Live streams (<c>?tab=activity</c>, the default).</summary>
    Activity,
    Playback,
    Titles,
    Library,
    People,
    Devices,
    Bandwidth,
}

/// <summary>Tab names, labels and which toolbar filters each tab shows (<c>AdminInsights.tsx</c>).</summary>
public static class AdminInsightsTabs
{
    /// <summary>The tab a bare <c>/admin/insights</c> opens, and the one omitted from the URL.</summary>
    public const AdminInsightsTab Default = AdminInsightsTab.Activity;

    public static IReadOnlyList<AdminInsightsTab> Ordered { get; } =
        [AdminInsightsTab.Activity, AdminInsightsTab.Playback, AdminInsightsTab.Titles, AdminInsightsTab.Library,
         AdminInsightsTab.People, AdminInsightsTab.Devices, AdminInsightsTab.Bandwidth];

    public static string Wire(this AdminInsightsTab tab) => tab switch
    {
        AdminInsightsTab.Activity => "activity",
        AdminInsightsTab.Playback => "playback",
        AdminInsightsTab.Titles => "titles",
        AdminInsightsTab.Library => "library",
        AdminInsightsTab.People => "people",
        AdminInsightsTab.Devices => "devices",
        AdminInsightsTab.Bandwidth => "bandwidth",
        _ => throw new ArgumentOutOfRangeException(nameof(tab), tab, "Unknown Insights tab."),
    };

    /// <summary>Exact, case-sensitive match as the official <c>INSIGHTS_TABS.includes</c>.</summary>
    public static bool TryParse(string? wire, out AdminInsightsTab tab)
    {
        foreach (var candidate in Ordered)
            if (string.Equals(candidate.Wire(), wire, StringComparison.Ordinal)) { tab = candidate; return true; }
        tab = Default;
        return false;
    }

    /// <summary>The tab's label key: the live tab reuses the Activity page title, the others are <c>web.admin.insights.tabs.*</c> (Playback is <c>tabs.activity</c>).</summary>
    public static string LabelKey(this AdminInsightsTab tab) => tab switch
    {
        AdminInsightsTab.Activity => "web.admin.activity.page.title",
        AdminInsightsTab.Playback => "web.admin.insights.tabs.activity",
        _ => "web.admin.insights.tabs." + tab.Wire(),
    };

    /// <summary>The shared toolbar (range, library, account, time zone) is hidden on the live tab, which brings its own search and filters.</summary>
    public static bool ShowsToolbar(this AdminInsightsTab tab) => tab != AdminInsightsTab.Activity;

    /// <summary>Everything but Bandwidth, which is server-wide.</summary>
    public static bool ShowsLibraryFilter(this AdminInsightsTab tab) => tab != AdminInsightsTab.Bandwidth;

    /// <summary>Playback, Titles, Library and Devices; People lists the accounts, Bandwidth is server-wide.</summary>
    public static bool ShowsAccountFilter(this AdminInsightsTab tab) =>
        tab is AdminInsightsTab.Playback or AdminInsightsTab.Titles or AdminInsightsTab.Library or AdminInsightsTab.Devices;

    public static bool ShowsTimezoneFilter(this AdminInsightsTab tab) => tab != AdminInsightsTab.Bandwidth;
}

/// <summary>A person (account, and optionally one of its profiles) a detail panel is scoped to (<c>?person=</c>, <c>?profile=</c>).</summary>
public sealed record AdminInsightsPersonScope(long UserId, string? ProfileId = null);

/// <summary>The detail panel the URL opens: a title (<c>?title=</c>), or a person; a title opened from a person keeps both.</summary>
public sealed record AdminInsightsDetailState(string? TitleId = null, AdminInsightsPersonScope? Person = null)
{
    public bool IsOpen => TitleId != null || Person != null;
}

/// <summary>
/// The Insights page's filter state, parsed from and written to the <see cref="AdminRouteRequest"/> query exactly as the official
/// page writes its URL (<c>insightsHelpers.ts</c> and <c>AdminInsights.tsx</c>, c59798116). Parameters at their default are
/// omitted: <c>tab</c> when <c>activity</c>, <c>range</c> when <c>30d</c>, <c>from</c>/<c>to</c> unless the range is custom,
/// <c>tz</c> when it equals the browser's zone. Every change replaces the location in place (the official page uses
/// <c>setSearchParams(..., {replace: true})</c>); unknown parameters are kept.
/// <para>
/// The page-wide dates need "today": <see cref="AdminInsightsFilterState.Parse"/> and <see cref="AdminInsightsFilters.WithRange"/> take it as a
/// <see cref="DateOnly"/> in the local calendar so tests and clocks are explicit.
/// </para>
/// </summary>
/// <param name="FromDate">Custom range start; when the URL has none, today minus 29 days (used only when <paramref name="Range"/> is custom).</param>
/// <param name="ToDate">Custom range end; when the URL has none, today.</param>
/// <param name="Tz">The time zone: the URL's, else the default passed to <see cref="Parse"/> (the browser zone).</param>
/// <param name="LibraryId">A positive integer or null.</param>
/// <param name="UserId">A positive integer or null.</param>
public sealed record AdminInsightsFilterState(
    AdminInsightsTab Tab,
    AdminInsightsRange Range,
    string FromDate,
    string ToDate,
    string Tz,
    long? LibraryId,
    long? UserId)
{
    /// <summary>Reads the Insights URL state (official <c>parseInsightsUrlState</c>). Anything invalid falls back to its default.</summary>
    public static AdminInsightsFilterState Parse(IReadOnlyDictionary<string, string>? query, string defaultTz, DateOnly today)
    {
        string? Get(string key) => query != null && query.TryGetValue(key, out var value) ? value : null;
        var tab = AdminInsightsTabs.TryParse(Get("tab"), out var parsedTab) ? parsedTab : AdminInsightsTabs.Default;
        var range = AdminInsightsRanges.TryParse(Get("range"), out var parsedRange) ? parsedRange : AdminInsightsRanges.Default;
        var (fallbackFrom, fallbackTo) = AdminInsightsFilters.DefaultCustomDates(today);
        return new AdminInsightsFilterState(tab, range,
            OrFallback(Get("from"), fallbackFrom), OrFallback(Get("to"), fallbackTo), OrFallback(Get("tz"), defaultTz),
            AdminInsightsFilters.ParsePositiveId(Get("library_id")), AdminInsightsFilters.ParsePositiveId(Get("user_id")));
    }

    public static AdminInsightsFilterState Parse(AdminRouteRequest request, string defaultTz, DateOnly today) => Parse(request.Query, defaultTz, today);

    private static string OrFallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    /// <summary>
    /// The state as the URL the official page would show for it: <paramref name="detail"/> (title or person panel) is included when open,
    /// and every default is left out. <see cref="Parse(IReadOnlyDictionary{string, string}?, string, DateOnly)"/> reads it back to the same state.
    /// </summary>
    public AdminInsightsRouteQuery ToQuery(string browserTz, AdminInsightsDetailState? detail = null)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Tab != AdminInsightsTabs.Default) query["tab"] = Tab.Wire();
        if (Range != AdminInsightsRanges.Default) query["range"] = Range.Wire();
        if (Range == AdminInsightsRange.Custom)
        {
            query["from"] = FromDate;
            query["to"] = ToDate;
        }
        if (!string.Equals(Tz, browserTz, StringComparison.Ordinal)) query["tz"] = Tz;
        if (LibraryId is { } library) query["library_id"] = library.ToString(CultureInfo.InvariantCulture);
        if (UserId is { } user) query["user_id"] = user.ToString(CultureInfo.InvariantCulture);
        if (detail?.TitleId is { } title) query["title"] = title;
        if (detail?.Person is { } person)
        {
            query["person"] = person.UserId.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(person.ProfileId)) query["profile"] = person.ProfileId;
        }
        return new AdminInsightsRouteQuery(query);
    }

    /// <summary>
    /// The window a read sends (official <c>buildInsightsWindowQueryParams</c>). <paramref name="person"/> replaces the page's account
    /// filter (and adds its profile) for a person panel; <paramref name="omitLibrary"/> and <paramref name="omitUser"/> drop the page's own filter.
    /// </summary>
    public AdminInsightsWindow ToWindow(bool omitLibrary = false, bool omitUser = false, AdminInsightsPersonScope? person = null)
    {
        var user = person?.UserId ?? (omitUser ? null : UserId);
        return new AdminInsightsWindow
        {
            Range = Range,
            Tz = Tz,
            FromDate = Range == AdminInsightsRange.Custom ? FromDate : null,
            ToDate = Range == AdminInsightsRange.Custom ? ToDate : null,
            LibraryId = !omitLibrary && LibraryId is { } library ? library.ToString(CultureInfo.InvariantCulture) : null,
            UserId = user is { } id ? id.ToString(CultureInfo.InvariantCulture) : null,
            ProfileId = string.IsNullOrEmpty(person?.ProfileId) ? null : person.ProfileId,
        };
    }

    /// <summary>
    /// The filters a detail panel honours: only those the current tab shows (an account chosen on another tab stays in the URL but is
    /// hidden here), so Bandwidth blanks the library and the tabs without the account filter blank the account.
    /// </summary>
    public AdminInsightsFilterState ForDetail() => this with
    {
        LibraryId = Tab.ShowsLibraryFilter() ? LibraryId : null,
        UserId = Tab.ShowsAccountFilter() ? UserId : null,
    };

    /// <summary>
    /// Whether Reset shows: never on the live tab, otherwise when the range, time zone, library or account differ from their defaults
    /// (<paramref name="browserTz"/> is the default zone).
    /// </summary>
    public bool HasNonDefaultFilters(string browserTz) =>
        Tab.ShowsToolbar()
        && (Range != AdminInsightsRanges.Default || !string.Equals(Tz, browserTz, StringComparison.Ordinal) || LibraryId != null || UserId != null);
}

/// <summary>A query for the Insights route: the dictionary the <see cref="AdminRouteRequest"/> carries, plus its href.</summary>
public sealed record AdminInsightsRouteQuery(IReadOnlyDictionary<string, string> Values)
{
    /// <summary>The Insights location with this query (<c>Query</c> is null when empty, as <see cref="AdminRouteRequest.WithQuery"/> leaves it).</summary>
    public AdminRouteRequest ToRequest() => new(AdminRoute.Insights, Values.Count > 0 ? Values : null);

    /// <summary>The canonical href: <c>/admin/insights</c> plus the query in ordinal key order.</summary>
    public string Href => ToRequest().Href;
}

/// <summary>
/// Changes to the Insights URL, each returning the next location (the official page's <c>updateParams</c> handlers). They start from the
/// current request, so parameters the page does not own survive, and always target <see cref="AdminRoute.Insights"/>.
/// </summary>
public static class AdminInsightsFilters
{
    /// <summary>The parameters a detail panel owns (<c>INSIGHTS_DETAIL_PARAMS</c>).</summary>
    public static IReadOnlyList<string> DetailParameters { get; } = ["title", "person", "profile"];

    /// <summary>Today minus 29 days to today, as <c>YYYY-MM-DD</c> (the seed for a custom range).</summary>
    public static (string From, string To) DefaultCustomDates(DateOnly today)
        => (FormatIsoDate(today.AddDays(-29)), FormatIsoDate(today));

    public static string FormatIsoDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Official <c>parsePositiveIntParam</c>: <c>Number(raw)</c> as a safe positive integer, else null (so <c>007</c> is 7 and <c>1e2</c> is 100).</summary>
    public static long? ParsePositiveId(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
        return value > 0 && value <= 9007199254740991d && Math.Floor(value) == value ? (long)value : null;
    }

    /// <summary>Official <c>parseInsightsDetailState</c>: the panel the URL opens (<c>title</c>, or <c>person</c> with an optional <c>profile</c>).</summary>
    public static AdminInsightsDetailState ParseDetail(IReadOnlyDictionary<string, string>? query)
    {
        string? Get(string key) => query != null && query.TryGetValue(key, out var value) ? value : null;
        var title = Get("title")?.Trim();
        var person = ParsePositiveId(Get("person"));
        var profile = Get("profile")?.Trim();
        return new AdminInsightsDetailState(string.IsNullOrEmpty(title) ? null : title,
            person is { } id ? new AdminInsightsPersonScope(id, string.IsNullOrEmpty(profile) ? null : profile) : null);
    }

    public static AdminInsightsDetailState ParseDetail(AdminRouteRequest request) => ParseDetail(request.Query);

    /// <summary>Selecting the default tab removes <c>tab</c> from the URL.</summary>
    public static AdminRouteRequest WithTab(AdminRouteRequest request, AdminInsightsTab tab)
        => Edit(request, query =>
        {
            if (tab == AdminInsightsTabs.Default) query.Remove("tab"); else query["tab"] = tab.Wire();
        });

    /// <summary>
    /// A preset range: <c>30d</c> removes <c>range</c>; any other sets it. Custom keeps an existing <c>from</c>/<c>to</c> and seeds
    /// the missing ones from <paramref name="today"/>; every other range removes both.
    /// </summary>
    public static AdminRouteRequest WithRange(AdminRouteRequest request, AdminInsightsRange range, DateOnly today)
        => Edit(request, query =>
        {
            if (range == AdminInsightsRanges.Default) query.Remove("range"); else query["range"] = range.Wire();
            if (range == AdminInsightsRange.Custom)
            {
                var (from, to) = DefaultCustomDates(today);
                if (!query.TryGetValue("from", out var existingFrom) || existingFrom.Length == 0) query["from"] = from;
                if (!query.TryGetValue("to", out var existingTo) || existingTo.Length == 0) query["to"] = to;
            }
            else
            {
                query.Remove("from");
                query.Remove("to");
            }
        });

    /// <summary>Editing the start date switches to custom and sets <c>from</c> when the value is not empty.</summary>
    public static AdminRouteRequest WithCustomFrom(AdminRouteRequest request, string? value)
        => Edit(request, query =>
        {
            query["range"] = AdminInsightsRange.Custom.Wire();
            if (!string.IsNullOrEmpty(value)) query["from"] = value;
        });

    /// <summary>Editing the end date switches to custom and sets <c>to</c> when the value is not empty.</summary>
    public static AdminRouteRequest WithCustomTo(AdminRouteRequest request, string? value)
        => Edit(request, query =>
        {
            query["range"] = AdminInsightsRange.Custom.Wire();
            if (!string.IsNullOrEmpty(value)) query["to"] = value;
        });

    /// <summary>"All libraries" (null) removes <c>library_id</c>.</summary>
    public static AdminRouteRequest WithLibrary(AdminRouteRequest request, long? libraryId)
        => Edit(request, query =>
        {
            if (libraryId is { } id) query["library_id"] = id.ToString(CultureInfo.InvariantCulture); else query.Remove("library_id");
        });

    /// <summary>"All accounts" (null) removes <c>user_id</c>.</summary>
    public static AdminRouteRequest WithAccount(AdminRouteRequest request, long? userId)
        => Edit(request, query =>
        {
            if (userId is { } id) query["user_id"] = id.ToString(CultureInfo.InvariantCulture); else query.Remove("user_id");
        });

    /// <summary>Choosing the browser's own zone removes <c>tz</c>; any other zone sets it.</summary>
    public static AdminRouteRequest WithTimezone(AdminRouteRequest request, string tz, string browserTz)
        => Edit(request, query =>
        {
            if (string.Equals(tz, browserTz, StringComparison.Ordinal)) query.Remove("tz"); else query["tz"] = tz;
        });

    /// <summary>Opens the title panel; an open person panel stays (the title shows that person's plays).</summary>
    public static AdminRouteRequest WithTitle(AdminRouteRequest request, string contentId)
        => Edit(request, query => query["title"] = contentId);

    /// <summary>Opens a person panel (<c>person</c>, plus <c>profile</c> when one profile is chosen).</summary>
    public static AdminRouteRequest WithPerson(AdminRouteRequest request, AdminInsightsPersonScope person)
        => Edit(request, query =>
        {
            query["person"] = person.UserId.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(person.ProfileId)) query.Remove("profile"); else query["profile"] = person.ProfileId;
        });

    /// <summary>Closes whichever panel is open: removes <c>title</c>, <c>person</c> and <c>profile</c>.</summary>
    public static AdminRouteRequest CloseDetail(AdminRouteRequest request)
        => Edit(request, query => { foreach (var key in DetailParameters) query.Remove(key); });

    /// <summary>Closes only the title panel, returning to the person it was opened from.</summary>
    public static AdminRouteRequest CloseTitle(AdminRouteRequest request)
        => Edit(request, query => query.Remove("title"));

    /// <summary>
    /// What closing the open title or person panel does: while a person is in the URL the title closes alone (back to that person),
    /// otherwise everything closes.
    /// </summary>
    public static AdminRouteRequest CloseForDetail(AdminRouteRequest request)
        => ParseDetail(request) is { TitleId: not null, Person: not null } ? CloseTitle(request) : CloseDetail(request);

    /// <summary>
    /// Reset keeps only the tab: the range, time zone, library, account, any panel and every other parameter go.
    /// The tab is left out when it is the default.
    /// </summary>
    public static AdminRouteRequest Reset(AdminRouteRequest request, AdminInsightsTab tab)
        => tab == AdminInsightsTabs.Default
            ? new AdminRouteRequest(AdminRoute.Insights)
            : new AdminRouteRequest(AdminRoute.Insights, new Dictionary<string, string>(StringComparer.Ordinal) { ["tab"] = tab.Wire() });

    /// <summary>
    /// The Insights href with some parameters set or (null or empty) removed and the rest kept (official <c>insightsHrefWith</c>),
    /// for links from a row to its title or person panel.
    /// </summary>
    public static string HrefWith(IReadOnlyDictionary<string, string>? current, IReadOnlyDictionary<string, string?> patch)
    {
        var request = new AdminRouteRequest(AdminRoute.Insights, current);
        foreach (var (key, value) in patch) request = request.WithQuery(key, value);
        return request.Href;
    }

    private static AdminRouteRequest Edit(AdminRouteRequest request, Action<Dictionary<string, string>> change)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Query != null) foreach (var (key, value) in request.Query) query[key] = value;
        change(query);
        return new AdminRouteRequest(AdminRoute.Insights, query.Count > 0 ? query : null);
    }
}
