using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminConfigureNavigationTests
{
    [Theory]
    [InlineData("/admin/settings/general")]
    [InlineData("/admin/settings")]
    [InlineData("/admin/settings/database")]
    [InlineData("/admin/settings/retired-id")]
    public void GeneralResolvesButRequiresRegistration(string href)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(AdminRouteResolutionKind.Route, resolution.Kind);
        Assert.Equal("/admin/settings/general", resolution.CanonicalHref);
        Assert.NotNull(resolution.Request);
        var empty = new AdminPageRegistry([]);
        Assert.False(empty.CanOpen(resolution.Request));
        var registered = new AdminPageRegistry([new(resolution.Request.Route, typeof(object))]);
        Assert.True(registered.CanOpen(resolution.Request));
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("configuration")]
    [InlineData("redis")]
    public void SearchShowsGeneralOnlyWhenRegistered(string query)
    {
        var request = new AdminRouteRequest(AdminRoute.GeneralSettings);
        var empty = new AdminPageRegistry([]);
        Assert.Empty(AdminNavigation.SearchGroups(query, empty.IsRegistered));
        var registry = new AdminPageRegistry([new(request.Route, typeof(object))]);
        Assert.Equal(AdminRoute.GeneralSettings, Assert.Single(Assert.Single(AdminNavigation.SearchGroups(query, registry.IsRegistered)).Pages).Route);
    }
}
