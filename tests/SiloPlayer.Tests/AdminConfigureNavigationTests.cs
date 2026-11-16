using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminConfigureNavigationTests
{
    [Fact]
    public void ConfigureCatalogTracksAllNineteenLeavesWithoutRegisteringPlaceholders()
    {
        var topics = AdminRouteCatalog.Sections.Single(section => section.Id == "configure").Entries.Cast<AdminNavTopic>().ToArray();
        Assert.Equal(["server", "access", "integrations"], topics.Select(topic => topic.Id));
        Assert.Equal(19, topics.Sum(topic => topic.Leaves.Count));
        Assert.Equal(45, AdminRouteCatalog.Leaves.Count());
        var registry = new AdminPageRegistry([]);
        Assert.All(topics.SelectMany(topic => topic.Leaves), leaf => Assert.False(registry.IsRegistered(leaf.Route)));
    }

    [Theory]
    [InlineData("/admin/settings/subtitles", AdminRoute.ProviderSettings, "/admin/settings/providers")]
    [InlineData("/admin/settings/providers", AdminRoute.ProviderSettings, "/admin/settings/providers")]
    [InlineData("/admin/settings/branding", AdminRoute.AppearanceSettings, "/admin/settings/appearance")]
    [InlineData("/admin/settings?tab=jellyfin", AdminRoute.CompatibilitySettings, "/admin/settings/compatibility")]
    public void ConfigureAliasesKeepTheirOwnIdentityButNeedARegisteredPage(string href, AdminRoute route, string canonical)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(AdminRouteResolutionKind.Route, resolution.Kind);
        Assert.Equal(route, resolution.Request!.Route);
        Assert.Equal(canonical, resolution.CanonicalHref);
        Assert.False(new AdminPageRegistry([]).CanOpen(resolution.Request));
    }

    [Fact]
    public void NestedToolsKeepTheirFullRouteScopeWithoutExposingPages()
    {
        Assert.True(AdminRouteCatalog.IsValid(new(AdminRoute.ScheduledTasks) { Segments = ["library-scan"] }));
        Assert.True(AdminRouteCatalog.IsValid(new(AdminRoute.Plugins) { Segments = ["plugin-1"] }));
        Assert.True(AdminRouteCatalog.IsValid(new(AdminRoute.Backups) { Segments = ["b1", "restore"] }));
        Assert.True(AdminRouteCatalog.IsValid(new(AdminRoute.Backups) { Segments = ["uploads", "u1", "restore"] }));
        Assert.False(AdminRouteCatalog.IsValid(new(AdminRoute.Backups) { Segments = ["u1"] }));
        Assert.False(AdminRouteCatalog.IsValid(new(AdminRoute.ScheduledTasks) { Segments = ["scan", "edit"] }));
        Assert.Equal("web.admin.common.title.tab.task", AdminNavigation.TitleKeys(new(AdminRoute.ScheduledTasks) { Segments = ["scan"] }).Tab);
    }
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

    [Theory]
    [InlineData("smtp")]
    [InlineData("test email")]
    [InlineData("mail server")]
    public void EmailIsAnIntegrationsLeafSearchableOnlyWhenRegistered(string query)
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/settings/email", out var resolution));
        Assert.Equal(AdminRoute.EmailSettings, resolution.Request!.Route);
        var topic = AdminRouteCatalog.Sections.Single(section => section.Id == "configure").Entries.Cast<AdminNavTopic>()
            .Single(entry => entry.Leaves.Any(leaf => leaf.Route == AdminRoute.EmailSettings));
        Assert.Equal("integrations", topic.Id);
        Assert.False(new AdminPageRegistry([]).CanOpen(resolution.Request));
        Assert.Empty(AdminNavigation.SearchGroups(query, new AdminPageRegistry([]).IsRegistered));
        var registry = new AdminPageRegistry([new(AdminRoute.EmailSettings, typeof(object))]);
        Assert.True(registry.CanOpen(resolution.Request));
        Assert.Equal(AdminRoute.EmailSettings, Assert.Single(Assert.Single(AdminNavigation.SearchGroups(query, registry.IsRegistered)).Pages).Route);
    }

    [Theory]
    [InlineData("bandwidth")]
    [InlineData("disk ceiling")]
    [InlineData("prepared file directory")]
    public void DownloadSettingsIsADownloadsTopicLeafSearchableOnlyWhenRegistered(string query)
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/settings/downloads", out var resolution));
        Assert.Equal(AdminRoute.DownloadSettings, resolution.Request!.Route);
        var topic = AdminRouteCatalog.Sections.Single(section => section.Id == "manage").Entries.Cast<AdminNavTopic>()
            .Single(entry => entry.Leaves.Any(leaf => leaf.Route == AdminRoute.DownloadSettings));
        Assert.Equal("downloads", topic.Id);
        Assert.False(new AdminPageRegistry([]).CanOpen(resolution.Request));
        Assert.Empty(AdminNavigation.SearchGroups(query, new AdminPageRegistry([]).IsRegistered));
        var registry = new AdminPageRegistry([new(AdminRoute.DownloadSettings, typeof(object))]);
        Assert.True(registry.CanOpen(resolution.Request));
        Assert.Equal(AdminRoute.DownloadSettings, Assert.Single(Assert.Single(AdminNavigation.SearchGroups(query, registry.IsRegistered)).Pages).Route);
    }

    [Fact]
    public void EveryDownloadSettingsLabelResolvesToCatalogTextNotAKey()
    {
        var keys = new[]
        {
            "web.admin.settings.downloads.title", "web.admin.settings.downloads.group", "web.admin.settings.downloads.loadError", "web.admin.settings.downloads.storageLink",
            "web.admin.settings.downloads.enabled.label", "web.admin.settings.downloads.enabled.description",
            "web.admin.settings.downloads.transcode.label", "web.admin.settings.downloads.transcode.description",
            "web.admin.settings.downloads.prepares.label", "web.admin.settings.downloads.prepares.description",
            "web.admin.settings.downloads.artifactDir.label", "web.admin.settings.downloads.artifactDir.description",
            "web.admin.settings.downloads.budget.label", "web.admin.settings.downloads.budget.hint",
            "web.admin.settings.downloads.diskCeiling.label", "web.admin.settings.downloads.diskCeiling.description",
            "web.admin.settings.downloads.serverBandwidth.label", "web.admin.settings.downloads.serverBandwidth.hint",
            "web.admin.settings.downloads.perUser.heading", "web.admin.settings.downloads.perUser.bandwidth.label",
            "web.admin.settings.downloads.perUser.concurrent.label", "web.admin.settings.downloads.perUser.concurrent.hint",
            "web.admin.settings.downloads.perUser.perPeriod.label", "web.admin.settings.downloads.perUser.perPeriod.hint",
            "web.admin.settings.downloads.perUser.period.label", "web.admin.settings.downloads.perUser.period.description",
            "web.admin.settings.downloads.server.heading", "web.admin.settings.downloads.server.localFallback.label", "web.admin.settings.downloads.server.localFallback.description",
            "web.admin.settings.downloads.server.cacheHours.label", "web.admin.settings.downloads.server.cacheHours.description",
            "web.admin.common.units.gigabytes", "web.admin.common.units.megabitsPerSecond", "web.settings.limitField.unlimited",
            "web.settings.pathSettingField.reset", "web.settings.advancedSection.title",
        };
        Assert.All(keys, key => Assert.NotEqual(key, AdminText.T(key)));
        Assert.Equal("Reset Prepared file directory to default", AdminText.T("web.settings.pathSettingField.resetLabel", new { label = "Prepared file directory" }));
        Assert.Equal("Advanced · 6 settings", AdminText.T("web.settings.advancedSection.accessibleLabel", new Dictionary<string, object?> { ["title"] = "Advanced", ["count"] = 6L, ["count_text"] = "6" }));
        Assert.Equal("Advanced · 1 setting", AdminText.T("web.settings.advancedSection.accessibleLabel", new Dictionary<string, object?> { ["title"] = "Advanced", ["count"] = 1L, ["count_text"] = "1" }));
        Assert.Equal("Leave blank for a silo-download-artifacts folder beside this server's transcode directory. Transcode nodes use download-artifacts inside their own transcode directory unless a node has its own directory.",
            AdminText.T("web.admin.settings.downloads.artifactDir.description"));
    }
}
