using SiloPlayer.Core.Services.Admin;
using Present = SiloPlayer.Core.Services.Admin.AdminBackupPresentation;

namespace SiloPlayer.Tests;

/// <summary>The Backups route family resolves natively (formerly Unsupported) and opens only once registered.</summary>
public sealed class AdminBackupsNavigationTests
{
    [Theory]
    [InlineData("/admin/backups", new string[0], "/admin/backups")]
    [InlineData("/admin/backups/4/restore", new[] { "4", "restore" }, "/admin/backups/4/restore")]
    [InlineData("/admin/backups/20261115T040000Z-nightly/restore?job=job-1", new[] { "20261115T040000Z-nightly", "restore" }, "/admin/backups/20261115T040000Z-nightly/restore?job=job-1")]
    [InlineData("/admin/backups/uploads/0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b/restore", new[] { "uploads", "0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b", "restore" },
        "/admin/backups/uploads/0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b/restore")]
    public void EveryBackupsRouteResolvesToTheBackupsLeafAndNeedsItsRegistration(string href, string[] segments, string canonical)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(AdminRouteResolutionKind.Route, resolution.Kind);
        Assert.Equal(AdminRoute.Backups, resolution.Request!.Route);
        Assert.Equal(segments, resolution.Request.Segments);
        Assert.Equal(canonical, resolution.CanonicalHref);
        Assert.False(resolution.Redirected);
        Assert.False(new AdminPageRegistry([]).CanOpen(resolution.Request));
        Assert.True(new AdminPageRegistry([new(AdminRoute.Backups, typeof(object))]).CanOpen(resolution.Request));
    }

    [Theory]
    [InlineData("/admin/backups/uploads")]
    [InlineData("/admin/backups/x/y/z/w")]
    [InlineData("/admin/backups/b1/delete")]
    public void PathsOutsideTheFamilyFallBackToTheDashboardCatchAll(string href)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.True(resolution.IsCatchAll);
        Assert.Equal(AdminRoute.Dashboard, resolution.Request!.Route);
    }

    [Fact]
    public void PluginsStayUnsupportedAndMaintenanceKeepsItsOwnRoute()
    {
        foreach (var href in new[] { "/admin/plugins", "/admin/plugins/arr" })
        {
            Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
            Assert.Equal(AdminRouteResolutionKind.Unsupported, resolution.Kind);
        }
        Assert.True(AdminRouteCatalog.TryResolve("/admin/maintenance", out var maintenance));
        Assert.Equal(AdminRouteResolutionKind.Route, maintenance.Kind);
        Assert.Equal(AdminRoute.Maintenance, maintenance.Request!.Route);
    }

    [Theory]
    [InlineData("back up now")]
    [InlineData("retention")]
    public void BackupsIsSearchableOnlyWhenRegistered(string query)
    {
        Assert.Empty(AdminNavigation.SearchGroups(query, new AdminPageRegistry([]).IsRegistered));
        var registry = new AdminPageRegistry([new(AdminRoute.Backups, typeof(object))]);
        Assert.Contains(AdminNavigation.SearchGroups(query, registry.IsRegistered).SelectMany(group => group.Pages), page => page.Route == AdminRoute.Backups);
    }

    [Fact]
    public void RestoreLinksBuildTheOfficialHrefs()
    {
        Assert.Equal("/admin/backups/20261115T040000Z-nightly/restore", Present.RestoreRequest("20261115T040000Z-nightly").Href);
        Assert.Equal("/admin/backups/uploads/0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b/restore", Present.UploadRestoreRequest("0f1e2d3c-4b5a-6978-8a9b-0c1d2e3f4a5b").Href);
        Assert.Equal("/admin/backups/uploads/u-1/restore", Present.ActiveRestoreRequest(new() { Operation = "restore", UploadId = "u-1", BackupId = "b" })!.Href);
        Assert.Null(Present.ActiveRestoreRequest(new() { Operation = "restore" }));
    }

    [Fact]
    public void EveryBackupsCatalogKeyResolvesToTextAndTheCatalogFormats()
    {
        var keys = AdminText.Keys.Where(key => key.StartsWith("web.admin.backups.", StringComparison.Ordinal) || key.StartsWith("configure.native.backups.", StringComparison.Ordinal)).ToArray();
        Assert.True(keys.Length > 580);
        Assert.All(keys, key =>
        {
            Assert.NotEqual(key, AdminText.Pattern(key));
            // Every official pattern formats with plausible values without throwing.
            AdminText.T(key, new Dictionary<string, object?> { ["count"] = 2L, ["count_text"] = "2", ["kind"] = "manual", ["day"] = "today", ["copy"] = "today", ["when"] = "today",
                ["source"] = "this", ["section"] = "users", ["field"] = "max_streams", ["chosen"] = 1L, ["keep"] = 7L });
        });
        Assert.Equal("Restore… from Nov 15", AdminBackupText.Accessible("web.admin.backups.table.restore", new { taken = "Nov 15" }));
        Assert.Equal("Restore…", AdminBackupText.Visible("web.admin.backups.table.restore", new { taken = "Nov 15" }));
        Assert.Contains(AdminBackupText.Runs("web.admin.backups.download.encrypted", fills: new Dictionary<string, string> { ["decrypt"] = Present.DecryptCommand }),
            run => run.Tag == "decrypt" && run.Text == Present.DecryptCommand);
    }
}
