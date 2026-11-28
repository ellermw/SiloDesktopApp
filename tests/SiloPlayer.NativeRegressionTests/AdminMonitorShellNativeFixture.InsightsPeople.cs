using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

/// <summary>
/// Insights › People, Devices &amp; Quality and Bandwidth, and the person panel, over synthetic reads (official c59798116 schema): every toggle
/// and sort, the People search and "Show more accounts", the panel for an account, a profile and an unlisted profile with a title opened from it
/// and a return, and the empty and error states. Every wait has a time limit; a read that never answers fails the case, never hangs it. Captures
/// at 1440 and 560 wide are written for each tab, scrolled so every card shows at least once. No live server, account or user data.
/// </summary>
internal static partial class AdminMonitorShellNativeFixture
{
    private static async Task InsightsPeopleAsync(Window window, Server server, AuthService auth, NavigationService navigation)
    {
        var data = server.Insights;
        data.ServeUsers = true;
        server.SessionCount = 3;
        server.RestartRequired = false;
        auth.SetCurrentUser(new SiloPlayer.Core.Models.Auth.UserInfo { Id = "1", Username = "synthetic-admin", Role = "admin" });
        var frame = new Frame { Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AppBackgroundBrush"] };
        window.Content = frame;
        navigation.Frame = frame;
        AdminRouteRequest Request(params (string, string)[] query) =>
            new(AdminRoute.Insights, query.Length == 0 ? null : query.ToDictionary(pair => pair.Item1, pair => pair.Item2, StringComparer.Ordinal));
        frame.Navigate(typeof(AdminShellPage), Request(("tab", "people")));
        var shell = (AdminShellPage)frame.Content;
        var inner = (Frame)Field(Sheet(), "_frame")!;
        var insights = (AdminInsightsPage)inner.Content;

        T? Find<T>(DependencyObject root, string id) where T : DependencyObject =>
            Descendants(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);
        Button[] ButtonsById(DependencyObject root, string id) => [.. Descendants(root).OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button) == id)];
        Button? Named(DependencyObject root, string name) => Descendants(root).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetName(button) == name);
        async Task Wait(Func<bool> condition, int milliseconds = 5000) => await Until(() => { try { return condition(); } catch (Exception) { return false; } }, milliseconds);
        bool Try(Func<bool> condition) { try { return condition(); } catch (ArgumentException) { return false; } }
        bool Has(DependencyObject root, string text) => Try(() => Descendants(root).OfType<TextBlock>().Any(label => label.Text == text));
        bool HasCI(DependencyObject root, string text) => Try(() => Descendants(root).OfType<TextBlock>().Any(label => string.Equals(label.Text, text, StringComparison.OrdinalIgnoreCase)));
        bool HasStarting(DependencyObject root, string text) => Try(() => Descendants(root).OfType<TextBlock>().Any(label => label.Text.StartsWith(text, StringComparison.Ordinal)));
        bool HasContaining(DependencyObject root, string text) => Try(() => Descendants(root).OfType<TextBlock>().Any(label => label.Text.Contains(text, StringComparison.Ordinal)));
        Grid? Card(string title) => Descendants(insights).OfType<Grid>().FirstOrDefault(grid => grid.GetType().Name == "AdminInsightsCard" && AutomationProperties.GetName(grid) == title);
        // A control may be missing for a moment while a result is rebuilt; a null must never reach the platform call.
        bool Pressed(Button? button) => button != null && AutomationProperties.GetItemStatus(button) == "pressed";
        Button[] Links(DependencyObject root) => [.. Descendants(root).OfType<Button>().Where(button => button.Tag as string == "insights-link")];
        bool HasFilter(string filter) => Find<FrameworkElement>(insights, "AdminInsightsFilter_" + filter) != null;
        int ReadsOf(string needle) { lock (data.Calls) return data.Calls.Count(call => call.Contains(needle, StringComparison.Ordinal)); }
        string LastRead(string path) { lock (data.Calls) return data.Calls.LastOrDefault(call => call.Contains(path, StringComparison.Ordinal)) ?? ""; }
        Button? Add(string widget) => Find<Button>(insights, "AdminInsightsAdd_" + widget);
        async Task Resize(int width)
        {
            var scale = frame.XamlRoot?.RasterizationScale ?? 1;
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(900 * scale)));
            await Task.Delay(350);
        }
        async Task Open(AdminRouteRequest request)
        {
            var previous = insights;
            shell.Navigate(request);
            await Wait(() => inner.Content is AdminInsightsPage page && !ReferenceEquals(page, previous));
            insights = (AdminInsightsPage)inner.Content;
        }
        async Task CaptureTab(string name, int width)
        {
            var scroll = (ScrollViewer)Field(insights, "_scroll")!;
            scroll.ChangeView(null, 0, null, true);
            await Task.Delay(150);
            await CaptureAsync(frame, $"insights-{name}-{width}.png");
            // Scrolled through, so every card is captured at least once at the real window size.
            var step = Math.Max(200, scroll.ViewportHeight - 120);
            var shot = 1;
            for (var offset = step; shot < 14; offset += step)
            {
                scroll.ChangeView(null, Math.Min(offset, scroll.ScrollableHeight), null, true);
                await Task.Delay(250);
                await CaptureAsync(frame, $"insights-{name}-{width}-s{shot++}.png");
                if (offset >= scroll.ScrollableHeight) break;
            }
            scroll.ChangeView(null, 0, null, true);
            await Task.Delay(150);
            await CaptureAsync((FrameworkElement)Field(insights, "_body")!, $"insights-{name}-{width}-full.png");
        }
        async Task CaptureSheet(string name)
        {
            await Task.Delay(300);
            await CaptureAsync(frame, name);
        }
        async Task CaptureSheetEnd(string name)
        {
            var sheet = Descendants(shell).OfType<Grid>().First(grid => grid.GetType().Name == "LogsDetailSheet");
            var scroller = Descendants(sheet).OfType<ScrollViewer>().First();
            scroller.ChangeView(null, scroller.ScrollableHeight, null, true);
            await Task.Delay(350);
            await CaptureAsync(frame, name);
        }
        async Task Choose(Button button, string text)
        {
            button.Flyout!.ShowAt(button);
            MenuFlyoutItem? item = null;
            await Wait(() => (item = VisualTreeHelper.GetOpenPopupsForXamlRoot(button.XamlRoot).SelectMany(popup => Descendants(popup.Child)).OfType<MenuFlyoutItem>().FirstOrDefault(entry => entry.Text == text)) != null);
            Check(item != null, $"the flyout offers '{text}'");
            if (item != null) ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();
            await Task.Delay(150);
        }
        DependencyObject Sheet() => Descendants(shell).OfType<Grid>().FirstOrDefault(grid => grid.GetType().Name == "LogsDetailSheet") ?? (DependencyObject)shell;
        async Task Click(Func<Button?> find)
        {
            await Wait(() => find() is { IsLoaded: true, IsEnabled: true });
            var button = find();
            Check(button is { IsLoaded: true }, "a control to press was ready");
            if (button != null) Invoke(button);
        }
        Button? OnDashboard(string widget) => Find<Button>(insights, "AdminInsightsOnDashboard_" + widget);
        int RowCount() => ButtonsById(insights, "AdminInsightsPeopleHistory").Length;
        Button? SortHeader(string column) => Find<Button>(insights, "AdminInsightsPeopleSort_" + column);
        string SortStatus(string column) => SortHeader(column) is { } header ? AutomationProperties.GetItemStatus(header) : "(missing)";

        // ── People ───────────────────────────────────────────
        await Resize(1440);
        await Wait(() => Find<TextBox>(insights, "AdminInsightsPeopleSearch") != null && RowCount() > 0, 10000);
        Check(new[] { "Active accounts", "Active profiles", "Total watch time" }.All(label => Has(insights, label)), "People shows its three KPI cards");
        Check(Has(insights, "Exact watch time is available for 400 of 420 sessions; older sessions are estimated."), "People shows the watch-time banner when some sessions were not measured");
        Check(new[] { "Account", "Watch time", "Plays", "Direct play", "Completion", "Last seen", "Actions" }.All(label => Has(insights, label)), "People shows the seven column headings");
        Check(RowCount() == 50, $"People lists the first 50 accounts ({RowCount()})");
        Check(Find<Button>(insights, "AdminInsightsPeopleMore") != null, "People offers Show more accounts when there are more");
        Check(Has(insights, "Live") && Descendants(insights).OfType<TextBlock>().Count(label => label.Text == "Live") == 3, "three live accounts carry the Live badge");
        Check(Has(insights, "3 profiles") && Has(insights, "Silo Desktop · Windows") && Has(insights, "Silo for Roku · Roku OS"), "People shows profile counts and each account's top client");
        Check(Has(insights, "3m ago") && Descendants(insights).OfType<TextBlock>().Any(label => label.Text == "3m ago" && ToolTipService.GetToolTip(label) is string tip && tip.Length > 3), "Last seen is relative with the absolute time as its tooltip");
        Check(Has(insights, "—"), "an account never seen shows a dash");
        Check(!HasCI(insights, "Most active profiles"), "People has no Most active profiles list (the official tab has none)");
        Check(HasFilter("allLibraries") && !HasFilter("allAccounts") && HasFilter("timezone"), "People hides only the account filter");
        Check(SortStatus("WatchTime") == "descending", "People starts sorted by watch time, descending");
        await Wait(() => Add("top-profiles") != null || OnDashboard("top-profiles") != null, 10000);
        Check(Add("top-profiles") != null || OnDashboard("top-profiles") != null, "People offers Add to dashboard for Top profiles");
        {
            // The table fits its card at this width: its scroll area has nothing to scroll, so the Actions column is not cut off.
            var peopleTable = Descendants(insights).OfType<Grid>().First(grid => grid.GetType().Name == "AdminInsightsTable");
            var peopleScroll = Descendants(peopleTable).OfType<ScrollViewer>().First();
            Check(peopleScroll.ExtentWidth <= peopleScroll.ViewportWidth + 1, $"at 1440 the People table fits its card ({peopleScroll.ExtentWidth:0} of {peopleScroll.ViewportWidth:0})");
            Check(Descendants(peopleTable).OfType<Button>().Count(button => AutomationProperties.GetAutomationId(button) == "AdminInsightsPeopleDetails" && button.ActualWidth > 30) > 40, "every Details link is drawn in full");
        }
        await CaptureTab("people", 1440);

        // Expanding an account lists its household profiles.
        var before = RowCount();
        var expanders = Descendants(insights).OfType<Button>().Where(button => (AutomationProperties.GetAutomationId(button) ?? "").StartsWith("AdminInsightsPeopleExpand_", StringComparison.Ordinal)).ToArray();
        Check(expanders.Length > 0 && AutomationProperties.GetName(expanders[0]).StartsWith("Show household profiles for ", StringComparison.Ordinal), "an account with profiles has an expand button named for it");
        await Click(() => Find<Button>(insights, "AdminInsightsPeopleExpand_1"));
        await Click(() => Find<Button>(insights, "AdminInsightsPeopleExpand_3"));
        await Wait(() => RowCount() == before + 4);
        Check(RowCount() == before + 4 && Has(insights, "Kids") && Has(insights, "Movie Night With A Long Profile Name") && Has(insights, "Profile pf3x2-ab"), "expanding shows every profile, naming an unnamed one by its id");
        Check(AutomationProperties.GetName(Find<Button>(insights, "AdminInsightsPeopleExpand_3")!).StartsWith("Hide household profiles for ", StringComparison.Ordinal), "an expanded account's button offers to hide its profiles");
        await CaptureAsync(frame, "insights-people-expanded-1440.png");
        await Click(() => Find<Button>(insights, "AdminInsightsPeopleExpand_1"));
        await Click(() => Find<Button>(insights, "AdminInsightsPeopleExpand_3"));
        await Wait(() => RowCount() == before);

        // The sort rules: the active column flips, a new one starts descending.
        Program.Log("STEP sort: first click");
        await Click(() => SortHeader("Plays"));
        Program.Log("STEP sort: clicked");
        await Wait(() => ReadsOf("sort=plays&dir=desc") > 0);
        Check(ReadsOf("sort=plays&dir=desc") > 0, "a new sort column reads descending");
        await Wait(() => SortStatus("Plays") == "descending");
        await Click(() => SortHeader("Plays"));
        await Wait(() => ReadsOf("sort=plays&dir=asc") > 0);
        Check(ReadsOf("sort=plays&dir=asc") > 0, "the active column flips to ascending");
        await Click(() => SortHeader("LastSeen"));
        await Wait(() => ReadsOf("sort=last_seen&dir=desc") > 0);
        Check(ReadsOf("sort=last_seen&dir=desc") > 0, "choosing Last seen after an ascending column starts it descending");
        await Wait(() => SortStatus("LastSeen") == "descending");
        await Click(() => SortHeader("DirectPct"));
        await Wait(() => ReadsOf("sort=direct_pct&dir=desc") > 0);
        await Click(() => SortHeader("CompletionPct"));
        await Wait(() => ReadsOf("sort=completion_pct&dir=desc") > 0);
        Check(ReadsOf("sort=direct_pct&dir=desc") > 0 && ReadsOf("sort=completion_pct&dir=desc") > 0, "every sortable column reads by its own name");
        await Click(() => SortHeader("WatchTime"));
        await Wait(() => ReadsOf("sort=watch_time&dir=desc") > 1);

        // A new read keeps the rows on screen with their links off, then replaces them.
        data.HoldPeople = new TaskCompletionSource();
        // The active column flips to ascending, a read this page has not made before.
        await Click(() => SortHeader("WatchTime"));
        await Wait(() => LastRead("/insights/people").Contains("sort=watch_time&dir=asc", StringComparison.Ordinal));
        await Task.Delay(300);
        Check(RowCount() == 50 && Links(insights).Length > 0 && Links(insights).All(link => !link.IsEnabled), "while a new sort loads the rows stay and their links are off");
        Check(Find<Button>(insights, "AdminInsightsPeopleMore") == null, "Show more accounts is hidden while a new read loads");
        await CaptureAsync(frame, "insights-people-held-1440.png");
        data.HoldPeople.SetResult();
        data.HoldPeople = null;
        await Wait(() => Links(insights).Length > 0 && Links(insights).All(link => link.IsEnabled));
        Check(Links(insights).All(link => link.IsEnabled), "the new sort's links work again");
        await Click(() => SortHeader("WatchTime"));
        await Wait(() => SortStatus("WatchTime") == "descending" && RowCount() == 50);

        // Show more accounts adds the next 50 (here 25) and goes away at the end.
        await Click(() => Find<Button>(insights, "AdminInsightsPeopleMore"));
        await Wait(() => RowCount() == 75);
        Check(RowCount() == 75 && ReadsOf("offset=50") > 0 && Find<Button>(insights, "AdminInsightsPeopleMore") == null, "Show more accounts loads the next page and ends at the total");
        await CaptureAsync(frame, "insights-people-all-1440.png");

        // The search is applied after a quarter of a second, keeps the box and its focus, and an empty match says so.
        var box = Find<TextBox>(insights, "AdminInsightsPeopleSearch")!;
        box.Focus(FocusState.Programmatic);
        var searchReads = ReadsOf("search=");
        box.Text = "viewer-6";
        await Task.Delay(120);
        Check(ReadsOf("search=") == searchReads, "typing alone reads nothing before the debounce");
        await Wait(() => ReadsOf("search=viewer-6") > 0);
        await Wait(() => RowCount() == 10 && !Links(insights).Any(link => !link.IsEnabled));
        Check(RowCount() == 10 && ReferenceEquals(Find<TextBox>(insights, "AdminInsightsPeopleSearch"), box) && box.Text == "viewer-6", "the search narrows the rows and the same box stays");
        Check(ReferenceEquals(FocusManager.GetFocusedElement(frame.XamlRoot), box), "the search box keeps its focus across the read");
        await CaptureAsync(frame, "insights-people-search-1440.png");
        box.Text = "zzzz-nobody";
        await Wait(() => Has(insights, "No viewing activity matches these filters"));
        Check(Has(insights, "No viewing activity matches these filters") && Find<TextBox>(insights, "AdminInsightsPeopleSearch") == box, "a search with no match shows the empty line and keeps the box");
        await CaptureAsync(frame, "insights-people-nomatch-1440.png");
        box.Text = "";
        // The earlier read of this sort is kept while it is fresh, so it comes back with the pages it had.
        await Wait(() => RowCount() >= 50 && !Has(insights, "No viewing activity matches these filters"));

        // A person from a row, and a History drilldown with the window and no library.
        await Click(() => ButtonsById(insights, "AdminInsightsPeopleHistory")[1]);
        await Wait(() => shell.CurrentRequest.Route == AdminRoute.PlaybackHistory);
        Check(shell.CurrentRequest.Route == AdminRoute.PlaybackHistory && shell.CurrentRequest.Get("user_id") != null && shell.CurrentRequest.Get("started_after") != null && shell.CurrentRequest.Get("started_before") != null,
            "History on a row opens Playback History for that account in the window");
        await Open(Request(("tab", "people"), ("library_id", "1")));
        await Wait(() => RowCount() > 0);
        await Click(() => ButtonsById(insights, "AdminInsightsPeopleHistory")[0]);
        await Wait(() => shell.CurrentRequest.Route == AdminRoute.PlaybackHistory);
        Check(shell.CurrentRequest.Get("library_id") == "1", "a History drilldown keeps the page's library");
        await Open(Request(("tab", "people")));
        await Wait(() => RowCount() > 0);

        // ── The person panel ─────────────────────────────────
        await Click(() => ButtonsById(insights, "AdminInsightsPeopleDetails")[2]);
        await Wait(() => shell.CurrentRequest.Get("person") != null && HasCI(Sheet(), "Top titles") && HasCI(Sheet(), "Clients"));
        var person = shell.CurrentRequest.Get("person");
        await Wait(() => HasCI(Sheet(), "Plays over time") && Has(Sheet(), "Completion rate") && Has(Sheet(), "Direct play rate"));
        Check(person == "3" && shell.CurrentRequest.Get("profile") == null, "Details on an account opens its panel");
        Check(new[] { "Plays", "Watch time", "Completion rate", "Direct play rate" }.All(label => Has(shell, label)) && HasCI(Sheet(), "Plays over time") && HasCI(Sheet(), "Top titles") && HasCI(Sheet(), "Clients"),
            "the person panel shows four KPI cards, Plays over time, Top titles and Clients");
        Check(Has(Sheet(), "viewer-03"), "the panel is headed by the account's name");
        Check(Find<Button>(Sheet(), "AdminInsightsPersonProfile") != null && Find<Button>(Sheet(), "AdminInsightsPersonHistory") != null, "the panel offers the profile picker and View history");
        Check(Find<Button>(Sheet(), "AdminInsightsPersonOpenAccount") != null == AdminShellPage.CanOpen(AdminRouteRequest.UserDetail("3")), "Open account shows only when the admin user page can open");
        bool PersonRead(string path, string needle) { lock (data.Calls) return data.Calls.Any(call => call.Contains(path, StringComparison.Ordinal) && call.Contains("user_id=3", StringComparison.Ordinal) && call.Contains(needle, StringComparison.Ordinal)); }
        Check(PersonRead("/insights/activity", "user_id=3") && PersonRead("/insights/titles", "limit=8") && PersonRead("/insights/devices", "min_sessions=1"), "the panel makes three reads scoped to the account");
        Check(Has(Sheet(), "Silo Desktop · Windows") && HasContaining(Sheet(), "% direct play"), "Clients lists the person's clients with their direct-play share");
        await CaptureSheet("insights-person-panel-all-1440.png");
        await CaptureSheetEnd("insights-person-panel-all-1440-end.png");

        // A title opened from the panel keeps the person; closing it returns to the panel.
        var topTitle = Links(Sheet()).FirstOrDefault();
        Check(topTitle != null, "Top titles rows are links");
        if (topTitle != null)
        {
            await Click(() => topTitle);
            await Wait(() => shell.CurrentRequest.Get("title") != null && HasCI(Sheet(), "Who watched"));
            Check(shell.CurrentRequest.Get("title") != null && shell.CurrentRequest.Get("person") == "3" && HasStarting(Sheet(), "Plays by "), "a title opened from the panel stays scoped to the person");
            await CaptureSheet("insights-person-title-1440.png");
            await Click(() => Find<Button>(shell, "LogsDetailClose"));
            await Wait(() => shell.CurrentRequest.Get("title") == null && HasCI(Sheet(), "Top titles"));
            Check(shell.CurrentRequest.Get("title") == null && shell.CurrentRequest.Get("person") == "3" && HasCI(Sheet(), "Top titles"), "closing the title returns to the person's panel");
        }

        // The profile picker.
        await Wait(() => Find<Button>(Sheet(), "AdminInsightsPersonProfile") != null);
        await Choose(Find<Button>(Sheet(), "AdminInsightsPersonProfile")!, "Kids");
        await Wait(() => shell.CurrentRequest.Get("profile") == "p1" && PersonRead("/insights/activity", "profile_id=p1"));
        Check(shell.CurrentRequest.Get("profile") == "p1" && PersonRead("/insights/activity", "profile_id=p1") && PersonRead("/insights/devices", "profile_id=p1"), "choosing a profile reads all three scoped to it");
        await Wait(() => Has(Sheet(), "Kids") && HasStarting(Sheet(), "viewer-03 · "));
        Check(Has(Sheet(), "Kids") && HasStarting(Sheet(), "viewer-03 · "), "a profile heads the panel and its account is named under it");
        await CaptureSheet("insights-person-panel-profile-1440.png");
        await Choose(Find<Button>(Sheet(), "AdminInsightsPersonProfile")!, "All profiles");
        await Wait(() => shell.CurrentRequest.Get("profile") == null);
        Check(shell.CurrentRequest.Get("profile") == null && shell.CurrentRequest.Get("person") == "3", "All profiles drops the profile and keeps the account");

        // View history, then back.
        await Click(() => Find<Button>(Sheet(), "AdminInsightsPersonHistory"));
        await Wait(() => shell.CurrentRequest.Route == AdminRoute.PlaybackHistory);
        Check(shell.CurrentRequest.Route == AdminRoute.PlaybackHistory && shell.CurrentRequest.Get("user_id") == "3" && shell.CurrentRequest.Get("started_after") != null, "View history opens the person's sessions in the window");

        // A profile the account no longer lists is still reachable.
        await Open(Request(("tab", "people"), ("person", "7"), ("profile", "ghost-profile-id")));
        await Wait(() => Has(Sheet(), "Profile ghost-pr") && Find<Button>(Sheet(), "AdminInsightsPersonProfile") != null);
        Check(Has(Sheet(), "Profile ghost-pr") && Find<Button>(Sheet(), "AdminInsightsPersonProfile") != null, "a profile the account does not list is labelled by its id and keeps the picker");
        await CaptureSheet("insights-person-panel-unlisted-1440.png");

        // Errors, retry and an empty window.
        data.Fail = true;
        await Open(Request(("tab", "people"), ("person", "8")));
        await Wait(() => Descendants(Sheet()).OfType<Border>().Count(border => AutomationProperties.GetAutomationId(border) == "AdminInsightsSectionError") == 3, 8000);
        Check(Descendants(Sheet()).OfType<Border>().Count(border => AutomationProperties.GetAutomationId(border) == "AdminInsightsSectionError") == 3, "each of the panel's three reads shows its own error");
        await CaptureSheet("insights-person-panel-error-1440.png");
        data.Fail = false;
        await Click(() => Named(Descendants(Sheet()).OfType<Border>().First(border => AutomationProperties.GetAutomationId(border) == "AdminInsightsSectionError"), "Retry"));
        await Wait(() => Descendants(Sheet()).OfType<Border>().Count(border => AutomationProperties.GetAutomationId(border) == "AdminInsightsSectionError") == 2);
        Check(Descendants(Sheet()).OfType<Border>().Count(border => AutomationProperties.GetAutomationId(border) == "AdminInsightsSectionError") == 2, "Retry reads only that part again");
        data.Empty = true;
        await Open(Request(("tab", "people"), ("person", "9")));
        await Wait(() => Has(Sheet(), "No plays in this period"));
        Check(Has(Sheet(), "No plays in this period") && Has(Sheet(), "No activity in this period") && Has(Sheet(), "No playback in this period"), "an empty person shows each part's own empty line");
        await CaptureSheet("insights-person-panel-empty-1440.png");
        await Open(Request(("tab", "people"), ("range", "all")));
        await Wait(() => Has(insights, "No viewing activity matches these filters"));
        Check(Has(insights, "No viewing activity matches these filters") && Find<TextBox>(insights, "AdminInsightsPeopleSearch") != null, "an empty People window shows the empty line under the search box");
        await CaptureTab("people-empty", 1440);
        data.Empty = false;
        data.Fail = true;
        await Open(Request(("tab", "people"), ("range", "7d")));
        await Wait(() => Find<Border>(insights, "AdminInsightsSectionError") != null);
        Check(Find<Border>(insights, "AdminInsightsSectionError") != null && Find<TextBox>(insights, "AdminInsightsPeopleSearch") != null, "a failed People read shows the error with the search box still there");
        await CaptureAsync(frame, "insights-people-error-1440.png");
        data.Fail = false;
        await Click(() => Named(Find<Border>(insights, "AdminInsightsSectionError")!, "Retry"));
        await Wait(() => RowCount() > 0);
        Check(RowCount() > 0, "Retry reads People again");

        // A failed refresh must leave the persistent People frame mounted, and recovery must clear the notice.
        var refresh = Find<Button>(insights, "AdminInsightsRefresh")!;
        var searchBeforeRefresh = Find<TextBox>(insights, "AdminInsightsPeopleSearch");
        var rowsBeforeRefresh = RowCount();
        bool HasRefreshError() => HasStarting(insights, "Failed to load insights.");
        data.Fail = true;
        await Click(() => refresh);
        await Wait(() => refresh.IsEnabled && HasRefreshError());
        Check(HasRefreshError() && RowCount() == rowsBeforeRefresh && ReferenceEquals(Find<TextBox>(insights, "AdminInsightsPeopleSearch"), searchBeforeRefresh),
            "a failed People refresh shows a notice while keeping the rows and the same search box");
        data.Fail = false;
        await Click(() => refresh);
        await Wait(() => refresh.IsEnabled && !HasRefreshError());
        Check(!HasRefreshError() && RowCount() == rowsBeforeRefresh && ReferenceEquals(Find<TextBox>(insights, "AdminInsightsPeopleSearch"), searchBeforeRefresh),
            "a successful People refresh clears the notice without replacing the search box");

        // ── Devices & Quality ────────────────────────────────
        await Open(Request(("tab", "devices")));
        await Wait(() => Card("Direct play by app & video format") != null && Descendants(insights).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminInsightsMatrixCell"), 10000);
        Check(HasFilter("allLibraries") && HasFilter("allAccounts") && HasFilter("timezone"), "Devices shows the library, account and time zone filters");
        Check(new[] { "Direct play rate", "Hardware transcoding", "Subtitle burn-ins", "HDR tone mapping" }.All(label => Has(insights, label)), "Devices shows its four KPI cards");
        Check(HasStarting(insights, "Detailed playback info is available for 380 of 420 sessions (tracked since "), "Devices shows the partial coverage banner");
        var cells = ButtonsById(insights, "AdminInsightsMatrixCell");
        Check(cells.Length == 9, $"the matrix has a cell for each client and codec that had sessions ({cells.Length})");
        Check(Pressed(Named(insights, "5+ sessions")!) && Named(insights, "All") != null, "the matrix offers 5+ sessions (the default) and All");
        Check(cells.Any(cell => AutomationProperties.GetName(cell) == "Silo Desktop · H.264: 99% direct (90 sessions; 1 transcode)"), "a matrix cell's tooltip says client, codec, rate and transcodes");
        Check(new[] { "H.264", "HEVC", "AV1", "VP9" }.All(label => Has(Card("Direct play by app & video format")!, label)) && Has(Card("Direct play by app & video format")!, "Unknown client"), "the matrix names its codec columns and a client with no name");
        Check(Has(Card("Direct play by app & video format")!, "JF"), "a Jellyfin client carries the JF pill");
        Check(new[] { "Why streams didn’t direct play", "Clients transcoding most", "Hardware acceleration", "Burned-in subtitle formats", "Resolution", "Silo apps vs Jellyfin apps", "App versions", "Subtitles", "Audio" }.All(title => Card(title) != null), "Devices shows every official card");
        Check(Has(Card("Why streams didn’t direct play")!, "a_newer_server_reason") && Has(Card("Why streams didn’t direct play")!, "50%"), "a reason this build does not know reads as its code, with its share");
        Check(new[] { "NVENC", "QSV", "VAAPI", "VideoToolbox", "Software (CPU)", "Unknown", "AMF" }.All(label => Has(Card("Hardware acceleration")!, label)), "hardware acceleration uses the official labels");
        Check(Has(Card("Resolution")!, "4K → 1080p") && Has(Card("Resolution")!, "8K") && Has(Card("Resolution")!, "SD"), "resolution names classes and downscales");
        Check(Has(Card("Subtitles")!, "In the stream") && Has(Card("Subtitles")!, "Streaming track") && Has(Card("Subtitles")!, "Unknown") && HasContaining(Card("Subtitles")!, "forced"), "subtitles show deliveries, an unknown delivery and the forced and accessibility counts");
        Check(Has(Card("Audio")!, "Original file") && Has(Card("Audio")!, "Surround") && Has(Card("Audio")!, "English"), "audio shows handling, channels and languages");
        await Wait(() => Descendants(insights).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminInsightsDevicesHistory"));
        await CaptureTab("devices", 1440);

        await Click(() => Named(insights, "All"));
        await Wait(() => ReadsOf("min_sessions=1") > 0 && ButtonsById(insights, "AdminInsightsMatrixCell").Length == 11);
        Check(ReadsOf("min_sessions=1") > 0 && ButtonsById(insights, "AdminInsightsMatrixCell").Length == 11 && Pressed(Named(insights, "All")!), "All reads with min_sessions=1 and shows the smaller clients");
        await CaptureAsync(Card("Direct play by app & video format")!, "insights-devices-matrix-all-1440.png");
        await Click(() => Named(insights, "5+ sessions"));
        await Wait(() => ButtonsById(insights, "AdminInsightsMatrixCell").Length == 9);

        // Drilldowns.
        await Click(() => ButtonsById(insights, "AdminInsightsMatrixCell")[0]);
        await Wait(() => shell.CurrentRequest.Route == AdminRoute.PlaybackHistory);
        Check(shell.CurrentRequest.Get("client_name") == "Silo Desktop" && shell.CurrentRequest.Get("device_platform") == "Windows" && shell.CurrentRequest.Get("source_video_codec") == "h264" && shell.CurrentRequest.Get("started_after") != null,
            "a matrix cell opens History for that client, platform and codec");
        await Open(Request(("tab", "devices"), ("user_id", "4"), ("library_id", "2")));
        await Wait(() => Card("Why streams didn’t direct play") != null && Links(Card("Why streams didn’t direct play")!).Length > 0);
        await Click(() => Links(Card("Why streams didn’t direct play")!)[0]);
        await Wait(() => shell.CurrentRequest.Route == AdminRoute.PlaybackHistory);
        Check(shell.CurrentRequest.Get("adaptation_reason") == "video_codec_unsupported" && shell.CurrentRequest.Get("user_id") == "4" && shell.CurrentRequest.Get("library_id") == "2", "a reason opens History with that reason and the page's account and library");
        await Open(Request(("tab", "devices")));
        await Wait(() => Card("Clients transcoding most") != null && Links(Card("Clients transcoding most")!).Length > 0);
        await Click(() => Links(Card("Clients transcoding most")!)[0]);
        await Wait(() => shell.CurrentRequest.Route == AdminRoute.PlaybackHistory);
        Check(shell.CurrentRequest.Get("play_method") == "transcode" && shell.CurrentRequest.Get("client_name") == "Silo for Roku", "a transcoding client opens History filtered to its transcodes");
        await Open(Request(("tab", "devices")));
        await Wait(() => Find<Button>(insights, "AdminInsightsDevicesHistory") != null);
        await Click(() => Find<Button>(insights, "AdminInsightsDevicesHistory"));
        await Wait(() => shell.CurrentRequest.Route == AdminRoute.PlaybackHistory);
        Check(shell.CurrentRequest.Get("subtitle_burn_in") == "true", "the burned-in card's History link filters to burned-in sessions");

        data.FullCoverage = true;
        await Open(Request(("tab", "devices"), ("range", "7d")));
        await Wait(() => Card("App versions") != null);
        Check(!HasStarting(insights, "Detailed playback info"), "full route-fact coverage shows no banner");
        data.FullCoverage = false;
        data.Empty = true;
        await Open(Request(("tab", "devices"), ("range", "all")));
        await Wait(() => Card("App versions") != null);
        Check(Has(insights, "Not enough sessions yet. Switch to “All” or widen the date range.") && Has(Card("Why streams didn’t direct play")!, "No conversion reasons recorded in this period") && Has(Card("App versions")!, "No app reported a version in this period"),
            "an empty Devices window shows each card's own empty line");
        await CaptureTab("devices-empty", 1440);
        data.Empty = false;
        data.Fail = true;
        await Open(Request(("tab", "devices"), ("range", "7d")));
        await Wait(() => Find<Border>(insights, "AdminInsightsSectionError") != null);
        Check(Find<Border>(insights, "AdminInsightsSectionError") != null && Card("App versions") == null, "a failed Devices read shows the load error");
        await CaptureAsync(frame, "insights-devices-error-1440.png");
        data.Fail = false;
        await Click(() => Named(Find<Border>(insights, "AdminInsightsSectionError")!, "Retry"));
        await Wait(() => Card("App versions") != null);
        Check(Card("App versions") != null, "Retry reads Devices again");

        // ── Bandwidth ────────────────────────────────────────
        await Open(Request(("tab", "bandwidth")));
        await Wait(() => Card("Bandwidth over time") != null && Card("Bandwidth by node") != null, 10000);
        Check(!HasFilter("allLibraries") && !HasFilter("allAccounts") && !HasFilter("timezone") && Find<Button>(insights, "AdminInsightsRange_30d") != null, "Bandwidth shows the range only");
        Check(new[] { "Total data sent", "Peak speed", "Daily average", "Remote viewing" }.All(label => Has(insights, label)), "Bandwidth shows its four KPI cards");
        Check(HasStarting(insights, "Bandwidth history starts on "), "Bandwidth says when its history starts when that is after the window");
        Check(HasContaining(insights, "API server ·") && HasContaining(insights, "stream nodes"), "with remote nodes the first card splits API and nodes");
        Check(Pressed(Named(Card("Bandwidth over time")!, "Data sent")!) && Pressed(Named(Card("Bandwidth over time")!, "Total")!) && Named(Card("Bandwidth over time")!, "By node") != null, "the chart starts on data sent in total and offers By node");
        Check(Has(Card("Bandwidth over time")!, "Downloads"), "the total view draws a Downloads line when there are downloads");
        await CaptureTab("bandwidth", 1440);
        await Click(() => Named(Card("Bandwidth over time")!, "By node"));
        await Wait(() => Has(Card("Bandwidth over time")!, "Edge (#2)"));
        Check(new[] { "API server", "Edge (#2)", "Edge (#3)", "Far", "Other" }.All(label => Has(Card("Bandwidth over time")!, label)), "By node labels the API, shares of a name by node id, and the rest as Other");
        await CaptureAsync(Card("Bandwidth over time")!, "insights-bandwidth-nodes-volume-1440.png");
        await Click(() => Named(Card("Bandwidth over time")!, "Peak speed"));
        await Wait(() => Pressed(Named(Card("Bandwidth over time")!, "Peak speed")!));
        Check(Has(Card("Bandwidth over time")!, "Edge (#3)") && Has(Card("Bandwidth over time")!, "Total"), "the peak view by node draws a line per node under a total");
        await CaptureAsync(Card("Bandwidth over time")!, "insights-bandwidth-nodes-peak-1440.png");
        await Click(() => Named(Card("Bandwidth over time")!, "Total"));
        await Wait(() => Pressed(Named(Card("Bandwidth over time")!, "Total")!));
        await CaptureAsync(Card("Bandwidth over time")!, "insights-bandwidth-total-peak-1440.png");
        var nodes = Card("Bandwidth by node")!;
        Check(new[] { "Node", "Data sent", "Share", "Peak speed" }.All(label => Has(nodes, label)) && Has(nodes, "API server") && Has(nodes, "Proxy") && Has(nodes, "Transcode") && Has(nodes, "A Node With A Remarkably Long Display Name"),
            "the node table lists the API and each node with its type");
        Check(Descendants(nodes).OfType<ScrollViewer>().Any(), "the node table scrolls inside its own area");

        data.NoRemoteNodes = true;
        await Open(Request(("tab", "bandwidth"), ("range", "7d")));
        await Wait(() => Card("Bandwidth over time") != null && HasContaining(insights, "active day"), 8000);
        Check(Card("Bandwidth by node") == null && Named(Card("Bandwidth over time")!, "By node") == null, "without remote nodes there is no By node view and no node table");
        Check(HasContaining(insights, "playback ·") && HasContaining(insights, "downloads"), "without remote nodes the first card splits playback from downloads");
        await CaptureTab("bandwidth-no-nodes", 1440);
        data.NoRemoteNodes = false;
        data.Empty = true;
        await Open(Request(("tab", "bandwidth"), ("range", "all")));
        await Wait(() => Card("Bandwidth by node") != null);
        Check(Has(insights, "No bandwidth data in this period") && Has(Card("Bandwidth by node")!, "No remote node traffic in this period"), "an empty Bandwidth window shows the chart's and the table's own empty lines");
        await CaptureTab("bandwidth-empty", 1440);
        data.Empty = false;
        data.Fail = true;
        await Open(Request(("tab", "bandwidth"), ("range", "7d")));
        await Wait(() => Find<Border>(insights, "AdminInsightsSectionError") != null);
        Check(Find<Border>(insights, "AdminInsightsSectionError") != null && Card("Bandwidth over time") == null, "a failed Bandwidth read shows the load error");
        await CaptureAsync(frame, "insights-bandwidth-error-1440.png");
        data.Fail = false;
        await Click(() => Named(Find<Border>(insights, "AdminInsightsSectionError")!, "Retry"));
        await Wait(() => Card("Bandwidth over time") != null);
        Check(Card("Bandwidth over time") != null, "Retry reads Bandwidth again");

        // The title panel's viewer names now open the person's panel, without the title.
        await Open(Request(("tab", "titles"), ("title", "m1")));
        await Wait(() => HasCI(Sheet(), "Who watched") && Named(Sheet(), "Kids") != null);
        var viewer = Named(Sheet(), "Kids");
        Check(viewer != null, "a title's viewers are links now that the person panel exists");
        if (viewer != null)
        {
            await Click(() => viewer);
            await Wait(() => shell.CurrentRequest.Get("person") != null && shell.CurrentRequest.Get("title") == null && HasCI(Sheet(), "Top titles"));
            Check(shell.CurrentRequest.Get("person") == "7" && shell.CurrentRequest.Get("profile") == "p1" && shell.CurrentRequest.Get("title") == null && HasCI(Sheet(), "Top titles"), "a viewer opens the person's panel and closes the title");
        }

        // ── Narrow ───────────────────────────────────────────
        await Resize(560);
        await Open(Request(("tab", "people")));
        await Wait(() => RowCount() > 0);
        await Task.Delay(300);
        await CaptureTab("people", 560);
        await Click(() => Find<Button>(insights, "AdminInsightsPeopleExpand_3"));
        await Wait(() => RowCount() == 53);
        await CaptureAsync(frame, "insights-people-expanded-560.png");
        await Open(Request(("tab", "devices")));
        await Wait(() => Card("App versions") != null && Descendants(insights).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "AdminInsightsDevicesHistory"));
        await Task.Delay(300);
        await CaptureTab("devices", 560);
        await Open(Request(("tab", "bandwidth")));
        await Wait(() => Card("Bandwidth over time") != null && Card("Bandwidth by node") != null);
        await Task.Delay(300);
        {
            var nodeTable = Descendants(Card("Bandwidth by node")!).OfType<Grid>().First(grid => grid.GetType().Name == "AdminInsightsTable");
            Check(!Descendants(nodeTable).OfType<TextBlock>().Any(label => label.IsTextTrimmed), "at 560 nothing in the node table is trimmed or wrapped short of its text (it scrolls inside its card instead)");
            Check(!Descendants(nodeTable).OfType<TextBlock>().Any(label => label.Text.EndsWith(" GB", StringComparison.Ordinal) && label.ActualHeight > 20), "a data figure stays on one line");
        }
        await CaptureTab("bandwidth", 560);
        await Click(() => Named(Card("Bandwidth over time")!, "By node"));
        await Wait(() => Has(Card("Bandwidth over time")!, "Edge (#2)"));
        await CaptureAsync(Card("Bandwidth over time")!, "insights-bandwidth-nodes-volume-560.png");
        await Open(Request(("tab", "library")));
        await Wait(() => Card("Added and not played yet") != null);
        await Task.Delay(300);
        var unplayed = Card("Added and not played yet")!;
        var longTitle = Descendants(unplayed).OfType<TextBlock>().FirstOrDefault(label => label.Text.StartsWith("A Documentary With A Remarkably Long Title", StringComparison.Ordinal));
        Check(longTitle != null && longTitle.TextWrapping == TextWrapping.Wrap && longTitle.ActualHeight > 20, "a long unplayed title wraps inside the card instead of being cut at its edge");
        if (longTitle != null)
        {
            var origin = longTitle.TransformToVisual(unplayed).TransformPoint(new Windows.Foundation.Point(0, 0));
            Check(origin.X + longTitle.ActualWidth <= unplayed.ActualWidth + 1, "the wrapped title ends inside the card");
        }
        await CaptureAsync(unplayed, "insights-library-unplayed-560.png");
        await Open(Request(("tab", "people"), ("person", "3")));
        await Wait(() => HasCI(Sheet(), "Top titles") && Has(Sheet(), "Completion rate") && Find<Button>(Sheet(), "AdminInsightsPersonProfile") != null);
        await CaptureSheet("insights-person-panel-all-560.png");
        await CaptureSheetEnd("insights-person-panel-all-560-end.png");
        await Open(Request(("tab", "people"), ("person", "3"), ("profile", "p1")));
        await Wait(() => Has(Sheet(), "Kids") && Has(Sheet(), "Completion rate"));
        await CaptureSheet("insights-person-panel-profile-560.png");
        await Open(Request(("tab", "people"), ("person", "3"), ("profile", "p1"), ("title", "s0")));
        await Wait(() => HasCI(Sheet(), "Who watched") && HasStarting(Sheet(), "Plays by "));
        await CaptureSheet("insights-person-title-560.png");
        await Resize(1440);

        Check(frame.Navigate(typeof(Page)), "Insights can be left through the outer frame after using the new tabs");
        await Wait(() => !insights.IsLoaded);
        data.ServeUsers = false;
        navigation.Frame = null;
        server.RestartRequired = true;
        server.SessionCount = 3;
    }
}
