using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using A = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityText;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminAutoscanActivityPage
{
    private static TextBlock SafeText(string text, double size = 14, bool muted = false, bool mono = false) => LogsNativeUi.Text(A.Safe(text), size, muted, mono);
    private static string Timestamp(DateTimeOffset? value) => value == null ? "-" : SiloPlayer.Helpers.DateTimeDisplay.FormatShortDateTime(value.Value);

    private void Closed()
    {
        _notices.Children.Clear(); _live.Children.Clear(); _rows.Children.Clear();
        _renderedItems = null; _renderedHistory = null; _renderedLabels = null;
        RenderedIds = []; RenderedContent = "closed";
        _refresh.IsEnabled = _next.IsEnabled = _previous.IsEnabled = _restart.IsEnabled = false;
        _search.IsEnabled = _status.IsEnabled = _size.IsEnabled = false; _summary.Text = "";
    }

    private void RenderLive(AdminAutoscanActivityState state)
    {
        _live.Children.Clear();
        var status = state.Status;
        var tiles = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        var columns = _compact ? 2 : 5;
        for (var index = 0; index < columns; index++) tiles.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var index = 0; index < (_compact ? 3 : 1); index++) tiles.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var values = new (string Label, string Value)[] {
            ("active", status?.ActiveScans.ToString() ?? "-"), ("queued", status?.AcceptedScans.ToString() ?? "-"),
            ("runningScans", status?.RunningScans.ToString() ?? "-"), ("polling", status?.RunningPolls?.Count.ToString() ?? "-"),
            ("latestPoll", status?.LatestEventAt is { } latest ? SiloPlayer.Helpers.DateTimeDisplay.FormatTime(latest) : "-") };
        for (var index = 0; index < values.Length; index++)
        {
            var tile = Stack(4); tile.Children.Add(SafeText(A.T("stats." + values[index].Label), 12, true));
            tile.Children.Add(SafeText(values[index].Value, index == 4 ? 16 : 24));
            Grid.SetColumn(tile, index % columns); Grid.SetRow(tile, index / columns); tiles.Children.Add(tile);
        }
        _live.Children.Add(Card(tiles));
        if (state.StatusFailure != null)
        {
            _live.Children.Add(SafeText(A.T("native.statusFailed"), 13));
            _live.Children.Add(Action(A.T("common.tryAgain"), "AutoscanActivityStatusRetry", () => _controller?.RefreshAsync()));
        }
        if (status?.RunningPolls is { Count: > 0 } polls)
        {
            var polling = Stack(12); polling.Children.Add(SafeText(A.T("polls.title"), 16));
            polling.Children.Add(SafeText(A.T("polls.description"), 12, true));
            polling.Children.Add(SafeText(A.Count("activeCount", polls.Count), 12));
            foreach (var poll in polls)
            {
                var card = Stack(8);
                card.Children.Add(SafeText(A.Source(poll.SourceId, poll.PluginId, poll.CapabilityId, state.Labels, true), 14));
                card.Children.Add(SafeText(poll.PluginId + " · " + poll.CapabilityId, 12, true));
                card.Children.Add(Badge("running", true));
                card.Children.Add(SafeText(A.T("polls.started", new { time = Timestamp(poll.StartedAt) }) + " · " +
                    A.T("polls.elapsed", new { duration = A.Duration(poll.ElapsedMs) }), 12, true));
                polling.Children.Add(Card(card));
            }
            _live.Children.Add(polling);
        }
        var queue = Stack(12); queue.Children.Add(SafeText(A.T("queue.title"), 16));
        queue.Children.Add(SafeText(A.T("queue.description"), 12, true));
        if (state.Queue.Count == 0)
            queue.Children.Add(SafeText(A.T(status?.ActiveScans > 0 ? "queue.pendingDetails" : "queue.empty"), 13, true));
        else
        {
            _queuePage = Math.Clamp(_queuePage, 0, (state.Queue.Count - 1) / 8);
            queue.Children.Add(SafeText(A.Count("activeCount", state.Queue.Count), 12));
            if (!_compact) queue.Children.Add(RowGrid(new UIElement[] {
                SafeText(A.T("column.status"),12,true), SafeText(A.T("column.library"),12,true), SafeText(A.T("column.scope"),12,true),
                SafeText(A.T("column.progress"),12,true), SafeText(A.T("column.action"),12,true) }, [1, 1.2, 2.2, 2.2, 1]));
            foreach (var scan in state.Queue.Skip(_queuePage * 8).Take(8))
            {
                var progress = Progress(scan);
                var cancel = Action(A.T("queue.cancelLabel"), "AutoscanActivityCancel_" + scan.Id, () => _controller?.CancelAsync(scan.LibraryId));
                cancel.Style = (Style)Application.Current.Resources["GhostButtonStyle"];
                cancel.Content = new Border { Width = 14, Height = 14, BorderThickness = new(1.5), CornerRadius = new(2),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)), IsHitTestVisible = false };
                cancel.Width = cancel.Height = cancel.MinWidth = cancel.MinHeight = 32; cancel.Padding = new(0);
                ToolTipService.SetToolTip(cancel, A.T("queue.cancelLabel"));
                cancel.IsEnabled = state.Cancelling == null && !state.CancelReview.ContainsKey(scan.LibraryId);
                var scope = Stack(4); scope.Children.Add(SafeText(A.Mode(scan.Mode), 13)); scope.Children.Add(SafeText(scan.Path ?? A.T("entireLibrary"), 12, true, true));
                var progressView = Stack(4); progressView.Children.Add(SafeText(progress, 12, true));
                if (scan.IsRunning && scan.TotalFiles is > 0 && scan.FilesProcessed is { } processed)
                    progressView.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = Math.Clamp(processed * 100d / scan.TotalFiles.Value, 0, 100) });
                var cells = new UIElement[] { Badge(scan.Status), LibraryLink(scan.LibraryId, state.Labels), scope, progressView, cancel };
                queue.Children.Add(Card(_compact ? CardStack(cells) : RowGrid(cells, [1, 1.2, 2.2, 2.2, 1])));
            }
            if (state.Queue.Count > 8)
            {
                var paging = Inline();
                var previous = Action(A.T("web.ui.pagination.previous"), "AutoscanQueuePrevious", () => { _queuePage--; RenderLive(state); return Task.CompletedTask; });
                previous.IsEnabled = _queuePage > 0;
                var next = Action(A.T("web.ui.pagination.next"), "AutoscanQueueNext", () => { _queuePage++; RenderLive(state); return Task.CompletedTask; });
                next.IsEnabled = (_queuePage + 1) * 8 < state.Queue.Count;
                paging.Children.Add(previous); paging.Children.Add(SafeText(A.T("native.page", new { page = _queuePage + 1 }), 12, true)); paging.Children.Add(next);
                queue.Children.Add(paging);
            }
        }
        if (state.QueueCapped) queue.Children.Add(SafeText(A.T("native.livePartial"), 12, true));
        _live.Children.Add(queue);
    }

    private static string Progress(AdminLibraryScanRun scan)
    {
        if (!scan.IsRunning) return A.T("queue.waiting");
        if (scan.TotalFiles is > 0 && scan.FilesProcessed is > 0)
            return A.T("web.admin.common.scans.progress", new { message = scan.Message ?? A.T("web.admin.common.scans.processingFiles"),
                processed = scan.FilesProcessed.Value.ToString("N0"), total = scan.TotalFiles.Value.ToString("N0"),
                percent = Math.Clamp((long)Math.Round(scan.FilesProcessed.Value * 100d / scan.TotalFiles.Value, MidpointRounding.AwayFromZero), 0, 100) });
        return scan.Message ?? A.T("web.admin.common.scans.trigger.autoscan");
    }

    private static FrameworkElement Badge(string status, bool poll = false)
    {
        var label = poll ? A.T("pollStatus." + status) : A.ScanStatus(status);
        var color = status is "error" or "failed" ? Windows.UI.Color.FromArgb(255, 248, 113, 113)
            : status is "unresolved" or "cancelled" ? Windows.UI.Color.FromArgb(255, 251, 191, 36)
            : status is "success" or "completed" ? Windows.UI.Color.FromArgb(255, 52, 211, 153)
            : status == "accepted" ? Windows.UI.Color.FromArgb(255, 156, 163, 175)
            : status == "running" && !poll ? Windows.UI.Color.FromArgb(255, 56, 189, 248) : Windows.UI.Color.FromArgb(255, 167, 139, 250);
        return ToneBadge(label, color);
    }

    private static FrameworkElement ChangeBadge(string outcome)
    {
        var label = outcome is "queued" or "joined" or "suppressed" or "unresolved" or "ignored" or "error" ? A.T("outcome." + outcome) : outcome;
        var color = outcome switch {
            "queued" => Windows.UI.Color.FromArgb(255, 52, 211, 153), "joined" => Windows.UI.Color.FromArgb(255, 56, 189, 248),
            "unresolved" => Windows.UI.Color.FromArgb(255, 251, 191, 36), "error" => Windows.UI.Color.FromArgb(255, 248, 113, 113),
            _ => Windows.UI.Color.FromArgb(255, 156, 163, 175) };
        return ToneBadge(label, color);
    }

    private static FrameworkElement ToneBadge(string label, Windows.UI.Color color)
    {
        var text = SafeText(label, 12); text.Foreground = new SolidColorBrush(color);
        return new Border { Child = text, BorderThickness = new(1), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(77, color.R, color.G, color.B)),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, color.R, color.G, color.B)), Padding = new(8, 3),
            CornerRadius = new(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    }

    private static FrameworkElement LibraryLink(string libraryId, AdminAutoscanActivityLabels labels)
    {
        var request = new AdminRouteRequest(AdminRoute.Libraries);
        var label = A.Safe(A.Library(libraryId, labels));
        var link = new HyperlinkButton { Content = SafeText(label, 13), Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(link, label);
        link.IsEnabled = AdminShellPage.CreateRegistry().CanOpen(request);
        link.Click += (_, _) => AdminShellPage.Open(request);
        ToolTipService.SetToolTip(link, libraryId);
        return link;
    }

    private static FrameworkElement SourceLabel(string? id, string? plugin, string? capability, AdminAutoscanActivityLabels labels, bool poll = false)
    {
        var label = A.Safe(A.Source(id, plugin, capability, labels, poll));
        var request = new AdminRouteRequest(AdminRoute.AutoscanSources);
        if (!AdminShellPage.CreateRegistry().CanOpen(request))
        {
            var text = SafeText(label, 13); ToolTipService.SetToolTip(text, A.Safe(id)); return text;
        }
        var link = new HyperlinkButton { Content = SafeText(label, 13), Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(link, label); link.Click += (_, _) => AdminShellPage.Open(request);
        ToolTipService.SetToolTip(link, A.Safe(id));
        return link;
    }

    private static StackPanel CardStack(IEnumerable<UIElement> children)
    {
        var stack = Stack(8); foreach (var child in children) stack.Children.Add(child); return stack;
    }

    private static Grid RowGrid(IReadOnlyList<UIElement> cells, double[] widths)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        foreach (var width in widths) grid.ColumnDefinitions.Add(new() { Width = new(width, GridUnitType.Star) });
        for (var index = 0; index < cells.Count; index++) { Grid.SetColumn((FrameworkElement)cells[index], index); grid.Children.Add(cells[index]); }
        return grid;
    }

    private static FrameworkElement HistoryHead(bool scans)
    {
        string[] keys = scans ? ["status", "library", "scope", "source", "result", "poll", "time"] : ["status", "source", "counts", "details", "duration", "time"];
        return Card(RowGrid(keys.Select(key => (UIElement)SafeText(A.T("column." + key), 12, true)).ToArray(), scans ? [0.8, 1, 1.6, 1, 1.6, 0.8, 1] : [0.8, 1.2, 1.4, 2, 0.7, 1]));
    }

    private FrameworkElement ScanRow(AdminAutoscanActivityScan scan, AdminAutoscanActivityLabels labels)
    {
        var scope = Stack(4); scope.Children.Add(SafeText(A.Mode(scan.Mode), 13)); scope.Children.Add(SafeText(scan.Path ?? A.T("entireLibrary"), 12, true, true));
        var result = Stack(4); result.Children.Add(SafeText(A.Result(scan.Result), 12, true));
        if (!string.IsNullOrEmpty(scan.ErrorMessage)) result.Children.Add(SafeText(scan.ErrorMessage, 12));
        result.Children.Add(SafeText(scan.Id, 11, true, true));
        var poll = Stack(4);
        if (!string.IsNullOrEmpty(scan.EventStatus)) poll.Children.Add(Badge(scan.EventStatus, true));
        poll.Children.Add(SafeText(Timestamp(scan.EventCompletedAt), 11, true));
        if (scan.AutoscanEventId != null)
        {
            var events = Action(A.T("history.polls"), "AutoscanActivityRelatedPoll_" + scan.Id, async () =>
            {
                if (_controller == null) return;
                await _controller.SelectAsync(AdminAutoscanHistory.Polls);
                await _controller.FilterAsync(scan.Id, "", 25, scan.SourceId);
            });
            poll.Children.Add(events);
        }
        UIElement[] cells = [Badge(scan.Status), LibraryLink(scan.LibraryId, labels), scope,
            SourceLabel(scan.SourceId, scan.PluginId, scan.CapabilityId, labels), result, poll,
            SafeText(Timestamp(scan.CompletedAt ?? scan.StartedAt ?? scan.RequestedAt), 12, true)];
        var card = Card(_compact ? CardStack(cells) : RowGrid(cells, [0.8, 1, 1.6, 1, 1.6, 0.8, 1]));
        AutomationProperties.SetAutomationId(card, "AutoscanActivityScan_" + scan.Id); return card;
    }

    private FrameworkElement PollRow(AdminAutoscanActivityEvent poll, AdminAutoscanActivityLabels labels)
    {
        var source = Stack(4); source.Children.Add(SourceLabel(poll.SourceId, poll.PluginId, poll.CapabilityId, labels, true));
        if (poll.DeliveryMode == "webhook") source.Children.Add(SafeText(A.T(string.IsNullOrEmpty(poll.ProviderEventType) ? "delivery.webhook" : "delivery.webhookEvent", new { event_type = poll.ProviderEventType }), 12, true));
        var metrics = Stack(4);
        foreach (var entry in new (string Key, long Count)[] { ("changes", poll.ChangesReturned), ("targets", poll.TargetsClaimed),
            ("created", poll.ScansCreated), ("reused", poll.ScansReused), ("suppressed", poll.ScansSuppressed) })
            if (entry.Key is "changes" or "targets" || entry.Count > 0) metrics.Children.Add(SafeText(A.Count("metrics." + entry.Key, entry.Count), 12, true));
        if (!string.IsNullOrEmpty(poll.ErrorMessage)) metrics.Children.Add(SafeText(poll.ErrorMessage, 12));
        var runs = poll.ScanRuns ?? [];
        var joined = (poll.Changes ?? []).Where(change => change.Outcome == "joined" && change.ScanRunId != null && !runs.Any(run => run.Id == change.ScanRunId))
            .Select(change => change.ScanRunId).Distinct().Count();
        var linked = runs.Count + joined;
        var details = new Expander { Header = SafeText(A.Count("details.pathCount", poll.ChangesReturned) + " · " +
            A.T("details.linkedCount", new { count = linked, count_text = linked.ToString("N0") + (poll.ChangesTruncated ? "+" : "") }), 12, true),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(details, "AutoscanActivityDetails_" + poll.Id);
        details.Expanding += (_, _) => { _expanded.Add(poll.Id); details.Content ??= EventDetails(poll, labels); };
        details.Collapsed += (_, _) => _expanded.Remove(poll.Id);
        if (_expanded.Contains(poll.Id)) { details.Content = EventDetails(poll, labels); details.IsExpanded = true; }
        var elapsed = poll.Status == "running" ? Math.Max(0, (long)(DateTimeOffset.UtcNow - poll.StartedAt).TotalMilliseconds) : poll.DurationMs;
        UIElement[] cells = [Badge(poll.Status, true), source, metrics, details, SafeText(A.Duration(elapsed), 12, true),
            SafeText(Timestamp(poll.Status == "running" ? poll.StartedAt : poll.CompletedAt), 12, true)];
        var card = Card(_compact ? CardStack(cells) : RowGrid(cells, [0.8, 1.2, 1.4, 2, 0.7, 1]));
        AutomationProperties.SetAutomationId(card, "AutoscanActivityPoll_" + poll.Id); return card;
    }

    private static FrameworkElement EventDetails(AdminAutoscanActivityEvent poll, AdminAutoscanActivityLabels labels)
    {
        var panel = Stack(12); panel.Children.Add(SafeText(A.T("details.paths"), 12, true));
        var changes = poll.Changes ?? [];
        if (changes.Count == 0) panel.Children.Add(SafeText(A.T(poll.ChangesReturned > 0 ? "changes.notRecorded" : "changes.none"), 12, true));
        foreach (var change in changes)
        {
            var row = Stack(6);
            row.Children.Add(ChangeBadge(change.Outcome));
            row.Children.Add(SafeText(change.SourcePath, 12, true, true));
            if (!string.IsNullOrEmpty(change.RewrittenPath) && change.RewrittenPath != change.SourcePath)
                row.Children.Add(SafeText(A.T("change.rewrittenTo", new { path = change.RewrittenPath }), 12, true, true));
            row.Children.Add(SafeText(ChangeExplanation(change, labels), 12, true));
            if (change.LibraryId != null) row.Children.Add(LibraryLink(change.LibraryId, labels));
            if (!string.IsNullOrEmpty(change.ScanRunId)) row.Children.Add(SafeText(change.ScanRunId, 11, true, true));
            panel.Children.Add(Card(row));
        }
        if (poll.ChangesTruncated) panel.Children.Add(SafeText(A.T("changes.truncated", new { shown = changes.Count.ToString("N0"), count = poll.ChangesReturned, count_text = poll.ChangesReturned.ToString("N0") }), 12, true));
        panel.Children.Add(SafeText(A.T("details.scansCreated"), 12, true));
        if (poll.ScanRuns is not { Count: > 0 }) panel.Children.Add(SafeText(A.T(changes.Any(change => change.Outcome == "joined" && change.ScanRunId != null) ? "runs.noneJoined" : "runs.none"), 12, true));
        foreach (var run in poll.ScanRuns ?? [])
        {
            var row = Stack(6); row.Children.Add(Badge(run.Status)); row.Children.Add(SafeText(A.Mode(run.Mode), 12));
            row.Children.Add(SafeText(run.Path ?? A.T("entireLibrary"), 12, true, true));
            row.Children.Add(SafeText(A.Result(run.Result), 12, true)); row.Children.Add(LibraryLink(run.LibraryId, labels));
            row.Children.Add(SafeText(Timestamp(run.CompletedAt ?? run.StartedAt ?? run.RequestedAt), 12, true));
            if (run.ErrorMessage != null) row.Children.Add(SafeText(run.ErrorMessage, 12));
            panel.Children.Add(Card(row));
        }
        return panel;
    }

    private static string ChangeExplanation(AdminAutoscanActivityChange change, AdminAutoscanActivityLabels labels)
    {
        var library = change.LibraryId == null ? null : A.Library(change.LibraryId, labels);
        if (change.Outcome is "queued" or "joined")
        {
            var key = change.Outcome == "queued" ? "queued" : change.Reason == "follow_up_scan" ? "followUp" : "joined";
            var scope = A.T("change.scope", new { mode = change.TargetMode == null ? A.T("change.scanFallback") : A.Mode(change.TargetMode), path = change.TargetPath ?? A.T("entireLibrary") });
            return A.T("change." + key + (library == null ? "" : "InLibrary"), new { scope, library });
        }
        if (change.Outcome == "suppressed") return A.T(string.IsNullOrEmpty(change.TargetPath) ? "change.suppressed" : "change.suppressedPath", new { path = change.TargetPath });
        if (change.Outcome == "error" && change.Reason == "resolve_failed") return A.T(string.IsNullOrEmpty(change.Detail) ? "change.resolveFailed" : "change.resolveFailedDetail", new { detail = change.Detail });
        var reason = change.Reason switch {
            "no_library_match" => "noLibraryMatch", "subtree_outside_library" => "subtreeOutsideLibrary", "path_outside_library" => "pathOutsideLibrary",
            "path_ambiguous" => "pathAmbiguous", "path_required" => "pathRequired", "path_permission_denied" => "pathPermissionDenied",
            "path_not_file_or_dir" => "pathNotFileOrDir", "path_not_inspectable" => "pathNotInspectable", "library_disabled" => "libraryDisabled",
            "library_root_offline" => "libraryRootOffline", "unsupported_extension" => "unsupportedExtension", "path_ignored" => "pathIgnored",
            "resolves_to_library" => "resolvesToLibrary", "enqueue_failed" => "enqueueFailed", _ => null };
        var text = reason != null ? A.T("reason." + reason) : change.Detail ?? change.Reason ?? A.T(change.Outcome == "error" ? "change.processingFailed" : "change.notScanned");
        return library == null ? text : A.T("change.withLibrary", new { text, library });
    }
}
