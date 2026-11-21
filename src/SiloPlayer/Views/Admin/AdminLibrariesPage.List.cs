using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using Windows.System;
using static SiloPlayer.Views.Admin.AdminLibrariesUi;
using P = SiloPlayer.Core.Services.Admin.AdminLibraryPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The library list: the sortable table from 1024 px (WebUI <c>LibraryTable</c>, dragged by its
/// grip with the pointer or with Space/arrows/Space and Escape from the keyboard, announced
/// through the live region) and the compact cards below it (WebUI <c>LibraryCardList</c>,
/// reordered one place at a time from each card's menu).
/// </summary>
public sealed partial class AdminLibrariesPage
{
    private object?[]? _renderedListKey;
    private readonly List<Flyout> _openMenus = [];
    private readonly List<FrameworkElement> _rowBands = [];
    private (string LibraryId, Flyout Flyout, StackPanel Host, LibraryUiOwner Owner)? _cardMenu;
    private int _dragFrom = -1, _dragOver = -1;
    private bool _keyboardDrag, _pointerDrag;
    private Windows.Foundation.Point _dragStart;
    private Button? _dragGrip;
    private AdminLibrariesController? _dragController;
    private MonitorAuthorityLease? _dragOwner;
    private IReadOnlyList<AdminLibrary>? _dragLibraries;
    private string? _dragLibraryId, _dragTargetId;
    private long _dragGeneration;

    private void RenderList(AdminLibrariesSnapshot state)
    {
        object?[] key =
        [
            state.Owner, state.Libraries, state.Scans, state.RefreshJobs, state.Monitoring, state.Trickplay, state.MountResults, state.ScanStarting, state.ScanCancelling,
            state.MountChecking, state.CleanupPending, state.RefreshStarting, state.JobCancelling, state.Reordering, state.Fetching, state.Complete, _wide, _phone,
        ];
        if (_renderedListKey != null && key.Length == _renderedListKey.Length && key.Zip(_renderedListKey).All(pair => Equals(pair.First, pair.Second)))
        {
            RefreshCardMenu(state);
            return;
        }
        // An open card menu stays open (a move can be repeated, the mount result shows under its label):
        // the menu updates in place and the cards rebuild when it closes.
        if (_cardMenu is { Owner.IsCurrent: true } && !_wide) { RefreshCardMenu(state); return; }
        // Keep the capturing grip and its row geometry mounted until pointer release.
        // Status-only updates are rendered on release; a new owner/list or layout still cancels.
        if (_dragGrip != null && DragIsCurrent && _wide) return;
        _renderedListKey = key;
        var focused = Focused(XamlRoot);
        var focusId = focused == null ? "" : AutomationProperties.GetAutomationId(focused);
        var restoreFocus = focused != null && IsDescendant(focused, _list);
        if (!_keyboardDrag || !DragIsCurrent) CancelDrag();
        var rows = AdminLibrariesController.Rows(state);
        _list.Content = _wide ? Table(state, rows) : Cards(state, rows);
        RefreshCardMenu(state);
        if (restoreFocus && focusId.Length > 0)
            EnqueueUi(() => FindControl(_list, focusId)?.Focus(_keyboardDrag ? FocusState.Keyboard : FocusState.Programmatic));
    }

    private static bool IsDescendant(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    // ── Table ────────────────────────────────────────────────────────────

    private FrameworkElement Table(AdminLibrariesSnapshot state, IReadOnlyList<AdminLibraryRow> rows)
    {
        var owner = RenderOwner(state);
        var grid = new Grid();
        AutomationProperties.SetAutomationId(grid, "LibrariesTable");
        foreach (var width in new[] { new GridLength(40), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(240) })
            grid.ColumnDefinitions.Add(new() { Width = width });
        _rowBands.Clear();
        var rowIndex = 0;
        void AddRow() => grid.RowDefinitions.Add(new() { Height = GridLength.Auto });

        AddRow();
        AddBand(grid, rowIndex, header: true);
        var headers = new[] { "", T("web.admin.libraries.column.name"), T("web.admin.libraries.column.paths"), T("web.admin.libraries.column.type"),
            T("web.admin.libraries.column.status"), T("web.admin.libraries.column.lastScanned"), T("web.admin.libraries.column.actions") };
        for (var column = 1; column < headers.Length; column++)
        {
            var head = Text(headers[column], 14); head.FontWeight = FontWeights.Medium; head.TextWrapping = TextWrapping.NoWrap;
            Place(grid, Cell(head, HorizontalAlignment.Left, 10), rowIndex, column);
        }
        rowIndex++;

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var library = row.Library;
            AddRow();
            var band = AddBand(grid, rowIndex, header: false);
            band.Opacity = _dragFrom == index ? 0.4 : 1;
            if (_dragOver >= 0 && _dragFrom >= 0 && _dragOver == index && _dragOver != _dragFrom)
            {
                band.BorderBrush = Solid(Primary);
                band.BorderThickness = _dragOver < _dragFrom ? new(0, 2, 0, 1) : new(0, 0, 0, 2);
            }
            _rowBands.Add(band);
            Place(grid, Cell(Grip(state, row, index, rows.Count, owner), HorizontalAlignment.Center), rowIndex, 0);
            var name = Strong(library.Name); name.TextWrapping = TextWrapping.NoWrap;
            Place(grid, Cell(name), rowIndex, 1);
            Place(grid, Cell(PathsCell(library, owner)), rowIndex, 2);
            Place(grid, Cell(Badge(P.TypeLabel(library.Type))), rowIndex, 3);
            Place(grid, Cell(StatusBadges(row, vertical: true)), rowIndex, 4);
            Place(grid, Cell(LastScanned(library)), rowIndex, 5);
            Place(grid, Cell(RowActions(row, owner), HorizontalAlignment.Right), rowIndex, 6);
            rowIndex++;
            if (row.HasActiveWork)
            {
                AddRow();
                var work = ActiveWork(row, owner, touch: false);
                work.Margin = new(48, 0, 0, 0);
                var holder = new Border { Child = work, Padding = new(16, 8, 16, 8), Background = Tint("MutedBrush", 0.20), BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1) };
                Grid.SetColumnSpan(holder, 7);
                Place(grid, holder, rowIndex, 0);
                rowIndex++;
            }
        }
        foreach (var row in rows.Where(row => row.HasScanWarning))
        {
            AddRow();
            var holder = new Border { Child = ScanWarning(row, owner, touch: false), Padding = new(12, 8, 12, 8), Background = Solid(Destructive, 0.05), BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1) };
            AutomationProperties.SetAutomationId(holder, "LibrariesWarning_" + row.Library.Id);
            Grid.SetColumnSpan(holder, 7);
            Place(grid, holder, rowIndex, 0);
            rowIndex++;
        }
        var scroller = new ScrollViewer
        {
            Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
        };
        return Surface(scroller, Radius2Xl, 0);
    }

    private static Border AddBand(Grid grid, int row, bool header)
    {
        var band = new Border { BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        Grid.SetRow(band, row); Grid.SetColumnSpan(band, 7);
        grid.Children.Add(band);
        if (!header)
        {
            band.PointerEntered += (_, _) => band.Background = Tint("MutedBrush", 0.5);
            band.PointerExited += (_, _) => band.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        return band;
    }

    private static Border Cell(UIElement child, HorizontalAlignment alignment = HorizontalAlignment.Left, double vertical = 8) => new()
    {
        // shadcn TableHead/TableCell: px-2 sm:px-3 (the table only shows from 1024 px, so always 12); p-2 / h-10 py-3.
        Child = child, Padding = new(12, vertical, 12, vertical), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = alignment,
    };

    private static void Place(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row); Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private FrameworkElement PathsCell(AdminLibrary library, LibraryUiOwner owner)
    {
        if (library.Paths.Count == 1)
        {
            var path = Mono(library.Paths[0]);
            path.TextWrapping = TextWrapping.NoWrap;
            ToolTipService.SetToolTip(path, library.Paths[0]);
            return path;
        }
        var text = T("web.admin.libraries.list.folderCount", ("count", library.Paths.Count));
        var button = new HyperlinkButton { Content = Text(text, 12, true), Padding = new(0), MinHeight = 0 };
        AutomationProperties.SetName(button, text);
        AutomationProperties.SetAutomationId(button, "LibrariesFolders_" + library.Id);
        button.Click += (_, _) => OpenEditor(library, owner);
        return button;
    }

    private static FrameworkElement LastScanned(AdminLibrary library)
    {
        var stack = new StackPanel { Spacing = 4 };
        var when = Text(library.LastScannedAt is { } scanned ? DateTimeText(scanned) : T("web.admin.libraries.list.neverScanned"), 12, true);
        when.TextWrapping = TextWrapping.NoWrap;
        stack.Children.Add(when);
        if (library.ScanWarningAt is { } warned)
        {
            var warning = Text(T("web.admin.libraries.list.warningAt", ("time", DateTimeText(warned))), 11);
            warning.Foreground = Solid(Destructive); warning.TextWrapping = TextWrapping.NoWrap;
            stack.Children.Add(warning);
        }
        return stack;
    }

    /// <summary>The Enabled/Disabled, scan, warning, monitoring and seek-preview badges (WebUI <c>LibraryStatusBadges</c>).</summary>
    private static Panel StatusBadges(AdminLibraryRow row, bool vertical)
    {
        Panel panel = vertical ? new StackPanel { Spacing = 4 } : new LogsWrapPanel { Spacing = 6, RowSpacing = 6 };
        var library = row.Library;
        panel.Children.Add(Badge(T(library.Enabled ? "web.admin.libraries.list.enabled" : "web.admin.libraries.list.disabled"),
            library.Enabled ? BadgeTone.Outline : BadgeTone.Destructive));
        if (row.RunningScans > 0) panel.Children.Add(Badge(T("web.admin.libraries.scans.running", AdminLibrariesText.Count(row.RunningScans))));
        if (row.QueuedScans > 0) panel.Children.Add(Badge(T("web.admin.libraries.scans.queued", AdminLibrariesText.Count(row.QueuedScans))));
        if (library.ScanWarningCode == "empty_root") panel.Children.Add(Badge(T("web.admin.libraries.list.emptyRootGuarded"), BadgeTone.Destructive));
        if (library.ScanWarningCode == "dead_root") panel.Children.Add(Badge(T("web.admin.libraries.list.rootUnreachable"), BadgeTone.Destructive));
        if (library.ScanWarningCode == "partial_walk") panel.Children.Add(Badge(T("web.admin.libraries.list.partialScan"), BadgeTone.Destructive));
        if (P.MonitoringNeedsAttention(row.Monitoring))
        {
            var label = P.MonitoringStateLabel(row.Monitoring!.State);
            var detail = row.Monitoring.Detail.Trim();
            var badge = Badge(T("web.admin.common.libraryEditor.monitoringBadge.label", ("state", label)), BadgeTone.Warning, "alert-triangle",
                detail.Length > 0 ? detail : T("web.admin.common.libraryEditor.monitoringBadge.title", ("state", label)));
            if (detail.Length > 0) AutomationProperties.SetName(badge, T("web.admin.common.libraryEditor.monitoringBadge.label", ("state", label)) + ". " +
                T("web.admin.common.libraryEditor.monitoringBadge.srDetail", ("detail", detail)));
            panel.Children.Add(badge);
        }
        if (row.Trickplay is { } trickplay)
        {
            var detail = P.TrickplayDetail(trickplay);
            var badge = Badge(P.TrickplayLabel(trickplay), trickplay.Unusable > 0 ? BadgeTone.Warning : BadgeTone.Outline, trickplay.Unusable > 0 ? "alert-triangle" : null, detail);
            AutomationProperties.SetName(badge, P.TrickplayLabel(trickplay) + T("web.admin.common.trickplay.badge.srDetail", ("detail", detail)));
            panel.Children.Add(badge);
        }
        return panel;
    }

    private FrameworkElement MountIcon(AdminLibraryRow row, double size)
    {
        if (row.MountChecking) return Icon("hard-drive", size, Brush("SecondaryTextBrush"));
        if (row.MountResult is { Healthy: true }) return Icon("circle-check", size, Solid(Success));
        if (row.MountResult != null) return Icon("alert-triangle", size, Solid(Destructive));
        return Icon("hard-drive", size);
    }

    private StackPanel RowActions(AdminLibraryRow row, LibraryUiOwner owner)
    {
        var library = row.Library;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        var scan = IconButton(row.ScanStops ? "square" : "refresh-cw", row.ScanLabel, "LibrariesScan_" + library.Id, foreground: row.ScanStops ? Solid(Destructive) : null, fillIcon: row.ScanStops);
        if (row.ScanStarting && !row.ScanStops) scan.Content = Spinning("refresh-cw", 12);
        scan.IsEnabled = !row.ScanDisabled;
        scan.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.ScanOrStopAsync(library.Id, owner.Lease); };
        actions.Children.Add(scan);
        var refresh = IconButton(row.RefreshStops ? "square" : "database-backup", row.RefreshLabel, "LibrariesRefresh_" + library.Id,
            foreground: row.RefreshStops ? Solid(Destructive) : null, fillIcon: row.RefreshStops);
        refresh.IsEnabled = !row.RefreshDisabled;
        refresh.Click += (_, _) => RefreshOrStop(row, owner);
        actions.Children.Add(refresh);
        var mountName = row.MountMessage ?? T("web.admin.libraries.list.verifyMounts");
        var mount = IconButton("hard-drive", mountName, "LibrariesMount_" + library.Id);
        mount.Content = MountIcon(row, 12);
        mount.IsEnabled = !row.MountChecking;
        mount.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.CheckMountAsync(library.Id, owner.Lease); };
        actions.Children.Add(mount);
        var edit = IconButton("pencil", T("web.admin.libraries.list.edit", ("name", library.Name)), "LibrariesEdit_" + library.Id);
        edit.Click += (_, _) => OpenEditor(library, owner);
        actions.Children.Add(edit);
        var delete = IconButton("trash-2", T("web.admin.libraries.list.delete", ("name", library.Name)), "LibrariesDelete_" + library.Id);
        delete.Click += (_, _) => _ = ConfirmDeleteAsync(library, owner);
        actions.Children.Add(delete);
        if (row.CanCleanup)
        {
            var cleanup = IconButton("trash-2", T("web.admin.libraries.list.confirmCleanupTitle"), "LibrariesCleanup_" + library.Id, foreground: Solid(Destructive));
            AutomationProperties.SetHelpText(cleanup, library.Name);
            cleanup.IsEnabled = !row.CleanupPending;
            cleanup.Click += (_, _) => _ = ConfirmCleanupAsync(library, owner);
            actions.Children.Add(cleanup);
        }
        return actions;
    }

    private void RefreshOrStop(AdminLibraryRow row, LibraryUiOwner owner)
    {
        if (!owner.IsCurrent) return;
        if (row.ActiveRefresh is { } job) _ = owner.Controller.CancelJobAsync(job.Id, owner.Lease);
        else _ = ShowRefreshDialogAsync(row.Library, owner);
    }

    /// <summary>The refresh job and scans running for a library, each with its stop control (WebUI <c>LibraryActiveWork</c>).</summary>
    private StackPanel ActiveWork(AdminLibraryRow row, LibraryUiOwner owner, bool touch)
    {
        var stack = new StackPanel { Spacing = 6 };
        AutomationProperties.SetAutomationId(stack, "LibrariesActiveWork_" + row.Library.Id);
        if (row.ActiveRefresh is { } job)
        {
            var line = new Grid { ColumnSpacing = 6 };
            line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var stop = StopButton(T("web.admin.libraries.activeWork.cancelRefresh"), "LibrariesStopRefresh_" + row.Library.Id, touch);
            stop.IsEnabled = !row.JobCancelling;
            stop.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.CancelJobAsync(job.Id, owner.Lease); };
            line.Children.Add(stop);
            var icon = Icon("database-backup", 12, Brush("SecondaryTextBrush")); icon.Margin = new(0, 2, 0, 0); icon.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(icon, 1); line.Children.Add(icon);
            var text = new StackPanel { Spacing = 2 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var label = Text(T("web.admin.libraries.activeWork.metadata"), 11); label.FontWeight = FontWeights.Medium;
            head.Children.Add(label);
            var message = Text(P.JobMessage(job), 11, true); message.TextTrimming = TextTrimming.CharacterEllipsis; message.TextWrapping = TextWrapping.NoWrap;
            head.Children.Add(message);
            text.Children.Add(head);
            text.Children.Add(Text(P.JobProgress(job), 10, true));
            Grid.SetColumn(text, 2); line.Children.Add(text);
            stack.Children.Add(line);
        }
        if (row.Scans.Count > 0) stack.Children.Add(ScanTasks(row, owner, touch));
        return stack;
    }

    private readonly HashSet<string> _expandedScans = new(StringComparer.Ordinal);

    private StackPanel ScanTasks(AdminLibraryRow row, LibraryUiOwner owner, bool touch)
    {
        const int collapsedLimit = 4;
        var libraryId = row.Library.Id;
        var expanded = _expandedScans.Contains(libraryId);
        var stack = new StackPanel { Spacing = 4 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var stop = StopButton(T("web.admin.libraries.activeWork.cancelScans"), "LibrariesStopScans_" + libraryId, touch);
        stop.IsEnabled = !row.ScanCancelling;
        stop.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.CancelScansAsync(libraryId, owner.Lease); };
        head.Children.Add(stop);
        head.Children.Add(row.RunningScans > 0 ? Spinning("refresh-cw", 12, Brush("SecondaryTextBrush"), 2) : Icon("refresh-cw", 12, Brush("SecondaryTextBrush")));
        var label = Text(T("web.admin.libraries.activeWork.scan"), 11); label.FontWeight = FontWeights.Medium;
        head.Children.Add(label);
        var counts = new List<string>();
        if (row.RunningScans > 0) counts.Add(T("web.admin.libraries.scans.running", AdminLibrariesText.Count(row.RunningScans)));
        if (row.QueuedScans > 0) counts.Add(T("web.admin.libraries.scans.queued", AdminLibrariesText.Count(row.QueuedScans)));
        head.Children.Add(Text(string.Join(" · ", counts), 11, true));
        if (row.Scans.Count > collapsedLimit)
        {
            var toggle = new HyperlinkButton { Padding = new(4, 0, 4, 0), MinHeight = touch ? 44 : 0 };
            var toggleContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            toggleContent.Children.Add(Text(expanded ? T("web.admin.libraries.scanQueue.showLess") : T("web.admin.libraries.activeWork.showAll", AdminLibrariesText.Count(row.Scans.Count)), 10, true));
            toggleContent.Children.Add(Icon(expanded ? "chevron-up" : "chevron-down", 12, Brush("SecondaryTextBrush")));
            toggle.Content = toggleContent;
            AutomationProperties.SetAutomationId(toggle, "LibrariesScanToggle_" + libraryId);
            AutomationProperties.SetItemStatus(toggle, expanded ? "expanded" : "collapsed");
            toggle.Click += (_, _) => { if (!owner.IsCurrent) return; if (!_expandedScans.Remove(libraryId)) _expandedScans.Add(libraryId); _renderedListKey = null; if (_controller != null) Render(_controller.State); };
            head.Children.Add(toggle);
        }
        stack.Children.Add(head);
        var list = new StackPanel { Spacing = 2, Margin = new(touch ? 38 : 54, 0, 0, 0) };
        foreach (var scan in expanded ? row.Scans : row.Scans.Take(collapsedLimit)) list.Children.Add(CompactScan(scan));
        stack.Children.Add(expanded ? new ScrollViewer { Content = list, MaxHeight = 224, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } : list);
        return stack;
    }

    private static StackPanel CompactScan(AdminLibraryScanRun scan)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        ToolTipService.SetToolTip(row, scan.Path ?? P.ScanTarget(null));
        row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new(3), VerticalAlignment = VerticalAlignment.Center, Background = scan.IsRunning ? Solid(Success) : Tint("SecondaryTextBrush", 0.3) });
        if (scan.Mode != "file") row.Children.Add(Text(P.ScanMode(scan.Mode), 10));
        var target = scan.Path != null ? Mono(P.ScanTarget(scan.Path), 10) : Text(P.ScanTarget(null), 10, true);
        target.TextTrimming = TextTrimming.CharacterEllipsis; target.TextWrapping = TextWrapping.NoWrap; target.MaxWidth = 280;
        row.Children.Add(target);
        var progress = scan.IsRunning ? P.ScanProgress(scan) : "";
        if (progress.Length > 0) { var text = Text("· " + progress, 10, true); text.TextWrapping = TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; text.MaxWidth = 320; row.Children.Add(text); }
        return row;
    }

    private static Button StopButton(string label, string automationId, bool touch)
    {
        // h-5 w-5 rounded; on touch the hit area grows to 44 px without changing how it looks (after:-inset-3): a 44 px button with a -12 px margin.
        var button = IconButton("square", label, automationId, touch ? 44 : 20, 12, Solid(Destructive), fillIcon: true);
        button.CornerRadius = new(4);
        if (touch) button.Margin = new(-12);
        return button;
    }

    /// <summary>A library's scan warning: what went wrong, the hint, and mount check / cleanup for missing or empty roots.</summary>
    private StackPanel ScanWarning(AdminLibraryRow row, LibraryUiOwner owner, bool touch)
    {
        var library = row.Library;
        var stack = new StackPanel { Spacing = 8, Padding = new(0, 4, 0, 4) };
        var (title, hint) = P.ScanWarningText(library);
        var heading = Text(title, 14); heading.FontWeight = FontWeights.Medium; heading.Foreground = Solid(Destructive);
        stack.Children.Add(heading);
        stack.Children.Add(Text(hint, 14, true));
        if (library.ScanWarningCode is not ("empty_root" or "dead_root")) return stack;
        var actions = new LogsWrapPanel { Spacing = 8, RowSpacing = 8 };
        var check = Action(T("web.admin.libraries.warning.checkMount"), "LibrariesWarningMount_" + library.Id, height: touch ? 44 : 32);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }; // gap-1.5 + me-1
        content.Children.Add(MountIcon(row, 14));
        content.Children.Add(new TextBlock { Text = T("web.admin.libraries.warning.checkMount"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        check.Content = content;
        ToolTipService.SetToolTip(check, row.MountMessage ?? T("web.admin.libraries.warning.checkMountTitle"));
        check.IsEnabled = !row.MountChecking;
        check.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.CheckMountAsync(library.Id, owner.Lease); };
        actions.Children.Add(check);
        if (library.ScanWarningCode == "dead_root")
        {
            var cleanup = Action(T("web.admin.libraries.warning.confirmCleanup"), "LibrariesWarningCleanup_" + library.Id, "trash-2", height: touch ? 44 : 32, gap: 10);
            ToolTipService.SetToolTip(cleanup, T("web.admin.libraries.list.confirmCleanupTitle"));
            cleanup.IsEnabled = !row.CleanupPending;
            cleanup.Click += (_, _) => _ = ConfirmCleanupAsync(library, owner);
            actions.Children.Add(cleanup);
        }
        stack.Children.Add(actions);
        return stack;
    }

    // ── Reorder: pointer drag and keyboard (WebUI dnd-kit sensors + useSortableAccessibility) ──

    private Button Grip(AdminLibrariesSnapshot state, AdminLibraryRow row, int index, int count, LibraryUiOwner owner)
    {
        var grip = IconButton("grip-vertical", T("web.admin.libraries.list.dragToReorder"), "LibrariesGrip_" + row.Library.Id, 28, 16, Brush("SecondaryTextBrush"));
        AutomationProperties.SetHelpText(grip, Core.Localization.AppStrings.Get("web.common.sortable.instructions", fallback: ""));
        grip.IsEnabled = state.CanReorder || (_keyboardDrag && _dragFrom == index);
        // Button consumes Space/Enter in KeyDown; the reorder keys are read before it.
        grip.PreviewKeyDown += (_, args) => OnGripKey(args, index, count, owner, state.Libraries);
        var gripId = AutomationProperties.GetAutomationId(grip);
        grip.LostFocus += (_, _) =>
        {
            if (!owner.IsCurrent || !_keyboardDrag) return;
            // A re-render replaces the grip and focus moves to its replacement; only real focus loss cancels.
            EnqueueUi(() =>
            {
                if (owner.IsCurrent && _keyboardDrag && (Focused(XamlRoot) is not { } now || AutomationProperties.GetAutomationId(now) != gripId)) CancelDragAnnounced();
            });
        };
        // Button handles pointer presses itself, so the drag listens to handled events too.
        grip.AddHandler(PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            var point = args.GetCurrentPoint(_list);
            if (!point.Properties.IsLeftButtonPressed || !owner.IsCurrent || !BeginPointerDrag(grip, owner.Controller, owner.Lease!, state.Libraries, row.Library.Id, point.Position)) return;
            grip.CapturePointer(args.Pointer);
            args.Handled = true;
        }), true);
        grip.AddHandler(PointerMovedEvent, new PointerEventHandler((_, args) =>
        {
            var point = args.GetCurrentPoint(_list);
            if (point.Properties.IsLeftButtonPressed && MovePointerDrag(grip, point.Position)) args.Handled = true;
        }), true);
        grip.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, args) =>
        {
            if (_dragGrip != grip) return;
            ReleasePointerDrag(grip, () => grip.ReleasePointerCapture(args.Pointer));
            args.Handled = true;
        }), true);
        grip.PointerCaptureLost += (_, _) => LosePointerDragCapture(grip);
        grip.PointerCanceled += (_, _) => CancelPointerDrag(grip);
        return grip;
    }

    private bool DragIsCurrent => ReferenceEquals(_controller, _dragController) && _dragController?.IsOwnerCurrent(_dragOwner) == true
        && ReferenceEquals(_dragController.State.Libraries, _dragLibraries);

    private bool BeginPointerDrag(Button grip, AdminLibrariesController controller, MonitorAuthorityLease owner, IReadOnlyList<AdminLibrary> libraries, string libraryId, Windows.Foundation.Point point)
    {
        if (!ReferenceEquals(controller, _controller) || !controller.IsOwnerCurrent(owner) || !controller.State.CanReorder || !ReferenceEquals(controller.State.Libraries, libraries)) return false;
        var index = libraries.ToList().FindIndex(library => library.Id == libraryId);
        if (index < 0) return false;
        CancelDrag();
        _dragController = controller; _dragOwner = owner;
        _dragLibraries = libraries; _dragLibraryId = libraryId; _dragTargetId = libraryId;
        _dragFrom = index; _dragOver = index; _dragGrip = grip; _dragStart = point;
        return true;
    }

    private bool MovePointerDrag(Button grip, Windows.Foundation.Point point)
    {
        if (_dragGrip != grip || _dragFrom < 0 || !DragIsCurrent) return false;
        if (!_pointerDrag && Math.Abs(point.Y - _dragStart.Y) < 5 && Math.Abs(point.X - _dragStart.X) < 5) return false;
        if (!_pointerDrag) { _pointerDrag = true; Announce(SortableText("web.common.sortable.pickedUp", _dragFrom)); }
        var over = RowIndexAt(point.Y);
        if (over >= 0 && over < _dragLibraries!.Count && over != _dragOver)
        {
            _dragOver = over; _dragTargetId = _dragLibraries[over].Id;
            Announce(SortableText("web.common.sortable.over", _dragFrom, over)); RedrawDragBands();
        }
        return true;
    }

    private void ReleasePointerDrag(Button grip, Action releaseCapture)
    {
        if (_dragGrip != grip) return;
        var shouldCommit = _pointerDrag && DragIsCurrent;
        _pointerDrag = false;
        releaseCapture();
        if (_dragGrip != grip) return;
        if (shouldCommit) CommitDrag();
        else
        {
            CancelDrag();
            _renderedListKey = null;
            if (_controller != null) Render(_controller.State);
        }
    }

    private void LosePointerDragCapture(Button grip)
    {
        var generation = _dragGeneration;
        EnqueueUi(() => { if (_dragGrip == grip && _dragGeneration == generation) CancelDragAnnounced(); });
    }

    private void CancelPointerDrag(Button grip)
    {
        if (_dragGrip == grip) CancelDragAnnounced();
    }

    private void OnGripKey(KeyRoutedEventArgs args, int index, int count, LibraryUiOwner owner, IReadOnlyList<AdminLibrary> libraries)
    {
        if (!owner.IsCurrent || !ReferenceEquals(owner.Controller.State.Libraries, libraries)) return;
        switch (args.Key)
        {
            case VirtualKey.Space or VirtualKey.Enter:
                args.Handled = true;
                if (!_keyboardDrag)
                {
                    if (!owner.Controller.State.CanReorder) return;
                    _dragController = owner.Controller; _dragOwner = owner.Lease;
                    _dragLibraries = libraries; _dragLibraryId = libraries[index].Id; _dragTargetId = libraries[index].Id;
                    _keyboardDrag = true; _dragFrom = index; _dragOver = index;
                    Announce(SortableText("web.common.sortable.pickedUp", index));
                    RedrawDragBands();
                }
                else CommitDrag();
                break;
            case VirtualKey.Up or VirtualKey.Down when _keyboardDrag:
                args.Handled = true;
                var next = Math.Clamp(_dragOver + (args.Key == VirtualKey.Up ? -1 : 1), 0, count - 1);
                if (next == _dragOver) return;
                _dragOver = next;
                _dragTargetId = libraries[next].Id;
                Announce(SortableText("web.common.sortable.over", _dragFrom, next));
                RedrawDragBands();
                break;
            case VirtualKey.Escape when _keyboardDrag:
                args.Handled = true;
                CancelDragAnnounced();
                break;
        }
    }

    private int RowIndexAt(double y)
    {
        for (var index = 0; index < _rowBands.Count; index++)
        {
            var band = _rowBands[index];
            if (band.XamlRoot == null) continue;
            var top = band.TransformToVisual(_list).TransformPoint(new(0, 0)).Y;
            if (y < top + band.ActualHeight) return index;
        }
        return _rowBands.Count - 1;
    }

    private void RedrawDragBands()
    {
        for (var index = 0; index < _rowBands.Count; index++)
        {
            var band = (Border)_rowBands[index];
            band.Opacity = index == _dragFrom ? 0.4 : 1;
            var marked = _dragFrom >= 0 && index == _dragOver && _dragOver != _dragFrom;
            band.BorderBrush = marked ? Solid(Primary) : Brush("BorderBrush");
            band.BorderThickness = marked ? (_dragOver < _dragFrom ? new(0, 2, 0, 1) : new(0, 0, 0, 2)) : new(0, 0, 0, 1);
        }
    }

    private string SortableText(string key, int from, int? at = null)
    {
        var libraries = _controller?.State.Libraries ?? [];
        var name = from >= 0 && from < libraries.Count ? libraries[from].Name : Core.Localization.AppStrings.Get("web.common.sortable.fallbackName", fallback: "Item");
        return T(key, ("name", name), ("position", (at ?? from) + 1), ("total", libraries.Count));
    }

    private void CommitDrag()
    {
        var controller = _dragController;
        var owner = _dragOwner;
        if (!DragIsCurrent || controller?.State.CanReorder != true) { CancelDrag(); return; }
        var libraries = controller?.State.Libraries ?? [];
        var from = libraries.ToList().FindIndex(library => library.Id == _dragLibraryId);
        var to = libraries.ToList().FindIndex(library => library.Id == _dragTargetId);
        if (controller != null && from >= 0 && to >= 0 && from != to)
        {
            Announce(SortableText("web.common.sortable.droppedAt", from, to));
            var order = P.ArrayMove(libraries, from, to);
            ResetDrag();
            if (order != null) _ = controller.ReorderAsync(order, owner);
        }
        else
        {
            if (from >= 0) Announce(SortableText("web.common.sortable.dropped", from));
            ResetDrag();
        }
        _renderedListKey = null;
        if (controller != null) Render(controller.State);
    }

    private void CancelDragAnnounced()
    {
        if (_dragFrom >= 0) Announce(SortableText("web.common.sortable.canceled", _dragFrom));
        CancelDrag();
        if (_controller != null) { _renderedListKey = null; Render(_controller.State); }
    }

    private void CancelDrag()
    {
        var active = _dragFrom >= 0;
        ResetDrag();
        if (active) RedrawDragBands();
    }

    private void ResetDrag()
    {
        _dragFrom = -1; _dragOver = -1; _keyboardDrag = false; _pointerDrag = false; _dragGrip = null;
        _dragController = null; _dragOwner = null; _dragLibraries = null; _dragLibraryId = null; _dragTargetId = null; _dragGeneration++;
    }

    // ── Cards ────────────────────────────────────────────────────────────

    private FrameworkElement Cards(AdminLibrariesSnapshot state, IReadOnlyList<AdminLibraryRow> rows)
    {
        var owner = RenderOwner(state);
        var list = new StackPanel { Spacing = 12 };
        AutomationProperties.SetAutomationId(list, "LibrariesCards");
        var moving = state.Reordering || state.Fetching || !state.CanReorder;
        for (var index = 0; index < rows.Count; index++) list.Children.Add(Card(rows[index], owner, !moving && index > 0, !moving && index < rows.Count - 1));
        return list;
    }

    private readonly HashSet<string> _expandedPaths = new(StringComparer.Ordinal);

    private Border Card(AdminLibraryRow row, LibraryUiOwner owner, bool canMoveUp, bool canMoveDown)
    {
        var library = row.Library;
        var stack = new StackPanel { Spacing = 12 };
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var typeIcon = Icon(P.FindType(library.Type)?.Icon ?? "folder-open", 20, Brush("SecondaryTextBrush")); typeIcon.VerticalAlignment = VerticalAlignment.Top; typeIcon.Margin = new(0, 2, 0, 0);
        head.Children.Add(typeIcon);
        var name = Strong(library.Name, 16); name.MaxLines = 2; name.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetHeadingLevel(name, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        Grid.SetColumn(name, 1); head.Children.Add(name);
        var menu = CardMenuButton(row, owner, canMoveUp, canMoveDown);
        Grid.SetColumn(menu, 2); head.Children.Add(menu);
        stack.Children.Add(head);

        var badges = StatusBadges(row, vertical: false);
        badges.Children.Insert(0, Badge(P.TypeLabel(library.Type)));
        badges.Children.Add(Text(T("web.admin.libraries.list.folderCount", ("count", library.Paths.Count)), 12, true));
        stack.Children.Add(badges);
        stack.Children.Add(CardPaths(library, owner));

        var last = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var label = Text(T("web.admin.libraries.column.lastScanned"), 12, true); label.FontWeight = FontWeights.Medium; label.VerticalAlignment = VerticalAlignment.Top;
        last.Children.Add(label);
        last.Children.Add(LastScanned(library));
        stack.Children.Add(last);
        if (row.HasScanWarning) stack.Children.Add(new Border { Child = ScanWarning(row, owner, touch: true), CornerRadius = new(RadiusXl), Padding = new(12, 8, 12, 8), Background = Solid(Destructive, 0.05) });
        if (row.HasActiveWork) stack.Children.Add(new Border { Child = ActiveWork(row, owner, touch: true), CornerRadius = new(RadiusXl), Padding = new(12, 8, 12, 8), Background = Tint("MutedBrush", 0.20) });

        var scan = Action(row.ScanLabel, "LibrariesCardScan_" + library.Id, row.ScanStops ? "square" : "refresh-cw", row.ScanStops ? "DestructiveButtonStyle" : "OutlineButtonStyle",
            null, 44, gap: 8);
        scan.HorizontalAlignment = HorizontalAlignment.Stretch;
        scan.IsEnabled = !row.ScanDisabled;
        scan.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.ScanOrStopAsync(library.Id, owner.Lease); };
        stack.Children.Add(scan);
        var card = Surface(stack, Radius2Xl, 16);
        AutomationProperties.SetAutomationId(card, "LibrariesCard_" + library.Id);
        return card;
    }

    private FrameworkElement CardPaths(AdminLibrary library, LibraryUiOwner owner)
    {
        var list = new StackPanel { Spacing = 4 };
        foreach (var path in library.Paths) { var text = Mono(path); text.TextWrapping = TextWrapping.WrapWholeWords; text.IsTextSelectionEnabled = true; list.Children.Add(text); }
        if (library.Paths.Count <= 1) return list;
        var expanded = _expandedPaths.Contains(library.Id);
        var stack = new StackPanel { Spacing = 4 };
        var toggle = new HyperlinkButton { MinHeight = 44, Padding = new(0) };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        content.Children.Add(Text(T(expanded ? "web.admin.libraries.list.hidePaths" : "web.admin.libraries.list.showPaths"), 12, true));
        content.Children.Add(Icon(expanded ? "chevron-up" : "chevron-down", 14, Brush("SecondaryTextBrush")));
        toggle.Content = content;
        AutomationProperties.SetAutomationId(toggle, "LibrariesPaths_" + library.Id);
        AutomationProperties.SetItemStatus(toggle, expanded ? "expanded" : "collapsed");
        toggle.Click += (_, _) => { if (!owner.IsCurrent) return; if (!_expandedPaths.Remove(library.Id)) _expandedPaths.Add(library.Id); _renderedListKey = null; if (_controller != null) Render(_controller.State); };
        stack.Children.Add(toggle);
        if (expanded) stack.Children.Add(list);
        return stack;
    }

    private Button CardMenuButton(AdminLibraryRow row, LibraryUiOwner owner, bool canMoveUp, bool canMoveDown)
    {
        var library = row.Library;
        var button = IconButton("ellipsis", T("web.admin.libraries.list.moreActions", ("name", library.Name)), "LibrariesMenu_" + library.Id, 44, 16, Brush("SecondaryTextBrush"));
        button.CornerRadius = new(RadiusMd);
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new(0, -8, -8, 0);
        var host = new StackPanel { Width = 256, Spacing = 0 };
        var flyout = new Flyout { Content = host, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight, ShouldConstrainToRootBounds = true };
        flyout.FlyoutPresenterStyle = RoundedPresenterStyle(RadiusMd);
        flyout.Opened += (_, _) => { if (!owner.IsCurrent) { flyout.Hide(); return; } _openMenus.Add(flyout); _cardMenu = (library.Id, flyout, host, owner); FillCardMenu(host, owner.Controller.State, library.Id, owner); };
        flyout.Closed += (_, _) =>
        {
            _openMenus.Remove(flyout);
            if (_cardMenu?.Flyout != flyout) return;
            _cardMenu = null;
            _renderedListKey = null;
            if (_controller != null) EnqueueUi(() => { if (_controller != null) Render(_controller.State); });
        };
        button.Click += (_, _) => { if (owner.IsCurrent) flyout.ShowAt(button); };
        return button;
    }

    private void RefreshCardMenu(AdminLibrariesSnapshot state)
    {
        if (_cardMenu is { } menu && menu.Owner.IsCurrent) FillCardMenu(menu.Host, state, menu.LibraryId, menu.Owner);
    }

    /// <summary>The card's action menu (WebUI <c>LibraryCardMenu</c>); Verify mounts and Move up/down keep it open.</summary>
    private void FillCardMenu(StackPanel host, AdminLibrariesSnapshot state, string libraryId, LibraryUiOwner owner)
    {
        if (!owner.IsCurrent || !ReferenceEquals(state.Owner, owner.Lease)) return;
        var rows = AdminLibrariesController.Rows(state);
        var index = rows.Select((row, i) => (row, i)).FirstOrDefault(entry => entry.row.Library.Id == libraryId);
        if (index.row == null) { _cardMenu?.Flyout.Hide(); return; }
        var row = index.row;
        var moving = state.Reordering || state.Fetching || !state.CanReorder;
        var focusId = Focused(XamlRoot) is { } focused && IsDescendant(focused, host) ? AutomationProperties.GetAutomationId(focused) : "";
        host.Children.Clear();
        Button Item(string id, UIElement icon, string label, string? detail, bool enabled, bool destructive, Action click)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.Children.Add(icon);
            var text = new StackPanel();
            var title = Text(label); if (destructive) title.Foreground = Solid(Destructive);
            text.Children.Add(title);
            if (detail != null) text.Children.Add(Text(detail, 12, true));
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            var item = new Button
            {
                Content = grid, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new(8, 6, 8, 6), CornerRadius = new(RadiusSm), Style = (Style)Application.Current.Resources["GhostButtonStyle"], IsEnabled = enabled,
            };
            AutomationProperties.SetName(item, label);
            AutomationProperties.SetAutomationId(item, id + "_" + libraryId);
            item.Click += (_, _) => { if (owner.IsCurrent) click(); };
            host.Children.Add(item);
            return item;
        }
        Item("LibrariesMenuRefresh", Icon(row.RefreshStops ? "square" : "database-backup", 16, row.RefreshStops ? Solid(Destructive) : null, row.RefreshStops), row.RefreshLabel, null,
            !row.RefreshDisabled, row.RefreshStops, () => { _cardMenu?.Flyout.Hide(); RefreshOrStop(row, owner); });
        Item("LibrariesMenuMount", MountIcon(row, 16), T("web.admin.libraries.list.verifyMounts"), row.MountMessage, !row.MountChecking, false,
            () => { if (owner.IsCurrent) _ = owner.Controller.CheckMountAsync(libraryId, owner.Lease); });
        Item("LibrariesMenuEdit", Icon("pencil", 16), T("web.admin.libraries.list.edit", ("name", row.Library.Name)), null, true, false,
            () => { _cardMenu?.Flyout.Hide(); OpenEditor(row.Library, owner); });
        Item("LibrariesMenuUp", Icon("arrow-up", 16), T("web.admin.libraries.list.moveUp"), null, !moving && index.i > 0, false,
            () => { if (owner.IsCurrent) _ = owner.Controller.MoveAsync(libraryId, -1, owner.Lease); });
        Item("LibrariesMenuDown", Icon("arrow-down", 16), T("web.admin.libraries.list.moveDown"), null, !moving && index.i < rows.Count - 1, false,
            () => { if (owner.IsCurrent) _ = owner.Controller.MoveAsync(libraryId, 1, owner.Lease); });
        host.Children.Add(new Border { Height = 1, Margin = new(-4, 4, -4, 4), Background = Brush("BorderBrush") });
        Item("LibrariesMenuDelete", Icon("trash-2", 16, Solid(Destructive)), T("web.admin.libraries.list.delete", ("name", row.Library.Name)), null, true, true,
            () => { _cardMenu?.Flyout.Hide(); _ = ConfirmDeleteAsync(row.Library, owner); });
        if (row.CanCleanup)
            Item("LibrariesMenuCleanup", Icon("trash-2", 16, Solid(Destructive)), T("web.admin.libraries.warning.confirmCleanup"), null, !row.CleanupPending, true,
                () => { _cardMenu?.Flyout.Hide(); _ = ConfirmCleanupAsync(row.Library, owner); });
        if (focusId.Length > 0) EnqueueUi(() => FindControl(host, focusId)?.Focus(FocusState.Keyboard));
    }
}
