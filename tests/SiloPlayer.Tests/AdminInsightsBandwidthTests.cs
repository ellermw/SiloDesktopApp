using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using B = SiloPlayer.Core.Services.Admin.AdminInsightsBandwidthPresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// The Bandwidth tab (official <c>BandwidthTab.tsx</c> at c59798116) and the egress band rule it shares with the Dashboard's Egress widget
/// (<c>egressBands.ts</c>): KPI values, the chart's series for each toggle, node colours and duplicate-name labels, and the node table.
/// Synthetic data only.
/// </summary>
public sealed class AdminInsightsBandwidthTests
{
    public AdminInsightsBandwidthTests() => CultureInfo.CurrentCulture = new CultureInfo("en-US");

    private static AdminInsightsBandwidth Data() => JsonSerializer.Deserialize<AdminInsightsBandwidth>(AdminInsightsWire.Bandwidth)!;

    private static AdminInsightsEgressSource Source(string key, string kind, string name, string? nodeId, long bytes, string? type = null, long peak = 0, double share = 0)
        => new() { Key = key, Kind = kind, Name = name, NodeId = nodeId, EgressBytes = bytes, NodeType = type, PeakEgressKbps = peak, ShareOfTotal = share };

    private static AdminInsightsBandwidthPoint Point(string date, long total, long download = 0, long peak = 0, long peakDownload = 0, params (string Key, long Bytes, long Kbps)[] by)
        => new()
        {
            Date = date, EgressBytes = total, DownloadEgressBytes = download, PeakEgressKbps = peak, PeakDownloadKbps = peakDownload,
            BytesBySource = by.ToDictionary(item => item.Key, item => item.Bytes), PeakKbpsBySource = by.ToDictionary(item => item.Key, item => item.Kbps),
        };

    /// <summary>An API server and three nodes, one of which shares a name with another.</summary>
    private static AdminInsightsBandwidth WithNodes()
    {
        var data = Data();
        data.HasRemoteNodes = true;
        data.Sources =
        [
            Source("api", "api", "api", null, 500, null, 90_000, 0.5),
            Source("node:2", "node", "Edge", "2", 300, "proxy", 70_000, 0.3),
            Source("node:3", "node", "Edge", "3", 150, "transcode", 40_000, 0.15),
            Source("node:9", "node", "Far", "9", 50, null, 10_000, 0.05),
        ];
        data.Points =
        [
            Point("2026-11-20", 1000, 100, 200, 20, ("api", 500, 90), ("node:2", 300, 70), ("node:3", 150, 40), ("node:9", 50, 10)),
            Point("2026-11-22", 600, 0, 100, 0, ("api", 300, 50), ("node:2", 200, 40)),
        ];
        return data;
    }

    // ── Notice and KPI cards ──────────────────────────────────

    [Fact]
    public void TheNoticeSaysWhenBandwidthHistoryStartsAfterTheWindow()
    {
        var data = Data();
        data.From = "2026-10-20T00:00:00Z";
        data.OldestMetricAt = "2026-11-02T00:00:00Z";
        Assert.Equal("Bandwidth history starts on " + AdminInsightsRules.FormatShortCalendarDate("2026-11-02T00:00:00Z") + ".", B.Notice(data));
        data.OldestMetricAt = "2026-10-20T00:00:00Z";
        Assert.Null(B.Notice(data));
        data.OldestMetricAt = null;
        Assert.Null(B.Notice(data));
    }

    [Fact]
    public void FourKpiCardsCarryTheOfficialValuesAndSubtexts()
    {
        var data = WithNodes();
        data.Summary = new AdminInsightsBandwidthSummary
        {
            TotalEgressBytes = 5_000_000_000, ApiEgressBytes = 2_000_000_000, NodeEgressBytes = 3_000_000_000, PlaybackEgressBytes = 4_000_000_000, DownloadEgressBytes = 1_000_000_000,
            PeakEgressKbps = 42_000, PeakEgressAt = "2026-11-21T20:15:00Z", AvgDailyBytes = 250_000_000, ActiveDays = 20, RemoteSessions = 30, LocalSessions = 70,
        };
        var kpis = B.Kpis(data);
        Assert.Equal(["Total data sent", "Peak speed", "Daily average", "Remote viewing"], kpis.Select(kpi => kpi.Label));
        Assert.Equal("5.0 GB", kpis[0].Value);
        Assert.Equal("2.0 GB API server · 3.0 GB stream nodes", kpis[0].Subtext);
        Assert.Equal("42 Mbps", kpis[1].Value);
        Assert.Equal("on " + AdminInsightsRules.FormatShortTimestamp("2026-11-21T20:15:00Z"), kpis[1].Subtext);
        Assert.Equal(ActivityTone.Info, kpis[1].Accent);
        Assert.Equal("250 MB", kpis[2].Value);
        Assert.Equal("across 20 active days", kpis[2].Subtext);
        Assert.Equal("30%", kpis[3].Value);
        Assert.Equal("30 remote · 70 local", kpis[3].Subtext);
    }

    [Fact]
    public void WithoutRemoteNodesTheFirstCardSplitsPlaybackFromDownloadsAndUnknownLocationsSayNoNetworkInfo()
    {
        var data = Data();
        data.HasRemoteNodes = false;
        data.Summary.PlaybackEgressBytes = 4_000_000_000;
        data.Summary.DownloadEgressBytes = 1_000_000_000;
        data.Summary.PeakEgressAt = null;
        data.Summary.RemoteSessions = 0;
        data.Summary.LocalSessions = 0;
        data.Summary.ActiveDays = 1;
        var kpis = B.Kpis(data);
        Assert.Equal("4.0 GB playback · 1.0 GB downloads", kpis[0].Subtext);
        Assert.Equal("—", kpis[1].Subtext);
        Assert.Equal("across 1 active day", kpis[2].Subtext);
        Assert.Equal("—", kpis[3].Value);
        Assert.Equal("No network info yet", kpis[3].Subtext);
    }

    // ── The chart ─────────────────────────────────────────────

    [Fact]
    public void AWindowWithoutPointsHasNoChart() => Assert.Null(B.Chart(new AdminInsightsBandwidth(), B.Metric.Volume, B.Scope.Total));

    [Fact]
    public void TheTotalViewDrawsTheDailyGridWithGapsAndADownloadsOverlayOnlyWhenThereAreDownloads()
    {
        var data = WithNodes();
        var series = B.Chart(data, B.Metric.Volume, B.Scope.Total)!;
        Assert.Equal("Data sent", series.SeriesLabel);
        Assert.Equal(["2026-11-20", "2026-11-21", "2026-11-22"], series.Points.Select(point => point.T.UtcDateTime.ToString("yyyy-MM-dd")));
        Assert.Equal([1000d, null, 600d], series.Points.Select(point => point.Value));
        Assert.All(series.Points, point => Assert.Equal(12, point.T.Hour));
        var downloads = Assert.Single(series.Overlays);
        Assert.Equal("Downloads", downloads.Label);
        Assert.Equal(1, downloads.Slot);
        Assert.Equal([100d, null, 0d], downloads.Points.Select(point => point.Value));
        Assert.Empty(series.Bands);
        Assert.Equal(("Nov 20, 2026", "Nov 22, 2026"), series.EdgeLabels);
        foreach (var point in data.Points) point.DownloadEgressBytes = 0;
        Assert.Empty(B.Chart(data, B.Metric.Volume, B.Scope.Total)!.Overlays);
    }

    [Fact]
    public void ThePeakViewIsInMegabitsAndItsDownloadsFollowThePeakDownloadSpeed()
    {
        var series = B.Chart(WithNodes(), B.Metric.Peak, B.Scope.Total)!;
        Assert.Equal("Peak speed", series.SeriesLabel);
        Assert.Equal([0.2, null, 0.1], series.Points.Select(point => point.Value));
        Assert.Equal([0.02, null, 0d], series.Overlays.Single().Points.Select(point => point.Value));
        Assert.Equal("1.5 Mbps", B.FormatValue(B.Metric.Peak)(1.5));
        Assert.Equal("1.5", B.FormatTick(B.Metric.Peak)(1.5));
        Assert.Equal("1.5 GB", B.FormatValue(B.Metric.Volume)(1_500_000_000));
    }

    [Fact]
    public void ByNodeNeedsRemoteNodesAndMoreThanOneSourceElseItIsTheTotal()
    {
        var data = WithNodes();
        Assert.True(B.CanSplitNodes(data));
        Assert.Equal(B.Scope.Nodes, B.EffectiveScope(data, B.Scope.Nodes));
        data.HasRemoteNodes = false;
        Assert.False(B.CanSplitNodes(data));
        Assert.Equal(B.Scope.Total, B.EffectiveScope(data, B.Scope.Nodes));
        data.HasRemoteNodes = true;
        data.Sources = [data.Sources[0]];
        Assert.Equal(B.Scope.Total, B.EffectiveScope(data, B.Scope.Nodes));
        Assert.Empty(B.Chart(data, B.Metric.Volume, B.Scope.Nodes)!.Bands);
    }

    [Fact]
    public void ByNodeStacksTheFourBiggestSourcesWithTheRestAsOtherAndLabelsDuplicateNamesWithTheirIds()
    {
        var data = WithNodes();
        // A fifth source: the four that sent the most get bands, the rest share Other, below them.
        data.Sources.Add(Source("node:12", "node", "Small", "12", 10));
        var series = B.Chart(data, B.Metric.Volume, B.Scope.Nodes)!;
        Assert.Equal("Data sent", series.SeriesLabel);
        Assert.False(series.NeutralTotal);
        Assert.Equal(["other", "api", "node:2", "node:3", "node:9"], series.Bands.Select(band => band.Key));
        Assert.True(series.Bands[0].Neutral);
        Assert.Equal("Other", series.Bands[0].Label);
        Assert.Equal(["API server", "Edge (#2)", "Edge (#3)", "Far"], series.Bands.Skip(1).Select(band => band.Label));
        Assert.Equal([0, 3, 4, 2], series.Bands.Skip(1).Select(band => band.SeriesSlot));
        // Other is what the total says beyond the kept sources: nothing on the first day, 100 on the second.
        Assert.Equal([0d, null, 100d], series.Bands[0].Points.Select(point => point.Value));
        Assert.Equal([500d, null, 300d], series.Bands[1].Points.Select(point => point.Value));
    }

    [Fact]
    public void ByNodeWithNothingLeftOverHasNoOtherBand()
    {
        // Every byte of the second day is accounted for by the four kept sources, so nothing is left over for Other.
        var data = WithNodes();
        data.Points[1].BytesBySource["node:2"] = 300;
        var series = B.Chart(data, B.Metric.Volume, B.Scope.Nodes)!;
        Assert.Equal(["api", "node:2", "node:3", "node:9"], series.Bands.Select(band => band.Key));
    }

    [Fact]
    public void ThePeakByNodeViewIsOverlayLinesUnderANeutralTotalNotStackedBands()
    {
        var series = B.Chart(WithNodes(), B.Metric.Peak, B.Scope.Nodes)!;
        Assert.Equal("Total", series.SeriesLabel);
        Assert.True(series.NeutralTotal);
        Assert.Empty(series.Bands);
        Assert.Equal(["API server", "Edge (#2)", "Edge (#3)", "Far"], series.Overlays.Select(overlay => overlay.Label));
        Assert.Equal([0, 3, 4, 2], series.Overlays.Select(overlay => overlay.Slot));
        Assert.Equal([0.09, null, 0.05], series.Overlays[0].Points.Select(point => point.Value));
    }

    // ── The shared egress band rule ───────────────────────────

    [Fact]
    public void TheDashboardsBandsAndTheBandwidthChartTakeTheirColoursAndLabelsFromOneRule()
    {
        // The same sources as the Dashboard's Egress widget reads them: its bands and this tab's bands must agree for every source.
        var sources = new[]
        {
            Source("api", "api", "api", null, 500), Source("node:7", "node", "Edge", "7", 300), Source("node:3", "node", "Edge", "3", 150), Source("node:4", "node", "Solo", "4", 50),
        };
        var series = new DashboardTimeseries
        {
            ResolutionSeconds = 60,
            EgressSources = sources.Select(source => new DashboardEgressSource { Key = source.Key, Kind = source.Kind, Name = source.Name, NodeId = source.NodeId, EgressBytes = source.EgressBytes }).ToList(),
            Points = [new DashboardTimeseriesPoint { T = DateTimeOffset.UtcNow, EgressKbps = 1000, EgressBySource = sources.ToDictionary(source => source.Key, _ => 250L) }],
        };
        var dashboard = DashboardChartMath.EgressBands(series)!.Where(band => band.Key != "other").ToDictionary(band => band.Key);

        var data = WithNodes();
        data.Sources = [.. sources];
        var insights = B.Chart(data, B.Metric.Volume, B.Scope.Nodes)!.Bands.Where(band => band.Key != "other").ToDictionary(band => band.Key);

        Assert.Equal(dashboard.Keys.Order(), insights.Keys.Order());
        foreach (var key in dashboard.Keys) Assert.Equal(dashboard[key].SeriesSlot, insights[key].SeriesSlot);
        // The widget names the API by its own name; Insights says "API server". Every node label is the same.
        foreach (var key in dashboard.Keys.Where(key => key != "api")) Assert.Equal(dashboard[key].Label, insights[key].Label);
        Assert.Equal("Edge (#7)", insights["node:7"].Label);
        Assert.Equal("api", dashboard["api"].Label);
        Assert.Equal("API server", insights["api"].Label);
    }

    [Fact]
    public void ABandTakesTheNextFreeColourWhenTwoNodesWantTheSameOneAndTheLowerIdKeepsIt()
    {
        // 3 and 7 both prefer the fourth slot of the four (id mod 4); the API takes the first; the lower id keeps its own.
        var slots = AdminEgressBands.Slots(
        [
            new("node:7", "node", "B", "7"), new("node:3", "node", "A", "3"), new("api", "api", "api", null),
        ]);
        Assert.Equal(0, slots["api"]);
        Assert.Equal(4, slots["node:3"]);
        Assert.Equal(2, slots["node:7"]);
        Assert.NotEqual(slots["node:3"], slots["node:7"]);
        Assert.DoesNotContain(1, slots.Values);
        Assert.Equal(slots, AdminEgressBands.Slots(
        [
            new("api", "api", "api", null), new("node:3", "node", "A", "3"), new("node:7", "node", "B", "7"),
        ]));
    }

    [Fact]
    public void ANodeIdOfZeroIsNotAnIdentityAndTheApiLabelCanBeReplaced()
    {
        var sources = new EgressSourceIdentity[] { new("node:0", "node", "Edge", "0"), new("node:1", "node", "Edge", "1"), new("node:2", "node", "Edge", "2"), new("api", "api", "api", null) };
        var labels = AdminEgressBands.Labels(sources, "API server");
        Assert.Equal("Edge", labels["node:0"]);
        Assert.Equal("Edge (#1)", labels["node:1"]);
        Assert.Equal("Edge (#2)", labels["node:2"]);
        Assert.Equal("API server", labels["api"]);
        Assert.Equal("api", AdminEgressBands.Labels(sources)["api"]);
        // A name that only looks the same after the API label replaces the API's own name still counts as a clash.
        var clash = AdminEgressBands.Labels([new("api", "api", "api", null), new("node:5", "node", "API server", "5")], "API server");
        Assert.Equal("API server", clash["api"]);
        Assert.Equal("API server (#5)", clash["node:5"]);
    }

    // ── The node table ────────────────────────────────────────

    [Fact]
    public void TheNodeTableListsEachSourceWithItsTypeShareAndPeak()
    {
        var rows = B.NodeRows(WithNodes());
        Assert.Equal(["API server", "Edge", "Edge", "Far"], rows.Select(row => row.Name));
        Assert.Equal([null, "Proxy", "Transcode", null], rows.Select(row => row.TypeBadge));
        Assert.Equal(["500 B", "300 B", "150 B", "50 B"], rows.Select(row => row.DataSent));
        Assert.Equal(["50%", "30%", "15%", "5.0%"], rows.Select(row => row.SharePercent));
        Assert.Equal([0.5, 0.3, 0.15, 0.05], rows.Select(row => row.ShareFill));
        Assert.Equal(["90 Mbps", "70 Mbps", "40 Mbps", "10 Mbps"], rows.Select(row => row.Peak));
    }

    [Fact]
    public void ATypeThisBuildDoesNotKnowIsShownAsSentAndAShareIsClamped()
    {
        var data = Data();
        data.Sources = [Source("node:1", "node", "N", "1", 5, "relay", 1, 1.4), Source("node:2", "node", "M", "2", 5, "", 1, -0.2)];
        var rows = B.NodeRows(data);
        Assert.Equal(["relay", null], rows.Select(row => row.TypeBadge));
        Assert.Equal([1d, 0d], rows.Select(row => row.ShareFill));
        data.Sources = [];
        Assert.Empty(B.NodeRows(data));
    }
}
