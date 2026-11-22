using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

/// <summary>
/// What the Libraries / Scan Activity / Catalog Health tabs look like and how they behave under the keyboard and live updates, measured on the laid-out page
/// (official 93794454b): no element on any tab is drawn with oval corners; the table is as wide as its card and clear of the page scrollbar at 1440, 1280,
/// 1024 and 800, with nothing trimmed that the official page wraps and the warning sentence wrapping; the Library cell starts under its header at 1920; the
/// Catalog Health tiles share a row height; a failed load shows no "No libraries configured yet."; the summary strip and the Scan Activity panel keep
/// their controls (and keyboard focus) while a scan's progress moves; the tab strip and the tiles are one tab stop each and report their selection through
/// UI Automation. Each check fails on the code it replaced (see the Libraries tabs parity record). Dispatch: SILO_NATIVE_TEST_ONLY=admin-libraries.
/// </summary>
internal static partial class AdminLibrariesNativeFixture
{
    private static Windows.Foundation.Rect BoundsIn(FrameworkElement element, UIElement relativeTo) =>
        element.TransformToVisual(relativeTo).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static T? AncestorOf<T>(DependencyObject element) where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(element); current != null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }

    private static bool? SelectedInAutomation(FrameworkElement? element) =>
        element == null ? null : FrameworkElementAutomationPeer.CreatePeerForElement(element)?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider provider ? provider.IsSelected : null;

    /// <summary>Opens a tab by its button, as a click does, and waits until its panel is drawn.</summary>
    private static async Task GoToTabAsync(AdminShellPage shell, AdminLibrariesPage page, AdminLibrariesTab tab, string shown)
    {
        if (page.SelectedTab != tab) await InvokeAsync(Find<Button>(shell, "LibrariesTab_" + tab));
        await Until(() => page.SelectedTab == tab && Shown(Find<FrameworkElement>(shell, shown)));
        await Task.Delay(250);
    }

    private static string ScanEvent(string id, string library, string mode, string status, string? path = null, int? processed = null, int? total = null) =>
        "{\"id\":\"" + id + "\",\"library_id\":" + library + ",\"mode\":\"" + mode + "\",\"status\":\"" + status + "\",\"trigger\":\"manual\",\"started_at\":\"2026-11-15T09:00:00Z\""
        + (path == null ? "" : ",\"path\":\"" + path + "\"")
        + (total == null ? "" : ",\"result\":{\"total_files\":" + total + ",\"files_processed\":" + processed + ",\"message\":\"Scanning files\"}") + "}";

    private static async Task<AdminLibrariesPage> RunTabsLayoutAsync(Window window, Frame outer, AdminShellPage shell, Frame inner, AdminLibrariesPage page, Server server, Realtime realtime)
    {
        var scroll = (ScrollViewer)Field(page, "_scroll")!;
        Resize(window, 1440, 900);
        await Until(() => page.Controller?.State.Loaded == true);

        // The tabs are shown on a populated page: library 1 scanning with a progress bar, library 2 with a running scan and a queue, and a metadata refresh.
        realtime.Event("scans", "scan.progress", ScanEvent("s1", "1", "library", "running", processed: 100, total: 400));
        realtime.Event("scans", "scan.started", ScanEvent("s2", "2", "subtree", "running", "/media/shows/A"));
        for (var index = 3; index <= 7; index++) realtime.Event("scans", "scan.queued", ScanEvent("s" + index, "2", "file", "accepted", "/media/shows/B/e" + index + ".mkv"));
        await Until(() => page.Controller!.State.Scans.Count >= 7 && Shown(Find<FrameworkElement>(shell, "LibrariesTable")));

        // ── Per width: every tab ──
        foreach (var width in new[] { 1440, 1280, 1024, 800, 560 })
        {
            var tag = $"[{width}]";
            var wide = width >= 1024;
            Resize(window, width, 900);
            await GoToTabAsync(shell, page, AdminLibrariesTab.Libraries, wide ? "LibrariesTable" : "LibrariesCards");
            await Until(() => Shown(Find<FrameworkElement>(shell, wide ? "LibrariesTable" : "LibrariesCards")) && Find<FrameworkElement>(shell, wide ? "LibrariesCards" : "LibrariesTable") == null);
            scroll.ChangeView(null, 0, null, true);
            await Task.Delay(300);
            var viewport = scroll.ActualWidth;

            // Libraries: the summary strip and the list stay clear of the page scrollbar (the scroller spans the gutters, as the official document scroll does).
            var strip = Find<FrameworkElement>(shell, "LibrariesSummary");
            Check(strip != null && Shown(strip) && BoundsIn(strip, scroll).Right <= viewport - 12, $"{tag} the summary strip ends clear of the page scrollbar (right edge {(strip == null ? 0 : BoundsIn(strip, scroll).Right):0.#} of {viewport:0.#})");
            if (wide)
            {
                var grid = Find<Grid>(shell, "LibrariesTable")!;
                var scroller = AncestorOf<ScrollViewer>(grid)!;
                var card = AncestorOf<Border>(scroller)!;
                var cardBounds = BoundsIn(card, scroll);
                Check(cardBounds.Right <= viewport - 12, $"{tag} the table's card ends clear of the page scrollbar (right edge {cardBounds.Right:0.#} of {viewport:0.#})");
                var gridRight = BoundsIn(grid, card).Right;
                Check(gridRight <= card.ActualWidth + 0.5, $"{tag} the table's right edge is inside its card ({gridRight:0.#} of {card.ActualWidth:0.#})");
                Check(scroller.ScrollableWidth < 1, $"{tag} the table fits its card without horizontal scrolling (scrolls {scroller.ScrollableWidth:0.#})");
                var actions = Descendants(grid).OfType<TextBlock>().First(text => text.Text == AdminLibrariesText.T("web.admin.libraries.column.actions"));
                Check(BoundsIn(actions, card).Right <= card.ActualWidth - 8 && actions.ActualWidth + 0.5 >= actions.DesiredSize.Width, $"{tag} the Actions header is whole and inside the card");
                foreach (var library in new[] { "1", "2", "3", "4" })
                {
                    var menu = Find<Button>(shell, "LibrariesMenu_" + library)!;
                    var menuRight = BoundsIn(menu, card).Right;
                    Check(menuRight <= card.ActualWidth - 8, $"{tag} row {library}'s \"…\" button is inside the card with its cell padding ({menuRight:0.#} of {card.ActualWidth:0.#})");
                }
                // Nothing in the table is cut off by its cell or its card; only names and paths (which the official page truncates) may be trimmed.
                var clipped = new List<string>();
                foreach (var text in Descendants(grid).OfType<TextBlock>().Where(text => text.ActualWidth > 0 && text.Text.Length > 0))
                {
                    var cell = AncestorOf<Border>(text);
                    var rightInCard = BoundsIn(text, card).Right;
                    if (rightInCard > card.ActualWidth + 0.5) clipped.Add($"\"{text.Text}\" ends at {rightInCard:0.#}");
                    else if (cell != null && ReferenceEquals(VisualTreeHelper.GetParent(cell), grid) && BoundsIn(text, cell).Right > cell.ActualWidth + 0.5) clipped.Add($"\"{text.Text}\" overflows its cell");
                    var byDesign = AutomationProperties.GetAutomationId(text).StartsWith("LibrariesName_", StringComparison.Ordinal) || AncestorOf<Button>(text) is { } owner && AutomationProperties.GetAutomationId(owner).StartsWith("LibrariesFolders_", StringComparison.Ordinal);
                    if (text.TextTrimming != TextTrimming.None && text.IsTextTrimmed && !byDesign) clipped.Add($"\"{text.Text}\" is trimmed");
                    if (text.TextWrapping == TextWrapping.NoWrap && text.TextTrimming == TextTrimming.None && !byDesign)
                    {
                        // A line that cannot wrap is as wide as its text: a badge or cell narrower than that cuts the last letters off.
                        var natural = new TextBlock { Text = text.Text, FontSize = text.FontSize, FontWeight = text.FontWeight, FontFamily = text.FontFamily, CharacterSpacing = text.CharacterSpacing, TextWrapping = TextWrapping.NoWrap };
                        natural.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                        if (text.ActualWidth + 1 < natural.DesiredSize.Width) clipped.Add($"\"{text.Text}\" is drawn {text.ActualWidth:0.#} wide but needs {natural.DesiredSize.Width:0.#}");
                    }
                }
                Check(clipped.Count == 0, $"{tag} no text in the table is cut off or trimmed where the official page wraps: {string.Join("; ", clipped)}");
                // The scan warning under a row wraps at the table's width instead of making the table as wide as one line.
                var warning = Find<Border>(shell, "LibrariesWarning_3")!;
                var sentence = Descendants(warning).OfType<TextBlock>().First(text => text.Text == AdminLibraryPresentation.ScanWarningText(page.Controller!.State.Libraries.First(entry => entry.Id == "3")).Title);
                Check(warning.ActualWidth <= grid.ActualWidth + 0.5 && BoundsIn(warning, card).Right <= card.ActualWidth + 0.5, $"{tag} the warning row is as wide as the table, not wider");
                if (width <= 1280) Check(sentence.ActualHeight > sentence.LineHeight * 1.5, $"{tag} the warning sentence wraps onto more than one line (height {sentence.ActualHeight:0.#} for a {sentence.LineHeight:0.#} px line)");
                else Check(sentence.ActualHeight >= sentence.LineHeight - 0.5, $"{tag} the warning sentence is drawn");
            }
            else
            {
                foreach (var library in new[] { "1", "2", "3", "4" })
                {
                    var card = Find<FrameworkElement>(shell, "LibrariesCard_" + library)!;
                    Check(BoundsIn(card, scroll).Right <= viewport - 12 && BoundsIn(card, scroll).Left >= 0, $"{tag} library {library}'s card is inside the page, clear of the scrollbar");
                }
                // The card's Stop is a filled square (fill-current), like the row's.
                var stop = Find<Button>(shell, "LibrariesCardScan_1")!;
                Check(Descendants(stop).OfType<Microsoft.UI.Xaml.Shapes.Path>().Any(path => path.Fill is SolidColorBrush), $"{tag} the card's Stop icon is a filled square");
            }
            Check(CornerShapeChecks.OvalCorners(page) is { Count: 0 }, $"{tag} Libraries tab: no element is drawn with oval corners: {string.Join("; ", CornerShapeChecks.OvalCorners(page))}");

            // The tab strip: touch height below 1024, inside the page, and the three tabs fully drawn or reachable.
            var tabs = new[] { "Libraries", "Activity", "Health" }.Select(name => Find<Button>(shell, "LibrariesTab_" + name)!).ToArray();
            Check(tabs.All(tab => Math.Abs(tab.ActualHeight - (wide ? 40 : 44)) <= 1), $"{tag} the tabs are {(wide ? 40 : 44)} px tall ({string.Join(", ", tabs.Select(tab => tab.ActualHeight.ToString("0.#")))})");
            var tabStrip = (FrameworkElement)Field(page, "_tabStrip")!;
            var body = (FrameworkElement)Field(page, "_body")!;
            Check(Math.Abs(tabStrip.ActualWidth - body.ActualWidth) <= 1 && BoundsIn(tabStrip, scroll).Right <= viewport - 12, $"{tag} the tab strip is as wide as the page content ({tabStrip.ActualWidth:0.#} of {body.ActualWidth:0.#}) and clear of the scrollbar");

            // Scan Activity: populated.
            await GoToTabAsync(shell, page, AdminLibrariesTab.Activity, "LibrariesActivity");
            await Until(() => Find<FrameworkElement>(shell, "LibrariesActivityCard_2") is { IsLoaded: true, ActualWidth: > 0 } && Find<Button>(shell, "LibrariesFilter_all") is { ActualWidth: > 0 });
            scroll.ChangeView(null, 0, null, true);
            await Task.Delay(200);
            foreach (var id in new[] { "LibrariesActivityCard_1", "LibrariesActivityCard_2", "LibrariesRefresh_2" })
                Check(Find<FrameworkElement>(shell, id) is { } shown && BoundsIn(shown, scroll).Right <= viewport - 12, $"{tag} {id} ends clear of the page scrollbar");
            Check(CornerShapeChecks.OvalCorners(page) is { Count: 0 }, $"{tag} Scan Activity tab: no element is drawn with oval corners: {string.Join("; ", CornerShapeChecks.OvalCorners(page))}");
            var chip = Find<Button>(shell, "LibrariesFilter_2")!;
            var chipCount = Descendants(chip).OfType<Border>().First(border => border.Child is TextBlock);
            var chipLabel = Descendants(chip).OfType<TextBlock>().First(text => text.Text == "Shows");
            var countCentre = BoundsIn(chipCount, chip).Top + chipCount.ActualHeight / 2; var labelCentre = BoundsIn(chipLabel, chip).Top + chipLabel.ActualHeight / 2;
            Check(Math.Abs(countCentre - labelCentre) <= 1, $"{tag} the filter chip's count is centred against its label (centres {countCentre:0.#} and {labelCentre:0.#})");

            // Catalog Health: tiles and the open section.
            await GoToTabAsync(shell, page, AdminLibrariesTab.Health, "LibrariesTile_Stale");
            scroll.ChangeView(null, 0, null, true);
            await Task.Delay(200);
            Check(CornerShapeChecks.OvalCorners(page) is { Count: 0 }, $"{tag} Catalog Health tab: no element is drawn with oval corners: {string.Join("; ", CornerShapeChecks.OvalCorners(page))}");
            var tiles = new[] { "Unmatched", "Matcher", "Ambiguous", "Skipped", "Stale" }.Select(name => Find<Button>(shell, "LibrariesTile_" + name)!).ToArray();
            Check(tiles.All(tile => BoundsIn(tile, scroll).Right <= viewport - 12), $"{tag} the tiles end clear of the page scrollbar");
            if (width is 1440 or 1024 or 800)
            {
                // Force one label onto several lines: a tile whose label wraps does not make its row uneven (grid items stretch to the row's height, as official).
                var matcherLabel = Descendants(tiles[1]).OfType<TextBlock>().First(text => text.Text == AdminLibrariesText.T("web.admin.common.libraryEditor.matcher.title"));
                var original = matcherLabel.Text;
                matcherLabel.Text = original + " with a much longer name that has to wrap onto several lines";
                page.UpdateLayout();
                var rows = tiles.GroupBy(tile => Math.Round(BoundsIn(tile, scroll).Top)).ToArray();
                var uneven = rows.Where(row => row.Max(tile => tile.ActualHeight) - row.Min(tile => tile.ActualHeight) > 0.5).Select(row => string.Join("/", row.Select(tile => tile.ActualHeight.ToString("0.#")))).ToArray();
                Check(uneven.Length == 0 && matcherLabel.ActualHeight > matcherLabel.LineHeight * 1.5, $"{tag} Catalog Health tiles of a row share one height when a label wraps ({string.Join(" | ", uneven)})");
                matcherLabel.Text = original;
                page.UpdateLayout();
            }
            else
            {
                var rows = tiles.GroupBy(tile => Math.Round(BoundsIn(tile, scroll).Top)).ToArray();
                Check(rows.All(row => row.Max(tile => tile.ActualHeight) - row.Min(tile => tile.ActualHeight) <= 0.5), $"{tag} Catalog Health tiles of a row share one height");
            }
            if (width is 1440 or 1024 or 800 or 560) await CaptureAsync(outer, $"libraries-layout-{width}-health.png");
        }

        // ── 1920: the Library cell starts under its header (a Stretch cell with a MaxWidth is centred in a wide column) ──
        Resize(window, 1920, 900);
        await GoToTabAsync(shell, page, AdminLibrariesTab.Libraries, "LibrariesTable");
        await Task.Delay(300);
        {
            var grid = Find<Grid>(shell, "LibrariesTable")!;
            var header = Descendants(grid).OfType<TextBlock>().First(text => text.Text == AdminLibrariesText.T("web.admin.libraries.column.library"));
            var name = Find<TextBlock>(shell, "LibrariesName_1")!;
            var tile = Descendants(AncestorOf<Grid>(name)!).OfType<Border>().First(border => border.Width == 32);
            var headerLeft = BoundsIn(header, grid).Left; var tileLeft = BoundsIn(tile, grid).Left;
            Check(Math.Abs(tileLeft - headerLeft) <= 1, $"[1920] the Library cell's icon starts under its header (icon at {tileLeft:0.#}, header at {headerLeft:0.#})");
            var card = AncestorOf<Border>(AncestorOf<ScrollViewer>(grid)!)!;
            Check(grid.ActualWidth <= card.ActualWidth + 0.5 && AncestorOf<ScrollViewer>(grid)!.ScrollableWidth < 1, "[1920] the table is as wide as its card");
            ((ScrollViewer)Field(page, "_scroll")!).ChangeView(null, 0, null, true);
            await Task.Delay(200);
            await CaptureAsync(outer, "libraries-layout-1920-list.png");
        }

        // ── A failed load shows the error and Retry, and no "No libraries configured yet." ──
        Resize(window, 1440, 900);
        server.LibrariesReadFails = true;
        var failed = await OpenFreshAsync(shell, inner, page, null);
        await Until(() => Find<Button>(shell, "LibrariesRetry") is { IsLoaded: true });
        await Task.Delay(300);
        Check(Shown(Find<FrameworkElement>(shell, "LibrariesLoadError")) && failed.Controller!.State.Libraries.Count == 0, "a failed load shows its error with the list empty");
        Check(Find<FrameworkElement>(shell, "LibrariesEmpty") == null && !Texts(shell).Contains(AdminLibrariesText.T("web.admin.libraries.list.empty")), "a failed load does not also say \"No libraries configured yet.\"");
        server.LibrariesReadFails = false;
        await InvokeAsync(Find<Button>(shell, "LibrariesRetry"));
        await Until(() => failed.Controller!.State.Error == null && failed.Controller.State.Libraries.Count > 0 && Shown(Find<FrameworkElement>(shell, "LibrariesTable")));
        page = failed;

        // ── Live updates keep the controls under the keyboard ──
        await RunLiveUpdateChecksAsync(shell, page, realtime, server);

        // ── Keyboard and assistive technology: one tab stop, a real selected state ──
        await RunSelectionChecksAsync(shell, page);

        // ── Right-to-left: paths and names keep their own direction inside the sentences built around them ──
        await RunBidiChecksAsync(shell, page);

        // The state later steps expect: library 1's scan only, and its progress as the seed had it.
        for (var index = 2; index <= 7; index++) realtime.Event("scans", "scan.cancelled", ScanEvent("s" + index, "2", index == 2 ? "subtree" : "file", "cancelled"));
        await Until(() => page.Controller!.State.Scans.Count == 1);
        await GoToTabAsync(shell, page, AdminLibrariesTab.Libraries, "LibrariesTable");
        return page;
    }

    private static async Task RunLiveUpdateChecksAsync(AdminShellPage shell, AdminLibrariesPage page, Realtime realtime, Server server)
    {
        var root = shell.XamlRoot;
        // Scan Activity: a Stop button holding keyboard focus is the same control, still focused, after the scan's progress moves; the bar moves with it.
        await GoToTabAsync(shell, page, AdminLibrariesTab.Activity, "LibrariesActivity");
        // A first update settles the scan's own fields (the page seeded from the activity store lacks its trigger); only after that does a progress update change nothing that is drawn.
        realtime.Event("scans", "scan.progress", ScanEvent("s1", "1", "library", "running", processed: 200, total: 400));
        await Task.Delay(600);
        await Until(() => Find<ProgressBar>(shell, "LibrariesScanProgress_s1") != null && Find<Button>(shell, "LibrariesStopScans_1") is { IsLoaded: true, ActualWidth: > 0 });
        var stop = Find<Button>(shell, "LibrariesStopScans_1")!;
        var bar = Find<ProgressBar>(shell, "LibrariesScanProgress_s1")!;
        stop.Focus(FocusState.Keyboard);
        await Task.Delay(150);
        Check(stop.FocusState == FocusState.Keyboard, "the Stop button holds keyboard focus before the live update");
        var beforeValue = bar.Value; var keyBefore = (string?)Field(page, "_renderedActivityKey");
        realtime.Event("scans", "scan.progress", ScanEvent("s1", "1", "library", "running", processed: 300, total: 400));
        await Task.Delay(500);
        var stopAfter = Find<Button>(shell, "LibrariesStopScans_1")!;
        var barAfter = Find<ProgressBar>(shell, "LibrariesScanProgress_s1")!;
        var running = page.Controller!.State.Scans.First(scan => scan.Id == "s1");
        var keyAfter = (string?)Field(page, "_renderedActivityKey");
        var keyNote = string.Join(" / ", (keyBefore ?? "").Split('\n')) + " => " + string.Join(" / ", (keyAfter ?? "").Split('\n'));
        Check(ReferenceEquals(stop, stopAfter) && ReferenceEquals(bar, barAfter), $"a scan's progress update does not rebuild Scan Activity (the same Stop button and bar are still there; stop {ReferenceEquals(stop, stopAfter)}, bar {ReferenceEquals(bar, barAfter)}, key kept {keyBefore == keyAfter}: {keyNote})");
        Check(ReferenceEquals(FocusManager.GetFocusedElement(root), stopAfter) && stopAfter.FocusState == FocusState.Keyboard, "the Stop button keeps keyboard focus (and its focus ring) through the update");
        Check(barAfter.Value != beforeValue && barAfter.Value == AdminLibraryTabs.ScanProgressPercent(running), $"the bar moved in place with the progress ({beforeValue:0.#} to {barAfter.Value:0.#})");
        // A structural change (a library's last scan ends) does rebuild it, and focus on a control that remains stays where it was.
        var filterAll = Find<Button>(shell, "LibrariesFilter_all")!;
        filterAll.Focus(FocusState.Keyboard);
        await Task.Delay(150);
        realtime.Event("scans", "scan.cancelled", ScanEvent("s7", "2", "file", "cancelled"));
        await Until(() => Find<FrameworkElement>(shell, "LibrariesQueueScan_s7") == null && page.Controller!.State.Scans.All(scan => scan.Id != "s7"));
        await Task.Delay(500);
        var refocused = FocusManager.GetFocusedElement(root) as FrameworkElement;
        var chipNow = Find<Button>(shell, "LibrariesFilter_all");
        Check(refocused != null && AutomationProperties.GetAutomationId(refocused) == "LibrariesFilter_all" && refocused.FocusState == FocusState.Keyboard && !ReferenceEquals(refocused, filterAll),
            $"after a rebuild the focused filter chip is focused again with its keyboard state, not as a programmatic focus (focus is on {(refocused == null ? "nothing" : AutomationProperties.GetAutomationId(refocused) + " as " + refocused.FocusState)}; chip was replaced: {!ReferenceEquals(chipNow, filterAll)})");
        realtime.Event("scans", "scan.queued", ScanEvent("s7", "2", "file", "accepted", "/media/shows/B/e7.mkv"));
        await Until(() => page.Controller!.State.Scans.Any(scan => scan.Id == "s7"));

        // Libraries: View queue keeps focus through a progress update.
        await GoToTabAsync(shell, page, AdminLibrariesTab.Libraries, "LibrariesTable");
        await Until(() => Find<Button>(shell, "LibrariesViewQueue") is { IsLoaded: true, ActualWidth: > 0 });
        var view = Find<Button>(shell, "LibrariesViewQueue")!;
        view.Focus(FocusState.Keyboard);
        await Task.Delay(150);
        realtime.Event("scans", "scan.progress", ScanEvent("s1", "1", "library", "running", processed: 350, total: 400));
        await Task.Delay(500);
        var viewAfter = Find<Button>(shell, "LibrariesViewQueue")!;
        Check(ReferenceEquals(view, viewAfter) && ReferenceEquals(FocusManager.GetFocusedElement(root), viewAfter) && viewAfter.FocusState == FocusState.Keyboard,
            "View queue is the same control and keeps keyboard focus when a scan's progress moves");

        // A Stop pressed just after the last scan ended does not start a scan: it does what it said when it was drawn (the button that was on screen
        // when the scan ended is the one the admin's click lands on until the page re-draws).
        var stopRow = Find<Button>(shell, "LibrariesScan_1")!;
        Check(Name(stopRow) == AdminLibrariesText.T("web.admin.libraries.list.stopScans"), "library 1's row offers Stop while its scan runs");
        var scansBefore = server.Requests.Count(line => line == "POST /api/v2/scan");
        var cancelsBefore = server.Requests.Count(line => line == "POST /api/v2/scan/cancel");
        realtime.Event("scans", "scan.cancelled", ScanEvent("s1", "1", "library", "cancelled"));
        await Until(() => page.Controller!.State.Scans.All(scan => scan.LibraryId != "1"));
        ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(stopRow)).Invoke();
        await Task.Delay(400);
        Check(server.Requests.Count(line => line == "POST /api/v2/scan") == scansBefore && server.Requests.Count(line => line == "POST /api/v2/scan/cancel") == cancelsBefore + 1,
            "a Stop pressed after the scan ended asks to cancel and does not start a scan");
        realtime.Event("scans", "scan.started", ScanEvent("s1", "1", "library", "running", processed: 100, total: 400));
        await Until(() => page.Controller!.State.Scans.Any(scan => scan.Id == "s1") && Find<Button>(shell, "LibrariesScan_1") is { IsLoaded: true } b && Name(b) == AdminLibrariesText.T("web.admin.libraries.list.stopScans"));

        // The scan pill carries the official tooltip.
        var pill = Find<Button>(shell, "LibrariesScanPill_1")!;
        var tipText = ToolTipService.GetToolTip(pill) switch { string text => text, ToolTip { Content: string text } => text, _ => null };
        Check(tipText == AdminLibrariesText.T("web.admin.libraries.list.viewScansFor", ("name", page.Controller!.State.Libraries.First(library => library.Id == "1").Name)), $"the row's scan pill has the official \"View scans for …\" tooltip (is {tipText})");
    }


    private static async Task RunSelectionChecksAsync(AdminShellPage shell, AdminLibrariesPage page)
    {
        var names = new[] { "Libraries", "Activity", "Health" };
        var row = (StackPanel)Field(page, "_tabRow")!;
        var grid = (Grid)Field(page, "_healthTiles")!;
        Check(row.TabFocusNavigation == KeyboardNavigationMode.Once && grid.TabFocusNavigation == KeyboardNavigationMode.Once, "the tab strip and the tile grid are one tab stop each (TabFocusNavigation Once)");
        await GoToTabAsync(shell, page, AdminLibrariesTab.Libraries, "LibrariesTable");
        var tabs = names.Select(name => Find<Button>(shell, "LibrariesTab_" + name)!).ToArray();
        Check(tabs.Select((tab, index) => tab.IsTabStop == (index == 0)).All(ok => ok), "only the selected tab is a tab stop");
        Check(tabs.Select((tab, index) => SelectedInAutomation(tab) == (index == 0)).All(ok => ok), "UI Automation reports the selected tab through the SelectionItem pattern, and only that one");
        Check(tabs.All(tab => FrameworkElementAutomationPeer.CreatePeerForElement(tab).GetAutomationControlType() == AutomationControlType.TabItem), "the tabs are reported as tab items");
        // Tab leaves the strip instead of stepping through the other tabs.
        tabs[0].Focus(FocusState.Keyboard);
        await Task.Delay(100);
        if (TrySendKey(0x09))
        {
            await Task.Delay(200);
            var focused = FocusManager.GetFocusedElement(shell.XamlRoot) as FrameworkElement;
            Check(focused != null && !AutomationProperties.GetAutomationId(focused).StartsWith("LibrariesTab_", StringComparison.Ordinal), $"Tab from the selected tab leaves the strip (focus is on {(focused == null ? "nothing" : AutomationProperties.GetAutomationId(focused))})");
        }
        else Check(false, "keyboard input reaches the focused tab");
        // Automation can select a tab: Select() on the Scan Activity tab opens it, and selection follows.
        var activityPattern = (ISelectionItemProvider)FrameworkElementAutomationPeer.CreatePeerForElement(tabs[1]).GetPattern(PatternInterface.SelectionItem);
        activityPattern.Select();
        await Until(() => page.SelectedTab == AdminLibrariesTab.Activity);
        Check(SelectedInAutomation(tabs[1]) == true && SelectedInAutomation(tabs[0]) == false && tabs[1].IsTabStop && !tabs[0].IsTabStop, "selecting a tab through automation moves the selected state and the tab stop to it");

        await GoToTabAsync(shell, page, AdminLibrariesTab.Health, "LibrariesTile_Stale");
        var tiles = new[] { "Unmatched", "Matcher", "Ambiguous", "Skipped", "Stale" }.Select(name => Find<Button>(shell, "LibrariesTile_" + name)!).ToArray();
        Check(tiles.Select((tile, index) => tile.IsTabStop == (index == 0)).All(ok => ok) && tiles.Select((tile, index) => SelectedInAutomation(tile) == (index == 0)).All(ok => ok),
            "only the selected Catalog Health tile is a tab stop and the only one reported as selected");
        var stalePattern = (ISelectionItemProvider)FrameworkElementAutomationPeer.CreatePeerForElement(tiles[4]).GetPattern(PatternInterface.SelectionItem);
        stalePattern.Select();
        await Until(() => page.SelectedHealthSection == AdminHealthSection.Stale);
        Check(SelectedInAutomation(tiles[4]) == true && SelectedInAutomation(tiles[0]) == false && tiles[4].IsTabStop && !tiles[0].IsTabStop, "selecting a tile through automation moves its selected state and tab stop");

        // Leaving Catalog Health unmounts it: it returns on Unmatched with "Inspect" on the lazy tiles.
        await GoToTabAsync(shell, page, AdminLibrariesTab.Libraries, "LibrariesTable");
        await GoToTabAsync(shell, page, AdminLibrariesTab.Health, "LibrariesTile_Stale");
        Check(page.SelectedHealthSection == AdminHealthSection.Unmatched, "returning to Catalog Health starts on Unmatched Items");
        Check(SelectedInAutomation(Find<Button>(shell, "LibrariesTile_Unmatched")) == true && SelectedInAutomation(Find<Button>(shell, "LibrariesTile_Stale")) == false, "the Unmatched tile is the selected one again");
        foreach (var inspect in new[] { "Skipped", "Stale" })
            Check(Name(Find<Button>(shell, "LibrariesTile_" + inspect)).StartsWith(AdminLibrariesText.T("web.admin.libraries.health.inspect") + " ", StringComparison.Ordinal), $"the {inspect} tile reads Inspect again after Catalog Health was left");
        Check(Reached(Find<FrameworkElement>(shell, "LibrariesSectionHeading_Unmatched")), "the Unmatched section is the one shown after returning");
        await GoToTabAsync(shell, page, AdminLibrariesTab.Libraries, "LibrariesTable");
    }

    private static async Task RunBidiChecksAsync(AdminShellPage shell, AdminLibrariesPage page)
    {
        const string fsi = "⁨", lri = "⁦", pdi = "⁩";
        await GoToTabAsync(shell, page, AdminLibrariesTab.Libraries, "LibrariesTable");
        var detail = Find<TextBlock>(shell, "LibrariesSummaryDetail");
        Check(detail != null && detail.Text.StartsWith(fsi, StringComparison.Ordinal) && detail.Text.Contains(lri, StringComparison.Ordinal) && detail.Text.Contains(pdi + ")", StringComparison.Ordinal),
            $"the summary strip isolates the library name and wraps each path as one left-to-right unit (text: {detail?.Text})");
        page.FlowDirection = FlowDirection.RightToLeft;
        try
        {
            await GoToTabAsync(shell, page, AdminLibrariesTab.Activity, "LibrariesActivity");
            await Until(() => Descendants(shell).OfType<TextBlock>().Any(text => text.Text.Contains("e3.mkv", StringComparison.Ordinal) && text.ActualWidth > 0));
            var preview = Descendants(shell).OfType<TextBlock>().First(text => text.Text.Contains("e3.mkv", StringComparison.Ordinal));
            var previewRow = (StackPanel)VisualTreeHelper.GetParent(preview);
            Check(previewRow.FlowDirection == FlowDirection.LeftToRight && previewRow.Children.OfType<FrameworkElement>().All(child => child.FlowDirection == FlowDirection.LeftToRight),
                "in a right-to-left page the queued preview's paths and commas keep their left-to-right order");
        }
        finally { page.FlowDirection = FlowDirection.LeftToRight; }
        await GoToTabAsync(shell, page, AdminLibrariesTab.Libraries, "LibrariesTable");
    }
}
