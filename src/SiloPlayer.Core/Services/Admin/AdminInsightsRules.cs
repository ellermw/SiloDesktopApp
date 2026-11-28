using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// Timing the Insights and Playback History reads follow, from the official hooks at c59798116
/// (<c>hooks/queries/admin/insights.ts</c>, <c>history.ts</c>, <c>stats.ts</c>) and pages. Named so a page never hard-codes one.
/// </summary>
public static class AdminInsightsIntervals
{
    /// <summary>An Insights tab read is fresh for 45 seconds (<c>INSIGHTS_STALE_TIME</c>); tabs do not poll.</summary>
    public static readonly TimeSpan TabStale = TimeSpan.FromSeconds(45);
    /// <summary>The capabilities document is fresh for 5 minutes.</summary>
    public static readonly TimeSpan CapabilitiesStale = TimeSpan.FromMinutes(5);
    /// <summary>After Refresh, reads started in this window send <c>refresh=true</c> (<c>markAdminInsightsRefresh</c>).</summary>
    public static readonly TimeSpan RefreshWindow = TimeSpan.FromSeconds(5);
    /// <summary>The Refresh spinner stays up at least this long (<c>REFRESH_SPINNER_MIN_VISIBLE_MS</c>).</summary>
    public static readonly TimeSpan RefreshSpinnerMinVisible = TimeSpan.FromMilliseconds(600);

    /// <summary>A dashboard Insights widget over a week or month re-reads every 5 minutes (<c>dashboardInsightsInterval</c>).</summary>
    public static readonly TimeSpan DashboardShortRangePoll = TimeSpan.FromMinutes(5);
    /// <summary>A dashboard Insights widget over a quarter, year or all time re-reads every 15 minutes: those reads take seconds.</summary>
    public static readonly TimeSpan DashboardLongRangePoll = TimeSpan.FromMinutes(15);

    /// <summary>The Playback History page is fresh for 15 seconds and, on its first page only, re-reads at that interval (<c>ADMIN_HISTORY_STALE_TIME</c>).</summary>
    public static readonly TimeSpan HistoryStale = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan HistoryPoll = TimeSpan.FromSeconds(15);
    /// <summary>One session read for a <c>?session=</c> link is fresh for 60 seconds.</summary>
    public static readonly TimeSpan HistoryDetailStale = TimeSpan.FromSeconds(60);
    /// <summary>The History Refresh spinner stays up at least this long.</summary>
    public static readonly TimeSpan HistoryRefreshSpinnerMinVisible = TimeSpan.FromSeconds(1);

    /// <summary>Live sessions are fresh for 30 seconds (<c>ADMIN_STALE_TIME</c>) and updated by events in between.</summary>
    public static readonly TimeSpan LiveSessionsStale = TimeSpan.FromSeconds(30);

    /// <summary>Typing in the People or History search box applies after this pause.</summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

    /// <summary>The interval a dashboard Insights widget polls at for a window (week and month are short, everything longer is long).</summary>
    public static TimeSpan DashboardPoll(AdminInsightsRange range) =>
        range is AdminInsightsRange.Days7 or AdminInsightsRange.Days30 ? DashboardShortRangePoll : DashboardLongRangePoll;

    /// <summary>The interval for a dashboard widget range wire name (<c>week</c>, <c>month</c>, <c>quarter</c>, <c>year</c>, <c>all</c>).</summary>
    public static TimeSpan DashboardPoll(string? widgetRange) => widgetRange is "week" or "month" ? DashboardShortRangePoll : DashboardLongRangePoll;
}

/// <summary>Page sizes and row limits of the Insights and History screens.</summary>
public static class AdminInsightsLimits
{
    public const int PeoplePageSize = AdminInsightsApi.PeoplePageSize;
    /// <summary>Rows the dashboard's Top titles asks the server for and shows.</summary>
    public const int DashboardTitles = 8;
    /// <summary>Top profiles shown on the dashboard (the read itself asks for one account).</summary>
    public const int DashboardProfiles = 8;
    public const int PersonTopTitles = 8;
    public const int PersonTopClients = 6;
    /// <summary>A person's panel reads every client, not only those with five sessions.</summary>
    public const int PersonDevicesMinSessions = 1;
    public const int HistoryDefaultPageSize = 25;
    public static IReadOnlyList<int> HistoryPageSizes { get; } = [25, 50, 100];
}

/// <summary>
/// Insights rules that need no window or server: the dashboard range map, calendar grids, the viewing heatmap, People paging and
/// the formatters. Ported from <c>insightsHelpers.ts</c>, <c>viewingTime.ts</c>, <c>insightsTitleFormat.ts</c>, <c>ViewingHeatmap.tsx</c>
/// and <c>historyFormat.ts</c> at official c59798116. Formatters read <see cref="CultureInfo.CurrentCulture"/> unless given one.
/// </summary>
public static class AdminInsightsRules
{
    private const string EmDash = "—";

    // ── Dashboard range <-> Insights range ───────────────────

    /// <summary>
    /// The Insights preset closest to a dashboard widget range (official <c>widgetRangeToInsightsRange</c>): month 30d, quarter 90d,
    /// year 1y, all all, and everything shorter (hour, day, week, unknown) the 7-day view. Takes the wire name so the dashboard's range
    /// type can grow without this changing.
    /// </summary>
    public static AdminInsightsRange WidgetRangeToInsightsRange(string? widgetRange) => widgetRange switch
    {
        "month" => AdminInsightsRange.Days30,
        "quarter" => AdminInsightsRange.Days90,
        "year" => AdminInsightsRange.Year1,
        "all" => AdminInsightsRange.All,
        _ => AdminInsightsRange.Days7,
    };

    public static AdminInsightsRange WidgetRangeToInsightsRange(DashboardWidgetRange range) => WidgetRangeToInsightsRange(DashboardRanges.WireName(range));

    /// <summary>The dashboard widget range wire name for an Insights preset (official <c>insightsRangeToWidgetRange</c>): <c>week</c>, <c>month</c>, <c>quarter</c>, <c>year</c>, <c>all</c>; a custom range has none (null).</summary>
    public static string? InsightsRangeToWidgetRange(AdminInsightsRange range) => range switch
    {
        AdminInsightsRange.Days7 => "week",
        AdminInsightsRange.Days30 => "month",
        AdminInsightsRange.Days90 => "quarter",
        AdminInsightsRange.Year1 => "year",
        AdminInsightsRange.All => "all",
        _ => null,
    };

    /// <summary>The window a dashboard Insights widget reads: the whole server in the given zone, no scope (official <c>dashboardInsightsWindow</c>).</summary>
    public static AdminInsightsWindow DashboardWindow(string? widgetRange, string tz)
        => new() { Range = WidgetRangeToInsightsRange(widgetRange), Tz = tz };

    /// <summary>
    /// The Insights link a dashboard card offers (<c>/admin/insights?tab=&lt;tab&gt;&amp;range=&lt;mapped&gt;</c>). The default range is left
    /// out and so is the default tab, as the page itself would write them.
    /// </summary>
    public static AdminRouteRequest InsightsLinkFor(AdminInsightsTab tab, string? widgetRange)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tab != AdminInsightsTabs.Default) query["tab"] = tab.Wire();
        var range = WidgetRangeToInsightsRange(widgetRange);
        if (range != AdminInsightsRanges.Default) query["range"] = range.Wire();
        return new AdminRouteRequest(AdminRoute.Insights, query.Count > 0 ? query : null);
    }

    // ── Time zones, dates and grids ──────────────────────────

    /// <summary>Parses an RFC 3339 instant from the server; false for anything else.</summary>
    public static bool TryParseInstant(string? value, out DateTimeOffset instant) => AdminPlaybackAuditText.TryParseInstant(value, out instant);

    /// <summary>
    /// The browser zone's counterpart: the machine's IANA zone id, or <c>UTC</c> when it has none (official <c>detectBrowserTimezone</c>).
    /// On Windows the id is converted to its IANA name where one exists.
    /// </summary>
    public static string DetectTimezone()
    {
        try
        {
            var local = TimeZoneInfo.Local;
            if (!string.IsNullOrWhiteSpace(local.Id) && (local.Id.Contains('/') || local.Id == "UTC")) return local.Id;
            return TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana) && !string.IsNullOrWhiteSpace(iana) ? iana : "UTC";
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return "UTC";
        }
    }

    /// <summary>
    /// The calendar date (<c>YYYY-MM-DD</c>) an instant falls on in <paramref name="tz"/> (official <c>isoCalendarDateInTz</c>): null for an
    /// unparsable instant, the UTC date for an unknown zone.
    /// </summary>
    public static string? CalendarDateInTz(string? iso, string? tz)
    {
        if (!TryParseInstant(iso, out var instant)) return null;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(tz) ? "UTC" : tz);
            return TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return instant.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <summary>Every UTC calendar day between two <c>YYYY-MM-DD</c> dates, inclusive; empty for a bad or reversed pair; at most 3665 days (official <c>buildDailyUtcGrid</c>).</summary>
    public static IReadOnlyList<string> BuildDailyUtcGrid(string? firstDate, string? lastDate)
    {
        if (!TryParseDate(firstDate, out var start) || !TryParseDate(lastDate, out var end) || end < start) return [];
        var days = new List<string>();
        for (var day = start; day <= end && days.Count < 3665; day = day.AddDays(1))
            days.Add(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return days;
    }

    /// <summary>
    /// Every <c>label_date</c> bucket in <c>[fromIso, toIso)</c> in <paramref name="tz"/> at date_trunc granularity (<c>day</c>, ISO Monday
    /// <c>week</c> or <c>month</c>), so a quiet period is a zero-height column and not a gap (official <c>buildActivityBucketGrid</c>).
    /// At most 4000 buckets; empty when either end is not a date.
    /// </summary>
    public static IReadOnlyList<string> BuildActivityBucketGrid(string? fromIso, string? toIso, string? tz, string? unit)
    {
        var fromDate = CalendarDateInTz(fromIso, tz);
        // The window is half-open: its last day is the day of the last millisecond inside it.
        var inclusiveTo = TryParseInstant(toIso, out var toInstant) ? toInstant.AddMilliseconds(-1).UtcDateTime.ToString("O", CultureInfo.InvariantCulture) : toIso;
        var toDate = CalendarDateInTz(inclusiveTo, tz);
        if (!TryParseDate(fromDate, out var start) || !TryParseDate(toDate, out var end) || end < start) return [];

        if (unit == "week") start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
        else if (unit == "month") start = new DateOnly(start.Year, start.Month, 1);

        var buckets = new List<string>();
        for (var cursor = start; cursor <= end && buckets.Count < 4000;)
        {
            buckets.Add(cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cursor = unit switch
            {
                "week" => cursor.AddDays(7),
                "month" => new DateOnly(cursor.Year, cursor.Month, 1).AddMonths(1),
                _ => cursor.AddDays(1),
            };
        }
        return buckets;
    }

    /// <summary>
    /// The dates a chart draws for a read: the full grid, or the response's own bucket dates when the grid is empty (official fallback in
    /// <c>PlaysOverTimeChart</c>, <c>ActivePeopleChart</c> and <c>InsightsPlaysChart</c>).
    /// </summary>
    public static IReadOnlyList<string> ChartDates(string from, string to, string tz, string unit, IEnumerable<string> bucketLabelDates)
    {
        var grid = BuildActivityBucketGrid(from, to, tz, unit);
        return grid.Count > 0 ? grid : bucketLabelDates.ToArray();
    }

    /// <summary>
    /// "Oct 26, 2026" and "Nov 24, 2026" for a response window <c>[from, to)</c>: calendar dates in the window's own zone, the end being the
    /// last day inside the window (official <c>formatInsightsWindowDates</c>).
    /// </summary>
    public static (string From, string To) FormatWindowDates(string fromIso, string toIso, string tz, CultureInfo? culture = null)
    {
        var last = TryParseInstant(toIso, out var toInstant) ? toInstant.AddMilliseconds(-1).UtcDateTime.ToString("O", CultureInfo.InvariantCulture) : toIso;
        return (FormatShortCalendarDate(CalendarDateInTz(fromIso, tz) ?? fromIso, culture), FormatShortCalendarDate(CalendarDateInTz(last, tz) ?? last, culture));
    }

    /// <summary>"Nov 24, 2026": a calendar date as itself, an instant in local time; a dash for none, the input as sent when unparsable (official <c>formatShortCalendarDate</c>).</summary>
    public static string FormatShortCalendarDate(string? isoOrDate, CultureInfo? culture = null)
    {
        if (string.IsNullOrEmpty(isoOrDate)) return EmDash;
        culture ??= CultureInfo.CurrentCulture;
        if (TryParseDate(isoOrDate, out var date)) return date.ToString("MMM d, yyyy", culture);
        return TryParseInstant(isoOrDate, out var instant) ? instant.ToLocalTime().ToString("MMM d, yyyy", culture) : isoOrDate;
    }

    /// <summary>A local date and time for an instant; a dash for none, the input as sent when unparsable (official <c>formatShortTimestamp</c>).</summary>
    public static string FormatShortTimestamp(string? iso, CultureInfo? culture = null)
    {
        if (string.IsNullOrEmpty(iso)) return EmDash;
        return TryParseInstant(iso, out var instant) ? instant.ToLocalTime().ToString("g", culture ?? CultureInfo.CurrentCulture) : iso;
    }

    /// <summary>
    /// A bucket's axis label from its UTC calendar date: "Nov 1" for a day or week, "Nov 2026" for a month; never shifted by the viewer's zone
    /// (official <c>formatActivityBucketLabel</c>). A dash for a date that does not parse.
    /// </summary>
    public static string FormatBucketLabel(string? labelDate, string? unit, CultureInfo? culture = null)
    {
        if (!TryParseDate(labelDate, out var date)) return EmDash;
        return date.ToString(unit == "month" ? "MMM yyyy" : "MMM d", culture ?? CultureInfo.CurrentCulture);
    }

    // ── Viewing heatmap and weekday / hour totals ────────────

    /// <summary>Which cell value a weekday or hour chart sums.</summary>
    public enum ViewingMetric
    {
        Plays,
        WatchTime,
    }

    private static long CellValue(AdminInsightsHeatmapCell cell, ViewingMetric metric) => metric == ViewingMetric.WatchTime ? cell.WatchSeconds : cell.Plays;

    /// <summary>Totals per weekday, Monday first (index 0). Cells outside 0..6 are ignored.</summary>
    public static long[] WeekdayTotals(IEnumerable<AdminInsightsHeatmapCell> cells, ViewingMetric metric)
    {
        var totals = new long[7];
        foreach (var cell in cells)
            if (cell.DayOfWeek is >= 0 and < 7) totals[cell.DayOfWeek] += CellValue(cell, metric);
        return totals;
    }

    /// <summary>Totals per hour of the day, 0 to 23. Cells outside that range are ignored.</summary>
    public static long[] HourTotals(IEnumerable<AdminInsightsHeatmapCell> cells, ViewingMetric metric)
    {
        var totals = new long[24];
        foreach (var cell in cells)
            if (cell.Hour is >= 0 and < 24) totals[cell.Hour] += CellValue(cell, metric);
        return totals;
    }

    /// <summary>The heat level of a cell (0 empty, then 1 to 4 for up to 25%, 50%, 75% and more of the busiest cell's plays).</summary>
    public static int HeatmapLevel(long plays, long maxPlays)
    {
        if (plays <= 0 || maxPlays <= 0) return 0;
        var ratio = (double)plays / maxPlays;
        if (ratio <= 0.25) return 1;
        if (ratio <= 0.5) return 2;
        if (ratio <= 0.75) return 3;
        return 4;
    }

    /// <summary>Short weekday names, Monday first, in the culture.</summary>
    public static IReadOnlyList<string> WeekdayLabels(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        DayOfWeek[] order = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];
        return order.Select(day => culture.DateTimeFormat.GetAbbreviatedDayName(day)).ToArray();
    }

    /// <summary>Hour labels 0 to 23 on the culture's clock ("9 AM" on a 12-hour clock, "21" on a 24-hour one).</summary>
    public static IReadOnlyList<string> HourLabels(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var twelveHour = culture.DateTimeFormat.ShortTimePattern.Contains('t');
        return Enumerable.Range(0, 24).Select(hour => new DateTime(2024, 1, 1, hour, 0, 0, DateTimeKind.Unspecified).ToString(twelveHour ? "h tt" : "HH", culture)).ToArray();
    }

    /// <summary>The weekday x hour grid of a heatmap: plays and watch seconds per cell, and the busiest cell's plays.</summary>
    public sealed class HeatmapGrid
    {
        private readonly long[] _plays = new long[7 * 24];
        private readonly long[] _watchSeconds = new long[7 * 24];

        /// <summary>The largest plays value of any cell in the read, even one outside the grid (as the official page computes it).</summary>
        public long MaxPlays { get; private set; }

        public long Plays(int dayOfWeek, int hour) => _plays[Index(dayOfWeek, hour)];
        public long WatchSeconds(int dayOfWeek, int hour) => _watchSeconds[Index(dayOfWeek, hour)];
        public int Level(int dayOfWeek, int hour) => HeatmapLevel(Plays(dayOfWeek, hour), MaxPlays);

        private static int Index(int dayOfWeek, int hour) =>
            dayOfWeek is >= 0 and < 7 && hour is >= 0 and < 24 ? dayOfWeek * 24 + hour : throw new ArgumentOutOfRangeException(nameof(hour));

        internal void Set(AdminInsightsHeatmapCell cell)
        {
            if (cell.Plays > MaxPlays) MaxPlays = cell.Plays;
            // A repeated cell replaces the earlier one, as the official keyed map does.
            if (cell.DayOfWeek is >= 0 and < 7 && cell.Hour is >= 0 and < 24)
            {
                _plays[cell.DayOfWeek * 24 + cell.Hour] = cell.Plays;
                _watchSeconds[cell.DayOfWeek * 24 + cell.Hour] = cell.WatchSeconds;
            }
        }
    }

    public static HeatmapGrid BuildHeatmapGrid(IEnumerable<AdminInsightsHeatmapCell> cells)
    {
        var grid = new HeatmapGrid();
        foreach (var cell in cells) grid.Set(cell);
        return grid;
    }

    // ── People paging ────────────────────────────────────────

    /// <summary>
    /// The offset of the next People page, or null when there is none: the pages are empty or have loaded every account (official
    /// <c>getNextPageParam</c>), or the server's offset ceiling is reached.
    /// </summary>
    public static int? NextPeopleOffset(IReadOnlyList<AdminInsightsPeople> loadedPages)
    {
        if (loadedPages.Count == 0) return 0;
        var last = loadedPages[^1];
        long loaded = 0;
        foreach (var page in loadedPages) loaded += page.Accounts.Count;
        if (last.Accounts.Count == 0 || loaded >= last.TotalAccounts || loaded > AdminInsightsApi.PeopleMaxOffset) return null;
        return (int)loaded;
    }

    /// <summary>Every loaded account once, in page order; a repeated account (the data moved between reads) keeps its first row.</summary>
    public static IReadOnlyList<AdminInsightsPersonAccount> MergePeopleAccounts(IEnumerable<AdminInsightsPeople> pages)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var merged = new List<AdminInsightsPersonAccount>();
        foreach (var page in pages)
            foreach (var account in page.Accounts)
                if (seen.Add(account.UserId)) merged.Add(account);
        return merged;
    }

    /// <summary>
    /// A People header click: the active column flips its direction, a new column starts descending (official <c>handleSort</c>).
    /// </summary>
    public static (AdminInsightsPeopleSort Sort, bool Descending) NextPeopleSort(AdminInsightsPeopleSort current, bool descending, AdminInsightsPeopleSort clicked)
        => clicked == current ? (current, !descending) : (clicked, true);

    // ── Coverage notices ─────────────────────────────────────

    /// <summary>"Exact watch time is available for N of M sessions": some sessions were recorded before watch time was measured (Playback and People).</summary>
    public static bool ShowWatchTimeNotice(AdminInsightsCoverage coverage) =>
        coverage.TotalSessions > 0 && coverage.MeasuredSessions < coverage.TotalSessions;

    /// <summary>The Devices banner about sessions without route facts.</summary>
    public enum RouteFactsNoticeKind
    {
        /// <summary>No banner: every session has them, or there are no sessions.</summary>
        Hidden,
        /// <summary><c>web.admin.insights.coverage.routeFactsNone</c>: none of the sessions have them.</summary>
        None,
        /// <summary><c>web.admin.insights.coverage.routeFactsPartial</c>: some do; its arguments are <c>recorded</c>, <c>total</c>, <c>total_count</c> and <c>since</c>.</summary>
        Partial,
    }

    public static RouteFactsNoticeKind RouteFactsNotice(AdminInsightsCoverage coverage) =>
        coverage.TotalSessions <= 0 ? RouteFactsNoticeKind.Hidden
        : coverage.RouteFactsSessions == 0 ? RouteFactsNoticeKind.None
        : coverage.RouteFactsSessions < coverage.TotalSessions ? RouteFactsNoticeKind.Partial
        : RouteFactsNoticeKind.Hidden;

    /// <summary>Bandwidth says its history starts later than the window when the oldest metric is after the window's start.</summary>
    public static bool ShowMetricsStartNotice(AdminInsightsBandwidth data) =>
        TryParseInstant(data.OldestMetricAt, out var oldest) && TryParseInstant(data.From, out var from) && from < oldest;

    // ── Formatters ───────────────────────────────────────────

    /// <summary>A [0, 1] ratio as a whole or single-decimal percentage without the sign: one decimal under 10%, clamped to 0..100, "0" for none (official <c>formatRatePercentNumber</c>).</summary>
    public static string FormatRatePercentNumber(double? rate, CultureInfo? culture = null)
    {
        if (rate is not { } value || double.IsNaN(value) || double.IsInfinity(value)) return "0";
        var percent = Math.Max(0, Math.Min(100, value * 100));
        var decimals = percent is > 0 and < 10 ? 1 : 0;
        return Math.Round(percent, decimals, MidpointRounding.AwayFromZero).ToString("N" + decimals, culture ?? CultureInfo.CurrentCulture);
    }

    /// <summary>The value of a coverage bar: a played share (0..1) as a percentage rounded to one decimal, against a fixed maximum of 100.</summary>
    public static double CoverageBarValue(double playedShare) => Math.Round(playedShare * 1000, MidpointRounding.AwayFromZero) / 10;

    /// <summary>A count message such as "3 plays" (<c>web.admin.insights.playCount</c> and kin) with the number in the interface locale's digits.</summary>
    public static string FormatCount(string key, long count) => AdminInsightsText.T(key, AdminInsightsText.Count(count));

    public static string PlayCount(long count) => FormatCount("web.admin.insights.playCount", count);
    public static string SessionCount(long count) => FormatCount("web.admin.insights.sessionCount", count);
    public static string ViewerCount(long count) => FormatCount("web.admin.insights.viewerCount", count);
    public static string EpisodeCount(long count) => FormatCount("web.admin.insights.episodeCount", count);
    public static string TitleCount(long count) => FormatCount("web.admin.insights.library.titleCount", count);

    /// <summary>
    /// A watch time such as "42m" or "12.5h" (the dashboard's <c>formatWatchTime</c>, which every Insights screen uses). Sub-hour totals stay in minutes.
    /// </summary>
    public static string WatchTime(long? seconds) => DashboardFormat.WatchTime(seconds);

    /// <summary>"Arrival (2016)", or the removed-title label when the catalog has no title for the row (official <c>formatInsightsTitle</c>).</summary>
    public static string FormatTitle(string? title, long? year)
    {
        var text = string.IsNullOrWhiteSpace(title) ? AdminInsightsText.T("web.admin.insights.titles.unknownTitle") : title.Trim();
        return year is > 0 and var y
            ? AdminInsightsText.T("web.admin.insights.titles.titleWithYear", ("title", text), ("year", y.ToString(CultureInfo.InvariantCulture)))
            : text;
    }

    /// <summary>"Profile ab12cd34" for a profile the account's store no longer lists (the first eight characters of its id).</summary>
    public static string UnnamedProfileLabel(string profileId)
        => AdminInsightsText.T("web.admin.insights.people.unnamedProfile", ("id", profileId.Length > 8 ? profileId[..8] : profileId));

    /// <summary>The hardware acceleration label (official <c>formatHwAccelLabel</c>): known backends by name, <c>none</c> as software, unknown as such, anything else upper-cased.</summary>
    public static string HwAccelLabel(string? key)
    {
        switch ((key ?? "").ToLowerInvariant())
        {
            case "software":
            case "none":
                return AdminInsightsText.T("web.admin.insights.devices.hwAccel.software");
            case "unknown":
            case "":
                return AdminInsightsText.T("web.admin.insights.devices.hwAccel.unknown");
            case "vaapi": return "VAAPI";
            case "qsv": return "QSV";
            case "nvenc": return "NVENC";
            case "videotoolbox": return "VideoToolbox";
            default: return key!.ToUpperInvariant();
        }
    }

    /// <summary>The play method class of a history row (official <c>normalizeMethodKey</c>): <c>direct</c>, <c>remux</c>, <c>transcode</c> or <c>unknown</c>.</summary>
    public static string NormalizeMethodKey(string? playMethod) => playMethod switch
    {
        "direct" or "direct_play" => "direct",
        "remux" or "direct_stream" => "remux",
        "transcode" => "transcode",
        _ => "unknown",
    };

    // ── History drilldown ────────────────────────────────────

    /// <summary>
    /// The Playback History location an Insights number drills into (official <c>buildHistoryDrilldownHref</c>): only the facets that are set
    /// are included, so no facet means plain <c>/admin/history</c>. <c>unknown</c> as a client or platform matches rows with none.
    /// <c>subtitle_burn_in</c> is written as <c>true</c> or <c>false</c> (the history page only acts on <c>true</c>).
    /// </summary>
    public static AdminRouteRequest BuildHistoryDrilldown(AdminInsightsHistoryDrilldown drilldown)
    {
        var query = new Dictionary<string, string>(StringComparer.Ordinal);
        void Set(string key, string? value) { if (!string.IsNullOrEmpty(value)) query[key] = value; }
        // The official builder tests truthiness of a numeric id, so a zero id is no id.
        void SetId(string key, string? value) { if (value != "0") Set(key, value); }
        SetId("user_id", drilldown.UserId);
        Set("profile_id", drilldown.ProfileId);
        Set("media_item_id", drilldown.MediaItemId);
        Set("series_id", drilldown.SeriesId);
        SetId("library_id", drilldown.LibraryId);
        Set("play_method", drilldown.PlayMethod);
        Set("client_name", drilldown.ClientName);
        Set("device_platform", drilldown.DevicePlatform);
        Set("source_video_codec", drilldown.SourceVideoCodec);
        Set("adaptation_reason", drilldown.AdaptationReason);
        if (drilldown.SubtitleBurnIn is { } burnIn) query["subtitle_burn_in"] = burnIn ? "true" : "false";
        Set("stream_location", drilldown.StreamLocation);
        Set("started_after", drilldown.StartedAfter);
        Set("started_before", drilldown.StartedBefore);
        return new AdminRouteRequest(AdminRoute.PlaybackHistory, query.Count > 0 ? query : null);
    }
}

/// <summary>The facets of a drilldown from Insights into Playback History (official <c>HistoryDrilldownParams</c>). Ids are decimal strings (an unset or zero id is omitted).</summary>
public sealed record AdminInsightsHistoryDrilldown
{
    public string? UserId { get; init; }
    public string? ProfileId { get; init; }
    public string? MediaItemId { get; init; }
    public string? SeriesId { get; init; }
    public string? LibraryId { get; init; }
    public string? PlayMethod { get; init; }
    public string? ClientName { get; init; }
    public string? DevicePlatform { get; init; }
    public string? SourceVideoCodec { get; init; }
    public string? AdaptationReason { get; init; }
    public bool? SubtitleBurnIn { get; init; }
    /// <summary><c>local</c> or <c>remote</c>.</summary>
    public string? StreamLocation { get; init; }
    /// <summary>An RFC 3339 instant: a response's <c>from</c> narrows history to the window an Insights number was counted in.</summary>
    public string? StartedAfter { get; init; }
    public string? StartedBefore { get; init; }
}
