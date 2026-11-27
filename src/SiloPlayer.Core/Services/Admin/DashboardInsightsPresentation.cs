using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>One bar-list row of an Insights leaderboard or coverage list; the widget adds the link actions.</summary>
/// <param name="ItemId">A catalog item the label opens (<c>/item/{id}</c>), or null (a removed title has nowhere to go).</param>
/// <param name="HistoryHref">An admin href the label opens (a profile's history), or null.</param>
public sealed record DashboardInsightsRow(string Id, string Label, double Value, string? Secondary, string? ItemId = null, string? HistoryHref = null);

/// <summary>What an Insights chart widget draws, shaped from one read (official <c>InsightsWidgets.tsx</c> and the <c>components/admin/insights</c> charts, c59798116).</summary>
public sealed record DashboardInsightsColumns(IReadOnlyList<DashboardChartColumn> Columns, IReadOnlyList<string> SeriesLabels);

public sealed record DashboardInsightsPeopleSeries(
    IReadOnlyList<DashboardChartPoint> Profiles, IReadOnlyList<DashboardChartPoint> Accounts, string StartLabel, string EndLabel);

/// <summary>
/// Pure shaping of the Insights reads into chart columns, points and bar rows, so the dashboard widgets stay thin and the numbers they
/// show are testable without a window. Every method returns null (or no rows) for "nothing to draw", which the widget words as its
/// own empty state, never as a zero-filled chart.
/// </summary>
public static class DashboardInsightsPresentation
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The UTC midnight a bucket's <c>label_date</c> anchors at, so columns stay evenly spaced and never shift a day in another zone.</summary>
    public static DateTimeOffset? BucketTime(string? labelDate)
        => DateOnly.TryParseExact(labelDate, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date)
            ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

    /// <summary>The label of a column anchored by <see cref="BucketTime"/>: "Nov 1", or "Nov 2026" for a month.</summary>
    public static string BucketLabel(DateTimeOffset time, string? unit)
        => AdminInsightsRules.FormatBucketLabel(time.UtcDateTime.ToString("yyyy-MM-dd", Invariant), unit);

    private static IReadOnlyList<string> Dates(AdminInsightsActivity data)
        => AdminInsightsRules.ChartDates(data.From, data.To, data.Tz, data.BucketUnit, data.Buckets.Select(bucket => bucket.LabelDate));

    private static Dictionary<string, AdminInsightsActivityBucket> ByDate(AdminInsightsActivity data)
    {
        var map = new Dictionary<string, AdminInsightsActivityBucket>(StringComparer.Ordinal);
        foreach (var bucket in data.Buckets) map[bucket.LabelDate] = bucket;
        return map;
    }

    /// <summary>The series labels of the Playback over time chart for a view (<c>method</c>, <c>media</c> or <c>watch_time</c>; anything else is method).</summary>
    public static IReadOnlyList<string> PlaysSeriesLabels(string? view) => view switch
    {
        DashboardRegistry.InsightsViewWatchTime => [AdminInsightsText.T("web.admin.insights.activity.volume.metricWatchTime")],
        DashboardRegistry.InsightsViewMedia =>
        [
            AdminInsightsText.T("web.admin.insights.activity.volume.seriesMovies"),
            AdminInsightsText.T("web.admin.insights.activity.volume.seriesEpisodes"),
            AdminInsightsText.T("web.admin.insights.activity.volume.seriesOther"),
        ],
        _ =>
        [
            DashboardText.T("web.admin.dashboard.widgets.playbackActivity.series.directPlay"),
            DashboardText.T("web.admin.dashboard.widgets.playbackActivity.series.directStream"),
            DashboardText.T("web.admin.dashboard.widgets.playbackActivity.series.transcode"),
        ],
    };

    /// <summary>
    /// Plays per day, week or month, split by play method or media type, or the watch time. Quiet buckets are filled across
    /// <c>[from, to)</c> so columns stay evenly spaced. Null when the window has none of the metric (a window can have sessions and no plays).
    /// </summary>
    public static DashboardInsightsColumns? PlaysColumns(AdminInsightsActivity data, string? view)
    {
        var watchTime = view == DashboardRegistry.InsightsViewWatchTime;
        var hasMetric = watchTime ? data.Buckets.Any(bucket => bucket.WatchSeconds > 0) : data.Buckets.Any(bucket => bucket.Plays > 0);
        if (!hasMetric) return null;
        var byDate = ByDate(data);
        var columns = new List<DashboardChartColumn>();
        foreach (var date in Dates(data))
        {
            if (BucketTime(date) is not { } time) continue;
            byDate.TryGetValue(date, out var bucket);
            IReadOnlyList<double> segments = watchTime ? [bucket?.WatchSeconds ?? 0]
                : view == DashboardRegistry.InsightsViewMedia ? [bucket?.Movies ?? 0, bucket?.Episodes ?? 0, bucket?.OtherMedia ?? 0]
                : [bucket?.Direct ?? 0, bucket?.Remux ?? 0, bucket?.Transcode ?? 0];
            columns.Add(new DashboardChartColumn(time, segments));
        }
        return columns.Count == 0 ? null : new DashboardInsightsColumns(columns, PlaysSeriesLabels(view));
    }

    /// <summary>Which axis a viewing-time chart totals the heatmap by.</summary>
    public enum ViewingAxis { Weekday, Hour }

    /// <summary>
    /// Plays or watch time by day of the week (Monday first) or hour of the day: the heatmap's rows or columns added up. The column time
    /// is the index in milliseconds since the epoch (<see cref="ViewingIndex"/> reads it back), since the columns are indexed, not timed.
    /// Null when every total is zero.
    /// </summary>
    public static DashboardInsightsColumns? ViewingColumns(IEnumerable<AdminInsightsHeatmapCell> cells, ViewingAxis axis, string? view)
    {
        var metric = view == DashboardRegistry.InsightsViewWatchTime ? AdminInsightsRules.ViewingMetric.WatchTime : AdminInsightsRules.ViewingMetric.Plays;
        var totals = axis == ViewingAxis.Weekday ? AdminInsightsRules.WeekdayTotals(cells, metric) : AdminInsightsRules.HourTotals(cells, metric);
        if (totals.All(total => total == 0)) return null;
        var label = AdminInsightsText.T(metric == AdminInsightsRules.ViewingMetric.WatchTime
            ? "web.admin.insights.activity.volume.metricWatchTime" : "web.admin.insights.activity.volume.metricPlays");
        return new DashboardInsightsColumns(
            totals.Select((total, index) => new DashboardChartColumn(DateTimeOffset.FromUnixTimeMilliseconds(index), [total])).ToArray(), [label]);
    }

    public static int ViewingIndex(DateTimeOffset columnTime) => (int)columnTime.ToUnixTimeMilliseconds();

    /// <summary>The tick label of an indexed viewing column: a short weekday or an hour on the culture's clock.</summary>
    public static string ViewingLabel(ViewingAxis axis, int index, CultureInfo? culture = null)
    {
        var labels = axis == ViewingAxis.Weekday ? AdminInsightsRules.WeekdayLabels(culture) : AdminInsightsRules.HourLabels(culture);
        return index >= 0 && index < labels.Count ? labels[index] : "";
    }

    /// <summary>
    /// Distinct profiles (the line) and accounts (the overlay) that started watching in each bucket. A quiet bucket is a real zero, not a
    /// gap. Null when no bucket has an active profile.
    /// </summary>
    public static DashboardInsightsPeopleSeries? ActivePeople(AdminInsightsActivity data)
    {
        if (!data.Buckets.Any(bucket => (bucket.ActiveProfiles ?? 0) > 0)) return null;
        var byDate = ByDate(data);
        var dates = Dates(data).Where(date => BucketTime(date) != null).ToArray();
        if (dates.Length == 0) return null;
        DashboardChartPoint Point(string date, Func<AdminInsightsActivityBucket, long?> pick)
            => new(BucketTime(date)!.Value, byDate.TryGetValue(date, out var bucket) ? pick(bucket) ?? 0 : 0);
        return new DashboardInsightsPeopleSeries(
            dates.Select(date => Point(date, bucket => bucket.ActiveProfiles)).ToArray(),
            dates.Select(date => Point(date, bucket => bucket.ActiveAccounts)).ToArray(),
            AdminInsightsRules.FormatBucketLabel(dates[0], data.BucketUnit),
            AdminInsightsRules.FormatBucketLabel(dates[^1], data.BucketUnit));
    }

    /// <summary>
    /// Most-played movies and series together: both lists merged, most plays first and watch time as the tie-break, the first
    /// <paramref name="limit"/>. A show appears once with the plays of all its episodes. A removed title is worded as such and has no link.
    /// </summary>
    public static IReadOnlyList<DashboardInsightsRow> TopTitles(AdminInsightsTitles? data, int limit = AdminInsightsLimits.DashboardTitles)
        => data == null ? [] : data.Movies.Concat(data.Shows)
            .OrderByDescending(row => row.Plays).ThenByDescending(row => row.WatchSeconds).Take(limit)
            .Select(row => new DashboardInsightsRow(row.ContentId,
                string.IsNullOrEmpty(row.Title) ? AdminInsightsText.T("web.admin.insights.titles.unknownTitle") : row.Title,
                row.Plays, DashboardFormat.WatchTime(row.WatchSeconds), ItemId: string.IsNullOrEmpty(row.Title) ? null : row.ContentId))
            .ToArray();

    public static IReadOnlyList<DashboardInsightsRow> LegacyTopTitles(DashboardTopActivity data)
        => (data.Titles ?? []).Take(AdminInsightsLimits.DashboardTitles).Select(title => new DashboardInsightsRow(title.MediaItemId,
            string.IsNullOrEmpty(title.Title) ? title.MediaItemId : title.Title, title.Plays, DashboardFormat.WatchTime(title.TotalSeconds),
            ItemId: string.IsNullOrEmpty(title.MediaItemId) ? null : title.MediaItemId)).ToArray();

    /// <summary>The most active household profiles, by plays, at most <paramref name="limit"/>: "Profile · account", linking to that profile's history.</summary>
    public static IReadOnlyList<DashboardInsightsRow> TopProfiles(AdminInsightsPeople? data, int limit = AdminInsightsLimits.DashboardProfiles)
        => data == null ? [] : data.TopProfiles.Take(limit).Select(profile =>
            ProfileRow(profile.UserId, profile.Username, profile.ProfileId, profile.ProfileName, profile.Plays, profile.WatchSeconds)).ToArray();

    public static IReadOnlyList<DashboardInsightsRow> LegacyTopProfiles(DashboardTopActivity data)
        => (data.Profiles ?? []).Take(AdminInsightsLimits.DashboardProfiles).Select(profile =>
            ProfileRow(profile.UserId, profile.Username, profile.ProfileId, profile.ProfileName, profile.Plays, profile.TotalSeconds)).ToArray();

    private static DashboardInsightsRow ProfileRow(string userId, string? username, string? profileId, string? profileName, long plays, long seconds)
    {
        var account = !string.IsNullOrEmpty(username) ? username : DashboardText.T("web.admin.dashboard.widgets.topProfiles.userFallback", ("id", userId));
        var name = !string.IsNullOrEmpty(profileName) ? profileName : !string.IsNullOrEmpty(profileId) ? profileId : account;
        var history = "/admin/history?user_id=" + Uri.EscapeDataString(userId)
            + (string.IsNullOrEmpty(profileId) ? "" : "&profile_id=" + Uri.EscapeDataString(profileId));
        return new DashboardInsightsRow($"{userId}:{profileId}", name == account ? account : $"{name} · {account}",
            plays, DashboardFormat.WatchTime(seconds), HistoryHref: history);
    }

    /// <summary>
    /// Each movie and TV library with the share of its titles played in the window: the bar is the played share against a fixed 100, so
    /// a library nobody watches reads as a short bar however large it is.
    /// </summary>
    public static IReadOnlyList<DashboardInsightsRow> LibraryCoverage(AdminInsightsLibrary? data)
        => data == null ? [] : data.Libraries.Select(library => new DashboardInsightsRow(library.LibraryId, library.Name,
            AdminInsightsRules.CoverageBarValue(library.PlayedShare),
            AdminInsightsText.T("web.admin.insights.library.coverage.detail",
                ("played", DashboardFormat.Integer(library.PlayedTitles)), ("titles", DashboardFormat.Integer(library.Titles)),
                ("titles_count", library.Titles), ("never", DashboardFormat.Integer(library.NeverStarted))))).ToArray();

    /// <summary>"12.5% played": a coverage value with at most one decimal.</summary>
    public static string CoverageValueText(double value)
        => AdminInsightsText.T("web.admin.insights.library.coverage.playedShare",
            ("value", value.ToString("#,0.#", CultureInfo.CurrentCulture)));
}
