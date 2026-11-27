using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// <see cref="AdminRoute.Insights"/> as this unit leaves it: a route identity with a leaf (href, icon, page key, official keywords and
/// titles) that no page is registered for, so the shell neither lists nor opens it, and Activity and every other route unchanged.
/// </summary>
public sealed class AdminInsightsRouteTests
{
    [Fact]
    public void InsightsIsAppendedAtTheEndAndNoExistingValueMoved()
    {
        Assert.Equal(AdminRoute.Insights, Enum.GetValues<AdminRoute>().Max());
        Assert.Equal(45, (int)AdminRoute.Insights);
        Assert.Equal(44, (int)AdminRoute.CompatibilitySettings);
        Assert.Equal(0, (int)AdminRoute.Dashboard);
        Assert.Equal(1, (int)AdminRoute.Activity);
        Assert.Equal(AdminRoute.Insights, Enum.GetValues<AdminRoute>().Last());
    }

    [Fact]
    public void TheLeafIsTheOfficialInsightsPage()
    {
        var leaf = AdminRouteCatalog.Leaf(AdminRoute.Insights);
        Assert.Equal("/admin/insights", leaf.Href);
        Assert.Equal("bar-chart-3", leaf.Icon);
        Assert.False(leaf.Exact);
        Assert.Equal(AdminRouteDescendants.None, leaf.Descendants);
        Assert.Equal("web.admin.shell.nav.page.insights.label", leaf.SidebarLabelKey);
        Assert.Equal("web.admin.shell.nav.page.insights.description", leaf.DescriptionKey);
        Assert.Equal("web.admin.common.title.header.insights", leaf.HeaderTitleKey);
        Assert.Equal("web.admin.common.title.tab.insights", leaf.TabTitleKey);
        Assert.Equal(["activity", "live", "streams", "sessions", "now playing", "analytics", "insights", "playback", "watch time", "bandwidth", "egress", "devices",
            "codecs", "transcode", "heatmap", "leaderboard"], leaf.Keywords);
        Assert.Equal("monitor", AdminRouteCatalog.SectionOf(AdminRoute.Insights).Id);
        Assert.Null(AdminRouteCatalog.TopicOf(AdminRoute.Insights));
        Assert.Empty(AdminRouteCatalog.MissingTextKeys(AdminRoute.Insights));
        Assert.Equal("Insights", AdminNavigation.SidebarLabel(AdminRoute.Insights));
        Assert.Equal("Insights", AdminNavigation.HeaderTitle(AdminRoute.Insights));
        Assert.Equal("Admin Insights", AdminNavigation.TabTitle(AdminRoute.Insights));
        Assert.Equal("Insights", AdminNavigation.HeaderTitle(new AdminRouteRequest(AdminRoute.Insights, new Dictionary<string, string> { ["tab"] = "people" })));
    }

    [Fact]
    public void NothingShowsOrSearchesTheRouteUntilAPageIsRegistered()
    {
        // The shell lists and opens only registered pages (AdminPageRegistry); the always-available Monitor routes stay as they were.
        Assert.DoesNotContain(AdminRoute.Insights, AdminNavigation.MonitorRoutes);
        Assert.Equal([AdminRoute.Dashboard, AdminRoute.Activity, AdminRoute.ServerLog, AdminRoute.ClientDiagnostics, AdminRoute.LoggingSettings], AdminNavigation.MonitorRoutes);
        Assert.DoesNotContain(AdminNavigation.SearchPages, page => page.Route == AdminRoute.Insights);
        Assert.Empty(AdminNavigation.Search("heatmap"));
        Assert.Empty(AdminNavigation.Search("leaderboard"));
        Assert.Equal(AdminRoute.Activity, Assert.Single(AdminNavigation.Search("trans")).Route);
        var registry = new AdminPageRegistry([]);
        Assert.False(registry.IsRegistered(AdminRoute.Insights));
        Assert.False(registry.CanOpen(new AdminRouteRequest(AdminRoute.Insights)));
        // A search that allows it finds it by the official keywords.
        Assert.Contains(AdminNavigation.SearchGroups("heatmap", route => route == AdminRoute.Insights).SelectMany(group => group.Pages), page => page.Route == AdminRoute.Insights);
    }

    [Fact]
    public void TheHrefResolvesAndActivityStillResolvesToItself()
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/insights?tab=people&range=7d", out var insights));
        Assert.True(insights.OpensRoute);
        Assert.Equal(AdminRoute.Insights, insights.Request!.Route);
        Assert.Equal(("people", "7d"), (insights.Request.Get("tab"), insights.Request.Get("range")));
        Assert.Equal("/admin/insights?range=7d&tab=people", insights.Request.Href);
        Assert.True(AdminRouteCatalog.TryResolve("/admin/insights", out var bare));
        Assert.Equal(new AdminRouteRequest(AdminRoute.Insights), bare.Request);
        // Another unit swaps Activity for Insights; this one changes neither the route nor its href.
        Assert.True(AdminRouteCatalog.TryResolve("/admin/activity", out var activity));
        Assert.Equal(AdminRoute.Activity, activity.Request!.Route);
        Assert.Equal("/admin/activity", AdminRouteCatalog.Leaf(AdminRoute.Activity).Href);
        Assert.Equal("radio", AdminRouteCatalog.Leaf(AdminRoute.Activity).Icon);
        Assert.True(AdminRouteCatalog.IsValid(new AdminRouteRequest(AdminRoute.Insights)));
        Assert.False(AdminRouteCatalog.IsValid(new AdminRouteRequest(AdminRoute.Insights) { Segments = ["x"] }));
    }

    [Fact]
    public void TheFilterStateWritesTheInsightsRoute()
    {
        var state = AdminInsightsFilterState.Parse(new Dictionary<string, string> { ["tab"] = "devices", ["range"] = "7d" }, "UTC", new DateOnly(2026, 11, 26));
        var request = state.ToQuery("UTC").ToRequest();
        Assert.Equal(AdminRoute.Insights, request.Route);
        Assert.Equal("/admin/insights?range=7d&tab=devices", request.Href);
        Assert.True(AdminRouteCatalog.TryResolve(request.Href, out var resolved));
        Assert.Equal(request, resolved.Request);
    }
}
