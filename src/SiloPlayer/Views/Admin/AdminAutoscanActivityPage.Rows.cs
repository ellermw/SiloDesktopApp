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
                var progress = AdminLibraryPresentation.ScanProgress(scan);
                var cancel = Action(A.T("queue.cancelLabel"), "AutoscanActivityCancel_" + scan.Id, () => _controller?.CancelAsync(scan.LibraryId));
                cancel.Style = (Style)Application.Current.Resources["GhostButtonStyle"];
                cancel.Content = new Border { Width = 14, Height = 14, BorderThickness = new(1.5), CornerRadius = new(2),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 248, 113, 113)), IsHitTestVisible = false };
                cancel.Width = cancel.Height = cancel.MinWidth = cancel.MinHeight = 32; cancel.Padding = new(0);
                ToolTipService.SetToolTip(cancel, A.T("queue.cancelLabel"));
                cancel.IsEnabled = state.Cancelling == null && !state.CancelReview.ContainsKey(scan.LibraryId);
                var started = SafeText(scan.IsRunning ? AdminLibraryPresentation.ScanStarted(scan.StartedAt, DateTimeOffset.UtcNow) : A.T("queue.waiting"), 12, true);
                var library = Stack(4); library.Children.Add(SafeText(A.Library(scan.LibraryId, state.Labels), 14)); library.Children.Add(started);
                var scope = Stack(4); scope.Children.Add(SafeText(A.Mode(scan.Mode), 13)); scope.Children.Add(SafeText(string.IsNullOrEmpty(scan.Path) ? A.T("entireLibrary") : scan.Path, 12, true, true));
                var detail = SafeText(progress.Length > 0 ? progress : AdminLibraryPresentation.ScanTrigger(scan.Trigger ?? ""), 12, true);
                var cells = new UIElement[] { Badge(scan.Status), library, scope, detail, cancel };
                queue.Children.Add(Card(_compact ? CardStack(cells) : RowGrid(cells, [1, 1.2, 2.2, 2.2, 1])));
            }
            if (state.Queue.Count > 8)
            {
                var paging = Inline();
                var previous = Action(A.T("web.ui.pagination.previous"), "AutoscanQueuePrevious", () => { _queuePage--; RerenderLive(); return Task.CompletedTask; });
                previous.IsEnabled = _queuePage > 0;
                var next = Action(A.T("web.ui.pagination.next"), "AutoscanQueueNext", () => { _queuePage++; RerenderLive(); return Task.CompletedTask; });
                next.IsEnabled = (_queuePage + 1) * 8 < state.Queue.Count;
                paging.Children.Add(previous); paging.Children.Add(SafeText(A.T("native.page", new { page = _queuePage + 1 }), 12, true)); paging.Children.Add(next);
                queue.Children.Add(paging);
            }
        }
        if (state.QueueCapped) queue.Children.Add(SafeText(A.T("native.livePartial"), 12, true));
        _live.Children.Add(queue);
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
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(26, color.R, color.G, color.B)), Padding = new(8, 3, 8, 3),
            CornerRadius = new(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
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
        string[] keys = scans ? ["status", "library", "source", "scope", "result", "time", "poll"] : ["status", "source", "counts", "details", "duration", "time"];
        return Card(RowGrid(keys.Select(key => (UIElement)SafeText(A.T("column." + key), 12, true)).ToArray(), scans ? ScanWidths : PollWidths));
    }

    private static readonly double[] ScanWidths = [0.8, 1, 1, 1.6, 1.6, 1, 0.8];
    private static readonly double[] PollWidths = [0.8, 1.2, 1.4, 2, 0.7, 1];

    private FrameworkElement ScanRow(AdminAutoscanActivityScan scan, AdminAutoscanActivityLabels labels)
    {
        var library = Stack(4); library.Children.Add(SafeText(A.Library(scan.LibraryId, labels), 14)); library.Children.Add(SafeText(scan.Id, 11, true, true));
        var scope = Stack(4); scope.Children.Add(SafeText(A.Mode(scan.Mode), 13)); scope.Children.Add(SafeText(string.IsNullOrEmpty(scan.Path) ? A.T("entireLibrary") : scan.Path, 12, true, true));
        if (!string.IsNullOrEmpty(scan.ErrorMessage)) scope.Children.Add(Toned(SafeText(scan.ErrorMessage, 12), Danger));
        var poll = string.IsNullOrEmpty(scan.EventStatus) ? SafeText("-", 12, true) : Badge(scan.EventStatus, true);
        UIElement[] cells = [Badge(scan.Status), library, SafeText(A.Source(scan.SourceId, scan.PluginId, scan.CapabilityId, labels), 13), scope,
            SafeText(A.Result(scan.Result), 12, true), SafeText(Timestamp(scan.CompletedAt ?? scan.StartedAt ?? scan.RequestedAt), 12, true), poll];
        var card = Card(_compact ? CardStack(cells) : RowGrid(cells, ScanWidths));
        AutomationProperties.SetAutomationId(card, "AutoscanActivityScan_" + scan.Id); return card;
    }

    private static int JoinedRuns(AdminAutoscanActivityEvent poll)
    {
        var created = (poll.ScanRuns ?? []).Select(run => run.Id).ToHashSet(StringComparer.Ordinal);
        return (poll.Changes ?? []).Where(change => change.Outcome == "joined" && !string.IsNullOrEmpty(change.ScanRunId) && !created.Contains(change.ScanRunId))
            .Select(change => change.ScanRunId!).Distinct(StringComparer.Ordinal).Count();
    }

    private FrameworkElement PollRow(AdminAutoscanActivityEvent poll, AdminAutoscanActivityLabels labels)
    {
        var source = Stack(4); source.Children.Add(SafeText(A.Source(poll.SourceId, poll.PluginId, poll.CapabilityId, labels, true), 14));
        if (poll.DeliveryMode == "webhook") source.Children.Add(SafeText(A.T(string.IsNullOrEmpty(poll.ProviderEventType) ? "delivery.webhook" : "delivery.webhookEvent", new { event_type = poll.ProviderEventType }), 12, true));
        var metrics = Stack(4);
        foreach (var entry in new (string Key, long Count)[] { ("changes", poll.ChangesReturned), ("targets", poll.TargetsClaimed),
            ("created", poll.ScansCreated), ("reused", poll.ScansReused), ("suppressed", poll.ScansSuppressed) })
            metrics.Children.Add(SafeText(A.RawCount("metrics." + entry.Key, entry.Count), 12, true));
        if (!string.IsNullOrEmpty(poll.ErrorMessage)) metrics.Children.Add(Toned(SafeText(poll.ErrorMessage, 12), Danger));
        var linked = (poll.ScanRuns?.Count ?? 0) + JoinedRuns(poll);
        var details = new Expander { Header = SafeText(A.Count("details.pathCount", poll.ChangesReturned) + " · " +
            A.T("details.linkedCount", new { count = linked, count_text = A.Number(linked) + (poll.ChangesTruncated ? "+" : "") }), 12, true),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(details, "AutoscanActivityDetails_" + poll.Id);
        details.Expanding += (_, _) => { _expanded.Add(poll.Id); details.Content ??= EventDetails(poll, labels); };
        details.Collapsed += (_, _) => _expanded.Remove(poll.Id);
        if (_expanded.Contains(poll.Id)) { details.Content = EventDetails(poll, labels); details.IsExpanded = true; }
        var elapsed = poll.Status == "running" ? Math.Max(0, (long)(DateTimeOffset.UtcNow - poll.StartedAt).TotalMilliseconds) : poll.DurationMs;
        UIElement[] cells = [Badge(poll.Status, true), source, metrics, details, SafeText(A.Duration(elapsed), 12, true),
            SafeText(Timestamp(poll.Status == "running" ? poll.StartedAt : poll.CompletedAt), 12, true)];
        var card = Card(_compact ? CardStack(cells) : RowGrid(cells, PollWidths));
        AutomationProperties.SetAutomationId(card, "AutoscanActivityPoll_" + poll.Id); return card;
    }

    private static readonly Windows.UI.Color Danger = Windows.UI.Color.FromArgb(255, 248, 113, 113);
    private static readonly Windows.UI.Color Amber = Windows.UI.Color.FromArgb(255, 251, 191, 36);
    private static TextBlock Toned(TextBlock text, Windows.UI.Color color) { text.Foreground = new SolidColorBrush(color); return text; }

    private static FrameworkElement EventDetails(AdminAutoscanActivityEvent poll, AdminAutoscanActivityLabels labels)
    {
        var panel = Stack(12); panel.Children.Add(SafeText(A.T("details.paths"), 12, true));
        var changes = poll.Changes ?? [];
        if (changes.Count == 0) panel.Children.Add(SafeText(A.T(poll.ChangesReturned > 0 ? "changes.notRecorded" : "changes.none"), 12, true));
        foreach (var change in changes)
        {
            var row = Stack(6);
            row.Children.Add(ChangeBadge(change.Outcome));
            row.Children.Add(SafeText(change.SourcePath, 12, false, true));
            if (!string.IsNullOrEmpty(change.RewrittenPath) && change.RewrittenPath != change.SourcePath)
                row.Children.Add(SafeText(A.T("change.rewrittenTo", new { path = change.RewrittenPath }), 12, true, true));
            var (text, tone) = ChangeExplanation(change, labels);
            var explanation = SafeText(text, 12, tone == null); if (tone is { } color) Toned(explanation, color);
            row.Children.Add(explanation);
            if (change.Outcome is "queued" or "joined" && !string.IsNullOrEmpty(change.ScanRunId)) row.Children.Add(SafeText(change.ScanRunId, 11, true, true));
            panel.Children.Add(Card(row));
        }
        if (poll.ChangesTruncated) panel.Children.Add(SafeText(A.T("changes.truncated", new { shown = A.Number(changes.Count), count = poll.ChangesReturned, count_text = A.Number(poll.ChangesReturned) }), 12, true));
        panel.Children.Add(SafeText(A.T("details.scansCreated"), 12, true));
        var runs = poll.ScanRuns ?? [];
        if (runs.Count == 0) panel.Children.Add(SafeText(A.T(JoinedRuns(poll) > 0 ? "runs.noneJoined" : "runs.none"), 12, true));
        foreach (var run in runs)
        {
            var row = Stack(6); row.Children.Add(Badge(run.Status)); row.Children.Add(SafeText(run.Mode, 13));
            row.Children.Add(SafeText(string.IsNullOrEmpty(run.Path) ? A.T("entireLibrary") : run.Path, 12, true, true));
            if (run.Result != null) row.Children.Add(SafeText(A.Result(run.Result), 12, true));
            row.Children.Add(SafeText(Time(run.CompletedAt ?? run.StartedAt ?? run.RequestedAt), 12, true));
            panel.Children.Add(Card(row));
        }
        return panel;
    }

    private static string Time(DateTimeOffset? value) => value == null ? "-" : SiloPlayer.Helpers.DateTimeDisplay.FormatTime(value.Value);

    /// <summary>The official ChangeExplanation text and tone (error red, unresolved amber, otherwise muted).</summary>
    private static (string Text, Windows.UI.Color? Tone) ChangeExplanation(AdminAutoscanActivityChange change, AdminAutoscanActivityLabels labels)
    {
        var library = change.LibraryId == null ? null : A.Library(change.LibraryId, labels);
        if (change.Outcome is "queued" or "joined")
        {
            var key = change.Outcome == "queued" ? "queued" : change.Reason == "follow_up_scan" ? "followUp" : "joined";
            var scope = A.T("change.scope", new { mode = string.IsNullOrEmpty(change.TargetMode) ? A.T("change.scanFallback") : A.Mode(change.TargetMode), path = string.IsNullOrEmpty(change.TargetPath) ? A.T("entireLibrary") : change.TargetPath });
            return (A.T("change." + key + (library == null ? "" : "InLibrary"), new { scope, library }), null);
        }
        if (change.Outcome == "suppressed") return (A.T(string.IsNullOrEmpty(change.TargetPath) ? "change.suppressed" : "change.suppressedPath", new { path = change.TargetPath }), null);
        var reason = A.ReasonText(change.Reason);
        if (change.Outcome == "error")
        {
            if (change.Reason == "resolve_failed") return (A.T(string.IsNullOrEmpty(change.Detail) ? "change.resolveFailed" : "change.resolveFailedDetail", new { detail = change.Detail }), Danger);
            return (reason ?? (string.IsNullOrEmpty(change.Detail) ? A.T("change.processingFailed") : change.Detail), Danger);
        }
        var text = reason ?? (!string.IsNullOrEmpty(change.Detail) ? change.Detail : !string.IsNullOrEmpty(change.Reason) ? change.Reason : A.T("change.notScanned"));
        return (library == null ? text : A.T("change.withLibrary", new { text, library }), change.Outcome == "unresolved" ? Amber : null);
    }
}
