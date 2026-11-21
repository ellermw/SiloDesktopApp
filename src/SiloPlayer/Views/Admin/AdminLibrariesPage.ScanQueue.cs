using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AdminLibrariesUi;
using P = SiloPlayer.Core.Services.Admin.AdminLibraryPresentation;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The header's active scan queue (WebUI <c>ScanQueuePopover</c>): a trigger with the scan
/// count and a 400 px popover listing each library's running and queued scans with a
/// Cancel per library; four rows per group show before the rest collapse.
/// </summary>
public sealed partial class AdminLibrariesPage
{
    private Flyout? _scanQueueFlyout;
    private StackPanel? _scanQueueHost;
    private LibraryUiOwner? _scanQueueOwner;
    private readonly HashSet<string> _expandedQueueGroups = new(StringComparer.Ordinal);

    private Button ScanQueueButton(AdminLibrariesSnapshot state, IReadOnlyList<AdminScanQueueGroup> groups)
    {
        var owner = RenderOwner(state);
        var total = groups.Sum(group => group.Scans.Count);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new(4), Background = Solid(Success), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = T("web.admin.libraries.scanQueue.trigger", AdminLibrariesText.Count(total)), FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        var button = Action(T("web.admin.libraries.scanQueue.trigger", AdminLibrariesText.Count(total)), "LibrariesScanQueue");
        button.Content = content;
        var host = new StackPanel { Width = 400 };
        var flyout = new Flyout { Content = host, Placement = FlyoutPlacementMode.BottomEdgeAlignedRight, ShouldConstrainToRootBounds = true };
        button.Tag = flyout;
        flyout.FlyoutPresenterStyle = RoundedPresenterStyle(RadiusXl); // popover.tsx: rounded-xl (16), no override at AdminLibraries.tsx:672
        flyout.FlyoutPresenterStyle.Setters.Add(new Setter(FlyoutPresenter.PaddingProperty, new Thickness(0)));
        flyout.FlyoutPresenterStyle.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty, 400d));
        flyout.Opened += (_, _) => { if (!owner.IsCurrent) { flyout.Hide(); return; } _scanQueueOwner = owner; _scanQueueFlyout = flyout; _scanQueueHost = host; FillScanQueue(host, owner.Controller.State); };
        flyout.Closed += (_, _) => { if (ReferenceEquals(_scanQueueFlyout, flyout)) { _scanQueueFlyout = null; _scanQueueHost = null; _scanQueueOwner = null; } };
        button.Click += (_, _) => { if (owner.IsCurrent) flyout.ShowAt(button); };
        return button;
    }

    private void UpdateScanQueueFlyout(AdminLibrariesSnapshot state)
    {
        if (_scanQueueHost == null || _scanQueueFlyout == null) return;
        if (_scanQueueOwner?.IsCurrent != true || state.Scans.Count == 0) { _scanQueueFlyout.Hide(); return; }
        if (ReferenceEquals(_scanQueueOwner?.Lease, state.Owner)) FillScanQueue(_scanQueueHost, state);
    }

    private void FillScanQueue(StackPanel host, AdminLibrariesSnapshot state)
    {
        var groups = AdminLibrariesController.ScanGroups(state);
        var focusId = Focused(XamlRoot) is { } focused && IsDescendant(focused, host)
            ? AutomationProperties.GetAutomationId(focused) : "";
        host.Children.Clear();
        var running = groups.Sum(group => group.Running);
        var queued = groups.Sum(group => group.Queued);
        host.Children.Add(new Border { Height = 1, Background = Solid(Primary, 0.6) });
        var header = new Grid { Padding = new(16, 12, 16, 12), BorderBrush = Tint("BorderBrush", 0.4), BorderThickness = new(0, 0, 0, 1) };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(Spinning("refresh-cw", 14, Solid(Primary), 3));
        var heading = Text(T("web.admin.libraries.scanQueue.title"), 14); heading.FontWeight = FontWeights.SemiBold;
        title.Children.Add(heading);
        header.Children.Add(title);
        var counts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (running > 0)
        {
            counts.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new(3), Background = Solid(Success), VerticalAlignment = VerticalAlignment.Center });
            counts.Children.Add(Text(T("web.admin.libraries.scans.running", AdminLibrariesText.Count(running)), 11, true));
        }
        if (running > 0 && queued > 0) counts.Children.Add(Text("·", 11, true));
        if (queued > 0)
        {
            counts.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new(3), Background = Tint("SecondaryTextBrush", 0.4), VerticalAlignment = VerticalAlignment.Center });
            counts.Children.Add(Text(T("web.admin.libraries.scans.queued", AdminLibrariesText.Count(queued)), 11, true));
        }
        Grid.SetColumn(counts, 1); header.Children.Add(counts);
        host.Children.Add(header);

        var list = new StackPanel { Padding = new(12, 8, 12, 8) };
        for (var index = 0; index < groups.Count; index++)
        {
            list.Children.Add(ScanQueueGroup(groups[index], state));
            if (index < groups.Count - 1) list.Children.Add(new Border { Height = 1, Margin = new(0, 4, 0, 4), Background = Tint("BorderBrush", 0.2) });
        }
        host.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        AutomationProperties.SetAutomationId(host, "LibrariesScanQueuePopover");
        if (focusId.Length > 0) EnqueueUi(() => FindControl(host, focusId)?.Focus(FocusState.Keyboard));
    }

    private StackPanel ScanQueueGroup(AdminScanQueueGroup group, AdminLibrariesSnapshot state)
    {
        var owner = RenderOwner(state);
        const int collapsedLimit = 4;
        var stack = new StackPanel();
        var head = new Grid { ColumnSpacing = 8, Padding = new(0, 6, 0, 6) };
        head.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var name = Text(Core.Localization.AppStrings.Uppercase(group.Name), 10, true); name.FontWeight = FontWeights.SemiBold; name.CharacterSpacing = 100; // tracking-widest
        head.Children.Add(name);
        var rule = new Border { Height = 1, Background = Tint("BorderBrush", 0.25), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(rule, 1); head.Children.Add(rule);
        var cancel = Action(Core.Localization.AppStrings.Get("common.cancel", fallback: "Cancel"), "LibrariesQueueCancel_" + group.LibraryId, "square", "GhostButtonStyle", height: 24, iconSize: 10, gap: 4, fontSize: 10);
        cancel.Padding = new(8, 0, 8, 0);
        cancel.IsEnabled = !state.ScanCancelling.Contains(group.LibraryId);
        cancel.Click += (_, _) => { if (owner.IsCurrent) _ = owner.Controller.CancelScansAsync(group.LibraryId, owner.Lease); };
        Grid.SetColumn(cancel, 2); head.Children.Add(cancel);
        stack.Children.Add(head);

        var expanded = _expandedQueueGroups.Contains(group.LibraryId);
        var visible = expanded ? group.Scans : group.Scans.Take(collapsedLimit).ToArray();
        foreach (var scan in visible) stack.Children.Add(ScanQueueRow(scan));
        if (group.Scans.Count > collapsedLimit)
        {
            var hidden = group.Scans.Count - visible.Count;
            var toggle = new HyperlinkButton { Padding = new(10, 6, 10, 6) };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            content.Children.Add(Text(expanded ? T("web.admin.libraries.scanQueue.showLess")
                : T(hidden <= group.Queued ? "web.admin.libraries.scanQueue.moreQueued" : "web.admin.libraries.scanQueue.more", AdminLibrariesText.Count(hidden)), 10, true));
            content.Children.Add(Icon(expanded ? "chevron-up" : "chevron-down", 12, Brush("SecondaryTextBrush")));
            toggle.Content = content;
            AutomationProperties.SetAutomationId(toggle, "LibrariesQueueToggle_" + group.LibraryId);
            toggle.Click += (_, _) =>
            {
                if (!owner.IsCurrent) return;
                if (!_expandedQueueGroups.Remove(group.LibraryId)) _expandedQueueGroups.Add(group.LibraryId);
                if (_scanQueueHost != null && _controller != null) FillScanQueue(_scanQueueHost, _controller.State);
            };
            stack.Children.Add(toggle);
        }
        return stack;
    }

    private static Border ScanQueueRow(AdminLibraryScanRun scan)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var dot = scan.IsRunning
            ? new Border { Width = 7, Height = 7, CornerRadius = new(3.5), Background = Solid(Success) }
            : new Border { Width = 5, Height = 5, CornerRadius = new(2.5), Background = Tint("SecondaryTextBrush", 0.2), BorderBrush = Tint("SecondaryTextBrush", 0.15), BorderThickness = new(1.5) };
        dot.HorizontalAlignment = HorizontalAlignment.Center; dot.VerticalAlignment = VerticalAlignment.Center;
        // WebUI: a 10 × 10 box (mt-[5px]) holds the dot centered, so the text column starts 10 px + gap-2.5 in.
        var dotBox = new Grid { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 5, 0, 0) };
        dotBox.Children.Add(dot);
        grid.Children.Add(dotBox);
        var details = new StackPanel { Spacing = 1 };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var mode = Text(P.ScanMode(scan.Mode), 12); mode.FontWeight = FontWeights.Medium;
        title.Children.Add(mode);
        if (!string.IsNullOrEmpty(scan.Trigger))
            title.Children.Add(new Border { Child = Text(P.ScanTrigger(scan.Trigger), 9, true), Background = Tint("MutedBrush", 0.5), CornerRadius = new(4), Padding = new(4, 1, 4, 1), VerticalAlignment = VerticalAlignment.Center });
        details.Children.Add(title);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var target = Mono(P.ScanTarget(scan.Path), 10); target.MaxWidth = 200; target.TextTrimming = TextTrimming.CharacterEllipsis; target.TextWrapping = TextWrapping.NoWrap;
        if (scan.Path != null) ToolTipService.SetToolTip(target, scan.Path);
        meta.Children.Add(target);
        meta.Children.Add(Text("·", 10, true));
        meta.Children.Add(Text(scan.IsRunning ? P.ScanStarted(scan.StartedAt, DateTimeOffset.UtcNow) : T("web.admin.libraries.scanQueue.waiting"), 10, true));
        details.Children.Add(meta);
        var progress = P.ScanProgress(scan);
        if (progress.Length > 0) { var text = Text(progress, 10, true); text.TextTrimming = TextTrimming.CharacterEllipsis; text.TextWrapping = TextWrapping.NoWrap; details.Children.Add(text); }
        Grid.SetColumn(details, 1); grid.Children.Add(details);
        var row = new Border { Child = grid, Padding = new(10, 8, 10, 8), CornerRadius = new(RadiusLg), Background = scan.IsRunning ? Solid(Primary, 0.04) : null };
        AutomationProperties.SetAutomationId(row, "LibrariesQueueScan_" + scan.Id);
        return row;
    }
}
