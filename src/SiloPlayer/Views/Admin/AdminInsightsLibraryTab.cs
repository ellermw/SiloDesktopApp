using Microsoft.UI.Text;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using A = SiloPlayer.Controls.AdminMonitorUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Insights › Library (official <c>LibraryTab.tsx</c>): how the catalog is used. How much of each library was played, what nobody started,
/// which genres, ratings and decades people watch, and how soon new titles get their first play. A report only.
/// </summary>
internal sealed class AdminInsightsLibraryTab(AdminInsightsPage page) : AdminInsightsTabBase<AdminInsightsLibrary>(page)
{
    protected override string KeyFor(AdminInsightsFilterState filters) => AdminInsightsApi.LibraryPath(filters.ToWindow());

    protected override Task<AdminInsightsLibrary> LoadAsync(MonitorAuthorityLease lease, AdminInsightsFilterState filters, bool refresh, CancellationToken ct)
        => Page.Insights.GetLibraryAsync(lease, filters.ToWindow(), refresh, ct);

    protected override FrameworkElement BuildSkeleton() => AdminInsightsLayout.Stack(AdminInsightsLayout.KpiSkeleton(), AdminInsightsLayout.CardSkeleton(256));

    private static string I(string key, params (string, object?)[] values) => AdminInsightsText.T(key, values);

    protected override FrameworkElement BuildBody(AdminInsightsLibrary data, AdminInsightsFilterState filters)
    {
        var coverageTitle = I("web.admin.insights.library.coverage.title");
        Func<double, string> plays = value => AdminInsightsRules.PlayCount((long)Math.Round(value));
        Func<double, string> titles = value => AdminInsightsRules.TitleCount((long)Math.Round(value));
        FrameworkElement Plain(string title, string? subtitle, Func<FrameworkElement> body) => new AdminInsightsCard(title, subtitle, null, body);

        return AdminInsightsLayout.Stack(
            AdminInsightsLayout.KpiGrid(AdminInsightsLibraryPresentation.Kpis(data)),
            new AdminInsightsCard(coverageTitle, I("web.admin.insights.library.coverage.subtitle"),
                () => [new AdminInsightsPinButton(Page, DashboardRegistry.InsightsLibraryCoverage, coverageTitle)],
                () => AdminInsightsLayout.RowList(AdminInsightsLibraryPresentation.Coverage(data), DashboardInsightsPresentation.CoverageValueText,
                    I("web.admin.insights.library.coverage.empty"), seriesIndex: 2,
                    open: row => row.LibraryId is { } library ? Guarded(() => Page.NarrowToLibrary(library)) : null, max: 100)),
            AdminInsightsLayout.TwoUp(
                Plain(I("web.admin.insights.library.genres.title"), I("web.admin.insights.library.genres.subtitle"),
                    () => AdminInsightsLayout.RowList(AdminInsightsLibraryPresentation.PlaysRows(data.Genres), plays, I("web.admin.insights.activity.volume.empty"), seriesIndex: 0)),
                Plain(I("web.admin.insights.library.ratings.title"), null,
                    () => AdminInsightsLayout.RowList(AdminInsightsLibraryPresentation.PlaysRows(data.ContentRatings), plays, I("web.admin.insights.activity.volume.empty"), seriesIndex: 1))),
            AdminInsightsLayout.TwoUp(
                Plain(I("web.admin.insights.library.decades.title"), null, () => Decades(data)),
                Plain(I("web.admin.insights.library.firstPlay.title"), I("web.admin.insights.library.firstPlay.subtitle"),
                    () => AdminInsightsLayout.RowList(AdminInsightsLibraryPresentation.FirstPlayRows(data.TimeToFirstPlay), titles,
                        I("web.admin.insights.library.firstPlay.empty"), seriesIndex: 3))),
            Plain(I("web.admin.insights.library.unplayed.title"), I("web.admin.insights.library.unplayed.subtitle"), () => Unplayed(data)));
    }

    /// <summary>Plays by release decade, oldest first and unknown last, as indexed columns labelled "1990s".</summary>
    private static FrameworkElement Decades(AdminInsightsLibrary data)
    {
        if (AdminInsightsLibraryPresentation.DecadeColumns(data.Decades) is not { } shape)
            return AdminInsightsLayout.Empty(I("web.admin.insights.activity.volume.empty"));
        var labels = AdminInsightsLibraryPresentation.DecadeLabels(data.Decades);
        return DashboardInsightsCharts.Columns(shape,
            time => DashboardInsightsPresentation.ViewingIndex(time) is var index && index >= 0 && index < labels.Count ? labels[index] : "",
            DashboardInsightsCharts.WatchTimeOrCount(false), "", I("web.admin.insights.library.decades.title"), 180, 0);
    }

    /// <summary>The newest titles added this period that nobody has played, each a link to its title panel.</summary>
    private FrameworkElement Unplayed(AdminInsightsLibrary data)
    {
        var rows = AdminInsightsLibraryPresentation.Unplayed(data.RecentlyAddedUnplayed);
        if (rows.Count == 0)
        {
            var empty = Center(A.Text(I("web.admin.insights.library.unplayed.empty"), 12, A.Muted, trim: false));
            return new Border { Padding = new Thickness(0, 24, 0, 24), Child = empty };
        }
        var list = new StackPanel();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var open = Guarded(() => Page.OpenTitleFromList(row.ContentId));
            var title = DashboardUi.TextLink(row.Title, 12, (_, _) => open?.Invoke(), A.Foreground, underlineOnHover: true);
            title.FontWeight = FontWeights.Medium;
            title.Tag = DashboardBarList.LinkTag;
            title.HorizontalAlignment = HorizontalAlignment.Left;
            var added = A.Text(row.Added, 12, A.Muted, trim: false);
            var line = new AdminInsightsCardHeader(title, added, centered: false);
            list.Children.Add(new Border
            {
                Padding = new Thickness(0, 8, 0, 8), Child = line,
                BorderThickness = new Thickness(0, index == 0 ? 0 : 1, 0, 0), BorderBrush = A.Solid(A.Border, 0.5),
            });
        }
        return list;
    }

    private static TextBlock Center(TextBlock text)
    {
        text.TextAlignment = TextAlignment.Center;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        return text;
    }
}
