using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Core.Services.Admin.AdminInsightsPlaybackPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Insights › Playback (official <c>PlaybackTab.tsx</c>, c59798116): what was played, when, how, and by how many people, from one Activity
/// read. Its charts are the dashboard widgets' charts (<see cref="DashboardInsightsCharts"/>); the cards differ only in their toggles and
/// their fixed plot heights.
/// </summary>
internal sealed class AdminInsightsPlaybackTab(AdminInsightsPage page) : AdminInsightsTabBase<AdminInsightsActivity>(page)
{
    private VolumeMetric _metric = VolumeMetric.Plays;
    private VolumeSplit _split = VolumeSplit.Method;
    private PeakView _peak = PeakView.Lines;
    private bool _weekdayWatchTime;
    private bool _hourWatchTime;

    protected override string KeyFor(AdminInsightsFilterState filters) => AdminInsightsApi.ActivityPath(filters.ToWindow());

    protected override Task<AdminInsightsActivity> LoadAsync(MonitorAuthorityLease lease, AdminInsightsFilterState filters, bool refresh, CancellationToken ct)
        => Page.Insights.GetActivityAsync(lease, filters.ToWindow(), refresh, ct);

    protected override FrameworkElement BuildSkeleton() => AdminInsightsLayout.Stack(
        AdminInsightsLayout.KpiSkeleton(), AdminInsightsLayout.CardSkeleton(288),
        AdminInsightsLayout.TwoUp(AdminInsightsLayout.CardSkeleton(256), AdminInsightsLayout.CardSkeleton(256)));

    protected override FrameworkElement BuildBody(AdminInsightsActivity data, AdminInsightsFilterState filters)
    {
        var notice = WatchTimeNotice(data);
        return AdminInsightsLayout.Stack(
            notice == null ? null : AdminInsightsUi.Notice(notice),
            AdminInsightsLayout.KpiGrid(Kpis(data)),
            VolumeCard(data, filters),
            AdminInsightsLayout.TwoUp(HeatmapCard(data, filters), PeakCard(data, filters)),
            AdminInsightsLayout.TwoUp(ViewingCard(data, filters, weekday: true), ViewingCard(data, filters, weekday: false)),
            AdminInsightsLayout.TwoUp(ActivePeopleCard(data, filters), EndReasonsCard(data)));
    }

    private static string I(string key, params (string, object?)[] values) => AdminInsightsText.T(key, values);

    private FrameworkElement Pin(string widgetId, string chartName) => new AdminInsightsPinButton(Page, widgetId, chartName);

    private AdminInsightsCard VolumeCard(AdminInsightsActivity data, AdminInsightsFilterState filters)
    {
        var title = I("web.admin.insights.activity.volume.title");
        AdminInsightsCard? card = null;
        FrameworkElement MetricToggle() => AdminInsightsLayout.Toggle(
            [(VolumeMetric.Plays, I("web.admin.insights.activity.volume.metricPlays"), I("web.admin.insights.activity.volume.metricPlays")),
             (VolumeMetric.WatchTime, I("web.admin.insights.activity.volume.metricWatchTime"), I("web.admin.insights.activity.volume.metricWatchTime"))],
            _metric, metric => { _metric = metric; card!.Rebuild(); });
        FrameworkElement SplitToggle() => AdminInsightsLayout.Toggle(
            [(VolumeSplit.Method, I("web.admin.insights.activity.volume.splitMethod"), I("web.admin.insights.activity.volume.splitMethod")),
             (VolumeSplit.Media, I("web.admin.insights.activity.volume.splitMediaType"), I("web.admin.insights.activity.volume.splitMediaType"))],
            _split, split => { _split = split; card!.Rebuild(); });
        card = new AdminInsightsCard(title, I("web.admin.insights.activity.volume.subtitle"),
            () => [Pin(DashboardRegistry.InsightsPlays, title), MetricToggle(), _metric == VolumeMetric.Plays ? SplitToggle() : null],
            () => DashboardInsightsCharts.Plays(data, VolumeView(_metric, _split), 210, 0) ?? AdminInsightsLayout.Empty(I("web.admin.insights.activity.volume.empty")),
            centered: true);
        return card;
    }

    private AdminInsightsCard HeatmapCard(AdminInsightsActivity data, AdminInsightsFilterState filters)
    {
        var title = I("web.admin.insights.activity.heatmap.title");
        return new AdminInsightsCard(title, I("web.admin.insights.activity.heatmap.subtitle", ("tz", data.Tz)),
            () => [Pin(DashboardRegistry.InsightsViewingHours, title)],
            () => data.Heatmap.Count == 0
                ? AdminInsightsLayout.Empty(I("web.admin.insights.activity.heatmap.empty"))
                : DashboardViewingHeatmap.Create(data.Heatmap, compact: false));
    }

    private AdminInsightsCard PeakCard(AdminInsightsActivity data, AdminInsightsFilterState filters)
    {
        AdminInsightsCard? card = null;
        string Dash(string key) => DashboardText.T("web.admin.dashboard.widgets.concurrentStreams." + key);
        FrameworkElement Toggle() => AdminInsightsLayout.Toggle(
            [(PeakView.Lines, Dash("viewLines"), Dash("viewLinesLabel")), (PeakView.Stacked, Dash("viewStacked"), Dash("viewStackedLabel")),
             (PeakView.Total, Dash("viewTotal"), Dash("viewTotalLabel"))],
            _peak, view => { _peak = view; card!.Rebuild(); });
        FrameworkElement Body()
        {
            var stack = new StackPanel { Spacing = 12 };
            if (ShowsPeakScopeNotice(filters)) stack.Children.Add(AdminInsightsUi.Notice(I("web.admin.insights.activity.peak.scopeNotice")));
            if (Peak(data, _peak) is not { } series)
            {
                stack.Children.Add(AdminInsightsLayout.Empty(I("web.admin.insights.activity.peak.empty")));
                return stack;
            }
            var chart = new DashboardLineChart
            {
                Points = series.Points,
                SeriesLabel = series.SeriesLabel,
                NeutralTotal = series.NeutralTotal,
                Overlays = series.Overlays,
                Bands = series.Bands,
                EdgeLabels = series.EdgeLabels,
                FormatTimestamp = PeakLabel,
                MinTickStep = 1,
                AccessibleLabel = I("web.admin.insights.activity.peak.chartLabel"),
                PlotHeight = 200,
            };
            chart.Build();
            stack.Children.Add(chart);
            return stack;
        }
        card = new AdminInsightsCard(I("web.admin.insights.activity.peak.title"), I("web.admin.insights.activity.peak.subtitle"), () => [Toggle()], Body);
        return card;
    }

    private AdminInsightsCard ViewingCard(AdminInsightsActivity data, AdminInsightsFilterState filters, bool weekday)
    {
        var title = I(weekday ? "web.admin.insights.activity.weekday.title" : "web.admin.insights.activity.hour.title");
        AdminInsightsCard? card = null;
        bool WatchTime() => weekday ? _weekdayWatchTime : _hourWatchTime;
        FrameworkElement Toggle() => AdminInsightsLayout.Toggle(
            [(false, I("web.admin.insights.activity.volume.metricPlays"), I("web.admin.insights.activity.volume.metricPlays")),
             (true, I("web.admin.insights.activity.volume.metricWatchTime"), I("web.admin.insights.activity.volume.metricWatchTime"))],
            WatchTime(), watchTime =>
            {
                if (weekday) _weekdayWatchTime = watchTime; else _hourWatchTime = watchTime;
                card!.Rebuild();
            });
        card = new AdminInsightsCard(title, I("web.admin.insights.activity.heatmap.subtitle", ("tz", data.Tz)),
            () => [Pin(weekday ? DashboardRegistry.InsightsWeekday : DashboardRegistry.InsightsHourOfDay, title), Toggle()],
            () => DashboardInsightsCharts.Viewing(data.Heatmap, weekday ? DashboardInsightsPresentation.ViewingAxis.Weekday : DashboardInsightsPresentation.ViewingAxis.Hour,
                    MetricView(WatchTime()), title, 180, 0)
                ?? AdminInsightsLayout.Empty(I("web.admin.insights.activity.heatmap.empty")));
        return card;
    }

    private AdminInsightsCard ActivePeopleCard(AdminInsightsActivity data, AdminInsightsFilterState filters)
    {
        var title = I("web.admin.insights.activity.activePeople.title");
        return new AdminInsightsCard(title, I("web.admin.insights.activity.activePeople.subtitle"),
            () => [Pin(DashboardRegistry.InsightsActivePeople, title)],
            () => DashboardInsightsCharts.ActivePeople(data, 200, 0) ?? AdminInsightsLayout.Empty(I("web.admin.insights.activity.volume.empty")));
    }

    private static AdminInsightsCard EndReasonsCard(AdminInsightsActivity data)
        => new(I("web.admin.insights.activity.endReasons.title"), I("web.admin.insights.activity.endReasons.subtitle"), null,
            () => AdminInsightsLayout.RowList(EndReasons(data.EndReasons), sessions => AdminInsightsRules.SessionCount((long)Math.Round(sessions)),
                I("web.admin.insights.activity.volume.empty"), seriesIndex: 3));
}
