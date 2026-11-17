using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

/// <summary>
/// Current-upstream Dashboard UI (Silo-Server 7115ee70): the Egress Total/Nodes
/// view persisted in the layout entry (#4062) through pick, Reload, a later
/// visit and Reset, and Now Playing's playback position (#3726), shared
/// episode line (#4144) and columns that follow the widget's own width at
/// 672px. Synthetic data; captures are evidence hooks, not live visual parity.
/// Runs inside the "admin-dashboard" dispatch after the original cases.
/// </summary>
internal static partial class AdminDashboardNativeFixture
{
    private const string LongUser = "averyveryverylongusername-for-truncation-checks-0123456789";
    private const string MultilineProfile = "A\nB\nC";
    private const string MultilineClient = "Synthetic Client\r\nSecond line";
    private const string MultilineNetwork = "synthetic-network-provider-with-a-very-long-name-0123456789-abcdefghij\u2028Second line";

    private static string CurrentSession(int i, Action<JsonObject> configure)
    {
        var row = JsonNode.Parse(Server.Session(i))!.AsObject();
        configure(row);
        return row.ToJsonString();
    }

    private static string[] CurrentUiSessions() =>
    [
        CurrentSession(0, row =>
        {
            row["media_type"] = "episode"; row["series_name"] = "Show"; row["season_number"] = 3; row["episode_number"] = 13;
            row["episode_name"] = "Synthetic Episode"; row["position_seconds"] = 2832.4; row["file_duration"] = 8980;
        }),
        CurrentSession(1, row => { row["media_type"] = "audiobook"; row["position_seconds"] = 600; row["file_duration"] = null; }),
        CurrentSession(2, row => { row["media_type"] = "episode"; row["username"] = LongUser; row["position_seconds"] = 65; row["file_duration"] = 0; }),
        // Enough tags to need a second line, which WebUI #4401 clips whole.
        CurrentSession(3, row =>
        {
            row["is_jellyfin_client"] = true; row["client_name"] = MultilineClient; row["client_version"] = "1.0";
            row["client_label"] = MultilineClient;
            row["routing_network_provider"] = MultilineNetwork;
            row["profile_name"] = MultilineProfile;
        }),
    ];

    private static string? StoredEgressView(string? document)
    {
        if (document == null) return null;
        using var json = JsonDocument.Parse(document);
        foreach (var entry in json.RootElement.GetProperty("entries").EnumerateArray())
            if (entry.GetProperty("id").GetString() == DashboardRegistry.Egress24h)
                return entry.TryGetProperty("view", out var view) ? view.GetString() : null;
        return null;
    }

    private static (Frame Frame, AdminDashboardPage Page) OpenDashboard(Window window)
    {
        var frame = new Frame();
        window.Content = frame;
        frame.Navigate(typeof(AdminShellPage), new AdminRouteRequest(AdminRoute.Dashboard));
        var inner = (Frame)typeof(AdminShellPage).GetField("_frame", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(frame.Content)!;
        var page = (AdminDashboardPage)inner.Content;
        page.SetHostActivity(visible: true, focused: true);
        return (frame, page);
    }

    private static FrameworkElement WidgetFrame(AdminDashboardPage page, string id)
        => Descendants(page).First(element => element.GetType().Name == "DashboardWidgetFrame"
            && (string)element.GetType().GetProperty("Id")!.GetValue(element)! == id);

    private static void Invoke(Button button)
        => ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)
            .GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();

    private static Button? EgressToggle(AdminDashboardPage page, bool nodes)
    {
        var name = DashboardText.T(nodes ? "web.admin.dashboard.widgets.egress.viewNodesLabel" : "web.admin.dashboard.widgets.egress.viewTotalLabel");
        return Descendants(WidgetFrame(page, DashboardRegistry.Egress24h)).OfType<Button>().FirstOrDefault(button => AutomationProperties.GetName(button) == name);
    }

    private static int LayoutPuts(Server server)
    {
        lock (server.Writes) return server.Writes.Count(w => w.StartsWith("PUT /api/v2/admin/dashboard/layout", StringComparison.Ordinal));
    }

    private static string? LastLayoutBody(Server server)
    {
        lock (server.Writes) return server.LayoutBodies.Count == 0 ? null : server.LayoutBodies[^1];
    }

    private static async Task CurrentUiAsync(Window window, Server server, DashboardMemoryLayoutCache cache)
    {
        server.PersistLayout = true;
        server.LayoutETag = "\"V1\"";
        server.StoredLayout = """{"version":1,"entries":[{"id":"now-playing","span":12,"rows":5},{"id":"egress-24h","span":6,"rows":3,"range":"day","view":"nodes"},{"id":"users","span":6,"rows":3}]}""";
        server.SessionRows = CurrentUiSessions();
        try
        {
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900));
            var (frame, page) = OpenDashboard(window);
            var scale = frame.XamlRoot?.RasterizationScale ?? 1;
            void ResizeLogical(int width, int height) => window.AppWindow.ResizeClient(
                new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
            ResizeLogical(1440, 900);
            page.ViewportWidthOverride = 1440;
            await Until(() => page.LayoutSync?.IsLoaded == true && page.Store?.HasDashboardData == true);
            await (page.Store?.RefreshDashboardAsync(manual: true) ?? Task.CompletedTask);
            await Until(() => page.Store?.Get(DashboardDataStore.Sessions).As<IReadOnlyList<DashboardSession>>()?.Count == 4);
            await Task.Delay(600);
            Check(page.PlacedWidgets().Select(w => w.Id).SequenceEqual(["now-playing", "egress-24h", "users"]), "current-ui adopts the stored server layout");

            // ---- Egress view (M1): stored Nodes, pick, Reload, revisit, Reset ----
            Check(page.WidgetView(DashboardRegistry.Egress24h) == "nodes", "Egress opens on the Nodes view the server layout stores");
            Check(EgressToggle(page, nodes: true) is { } nodesOn && AutomationProperties.GetItemStatus(nodesOn) == "pressed", "Egress Nodes option is pressed for a stored Nodes view");
            await CaptureAsync(WidgetFrame(page, DashboardRegistry.Egress24h), "current-ui-egress-nodes-1440.png");

            var putsBefore = LayoutPuts(server);
            if (EgressToggle(page, nodes: false) is { } total) Invoke(total);
            else Check(false, "Egress Total option is rendered when node bands exist");
            Check(page.WidgetView(DashboardRegistry.Egress24h) == "total", "Egress Total pick is echoed from the layout entry immediately");
            Check(EgressToggle(page, nodes: false) is { } totalOn && AutomationProperties.GetItemStatus(totalOn) == "pressed", "Egress Total option is pressed after the echo");
            // Picking the view already shown queues nothing.
            if (EgressToggle(page, nodes: false) is { } again) Invoke(again);
            await Task.Delay(1300);
            Check(LayoutPuts(server) == putsBefore + 1, $"one debounced PUT carries the Egress pick (puts {LayoutPuts(server) - putsBefore})");
            Check(StoredEgressView(LastLayoutBody(server)) == "total", "the saved layout stores view total on the Egress entry");
            await CaptureAsync(WidgetFrame(page, DashboardRegistry.Egress24h), "current-ui-egress-total-1440.png");

            // Another device stores Nodes; Reload draws it on the reused widget.
            server.StoredLayout = """{"version":1,"entries":[{"id":"now-playing","span":12,"rows":5},{"id":"egress-24h","span":6,"rows":3,"range":"day","view":"nodes"},{"id":"users","span":6,"rows":3}]}""";
            server.LayoutETag = "\"V9\"";
            await page.LayoutSync!.ReloadServerLayoutAsync();
            await Task.Delay(300);
            Check(page.WidgetView(DashboardRegistry.Egress24h) == "nodes", "Reload draws the server's stored Nodes view on the reused Egress widget");

            // A layout edit elsewhere keeps the stored view in the PUT.
            page.LayoutSync.MoveWidget("users", "now-playing");
            await Task.Delay(1300);
            Check(StoredEgressView(LastLayoutBody(server)) == "nodes", "a move saves the Egress entry with its stored view");

            // A later visit paints and adopts the persisted view.
            window.Content = null;
            await Task.Delay(400);
            (frame, page) = OpenDashboard(window);
            page.ViewportWidthOverride = 1440;
            await Until(() => page.LayoutSync?.IsLoaded == true && page.Store?.HasDashboardData == true);
            await Until(() => page.Store?.Get(DashboardDataStore.Sessions).As<IReadOnlyList<DashboardSession>>()?.Count == 4);
            await Task.Delay(600);
            Check(page.WidgetView(DashboardRegistry.Egress24h) == "nodes", "the Egress Nodes view survives leaving and reopening the dashboard");

            // ---- Now Playing (M4-M6) ----
            page.LayoutSync!.ResizeWidget("now-playing", 12, 5);
            await Task.Delay(500);
            var nowPlaying = WidgetFrame(page, DashboardRegistry.NowPlaying);
            var texts = Descendants(nowPlaying).OfType<TextBlock>().ToArray();
            Check(texts.Any(text => text.Text == "S3E13 · Show"), "episode tile reads S3E13 · Show like Activity");
            foreach (var index in new[] { 1, 2 })
            {
                var session = page.Store!.Get(DashboardDataStore.Sessions).As<IReadOnlyList<DashboardSession>>()![index];
                var expected = AdminActivityPresentation.GetPlaybackSessionSubtitle(session);
                var title = texts.Single(text => text.Text == DashboardPresentation.SessionTitle(session, "nowPlaying"));
                var info = (Grid)title.Parent;
                Check(expected == null && !info.Children.OfType<TextBlock>().Any(text => Grid.GetRow(text) == 1),
                    $"{session.MediaType} without episode metadata omits the subtitle like Activity");
            }
            Check(!texts.Any(text => text.Text.Contains(" · E13", StringComparison.Ordinal) || text.Text.Contains(" — Show", StringComparison.Ordinal)), "the old S3 · E13 — Show line is gone");
            TextBlock? Times(string position) => texts.FirstOrDefault(text => AutomationProperties.GetName(text).StartsWith(position + " · ", StringComparison.Ordinal));
            var episodeTimes = Times("47:12 / 2:29:40");
            Check(episodeTimes != null, "episode tile shows position / duration then elapsed");
            Check(Times("10:00") != null, "a null duration shows the position alone");
            Check(Times("1:05") != null, "a zero duration shows the position alone");
            Check(Times("10:00 / 2:00:00") != null, "a movie tile shows position / duration");
            foreach (var position in new[] { "47:12 / 2:29:40", "1:05" })
            {
                if (Times(position) is not { } times) continue;
                var userRow = (FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(times);
                var info = (FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(userRow);
                var bounds = times.TransformToVisual(info).TransformBounds(new Windows.Foundation.Rect(0, 0, times.ActualWidth, times.ActualHeight));
                // Auto columns can overflow without shrinking: compare with the available info area.
                Check(times.TextWrapping == TextWrapping.NoWrap && times.TextTrimming == TextTrimming.None
                    && bounds.Left >= -1 && bounds.Right <= info.ActualWidth + 1 && times.ActualHeight <= 16.5, $"{position} times stay whole on one line");
            }
            var longName = texts.FirstOrDefault(text => text.Text == LongUser);
            Check(longName != null && longName.ActualHeight <= 16.5, "a long username stays on one line");
            if (longName != null)
            {
                var natural = new TextBlock { Text = LongUser, FontSize = longName.FontSize, FontWeight = longName.FontWeight };
                natural.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                Program.Log($"TRACE dashboard long username arranged {longName.ActualWidth:0}px of natural {natural.DesiredSize.Width:0}px");
            }
            // WebUI #4401: tags are one line, so every card is the same height and rows stay even,
            // including the long-name card and the card whose tags would wrap.
            var cardHeights = Descendants(nowPlaying).OfType<Border>().Where(border => border.CornerRadius.TopLeft == 20 && border.Padding.Left == 14)
                .Select(border => Math.Round(border.ActualHeight)).Distinct().ToArray();
            Program.Log($"TRACE dashboard Now Playing card heights: {string.Join(",", cardHeights)}");
            Check(cardHeights.Length == 1, $"every Now Playing card has the same height ({string.Join(",", cardHeights)})");
            var tagRows = Descendants(nowPlaying).OfType<Panel>().Where(panel => panel.GetType().Name == "DashboardTagViewport").ToArray();
            Check(tagRows.Length == 4 && tagRows.All(row => row.ActualHeight <= 18.5 && row.Clip != null), "each card's tag row is clipped to one 18px line");
            AssertTagRows(tagRows, "default", 18);
            var badgeTexts = tagRows.SelectMany(row => Descendants(row).OfType<TextBlock>()).ToArray();
            Check(badgeTexts.All(text => text.MaxLines == 1), "all badge text, including explicit line breaks, is limited to one actual line");
            var profileText = badgeTexts.Single(text => text.Text == MultilineProfile);
            var profilePill = (Border)profileText.Parent;
            Check((string?)ToolTipService.GetToolTip(profilePill) == MultilineProfile, "profile tooltip retains the full multiline label");
            Check(badgeTexts.Any(text => text.Text.Contains(MultilineClient, StringComparison.Ordinal))
                && badgeTexts.Any(text => text.Text == MultilineNetwork), "multiline client and route labels reach the badge regression");

            // Exercise typography changes on already rendered cards. 1.25 is
            // AccessibilityService's x-large scale; 2 also covers larger text.
            // Keep system text scaling enabled and restore exact baselines.
            var tagText = tagRows.SelectMany(row => Descendants(row).OfType<TextBlock>())
                .Select(text => (Text: text, FontSize: text.FontSize, LineHeight: text.LineHeight)).ToArray();
            try
            {
                foreach (var textScale in new[] { 1.25, 2.0 })
                {
                    foreach (var baseline in tagText)
                    {
                        baseline.Text.FontSize = baseline.FontSize * textScale;
                        baseline.Text.LineHeight = baseline.LineHeight * textScale;
                    }
                    nowPlaying.UpdateLayout();
                    await Task.Delay(100);
                    AssertTagRows(tagRows, $"text scale {textScale}", 12 * textScale + 6);
                }
            }
            finally
            {
                foreach (var baseline in tagText)
                {
                    baseline.Text.FontSize = baseline.FontSize;
                    baseline.Text.LineHeight = baseline.LineHeight;
                }
                nowPlaying.UpdateLayout();
            }
            AssertTagRows(tagRows, "restored", 18);
            Check(tagRows.All(row => row.ActualHeight <= 18.5), "tag viewports shrink back after enlarged text is restored");

            // A later row may still have different typography. Its taller pill
            // must not determine the first-row viewport or peek through it.
            var profileRow = tagRows.Single(row => Descendants(row).Contains(profilePill));
            var profileLineHeight = profileText.LineHeight;
            try
            {
                profileText.LineHeight = 48;
                nowPlaying.UpdateLayout();
                Check(profilePill.DesiredSize.Height > 18.5 && profileRow.ActualHeight <= 18.5
                    && profilePill.TransformToVisual(profileRow).TransformPoint(new Windows.Foundation.Point(0, 0)).Y >= profileRow.ActualHeight,
                    "a taller wrapped profile badge stays wholly below the first-row clip");
            }
            finally
            {
                profileText.LineHeight = profileLineHeight;
                nowPlaying.UpdateLayout();
            }
            AssertTagRows(tagRows, "restored after taller wrapped badge", 18);

            await AssertNowPlayingColumnsAsync(page, "span12-1440", expectColumns: 2);
            await CaptureAsync(nowPlaying, "current-ui-now-playing-span12-1440.png");
            page.LayoutSync.ResizeWidget("now-playing", 6, 5);
            await AssertNowPlayingColumnsAsync(page, "span6-1440", expectColumns: 1);
            await CaptureAsync(WidgetFrame(page, DashboardRegistry.NowPlaying), "current-ui-now-playing-span6-1440.png");
            page.LayoutSync.ResizeWidget("now-playing", 12, 5);
            await AssertNowPlayingColumnsAsync(page, "span12-1440-again", expectColumns: 2);

            // Below lg the grid stacks; columns still follow the widget's own width, not the viewport.
            var sawStackedTwo = false;
            var sawStackedOne = false;
            foreach (var width in new[] { 1000, 800, 700, 1000 })
            {
                ResizeLogical(width, 900);
                page.ViewportWidthOverride = width;
                var columns = await AssertNowPlayingColumnsAsync(page, $"viewport{width}", expectColumns: null);
                sawStackedTwo |= columns == 2;
                sawStackedOne |= columns == 1;
                await CaptureAsync(WidgetFrame(page, DashboardRegistry.NowPlaying), $"current-ui-now-playing-{width}x900.png");
            }
            Check(sawStackedTwo, "a stacked viewport with a widget at least 672px wide pairs cards");
            ResizeLogical(1440, 900);
            page.ViewportWidthOverride = 1440;
            await Task.Delay(400);
            if (!sawStackedOne) Program.Log("NOTE: dashboard no stacked width in this host fell below 672px; one-column coverage comes from span 6 at 1440px.");

            // ---- Reset: the default arrangement draws Total and stores no view ----
            int deletesBefore;
            lock (server.Writes) deletesBefore = server.Writes.Count(w => w.StartsWith("DELETE /api/v2/admin/dashboard/layout", StringComparison.Ordinal));
            page.LayoutSync.ResetLayout();
            await Task.Delay(600);
            lock (server.Writes) Check(server.Writes.Count(w => w.StartsWith("DELETE /api/v2/admin/dashboard/layout", StringComparison.Ordinal)) == deletesBefore + 1, "current-ui reset deletes the server layout");
            Check(page.WidgetView(DashboardRegistry.Egress24h) == "total", "Reset draws the default Total view on the reused Egress widget");
            Check(page.LayoutSync.Entries.Single(entry => entry.Id == DashboardRegistry.Egress24h).View == null, "Reset stores no Egress view");
            page.LayoutSync.ResizeWidget("libraries", 12, null);
            await Task.Delay(1300);
            Check(LastLayoutBody(server) is { } afterReset && StoredEgressView(afterReset) == null && afterReset.Contains("\"egress-24h\"", StringComparison.Ordinal),
                "the first save after Reset carries the Egress entry without a view");
            await CaptureAsync(frame, "current-ui-after-reset-1440x900.png");
            await AssertColumnFocusAsync(page);
            window.Content = null;
            await Task.Delay(300);
        }
        finally
        {
            cache.Clear(DashboardLayoutSync.Scope("https://fixture.invalid", "1"));
            server.PersistLayout = false;
            server.StoredLayout = null;
            server.SessionRows = null;
            server.LayoutETag = "\"L1\"";
        }
    }

    private static void AssertTagRows(Panel[] rows, string label, double minimumHeight)
    {
        Check(rows.Length == 4, $"{label} renders all four tag viewports");
        var wrappedTag = false;
        foreach (var row in rows)
        {
            var pills = row.Children.OfType<Panel>().SelectMany(panel => panel.Children.OfType<FrameworkElement>()).ToArray();
            var pillHeight = pills.Select(pill => pill.DesiredSize.Height).DefaultIfEmpty(0).Max();
            Check(row.ActualHeight >= minimumHeight - 0.5 && Math.Abs(row.ActualHeight - pillHeight) <= 0.5
                && row.Clip is Microsoft.UI.Xaml.Media.RectangleGeometry clip && Math.Abs(clip.Rect.Height - row.ActualHeight) <= 0.5,
                $"{label} clips at one measured pill height ({row.ActualHeight:0.#}, pills {pillHeight:0.#})");
            foreach (var pill in pills)
            {
                var top = pill.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                if (top >= row.ActualHeight) wrappedTag = true;
                else Check(top + pill.ActualHeight <= row.ActualHeight + 0.5,
                    $"{label} first-line tag fits wholly ({top:0.#}+{pill.ActualHeight:0.#} <= {row.ActualHeight:0.#})");
            }
        }
        Check(wrappedTag, $"{label} wrapped tags fall wholly below the one-line clip");
    }

    private static async Task AssertColumnFocusAsync(AdminDashboardPage page)
    {
        var widget = WidgetFrame(page, DashboardRegistry.Playback24h);
        widget.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        await Task.Delay(300);
        var chart = Descendants(widget).Single(element => element.GetType().Name == "DashboardStackedColumnChart");
        var type = chart.GetType();
        var bars = (Grid)type.GetField("_bars", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!;
        var overlay = (Canvas)type.GetField("_overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart)!;
        var cells = bars.Children.OfType<ContentControl>().ToArray();
        Check(cells.Length >= 4, "playback chart has columns for keyboard inspection");
        if (cells.Length < 4) return;
        foreach (var index in new[] { 2, 3 })
        {
            var cell = cells[index];
            Check(cell.Focus(FocusState.Keyboard), $"column {index} accepts keyboard focus");
            await Task.Delay(50);
            chart.UpdateLayout();
            Check(cell.FocusState == FocusState.Keyboard && ReferenceEquals(bars.Children[index], cell),
                $"column {index} keeps the same focused cell after GotFocus");
            Check((int?)type.GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chart) == index
                && overlay.Children.OfType<Border>().Any(tip => tip.Visibility == Visibility.Visible && tip.ActualWidth > 0),
                $"column {index} keeps its tooltip visible after keyboard focus");
        }
    }

    /// <summary>
    /// Waits for Now Playing to settle, then checks the column rule against its
    /// measured width, that it stopped re-rendering (no resize feedback loop),
    /// and that the fit uses the full height with no "+N more" link (WebUI #4401).
    /// </summary>
    private static async Task<int> AssertNowPlayingColumnsAsync(AdminDashboardPage page, string label, int? expectColumns)
    {
        await Task.Delay(500);
        var layout = page.NowPlayingLayout();
        if (layout is not { } current) { Check(false, $"{label} Now Playing is placed"); return 0; }
        Check(current.Columns == DashboardGridPlacement.NowPlayingColumns(current.ContentWidth),
            $"{label} Now Playing columns {current.Columns} follow its {current.ContentWidth:0}px content width");
        if (expectColumns is { } expected) Check(current.Columns == expected, $"{label} Now Playing uses {expected} column(s) (width {current.ContentWidth:0})");
        var renders = page.WidgetRenderCount(DashboardRegistry.NowPlaying);
        await Task.Delay(600);
        Check(page.WidgetRenderCount(DashboardRegistry.NowPlaying) == renders && page.NowPlayingLayout()?.Columns == current.Columns,
            $"{label} Now Playing settles without re-rendering");

        var widget = Descendants(WidgetFrame(page, DashboardRegistry.NowPlaying)).First(element => element.GetType().Name == "NowPlayingWidget");
        var type = widget.GetType();
        var cards = (Grid?)type.GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget);
        var viewport = (Grid?)type.GetField("_viewport", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(widget);
        var sessions = (int)type.GetProperty("SessionCount")!.GetValue(widget)!;
        if (cards == null || viewport == null) { Check(false, $"{label} Now Playing rendered cards"); return current.Columns; }
        Check(cards.ColumnDefinitions.Count == current.Columns, $"{label} card grid has {current.Columns} column(s)");
        var all = cards.Children.OfType<FrameworkElement>().ToArray();
        var shown = all.Count(card => card.Opacity > 0);
        var moreTexts = Enumerable.Range(1, Math.Max(1, sessions))
            .Select(n => DashboardText.T("web.admin.dashboard.widgets.nowPlaying.moreStreams", ("count", (long)n), ("count_text", (long)n))).ToHashSet();
        Check(viewport.Children.Count == 1 && ReferenceEquals(viewport.Children[0], cards)
              && !Descendants(widget).OfType<TextBlock>().Any(text => moreTexts.Contains(text.Text)),
            $"{label} has no +N more link; the header's View all counts all {sessions}");
        double Bottom(FrameworkElement card) => card.TransformToVisual(viewport).TransformPoint(new Windows.Foundation.Point(0, card.ActualHeight)).Y;
        if (page.GridMode == DashboardGridMode.Twelve && shown < all.Length)
            Check(shown >= 1 && (shown == 1 || Bottom(all[shown - 1]) <= viewport.ActualHeight + 0.5) && Bottom(all[shown]) > viewport.ActualHeight,
                $"{label} shows every whole card the full {viewport.ActualHeight:0}px height fits ({shown}/{all.Length})");
        Program.Log($"TRACE dashboard {label}: columns={current.Columns}, width={current.ContentWidth:0}, shown={shown}/{sessions}, mode={page.GridMode}");
        return current.Columns;
    }
}
