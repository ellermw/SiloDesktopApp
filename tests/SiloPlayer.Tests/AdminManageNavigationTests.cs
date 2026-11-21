using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// G0 Admin Manage foundation: route identities, descendants, WebUI redirects,
/// page registration and sidebar/search facts, pinned to official Silo
/// 1452ea2d10efbe7d3ea5ce7140fb49756b493670 (lib/adminNavigation.ts, App.tsx,
/// lib/documentTitle.ts, lib/adminSettingsSearch.ts, AdminRequests.tsx).
/// </summary>
public sealed class AdminManageNavigationTests
{
    private static AdminNavSection Manage => AdminRouteCatalog.Sections.Single(section => section.Id == "manage");

    [Fact]
    public void ManageIsExactlySixTopicsAndTwentyOneLeavesInWebUiOrder()
    {
        var topics = Manage.Entries.Cast<AdminNavTopic>().ToArray();
        Assert.Equal(["libraries", "autoscan", "requests", "users", "playback", "downloads"], topics.Select(topic => topic.Id));
        Assert.Equal(
        [
            "libraries:/admin/libraries", "libraries:/admin/collections", "libraries:/admin/sections", "libraries:/admin/subtitles",
            "libraries:/admin/settings/library",
            "autoscan:/admin/autoscan/activity", "autoscan:/admin/autoscan", "autoscan:/admin/settings/autoscan",
            "requests:/admin/requests", "requests:/admin/requests/discover", "requests:/admin/settings/requests",
            "users:/admin/users", "users:/admin/access-groups", "users:/admin/devices", "users:/admin/history-import",
            "playback:/admin/history", "playback:/admin/marker-history", "playback:/admin/nodes", "playback:/admin/settings/playback",
            "downloads:/admin/downloads", "downloads:/admin/settings/downloads",
        ], topics.SelectMany(topic => topic.Leaves.Select(leaf => $"{topic.Id}:{leaf.Href}")));
        Assert.Equal(["monitor", "manage", "configure"], AdminRouteCatalog.Sections.Select(section => section.Id));

    }

    [Fact]
    public void EveryRouteIdentityHasOneLeafAndOneHref()
    {
        var leaves = AdminRouteCatalog.Leaves.ToArray();
        Assert.Equal(Enum.GetValues<AdminRoute>().Order(), leaves.Select(leaf => leaf.Route).Order());
        Assert.Equal(leaves.Length, leaves.Select(leaf => leaf.Href).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(45, leaves.Length);

    }

    [Theory]
    [InlineData(AdminRoute.HomeSections, "web.admin.shell.nav.item.homeSections")]
    [InlineData(AdminRoute.RequestQueue, "web.admin.shell.nav.item.queue")]
    [InlineData(AdminRoute.PlaybackHistory, "web.admin.shell.nav.item.history")]
    [InlineData(AdminRoute.SkipMarkers, "web.admin.shell.nav.item.skipMarkers")]
    [InlineData(AdminRoute.PreparedFiles, "web.admin.shell.nav.item.preparedFiles")]
    [InlineData(AdminRoute.LibrarySettings, "web.admin.shell.nav.item.settings")]
    [InlineData(AdminRoute.DownloadSettings, "web.admin.shell.nav.item.settings")]
    [InlineData(AdminRoute.Libraries, "web.admin.shell.nav.page.libraries.label")]
    [InlineData(AdminRoute.SubtitleFiles, "web.admin.shell.nav.page.subtitleFiles.label")]
    public void TopicItemsUseTheShorterSidebarNames(AdminRoute route, string key) =>
        Assert.Equal(key, AdminRouteCatalog.Leaf(route).SidebarLabelKey);

    [Fact]
    public void ExactLeavesMatchTheWebUiExactFlags()
    {
        Assert.Equal([AdminRoute.Dashboard, AdminRoute.AutoscanSources, AdminRoute.RequestQueue],
            AdminRouteCatalog.Leaves.Where(leaf => leaf.Exact).Select(leaf => leaf.Route));
    }

    public static TheoryData<AdminRouteRequest, string, string> TitleCases => new()
    {
        { new AdminRouteRequest(AdminRoute.Collections), "web.admin.common.title.header.collections", "web.admin.common.title.tab.collections" },
        { AdminRouteRequest.CollectionNew(), "web.admin.common.title.tab.collectionNew", "web.admin.common.title.tab.collectionNew" },
        { AdminRouteRequest.CollectionEditor("9"), "web.admin.common.title.tab.collectionEdit", "web.admin.common.title.tab.collectionEdit" },
        { new AdminRouteRequest(AdminRoute.Users), "web.admin.common.title.header.users", "web.admin.common.title.tab.users" },
        { AdminRouteRequest.UserDetail("7"), "web.admin.common.title.header.user", "web.admin.common.title.tab.user" },
        { AdminRouteRequest.AccessGroup("3"), "web.admin.common.title.header.accessGroups", "web.admin.common.title.tab.accessGroups" },
        { AdminRouteRequest.Device("7", "d"), "web.admin.common.title.header.devices", "web.admin.common.title.tab.devices" },
        { new AdminRouteRequest(AdminRoute.AutoscanActivity), "web.admin.common.title.header.autoscanActivity", "web.admin.common.title.tab.autoscanActivity" },
        { new AdminRouteRequest(AdminRoute.AutoscanSources), "web.admin.common.title.header.autoscan", "web.admin.common.title.tab.autoscan" },
        { new AdminRouteRequest(AdminRoute.RequestDiscover), "web.admin.common.title.header.requests", "web.admin.common.title.tab.requests" },
        { new AdminRouteRequest(AdminRoute.SubtitleFiles), "web.admin.common.title.header.subtitles", "web.admin.common.title.tab.subtitles" },
        { new AdminRouteRequest(AdminRoute.SkipMarkers), "web.admin.common.title.header.markerHistory", "web.admin.common.title.tab.markerHistory" },
        { new AdminRouteRequest(AdminRoute.HistoryImport), "web.admin.common.title.header.historyImport", "web.admin.common.title.tab.historyImport" },
        { new AdminRouteRequest(AdminRoute.PlaybackSettings), "web.admin.common.title.header.settings", "web.admin.common.title.tab.settings" },
        { new AdminRouteRequest(AdminRoute.Dashboard), "web.admin.shell.mobileHeader.dashboardTitle", "web.admin.common.title.tab.admin" },
    };

    [Theory]
    [MemberData(nameof(TitleCases))]
    public void TitlesFollowDocumentTitleIncludingDescendants(AdminRouteRequest request, string header, string tab) =>
        Assert.Equal((header, tab), AdminNavigation.TitleKeys(request));

    public static TheoryData<string, AdminRoute, string[], string?> RouteCases => new()
    {
        { "/admin", AdminRoute.Dashboard, [], null },
        { "/admin/autoscan", AdminRoute.AutoscanSources, [], null },
        { "/admin/autoscan/activity", AdminRoute.AutoscanActivity, [], null },
        { "/admin/requests", AdminRoute.RequestQueue, [], null },
        { "/admin/requests/discover", AdminRoute.RequestDiscover, [], null },
        { "/admin/collections/new", AdminRoute.Collections, ["new"], null },
        { "/admin/collections/42/edit", AdminRoute.Collections, ["42", "edit"], null },
        { "/admin/users/7?tab=access", AdminRoute.Users, ["7"], "access" },
        { "/admin/access-groups/3", AdminRoute.AccessGroups, ["3"], null },
        { "/admin/devices/7/device%201", AdminRoute.Devices, ["7", "device 1"], null },
        { "/admin/downloads?tab=files", AdminRoute.PreparedFiles, [], "files" },
        { "/admin/history", AdminRoute.PlaybackHistory, [], null },
        { "/admin/history-import", AdminRoute.HistoryImport, [], null },
        { "/admin/marker-history", AdminRoute.SkipMarkers, [], null },
        { "/admin/settings/library", AdminRoute.LibrarySettings, [], null },
        { "/admin/settings/logging", AdminRoute.LoggingSettings, [], null },
        { "/admin/logs?tab=ffmpeg", AdminRoute.ServerLog, [], "ffmpeg" },
    };

    [Theory]
    [MemberData(nameof(RouteCases))]
    public void WebUiHrefsResolveToStableIdentities(string href, AdminRoute route, string[] segments, string? tab)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(AdminRouteResolutionKind.Route, resolution.Kind);
        Assert.False(resolution.Redirected);
        Assert.Equal(route, resolution.Request!.Route);
        Assert.Equal(segments, resolution.Request.Segments);
        Assert.Equal(tab, resolution.Request.Get("tab"));
        Assert.True(AdminRouteCatalog.IsValid(resolution.Request));
    }

    [Theory]
    [InlineData("/admin/home-rows?page=5", AdminRoute.HomeSections, "/admin/sections?page=5")]
    [InlineData("/admin/stats", AdminRoute.Dashboard, "/admin")]
    [InlineData("/admin/requests?tab=settings&user=4", AdminRoute.RequestSettings, "/admin/settings/requests")]
    [InlineData("/admin/requests?tab=integrations", AdminRoute.RequestSettings, "/admin/settings/requests")]
    [InlineData("/admin/settings/scanner", AdminRoute.LibrarySettings, "/admin/settings/library")]
    [InlineData("/admin/settings?tab=intro", AdminRoute.PlaybackSettings, "/admin/settings/playback")]
    [InlineData("/admin/settings/log-retention", AdminRoute.LoggingSettings, "/admin/settings/logging")]
    [InlineData("/admin/settings/downloads?tab=x", AdminRoute.DownloadSettings, "/admin/settings/downloads")]
    [InlineData("/admin/libraries/5", AdminRoute.Dashboard, "/admin")]
    [InlineData("/admin/users/1/2", AdminRoute.Dashboard, "/admin")]
    [InlineData("/admin/autoscan/other", AdminRoute.Dashboard, "/admin")]
    [InlineData("/admin/devices/7", AdminRoute.Dashboard, "/admin")]
    [InlineData("/admin/not-a-page", AdminRoute.Dashboard, "/admin")]
    public void WebUiRedirectsLandWhereTheRouterSends(string href, AdminRoute route, string canonical)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(AdminRouteResolutionKind.Route, resolution.Kind);
        Assert.True(resolution.Redirected);
        Assert.Equal(route, resolution.Request!.Route);
        Assert.Equal(canonical, resolution.CanonicalHref);
    }

    [Fact]
    public void MovedOverridesTabLandsOnUsersWithItsNotice()
    {
        Assert.True(AdminRouteCatalog.TryResolve("/admin/requests?tab=overrides", out var resolution));
        Assert.Equal(AdminRoute.Users, resolution.Request!.Route);
        Assert.Null(resolution.Request.Query);
        Assert.Equal("web.admin.requests.page.overridesMoved.title", resolution.NoticeTitleKey);
        Assert.Equal("web.admin.requests.page.overridesMoved.description", resolution.NoticeDescriptionKey);
        Assert.True(AdminRouteCatalog.TryResolve("/admin/requests?tab=queue", out var other));
        Assert.Equal(AdminRoute.RequestQueue, other.Request!.Route);
        Assert.Null(other.NoticeTitleKey);
    }

    [Theory]
    [InlineData("/admin/plugins", "/admin/plugins")]
    [InlineData("/admin/plugins/arr", "/admin/plugins/arr")]
    [InlineData("/admin/plugins?tab=catalog", "/admin/plugins?tab=catalog")]

    public void PluginsPagesAreNativeRoutesNotUnsupportedOnes(string href, string canonical)
    {
        Assert.True(AdminRouteCatalog.TryResolve(href, out var resolution));
        Assert.Equal(AdminRouteResolutionKind.Route, resolution.Kind);
        Assert.Equal(AdminRoute.Plugins, resolution.Request!.Route);
        Assert.Equal(canonical, resolution.CanonicalHref);
    }

    [Theory]
    [InlineData("/settings/playback")]
    [InlineData("admin/users")]
    [InlineData("")]
    [InlineData("/administrator")]
    public void LocationsOutsideAdminDoNotResolve(string href) => Assert.False(AdminRouteCatalog.TryResolve(href, out _));

    [Fact]
    public void HrefsRoundTripWithEscapedDescendantsAndOrderedQuery()
    {
        var request = AdminRouteRequest.UserDetail("id with space%", new Dictionary<string, string> { ["tab"] = "preferences", ["level"] = "device.p 1.d/2" });
        Assert.Equal("/admin/users/id%20with%20space%25?level=device.p%201.d%2F2&tab=preferences", request.Href);
        Assert.True(AdminRouteCatalog.TryResolve(request.Href, out var resolution));
        Assert.Equal(request, resolution.Request);
        foreach (var factory in new[] { AdminRouteRequest.CollectionNew(), AdminRouteRequest.CollectionEditor("c-1"), AdminRouteRequest.AccessGroup("g"), AdminRouteRequest.Device("u", "d") })
        {
            Assert.True(AdminRouteCatalog.TryResolve(factory.Href, out var again));
            Assert.Equal(factory, again.Request);
        }
    }

    [Fact]
    public void RequestsCompareByValueAndUpdateQueryWithoutMutation()
    {
        var first = new AdminRouteRequest(AdminRoute.PreparedFiles, new Dictionary<string, string> { ["tab"] = "files", ["storage"] = "2" });
        var second = new AdminRouteRequest(AdminRoute.PreparedFiles, new Dictionary<string, string> { ["storage"] = "2", ["tab"] = "files" });
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, first with { Segments = ["x"] });
        Assert.Equal(new AdminRouteRequest(AdminRoute.Users), new AdminRouteRequest(AdminRoute.Users, new Dictionary<string, string>()));

        var history = first.WithQuery("tab", "history");
        Assert.Equal("files", first.Get("tab"));
        Assert.Equal("history", history.Get("tab"));
        Assert.Equal("2", history.Get("storage"));
        var cleared = history.WithQuery("tab", null).WithQuery("storage", "");
        Assert.Null(cleared.Query);
        Assert.Equal(new AdminRouteRequest(AdminRoute.PreparedFiles), cleared);
    }

    [Fact]
    public void SegmentsAreCopiedSoACallerCannotMutateARoute()
    {
        var segments = new List<string> { "7" };
        var request = new AdminRouteRequest(AdminRoute.Users) { Segments = segments };
        segments[0] = "8";
        Assert.Equal("7", request.Segment(0));
        Assert.Null(request.Segment(1));
    }

    public static TheoryData<AdminRouteRequest, bool> ValidityCases => new()
    {
        { new AdminRouteRequest(AdminRoute.Libraries), true },
        { new AdminRouteRequest(AdminRoute.Libraries) { Segments = ["5"] }, false },
        { new AdminRouteRequest(AdminRoute.Collections) { Segments = ["5"] }, false },
        { new AdminRouteRequest(AdminRoute.Collections) { Segments = ["5", "view"] }, false },
        { new AdminRouteRequest(AdminRoute.Collections) { Segments = ["new", "edit"] }, true },
        { new AdminRouteRequest(AdminRoute.Users) { Segments = ["1", "2"] }, false },
        { new AdminRouteRequest(AdminRoute.Devices) { Segments = ["1"] }, false },
        { new AdminRouteRequest(AdminRoute.Users) { Segments = [" "] }, false },
        { new AdminRouteRequest(AdminRoute.Users) { Segments = ["a\nb"] }, false },
        { new AdminRouteRequest(AdminRoute.Users) { Segments = ["a/b"] }, false },
        { new AdminRouteRequest(AdminRoute.Users) { Segments = [new string('x', AdminRouteCatalog.MaxSegmentLength + 1)] }, false },
        { new AdminRouteRequest(AdminRoute.Users) { Segments = [new string('x', AdminRouteCatalog.MaxSegmentLength)] }, true },
        { new AdminRouteRequest(AdminRoute.LoggingSettings) { Segments = ["x"] }, false },
        { new AdminRouteRequest((AdminRoute)999), false },
    };

    [Theory]
    [MemberData(nameof(ValidityCases))]
    public void OnlyDescendantsALeafOwnsAreValid(AdminRouteRequest request, bool valid) => Assert.Equal(valid, AdminRouteCatalog.IsValid(request));

    [Fact]
    public void RegistryOpensOnlyRegisteredValidLocations()
    {
        var registry = new AdminPageRegistry(AdminNavigation.MonitorRoutes.Select(route => new AdminPageRegistration(route, typeof(object))),
            [new AdminPageRegistration(AdminRoute.Users, typeof(string))]);
        Assert.True(registry.CanOpen(AdminRouteRequest.UserDetail("7")));
        Assert.False(registry.CanOpen(new AdminRouteRequest(AdminRoute.Users) { Segments = ["7", "8"] }));
        Assert.False(registry.CanOpen(new AdminRouteRequest(AdminRoute.Libraries)));
        Assert.Null(registry.PageTypeOf(AdminRoute.Libraries));
        Assert.Equal(typeof(string), registry.PageTypeOf(AdminRoute.Users));
        var topics = Manage.Entries.Cast<AdminNavTopic>().ToDictionary(topic => topic.Id);
        Assert.True(registry.HasAny(topics["users"]));
        Assert.False(registry.HasAny(topics["libraries"]));
        Assert.False(registry.CanOpen(null));
    }

    [Fact]
    public void RegistryRejectsDuplicateUnknownOrTypelessRoutes()
    {
        var builtIn = new[] { new AdminPageRegistration(AdminRoute.Dashboard, typeof(object)) };
        Assert.Throws<ArgumentException>(() => new AdminPageRegistry(builtIn, [new AdminPageRegistration(AdminRoute.Dashboard, typeof(string))]));
        Assert.Throws<ArgumentException>(() => new AdminPageRegistry(builtIn, [new AdminPageRegistration((AdminRoute)999, typeof(string))]));
        Assert.Throws<ArgumentNullException>(() => new AdminPageRegistry(builtIn, [new AdminPageRegistration(AdminRoute.Users, null!)]));
    }

    [Fact]
    public void ClosedTopicFoldsTheFirstShownChildBadge()
    {
        var requests = Manage.Entries.Cast<AdminNavTopic>().Single(topic => topic.Id == "requests");
        var pending = new AdminNavBadge(AdminNavBadgeKind.Count, 4, "4 pending requests");
        var attention = new AdminNavBadge(AdminNavBadgeKind.Attention, 0, "Requests needs attention");
        AdminNavBadge? Badge(AdminRoute route) => route switch { AdminRoute.RequestQueue => pending, AdminRoute.RequestSettings => attention, _ => null };

        Assert.Same(pending, AdminNavigation.TopicBadge(requests, open: false, _ => true, Badge));
        Assert.Null(AdminNavigation.TopicBadge(requests, open: true, _ => true, Badge));
        Assert.Same(attention, AdminNavigation.TopicBadge(requests, open: false, route => route != AdminRoute.RequestQueue, Badge));
        Assert.Null(AdminNavigation.TopicBadge(requests, open: false, _ => false, Badge));
    }

    [Fact]
    public void SearchListsOnlyAvailablePagesGroupedByTopic()
    {
        Assert.Empty(AdminNavigation.SearchGroups("sonarr", route => AdminNavigation.MonitorRoutes.Contains(route)));

        static bool All(AdminRoute route) => AdminRouteCatalog.SectionOf(route).Id != "configure";

        var sonarr = AdminNavigation.SearchGroups("sonarr", All);
        Assert.Equal(["web.admin.shell.nav.topic.autoscan", "web.admin.shell.nav.topic.requests"], sonarr.Select(group => group.LabelKey));
        Assert.All(sonarr, group => Assert.Equal("web.admin.shell.nav.item.settings", AdminRouteCatalog.Leaf(Assert.Single(group.Pages).Route).SidebarLabelKey));

        var discover = Assert.Single(AdminNavigation.SearchGroups("mdblist", All));
        Assert.Equal(AdminRoute.RequestDiscover, Assert.Single(discover.Pages).Route);
        Assert.Equal(AdminRoute.LibrarySettings, Assert.Single(AdminNavigation.SearchGroups("rotten", All).SelectMany(group => group.Pages)).Route);

        var onlyQueue = AdminNavigation.SearchGroups("approvals", route => route == AdminRoute.RequestQueue);
        Assert.Equal(AdminRoute.RequestQueue, Assert.Single(Assert.Single(onlyQueue).Pages).Route);
        Assert.Empty(AdminNavigation.SearchGroups("approvals", route => route == AdminRoute.RequestDiscover));
    }

    [Fact]
    public void SearchFindsIndexedSettingsAndWholeGroupsByName()
    {
        // Indexed settings are searched by their catalog labels (Logging's are not settings-indexed natively).
        var nodes = AdminRouteCatalog.Leaf(AdminRoute.Nodes);
        Assert.Equal(6, nodes.SettingKeys.Count);
        Assert.Equal(36, AdminRouteCatalog.Leaf(AdminRoute.PlaybackSettings).SettingKeys.Count);
        Assert.All(AdminRouteCatalog.Leaf(AdminRoute.DownloadSettings).SettingKeys, key => Assert.StartsWith("web.admin.settings.search.downloads.setting.", key));

        var logs = Assert.Single(AdminNavigation.SearchGroups("logs", route => AdminNavigation.MonitorRoutes.Contains(route)));
        Assert.Equal("web.admin.shell.nav.topic.logs", logs.LabelKey);
        Assert.Equal([AdminRoute.ServerLog, AdminRoute.ClientDiagnostics, AdminRoute.LoggingSettings], logs.Pages.Select(page => page.Route));
        var monitor = Assert.Single(AdminNavigation.SearchGroups("monitor", route => AdminNavigation.MonitorRoutes.Contains(route)));
        Assert.Equal([AdminRoute.Dashboard, AdminRoute.Activity], monitor.Pages.Select(page => page.Route));
        // Word prefixes match; inner substrings only from four letters.
        Assert.Equal(AdminRoute.Activity, Assert.Single(AdminNavigation.Search("trans")).Route);
        Assert.Empty(AdminNavigation.Search("ran"));
        Assert.Equal(AdminRoute.Activity, Assert.Single(AdminNavigation.Search("ranscode")).Route);
    }

    [Fact]
    public void MonitorRoutesHaveCompleteTextAndManageTextIsDeclared()
    {
        var missing = Enum.GetValues<AdminRoute>()
            .SelectMany(route => AdminRouteCatalog.MissingTextKeys(route).Select(key => $"{route}: {key}"));
        Assert.True(!missing.Any(), string.Join(Environment.NewLine, missing));

        Assert.Contains("web.admin.common.title.tab.user", AdminRouteCatalog.TextKeys(AdminRoute.Users));
        Assert.Contains("web.admin.common.title.tab.collectionEdit", AdminRouteCatalog.TextKeys(AdminRoute.Collections));
        Assert.Contains("web.admin.shell.nav.topic.downloads", AdminRouteCatalog.TextKeys(AdminRoute.DownloadSettings));
        Assert.Contains("web.admin.settings.search.downloads.label", AdminRouteCatalog.TextKeys(AdminRoute.DownloadSettings));
    }

    [Fact]
    public void ShellOpensOnlyRegisteredPagesWithoutSlideOrDashboardFallback()
    {
        var shell = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Views", "Admin", "AdminShellPage.cs"));
        Assert.DoesNotContain("_ => typeof(AdminDashboardPage)", shell, StringComparison.Ordinal);
        foreach (var motion in new[] { "SlideNavigationTransitionInfo", "DrillInNavigationTransitionInfo", "EntranceNavigationTransitionInfo", "EntranceThemeTransition", "ContentThemeTransition" })
            Assert.DoesNotContain(motion, shell, StringComparison.Ordinal);
        Assert.Contains("if (!_active || PageType(request) is not { } type) return;", shell, StringComparison.Ordinal);
        Assert.Contains("_frame.Navigate(type, request with { }, new SuppressNavigationTransitionInfo());", shell, StringComparison.Ordinal);
        Assert.Contains("_frame.GoBack(new SuppressNavigationTransitionInfo())", shell, StringComparison.Ordinal);
        Assert.Contains("App.Services.GetServices<AdminPageRegistration>()", shell, StringComparison.Ordinal);
        Assert.Contains("AdminRouteCatalog.MissingTextKeys(route)", shell, StringComparison.Ordinal);
        // The in-flow Logging guard keeps precedence; other pages use the shared guard.
        var logging = shell.IndexOf("IAdminLoggingNavigationGuard logging => logging.CanLeaveAsync()", StringComparison.Ordinal);
        var generic = shell.IndexOf("IAdminNavigationGuard guard => guard.CanLeaveAsync()", StringComparison.Ordinal);
        Assert.True(logging >= 0 && generic > logging);
        Assert.Contains("case IAdminPageSuspension page: page.SuspendAdminPage(); break;", shell, StringComparison.Ordinal);
    }
}
