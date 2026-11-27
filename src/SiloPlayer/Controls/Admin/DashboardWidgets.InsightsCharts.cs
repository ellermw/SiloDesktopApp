using Microsoft.UI.Xaml;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Controls.Admin.DashboardUi;

namespace SiloPlayer.Controls.Admin;

/// <summary>
/// Widgets read from the Insights Activity read of the card's window (official <c>InsightsWidgets.tsx</c>, c59798116). They count plays the
/// Insights way (a resumed sitting is one play, a false start none), show the whole server in the machine's time zone, and the five
/// that draw from Activity share one read per window. A failed poll keeps the last data on screen: the read re-runs only every 5 or 15
/// minutes, so an error shown over data would stay up until the next poll.
/// </summary>
internal abstract class InsightsActivityWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    protected string ActivityKey => DashboardDataStore.InsightsActivity(Range);
    protected override IEnumerable<string> Keys => [ActivityKey];

    /// <summary>The card's title key under <c>web.admin.insights</c> (the Insights page's own chart title).</summary>
    protected abstract string TitleKey { get; }

    protected static string I(string key, params (string, object?)[] values) => AdminInsightsText.T(key, values);

    /// <summary>
    /// The card: title with its window, optional toggle, "Insights ›" (Playback tab) and the range picker; the body is the skeleton
    /// while the first read is out, the unavailable line when there is no data to draw (a failed first read or an unsupported server).
    /// </summary>
    protected FrameworkElement Build(FrameworkElement? toggle, Func<AdminInsightsActivity, FrameworkElement> body, bool scroll = false)
    {
        var state = Store.Get(ActivityKey);
        var data = state.As<AdminInsightsActivity>();
        var header = Header(DashboardRanges.Title(I(TitleKey), Range), toggle, InsightsLink(AdminInsightsTab.Playback, HeaderLg), RangePicker());
        FrameworkElement content = data != null ? body(data)
            : state.Error != null ? SectionError(T("web.admin.dashboard.widgets.insights.loadFailed"))
            : DashboardChartChrome.ChartSkeleton(ChartMinHeight);
        return Card(header, content, scroll);
    }

    protected FrameworkElement MetricToggle(string selected)
        => Segmented(
            [(DashboardRegistry.InsightsViewPlays, I("web.admin.insights.activity.volume.metricPlays"), I("web.admin.insights.activity.volume.metricPlays")),
             (DashboardRegistry.InsightsViewWatchTime, I("web.admin.insights.activity.volume.metricWatchTime"), I("web.admin.insights.activity.volume.metricWatchTime"))],
            selected, C.SetView);

    /// <summary>The effective plays/watch-time view of a metric widget: a stored value it does not offer reads as plays.</summary>
    protected string MetricView => View == DashboardRegistry.InsightsViewWatchTime ? DashboardRegistry.InsightsViewWatchTime : DashboardRegistry.InsightsViewPlays;

    /// <summary>Plays or watch time by weekday or hour: the heatmap's rows or columns added up.</summary>
    protected FrameworkElement ViewingChart(AdminInsightsActivity data, DashboardInsightsPresentation.ViewingAxis axis)
    {
        if (DashboardInsightsPresentation.ViewingColumns(data.Heatmap, axis, MetricView) is not { } shape)
            return Empty(I("web.admin.insights.activity.heatmap.empty"));
        var watchTime = MetricView == DashboardRegistry.InsightsViewWatchTime;
        return ColumnChart(shape, time => DashboardInsightsPresentation.ViewingLabel(axis, DashboardInsightsPresentation.ViewingIndex(time)),
            watchTime ? seconds => DashboardFormat.WatchTime((long)Math.Round(seconds)) : value => N((long)Math.Round(value)), null, I(TitleKey));
    }
    protected FrameworkElement Empty(string message) => DashboardChartChrome.EmptyState(message, null, null, Now, ChartMinHeight);

    protected FrameworkElement ColumnChart(DashboardInsightsColumns shape, Func<DateTimeOffset, string> formatBucket, Func<double, string> formatValue,
        string? totalLabel, string accessibleLabel)
    {
        var chart = new DashboardStackedColumnChart
        {
            Columns = shape.Columns,
            SeriesLabels = shape.SeriesLabels,
            AccessibleLabel = accessibleLabel,
            FormatBucket = formatBucket,
            FormatValue = formatValue,
            TotalLabel = totalLabel,
            PlotHeight = Fixed ? double.NaN : DashboardGridPlacement.StackedChartMinHeight,
            MinPlotHeight = ChartMinHeight,
        };
        chart.Build();
        return chart;
    }
}

/// <summary>Plays per day, week or month, split by play method or media type, or the watch time.</summary>
internal sealed class InsightsPlaysWidget(DashboardWidgetContext c) : InsightsActivityWidget(c)
{
    protected override string TitleKey => "web.admin.insights.activity.volume.title";

    protected override FrameworkElement Render()
    {
        var mode = View is DashboardRegistry.InsightsViewMedia or DashboardRegistry.InsightsViewWatchTime ? View! : DashboardRegistry.InsightsViewMethod;
        var toggle = Segmented(
            [(DashboardRegistry.InsightsViewMethod, I("web.admin.insights.activity.volume.splitMethod"), I("web.admin.insights.activity.volume.splitMethod")),
             (DashboardRegistry.InsightsViewMedia, I("web.admin.insights.activity.volume.splitMediaType"), I("web.admin.insights.activity.volume.splitMediaType")),
             (DashboardRegistry.InsightsViewWatchTime, I("web.admin.insights.activity.volume.metricWatchTime"), I("web.admin.insights.activity.volume.metricWatchTime"))],
            mode, C.SetView);
        return Build(toggle, data =>
        {
            if (DashboardInsightsPresentation.PlaysColumns(data, mode) is not { } shape) return Empty(I("web.admin.insights.activity.volume.empty"));
            var watchTime = mode == DashboardRegistry.InsightsViewWatchTime;
            return ColumnChart(shape, time => DashboardInsightsPresentation.BucketLabel(time, data.BucketUnit),
                watchTime ? seconds => DashboardFormat.WatchTime((long)Math.Round(seconds)) : value => N((long)Math.Round(value)),
                watchTime ? null : I("web.admin.insights.activity.volume.metricPlays"), I("web.admin.insights.activity.volume.chartLabel"));
        });
    }
}

/// <summary>The weekday × hour heatmap of play starts.</summary>
internal sealed class InsightsViewingHoursWidget(DashboardWidgetContext c) : InsightsActivityWidget(c)
{
    protected override string TitleKey => "web.admin.insights.activity.heatmap.title";

    protected override FrameworkElement Render()
        => Build(null, data => data.Heatmap.Count == 0
            ? Empty(I("web.admin.insights.activity.heatmap.empty"))
            : DashboardViewingHeatmap.Create(data.Heatmap), scroll: true);
}

/// <summary>Plays or watch time by day of the week, Monday first.</summary>
internal sealed class InsightsWeekdayWidget(DashboardWidgetContext c) : InsightsActivityWidget(c)
{
    protected override string TitleKey => "web.admin.insights.activity.weekday.title";

    protected override FrameworkElement Render()
        => Build(MetricToggle(MetricView), data => ViewingChart(data, DashboardInsightsPresentation.ViewingAxis.Weekday));
}

/// <summary>Plays or watch time by hour of the day.</summary>
internal sealed class InsightsHourOfDayWidget(DashboardWidgetContext c) : InsightsActivityWidget(c)
{
    protected override string TitleKey => "web.admin.insights.activity.hour.title";

    protected override FrameworkElement Render()
        => Build(MetricToggle(MetricView), data => ViewingChart(data, DashboardInsightsPresentation.ViewingAxis.Hour));
}

/// <summary>Distinct profiles (the line) and accounts (the overlay) watching per day, week or month.</summary>
internal sealed class InsightsActivePeopleWidget(DashboardWidgetContext c) : InsightsActivityWidget(c)
{
    protected override string TitleKey => "web.admin.insights.activity.activePeople.title";

    protected override FrameworkElement Render()
        => Build(null, data =>
        {
            if (DashboardInsightsPresentation.ActivePeople(data) is not { } series) return Empty(I("web.admin.insights.activity.volume.empty"));
            var chart = new DashboardLineChart
            {
                Points = series.Profiles,
                SeriesLabel = I("web.admin.insights.people.kpi.activeProfiles"),
                Overlays = [(I("web.admin.insights.people.kpi.activeAccounts"), series.Accounts, 1)],
                EdgeLabels = (series.StartLabel, series.EndLabel),
                FormatTimestamp = time => DashboardInsightsPresentation.BucketLabel(time, data.BucketUnit),
                MinTickStep = 1,
                AccessibleLabel = I("web.admin.insights.activity.activePeople.title"),
                MinPlotHeight = ChartMinHeight,
            };
            chart.Build();
            return chart;
        });
}

/// <summary>Each movie and TV library with the share of its titles played in the window.</summary>
internal sealed class InsightsLibraryCoverageWidget(DashboardWidgetContext c) : DashboardWidgetView(c)
{
    private string Key => DashboardDataStore.InsightsLibrary(Range);
    protected override IEnumerable<string> Keys => [Key];

    protected override FrameworkElement Render()
    {
        var state = Store.Get(Key);
        var data = state.As<AdminInsightsLibrary>();
        var header = Header(DashboardRanges.Title(AdminInsightsText.T("web.admin.insights.library.coverage.title"), Range),
            InsightsLink(AdminInsightsTab.Library, HeaderLg), RangePicker());
        FrameworkElement body = data != null
            ? DashboardBarList.Create(DashboardInsightsPresentation.LibraryCoverage(data).Select(row => new DashboardBarItem(row.Id, row.Label, row.Value, row.Secondary)).ToArray(),
                value => DashboardInsightsPresentation.CoverageValueText(value), AdminInsightsText.T("web.admin.insights.library.coverage.empty"), max: 100, seriesIndex: 2)
            : state.Error != null ? SectionError(T("web.admin.dashboard.widgets.insights.loadFailed"))
            : DashboardChartChrome.BarListSkeleton();
        return Card(header, body);
    }
}
