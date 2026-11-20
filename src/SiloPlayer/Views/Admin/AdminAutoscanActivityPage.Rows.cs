using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AutoscanUi;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using A = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityText;
using D = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityDisplay;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminAutoscanActivityPage
{
    private static readonly double[] QueueWidths = [0.9, 1.6, 2.6, 2.2, 0.6];
    private static readonly double[] ScanWidths = [0.9, 1.2, 1.1, 2.4, 1.5, 1.0, 0.9];
    private static readonly double[] PollWidths = [0.9, 1.5, 2.0, 1.6, 0.8, 1.0];

    private void Closed()
    {
        _debounce.Stop();
        if (_sheet.Visibility == Visibility.Visible) _sheet.Close();
        _inspector = null;
        _notices.Children.Clear(); _live.Children.Clear(); _rows.Children.Clear();
        _renderedItems = null; _renderedHistory = null; _renderedLabels = null; _renderedSignature = null;
        RenderedIds = []; RenderedContent = "closed";
        _search.IsEnabled = _status.IsEnabled = _reset.IsEnabled = false;
    }

    private static TextBlock Dim(string text, double size = 12) => SafeText(text, size, true);

    private static FrameworkElement Tile(string label, string value, string? icon, double valueSize = 24)
    {
        var tile = Stack(4);
        var heading = Inline(8);
        if (icon != null) heading.Children.Add(Glyph(icon, 14, Brush("SecondaryTextBrush")));
        heading.Children.Add(Text(label, 12, true));
        tile.Children.Add(heading);
        var number = Text(value, valueSize); number.FontWeight = FontWeights.SemiBold; number.LineHeight = valueSize * 1.25; tile.Children.Add(number);
        return tile;
    }

    private void RenderLive(AdminAutoscanActivityState state)
    {
        var focus = CaptureFocus(_live);
        _live.Children.Clear();
        var status = state.Status;
        // Stat tiles: Active, Queued, Running scans, Polling, Latest poll plus the refresh button.
        var tiles = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        var columns = _phone ? 1 : 5;
        for (var index = 0; index < columns; index++) tiles.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var index = 0; index < (_phone ? 5 : 1); index++) tiles.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var refresh = IconButton("refresh-cw", A.T("refreshLabel"), "AutoscanActivityRefresh", () => _ = _controller?.ReloadHistoryAsync(), !state.Refreshing);
        var latest = Stack(8); latest.Children.Add(Tile(A.T("stats.latestPoll"), ClockTime(status?.LatestEventAt), "clock", 24)); refresh.HorizontalAlignment = HorizontalAlignment.Left; latest.Children.Add(refresh);
        var values = new FrameworkElement[]
        {
            Tile(A.T("stats.active"), (status?.ActiveScans ?? 0).ToString(), "scan-line"),
            Tile(A.T("stats.queued"), (status?.AcceptedScans ?? 0).ToString(), null),
            Tile(A.T("stats.runningScans"), (status?.RunningScans ?? 0).ToString(), null),
            Tile(A.T("stats.polling"), (status?.RunningPolls?.Count ?? 0).ToString(), "refresh-cw"),
            latest,
        };
        for (var index = 0; index < values.Length; index++) { Grid.SetColumn(values[index], index % columns); Grid.SetRow(values[index], index / columns); tiles.Children.Add(values[index]); }
        _live.Children.Add(Card(tiles));
        if (state.StatusFailure != null)
        {
            var failed = Stack(8); failed.Children.Add(SafeText(A.T("native.statusFailed"), 13));
            var retry = Button(A.T("common.tryAgain"), "AutoscanActivityStatusRetry"); retry.Click += (_, _) => _ = _controller?.RefreshAsync();
            failed.Children.Add(retry); _live.Children.Add(Card(failed));
        }
        if (status?.RunningPolls is { Count: > 0 } polls) _live.Children.Add(RunningPolls(polls, state));
        _live.Children.Add(Queue(state, status));
        RestoreFocus(_live, focus);
    }

    private FrameworkElement SectionTitle(string title, string description, string icon, string? badge, bool spin = false)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var heading = Stack(4); var line = Inline(8); line.Children.Add(Glyph(icon, 16, Brush("AccentBrush")));
        var label = Text(title, 14); label.FontWeight = FontWeights.SemiBold; line.Children.Add(label); heading.Children.Add(line); heading.Children.Add(Dim(description));
        grid.Children.Add(heading);
        if (badge != null) { var chip = ToneBadge(badge, AutoscanUi.Muted, secondary: true); chip.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(chip, 1); grid.Children.Add(chip); }
        return grid;
    }

    private FrameworkElement RunningPolls(IReadOnlyList<AdminAutoscanActivityPoll> polls, AdminAutoscanActivityState state)
    {
        var section = Stack(12);
        section.Children.Add(SectionTitle(A.T("polls.title"), A.T("polls.description"), "refresh-cw", A.RawCount("activeCount", polls.Count)));
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        var columns = _compact ? 1 : 2;
        for (var index = 0; index < columns; index++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var index = 0; index < polls.Count; index++)
        {
            var poll = polls[index];
            if (index % columns == 0) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var card = Stack(12);
            var top = new Grid { ColumnSpacing = 12 }; top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var names = Stack(4); var name = SafeText(A.Source(poll.SourceId, poll.PluginId, poll.CapabilityId, state.Labels, true), 14); name.FontWeight = FontWeights.Medium; names.Children.Add(name);
            names.Children.Add(Dim(poll.PluginId + D.Separator + poll.CapabilityId)); top.Children.Add(names);
            var badge = PollStatusBadge("running"); Grid.SetColumn(badge, 1); top.Children.Add(badge);
            card.Children.Add(top);
            var meta = new WrapPanel { HorizontalSpacing = 16, VerticalSpacing = 4 };
            meta.Children.Add(Dim(A.T("polls.started", new { time = Timestamp(poll.StartedAt) })));
            meta.Children.Add(Dim(A.T("polls.elapsed", new { duration = A.Duration(poll.ElapsedMs) })));
            card.Children.Add(meta);
            var frame = Card(card); AutomationProperties.SetAutomationId(frame, "AutoscanActivityRunningPoll_" + poll.Id);
            Grid.SetColumn(frame, index % columns); Grid.SetRow(frame, index / columns); grid.Children.Add(frame);
        }
        section.Children.Add(grid);
        AutomationProperties.SetLiveSetting(section, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        return section;
    }

    private FrameworkElement Queue(AdminAutoscanActivityState state, AdminAutoscanActivityStatus? status)
    {
        var scans = state.Queue;
        if (scans.Count == 0)
        {
            var empty = Stack(8); var title = Inline(8); title.Children.Add(Glyph("scan-line", 16, Brush("SecondaryTextBrush")));
            var label = Text(A.T("queue.title"), 14); label.FontWeight = FontWeights.Medium; title.Children.Add(label); empty.Children.Add(title);
            empty.Children.Add(Dim(A.T((status?.ActiveScans ?? 0) > 0 ? "queue.pendingDetails" : "queue.empty"), 14));
            return Dashed(empty);
        }
        var section = Stack(12);
        section.Children.Add(SectionTitle(A.T("queue.title"), A.T("queue.description"), "scan-line", A.RawCount("activeCount", scans.Count)));
        var slice = D.Slice(scans, _queuePage, D.QueuePageSize); _queuePage = slice.Page;
        if (_compact) foreach (var scan in slice.Rows) section.Children.Add(QueueCard(scan, state));
        else
        {
            var table = new DataTable([A.T("column.status"), A.T("column.library"), A.T("column.scope"), A.T("column.progress"), A.T("column.action")], QueueWidths);
            foreach (var scan in slice.Rows)
            {
                var progress = AdminLibraryPresentation.ScanProgress(scan);
                var library = Stack(4); var name = SafeText(A.Library(scan.LibraryId, state.Labels), 14); name.FontWeight = FontWeights.Medium; library.Children.Add(name); library.Children.Add(Dim(QueueStarted(scan)));
                var scope = Stack(4); scope.Children.Add(SafeText(A.Mode(scan.Mode), 13)); scope.Children.Add(SafeText(string.IsNullOrEmpty(scan.Path) ? A.T("entireLibrary") : scan.Path, 12, true, true));
                var detail = Dim(progress.Length > 0 ? progress : AdminLibraryPresentation.ScanTrigger(scan.Trigger ?? ""));
                var cancel = CancelButton(scan, state); cancel.HorizontalAlignment = HorizontalAlignment.Right;
                var row = table.AddRow([ScanStatusBadge(scan.Status), library, scope, detail, cancel]);
                AutomationProperties.SetAutomationId(row, "AutoscanActivityQueue_" + scan.Id);
            }
            section.Children.Add(table.Root);
        }
        if (scans.Count > D.QueuePageSize)
            section.Children.Add(Pagination("AutoscanQueuePager", scans.Count, _queuePage, D.QueuePageSize, page => { _queuePage = page; RerenderLive(); }, null, [],
                "web.admin.common.pagination.scans", phone: _phone));
        if (state.QueueCapped) section.Children.Add(Dim(A.T("native.livePartial")));
        return section;
    }

    private static string QueueStarted(AdminLibraryScanRun scan) =>
        scan.IsRunning ? AdminLibraryPresentation.ScanStarted(scan.StartedAt, DateTimeOffset.UtcNow) : A.T("queue.waiting");

    private Button CancelButton(AdminLibraryScanRun scan, AdminAutoscanActivityState state)
    {
        var libraryId = scan.LibraryId;
        return IconButton("square", A.T("queue.cancelLabel"), "AutoscanActivityCancel_" + scan.Id, () => _ = _controller?.CancelAsync(libraryId),
            state.Cancelling == null && !state.CancelReview.ContainsKey(scan.LibraryId), ghost: true, size: 32, tint: Destructive);
    }

    private FrameworkElement QueueCard(AdminLibraryScanRun scan, AdminAutoscanActivityState state)
    {
        var card = Stack(8);
        var top = new Grid { ColumnSpacing = 12 }; top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var identity = Stack(4); var line = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 }; line.Children.Add(ScanStatusBadge(scan.Status));
        var name = SafeText(A.Library(scan.LibraryId, state.Labels), 14); name.FontWeight = FontWeights.Medium; line.Children.Add(name); identity.Children.Add(line);
        var meta = new WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 2 };
        meta.Children.Add(Dim(A.Mode(scan.Mode))); meta.Children.Add(Dim(AdminLibraryPresentation.ScanTrigger(scan.Trigger ?? ""))); meta.Children.Add(Dim(QueueStarted(scan)));
        identity.Children.Add(meta); top.Children.Add(identity);
        var cancel = CancelButton(scan, state); Grid.SetColumn(cancel, 1); top.Children.Add(cancel);
        card.Children.Add(top);
        card.Children.Add(SafeText(string.IsNullOrEmpty(scan.Path) ? A.T("entireLibrary") : scan.Path, 12, true, true));
        var progress = AdminLibraryPresentation.ScanProgress(scan);
        if (progress.Length > 0) card.Children.Add(Dim(progress));
        var frame = Card(card); AutomationProperties.SetAutomationId(frame, "AutoscanActivityQueue_" + scan.Id); return frame;
    }

    // ---- Scan history -------------------------------------------------------------------------------------------------

    private void AddScanRows(AdminAutoscanActivityState state)
    {
        if (_compact)
        {
            foreach (var scan in state.Scans.Items) _rows.Children.Add(ScanCard(scan, state.Labels));
            return;
        }
        var table = new DataTable([A.T("column.status"), A.T("column.library"), A.T("column.source"), A.T("column.scope"), A.T("column.result"), A.T("column.time"), A.T("column.poll")], ScanWidths);
        foreach (var scan in state.Scans.Items)
        {
            var library = Stack(4); var name = SafeText(A.Library(scan.LibraryId, state.Labels), 14); name.FontWeight = FontWeights.Medium; library.Children.Add(name);
            library.Children.Add(SafeText(scan.Id, 11, true, true));
            var scope = Stack(4); scope.Children.Add(SafeText(A.Mode(scan.Mode), 13));
            scope.Children.Add(SafeText(string.IsNullOrEmpty(scan.Path) ? A.T("entireLibrary") : scan.Path, 12, true, true));
            if (!string.IsNullOrEmpty(scan.ErrorMessage)) scope.Children.Add(Toned(SafeText(scan.ErrorMessage, 12), AdminAutoscanTone.Danger));
            FrameworkElement result = scan.Result != null ? ResultText(scan.Result) : Dim(D.NoValue, 14);
            FrameworkElement poll = string.IsNullOrEmpty(scan.EventStatus) ? Dim(D.NoValue, 14) : PollStatusBadge(scan.EventStatus);
            var row = table.AddRow([ScanStatusBadge(scan.Status), library, SafeText(A.Source(scan.SourceId, scan.PluginId, scan.CapabilityId, state.Labels), 13), scope, result,
                Dim(Timestamp(scan.CompletedAt ?? scan.StartedAt ?? scan.RequestedAt)), poll]);
            AutomationProperties.SetAutomationId(row, "AutoscanActivityScan_" + scan.Id);
        }
        _rows.Children.Add(table.Root);
    }

    private FrameworkElement ScanCard(AdminAutoscanActivityScan scan, AdminAutoscanActivityLabels labels)
    {
        var card = Stack(8);
        var top = new Grid { ColumnSpacing = 12 }; top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var identity = Stack(4); var line = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 }; line.Children.Add(ScanStatusBadge(scan.Status));
        var name = SafeText(A.Library(scan.LibraryId, labels), 14); name.FontWeight = FontWeights.Medium; line.Children.Add(name); identity.Children.Add(line);
        var meta = new WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 2 };
        meta.Children.Add(Dim(A.Mode(scan.Mode))); meta.Children.Add(Dim(A.Source(scan.SourceId, scan.PluginId, scan.CapabilityId, labels)));
        meta.Children.Add(Dim(Timestamp(scan.CompletedAt ?? scan.StartedAt ?? scan.RequestedAt))); identity.Children.Add(meta); top.Children.Add(identity);
        if (!string.IsNullOrEmpty(scan.EventStatus)) { var badge = PollStatusBadge(scan.EventStatus); Grid.SetColumn(badge, 1); top.Children.Add(badge); }
        card.Children.Add(top);
        card.Children.Add(SafeText(string.IsNullOrEmpty(scan.Path) ? A.T("entireLibrary") : scan.Path, 12, true, true));
        if (scan.Result != null) card.Children.Add(ResultText(scan.Result));
        if (!string.IsNullOrEmpty(scan.ErrorMessage)) card.Children.Add(Toned(SafeText(scan.ErrorMessage, 12), AdminAutoscanTone.Danger));
        card.Children.Add(SafeText(scan.Id, 11, true, true));
        var frame = Card(card); AutomationProperties.SetAutomationId(frame, "AutoscanActivityScan_" + scan.Id); return frame;
    }

    // ---- Poll log -----------------------------------------------------------------------------------------------------

    private Button DetailsButton(AdminAutoscanActivityEvent poll)
    {
        var summary = D.DetailsSummary(poll); var active = D.HasActivity(poll);
        var content = new Grid { ColumnSpacing = 8 }; content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = Text(summary, 12, !active); label.FontWeight = active ? FontWeights.Medium : FontWeights.Normal; label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; content.Children.Add(label);
        var chevron = (FrameworkElement)Glyph("chevron-right", 14, Brush("SecondaryTextBrush")); Grid.SetColumn(chevron, 1); content.Children.Add(chevron);
        var button = Button("", "AutoscanActivityDetails_" + poll.Id, "GhostButtonStyle");
        button.Content = content; button.Padding = new(8, 4, 8, 4); button.MinHeight = 28; button.HorizontalAlignment = HorizontalAlignment.Left; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(button, summary);
        button.Click += (_, _) => OpenInspector(poll, button);
        return button;
    }

    private void AddPollRows(AdminAutoscanActivityState state)
    {
        var now = DateTimeOffset.UtcNow;
        if (_compact)
        {
            foreach (var poll in state.Polls.Items) _rows.Children.Add(PollCard(poll, state.Labels, now));
            return;
        }
        var table = new DataTable([A.T("column.status"), A.T("column.source"), A.T("column.counts"), A.T("column.details"), A.T("column.duration"), A.T("column.time")], PollWidths);
        foreach (var poll in state.Polls.Items)
        {
            var source = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 4 };
            var name = SafeText(PollName(poll, state.Labels), 14); name.FontWeight = FontWeights.Medium; source.Children.Add(name);
            if (DeliveryBadge(poll) is { } delivery) source.Children.Add(delivery);
            var counts = Stack(4); counts.Children.Add(MetricStrip(poll, pills: false));
            if (!string.IsNullOrEmpty(poll.ErrorMessage))
            {
                var error = Toned(SafeText(poll.ErrorMessage, 12), AdminAutoscanTone.Danger); error.TextWrapping = TextWrapping.NoWrap; error.TextTrimming = TextTrimming.CharacterEllipsis; error.MaxWidth = 448;
                ToolTipService.SetToolTip(error, A.Safe(poll.ErrorMessage)); counts.Children.Add(error);
            }
            var details = DetailsButton(poll);
            var row = table.AddRow([PollStatusBadge(poll.Status), source, counts, details, Dim(D.EventDuration(poll, now), 13), Dim(Timestamp(D.EventTime(poll)), 13)]);
            AutomationProperties.SetAutomationId(row, "AutoscanActivityPoll_" + poll.Id);
            var selected = _inspector?.Id == poll.Id; var baseBrush = selected ? Tint("MutedBrush", 0.4) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            row.Background = baseBrush;
            row.PointerEntered += (_, _) => row.Background = Brush("SurfaceHoverBrush");
            row.PointerExited += (_, _) => row.Background = baseBrush;
            // A row click opens the inspector like its Details button; clicks on a button, link or field act on that control instead.
            row.Tapped += (_, args) =>
            {
                if (HasAncestor<ButtonBase>(args.OriginalSource as DependencyObject)) return;
                OpenInspector(poll, details);
            };
        }
        _rows.Children.Add(table.Root);
    }

    private FrameworkElement PollCard(AdminAutoscanActivityEvent poll, AdminAutoscanActivityLabels labels, DateTimeOffset now)
    {
        var selected = _inspector?.Id == poll.Id;
        var card = Stack(12);
        var top = new Grid { ColumnSpacing = 12 }; top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var identity = Stack(6); var line = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 }; line.Children.Add(PollStatusBadge(poll.Status));
        var name = SafeText(PollName(poll, labels), 14); name.FontWeight = FontWeights.Medium; line.Children.Add(name);
        if (DeliveryBadge(poll) is { } delivery) line.Children.Add(delivery);
        identity.Children.Add(line); identity.Children.Add(MetricStrip(poll, pills: false)); top.Children.Add(identity);
        var when = Stack(0); when.HorizontalAlignment = HorizontalAlignment.Right;
        when.Children.Add(Dim(Timestamp(D.EventTime(poll)))); when.Children.Add(Dim(D.EventDuration(poll, now)));
        Grid.SetColumn(when, 1); top.Children.Add(when);
        card.Children.Add(top);
        if (!string.IsNullOrEmpty(poll.ErrorMessage))
        {
            var error = Toned(SafeText(poll.ErrorMessage, 12), AdminAutoscanTone.Danger); error.MaxLines = 2; error.TextTrimming = TextTrimming.CharacterEllipsis; card.Children.Add(error);
        }
        var details = DetailsButton(poll); details.HorizontalAlignment = HorizontalAlignment.Stretch;
        details.BorderBrush = Tint("BorderBrush", 0.7); details.BorderThickness = new(1); details.CornerRadius = new(6); details.Padding = new(12, 8, 12, 8);
        card.Children.Add(details);
        var frame = Card(card); AutomationProperties.SetAutomationId(frame, "AutoscanActivityPoll_" + poll.Id);
        if (selected) { frame.Background = Tint("MutedBrush", 0.4); frame.BorderBrush = Tint("PrimaryTextBrush", 0.25); }
        return frame;
    }
}
