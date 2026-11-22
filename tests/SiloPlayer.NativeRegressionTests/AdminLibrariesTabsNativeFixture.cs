using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

/// <summary>
/// The Libraries / Scan Activity / Catalog Health tabs (official 93794454b, #5087) on the real page: the tab structure and its counts, the route's
/// <c>tab</c> value, keyboard movement between tabs and tiles, each tab's states (Scan Activity loading, empty, failed with a reachable restart,
/// populated with a library filter and progress, footer pagination; Catalog Health tile counts, lazy reads and failed reads with Retry), and what a tab
/// change does to an in-flight command, an open editor and an expanded section. Dispatch: SILO_NATIVE_TEST_ONLY=admin-libraries (it runs inside it).
/// </summary>
internal static partial class AdminLibrariesNativeFixture
{
    private static int _visit;

    private static string RouteTab(AdminLibrariesPage page) => ((AdminRouteRequest)Field(page, "_route")!).Get("tab") ?? "";

    private static string Status(DependencyObject? element) => element == null ? "" : AutomationProperties.GetItemStatus(element);

    private static string Name(DependencyObject? element) => element == null ? "" : AutomationProperties.GetName(element);

    private static bool Reached(DependencyObject? element) => element is FrameworkElement shown && Shown(shown);

    /// <summary>Opens a fresh Libraries page (a new controller) on a route and waits for its list; returns it.</summary>
    private static async Task<AdminLibrariesPage> OpenFreshAsync(AdminShellPage shell, Frame inner, AdminLibrariesPage previous, Dictionary<string, string>? query, bool expectLoaded = true)
    {
        // Every open carries its own marker so the shell always treats it as a new visit, even to the route it already shows.
        query = new Dictionary<string, string>(query ?? new Dictionary<string, string>()) { ["visit"] = (++_visit).ToString(System.Globalization.CultureInfo.InvariantCulture) };
        AdminShellPage.Open(new AdminRouteRequest(AdminRoute.Libraries, query));
        await Until(() => inner.Content is AdminLibrariesPage current && !ReferenceEquals(current, previous) && (!expectLoaded || current.Controller?.State.Loaded == true) && current.IsLoaded);
        var opened = (AdminLibrariesPage)inner.Content;
        // The new page's own tab strip is in the tree and laid out before anything is asserted on it.
        if (expectLoaded) await Until(() => Find<Button>(opened, "LibrariesTab_Libraries") is { IsLoaded: true, ActualWidth: > 0 });
        return opened;
    }

    private static async Task<AdminLibrariesPage> RunTabsAsync(Window window, Frame outer, AdminShellPage shell, Frame inner, AdminLibrariesPage page, Server server, Realtime realtime)
    {
        var scroll = (ScrollViewer)Field(page, "_scroll")!;
        scroll.ChangeView(null, 0, null, true);

        // ── Structure: three tabs in the official order, Libraries first ──
        var names = new[] { "Libraries", "Activity", "Health" };
        var tabs = names.Select(name => Find<Button>(shell, "LibrariesTab_" + name)).ToArray();
        Check(tabs.All(tab => tab != null), "the page shows the three tabs Libraries, Scan Activity and Catalog Health");
        Check(Name(tabs[0]).StartsWith(AdminLibrariesText.T("web.admin.libraries.tabs.libraries"), StringComparison.Ordinal)
              && Name(tabs[1]).StartsWith(AdminLibrariesText.T("web.admin.libraries.tabs.activity"), StringComparison.Ordinal)
              && Name(tabs[2]).StartsWith(AdminLibrariesText.T("web.admin.libraries.tabs.health"), StringComparison.Ordinal), "the tabs use the official labels");
        Check(tabs.Select((tab, index) => AutomationProperties.GetPositionInSet(tab!) == index + 1 && AutomationProperties.GetSizeOfSet(tab!) == 3).All(ok => ok), "the tabs report their position in a set of three");
        Check(page.SelectedTab == AdminLibrariesTab.Libraries && Status(tabs[0]!) == "selected" && Status(tabs[1]!) == ""
              && RouteTab(page) == "", "Libraries is selected by default and the route carries no tab value");
        Check(Reached(Find<FrameworkElement>(shell, "LibrariesTable")) && !Reached(Find<FrameworkElement>(shell, "LibrariesActivity")) && !Reached(Find<FrameworkElement>(shell, "LibrariesTile_Unmatched")),
            "only the Libraries panel is shown by default");

        // ── Counts ──
        var libraryCount = controller_Count(page);
        Check(Name(tabs[0]).EndsWith(" " + libraryCount.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), $"the Libraries tab counts its {libraryCount} libraries");
        var state = page.Controller!.State;
        var active = state.Scans.Count + AdminLibrariesController.ActivityRefreshes(state).Count;
        Check(active > 0 && Name(tabs[1]).Contains(AdminLibrariesText.T("web.admin.libraries.tabs.activeCount", ("count", active)), StringComparison.Ordinal), $"the Scan Activity tab counts its {active} active scans and refreshes");
        var unmatched = (AdminUnmatchedPanel)Field(page, "_unmatched")!;
        var matcher = (AdminMatcherQueuesPanel)Field(page, "_matcher")!;
        await Until(() => unmatched.State.AwaitingTotal != null && matcher.State.HasData);
        var health = unmatched.State.AwaitingTotal!.Value + matcher.State.Total;
        Check(health > 0 && Name(tabs[2]).EndsWith(" " + health.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal),
            $"the Catalog Health tab adds the unmatched ({unmatched.State.AwaitingTotal}) and matcher ({matcher.State.Total}) totals ({health})");

        // ── Keyboard: Right, Left, Home and End move focus and select, and loop ──
        tabs[0]!.Focus(FocusState.Keyboard);
        await Task.Delay(100);
        var keyboard = TrySendKey(0x27);
        await Task.Delay(150);
        if (keyboard)
        {
            Check(page.SelectedTab == AdminLibrariesTab.Activity && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(shell.XamlRoot), tabs[1]), "Right selects Scan Activity and moves focus to it");
            Check(RouteTab(page) == "activity", "selecting Scan Activity puts tab=activity in the route");
            TrySendKey(0x23); await Task.Delay(150);
            Check(page.SelectedTab == AdminLibrariesTab.Health && RouteTab(page) == "health", "End selects the last tab, Catalog Health");
            TrySendKey(0x27); await Task.Delay(150);
            Check(page.SelectedTab == AdminLibrariesTab.Libraries && RouteTab(page) == "", "Right from the last tab loops to Libraries, which removes the tab value");
            TrySendKey(0x25); await Task.Delay(150);
            Check(page.SelectedTab == AdminLibrariesTab.Health, "Left from the first tab loops to Catalog Health");
            TrySendKey(0x24); await Task.Delay(150);
            Check(page.SelectedTab == AdminLibrariesTab.Libraries, "Home selects the first tab");
        }
        else Check(false, "keyboard input reaches the focused tab");
        Check(inner.ContentTransitions == null || inner.ContentTransitions.Count == 0, "a tab change swaps the panel in place without a transition");

        // ── A tab change keeps the other query values, and a pill carries its library into Scan Activity ──
        var withExtra = await OpenFreshAsync(shell, inner, page, new Dictionary<string, string> { ["tab"] = "activity", ["x"] = "1" });
        page = withExtra;
        Check(page.SelectedTab == AdminLibrariesTab.Activity && Status(Find<Button>(shell, "LibrariesTab_Activity")!) == "selected", "a link with tab=activity opens Scan Activity");
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Libraries"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Libraries);
        var route = (AdminRouteRequest)Field(page, "_route")!;
        Check(route.Get("tab") == null && route.Get("x") == "1", "choosing Libraries removes only the tab value from the route");
        page = await OpenFreshAsync(shell, inner, page, new Dictionary<string, string> { ["tab"] = "nonsense" });
        Check(page.SelectedTab == AdminLibrariesTab.Libraries, "an unknown tab value opens Libraries");

        // ── Scan Activity, populated: filter chips, a refresh row and a library's running scan with its progress ──
        await Until(() => page.Controller!.State.Scans.Count > 0 && AdminLibrariesController.ActivityRefreshes(page.Controller.State).Count > 0);
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Activity"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Activity && Find<Button>(shell, "LibrariesFilter_all") is { IsLoaded: true });
        var running = page.Controller!.State.Scans.First(scan => scan.IsRunning);
        var cardId = "LibrariesActivityCard_" + running.LibraryId;
        Check(Find<FrameworkElement>(shell, cardId) != null && Find<Button>(shell, "LibrariesStopRefresh_2") != null && Find<Button>(shell, "LibrariesFilter_2") != null && Find<Button>(shell, "LibrariesFilter_" + running.LibraryId) != null,
            "Scan Activity shows the library's card, the refresh row and one filter chip per library with work");
        Check(Name(Find<Button>(shell, "LibrariesFilter_all")).StartsWith("All (", StringComparison.Ordinal), "the first chip is All with the active count");
        if (Find<Microsoft.UI.Xaml.Controls.ProgressBar>(shell, "LibrariesScanProgress_" + running.Id) is { } bar)
        {
            Check(bar.Value == AdminLibraryTabs.ScanProgressPercent(running) && AutomationProperties.GetLabeledBy(bar) != null && Name(bar).Length > 0, "a running scan's progress bar is labeled by its mode and target and shows its percent");
        }
        else Check(running.TotalFiles is not > 0, "a running scan with a known total shows a labeled progress bar");
        await InvokeAsync(Find<Button>(shell, "LibrariesFilter_2"));
        await Until(() => Find<FrameworkElement>(shell, cardId) == null || running.LibraryId == "2");
        Check(Find<Button>(shell, "LibrariesStopRefresh_2") != null && (running.LibraryId == "2" || Find<FrameworkElement>(shell, cardId) == null), "the library filter narrows Scan Activity to that library");
        Check((string?)Field(page, "_activityFilter") == "2", "the filter is the library the chip names");
        await InvokeAsync(Find<Button>(shell, "LibrariesFilter_all"));
        await Until(() => Find<FrameworkElement>(shell, cardId) != null && Find<Button>(shell, "LibrariesStopRefresh_2") != null);
        await InvokeAsync(Find<Button>(shell, "LibrariesFilter_2"));
        await Until(() => (string?)Field(page, "_activityFilter") == "2");
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Libraries"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Libraries);
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Activity"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Activity);
        Check((string?)Field(page, "_activityFilter") == null, "reaching Scan Activity from its tab starts unfiltered");
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Libraries"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Libraries && Reached(Find<FrameworkElement>(shell, "LibrariesTable")));

        // A row's pill and the summary strip's View queue open Scan Activity with their filter and focus the tab.
        await Until(() => Find<Button>(shell, "LibrariesScanPill_" + running.LibraryId) != null && Find<Button>(shell, "LibrariesViewQueue") != null);
        await InvokeAsync(Find<Button>(shell, "LibrariesScanPill_" + running.LibraryId));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Activity);
        Check((string?)Field(page, "_activityFilter") == running.LibraryId, "a row's scan pill opens Scan Activity filtered to that library");
        await Until(() => ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(shell.XamlRoot), Find<Button>(shell, "LibrariesTab_Activity")));
        Check(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(shell.XamlRoot), Find<Button>(shell, "LibrariesTab_Activity")), "focus moves to the Scan Activity tab");
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Libraries"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Libraries && Find<Button>(shell, "LibrariesViewQueue") != null);
        await InvokeAsync(Find<Button>(shell, "LibrariesViewQueue"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Activity);
        Check((string?)Field(page, "_activityFilter") == null, "View queue opens Scan Activity unfiltered");
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Libraries"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Libraries && Reached(Find<FrameworkElement>(shell, "LibrariesTable")));

        // ── A tab change leaves an in-flight command alone ──
        server.ScanGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var scansBefore = server.Requests.Count(line => line == "POST /api/v2/scan");
        await Until(() => Find<Button>(shell, "LibrariesScan_4") is { IsLoaded: true, IsEnabled: true });
        await InvokeAsync(Find<Button>(shell, "LibrariesScan_4"));
        await Until(() => page.Controller!.State.ScanStarting.Contains("4"));
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Health"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Health);
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Libraries"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Libraries && Reached(Find<FrameworkElement>(shell, "LibrariesTable")));
        Check(page.Controller!.State.ScanStarting.Contains("4") && Find<Button>(shell, "LibrariesScan_4") is { IsEnabled: false }, "a scan start still in flight keeps its row disabled after switching tabs and back");
        server.ScanGate.SetResult();
        await Until(() => !page.Controller!.State.ScanStarting.Contains("4"));
        Check(server.Requests.Count(line => line == "POST /api/v2/scan") == scansBefore + 1, "switching tabs sent no second scan request");
        server.ScanGate = null;

        // ── An open editor owns the page: the tabs are out of reach until it closes ──
        await InvokeAsync(Find<Button>(shell, "LibrariesAdd"));
        await Until(() => page.EditorSession != null && Find<FrameworkElement>(shell, "LibraryEditor") != null);
        Check(tabs.Concat([Find<Button>(shell, "LibrariesTab_Health")]).All(tab => Find<Button>(shell, AutomationProperties.GetAutomationId(tab!)) is { IsEnabled: false }) && page.SelectedTab == AdminLibrariesTab.Libraries,
            "the tabs are disabled while the editor is open");
        await InvokeAsync(Find<Button>(shell, "LibraryEditorCancel"));
        await Until(() => page.EditorSession == null);
        Check(Find<Button>(shell, "LibrariesTab_Activity") is { IsEnabled: true }, "the tabs work again after the editor closes");

        // ── Catalog Health: counts, lazy reads, one section at a time ──
        var rootsBefore = server.Requests.Count(line => line.Contains("/libraries/roots", StringComparison.Ordinal) || line.Contains("skipped-roots", StringComparison.Ordinal) || line.Contains("stale-ids", StringComparison.Ordinal));
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Health"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Health && Reached(Find<Button>(shell, "LibrariesTile_Stale")));
        Check(RouteTab(page) == "health" && page.SelectedHealthSection == AdminHealthSection.Unmatched, "Catalog Health opens on Unmatched Items and puts tab=health in the route");
        var tileNames = new[] { "Unmatched", "Matcher", "Ambiguous", "Skipped", "Stale" };
        Check(tileNames.All(name => Find<Button>(shell, "LibrariesTile_" + name) != null), "Catalog Health has five tiles");
        Check(Name(Find<Button>(shell, "LibrariesTile_Unmatched")).StartsWith(unmatched.State.AwaitingTotal!.Value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " ", StringComparison.Ordinal)
              && Name(Find<Button>(shell, "LibrariesTile_Matcher")).StartsWith(matcher.State.Total.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " ", StringComparison.Ordinal),
            "the Unmatched and Metadata Matcher tiles show their totals");
        foreach (var inspect in new[] { "Ambiguous", "Skipped", "Stale" })
            Check(Name(Find<Button>(shell, "LibrariesTile_" + inspect)).StartsWith(AdminLibrariesText.T("web.admin.libraries.health.inspect") + " ", StringComparison.Ordinal), $"the {inspect} tile reads Inspect until its section is opened");
        Check(server.Requests.Count(line => line.Contains("/libraries/roots", StringComparison.Ordinal) || line.Contains("skipped-roots", StringComparison.Ordinal) || line.Contains("stale-ids", StringComparison.Ordinal)) == rootsBefore,
            "the heavy root, skipped and stale reads wait for their tile");
        Check(Reached(Find<FrameworkElement>(shell, "LibrariesSectionHeading_Unmatched")) && Find<Button>(shell, "LibrariesSection_Unmatched") is not { Visibility: Visibility.Visible }, "the section is embedded: a heading and description, no collapsible header");

        var otherReadsBefore = server.Requests.Count(line => line.Contains("skipped-roots", StringComparison.Ordinal) || line.Contains("stale-ids", StringComparison.Ordinal));
        await InvokeAsync(Find<Button>(shell, "LibrariesTile_Ambiguous"));
        await Until(() => page.SelectedHealthSection == AdminHealthSection.Ambiguous && server.Requests.Any(line => line.StartsWith("GET /api/v2/libraries/roots?", StringComparison.Ordinal)));
        Check(server.Requests.Count(line => line.Contains("skipped-roots", StringComparison.Ordinal) || line.Contains("stale-ids", StringComparison.Ordinal)) == otherReadsBefore, "opening the Ambiguous tile reads only the roots");
        await Until(() => Find<Button>(shell, "AmbiguousOverride_" + Uri.EscapeDataString("/media/movies/Root 1")) != null);
        Check(Name(Find<Button>(shell, "LibrariesTile_Ambiguous")).StartsWith(AdminLibrariesText.T("web.admin.libraries.health.inspect") + " ", StringComparison.Ordinal),
            "with several libraries the Ambiguous tile does not present one library's count as the catalog's");
        Check(Texts(shell).Contains(AdminRootLabels.ItemType("movie")) && Texts(shell).Contains(AdminLibrariesText.T("web.admin.libraries.ambiguousRoots.confidence.low")) && !Texts(shell).Contains("low"),
            "ambiguous roots show their type and confidence in the interface language (Movie, Low)");
        // Keyboard on the tiles: Right moves to Skipped and selects it; End to Stale.
        Find<Button>(shell, "LibrariesTile_Ambiguous")!.Focus(FocusState.Keyboard);
        await Task.Delay(100);
        if (TrySendKey(0x27))
        {
            await Task.Delay(200);
            Check(page.SelectedHealthSection == AdminHealthSection.Skipped && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(shell.XamlRoot), Find<Button>(shell, "LibrariesTile_Skipped")), "Right moves to the next tile and selects it");
            TrySendKey(0x23); await Task.Delay(200);
            Check(page.SelectedHealthSection == AdminHealthSection.Stale, "End selects the last tile");
        }
        else { Check(false, "keyboard input reaches the focused tile"); await InvokeAsync(Find<Button>(shell, "LibrariesTile_Stale")); }
        await Until(() => server.Requests.Any(line => line.StartsWith("GET /api/v2/libraries/stale-ids", StringComparison.Ordinal)) && server.Requests.Any(line => line.StartsWith("GET /api/v2/libraries/skipped-roots", StringComparison.Ordinal)));
        await Until(() => page.TileStatus(AdminHealthSection.Stale).Count != null && page.TileStatus(AdminHealthSection.Skipped).Count != null);
        Check(Name(Find<Button>(shell, "LibrariesTile_Skipped")).StartsWith("3 ", StringComparison.Ordinal) && Name(Find<Button>(shell, "LibrariesTile_Stale")).StartsWith("1 ", StringComparison.Ordinal),
            "a tile shows its count once its section has read (Skipped 3, Stale 1)");
        // Leaving a tile closes it: the expanded matcher library is collapsed when the tile is chosen again.
        await InvokeAsync(Find<Button>(shell, "LibrariesTile_Matcher"));
        await Until(() => Find<HyperlinkButton>(shell, "MatcherLibrary_1") != null);
        await InvokeAsync(Find<HyperlinkButton>(shell, "MatcherLibrary_1"));
        await Until(() => Find<FrameworkElement>(shell, "MatcherDetail") != null);
        await InvokeAsync(Find<Button>(shell, "LibrariesTile_Stale"));
        await Until(() => page.SelectedHealthSection == AdminHealthSection.Stale && matcher.State.SelectedLibraryId == null);
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Libraries"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Libraries);
        Check(!matcher.State.Open, "leaving Catalog Health closes the matcher section (no detail polling)");
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Health"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Health);
        Check(page.SelectedHealthSection == AdminHealthSection.Stale, "returning to Catalog Health keeps the selected tile");
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Libraries"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Libraries);

        // ── Failed reads on each tab leave a reachable way to try again ──
        // Catalog Health: both counts fail.
        server.UnmatchedReadFails = true; server.MatcherReadFails = true;
        page = await OpenFreshAsync(shell, inner, page, new Dictionary<string, string> { ["tab"] = "health" });
        var failedUnmatched = (AdminUnmatchedPanel)Field(page, "_unmatched")!;
        var failedMatcher = (AdminMatcherQueuesPanel)Field(page, "_matcher")!;
        await Until(() => failedUnmatched.State.TotalsError != null && failedMatcher.State.Error != null && Reached(Find<Button>(shell, "LibrariesTile_Matcher")));
        Check(page.TileStatus(AdminHealthSection.Unmatched).Error && page.TileStatus(AdminHealthSection.Matcher).Error, "a failed total shows the error mark on its tile, not a zero");
        Check(Name(Find<Button>(shell, "LibrariesTile_Unmatched")).StartsWith(AdminLibrariesText.T("web.admin.common.diagnosticsSection.countError"), StringComparison.Ordinal), "the error tile is named by the count error, not an empty count");
        Check(!Name(Find<Button>(shell, "LibrariesTab_Health")).Any(char.IsDigit), "the Catalog Health tab shows no badge while no count is known");
        await Until(() => Find<Button>(shell, "UnmatchedTotalsRetry") != null || Find<Button>(shell, "UnmatchedRetry") != null);
        server.UnmatchedReadFails = false;
        await InvokeAsync(Find<Button>(shell, "UnmatchedTotalsRetry") ?? Find<Button>(shell, "UnmatchedRetry"));
        await Until(() => failedUnmatched.State.AwaitingTotal != null);
        Check(!page.TileStatus(AdminHealthSection.Unmatched).Error && page.TileStatus(AdminHealthSection.Unmatched).Count > 0, "Retry brings the Unmatched count back");
        await InvokeAsync(Find<Button>(shell, "LibrariesTile_Matcher"));
        await Until(() => Find<Button>(shell, "MatcherRetryLoad") != null);
        server.MatcherReadFails = false;
        await InvokeAsync(Find<Button>(shell, "MatcherRetryLoad"));
        await Until(() => failedMatcher.State.HasData && failedMatcher.State.Error == null);
        Check(!page.TileStatus(AdminHealthSection.Matcher).Error && page.TileStatus(AdminHealthSection.Matcher).Count == 3, "Retry brings the Metadata Matcher count back");

        // Scan Activity: the refresh jobs read is on its way (Loading, not empty), then fails (a footer with Restart jobs), then recovers.
        foreach (var scan in page.Controller!.State.Scans.ToArray())
            realtime.Event("scans", "scan.cancelled", "{\"id\":\"" + scan.Id + "\",\"library_id\":" + scan.LibraryId + ",\"mode\":\"" + scan.Mode + "\",\"status\":\"cancelled\"}");
        await Until(() => page.Controller!.State.Scans.Count == 0);
        server.JobsEmpty = true;
        server.JobsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        page = await OpenFreshAsync(shell, inner, page, new Dictionary<string, string> { ["tab"] = "activity" });
        await Until(() => Reached(Find<FrameworkElement>(shell, "LibrariesActivityLoading")));
        Check(!page.Controller!.State.RefreshJobsLoaded && Find<FrameworkElement>(shell, "LibrariesActivityEmpty") == null, "Scan Activity says Loading, not that nothing is running, while the refresh jobs read is pending");
        server.JobsGate.SetResult();
        await Until(() => Reached(Find<FrameworkElement>(shell, "LibrariesActivityEmpty")));
        Check(Find<FrameworkElement>(shell, "LibrariesActivityLoading") == null || !Reached(Find<FrameworkElement>(shell, "LibrariesActivityLoading")), "the loading line gives way to the empty state");
        Check(Texts(shell).Contains(AdminLibrariesText.T("web.admin.libraries.activity.emptyTitle")) && Texts(shell).Contains(AdminLibrariesText.T("web.admin.libraries.activity.emptyDescription")), "an idle Scan Activity says No active scans or refreshes");
        // With nothing running or refreshing, a library with nothing else to say reads Idle on the Libraries tab.
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Libraries"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Libraries && Reached(Find<FrameworkElement>(shell, "LibrariesTable")));
        Check(Reached(Find<FrameworkElement>(shell, "LibrariesIdle_2")) && Find<FrameworkElement>(shell, "LibrariesIdle_1") == null && Find<FrameworkElement>(shell, "LibrariesSummary") is not { Visibility: Visibility.Visible }, "an idle library says Idle, one with a warning or a monitoring problem does not, and the summary strip is gone");
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Activity"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Activity && Reached(Find<FrameworkElement>(shell, "LibrariesActivityEmpty")));
        // The footer keeps pagination in the empty state: Load older refresh jobs reads the next page.
        var older = Find<Button>(shell, "LibrariesLoadOlderJobs");
        Check(older != null, "Load older refresh jobs stays reachable in the empty state (the footer is preserved)");
        await InvokeAsync(older);
        await Until(() => server.Requests.Any(line => line.StartsWith("GET /api/v2/admin/jobs?", StringComparison.Ordinal) && line.Contains("cursor=older", StringComparison.Ordinal)));
        Check(server.Requests.Count(line => line.StartsWith("GET /api/v2/admin/jobs?", StringComparison.Ordinal) && line.Contains("cursor=older", StringComparison.Ordinal)) == 1, "one click reads one older page");
        await Until(() => Find<Button>(shell, "LibrariesLoadOlderJobs") == null);
        Check(Reached(Find<FrameworkElement>(shell, "LibrariesActivityEmpty")), "once the last older page is read the footer drops its Load older button and the empty state stays");
        server.JobsReadFails = true;
        _ = page.Controller!.RestartJobsAsync(); // a failed re-read of the first page, as a jobs event or a poll can cause
        await Until(() => page.Controller!.State.RefreshJobsFailed);
        await Until(() => Find<Button>(shell, "LibrariesRestartJobs") != null);
        Check(Find<FrameworkElement>(shell, "LibrariesActivityEmpty") == null, "a failed jobs read shows its footer with Restart jobs and no empty state");
        server.JobsReadFails = false;
        await InvokeAsync(Find<Button>(shell, "LibrariesRestartJobs"));
        await Until(() => !page.Controller!.State.RefreshJobsFailed && Reached(Find<FrameworkElement>(shell, "LibrariesActivityEmpty")));
        Check(Find<Button>(shell, "LibrariesRestartJobs") == null && Find<Button>(shell, "LibrariesLoadOlderJobs") != null, "Restart jobs recovers the tab and offers the older jobs again");
        server.JobsEmpty = false;

        // The whole list failing is reachable from the other tabs too: its banner and Retry sit above the tab strip.
        server.LibrariesReadFails = true;
        page = await OpenFreshAsync(shell, inner, page, new Dictionary<string, string> { ["tab"] = "activity" });
        await Until(() => Find<Button>(shell, "LibrariesRetry") != null);
        Check(Reached(Find<Button>(shell, "LibrariesRetry")) && page.SelectedTab == AdminLibrariesTab.Activity, "a failed list read keeps Retry reachable while Scan Activity is selected");
        server.LibrariesReadFails = false;
        await InvokeAsync(Find<Button>(shell, "LibrariesRetry"));
        await Until(() => page.Controller!.State.Error == null && page.Controller.State.Libraries.Count > 0);
        Check(Find<Button>(shell, "LibrariesRetry") == null, "Retry reloads the list and clears the banner");

        // ── Widths: the tab strip holds at touch size below 1024 px, the tiles fold to 3 and 2 columns ──
        foreach (var (width, columns) in new[] { (1280, 5), (1024, 5), (800, 3), (640, 3), (560, 2) })
        {
            Resize(window, width, 700);
            await Until(() => page.SelectedTab == AdminLibrariesTab.Activity && Reached(Find<Button>(shell, "LibrariesTab_Health")));
            await InvokeAsync(Find<Button>(shell, "LibrariesTab_Health"));
            await Until(() => page.SelectedTab == AdminLibrariesTab.Health && Reached(Find<Button>(shell, "LibrariesTile_Stale")));
            await Task.Delay(200);
            var healthTab = Find<Button>(shell, "LibrariesTab_Health")!;
            var distinctColumns = tileNames.Select(name => Math.Round(Find<Button>(shell, "LibrariesTile_" + name)!.TransformToVisual(shell).TransformPoint(new(0, 0)).X)).Distinct().Count();
            Check(distinctColumns == columns, $"[{width}] the five tiles sit in {columns} columns (are in {distinctColumns})");
            Check(healthTab.ActualHeight >= (width >= 1024 ? 40 : 44) - 0.5, $"[{width}] the tab keeps its {(width >= 1024 ? 40 : 44)} px height (is {healthTab.ActualHeight})");
            var stripWidth = ((FrameworkElement)Field(page, "_tabStrip")!).ActualWidth;
            Check(stripWidth > 0 && stripWidth <= width + 1, $"[{width}] the tab strip stays inside the page width (is {stripWidth})");
            if (width == 800 || width == 560) { ((ScrollViewer)Field(page, "_scroll")!).ChangeView(null, 0, null, true); await CaptureAsync(outer, $"libraries-tabs-health-{width}.png"); }
            await InvokeAsync(Find<Button>(shell, "LibrariesTab_Activity"));
            await Until(() => page.SelectedTab == AdminLibrariesTab.Activity);
        }
        Resize(window, 1440, 900);
        await InvokeAsync(Find<Button>(shell, "LibrariesTab_Libraries"));
        await Until(() => page.SelectedTab == AdminLibrariesTab.Libraries);
        server.JobsEmpty = false; server.JobsReadFails = false; server.UnmatchedReadFails = false; server.MatcherReadFails = false; server.LibrariesReadFails = false;

        // The scan the later steps of the page expect the activity store to hold (a new page seeds its scans from it).
        realtime.Event("scans", "scan.started", "{\"id\":\"s1\",\"library_id\":1,\"mode\":\"library\",\"status\":\"running\",\"trigger\":\"manual\"}");
        await Until(() => page.Controller!.State.Scans.Any(scan => scan.Id == "s1"));

        // The remaining steps of the page run on a clean Libraries page.
        page = await OpenFreshAsync(shell, inner, page, null);
        await Until(() => Reached(Find<FrameworkElement>(shell, "LibrariesTable")));
        return page;
    }

    private static int controller_Count(AdminLibrariesPage page) => page.Controller!.State.Libraries.Count;
}
