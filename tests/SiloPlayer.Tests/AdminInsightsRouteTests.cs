using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Insights navigation and the retired Activity route.</summary>
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
    public void MonitorListsInsightsBetweenDashboardAndLogs()
    {
        Assert.Equal([AdminRoute.Dashboard, AdminRoute.Insights, AdminRoute.ServerLog, AdminRoute.ClientDiagnostics, AdminRoute.LoggingSettings], AdminNavigation.MonitorRoutes);
        var monitor = AdminRouteCatalog.Sections[0];
        Assert.Collection(monitor.Entries,
            entry => Assert.Equal(AdminRoute.Dashboard, Assert.IsType<AdminNavLeaf>(entry).Route),
            entry => Assert.Equal(AdminRoute.Insights, Assert.IsType<AdminNavLeaf>(entry).Route),
            entry => Assert.Equal("logs", Assert.IsType<AdminNavTopic>(entry).Id));
        Assert.DoesNotContain(AdminNavigation.SearchPages, page => page.Route == AdminRoute.Activity);
        foreach (var term in new[] { "heatmap", "leaderboard", "trans" })
            Assert.Equal(AdminRoute.Insights, Assert.Single(AdminNavigation.Search(term)).Route);
    }

    [Fact]
    public void SearchAndOpeningRespectPageRegistration()
    {
        var registry = new AdminPageRegistry([]);
        Assert.False(registry.IsRegistered(AdminRoute.Insights));
        Assert.False(registry.CanOpen(new AdminRouteRequest(AdminRoute.Insights)));
        Assert.Empty(AdminNavigation.SearchGroups("heatmap", route => registry.IsRegistered(route)));
        Assert.Contains(AdminNavigation.SearchGroups("heatmap", route => route == AdminRoute.Insights).SelectMany(group => group.Pages), page => page.Route == AdminRoute.Insights);
    }

    [Fact]
    public void TheHrefResolvesWithItsInsightsQuery()
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/insights?tab=people&range=7d", out var insights));
        Assert.True(insights.OpensRoute);
        Assert.Equal(AdminRoute.Insights, insights.Request!.Route);
        Assert.Equal(("people", "7d"), (insights.Request.Get("tab"), insights.Request.Get("range")));
        Assert.Equal("/admin/insights?range=7d&tab=people", insights.Request.Href);
        Assert.True(AdminRouteCatalog.TryResolve("/admin/insights", out var bare));
        Assert.Equal(new AdminRouteRequest(AdminRoute.Insights), bare.Request);
        Assert.True(AdminRouteCatalog.IsValid(new AdminRouteRequest(AdminRoute.Insights)));
        Assert.False(AdminRouteCatalog.IsValid(new AdminRouteRequest(AdminRoute.Insights) { Segments = ["x"] }));
    }

    [Theory]
    [InlineData(null, AdminRoute.Insights, "/admin/insights")]
    [InlineData("streams", AdminRoute.Insights, "/admin/insights")]
    [InlineData("preparations", AdminRoute.PreparedFiles, "/admin/downloads?tab=preparation")]
    public void LegacyActivityRedirectsAndDropsItsQuery(string? view, AdminRoute destination, string href)
    {
        Assert.Equal("/admin/activity", AdminRouteCatalog.Leaf(AdminRoute.Activity).Href);
        Assert.Equal("radio", AdminRouteCatalog.Leaf(AdminRoute.Activity).Icon);
        var legacy = new AdminRouteRequest(AdminRoute.Activity).WithQuery("view", view).WithQuery("tab", "people");
        Assert.True(AdminRouteCatalog.TryResolve(legacy.Href, out var resolution));
        Assert.True(resolution.Redirected);
        Assert.Equal(destination, resolution.Request!.Route);
        Assert.Equal(href, resolution.Request.Href);
        Assert.Equal(resolution.Request, AdminRouteCatalog.RedirectLegacyActivity(legacy));
        var registry = new AdminPageRegistry([new(AdminRoute.Insights, typeof(string)), new(AdminRoute.PreparedFiles, typeof(int))]);
        Assert.True(registry.CanOpen(legacy));
        Assert.False(registry.IsRegistered(AdminRoute.Activity));
        Assert.Equal(AdminNavigation.TabTitle(destination), AdminNavigation.TabTitle(legacy));
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

    [Theory]
    [InlineData("0001-01-01", "2026-11-20", "2026-10-28", "2026-11-20")]
    [InlineData("2026-02-30", "2026-11-26", "2026-10-28", "2026-11-26")]
    [InlineData("2026-11-12", "9999-12-31", "2026-11-12", "2026-11-26")]
    [InlineData("2026-11-20", "2026-11-12", "2026-10-28", "2026-11-26")]
    [InlineData("1601-01-02", "1601-01-02", "1601-01-02", "1601-01-02")]
    public void CustomRouteDatesAreSafeAndOrdered(string from, string to, string expectedFrom, string expectedTo)
    {
        Assert.True(AdminRouteCatalog.TryResolve($"/admin/insights?tab=playback&range=custom&from={from}&to={to}", out var route));
        var filters = AdminInsightsFilterState.Parse(route.Request!, "Asia/Tokyo", new DateOnly(2026, 11, 26));
        Assert.Equal(AdminInsightsTab.Playback, filters.Tab);
        Assert.Equal((expectedFrom, expectedTo), (filters.FromDate, filters.ToDate));
        var window = filters.ToWindow();
        Assert.Equal((expectedFrom, expectedTo), (window.FromDate, window.ToDate));
    }
}
