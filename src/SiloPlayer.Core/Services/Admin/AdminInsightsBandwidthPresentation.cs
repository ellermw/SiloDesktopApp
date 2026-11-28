using System.Globalization;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The Bandwidth tab (official <c>BandwidthTab.tsx</c>, c59798116) shaped from one Bandwidth read: the notice, KPI cards, the daily chart
/// (data sent or peak speed, in total or by node) and the per-node table. Node colours and duplicate-name labels come from
/// <see cref="AdminEgressBands"/>, the rule the Dashboard's Egress widget draws its bands with.
/// </summary>
public static class AdminInsightsBandwidthPresentation
{
    private const string Dash = "—";

    public enum Metric { Volume, Peak }

    public enum Scope { Total, Nodes }

    /// <summary>"Bandwidth history starts on …": the oldest recorded metric is after the start of the window.</summary>
    public static string? Notice(AdminInsightsBandwidth data)
        => AdminInsightsRules.ShowMetricsStartNotice(data)
            ? AdminInsightsText.T("web.admin.insights.coverage.metricsStart", ("since", AdminInsightsRules.FormatShortCalendarDate(data.OldestMetricAt)))
            : null;

    public static IReadOnlyList<AdminInsightsKpi> Kpis(AdminInsightsBandwidth data)
    {
        var summary = data.Summary;
        var known = summary.RemoteSessions + summary.LocalSessions;
        return
        [
            new(AdminInsightsText.T("web.admin.insights.bandwidth.kpi.totalEgress"), DashboardFormat.DataTotal(summary.TotalEgressBytes),
                data.HasRemoteNodes
                    ? AdminInsightsText.T("web.admin.insights.bandwidth.kpi.nodeSplit", ("api", DashboardFormat.DataTotal(summary.ApiEgressBytes)), ("nodes", DashboardFormat.DataTotal(summary.NodeEgressBytes)))
                    : AdminInsightsText.T("web.admin.insights.bandwidth.kpi.egressSplit", ("playback", DashboardFormat.DataTotal(summary.PlaybackEgressBytes)),
                        ("downloads", DashboardFormat.DataTotal(summary.DownloadEgressBytes)))),
            new(AdminInsightsText.T("web.admin.insights.bandwidth.kpi.peakEgress"), DashboardFormat.Mbps(summary.PeakEgressKbps / 1000d),
                string.IsNullOrEmpty(summary.PeakEgressAt) ? Dash
                    : AdminInsightsText.T("web.admin.insights.bandwidth.kpi.peakEgressAt", ("date", AdminInsightsRules.FormatShortTimestamp(summary.PeakEgressAt))),
                ActivityTone.Info),
            new(AdminInsightsText.T("web.admin.insights.bandwidth.kpi.dailyAverage"), DashboardFormat.DataTotal(summary.AvgDailyBytes),
                AdminInsightsText.T("web.admin.insights.bandwidth.kpi.dailyAverageSub", AdminInsightsText.Count(summary.ActiveDays))),
            new(AdminInsightsText.T("web.admin.insights.bandwidth.kpi.remoteShare"),
                known > 0 ? AdminInsightsNumbers.Percent((double)summary.RemoteSessions / known) : Dash,
                known > 0
                    ? AdminInsightsText.T("web.admin.insights.bandwidth.kpi.remoteShareDetail", ("remote", AdminInsightsNumbers.Count(summary.RemoteSessions)), ("local", AdminInsightsNumbers.Count(summary.LocalSessions)))
                    : AdminInsightsText.T("web.admin.insights.bandwidth.kpi.remoteShareUnknown")),
        ];
    }

    /// <summary>The By node view is offered only with remote nodes and more than one source; otherwise the chart is the total.</summary>
    public static bool CanSplitNodes(AdminInsightsBandwidth data) => data.HasRemoteNodes && data.Sources.Count > 1;

    public static Scope EffectiveScope(AdminInsightsBandwidth data, Scope chosen) => chosen == Scope.Nodes && CanSplitNodes(data) ? Scope.Nodes : Scope.Total;

    /// <summary>The chart to draw: the total line, plus overlays or stacked bands.</summary>
    public sealed record Series(
        IReadOnlyList<DashboardChartPoint> Points,
        string SeriesLabel,
        bool NeutralTotal,
        IReadOnlyList<(string Label, IReadOnlyList<DashboardChartPoint> Points, int Slot)> Overlays,
        IReadOnlyList<DashboardChartBand> Bands,
        (string Start, string End) EdgeLabels,
        Metric Metric);

    /// <summary>Null when the window has no point (the card says "No bandwidth data in this period").</summary>
    public static Series? Chart(AdminInsightsBandwidth data, Metric metric, Scope chosenScope)
    {
        var raw = data.Points;
        if (raw.Count == 0) return null;
        var byDate = new Dictionary<string, AdminInsightsBandwidthPoint>(StringComparer.Ordinal);
        foreach (var point in raw) byDate[point.Date] = point;
        var first = raw[0].Date;
        var last = raw[^1].Date;
        var grid = AdminInsightsRules.BuildDailyUtcGrid(first, last);
        IReadOnlyList<DashboardChartPoint> Points(Func<AdminInsightsBandwidthPoint, double> pick) => grid
            .Select(day => new DashboardChartPoint(AdminInsightsPlaybackPresentation.DayNoon(day), byDate.TryGetValue(day, out var point) ? pick(point) : null)).ToArray();
        var edges = (AdminInsightsRules.FormatShortCalendarDate(first), AdminInsightsRules.FormatShortCalendarDate(last));
        var total = Points(point => metric == Metric.Volume ? point.EgressBytes : point.PeakEgressKbps / 1000d);
        var metricLabel = AdminInsightsText.T(metric == Metric.Volume ? "web.admin.insights.bandwidth.chart.metricVolume" : "web.admin.insights.bandwidth.chart.metricPeak");

        if (EffectiveScope(data, chosenScope) == Scope.Nodes)
        {
            var top = data.Sources.OrderByDescending(source => source.EgressBytes).ThenBy(source => source.Key, StringComparer.Ordinal)
                .Take(AdminEgressBands.MaxNamedBands).ToList();
            var kept = top.Select(source => source.Key).ToHashSet(StringComparer.Ordinal);
            var identities = top.Select(AdminEgressBands.Identity).ToList();
            var labels = AdminEgressBands.Labels(identities, AdminInsightsText.T("web.admin.insights.bandwidth.chart.apiServer"));
            var slots = AdminEgressBands.Slots(identities);
            int SlotOf(AdminInsightsEgressSource source) => slots.GetValueOrDefault(source.Key, AdminEgressBands.BandColorSlots[0]);

            // Per-node daily peaks come from independent minutes, so stacking them could overshoot the cluster's own peak: they are separate lines
            // under the neutral total. Stacked bands are for additive daily byte volumes only.
            if (metric == Metric.Peak)
                return new(total, DashboardText.T("web.admin.dashboard.chart.total"), NeutralTotal: true,
                    top.Select(source => (labels[source.Key], Points(point => point.PeakKbpsBySource.GetValueOrDefault(source.Key) / 1000d), SlotOf(source))).ToArray(),
                    [], edges, metric);

            var named = top.Select(source => new DashboardChartBand(source.Key, labels[source.Key], SlotOf(source), false,
                Points(point => point.BytesBySource.GetValueOrDefault(source.Key)))).ToList();
            var other = Points(point => Math.Max(point.EgressBytes - point.BytesBySource.Where(pair => kept.Contains(pair.Key)).Sum(pair => pair.Value), 0));
            if (other.Any(point => (point.Value ?? 0) > 0))
                named.Insert(0, new DashboardChartBand("other", DashboardText.T("web.admin.dashboard.widgets.egress.otherBand"), null, true, other));
            return new(total, metricLabel, NeutralTotal: false, [], named, edges, metric);
        }

        var hasDownloads = raw.Any(point => metric == Metric.Volume ? point.DownloadEgressBytes > 0 : point.PeakDownloadKbps > 0);
        IReadOnlyList<(string, IReadOnlyList<DashboardChartPoint>, int)> overlays = hasDownloads
            ? [(DashboardText.T("web.admin.dashboard.widgets.egress.downloadsSeries"), Points(point => metric == Metric.Volume ? point.DownloadEgressBytes : point.PeakDownloadKbps / 1000d), 1)]
            : [];
        return new(total, metricLabel, NeutralTotal: false, overlays, [], edges, metric);
    }

    /// <summary>One row of the "Bandwidth by node" table.</summary>
    public sealed record NodeRow(string Key, string Name, string? TypeBadge, string DataSent, string SharePercent, double ShareFill, string Peak);

    /// <summary>The sources in the server's order (the API first, then nodes by data sent); an empty list reads as no remote node traffic.</summary>
    public static IReadOnlyList<NodeRow> NodeRows(AdminInsightsBandwidth data)
        => data.Sources.Select(source => new NodeRow(source.Key,
            source.Kind == "api" ? AdminInsightsText.T("web.admin.insights.bandwidth.chart.apiServer") : source.Name,
            source.NodeType switch
            {
                null or "" => null,
                "proxy" => AdminText.T("web.admin.activity.route.proxy"),
                "transcode" => AdminText.T("web.admin.activity.route.transcode"),
                var other => other,
            },
            DashboardFormat.DataTotal(source.EgressBytes),
            AdminInsightsNumbers.Percent(source.ShareOfTotal),
            Math.Clamp(source.ShareOfTotal * 100, 0, 100) / 100,
            DashboardFormat.Mbps(source.PeakEgressKbps / 1000d))).ToArray();

    /// <summary>The tooltip and tick formats of the chart: bytes for the volume view, megabits per second for the peak.</summary>
    public static Func<double, string> FormatValue(Metric metric) => metric == Metric.Volume ? value => DashboardFormat.DataTotal(value) : DashboardFormat.Mbps;

    public static Func<double, string> FormatTick(Metric metric) => metric == Metric.Volume ? value => DashboardFormat.DataTotal(value) : DashboardFormat.MbpsValue;

    public static string ChartLabel(Metric metric)
        => AdminInsightsText.T(metric == Metric.Volume ? "web.admin.insights.bandwidth.chart.volumeLabel" : "web.admin.insights.bandwidth.chart.peakLabel");

    /// <summary>The label of a day on the axis and in the tooltip: the UTC calendar date.</summary>
    public static string DayLabel(DateTimeOffset time) => AdminInsightsPlaybackPresentation.PeakLabel(time);
}
