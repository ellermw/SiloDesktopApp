using Microsoft.UI.Xaml.Automation;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// Insights › Titles (official <c>TitlesTab.tsx</c>): the most watched movies, shows and libraries and the titles people gave up on, ranked by
/// plays, watch time or viewers. Rows open the title panel; a library narrows the page to it. While a new ranking loads, the old one stays on
/// screen and keeps the formatting of the sort it was ranked by.
/// </summary>
internal sealed class AdminInsightsTitlesTab(AdminInsightsPage page) : AdminInsightsTabBase<AdminInsightsTitles>(page)
{
    private AdminInsightsTitleSort _sort = AdminInsightsTitleSort.Plays;

    protected override string KeyFor(AdminInsightsFilterState filters)
        => AdminInsightsApi.TitlesPath(filters.ToWindow(), _sort, AdminInsightsApi.TitlesPageLimit);

    protected override Task<AdminInsightsTitles> LoadAsync(MonitorAuthorityLease lease, AdminInsightsFilterState filters, bool refresh, CancellationToken ct)
        => Page.Insights.GetTitlesAsync(lease, filters.ToWindow(), _sort, AdminInsightsApi.TitlesPageLimit, refresh, ct);

    protected override FrameworkElement BuildSkeleton() => Layout(null);

    protected override FrameworkElement BuildBody(AdminInsightsTitles data, AdminInsightsFilterState filters) => Layout(data);

    private static string I(string key, params (string, object?)[] values) => AdminInsightsText.T(key, values);

    private FrameworkElement Layout(AdminInsightsTitles? data)
    {
        var shown = AdminInsightsTitlesPresentation.ShownSort(data, _sort);
        Func<double, string> format = value => AdminInsightsTitlesPresentation.FormatMetric(shown, value);
        FrameworkElement List(IReadOnlyList<AdminInsightsBarRow>? rows, int seriesIndex, string empty, Func<double, string> formatValue)
            => rows == null
                ? DashboardChartChrome.BarListSkeleton()
                : AdminInsightsLayout.RowList(rows, formatValue, empty, seriesIndex, open: OpenRow);
        AdminInsightsCard Card(string title, string? subtitle, FrameworkElement body) => new(title, subtitle, null, () => body);

        return AdminInsightsLayout.Stack(
            TopRow(),
            AdminInsightsLayout.TwoUp(
                Card(I("web.admin.insights.titles.movies.title"), null,
                    List(data == null ? null : AdminInsightsTitlesPresentation.TitleRows(data.Movies, shown), 0, I("web.admin.insights.titles.movies.empty"), format)),
                Card(I("web.admin.insights.titles.shows.title"), null,
                    List(data == null ? null : AdminInsightsTitlesPresentation.TitleRows(data.Shows, shown), 1, I("web.admin.insights.titles.shows.empty"), format))),
            AdminInsightsLayout.TwoUp(
                Card(I("web.admin.insights.titles.libraries.title"), null,
                    List(data == null ? null : AdminInsightsTitlesPresentation.LibraryRows(data.Libraries, shown), 2, I("web.admin.insights.titles.libraries.empty"), format)),
                Card(I("web.admin.insights.titles.leastFinished.title"), I("web.admin.insights.titles.leastFinished.subtitle"),
                    List(data == null ? null : AdminInsightsTitlesPresentation.LeastFinishedRows(data.LeastFinished), 3,
                        I("web.admin.insights.titles.leastFinished.empty"), AdminInsightsTitlesPresentation.UnfinishedShare))));
    }

    /// <summary>"Add to dashboard" for the Top titles widget, and the rank-by toggle, at the end of the row.</summary>
    private FrameworkElement TopRow()
    {
        var row = new DashboardWrapPanel { Spacing = 8, RowSpacing = 8, JustifyEnd = true };
        row.Children.Add(new AdminInsightsPinButton(Page, DashboardRegistry.TopTitles, DashboardText.T("web.admin.dashboard.widgetCatalog.topTitles.title")));
        var toggle = AdminInsightsLayout.Toggle(
            AdminInsightsTitlesPresentation.Sorts.Select(sort => (sort, I(sort.LabelKey()), I(sort.LabelKey()))).ToArray(), _sort, ChooseSort);
        AutomationProperties.SetName(toggle, I("web.admin.insights.titles.sortLabel"));
        row.Children.Add(toggle);
        return row;
    }

    private void ChooseSort(AdminInsightsTitleSort sort)
    {
        if (_sort == sort) return;
        _sort = sort;
        Reload();
    }

    private Action? OpenRow(AdminInsightsBarRow row)
    {
        if (row.TitleId is { } title) return Guarded(() => Page.OpenTitleFromList(title));
        if (row.LibraryId is { } library) return Guarded(() => Page.NarrowToLibrary(library));
        return null;
    }
}
