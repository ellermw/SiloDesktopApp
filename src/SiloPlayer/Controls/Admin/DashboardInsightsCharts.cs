using Microsoft.UI.Xaml;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Controls.Admin;

/// <summary>
/// The Insights charts, built once for the two places official draws them: a dashboard card (<c>InsightsWidgets.tsx</c>, which fills the
/// card) and an Insights tab (<c>PlaybackTab.tsx</c>, a fixed plot height). The shape of the data comes from
/// <see cref="DashboardInsightsPresentation"/>; this only turns it into the chart control. A plot height of NaN fills the parent.
/// </summary>
internal static class DashboardInsightsCharts
{
    /// <param name="totalLabel">The tooltip's total row: its label, the default "Total" for null, and no row for an empty string (a single series has nothing to total, as the official omitted <c>totalLabel</c>).</param>
    public static FrameworkElement Columns(DashboardInsightsColumns shape, Func<DateTimeOffset, string> formatBucket, Func<double, string> formatValue,
        string? totalLabel, string accessibleLabel, double plotHeight, double minPlotHeight)
    {
        var chart = new DashboardStackedColumnChart
        {
            Columns = shape.Columns,
            SeriesLabels = shape.SeriesLabels,
            AccessibleLabel = accessibleLabel,
            FormatBucket = formatBucket,
            FormatValue = formatValue,
            TotalLabel = totalLabel,
            PlotHeight = plotHeight,
            MinPlotHeight = minPlotHeight,
        };
        chart.Build();
        return chart;
    }

    public static Func<double, string> WatchTimeOrCount(bool watchTime)
        => watchTime ? seconds => DashboardFormat.WatchTime((long)Math.Round(seconds)) : value => DashboardFormat.Integer((long)Math.Round(value));

    /// <summary>Playback over time for a view (method, media or watch time); null when the window has no plays to draw.</summary>
    public static FrameworkElement? Plays(AdminInsightsActivity data, string view, double plotHeight, double minPlotHeight)
    {
        if (DashboardInsightsPresentation.PlaysColumns(data, view) is not { } shape) return null;
        var watchTime = view == DashboardRegistry.InsightsViewWatchTime;
        return Columns(shape, time => DashboardInsightsPresentation.BucketLabel(time, data.BucketUnit), WatchTimeOrCount(watchTime),
            watchTime ? "" : AdminInsightsText.T("web.admin.insights.activity.volume.metricPlays"),
            AdminInsightsText.T("web.admin.insights.activity.volume.chartLabel"), plotHeight, minPlotHeight);
    }

    /// <summary>Plays or watch time by weekday or hour; null when every total is zero.</summary>
    public static FrameworkElement? Viewing(IReadOnlyList<AdminInsightsHeatmapCell> cells, DashboardInsightsPresentation.ViewingAxis axis, string view,
        string accessibleLabel, double plotHeight, double minPlotHeight)
    {
        if (DashboardInsightsPresentation.ViewingColumns(cells, axis, view) is not { } shape) return null;
        return Columns(shape, time => DashboardInsightsPresentation.ViewingLabel(axis, DashboardInsightsPresentation.ViewingIndex(time)),
            WatchTimeOrCount(view == DashboardRegistry.InsightsViewWatchTime), "", accessibleLabel, plotHeight, minPlotHeight);
    }

    /// <summary>Distinct profiles (the line) and accounts (the overlay) per bucket; null when nobody was active.</summary>
    public static FrameworkElement? ActivePeople(AdminInsightsActivity data, double plotHeight, double minPlotHeight)
    {
        if (DashboardInsightsPresentation.ActivePeople(data) is not { } series) return null;
        var chart = new DashboardLineChart
        {
            Points = series.Profiles,
            SeriesLabel = AdminInsightsText.T("web.admin.insights.people.kpi.activeProfiles"),
            Overlays = [(AdminInsightsText.T("web.admin.insights.people.kpi.activeAccounts"), series.Accounts, 1)],
            EdgeLabels = (series.StartLabel, series.EndLabel),
            FormatTimestamp = time => DashboardInsightsPresentation.BucketLabel(time, data.BucketUnit),
            MinTickStep = 1,
            AccessibleLabel = AdminInsightsText.T("web.admin.insights.activity.activePeople.title"),
            PlotHeight = plotHeight,
            MinPlotHeight = minPlotHeight,
        };
        chart.Build();
        return chart;
    }
}
