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
    [InlineData("tailscale")]
    [InlineData("overlay")]
    [InlineData("port forwarding")]
    public void NetworkAccessIsAServerLeafSearchableOnlyWhenRegistered(string query)
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/settings/network-access", out var resolution));
        Assert.Equal(AdminRoute.NetworkAccessSettings, resolution.Request!.Route);
        Assert.Equal("/admin/settings/network-access", resolution.CanonicalHref);
        var topic = AdminRouteCatalog.Sections.Single(section => section.Id == "configure").Entries.Cast<AdminNavTopic>()
            .Single(entry => entry.Leaves.Any(leaf => leaf.Route == AdminRoute.NetworkAccessSettings));
        Assert.Equal("server", topic.Id);
        Assert.Equal(["GeneralSettings", "AppearanceSettings", "StorageSettings", "SearchSettings", "NetworkAccessSettings"],
            topic.Leaves.Take(5).Select(leaf => leaf.Route.ToString()));
        Assert.Equal("Network Access", AdminText.T(topic.Leaves.Single(leaf => leaf.Route == AdminRoute.NetworkAccessSettings).SidebarLabelKey));
        Assert.False(new AdminPageRegistry([]).CanOpen(resolution.Request));
        Assert.Empty(AdminNavigation.SearchGroups(query, new AdminPageRegistry([]).IsRegistered));
        var registry = new AdminPageRegistry([new(AdminRoute.NetworkAccessSettings, typeof(object))]);
        Assert.True(registry.CanOpen(resolution.Request));
        Assert.Equal(AdminRoute.NetworkAccessSettings, Assert.Single(Assert.Single(AdminNavigation.SearchGroups(query, registry.IsRegistered)).Pages).Route);
    }

    [Fact]
    public void ProductionConfigureRegistrationIncludesTheFinishedNetworkAccessPage()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "AdminConfigureServices.cs"));
        Assert.Contains("new AdminPageRegistration(AdminRoute.NetworkAccessSettings, typeof(AdminNetworkAccessSettingsPage))", source);
        Assert.Contains("AddSingleton<AdminNetworkAccessApi>", source);
        Assert.True(File.Exists(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", "AdminNetworkAccessSettingsPage.cs")));
        var installer = File.ReadAllText(Path.Combine(TestRepository.Root, "installer", "build.ps1"));
        Assert.Contains("'Views/Admin/AdminNetworkAccessSettingsPage.xbf'", installer);
    }

    [Fact]
    public void NetworkAccessCatalogCarriesEveryOfficialPageMessage()
    {
        foreach (var key in new[]
        {
            "web.admin.settings.network.page.title", "web.admin.settings.network.page.intro", "web.admin.settings.network.page.noProvidersTitle",
            "web.admin.settings.network.page.noProvidersBody", "web.admin.settings.network.page.providersFailed", "web.admin.settings.network.provider.description",
            "web.admin.settings.network.provider.noHosts", "web.admin.settings.network.provider.statusFailed", "web.admin.settings.network.host.apiServer",
            "web.admin.settings.network.host.proxyNode", "web.admin.settings.network.host.namedRole", "web.admin.settings.network.host.reachAt",
            "web.admin.settings.network.host.reachAtWithHostname", "web.admin.settings.network.host.authorize", "web.admin.settings.network.host.lastHeard",
            "web.admin.settings.network.host.connect", "web.admin.settings.network.host.disconnect", "web.admin.settings.network.host.startPluginFirst",
            "web.admin.settings.network.state.disconnected", "web.admin.settings.network.state.awaitingAuthorization", "web.admin.settings.network.state.connecting",
            "web.admin.settings.network.state.connected", "web.admin.settings.network.state.error", "web.admin.settings.network.state.unavailable",
            "web.admin.settings.hooks.networkAccess.connectFailed", "web.admin.settings.hooks.networkAccess.disconnectFailed",
        })
            Assert.True(AdminText.Has(key), key);
        Assert.Equal("Network Access", AdminText.T("web.admin.settings.network.page.title"));
        Assert.Equal("Waiting for authorization", AdminText.T("web.admin.settings.network.state.awaitingAuthorization"));
        Assert.Equal("Plugin not running", AdminText.T("web.admin.settings.network.state.unavailable"));
        Assert.Equal("Living Room · API server", AdminText.T("web.admin.settings.network.host.namedRole", new { name = "Living Room", role = "API server" }));
        Assert.Equal("Access Silo through your Tailscale network without port forwarding.",
            AdminText.T("web.admin.settings.network.provider.description", new { name = "Tailscale" }));
        Assert.Equal("Last heard from 1/2/2026", AdminText.T("web.admin.settings.network.host.lastHeard", new { time = "1/2/2026" }));
    }

    [Fact]
    public void RichCatalogLinksSplitBeforeUntrustedValuesAreInserted()
    {
        var runs = AdminNetworkAccessText.Rich("web.admin.settings.network.host.reachAtWithHostname",
            new { origin = "https://x.example.test/<link>spoof</link>", hostname = "x<muted>" });
        Assert.Equal([("", "Clients reach this host at "), ("link", "https://x.example.test/<link>spoof</link>"), ("", " (x<muted>)")],
            runs.Select(run => (run.Tag, run.Text)));
        Assert.Equal([("link", "Open the authorization page"), ("muted", " to finish enrolling this host.")],
            AdminNetworkAccessText.Rich("web.admin.settings.network.host.authorize").Select(run => (run.Tag, run.Text)));
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

    [Theory]
    [InlineData("bucket")]
    [InlineData("cloudflare")]
    [InlineData("artwork.storage_backend")]
    public void StorageIsAServerLeafSearchableOnlyWhenRegistered(string query)
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/settings/storage", out var resolution));
        Assert.Equal(AdminRoute.StorageSettings, resolution.Request!.Route);
        Assert.True(AdminRouteCatalog.TryResolve("/admin/settings/infrastructure", out var alias));
        Assert.Equal("/admin/settings/storage", alias.CanonicalHref);
        var topic = AdminRouteCatalog.Sections.Single(section => section.Id == "configure").Entries.Cast<AdminNavTopic>()
            .Single(entry => entry.Leaves.Any(leaf => leaf.Route == AdminRoute.StorageSettings));
        Assert.Equal("server", topic.Id);
        Assert.Equal("Storage", AdminText.T(topic.Leaves.Single(leaf => leaf.Route == AdminRoute.StorageSettings).SidebarLabelKey));
        Assert.Empty(AdminRouteCatalog.MissingTextKeys(AdminRoute.StorageSettings));
        Assert.False(new AdminPageRegistry([]).CanOpen(resolution.Request));
        var registry = new AdminPageRegistry([new(AdminRoute.StorageSettings, typeof(object))]);
        Assert.True(registry.CanOpen(resolution.Request));
        Assert.Contains(AdminNavigation.SearchGroups(query, registry.IsRegistered).SelectMany(group => group.Pages), page => page.Route == AdminRoute.StorageSettings);
    }

    [Fact]
    public void ProductionConfigureRegistrationIncludesTheFinishedStoragePage()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "AdminConfigureServices.cs"));
        Assert.Contains("new AdminPageRegistration(AdminRoute.StorageSettings, typeof(AdminStorageSettingsPage))", source);
        Assert.Contains("AddSingleton<AdminStorageApi>", source);
        Assert.Contains("Concat(AdminStorageSettingsController.Keys)", source);
        Assert.Contains("Concat(AdminStorageSettingsController.Secrets)", source);
        var installer = File.ReadAllText(Path.Combine(TestRepository.Root, "installer", "build.ps1"));
        Assert.Contains("'Views/Admin/AdminStorageSettingsPage.xbf'", installer);
        // The storage domain API stays separate from Monitor and never targets unrelated administration.
        var api = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer.Core", "Api", "AdminStorageApi.cs"));
        Assert.DoesNotContain("/api/v1/", api);
        Assert.DoesNotContain("AdminMonitorApi", api);
    }

    [Fact]
    public void ProductionConfigureRegistrationIncludesScheduledTasksUnderServer()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "AdminConfigureServices.cs"));
        Assert.Contains("new AdminPageRegistration(AdminRoute.ScheduledTasks, typeof(AdminScheduledTasksPage))", source);
        Assert.Contains("AddSingleton<AdminTasksApi>", source);
        var installer = File.ReadAllText(Path.Combine(TestRepository.Root, "installer", "build.ps1"));
        Assert.Contains("'Views/Admin/AdminScheduledTasksPage.xbf'", installer);
        Assert.Empty(AdminRouteCatalog.MissingTextKeys(AdminRoute.ScheduledTasks));
        Assert.Equal(("configure", "server"), (AdminRouteCatalog.SectionOf(AdminRoute.ScheduledTasks).Id, AdminRouteCatalog.TopicOf(AdminRoute.ScheduledTasks)!.Id));
        Assert.Equal("/admin/tasks", AdminRouteCatalog.Leaf(AdminRoute.ScheduledTasks).Href);
    }

    [Theory]
    [InlineData("/admin/tasks", null)]
    [InlineData("/admin/tasks/library-scan", "library-scan")]
    [InlineData("/admin/tasks/rebuild_catalog_search_index", "rebuild_catalog_search_index")]
    [InlineData("/admin/tasks/sync_catalog_search_index", "sync_catalog_search_index")]
    [InlineData("/admin/tasks/plugin%3A7%3Afeed-sync", "plugin:7:feed-sync")]
    public void TaskLocationsOpenOnlyOnceRegistered(string href, string? key)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        var request = resolution.Request!;
        Assert.Equal(AdminRoute.ScheduledTasks, request.Route);
        Assert.Equal(key, request.Segment(0));
        if (key != null) Assert.True(SiloPlayer.Core.Api.AdminTasksApi.IsTaskKey(key));
        Assert.False(new AdminPageRegistry([]).CanOpen(request));
        Assert.True(new AdminPageRegistry([new(AdminRoute.ScheduledTasks, typeof(object))]).CanOpen(request));
        Assert.Equal(href, request.Href);
        Assert.Equal(key == null ? "web.admin.common.title.tab.tasks" : "web.admin.common.title.tab.task", AdminNavigation.TitleKeys(request).Tab);
    }

    [Theory]
    [InlineData("tasks")]
    [InlineData("scheduler")]
    public void ScheduledTasksAreSearchableOnlyWhenRegistered(string query)
    {
        Assert.DoesNotContain(AdminNavigation.SearchGroups(query, new AdminPageRegistry([]).IsRegistered).SelectMany(group => group.Pages), page => page.Route == AdminRoute.ScheduledTasks);
        var registry = new AdminPageRegistry([new(AdminRoute.ScheduledTasks, typeof(object))]);
        Assert.Contains(AdminNavigation.SearchGroups(query, registry.IsRegistered).SelectMany(group => group.Pages), page => page.Route == AdminRoute.ScheduledTasks);
    }

    [Fact]
    public void EveryScheduledTasksLabelResolvesToCatalogText()
    {
        foreach (var key in new[]
        {
            "web.admin.tasks.page.title", "web.admin.tasks.page.subtitle", "web.admin.tasks.page.loading", "web.admin.tasks.onDemand.label",
            "web.admin.tasks.detail.notFoundTitle", "web.admin.tasks.detail.allTasks", "web.admin.tasks.detail.useRevision", "web.admin.tasks.detail.reviewSchedule",
            "web.admin.tasks.hooks.tasks.scheduleChanged", "web.admin.errors.scheduleRevisionMissing", "web.admin.common.taskStatus.completedWithErrors",
            "configure.native.tasks.listFailed", "configure.native.tasks.removeTrigger",
        })
            Assert.NotEqual(key, AdminTasksText.Message(key));
        Assert.All(AdminTasksText.Keys, key => Assert.True(key.StartsWith("web.admin.", StringComparison.Ordinal) || key.StartsWith("configure.native.tasks.", StringComparison.Ordinal), key));
        Assert.Equal("Use this revision and keep my draft", AdminTasksText.Message("web.admin.tasks.detail.useRevision"));
    }
}
