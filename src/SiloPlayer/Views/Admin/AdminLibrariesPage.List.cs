using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Helpers;
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
    private (string LibraryId, Flyout Flyout, StackPanel Host, LibraryUiOwner Owner, bool Card)? _cardMenu;
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
            state.MountChecking, state.CleanupPending, state.RefreshStarting, state.JobsCancelling, state.Reordering, state.Fetching, state.Complete, state.Error, _wide, _phone,
        ];
        if (_renderedListKey != null && key.Length == _renderedListKey.Length && key.Zip(_renderedListKey).All(pair => Equals(pair.First, pair.Second)))
        {
            RefreshCardMenu(state);
            return;
        }
        // An open card menu stays open (a move can be repeated, the mount result shows under its label):
        // the menu updates in place and the cards rebuild when it closes.
        if (_cardMenu is { Owner.IsCurrent: true }) { RefreshCardMenu(state); return; }
        // Keep the capturing grip and its row geometry mounted until pointer release.
        // Status-only updates are rendered on release; a new owner/list or layout still cancels.
        if (_dragGrip != null && DragIsCurrent && _wide) return;
        _renderedListKey = key;
        var focused = Focused(XamlRoot);
        var focusId = focused == null ? "" : AutomationProperties.GetAutomationId(focused);
        var restoreFocus = focused != null && IsDescendant(focused, _list);
        if (!_keyboardDrag || !DragIsCurrent) CancelDrag();
        var rows = AdminLibrariesController.Rows(state);
        // A failed load has its banner and Retry above the tabs: it is not also "No libraries configured yet", which would say the server has none.
        _rowBands.Clear();
        _list.Content = rows.Count == 0 && state.Error != null ? null : _wide ? Table(state, rows) : Cards(state, rows);
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

    private const int TableColumns = 5;

    /// <summary>The empty list (official <c>list.empty</c>), for the table and the cards alike.</summary>
    private static Border EmptyList()
    {
        var text = Text(T("web.admin.libraries.list.empty"), 14, true); text.TextAlignment = TextAlignment.Center; text.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetAutomationId(text, "LibrariesEmpty");
        return new Border { Child = text, Padding = new(24, 40, 24, 40), CornerRadius = new(Radius2Xl), Background = Tint("SurfaceBrush", 0.9) };
    }

    private FrameworkElement Table(AdminLibrariesSnapshot state, IReadOnlyList<AdminLibraryRow> rows)
    {
        _rowBands.Clear();
        if (rows.Count == 0) return EmptyList();
        var owner = RenderOwner(state);
        var grid = new Grid();
        AutomationProperties.SetAutomationId(grid, "LibrariesTable");
        // Grip (w-10), Library (its cell max-w-[26rem] = 416 px, so 440 with the cell padding), Status (takes the rest and wraps its pills), Last Scanned (nowrap)
        // and Actions (w-[13.5rem] = 216 px). The official table is `w-full` inside `overflow-x-auto`: as wide as its card, and it scrolls only when its
        // columns cannot fit, so the minimums below are the narrowest each column can be (the cell's icon and a few letters; a pill).
        foreach (var width in new[] { new GridLength(40), new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(216) })
            grid.ColumnDefinitions.Add(new() { Width = width });
        grid.ColumnDefinitions[1].MinWidth = TableLibraryMin; grid.ColumnDefinitions[1].MaxWidth = 440;
        grid.ColumnDefinitions[2].MinWidth = TableStatusMin;
        var fullRows = new List<FrameworkElement>(); // the rows that span every column and must wrap at the table's width
        var rowIndex = 0;
        void AddRow() => grid.RowDefinitions.Add(new() { Height = GridLength.Auto });

        AddRow();
        AddBand(grid, rowIndex, header: true);
        var headers = new[] { "", T("web.admin.libraries.column.library"), T("web.admin.libraries.column.status"), T("web.admin.libraries.column.lastScanned"), T("web.admin.libraries.column.actions") };
        for (var column = 1; column < headers.Length; column++)
        {
            // TableHead: h-10 px-2 py-3 with a 20 px line (text-sm). A table cell's height is a minimum, so the 24 px of padding plus the line makes the header 44 px, not 40.
            var head = Text(headers[column], 14); head.FontWeight = FontWeights.Medium; head.TextWrapping = TextWrapping.NoWrap; head.LineHeight = 20;
            Place(grid, Cell(head, column == TableColumns - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left, 12), rowIndex, column);
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
            var gripButton = Grip(state, row, index, rows.Count, owner);
            // The grip is quiet until its row is hovered or it has focus (opacity-45, group-hover:opacity-100, focus-visible:opacity-100).
            band.PointerEntered += (_, _) => gripButton.Opacity = 1;
            band.PointerExited += (_, _) => { if (gripButton.FocusState == FocusState.Unfocused) gripButton.Opacity = 0.45; };
            Place(grid, Cell(gripButton, HorizontalAlignment.Center), rowIndex, 0);
            Place(grid, Cell(LibraryCell(row, owner), HorizontalAlignment.Stretch, 12), rowIndex, 1);
            // The Status column is a star column: its pills wrap inside it instead of the column growing to its widest line.
            var status = StatusBadges(row, owner, showIdle: true);
            Place(grid, Cell(status, HorizontalAlignment.Stretch), rowIndex, 2);
            Place(grid, Cell(LastScanned(library)), rowIndex, 3);
            Place(grid, Cell(RowActions(row, owner), HorizontalAlignment.Right), rowIndex, 4);
            // The 2 px progress line along the row's bottom edge: the mean progress of its running scans (success at 75%).
            if (row.RunningScans > 0 && AdminLibraryTabs.LibraryScanProgressPercent(row.Scans) is { } percent)
            {
                var line = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Height = 2, IsHitTestVisible = false };
                line.ColumnDefinitions.Add(new() { Width = new(percent, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new() { Width = new(100 - percent, GridUnitType.Star) });
                line.Children.Add(new Border { Background = Solid(Success, 0.75) });
                AutomationProperties.SetAutomationId(line, "LibrariesProgress_" + library.Id);
                Grid.SetRow(line, rowIndex); Grid.SetColumnSpan(line, TableColumns);
                grid.Children.Add(line);
            }
            rowIndex++;
            // A library's warning sits directly beneath its own row.
            if (row.HasScanWarning)
            {
                AddRow();
                var holder = new Border { Child = ScanWarning(row, owner, touch: false), Padding = new(12, 8, 12, 8), Background = Solid(Destructive, 0.05), BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1) };
                AutomationProperties.SetAutomationId(holder, "LibrariesWarning_" + library.Id);
                Grid.SetColumnSpan(holder, TableColumns);
                Place(grid, holder, rowIndex, 0);
                fullRows.Add(holder);
                rowIndex++;
            }
        }
        // A horizontal scroller measures its content at unlimited width, so the table's width is set from the scroller's viewport here: the table is as wide as its
        // card (and wider only when its columns need it), and the warning sentence under a row wraps at that width instead of making the table as wide as itself.
        var scroller = new ScrollViewer
        {
            Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled,
        };
        grid.Width = TableMinWidth(grid);
        foreach (var full in fullRows) full.Width = grid.Width;
        scroller.SizeChanged += (_, args) => FitTableWidth(grid, fullRows, args.NewSize.Width);
        // LayoutUpdated is raised for every layout pass in the window, and the table is rebuilt while scans run: the handler lives only while
        // this grid is in the tree.
        EventHandler<object> fit = (_, _) => { if (scroller.ActualWidth > 0) FitTableWidth(grid, fullRows, scroller.ActualWidth); };
        grid.Loaded += (_, _) => { grid.LayoutUpdated -= fit; grid.LayoutUpdated += fit; };
        grid.Unloaded += (_, _) => grid.LayoutUpdated -= fit;
        return Surface(scroller, Radius2Xl, 0);
    }

    private const double TableLibraryMin = 120, TableStatusMin = 100;

    /// <summary>The narrowest the table can be: the grip, the Actions column, Last Scanned as measured, and the minimum of the two star columns.</summary>
    private static double TableMinWidth(Grid grid) => 40 + 216 + Math.Max(grid.ColumnDefinitions[3].ActualWidth, 100) + TableLibraryMin + TableStatusMin;

    /// <summary>Sets the table's width to its card's width (at least its minimum), and the spanning rows with it.</summary>
    private static void FitTableWidth(Grid grid, IReadOnlyList<FrameworkElement> fullRows, double viewport)
    {
        var width = Math.Max(viewport, TableMinWidth(grid));
        if (Math.Abs(grid.Width - width) < 0.5) return;
        grid.Width = width;
        foreach (var full in fullRows) full.Width = width;
    }

    private static Border AddBand(Grid grid, int row, bool header)
    {
        var band = new Border { BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, 0, 0, 1), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        Grid.SetRow(band, row); Grid.SetColumnSpan(band, TableColumns);
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

    /// <summary>
    /// The library cell: a 32 px type tile, the name, and "type · folder" (a single path, or the folder count) whose button opens the editor on Folders.
    /// </summary>
    private FrameworkElement LibraryCell(AdminLibraryRow row, LibraryUiOwner owner)
    {
        var library = row.Library;
        // max-w-[26rem]. Left-aligned: a Stretch element with a MaxWidth is centred in a wider slot, which put the icon away from its column header.
        var grid = new Grid { ColumnSpacing = 12, MaxWidth = 416, HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var tile = TypeTile(library.Type, 32, RadiusLg); tile.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(tile);
        var text = new StackPanel { Spacing = 2 };
        var name = Strong(library.Name); name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetAutomationId(name, "LibrariesName_" + library.Id);
        text.Children.Add(name);
        var meta = new Grid { ColumnSpacing = 6 };
        meta.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); meta.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); meta.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        meta.Children.Add(Text(P.TypeLabel(library.Type), 12, true));
        var dot = Text("·", 12, true); AutomationProperties.SetAccessibilityView(dot, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw); Grid.SetColumn(dot, 1); meta.Children.Add(dot);
        var folders = FolderLink(library, owner, singlePathMono: true);
        Grid.SetColumn(folders, 2); meta.Children.Add(folders);
        text.Children.Add(meta);
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        return grid;
    }

    /// <summary>The path button beside the type (<c>hover:text-foreground min-w-0 truncate</c>): it opens the editor on Folders, whatever its text.</summary>
    private Button FolderLink(AdminLibrary library, LibraryUiOwner owner, bool singlePathMono)
    {
        var single = singlePathMono && library.Paths.Count == 1 ? library.Paths[0] : null;
        var count = T("web.admin.libraries.list.folderCount", ("count", library.Paths.Count));
        UIElement content;
        if (single != null)
        {
            var path = Mono(AdminPathText.Isolate(single), 11); path.TextTrimming = TextTrimming.CharacterEllipsis; path.TextWrapping = TextWrapping.NoWrap;
            content = path;
        }
        else { var label = Text(count, 12, true); label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; content = label; }
        // A text button with padding and an equal negative margin (4 px radius): no text sits in a zero-padding rounded button, and the row does not grow.
        var button = new Button
        {
            Content = content, Style = (Style)Application.Current.Resources["GhostButtonStyle"], Padding = new(4, 2, 4, 2), Margin = new(-4, -2, -4, -2), MinHeight = 0, MinWidth = 0,
            CornerRadius = new(4), HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetName(button, single ?? count);
        AutomationProperties.SetAutomationId(button, "LibrariesFolders_" + library.Id);
        if (single != null) ToolTipService.SetToolTip(button, single);
        button.Click += (_, _) => OpenEditor(library, owner, AdminLibraryEditorSection.Folders);
        return button;
    }

    /// <summary>Relative last-scanned time with an accessible popover for the exact timestamp, plus any scan warning time (official <c>LibraryLastScanned</c>).</summary>
    private FrameworkElement LastScanned(AdminLibrary library)
    {
        var stack = new StackPanel { Spacing = 2 };
        if (library.LastScannedAt is { } scanned)
        {
            var exact = DateTimeText(scanned);
            var relative = AdminSubtitlesText.Relative(scanned, DateTimeOffset.UtcNow, DateTimeText);
            if (relative != exact)
            {
                var label = Text(relative, 12, true); label.TextWrapping = TextWrapping.NoWrap;
                var button = new Button
                {
                    Content = label, Style = (Style)Application.Current.Resources["GhostButtonStyle"], Padding = new(4, 2, 4, 2), Margin = new(-4, -2, -4, -2), MinHeight = 0, MinWidth = 0,
                    CornerRadius = new(4), HorizontalAlignment = HorizontalAlignment.Left,
                    Flyout = new Flyout { Content = Text(exact, 12), Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft, FlyoutPresenterStyle = RoundedPresenterStyle(RadiusMd) },
                };
                AutomationProperties.SetName(button, relative);
                AutomationProperties.SetHelpText(button, exact);
                AutomationProperties.SetAutomationId(button, "LibrariesLastScanned_" + library.Id);
                ToolTipService.SetToolTip(button, exact);
                stack.Children.Add(button);
            }
            else
            {
                var when = Text(exact, 12, true); when.TextWrapping = TextWrapping.NoWrap;
                stack.Children.Add(when);
            }
        }
        else
        {
            var never = Text(T("web.admin.libraries.list.neverScanned"), 12, true); never.TextWrapping = TextWrapping.NoWrap;
            stack.Children.Add(never);
        }
        if (library.ScanWarningAt is { } warned)
        {
            var warning = Text(T("web.admin.libraries.list.warningAt", ("time", DateTimeText(warned))), 11);
            warning.Foreground = Solid(Destructive);
            if (_compactTable) { warning.TextWrapping = TextWrapping.Wrap; warning.MaxWidth = 130; } else warning.TextWrapping = TextWrapping.NoWrap;
            stack.Children.Add(warning);
        }
        return stack;
    }

    /// <summary>A live scan or refresh as a clickable pill into Scan Activity (<c>rounded-full border px-2.5 py-0.5 text-[11px]</c>).</summary>
    private Button ActivityPill(string label, string name, string tone, bool dot, string? icon, string automationId, Action click, string? tooltip = null)
    {
        var (foreground, background, border) = tone switch
        {
            "success" => ((Brush)Solid(Success), (Brush)Solid(Success, 0.10), (Brush)Solid(Success, 0.35)),
            "primary" => ((Brush)Solid(Primary), (Brush)Solid(Primary, 0.10), (Brush)Solid(Primary, 0.30)),
            _ => (Brush("SecondaryForegroundBrush"), Brush("SecondaryBackgroundBrush"), Brush("BorderBrush")),
        };
        // A grid, not a horizontal stack: in a narrow Status column the label wraps inside the pill instead of being cut off at its edge.
        var content = new Grid { ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var column = 0;
        void AddPart(FrameworkElement part, GridLength width)
        {
            content.ColumnDefinitions.Add(new() { Width = width });
            part.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(part, column++); content.Children.Add(part);
        }
        if (dot) AddPart(new Border { Width = 6, Height = 6, CornerRadius = new(3), Background = Solid(Success) }, GridLength.Auto);
        if (icon != null) AddPart(Icon(icon, 12, foreground), GridLength.Auto);
        var text = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = foreground, TextWrapping = TextWrapping.Wrap };
        AddPart(text, new(1, GridUnitType.Star));
        var pill = new Button
        {
            Content = content, Style = (Style)Application.Current.Resources["GhostButtonStyle"], MinHeight = 0, MinWidth = 0, Padding = new(10, 3, 10, 3), BorderThickness = new(1),
            Background = background, BorderBrush = border, HorizontalAlignment = HorizontalAlignment.Left,
        };
        // rounded-full: a pill's radius follows its size (a fixed 100 is drawn as an oval with pointed ends).
        RoundedBadge.SetRadius(pill, 999);
        if (tooltip != null) ToolTipService.SetToolTip(pill, tooltip);
        AutomationProperties.SetName(pill, name);
        AutomationProperties.SetAutomationId(pill, automationId);
        pill.Click += (_, _) => click();
        return pill;
    }

    /// <summary>
    /// The Status column badges for a library row or card (official <c>LibraryStatusBadges</c>): no "Enabled" pill for a healthy library, live scans and
    /// refreshes as a pill into Scan Activity, Disabled, scan warnings, monitoring warnings and seek-preview status, and a quiet "Idle" when nothing else shows.
    /// </summary>
    private LogsWrapPanel StatusBadges(AdminLibraryRow row, LibraryUiOwner owner, bool showIdle)
    {
        var panel = new LogsWrapPanel { Spacing = 6, RowSpacing = 6 };
        var library = row.Library;
        var hasScans = row.RunningScans > 0 || row.QueuedScans > 0;
        var monitoringWarning = P.MonitoringNeedsAttention(row.Monitoring);
        var hasAnyPill = !library.Enabled || hasScans || row.ActiveRefresh != null || row.HasScanWarning || monitoringWarning || row.Trickplay != null;
        if (!library.Enabled) panel.Children.Add(Badge(T("web.admin.libraries.list.disabled"), BadgeTone.Destructive));
        if (hasScans)
        {
            var parts = new List<string>();
            if (row.RunningScans > 0) parts.Add(T("web.admin.libraries.scans.running", AdminLibrariesText.Count(row.RunningScans)));
            if (row.QueuedScans > 0) parts.Add(T("web.admin.libraries.scans.queued", AdminLibrariesText.Count(row.QueuedScans)));
            var text = string.Join(" · ", parts);
            // Visible text, then (as the official screen-reader text) where it leads.
            panel.Children.Add(ActivityPill(text, text + " · " + T("web.admin.libraries.list.viewScansFor", ("name", library.Name)), row.RunningScans > 0 ? "success" : "secondary",
                dot: row.RunningScans > 0, icon: null, "LibrariesScanPill_" + library.Id, () => { if (owner.IsCurrent) OpenActivityFor(library.Id); },
                tooltip: T("web.admin.libraries.list.viewScansFor", ("name", library.Name))));
        }
        if (row.ActiveRefresh is { } job)
        {
            var running = P.JobStatus(job) == "running";
            var label = running ? T("web.admin.libraries.activity.refreshingMetadata") : T("web.admin.libraries.activeWork.refreshQueued");
            panel.Children.Add(ActivityPill(label, label + " · " + library.Name, running ? "primary" : "secondary", dot: false, "database-backup", "LibrariesRefreshPill_" + library.Id,
                () => { if (owner.IsCurrent) OpenActivityFor(library.Id); }));
        }
        if (library.ScanWarningCode == "empty_root") panel.Children.Add(Badge(T("web.admin.libraries.list.emptyRootGuarded"), BadgeTone.Destructive));
        if (library.ScanWarningCode == "dead_root") panel.Children.Add(Badge(T("web.admin.libraries.list.rootUnreachable"), BadgeTone.Destructive));
        if (library.ScanWarningCode == "partial_walk") panel.Children.Add(Badge(T("web.admin.libraries.list.partialScan"), BadgeTone.Destructive));
        if (monitoringWarning)
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
        if (showIdle && !hasAnyPill)
        {
            var idle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            idle.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new(3), Background = Tint("SecondaryTextBrush", 0.4), VerticalAlignment = VerticalAlignment.Center });
            idle.Children.Add(Text(T("web.admin.libraries.list.idle"), 12, true));
            AutomationProperties.SetAutomationId(idle, "LibrariesIdle_" + library.Id);
            panel.Children.Add(idle);
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

    /// <summary>Scan or Stop as a labeled outline button; while scans run it is the destructive-tinted Stop (the accessible name says what stops).</summary>
    private Button ScanButton(AdminLibraryRow row, LibraryUiOwner owner)
    {
        var library = row.Library;
        var stops = row.ScanStops;
        var destructive = Solid(Destructive);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(stops ? Icon("square", 10, destructive, fill: true) : row.ScanStarting ? Spinning("refresh-cw", 12) : Icon("refresh-cw", 12));
        var label = Text(T(stops ? "web.admin.libraries.list.stopAction" : "web.admin.libraries.list.scanAction"), 12); label.FontWeight = FontWeights.Medium;
        if (stops) label.Foreground = destructive;
        content.Children.Add(label);
        var button = new Button
        {
            Content = content, Style = ButtonStyle("OutlineButtonStyle"), MinHeight = 28, Padding = new(10, 0, 10, 0), CornerRadius = new(RadiusMd), IsEnabled = !row.ScanDisabled,
        };
        if (stops) { button.Background = Solid(Destructive, 0.10); button.BorderBrush = Solid(Destructive, 0.35); button.Foreground = destructive; }
        AutomationProperties.SetName(button, row.ScanLabel);
        AutomationProperties.SetAutomationId(button, "LibrariesScan_" + library.Id);
        ToolTipService.SetToolTip(button, row.ScanLabel);
        button.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.ScanOrStopAsync(library.Id, stops, owner.Lease); };
        return button;
    }

    /// <summary>The Actions cell: Scan or Stop, Edit, and the labeled "More actions" menu (refresh metadata, verify mounts, cleanup, delete).</summary>
    private StackPanel RowActions(AdminLibraryRow row, LibraryUiOwner owner)
    {
        var library = row.Library;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; // gap-1.5
        actions.Children.Add(ScanButton(row, owner));
        var editLabel = Text(T("web.admin.libraries.list.editAction"), 12, true); editLabel.FontWeight = FontWeights.Medium;
        var edit = new Button
        {
            Content = editLabel, Style = (Style)Application.Current.Resources["GhostButtonStyle"], MinHeight = 28, Padding = new(10, 0, 10, 0), CornerRadius = new(RadiusMd),
        };
        AutomationProperties.SetName(edit, T("web.admin.libraries.list.edit", ("name", library.Name)));
        AutomationProperties.SetAutomationId(edit, "LibrariesEdit_" + library.Id);
        edit.Click += (_, _) => OpenEditor(library, owner);
        actions.Children.Add(edit);
        actions.Children.Add(MenuButton(row, owner, card: false));
        return actions;
    }

    private void RefreshOrStop(AdminLibraryRow row, LibraryUiOwner owner)
    {
        if (!owner.IsCurrent) return;
        if (row.ActiveRefresh is { } job) _ = owner.Controller.CancelJobAsync(job.Id, owner.Lease);
        else _ = ShowRefreshDialogAsync(row.Library, owner);
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
        grip.Opacity = 0.45;
        grip.GotFocus += (_, _) => grip.Opacity = 1;
        grip.LostFocus += (_, _) => grip.Opacity = 0.45;
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
        if (rows.Count == 0) return EmptyList();
        var owner = RenderOwner(state);
        var list = new StackPanel { Spacing = 12 };
        AutomationProperties.SetAutomationId(list, "LibrariesCards");
        foreach (var row in rows) list.Children.Add(Card(row, owner));
        return list;
    }

    private readonly HashSet<string> _expandedPaths = new(StringComparer.Ordinal);

    /// <summary>One library card below 1024 px (official <c>LibraryCard</c>): identity and menu, status, paths, last scanned, warning, and the full-width Scan or Stop.</summary>
    private Border Card(AdminLibraryRow row, LibraryUiOwner owner)
    {
        var library = row.Library;
        var stack = new StackPanel { Spacing = 12 };
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        head.Children.Add(TypeTile(library.Type, 36, RadiusXl));
        var info = new StackPanel { Spacing = 2 };
        var name = Text(library.Name, 16); name.FontWeight = FontWeights.Medium; name.MaxLines = 2; name.TextWrapping = TextWrapping.Wrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
        AutomationProperties.SetHeadingLevel(name, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        info.Children.Add(name);
        var meta = new LogsWrapPanel { Spacing = 6, RowSpacing = 0 };
        meta.Children.Add(Text(P.TypeLabel(library.Type), 12, true));
        var dot = Text("·", 12, true); AutomationProperties.SetAccessibilityView(dot, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        meta.Children.Add(dot);
        meta.Children.Add(FolderLink(library, owner, singlePathMono: false));
        info.Children.Add(meta);
        Grid.SetColumn(info, 1); head.Children.Add(info);
        var menu = MenuButton(row, owner, card: true);
        Grid.SetColumn(menu, 2); head.Children.Add(menu);
        stack.Children.Add(head);

        var badges = StatusBadges(row, owner, showIdle: true);
        if (library.Paths.Count == 0) badges.Children.Add(Badge(T("web.admin.libraries.list.folderCount", ("count", 0)), BadgeTone.Outline));
        stack.Children.Add(badges);
        stack.Children.Add(CardPaths(library, owner));

        var last = new LogsWrapPanel { Spacing = 6, RowSpacing = 2 };
        var label = Text(T("web.admin.libraries.column.lastScanned"), 12, true); label.FontWeight = FontWeights.Medium; label.VerticalAlignment = VerticalAlignment.Top;
        last.Children.Add(label);
        last.Children.Add(LastScanned(library));
        stack.Children.Add(last);
        if (row.HasScanWarning) stack.Children.Add(new Border { Child = ScanWarning(row, owner, touch: true), CornerRadius = new(RadiusXl), Padding = new(12, 8, 12, 8), Background = Solid(Destructive, 0.05) });

        // The action is what the button said when it was drawn; a state change in between does not turn a Stop into a Scan.
        var stops = row.ScanStops;
        // Official: a filled square (fill-current) for Stop.
        var scan = Action(row.ScanLabel, "LibrariesCardScan_" + library.Id, stops ? "square" : "refresh-cw", stops ? "DestructiveButtonStyle" : "OutlineButtonStyle",
            null, 44, gap: 8, fillIcon: stops);
        scan.HorizontalAlignment = HorizontalAlignment.Stretch;
        scan.IsEnabled = !row.ScanDisabled;
        scan.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.ScanOrStopAsync(library.Id, stops, owner.Lease); };
        stack.Children.Add(scan);
        var card = Surface(stack, Radius2Xl, 16);
        AutomationProperties.SetAutomationId(card, "LibrariesCard_" + library.Id);
        return card;
    }

    /// <summary>One path inline, or several behind an expander; paths wrap instead of scrolling (official <c>LibraryCardPaths</c>).</summary>
    private FrameworkElement CardPaths(AdminLibrary library, LibraryUiOwner owner)
    {
        var list = new StackPanel { Spacing = 4 };
        foreach (var path in library.Paths) { var text = Mono(AdminPathText.Isolate(path)); text.TextWrapping = TextWrapping.WrapWholeWords; text.IsTextSelectionEnabled = true; list.Children.Add(text); }
        if (library.Paths.Count <= 1) return list;
        var expanded = _expandedPaths.Contains(library.Id);
        var stack = new StackPanel { Spacing = 4 };
        // Padding with an equal negative margin and a 4 px radius: no text sits in a zero-padding rounded button, and the layout does not move.
        var toggle = new HyperlinkButton { MinHeight = 44, Padding = new(4, 0, 4, 0), Margin = new(-4, 0, -4, 0), CornerRadius = new(4) };
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

    /// <summary>The "More actions for …" button of a table row (28 px) or a card (44 px, offset into the corner), and its flyout (official <c>LibraryActionMenu</c>).</summary>
    private Button MenuButton(AdminLibraryRow row, LibraryUiOwner owner, bool card)
    {
        var library = row.Library;
        var button = IconButton("ellipsis", T("web.admin.libraries.list.moreActions", ("name", library.Name)), "LibrariesMenu_" + library.Id, card ? 44 : 28, 16, Brush("SecondaryTextBrush"));
        button.CornerRadius = new(RadiusMd);
        if (card)
        {
            button.VerticalAlignment = VerticalAlignment.Top;
            button.Margin = new(0, -8, -8, 0);
        }
        var host = new StackPanel { Width = 256, Spacing = 0 };
        var flyout = new Flyout { Content = host, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight, ShouldConstrainToRootBounds = true };
        flyout.FlyoutPresenterStyle = RoundedPresenterStyle(RadiusMd);
        flyout.Opened += (_, _) => { if (!owner.IsCurrent) { flyout.Hide(); return; } _openMenus.Add(flyout); _cardMenu = (library.Id, flyout, host, owner, card); FillCardMenu(host, owner.Controller.State, library.Id, owner, card); };
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
        if (_cardMenu is { } menu && menu.Owner.IsCurrent) FillCardMenu(menu.Host, state, menu.LibraryId, menu.Owner, menu.Card);
    }

    /// <summary>
    /// The library's action menu. Refresh metadata (or Stop refresh) and Verify mounts come first; a card adds Edit and Move up/down. Cleanup and Delete follow a
    /// divider. Verify mounts and Move up/down keep the menu open (the mount result shows under the label, a library can move several places).
    /// </summary>
    private void FillCardMenu(StackPanel host, AdminLibrariesSnapshot state, string libraryId, LibraryUiOwner owner, bool card)
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
            if (icon is FrameworkElement iconElement) iconElement.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(icon);
            var text = new StackPanel();
            var title = Text(label); title.FontWeight = FontWeights.Medium; if (destructive) title.Foreground = Solid(Destructive);
            text.Children.Add(title);
            if (detail != null) { var description = Text(detail, 12, true); description.TextWrapping = TextWrapping.Wrap; text.Children.Add(description); }
            Grid.SetColumn(text, 1); grid.Children.Add(text);
            var item = new Button
            {
                Content = grid, MinHeight = card ? 44 : 32, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new(8, 6, 8, 6), CornerRadius = new(RadiusSm), Style = (Style)Application.Current.Resources["GhostButtonStyle"], IsEnabled = enabled,
            };
            AutomationProperties.SetName(item, label);
            AutomationProperties.SetAutomationId(item, id + "_" + libraryId);
            item.Click += (_, _) => { if (owner.IsCurrent) click(); };
            host.Children.Add(item);
            return item;
        }
        Item("LibrariesMenuRefresh", Icon(row.RefreshStops ? "square" : "database-backup", 16, row.RefreshStops ? Solid(Destructive) : null, row.RefreshStops), row.RefreshLabel,
            row.RefreshStops ? null : T("web.admin.libraries.list.refreshMetadataDescription"), !row.RefreshDisabled, row.RefreshStops, () => { _cardMenu?.Flyout.Hide(); RefreshOrStop(row, owner); });
        Item("LibrariesMenuMount", MountIcon(row, 16), T("web.admin.libraries.list.verifyMounts"), row.MountMessage ?? T("web.admin.libraries.list.verifyMountsDescription"), !row.MountChecking, false,
            () => { _ = owner.Controller.CheckMountAsync(libraryId, owner.Lease); });
        if (card)
        {
            Item("LibrariesMenuEdit", Icon("pencil", 16), T("web.admin.libraries.list.edit", ("name", row.Library.Name)), null, true, false,
                () => { _cardMenu?.Flyout.Hide(); OpenEditor(row.Library, owner); });
            Item("LibrariesMenuUp", Icon("arrow-up", 16), T("web.admin.libraries.list.moveUp"), null, !moving && index.i > 0, false,
                () => { _ = owner.Controller.MoveAsync(libraryId, -1, owner.Lease); });
            Item("LibrariesMenuDown", Icon("arrow-down", 16), T("web.admin.libraries.list.moveDown"), null, !moving && index.i < rows.Count - 1, false,
                () => { _ = owner.Controller.MoveAsync(libraryId, 1, owner.Lease); });
        }
        host.Children.Add(new Border { Height = 1, Margin = new(-4, 4, -4, 4), Background = Brush("BorderBrush") });
        if (row.CanCleanup)
            Item("LibrariesMenuCleanup", Icon("trash-2", 16, Solid(Destructive)), T("web.admin.libraries.warning.confirmCleanup"), null, !row.CleanupPending, true,
                () => { _cardMenu?.Flyout.Hide(); _ = ConfirmCleanupAsync(row.Library, owner); });
        Item("LibrariesMenuDelete", Icon("trash-2", 16, Solid(Destructive)), T("web.admin.libraries.list.delete", ("name", row.Library.Name)), null, true, true,
            () => { _cardMenu?.Flyout.Hide(); _ = ConfirmDeleteAsync(row.Library, owner); });
        if (focusId.Length > 0) EnqueueUi(() => FindControl(host, focusId)?.Focus(FocusState.Keyboard));
    }
}
