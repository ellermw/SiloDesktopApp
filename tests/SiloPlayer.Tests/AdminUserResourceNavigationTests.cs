using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Access Groups, Devices and History Import are reachable through the real Admin navigation: registered, never "unsupported", with catalog text.</summary>
public sealed class AdminUserResourceNavigationTests
{
    private static readonly AdminPageRegistry Registry = new(
    [
        new(AdminRoute.AccessGroups, typeof(object)), new(AdminRoute.Devices, typeof(object)), new(AdminRoute.HistoryImport, typeof(object)),
    ]);

    [Theory]
    [InlineData("/admin/access-groups", AdminRoute.AccessGroups)]
    [InlineData("/admin/access-groups/3", AdminRoute.AccessGroups)]
    [InlineData("/admin/devices", AdminRoute.Devices)]
    [InlineData("/admin/devices/7/desktop", AdminRoute.Devices)]
    [InlineData("/admin/devices/7/device%201?profile=p7", AdminRoute.Devices)]
    [InlineData("/admin/history-import", AdminRoute.HistoryImport)]
    [InlineData("/admin/history-import?source=3", AdminRoute.HistoryImport)]
    public void EveryLocationOfTheFamilyResolvesToARegisteredRouteAndOpens(string href, AdminRoute route)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(AdminRouteResolutionKind.Route, resolution.Kind);
        Assert.Equal(route, resolution.Request!.Route);
        Assert.True(Registry.CanOpen(resolution.Request));
        Assert.Empty(AdminRouteCatalog.MissingTextKeys(route));
    }

    [Fact]
    public void ADeviceNeedsBothItsAccountAndItsDeviceAndNothingElseOpens()
    {
        Assert.False(Registry.CanOpen(new AdminRouteRequest(AdminRoute.Devices) { Segments = ["7"] }));
        Assert.False(Registry.CanOpen(new AdminRouteRequest(AdminRoute.AccessGroups) { Segments = ["3", "extra"] }));
        Assert.False(new AdminPageRegistry([]).CanOpen(new AdminRouteRequest(AdminRoute.AccessGroups)));
        Assert.False(Registry.CanOpen(new AdminRouteRequest(AdminRoute.Users)));
    }

    [Fact]
    public void TheRealAppRegistersTheThreePagesAndTheInstallerApprovesTheirCompiledMarkup()
    {
        var root = TestRepository.Root;
        var registration = File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "Views", "Admin", "AdminUserResourcesRegistration.cs"));
        foreach (var route in new[] { "AccessGroups", "Devices", "HistoryImport" }) Assert.Contains($"AdminRoute.{route}", registration);
        Assert.Contains("services.AddAdminUserResources();", File.ReadAllText(Path.Combine(root, "src", "SiloPlayer", "App.xaml.cs")));
        var installer = File.ReadAllText(Path.Combine(root, "installer", "build.ps1"));
        foreach (var page in new[] { "AdminAccessGroupsPage", "AdminDevicesPage", "AdminHistoryImportPage" }) Assert.Contains($"'Views/Admin/{page}.xbf'", installer);
    }
}
