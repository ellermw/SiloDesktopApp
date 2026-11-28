using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A KPI card of an Insights tab (official <c>InsightKpiCard</c>).</summary>
public sealed record AdminInsightsKpi(string Label, string Value, string? Subtext = null, ActivityTone? Accent = null);

/// <summary>
/// One row of an Insights bar list. <see cref="TitleId"/> opens the title panel, <see cref="LibraryId"/> narrows the page to that library,
/// <see cref="History"/> drills into Playback History.
/// </summary>
public sealed record AdminInsightsBarRow(string Id, string Label, double Value, string? Secondary = null, string? TitleId = null, long? LibraryId = null,
    AdminRouteRequest? History = null);

/// <summary>Number formatting every Insights tab shares (the official <c>toLocaleString(formatLocale())</c>).</summary>
internal static class AdminInsightsNumbers
{
    public static string Count(long value) => DashboardFormat.Integer(value);
    public static string Percent(double? rate) => AdminInsightsText.T("web.admin.insights.percent", ("value", AdminInsightsRules.FormatRatePercentNumber(rate)));
}

/// <summary>
/// The Playback tab (official <c>PlaybackTab.tsx</c>, c59798116) shaped from one Activity read: notice, KPI cards, and the series of the
/// peak-concurrent and end-reason cards. The volume, heatmap, weekday, hour and active-people cards reuse
/// <see cref="DashboardInsightsPresentation"/>, which is what the dashboard widgets of the same charts draw.
/// </summary>
public static class AdminInsightsPlaybackPresentation
{
    public enum VolumeMetric { Plays, WatchTime }
    public enum VolumeSplit { Method, Media }
    public enum PeakView { Lines, Stacked, Total }

    /// <summary>The Playback over time chart's view: watch time, or plays split by method or media type.</summary>
    public static string VolumeView(VolumeMetric metric, VolumeSplit split) => metric == VolumeMetric.WatchTime
        ? DashboardRegistry.InsightsViewWatchTime
        : split == VolumeSplit.Media ? DashboardRegistry.InsightsViewMedia : DashboardRegistry.InsightsViewMethod;

    /// <summary>The viewing-time chart's view key for a plays or watch-time switch.</summary>
    public static string MetricView(bool watchTime) => watchTime ? DashboardRegistry.InsightsViewWatchTime : DashboardRegistry.InsightsViewPlays;

    /// <summary>"Exact watch time is available for N of M sessions…": shown when some sessions were recorded before watch time was measured.</summary>
    public static string? WatchTimeNotice(AdminInsightsActivity data)
        => AdminInsightsRules.ShowWatchTimeNotice(data.Coverage) ? AdminInsightsText.T("web.admin.insights.coverage.watchTimePartial", MeasuredArguments(data.Coverage)) : null;

    private static (string, object?)[] MeasuredArguments(AdminInsightsCoverage coverage) =>
    [
        ("measured", AdminInsightsNumbers.Count(coverage.MeasuredSessions)), ("total", AdminInsightsNumbers.Count(coverage.TotalSessions)),
        ("total_count", coverage.TotalSessions),
    ];

    public static IReadOnlyList<AdminInsightsKpi> Kpis(AdminInsightsActivity data)
    {
        var summary = data.Summary;
        var partial = AdminInsightsRules.ShowWatchTimeNotice(data.Coverage);
        return
        [
            new(AdminInsightsText.T("web.admin.insights.activity.kpi.plays"), AdminInsightsNumbers.Count(summary.Plays),
                AdminInsightsText.T("web.admin.insights.activity.kpi.playsBreakdown",
                    ("movies", AdminInsightsNumbers.Count(summary.Movies)), ("movie_count", summary.Movies),
                    ("episodes", AdminInsightsNumbers.Count(summary.Episodes)), ("episode_count", summary.Episodes))),
            new(AdminInsightsText.T("web.admin.insights.activity.kpi.watchTime"), DashboardFormat.WatchTime(summary.WatchSeconds),
                partial ? AdminInsightsText.T("web.admin.insights.activity.kpi.watchTimeMeasured", MeasuredArguments(data.Coverage))
                    : AdminInsightsRules.SessionCount(data.Coverage.TotalSessions)),
            new(AdminInsightsText.T("web.admin.insights.activity.kpi.completionRate"), AdminInsightsNumbers.Percent(summary.CompletionRate),
                AdminInsightsText.T("web.admin.insights.activity.kpi.completionDetail",
                    ("completed", AdminInsightsNumbers.Count(summary.CompletedPlays)), ("total", AdminInsightsNumbers.Count(summary.Plays))),
                ActivityTone.Success),
            new(AdminInsightsText.T("web.admin.insights.activity.kpi.peakStreams"), AdminInsightsNumbers.Count(summary.PeakConcurrentStreams),
                string.IsNullOrEmpty(summary.PeakConcurrentAt)
                    ? AdminInsightsText.T("web.admin.insights.activity.kpi.peakStreamsNone")
                    : AdminInsightsText.T("web.admin.insights.activity.kpi.peakStreamsAt", ("date", AdminInsightsRules.FormatShortTimestamp(summary.PeakConcurrentAt))),
                ActivityTone.Info),
        ];
    }

    /// <summary>Peaks are server-wide, so a library or account filter does not apply to them: the card says so.</summary>
    public static bool ShowsPeakScopeNotice(AdminInsightsFilterState filters) => filters.LibraryId != null || filters.UserId != null;

    /// <summary>The Peak concurrent streams chart: the daily total with the per-method lines or stacked bands, on the UTC day grid.</summary>
    public sealed record PeakSeries(
        IReadOnlyList<DashboardChartPoint> Points,
        IReadOnlyList<(string Label, IReadOnlyList<DashboardChartPoint> Points, int Slot)> Overlays,
        IReadOnlyList<DashboardChartBand> Bands,
        (string Start, string End) EdgeLabels,
        string SeriesLabel,
        bool NeutralTotal);

    /// <summary>Null when the window has no peak data (the card shows its empty line).</summary>
    public static PeakSeries? Peak(AdminInsightsActivity data, PeakView view)
    {
        var raw = data.PeakStreams;
        if (raw.Count == 0) return null;
        var byDate = new Dictionary<string, AdminInsightsPeakStreamsPoint>(StringComparer.Ordinal);
        foreach (var point in raw) byDate[point.Date] = point;
        var first = raw[0].Date;
        var last = raw[^1].Date;
        var grid = AdminInsightsRules.BuildDailyUtcGrid(first, last);
        IReadOnlyList<DashboardChartPoint> Points(Func<AdminInsightsPeakStreamsPoint, long> pick) => grid
            .Select(day => new DashboardChartPoint(DayNoon(day), byDate.TryGetValue(day, out var point) ? pick(point) : null)).ToArray();
        var edges = (AdminInsightsRules.FormatShortCalendarDate(first), AdminInsightsRules.FormatShortCalendarDate(last));
        var total = Points(point => point.PeakStreams);
        string Method(string key) => DashboardText.T("web.admin.dashboard.widgets.playbackActivity.series." + key);
        if (view == PeakView.Lines)
            return new(total,
                [
                    (Method("directPlay"), Points(point => point.PeakDirect), 0),
                    (Method("directStream"), Points(point => point.PeakRemux), 1),
                    (Method("transcode"), Points(point => point.PeakTranscode), 2),
                ], [], edges, DashboardText.T("web.admin.dashboard.chart.total"), NeutralTotal: true);
        var seriesLabel = DashboardText.T("web.admin.dashboard.widgets.concurrentStreams.seriesLabel");
        if (view == PeakView.Stacked)
            return new(total, [],
                [
                    new("direct", Method("directPlay"), 0, false, Points(point => point.PeakStreamsDirect)),
                    new("remux", Method("directStream"), 1, false, Points(point => point.PeakStreamsRemux)),
                    new("transcode", Method("transcode"), 2, false, Points(point => point.PeakStreamsTranscode)),
                ], edges, seriesLabel, NeutralTotal: false);
        return new(total, [], [], edges, seriesLabel, NeutralTotal: false);
    }

    /// <summary>The noon UTC of a calendar day: where a daily point sits on the line chart (official <c>Date.parse(day T12:00:00Z)</c>).</summary>
    public static DateTimeOffset DayNoon(string day)
        => DateTimeOffset.ParseExact(day + "T12:00:00Z", "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>The tick and tooltip label of a peak point (official <c>formatShortCalendarDate</c> of the day).</summary>
    public static string PeakLabel(DateTimeOffset time) => AdminInsightsRules.FormatShortCalendarDate(time.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    private static readonly IReadOnlyDictionary<string, string> EndReasonLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["stopped"] = "web.admin.history.endReason.stopped",
        ["terminated"] = "web.admin.history.endReason.terminated",
        ["expired"] = "web.admin.history.endReason.expired",
        ["aborted"] = "web.admin.history.endReason.aborted",
        ["replaced"] = "web.admin.history.endReason.replaced",
    };

    /// <summary>Sessions by end reason with their share; a reason the server adds later is "Not recorded".</summary>
    public static IReadOnlyList<AdminInsightsBarRow> EndReasons(IReadOnlyList<AdminInsightsLabeledCount> reasons)
    {
        var total = reasons.Sum(reason => reason.Sessions);
        return reasons.Select(reason => new AdminInsightsBarRow(reason.Key,
            EndReasonLabels.TryGetValue(reason.Key, out var key) ? AdminPlaybackAuditText.T(key) : AdminInsightsText.T("web.admin.insights.activity.endReasons.unknown"),
            reason.Sessions,
            AdminInsightsNumbers.Percent(total > 0 ? (double)reason.Sessions / total : 0))).ToArray();
    }
}

/// <summary>The Titles tab (official <c>TitlesTab.tsx</c>): every list is formatted by the sort it was ranked with, not the toggle's.</summary>
public static class AdminInsightsTitlesPresentation
{
    public static IReadOnlyList<AdminInsightsTitleSort> Sorts { get; } = [AdminInsightsTitleSort.Plays, AdminInsightsTitleSort.WatchTime, AdminInsightsTitleSort.Viewers];

    /// <summary>The sort to format rows by: the one the response was ranked by, else the toggle's (official <c>data?.sort ?? sort</c>).</summary>
    public static AdminInsightsTitleSort ShownSort(AdminInsightsTitles? data, AdminInsightsTitleSort selected)
        => data == null || string.IsNullOrEmpty(data.Sort) ? selected : AdminInsightsTitleSorts.Parse(data.Sort);

    public static string FormatMetric(AdminInsightsTitleSort shown, double value)
    {
        var count = (long)Math.Round(value);
        return shown switch
        {
            AdminInsightsTitleSort.WatchTime => DashboardFormat.WatchTime(count),
            AdminInsightsTitleSort.Viewers => AdminInsightsRules.ViewerCount(count),
            _ => AdminInsightsRules.PlayCount(count),
        };
    }

    public static double Metric(AdminInsightsTitleSort shown, long plays, long viewers, long watchSeconds) => shown switch
    {
        AdminInsightsTitleSort.WatchTime => watchSeconds,
        AdminInsightsTitleSort.Viewers => viewers,
        _ => plays,
    };

    /// <summary>The two metrics the ranking does not show, after the episode count of a series.</summary>
    public static string Secondary(AdminInsightsTitleSort shown, long plays, long viewers, long watchSeconds, long episodes = 0)
    {
        var parts = new List<string>(4);
        if (episodes != 0) parts.Add(AdminInsightsRules.EpisodeCount(episodes));
        if (shown != AdminInsightsTitleSort.Plays) parts.Add(AdminInsightsRules.PlayCount(plays));
        if (shown != AdminInsightsTitleSort.Viewers) parts.Add(AdminInsightsRules.ViewerCount(viewers));
        if (shown != AdminInsightsTitleSort.WatchTime) parts.Add(DashboardFormat.WatchTime(watchSeconds));
        return string.Join(" · ", parts);
    }

    public static IReadOnlyList<AdminInsightsBarRow> TitleRows(IReadOnlyList<AdminInsightsTitleRow> rows, AdminInsightsTitleSort shown)
        => rows.Select(row => new AdminInsightsBarRow(row.ContentId, AdminInsightsRules.FormatTitle(row.Title, row.Year),
            Metric(shown, row.Plays, row.Viewers, row.WatchSeconds), Secondary(shown, row.Plays, row.Viewers, row.WatchSeconds, row.Episodes),
            TitleId: row.ContentId)).ToArray();

    /// <summary>A library row narrows the whole page to that library when clicked.</summary>
    public static IReadOnlyList<AdminInsightsBarRow> LibraryRows(IReadOnlyList<AdminInsightsLibraryRow> rows, AdminInsightsTitleSort shown)
        => rows.Select(row => new AdminInsightsBarRow(row.LibraryId, row.Name, Metric(shown, row.Plays, row.Viewers, row.WatchSeconds),
            Secondary(shown, row.Plays, row.Viewers, row.WatchSeconds), LibraryId: AdminInsightsFilters.ParsePositiveId(row.LibraryId))).ToArray();

    /// <summary>Titles left unfinished: the bar is the share of plays not completed, whatever the sort, so a longer bar is a title more people gave up on.</summary>
    public static IReadOnlyList<AdminInsightsBarRow> LeastFinishedRows(IReadOnlyList<AdminInsightsTitleRow> rows)
        => rows.Select(row => new AdminInsightsBarRow(row.ContentId, AdminInsightsRules.FormatTitle(row.Title, row.Year),
            row.Plays > 0 ? Math.Round((1 - (double)row.CompletedPlays / row.Plays) * 100, MidpointRounding.AwayFromZero) : 0,
            AdminInsightsText.T("web.admin.insights.activity.kpi.completionDetail",
                ("completed", AdminInsightsNumbers.Count(row.CompletedPlays)), ("total", AdminInsightsNumbers.Count(row.Plays))),
            TitleId: row.ContentId)).ToArray();

    public static string UnfinishedShare(double value)
        => AdminInsightsText.T("web.admin.insights.titles.leastFinished.unfinishedShare", ("value", value.ToString("#,0.###", CultureInfo.CurrentCulture)));
}

/// <summary>The Library tab (official <c>LibraryTab.tsx</c>).</summary>
public static class AdminInsightsLibraryPresentation
{
    public static IReadOnlyList<AdminInsightsKpi> Kpis(AdminInsightsLibrary data)
    {
        var summary = data.Summary;
        return
        [
            new(AdminInsightsText.T("web.admin.insights.library.kpi.played"), AdminInsightsNumbers.Percent(summary.PlayedShare),
                AdminInsightsText.T("web.admin.insights.library.kpi.playedDetail", ("played", AdminInsightsNumbers.Count(summary.PlayedTitles)),
                    ("titles", AdminInsightsNumbers.Count(summary.Titles)), ("titles_count", summary.Titles)), ActivityTone.Success),
            new(AdminInsightsText.T("web.admin.insights.library.kpi.neverStarted"), AdminInsightsNumbers.Count(summary.NeverStarted),
                string.IsNullOrEmpty(data.OldestHistoryAt) ? null
                    : AdminInsightsText.T("web.admin.insights.library.kpi.neverStartedSince", ("date", AdminInsightsRules.FormatShortCalendarDate(data.OldestHistoryAt)))),
            new(AdminInsightsText.T("web.admin.insights.library.kpi.added"), AdminInsightsNumbers.Count(summary.AddedTitles),
                AdminInsightsText.T("web.admin.insights.library.kpi.addedDetail", ("played", AdminInsightsNumbers.Count(summary.AddedPlayed))), ActivityTone.Info),
            new(AdminInsightsText.T("web.admin.insights.library.kpi.firstPlay"),
                summary.MedianDaysToFirstPlay is { } days
                    ? AdminInsightsText.T("web.admin.insights.library.kpi.firstPlayDays", ("days", days.ToString("#,0.###", CultureInfo.CurrentCulture)))
                    : "—",
                AdminInsightsText.T("web.admin.insights.library.kpi.firstPlayDetail")),
        ];
    }

    /// <summary>Every movie and TV library with the share of its titles played; a row narrows the page to that library.</summary>
    public static IReadOnlyList<AdminInsightsBarRow> Coverage(AdminInsightsLibrary data)
        => DashboardInsightsPresentation.LibraryCoverage(data).Select(row => new AdminInsightsBarRow(row.Id, row.Label, row.Value, row.Secondary,
            LibraryId: AdminInsightsFilters.ParsePositiveId(row.Id))).ToArray();

    /// <summary>Genres or content ratings: plays, with the count of titles behind them; <c>unknown</c> is its own label.</summary>
    public static IReadOnlyList<AdminInsightsBarRow> PlaysRows(IReadOnlyList<AdminInsightsLabeledPlays> rows)
        => rows.Select(row => new AdminInsightsBarRow(row.Key, row.Key == "unknown" ? AdminInsightsText.T("web.admin.insights.library.unknown") : row.Key,
            row.Plays, AdminInsightsRules.TitleCount(row.Titles))).ToArray();

    private static readonly IReadOnlyDictionary<string, string> FirstPlayLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["day"] = "web.admin.insights.library.firstPlay.day",
        ["week"] = "web.admin.insights.library.firstPlay.week",
        ["month"] = "web.admin.insights.library.firstPlay.month",
        ["later"] = "web.admin.insights.library.firstPlay.later",
        ["not_yet"] = "web.admin.insights.library.firstPlay.notYet",
    };

    /// <summary>How long titles added this period waited for a first play; <c>sessions</c> holds the title count.</summary>
    public static IReadOnlyList<AdminInsightsBarRow> FirstPlayRows(IReadOnlyList<AdminInsightsLabeledCount> rows)
        => rows.Select(row => new AdminInsightsBarRow(row.Key,
            AdminInsightsText.T(FirstPlayLabels.GetValueOrDefault(row.Key) ?? "web.admin.insights.library.unknown"), row.Sessions)).ToArray();

    /// <summary>The decade labels in column order: "1990s", unknown last.</summary>
    public static IReadOnlyList<string> DecadeLabels(IReadOnlyList<AdminInsightsLabeledPlays> decades)
        => decades.Select(decade => decade.Key == "unknown" ? AdminInsightsText.T("web.admin.insights.library.unknown")
            : AdminInsightsText.T("web.admin.insights.library.decades.label", ("year", decade.Key))).ToArray();

    /// <summary>Plays by release decade as indexed columns (<see cref="DashboardInsightsPresentation.ViewingIndex"/> reads the index back); null when there are none.</summary>
    public static DashboardInsightsColumns? DecadeColumns(IReadOnlyList<AdminInsightsLabeledPlays> decades)
        => decades.Count == 0 ? null : new DashboardInsightsColumns(
            decades.Select((decade, index) => new DashboardChartColumn(DateTimeOffset.FromUnixTimeMilliseconds(index), [decade.Plays])).ToArray(),
            [AdminInsightsText.T("web.admin.insights.activity.volume.metricPlays")]);

    public sealed record UnplayedRow(string ContentId, string Title, string Added);

    /// <summary>The newest titles added this period that nobody has played, "Library · added Nov 3, 2026".</summary>
    public static IReadOnlyList<UnplayedRow> Unplayed(IReadOnlyList<AdminInsightsUnplayedTitle> titles)
        => titles.Select(title => new UnplayedRow(title.ContentId, AdminInsightsRules.FormatTitle(title.Title, title.Year),
            AdminInsightsText.T("web.admin.insights.library.unplayed.added",
                ("library", title.LibraryName), ("date", AdminInsightsRules.FormatShortCalendarDate(title.AddedAt))))).ToArray();
}
