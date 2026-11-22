using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// The Libraries page's Libraries / Scan Activity / Catalog Health tabs (official 93794454b, #5087, and #4994 at 5e26ca784):
/// the tab and tile rules, the Scan Activity filter and panel states, the tab and tile counts, the scan progress percent,
/// bidi-safe paths and the interface-language root labels.
/// </summary>
public sealed class AdminLibrariesTabsTests
{
    private static AdminLibraryScanRun Scan(string id, string library, string status = "running", string mode = "subtree", string? path = null, long? total = null, long? processed = null) =>
        new(id, library, mode, path, status, null, null, total, processed, null);

    private static AdminScanQueueGroup Group(string id, string name, params AdminLibraryScanRun[] scans)
    {
        var running = scans.Count(scan => scan.IsRunning);
        return new AdminScanQueueGroup(id, name, scans, running, scans.Length - running);
    }

    [Theory]
    [InlineData("activity", AdminLibrariesTab.Activity)]
    [InlineData("health", AdminLibrariesTab.Health)]
    [InlineData("libraries", AdminLibrariesTab.Libraries)]
    [InlineData("", AdminLibrariesTab.Libraries)]
    [InlineData("autoscan", AdminLibrariesTab.Libraries)]
    [InlineData("Activity", AdminLibrariesTab.Libraries)]
    [InlineData(null, AdminLibrariesTab.Libraries)]
    public void OnlyActivityAndHealthSelectAnotherTab(string? value, AdminLibrariesTab expected) =>
        Assert.Equal(expected, AdminLibraryTabs.Parse(value));

    [Fact]
    public void TheRouteCarriesTheTabExceptForLibraries()
    {
        Assert.Null(AdminLibraryTabs.Query(AdminLibrariesTab.Libraries));
        Assert.Equal("activity", AdminLibraryTabs.Query(AdminLibrariesTab.Activity));
        Assert.Equal("health", AdminLibraryTabs.Query(AdminLibrariesTab.Health));
        foreach (var tab in Enum.GetValues<AdminLibrariesTab>()) Assert.Equal(tab, AdminLibraryTabs.Parse(AdminLibraryTabs.Query(tab)));
    }

    [Theory]
    [InlineData("matcher", AdminHealthSection.Matcher)]
    [InlineData("ambiguous", AdminHealthSection.Ambiguous)]
    [InlineData("skipped", AdminHealthSection.Skipped)]
    [InlineData("stale", AdminHealthSection.Stale)]
    [InlineData("unmatched", AdminHealthSection.Unmatched)]
    [InlineData("nope", AdminHealthSection.Unmatched)]
    public void AnUnknownHealthTileFallsBackToUnmatched(string value, AdminHealthSection expected) => Assert.Equal(expected, AdminLibraryTabs.ParseHealth(value));

    [Fact]
    public void ArrowsLoopAndHomeAndEndJump()
    {
        Assert.Equal(1, AdminLibraryTabs.Navigate(3, 0, AdminTabKey.Next));
        Assert.Equal(0, AdminLibraryTabs.Navigate(3, 2, AdminTabKey.Next));
        Assert.Equal(2, AdminLibraryTabs.Navigate(3, 0, AdminTabKey.Previous));
        Assert.Equal(0, AdminLibraryTabs.Navigate(3, 2, AdminTabKey.First));
        Assert.Equal(4, AdminLibraryTabs.Navigate(5, 1, AdminTabKey.Last));
        Assert.Equal(-1, AdminLibraryTabs.Navigate(0, 0, AdminTabKey.Next));
        Assert.Equal(1, AdminLibraryTabs.Navigate(3, 99, AdminTabKey.Previous));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(12L, null, 12L)]
    [InlineData(null, 3L, 3L)]
    [InlineData(12L, 3L, 15L)]
    [InlineData(0L, 0L, 0L)]
    public void TheHealthBadgeAddsWhatIsKnown(long? unmatched, long? matcher, long? expected) => Assert.Equal(expected, AdminLibraryTabs.HealthBadge(unmatched, matcher));

    [Theory]
    [InlineData(1, false, 4L, 4L)]
    [InlineData(1, true, 4L, null)]
    [InlineData(2, false, 4L, null)]
    [InlineData(0, false, 0L, null)]
    public void AnAmbiguousCountIsCatalogWideOnlyForASingleLibrary(int libraries, bool unknown, long total, long? expected) =>
        Assert.Equal(expected, AdminLibraryTabs.AmbiguousTileCount(libraries, unknown, total));

    [Fact]
    public void ATileShowsItsErrorMarkOnlyWhileNoCountIsKnown()
    {
        Assert.True(AdminLibraryTabs.UnmatchedTileError(new AdminUnmatchedState { TotalsError = "failed" }));
        Assert.False(AdminLibraryTabs.UnmatchedTileError(new AdminUnmatchedState { TotalsError = "failed", AwaitingTotal = 7 }));
        Assert.Equal(7, AdminLibraryTabs.UnmatchedTotal(new AdminUnmatchedState { TotalsError = "failed", AwaitingTotal = 7 }));
        Assert.True(AdminLibraryTabs.MatcherTileError(new AdminMatcherState { Loaded = true, Error = "failed" }));
        Assert.False(AdminLibraryTabs.MatcherTileError(new AdminMatcherState { Loaded = true, HasData = true, Error = "failed" }));
        // A failed first read is not a zero; a successful empty read is.
        Assert.Null(AdminLibraryTabs.MatcherTotal(new AdminMatcherState { Loaded = true, Error = "failed" }));
        Assert.Equal(0, AdminLibraryTabs.MatcherTotal(new AdminMatcherState { Loaded = true, HasData = true }));
    }

    [Theory]
    [InlineData(3, false, false, AdminActivityPanelKind.Populated)]
    [InlineData(3, true, true, AdminActivityPanelKind.Populated)]
    [InlineData(0, true, true, AdminActivityPanelKind.FooterOnly)]
    [InlineData(0, true, false, AdminActivityPanelKind.FooterOnly)]
    [InlineData(0, false, true, AdminActivityPanelKind.Loading)]
    [InlineData(0, false, false, AdminActivityPanelKind.Empty)]
    public void TheActivityPanelWaitsForRefreshJobsBeforeItsEmptyState(int active, bool error, bool loading, AdminActivityPanelKind expected) =>
        Assert.Equal(expected, AdminLibraryTabs.PanelKind(active, error, loading));

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(0L, 5L, null)]
    [InlineData(10L, null, null)]
    [InlineData(10L, 0L, 0)]
    [InlineData(10L, 5L, 50)]
    [InlineData(3L, 1L, 33)]
    [InlineData(3L, 2L, 67)]
    [InlineData(10L, 99L, 100)]
    public void ScanProgressIsTheClampedRoundedPercent(long? total, long? processed, int? expected) =>
        Assert.Equal(expected, AdminLibraryTabs.ScanProgressPercent(Scan("s", "1", total: total, processed: processed)));

    [Fact]
    public void ALibraryRowProgressAveragesOnlyItsRunningScansThatReportOne()
    {
        var scans = new[]
        {
            Scan("a", "1", total: 10, processed: 2), Scan("b", "1", total: 10, processed: 7), Scan("c", "1"),
            Scan("queued", "1", status: "accepted", total: 10, processed: 10),
        };
        Assert.Equal(45, AdminLibraryTabs.LibraryScanProgressPercent(scans));
        Assert.Null(AdminLibraryTabs.LibraryScanProgressPercent([Scan("c", "1"), Scan("q", "1", status: "accepted", total: 4, processed: 1)]));
        Assert.Null(AdminLibraryTabs.LibraryScanProgressPercent([]));
    }

    [Fact]
    public void TheFilterChipsCountScansThenAddRefreshesAndDropAStaleSelection()
    {
        var movies = new AdminLibrary { Id = "1", Name = "Movies", Type = "movies" };
        var shows = new AdminLibrary { Id = "2", Name = "Shows", Type = "series" };
        var groups = new[]
        {
            Group("2", "Shows", Scan("a", "2"), Scan("b", "2", status: "accepted")),
        };
        var refreshes = new[]
        {
            new AdminLibraryRefreshEntry("2", shows, new AdminLibraryJob { Id = "j2", State = "running" }),
            new AdminLibraryRefreshEntry("1", movies, new AdminLibraryJob { Id = "j1", State = "queued" }),
        };
        var entries = AdminLibraryTabs.FilterEntries(groups, refreshes, (id, library) => library?.Name ?? "Library #" + id);
        Assert.Equal(["2:Shows:3", "1:Movies:1"], entries.Select(entry => entry.LibraryId + ":" + entry.Name + ":" + entry.Count));
        Assert.Equal("1", AdminLibraryTabs.EffectiveFilter("1", entries));
        Assert.Null(AdminLibraryTabs.EffectiveFilter("9", entries));
        Assert.Null(AdminLibraryTabs.EffectiveFilter(null, entries));
        var totals = AdminLibraryTabs.Totals(groups, refreshes);
        Assert.Equal(new AdminActivityTotals(2, 1, 1, 2, 1), totals);
        Assert.Equal(4, totals.Active);
        Assert.True(totals.HasRunning);
        Assert.False(AdminLibraryTabs.Totals([Group("2", "Shows", Scan("q", "2", status: "accepted"))], [refreshes[1]]).HasRunning);
    }

    [Fact]
    public void ARefreshOnlyLibraryIsNamedWithTheFallbackWhenItIsUnknown()
    {
        var refresh = new AdminLibraryRefreshEntry("9", null, new AdminLibraryJob { Id = "j", State = "running" });
        var entries = AdminLibraryTabs.FilterEntries([], [refresh], (id, library) => library?.Name ?? "Library #" + id);
        Assert.Equal("Library #9", Assert.Single(entries).Name);
    }

    [Fact]
    public void PathsReadLeftToRightWithEachFolderIsolated()
    {
        const string fsi = "\u2068", pdi = "\u2069";
        Assert.Equal($"/{fsi}media{pdi}/{fsi}film{pdi}", AdminPathText.Isolate("/media/film"));
        Assert.Equal($"{fsi}C:{pdi}\\{fsi}Media{pdi}\\{fsi}Shows{pdi}", AdminPathText.Isolate("C:\\Media\\Shows"));
        // A backslash is part of a folder name in a POSIX path.
        Assert.Equal($"/{fsi}a\\b{pdi}", AdminPathText.Isolate("/a\\b"));
        Assert.Equal("", AdminPathText.Isolate(""));
        // The isolates change nothing about the characters: removing them gives the path back.
        var arabic = "/media/\u0641\u064a\u0644\u0645/2024";
        Assert.Equal(arabic, AdminPathText.Isolate(arabic).Replace(fsi, "").Replace(pdi, ""));
        // A path with thousands of separators is isolated only up to the limit; the rest stays one run.
        var deep = string.Concat(Enumerable.Repeat("/d", 100));
        var isolated = AdminPathText.Isolate(deep);
        // The leading "/" is the first separator, so a path that starts with one isolates the 31 folders after it.
        Assert.Equal(AdminPathText.MaxSeparators - 1, isolated.Count(character => character == '\u2068'));
        Assert.Equal(deep, isolated.Replace(fsi, "").Replace(pdi, ""));
    }

    [Fact]
    public void AWholePathIsOneLeftToRightUnitAndANameKeepsItsOwnDirection()
    {
        const string fsi = "⁨", lri = "⁦", pdi = "⁩";
        // Official: the path span is dir="ltr", the name span dir="auto". Inside a sentence built around them (the summary strip) they cannot reorder it.
        Assert.Equal($"{lri}/{fsi}media{pdi}/{fsi}film{pdi}{pdi}", AdminPathText.Whole("/media/film"));
        Assert.Equal($"{fsi}Movies{pdi}", AdminPathText.Name("Movies"));
        Assert.Equal("", AdminPathText.Whole(""));
        Assert.Equal("", AdminPathText.Name(""));
        // Only isolates are added: removing them gives the text back.
        var arabic = "مكتبة (أفلام)";
        Assert.Equal(arabic, AdminPathText.Name(arabic).Replace(fsi, "").Replace(pdi, ""));
        Assert.Equal("/media/film", AdminPathText.Whole("/media/film").Replace(fsi, "").Replace(lri, "").Replace(pdi, ""));
    }

    [Fact]
    public void AmbiguousRootTypesAndConfidenceUseTheInterfaceLanguage()
    {
        Assert.Equal("Movie", AdminRootLabels.ItemType("movie"));
        Assert.Equal("Audiobook", AdminRootLabels.ItemType("audiobook"));
        Assert.Equal("tvshow", AdminRootLabels.ItemType("tvshow"));
        Assert.Equal("Series", AdminRootLabels.RootType("series"));
        Assert.Equal(AdminLibrariesText.T("web.admin.libraries.ambiguousRoots.unknownType"), AdminRootLabels.RootType(""));
        Assert.Equal("Low", AdminRootLabels.Confidence("low"));
        Assert.Equal("Medium", AdminRootLabels.Confidence("medium"));
        Assert.Equal("High", AdminRootLabels.Confidence("high"));
        // A level the scanner does not send shows as sent.
        Assert.Equal("certain", AdminRootLabels.Confidence("certain"));
    }

    [Fact]
    public void TheOfficialTabAndActivityStringsAreInTheTable()
    {
        Assert.Equal("Scan Activity", AdminLibrariesText.T("web.admin.libraries.tabs.activity"));
        Assert.Equal("Catalog Health", AdminLibrariesText.T("web.admin.libraries.tabs.health"));
        Assert.Equal("1 active", AdminLibrariesText.T("web.admin.libraries.tabs.activeCount", ("count", 1)));
        Assert.Equal("2 active", AdminLibrariesText.T("web.admin.libraries.tabs.activeCount", ("count", 2)));
        Assert.Equal("All (4)", AdminLibrariesText.T("web.admin.libraries.activity.filterAll", ("count", 4)));
        Assert.Equal("+ 1 other library", AdminLibrariesText.T("web.admin.libraries.activity.moreLibraries", ("count", 1)));
        Assert.Equal("+ 3 other libraries", AdminLibrariesText.T("web.admin.libraries.activity.moreLibraries", ("count", 3)));
        Assert.Equal("View active scans for TV Shows", AdminLibrariesText.T("web.admin.libraries.list.viewScansFor", ("name", "TV Shows")));
        Assert.Equal("Manage media folders, monitor scans, and resolve matching issues.", AdminLibrariesText.T("web.admin.libraries.page.subtitle"));
        Assert.Equal("No libraries configured yet.", AdminLibrariesText.T("web.admin.libraries.list.empty"));
        Assert.Equal("Inspect", AdminLibrariesText.T("web.admin.libraries.health.inspect"));
    }
}

public sealed partial class AdminLibrariesControllerTests
{
    [Fact]
    public async Task ScanActivityKnowsWhenTheFirstRefreshJobsPageHasArrived()
    {
        var (kit, authority, controller, _, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            Assert.False(controller.State.RefreshJobsLoaded);
            await controller.OpenAsync();
            Assert.True(controller.State.RefreshJobsLoaded);
            Assert.False(controller.State.RefreshJobsFailed);
            var entry = Assert.Single(AdminLibrariesController.ActivityRefreshes(controller.State));
            Assert.Equal("2", entry.LibraryId);
            Assert.Equal("j1", entry.Job.Id);
            Assert.Equal("Library 2", entry.Library?.Name);
        }
    }

    [Fact]
    public async Task ScanActivityShowsEveryActiveRefreshJobEvenWhenItsLibraryIsNotListed()
    {
        static string Job(string id, string library, string state) =>
            "{\"id\":\"" + id + "\",\"kind\":\"library_refresh\",\"state\":\"" + state + "\",\"terminal\":false,\"cancelable\":true,\"created_at\":\"2026-11-15T10:00:00Z\",\"library_id\":\""
            + library + "\",\"library_ids\":[],\"artifact_size_bytes\":0,\"public_link_supported\":false}";
        var (kit, authority, controller, server, _, _) = Create();
        using (kit) using (authority) using (controller)
        {
            // Library 99 is not in the list (deleted, or past a truncated list); library 2 has two jobs.
            var jobs = "{\"items\":[" + string.Join(",", Job("j9", "99", "queued"), Job("j3", "3", "running"), Job("j2a", "2", "running"), Job("j2b", "2", "queued")) + "],\"page\":{\"has_more\":false}}";
            server.Override = request => request.RequestUri!.AbsolutePath == "/api/v2/admin/jobs" ? AdminMonitorKit.Json(jobs) : null;
            await controller.OpenAsync();
            var refreshes = AdminLibrariesController.ActivityRefreshes(controller.State);
            // Listed libraries first, in list order, each job its own entry; then the job whose library the list does not hold.
            Assert.Equal(["2:j2a", "2:j2b", "3:j3", "99:j9"], refreshes.Select(entry => entry.LibraryId + ":" + entry.Job.Id));
            Assert.Equal(["Library 2", "Library 2", "Library 3", "(not listed)"], refreshes.Select(entry => entry.Library?.Name ?? "(not listed)"));
            // The tab's count, the chips and the panel state all see it: Scan Activity is never "empty" while it runs.
            var totals = AdminLibraryTabs.Totals([], refreshes);
            Assert.Equal((4, 2), (totals.Refreshes, totals.RunningRefreshes));
            var chips = AdminLibraryTabs.FilterEntries([], refreshes, (id, library) => library?.Name ?? "Library #" + id);
            Assert.Equal(["2:Library 2:2", "3:Library 3:1", "99:Library #99:1"], chips.Select(chip => chip.LibraryId + ":" + chip.Name + ":" + chip.Count));
            // The list row still shows one refresh per library: its first active job.
            Assert.Equal("j2a", AdminLibrariesController.Rows(controller.State)[1].ActiveRefresh?.Id);
        }
    }

    [Fact]
    public async Task AFailedFirstRefreshJobsReadIsAFailureNotALoadingState()
    {
        using var kit = AdminMonitorKit.Create();
        var server = new Server { Override = request => request.RequestUri!.AbsolutePath == "/api/v2/admin/jobs" ? AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError) : null };
        kit.Http.Respond = (request, _) => Task.Run(() => server.Respond(request));
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        using var controller = new AdminLibrariesController(new AdminLibrariesApi(kit.Client), authority, new Channel(), new Scheduler());
        await controller.OpenAsync();
        Assert.True(controller.State.RefreshJobsFailed);
        Assert.False(controller.State.RefreshJobsLoaded);
        // Scan Activity then shows only its footer with the restart control, never "No active scans or refreshes".
        Assert.Equal(AdminActivityPanelKind.FooterOnly, AdminLibraryTabs.PanelKind(0, controller.State.RefreshJobsFailed, !controller.State.RefreshJobsLoaded && !controller.State.RefreshJobsFailed));
        // And Restart jobs recovers once the server answers.
        server.Override = null;
        await controller.RestartJobsAsync();
        Assert.True(controller.State.RefreshJobsLoaded);
        Assert.False(controller.State.RefreshJobsFailed);
    }

    [Fact]
    public async Task ScanGroupsCarryTheLibraryTypeForTheirIcon()
    {
        var (kit, authority, controller, _, channel, _) = Create();
        using (kit) using (authority) using (controller)
        {
            await controller.OpenAsync();
            channel.Snapshot("scans", "[{\"id\":\"s1\",\"library_id\":1,\"mode\":\"library\",\"status\":\"running\"},{\"id\":\"s2\",\"library_id\":99,\"mode\":\"file\",\"path\":\"/x/y.mkv\",\"status\":\"accepted\"}]");
            await Until(() => controller.State.Scans.Count == 2);
            var groups = AdminLibrariesController.ScanGroups(controller.State);
            Assert.Equal("movies", groups.First(group => group.LibraryId == "1").LibraryType);
            var unknown = groups.First(group => group.LibraryId == "99");
            Assert.Null(unknown.LibraryType);
            Assert.Equal("Library #99", unknown.Name);
        }
    }
}
