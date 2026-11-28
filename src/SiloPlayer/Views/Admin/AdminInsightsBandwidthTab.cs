using Microsoft.UI.Text;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using A = SiloPlayer.Controls.AdminMonitorUi;
using B = SiloPlayer.Core.Services.Admin.AdminInsightsBandwidthPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Insights › Bandwidth (official <c>BandwidthTab.tsx</c>, c59798116): data the server sent over the window, daily and in total, and which
/// node sent it. It is server-wide, so only the range applies. The by-node view colours and labels its sources with
/// <see cref="AdminEgressBands"/>, the rule the Dashboard's Egress widget draws its bands with.
/// </summary>
internal sealed class AdminInsightsBandwidthTab(AdminInsightsPage page) : AdminInsightsTabBase<AdminInsightsBandwidth>(page)
{
    private B.Metric _metric = B.Metric.Volume;
    private B.Scope _scope = B.Scope.Total;

    protected override string KeyFor(AdminInsightsFilterState filters) => AdminInsightsApi.BandwidthPath(filters.ToWindow());

    protected override Task<AdminInsightsBandwidth> LoadAsync(MonitorAuthorityLease lease, AdminInsightsFilterState filters, bool refresh, CancellationToken ct)
        => Page.Insights.GetBandwidthAsync(lease, filters.ToWindow(), refresh, ct);

    protected override FrameworkElement BuildSkeleton() => AdminInsightsLayout.Stack(AdminInsightsLayout.KpiSkeleton(), AdminInsightsLayout.CardSkeleton(288));

    private static string I(string key, params (string, object?)[] values) => AdminInsightsText.T(key, values);

    protected override FrameworkElement BuildBody(AdminInsightsBandwidth data, AdminInsightsFilterState filters)
    {
        var notice = B.Notice(data);
        return AdminInsightsLayout.Stack(
            notice == null ? null : AdminInsightsUi.Notice(notice),
            AdminInsightsLayout.KpiGrid(B.Kpis(data)),
            ChartCard(data),
            data.HasRemoteNodes ? NodesCard(data) : null);
    }

    // ── Bandwidth over time ──────────────────────────────────

    private FrameworkElement ChartCard(AdminInsightsBandwidth data)
    {
        AdminInsightsCard? card = null;
        FrameworkElement MetricToggle() => AdminInsightsLayout.Toggle(
            [(B.Metric.Volume, I("web.admin.insights.bandwidth.chart.metricVolume"), I("web.admin.insights.bandwidth.chart.metricVolume")),
             (B.Metric.Peak, I("web.admin.insights.bandwidth.chart.metricPeak"), I("web.admin.insights.bandwidth.chart.metricPeak"))],
            _metric, metric => { _metric = metric; card!.Rebuild(); });
        FrameworkElement ScopeToggle() => AdminInsightsLayout.Toggle(
            [(B.Scope.Total, I("web.admin.insights.bandwidth.chart.viewTotal"), I("web.admin.insights.bandwidth.chart.viewTotal")),
             (B.Scope.Nodes, I("web.admin.insights.bandwidth.chart.viewNodes"), I("web.admin.insights.bandwidth.chart.viewNodes"))],
            B.EffectiveScope(data, _scope), scope => { _scope = scope; card!.Rebuild(); });
        card = new AdminInsightsCard(I("web.admin.insights.bandwidth.chart.title"), I("web.admin.insights.bandwidth.chart.subtitle"),
            () => [MetricToggle(), B.CanSplitNodes(data) ? ScopeToggle() : null], () => Chart(data));
        return card;
    }

    private FrameworkElement Chart(AdminInsightsBandwidth data)
    {
        if (B.Chart(data, _metric, _scope) is not { } series) return AdminInsightsLayout.Empty(I("web.admin.insights.bandwidth.chart.empty"));
        var chart = new DashboardLineChart
        {
            Points = series.Points,
            SeriesLabel = series.SeriesLabel,
            NeutralTotal = series.NeutralTotal,
            Overlays = series.Overlays,
            Bands = series.Bands,
            EdgeLabels = series.EdgeLabels,
            FormatValue = B.FormatValue(series.Metric),
            FormatTick = B.FormatTick(series.Metric),
            FormatTimestamp = B.DayLabel,
            AccessibleLabel = B.ChartLabel(series.Metric),
            PlotHeight = 220,
        };
        chart.Build();
        return chart;
    }

    // ── Bandwidth by node ────────────────────────────────────

    private static FrameworkElement NodesCard(AdminInsightsBandwidth data)
        => new AdminInsightsCard(I("web.admin.insights.bandwidth.nodes.title"), null, null, () =>
        {
            var rows = B.NodeRows(data);
            if (rows.Count == 0)
            {
                var empty = A.Text(I("web.admin.insights.bandwidth.nodes.empty"), 14, A.Muted, trim: false);
                empty.TextAlignment = TextAlignment.Center;
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                return new Border { Padding = new Thickness(0, 24, 0, 24), Child = empty };
            }
            var table = new AdminInsightsTable(4);
            table.Align(1, HorizontalAlignment.Right).Align(2, HorizontalAlignment.Right).Align(3, HorizontalAlignment.Right);
            table.AddHeader([AdminInsightsTable.HeaderText(I("web.admin.insights.bandwidth.nodes.column.node")), Right(I("web.admin.insights.bandwidth.nodes.column.total")),
                Right(I("web.admin.insights.bandwidth.nodes.column.share")), Right(I("web.admin.insights.bandwidth.nodes.column.peak"))]);
            foreach (var row in rows)
            {
                var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                name.Children.Add(A.Text(row.Name, 14, A.Foreground, FontWeights.Medium, trim: true));
                if (row.TypeBadge != null) name.Children.Add(AdminInsightsTable.OutlineBadge(row.TypeBadge));
                table.AddRow([name, Value(row.DataSent), Share(row), Value(row.Peak)]);
            }
            return table;
        });

    private static TextBlock Right(string text)
    {
        var label = AdminInsightsTable.HeaderText(text);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        return label;
    }

    private static TextBlock Value(string text)
    {
        var block = A.Text(text, 14, A.Foreground, trim: false);
        block.HorizontalAlignment = HorizontalAlignment.Right;
        return block;
    }

    /// <summary>A 64px track with the node's share filled in, and the percentage in a 40px column.</summary>
    private static UIElement Share(B.NodeRow row)
    {
        var fill = new Border { Width = row.ShareFill * 64, Height = 6, CornerRadius = new CornerRadius(3), Background = A.Solid(A.Primary), HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Border { Width = 64, Height = 6, CornerRadius = new CornerRadius(3), Background = A.Solid(A.Muted, 0.25), VerticalAlignment = VerticalAlignment.Center, Child = fill };
        var percent = A.Text(row.SharePercent, 14, A.Foreground, trim: false);
        percent.Width = 40;
        percent.TextAlignment = TextAlignment.Right;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { track, percent } };
    }
}
