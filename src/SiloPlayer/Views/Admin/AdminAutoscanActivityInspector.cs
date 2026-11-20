using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.AutoscanUi;
using static SiloPlayer.Views.Admin.LogsNativeUi;
using A = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityText;
using D = SiloPlayer.Core.Services.Admin.AdminAutoscanActivityDisplay;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The body of the poll inspector sheet (88b825d57 <c>PollEventInspectorSheet</c>): the status badges, error, metric pills and the Paths and
/// Scans created tabs with their own search, outcome or status chips and pagination. The filters, tab and pages live here, so a newer
/// snapshot of the same poll replaces the data without resetting what the admin was doing.
/// </summary>
public sealed class AutoscanInspector
{
    private AdminAutoscanActivityEvent _event;
    private AdminAutoscanActivityLabels _labels;
    private readonly StackPanel _header = Stack(8), _tabs = Inline(16), _pathsPanel = Stack(12), _scansPanel = Stack(12);
    private readonly StackPanel _pathsChips = Stack(0), _pathsList = Stack(0), _scansChips = Stack(0), _scansList = Stack(0);
    private readonly Border _pathsPager = new(), _scansPager = new(), _truncated = new(), _runsNote = new();
    private readonly Grid _pathsBar = new(), _scansBar = new();
    private readonly TextBox _pathsSearch, _scansSearch;
    private readonly Button _pathsClear, _scansClear, _pathsReset, _scansReset;
    private string _tab, _outcome = "all", _scanStatus = "all";
    private int _pathsPage, _pathsSize = D.DefaultPathsPageSize, _scansPage, _scansSize = D.DefaultScansPageSize;
    private bool _syncing;
    private int _rebuilding;
    public StackPanel Root { get; } = Stack(16);
    public string Id => _event.Id;
    public AdminAutoscanActivityEvent Event => _event;
    public string Tab => _tab;

    public AutoscanInspector(AdminAutoscanActivityEvent poll, AdminAutoscanActivityLabels labels)
    {
        _event = poll; _labels = labels; _tab = D.InitialTab(poll);
        (_pathsSearch, _pathsClear, _pathsReset) = SearchControls("AutoscanInspectorPathsSearch", () => { _pathsPage = 0; RefreshPaths(); }, ResetPaths);
        (_scansSearch, _scansClear, _scansReset) = SearchControls("AutoscanInspectorScansSearch", () => { _scansPage = 0; RefreshScans(); }, ResetScans);
        BuildBar(_pathsBar, _pathsSearch, _pathsClear, _pathsReset, _pathsChips);
        BuildBar(_scansBar, _scansSearch, _scansClear, _scansReset, _scansChips);
        _truncated.BorderThickness = new(0, 0, 0, 1); _truncated.BorderBrush = Brush("BorderBrush"); _truncated.Padding = new(0, 0, 0, 8);
        _pathsPanel.Children.Add(_truncated); _pathsPanel.Children.Add(_pathsBar); _pathsPanel.Children.Add(_pathsList); _pathsPanel.Children.Add(_pathsPager);
        _runsNote.BorderThickness = new(0, 0, 0, 1); _runsNote.BorderBrush = Brush("BorderBrush"); _runsNote.Padding = new(0, 0, 0, 8);
        _scansPanel.Children.Add(_runsNote); _scansPanel.Children.Add(_scansBar); _scansPanel.Children.Add(_scansList); _scansPanel.Children.Add(_scansPager);
        Root.Children.Add(_header); Root.Children.Add(_tabs); Root.Children.Add(_pathsPanel); Root.Children.Add(_scansPanel);
        Refresh();
    }

    private (TextBox Search, Button Clear, Button Reset) SearchControls(string id, Action changed, Action reset)
    {
        var search = Input(A.T("web.ui.select.searchPlaceholder"), id, double.NaN); search.MaxLength = 1024;
        search.TextChanged += (_, _) => { if (!_syncing) changed(); UpdateClearButtons(); };
        var clear = IconButton("x", A.T("history.clearSearch"), id + "Clear", () => { search.Text = ""; }, ghost: true, size: 32);
        var resetButton = Button(A.T("history.reset"), id + "Reset"); resetButton.Height = 32; resetButton.MinHeight = 32; resetButton.FontSize = 12; resetButton.Click += (_, _) => reset();
        return (search, clear, resetButton);
    }

    private void UpdateClearButtons()
    {
        _pathsClear.Visibility = _pathsSearch.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _scansClear.Visibility = _scansSearch.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _pathsReset.Visibility = _pathsSearch.Text.Length > 0 || _outcome != "all" ? Visibility.Visible : Visibility.Collapsed;
        _scansReset.Visibility = _scansSearch.Text.Length > 0 || _scanStatus != "all" ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void BuildBar(Grid bar, TextBox search, Button clear, Button reset, StackPanel chips)
    {
        bar.RowSpacing = 10; bar.ColumnSpacing = 8; bar.Padding = new(0, 0, 0, 4);
        bar.RowDefinitions.Add(new() { Height = GridLength.Auto }); bar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        bar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bar.Children.Add(search);
        Grid.SetColumn(clear, 1); bar.Children.Add(clear);
        Grid.SetColumn(reset, 2); bar.Children.Add(reset);
        Grid.SetRow(chips, 1); Grid.SetColumnSpan(chips, 3); bar.Children.Add(chips);
    }

    /// <summary>Shows a newer snapshot of the same poll (or newer names); the admin's tab, filters and pages stay.</summary>
    public void SetEvent(AdminAutoscanActivityEvent poll, AdminAutoscanActivityLabels labels)
    {
        _event = poll; _labels = labels; Refresh();
    }

    private bool Phone => (_header.XamlRoot?.Size.Width ?? 1200) < 640;

    /// <summary>
    /// Rebuilds part of the sheet. The controls the admin used (a tab, a chip, a pager button, the scan-id jump) are rebuilt as new objects,
    /// so the one that had keyboard focus is gone afterwards; the outermost rebuild puts focus back on the control with the same
    /// automation id (the official sheet keeps its buttons mounted and so keeps focus). Nested rebuilds share the outer capture.
    /// </summary>
    private void Rebuild(Action body, string? focusAfter = null)
    {
        var outer = _rebuilding == 0;
        var id = outer ? FocusedIdIn(Root.XamlRoot, Root) : null;
        _rebuilding++;
        try { body(); } finally { _rebuilding--; }
        if (id != null) RestoreFocusById(Root.DispatcherQueue, Root, focusAfter ?? id);
    }

    private void Refresh() => Rebuild(RefreshAll);
    private void RefreshPaths() => Rebuild(BuildPaths);
    private void RefreshScans() => Rebuild(BuildScans);

    private void RefreshAll()
    {
        RefreshHeader(); RefreshTabs(); RefreshPaths(); RefreshScans();
        _pathsPanel.Visibility = _tab == "paths" ? Visibility.Visible : Visibility.Collapsed;
        _scansPanel.Visibility = _tab == "scans" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshHeader()
    {
        _header.Children.Clear();
        var badges = Inline(8); badges.Children.Add(PollStatusBadge(_event.Status));
        if (DeliveryBadge(_event) is { } delivery) badges.Children.Add(delivery);
        _header.Children.Add(badges);
        if (!string.IsNullOrEmpty(_event.ErrorMessage))
        {
            // A long error scrolls inside its own bounded box, which a keyboard can focus.
            var message = SafeText(_event.ErrorMessage, 12); message.Foreground = Solid(Destructive); message.IsTextSelectionEnabled = true;
            var scroll = new ScrollViewer
            {
                Content = message, MaxHeight = 128, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsTabStop = true, Padding = new(12, 8, 12, 8), CornerRadius = new(6), BorderThickness = new(1), BorderBrush = Solid(Destructive, 0.30), Background = Solid(Destructive, 0.10)
            };
            AutomationProperties.SetAutomationId(scroll, "AutoscanInspectorError"); AutomationProperties.SetName(scroll, A.T("pollStatus.error"));
            _header.Children.Add(scroll);
        }
        _header.Children.Add(MetricStrip(_event, pills: true));
    }

    private void RefreshTabs()
    {
        _tabs.Children.Clear();
        _tabs.Children.Add(TabButton("paths", A.T("details.paths"), _event.ChangesReturned));
        _tabs.Children.Add(TabButton("scans", A.T("details.scansCreated"), (_event.ScanRuns?.Count ?? 0) + _event.ScanRunsOmitted));
    }

    private Button TabButton(string tab, string label, long count)
    {
        var active = _tab == tab;
        var content = Inline(8);
        var title = Text(label, 12); title.FontWeight = FontWeights.Medium; title.Foreground = Brush(active ? "PrimaryTextBrush" : "SecondaryTextBrush"); content.Children.Add(title);
        var pill = Text(AdminText.FormatNumber(count), 11, true); pill.LineHeight = 14;
        content.Children.Add(new Border { Child = pill, Padding = new(6, 2, 6, 2), CornerRadius = new(4), Background = Tint("MutedBrush", 0.8) });
        var frame = new Border { Child = content, BorderThickness = new(0, 0, 0, 2), BorderBrush = active ? Brush("AccentBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent), Padding = new(4, 6, 4, 6) };
        var button = Button("", "AutoscanInspectorTab_" + tab, "GhostButtonStyle"); button.Content = frame; button.Padding = new(0); button.MinHeight = 0; button.CornerRadius = new(0);
        AutomationProperties.SetName(button, label + " " + AdminText.FormatNumber(count));
        if (active) AutomationProperties.SetItemStatus(button, "selected");
        button.Click += (_, _) => { if (_tab == tab) return; _tab = tab; Refresh(); };
        return button;
    }

    private Button Chip(string id, string label, int count, bool active, Action click)
    {
        var content = Inline(6); var name = Text(label, 12); name.FontWeight = FontWeights.Medium; content.Children.Add(name);
        var number = Text(AdminText.FormatNumber(count), 11, !active); number.LineHeight = 14;
        content.Children.Add(new Border { Child = number, Padding = new(4, 0, 4, 0), CornerRadius = new(4), Background = active ? Tint("AppBackgroundBrush", 0.7) : Tint("MutedBrush", 0.8) });
        var chip = Button("", id, "OutlineButtonStyle"); chip.Content = content; chip.Height = 28; chip.MinHeight = 28; chip.Padding = new(10, 0, 10, 0); chip.CornerRadius = new(6);
        chip.Background = active ? Brush("AccentBackgroundBrush") : Tint("AppBackgroundBrush", 0.6);
        AutomationProperties.SetName(chip, label + " " + AdminText.FormatNumber(count)); if (active) AutomationProperties.SetItemStatus(chip, "pressed");
        chip.Click += (_, _) => click();
        return chip;
    }

    private void ResetPaths()
    {
        _syncing = true; _pathsSearch.Text = ""; _syncing = false;
        _outcome = "all"; _pathsPage = 0; RefreshPaths();
    }

    private void ResetScans()
    {
        _syncing = true; _scansSearch.Text = ""; _syncing = false;
        _scanStatus = "all"; _scansPage = 0; RefreshScans();
    }

    private static Border NoteBox(string text)
    {
        var message = Text(text, 12, true); message.TextAlignment = TextAlignment.Center;
        return Dashed(message, 32);
    }

    private static Border Rows(IEnumerable<UIElement> rows)
    {
        var list = Stack(0); var first = true;
        foreach (var row in rows)
        {
            list.Children.Add(new Border { Child = row, Padding = new(16, 12, 16, 12), BorderBrush = Brush("BorderBrush"), BorderThickness = new(0, first ? 0 : 1, 0, 0) });
            first = false;
        }
        return new Border { Child = list, BorderBrush = Brush("BorderBrush"), BorderThickness = new(1), CornerRadius = new(8), Background = Tint("SurfaceBrush", 0.5) };
    }

    private void BuildPaths()
    {
        var changes = _event.Changes ?? [];
        _truncated.Visibility = _event.ChangesTruncated ? Visibility.Visible : Visibility.Collapsed;
        if (_event.ChangesTruncated)
        {
            var row = Inline(8); row.Children.Add(Glyph("triangle-alert", 14, Solid(Amber)));
            var note = SafeText(A.T("changes.truncated", new { count = _event.ChangesReturned, count_text = AdminText.FormatNumber(_event.ChangesReturned), shown = AdminText.FormatNumber(changes.Count) }), 12, true);
            row.Children.Add(note); _truncated.Child = row;
            AutomationProperties.SetAutomationId(note, "AutoscanInspectorTruncated");
        }
        _pathsBar.Visibility = changes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var outcomes = D.OutcomeCounts(changes);
        _pathsChips.Children.Clear();
        if (outcomes.Count > 1)
        {
            var wrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
            wrap.Children.Add(Chip("AutoscanInspectorOutcome_all", A.T("filter.allStatuses"), changes.Count, _outcome == "all", () => { _outcome = "all"; _pathsPage = 0; RefreshPaths(); }));
            foreach (var (outcome, count) in outcomes)
            {
                var value = outcome;
                wrap.Children.Add(Chip("AutoscanInspectorOutcome_" + outcome, D.IsKnownOutcome(outcome) ? A.T("outcome." + outcome) : outcome, count, _outcome == outcome, () => { _outcome = value; _pathsPage = 0; RefreshPaths(); }));
            }
            _pathsChips.Children.Add(wrap);
        }
        UpdateClearButtons();
        _pathsList.Children.Clear(); _pathsPager.Child = null;
        var filtered = D.FilterChanges(changes, _outcome, _pathsSearch.Text, _labels);
        if (changes.Count == 0) { _pathsList.Children.Add(NoteBox(A.T(_event.ChangesReturned > 0 ? "changes.notRecorded" : "changes.none"))); return; }
        if (filtered.Count == 0)
        {
            var empty = Stack(12); var message = Text(A.T("web.ui.select.noMatches"), 12, true); message.TextAlignment = TextAlignment.Center; empty.Children.Add(message);
            var reset = Button(A.T("history.reset"), "AutoscanInspectorPathsEmptyReset"); reset.HorizontalAlignment = HorizontalAlignment.Center; reset.Click += (_, _) => ResetPaths(); empty.Children.Add(reset);
            _pathsList.Children.Add(Dashed(empty, 32)); return;
        }
        var slice = D.Slice(filtered, _pathsPage, _pathsSize);
        var runIds = (_event.ScanRuns ?? []).Select(run => run.Id).ToHashSet(StringComparer.Ordinal);
        var rows = new List<UIElement>();
        for (var index = 0; index < slice.Rows.Count; index++)
        {
            var change = slice.Rows[index]; var absolute = slice.Page * _pathsSize + index;
            rows.Add(ChangeRow(change, absolute, runIds.Contains(change.ScanRunId ?? "")));
        }
        _pathsList.Children.Add(Rows(rows));
        _pathsPager.Padding = new(0, 8, 0, 0);
        _pathsPager.Child = Pagination("AutoscanInspectorPaths", filtered.Count, slice.Page, _pathsSize, page => { _pathsPage = page; RefreshPaths(); },
            size => { _pathsSize = size; _pathsPage = 0; RefreshPaths(); }, D.PathsPageSizes, null, phone: Phone);
    }

    private UIElement ChangeRow(AdminAutoscanActivityChange change, int absolute, bool canJump)
    {
        var row = Stack(6);
        var top = new Grid { ColumnSpacing = 8 }; top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        top.Children.Add(OutcomeBadge(change.Outcome));
        if (!string.IsNullOrEmpty(change.ScanRunId))
        {
            FrameworkElement run;
            if (canJump)
            {
                var content = Inline(4); content.Children.Add(Text(A.Safe(change.ScanRunId), 11, true, true)); content.Children.Add(Glyph("chevron-right", 12));
                var jump = Button("", "AutoscanInspectorJump_" + absolute, "OutlineButtonStyle"); jump.Content = content; jump.Height = 24; jump.MinHeight = 24; jump.Padding = new(6, 0, 6, 0); jump.CornerRadius = new(4);
                AutomationProperties.SetName(jump, change.ScanRunId);
                var runId = change.ScanRunId;
                jump.Click += (_, _) => JumpToRun(runId);
                run = jump;
            }
            else run = SafeText(change.ScanRunId, 11, true, true, selectable: true);
            run.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(run, 1); top.Children.Add(run);
        }
        row.Children.Add(top);
        row.Children.Add(PathDisplay(change.SourcePath));
        if (!string.IsNullOrEmpty(change.RewrittenPath) && change.RewrittenPath != change.SourcePath)
        {
            var rewritten = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brush("SecondaryTextBrush"), IsTextSelectionEnabled = true };
            var parts = A.T("change.rewrittenTo", new { path = "\u0001" }).Split('\u0001', 2);
            rewritten.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = parts[0] });
            rewritten.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = A.Safe(change.RewrittenPath), FontFamily = new FontFamily("Consolas") });
            if (parts.Length > 1 && parts[1].Length > 0) rewritten.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = parts[1] });
            row.Children.Add(rewritten);
        }
        var explanation = D.ChangeExplanation(change, _labels);
        row.Children.Add(Toned(SafeText(explanation.Text, 12, selectable: true), explanation.Tone));
        return row;
    }

    private void JumpToRun(string runId)
    {
        _syncing = true; _scansSearch.Text = runId; _syncing = false;
        _scanStatus = "all"; _scansPage = 0; _tab = "scans";
        Rebuild(RefreshAll, "AutoscanInspectorTab_scans");
    }

    private void BuildScans()
    {
        var runs = _event.ScanRuns ?? [];
        _runsNote.Visibility = _event.ScanRunsOmitted > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_event.ScanRunsOmitted > 0)
        {
            var row = Inline(8); row.Children.Add(Glyph("triangle-alert", 14, Solid(Amber)));
            var note = Text(A.T("native.runsOmitted", new { shown = AdminText.FormatNumber(runs.Count), total = AdminText.FormatNumber(runs.Count + _event.ScanRunsOmitted) }), 12, true);
            AutomationProperties.SetAutomationId(note, "AutoscanInspectorRunsOmitted"); row.Children.Add(note); _runsNote.Child = row;
        }
        _scansBar.Visibility = runs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var statuses = D.ScanStatusCounts(runs);
        _scansChips.Children.Clear();
        if (statuses.Count > 1)
        {
            var wrap = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
            wrap.Children.Add(Chip("AutoscanInspectorStatus_all", A.T("filter.allStatuses"), runs.Count, _scanStatus == "all", () => { _scanStatus = "all"; _scansPage = 0; RefreshScans(); }));
            foreach (var (status, count) in statuses)
            {
                var value = status;
                wrap.Children.Add(Chip("AutoscanInspectorStatus_" + status, A.ScanStatus(status, filter: true), count, _scanStatus == status, () => { _scanStatus = value; _scansPage = 0; RefreshScans(); }));
            }
            _scansChips.Children.Add(wrap);
        }
        UpdateClearButtons();
        _scansList.Children.Clear(); _scansPager.Child = null;
        var filtered = D.FilterRuns(runs, _scanStatus, _scansSearch.Text, _labels);
        if (runs.Count == 0) { _scansList.Children.Add(NoteBox(A.T(D.JoinedRunCount(_event) > 0 ? "runs.noneJoined" : "runs.none"))); return; }
        if (filtered.Count == 0)
        {
            var empty = Stack(12); var message = Text(A.T("web.ui.select.noMatches"), 12, true); message.TextAlignment = TextAlignment.Center; empty.Children.Add(message);
            var reset = Button(A.T("history.reset"), "AutoscanInspectorScansEmptyReset"); reset.HorizontalAlignment = HorizontalAlignment.Center; reset.Click += (_, _) => ResetScans(); empty.Children.Add(reset);
            _scansList.Children.Add(Dashed(empty, 32)); return;
        }
        var slice = D.Slice(filtered, _scansPage, _scansSize);
        _scansList.Children.Add(Rows(slice.Rows.Select(RunRow)));
        _scansPager.Padding = new(0, 8, 0, 0);
        _scansPager.Child = Pagination("AutoscanInspectorScans", filtered.Count, slice.Page, _scansSize, page => { _scansPage = page; RefreshScans(); },
            size => { _scansSize = size; _scansPage = 0; RefreshScans(); }, D.ScansPageSizes, "web.admin.common.pagination.scans", phone: Phone);
    }

    private UIElement RunRow(AdminAutoscanActivityRun run)
    {
        var row = Stack(6);
        var top = new Grid { ColumnSpacing = 8 }; top.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var identity = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
        identity.Children.Add(ScanStatusBadge(run.Status));
        var library = SafeText(A.Library(run.LibraryId, _labels), 12); library.FontWeight = FontWeights.Medium; identity.Children.Add(library);
        identity.Children.Add(SafeText(A.Mode(run.Mode), 12, true));
        top.Children.Add(identity);
        var time = Text(Timestamp(D.RunTime(run)), 12, true); time.TextWrapping = TextWrapping.NoWrap; Grid.SetColumn(time, 1); top.Children.Add(time);
        row.Children.Add(top);
        row.Children.Add(string.IsNullOrEmpty(run.Path) ? Text(A.T("entireLibrary"), 12, true) : PathDisplay(run.Path));
        var bottom = new Grid { ColumnSpacing = 8 }; bottom.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); bottom.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var result = Stack(4); result.Children.Add(ResultText(run.Result));
        if (!string.IsNullOrEmpty(run.ErrorMessage)) result.Children.Add(Toned(SafeText(run.ErrorMessage, 12, selectable: true), AdminAutoscanTone.Danger));
        bottom.Children.Add(result);
        var id = SafeText(run.Id, 11, true, true, selectable: true); Grid.SetColumn(id, 1); bottom.Children.Add(id);
        row.Children.Add(bottom);
        return row;
    }
}
