using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Controls.Admin;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;
using Windows.UI;

namespace SiloPlayer.Views.Admin;

/// <summary>The task list (official AdminTasks.tsx): category groups, the On demand group and in-place task rows.</summary>
public sealed partial class AdminScheduledTasksPage
{
    private sealed class TaskGroupView
    {
        internal readonly StackPanel Root = new() { Spacing = 12 };
        internal readonly TextBlock Heading = AdminSettingsUi.Text("", 12, true);
        internal readonly TextBlock Description = AdminSettingsUi.Text("", 12, true);
        internal readonly StackPanel Rows = new();
        internal readonly Border Panel;

        internal TaskGroupView()
        {
            Heading.FontWeight = FontWeights.Medium; Heading.CharacterSpacing = 240;
            AutomationProperties.SetHeadingLevel(Heading, AutomationHeadingLevel.Level2);
            var titles = new StackPanel { Spacing = 4 };
            titles.Children.Add(Heading); titles.Children.Add(Description);
            Panel = AdminMonitorUi.Panel(Rows, new Thickness(0));
            Root.Children.Add(titles); Root.Children.Add(Panel);
        }
    }

    private sealed class TaskRowView
    {
        internal readonly string Key;
        internal readonly Grid Root = new() { Padding = new(16), ColumnSpacing = 16, RowSpacing = 12 };
        internal readonly StackPanel Info = new() { Spacing = 2, MinWidth = 0 };
        internal readonly HyperlinkButton Name = new() { Padding = new(0), MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Left };
        internal readonly TextBlock NameText = new() { FontSize = 14, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
        internal readonly DashboardWrapPanel Summary = new() { Spacing = 8, RowSpacing = 2 };
        internal readonly StackPanel Progress = new() { Spacing = 4, Margin = new(0, 6, 0, 0) };
        internal readonly ColumnDefinition ProgressFill = new();
        internal readonly ColumnDefinition ProgressRest = new();
        internal readonly Border ProgressBar = new() { CornerRadius = new(4) };
        internal readonly TextBlock ProgressLabel = AdminSettingsUi.Text("", 12, true);
        internal readonly TextBlock ProgressPercent = AdminSettingsUi.Text("", 12, true);
        internal readonly Border Metrics = new() { CornerRadius = new(12), Padding = new(12), Margin = new(0, 12, 0, 0) };
        internal readonly Border BadgeHost = new() { VerticalAlignment = VerticalAlignment.Center };
        internal readonly Button Action;
        internal AdminTaskMetrics? RenderedMetrics;

        internal TaskRowView(string key)
        {
            Key = key;
            Root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            Root.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            Root.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Root.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Name.Content = NameText;
            Name.Foreground = AdminSettingsUi.Brush("PrimaryTextBrush");
            AutomationProperties.SetAutomationId(Name, "TaskLink_" + key);
            Info.Children.Add(Name);
            Info.Children.Add(Summary);
            var track = new Grid { Height = 8, CornerRadius = new(4), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .2) };
            track.ColumnDefinitions.Add(ProgressFill); track.ColumnDefinitions.Add(ProgressRest);
            track.Children.Add(ProgressBar);
            var line = new Grid { ColumnSpacing = 12 };
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            ProgressLabel.TextTrimming = TextTrimming.CharacterEllipsis; ProgressLabel.TextWrapping = TextWrapping.NoWrap;
            ProgressPercent.FontWeight = FontWeights.Medium;
            Grid.SetColumn(ProgressPercent, 1);
            line.Children.Add(ProgressLabel); line.Children.Add(ProgressPercent);
            Progress.Children.Add(track); Progress.Children.Add(line);
            AutomationProperties.SetAutomationId(Progress, "TaskProgress_" + key);
            Info.Children.Add(Progress);
            Metrics.Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .08);
            Info.Children.Add(Metrics);
            Root.Children.Add(Info);
            Root.Children.Add(BadgeHost);
            Action = new Button
            {
                MinWidth = 132, Height = 32, MinHeight = 0, Padding = new(12, 0, 12, 0), CornerRadius = new(8), FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            if (Application.Current.Resources.TryGetValue("OutlineButtonStyle", out var style) && style is Style outline)
            {
                Action.Style = outline; Action.Height = 32; Action.MinHeight = 0; Action.Padding = new(12, 0, 12, 0); Action.CornerRadius = new(8);
            }
            AutomationProperties.SetAutomationId(Action, "TaskAction_" + key);
            Root.Children.Add(Action);
        }
    }

    private readonly StackPanel _listHeader = new() { Spacing = 8 };
    private readonly TextBlock _listLoading = AdminSettingsUi.Text(AdminTasksText.Message("web.admin.tasks.page.loading"), 14, true);
    private readonly StackPanel _listAlert = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly TextBlock _listAlertText = AdminSettingsUi.Text("", 13);
    private Button? _listRetry;
    private readonly StackPanel _groupsHost = new() { Spacing = 24 };
    private readonly Dictionary<string, TaskGroupView> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskRowView> _rows = new(StringComparer.Ordinal);

    private void BuildList()
    {
        _body.Children.Clear();
        if (_listHeader.Children.Count == 0)
        {
            _listHeader.Children.Add(AdminSettingsUi.Title(AdminTasksText.Message("web.admin.tasks.page.title")));
            var subtitle = AdminSettingsUi.Text(AdminTasksText.Message("web.admin.tasks.page.subtitle"), 14, true);
            subtitle.MaxWidth = 800; subtitle.LineHeight = 22.75; subtitle.HorizontalAlignment = HorizontalAlignment.Left;
            _listHeader.Children.Add(subtitle);
            AutomationProperties.SetAutomationId(_listLoading, "TasksLoading");
            AutomationProperties.SetLiveSetting(_listLoading, AutomationLiveSetting.Polite);
            AutomationProperties.SetAutomationId(_listAlertText, "TasksError");
            _listRetry = AdminSettingsUi.Action(AdminTasksText.Message("common.retry"), "TasksRetry");
            _listRetry.HorizontalAlignment = HorizontalAlignment.Left;
            _listRetry.Click += async (_, _) => { if (_list != null) await _list.RetryAsync(); };
            _listAlert.Children.Add(AdminSettingsUi.Alert(_listAlertText));
            _listAlert.Children.Add(_listRetry);
        }
        _body.Children.Add(_listHeader);
        _body.Children.Add(_listLoading);
        _body.Children.Add(_listAlert);
        _body.Children.Add(_groupsHost);
    }

    private void RenderList(AdminTasksState state)
    {
        var current = state.IsOpen && state.Owner?.IsCurrent == true;
        _listLoading.Visibility = current && state.Loading ? Visibility.Visible : Visibility.Collapsed;
        var failed = current && state.Failure != null;
        _listAlert.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
        if (failed) AdminSettingsUi.UpdateLiveText(_listAlertText, AdminTasksText.Message("configure.native.tasks.listFailed") + " " + AdminTasksText.Failure(state.Failure));
        if (_listRetry != null) _listRetry.IsEnabled = failed && !state.Refreshing;
        RenderGroups(current ? state : AdminTasksState.Closed);
    }

    private void RenderGroups(AdminTasksState state)
    {
        var wantedGroups = state.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _groups.Keys.Where(id => !wantedGroups.Contains(id)).ToArray())
        {
            _groupsHost.Children.Remove(_groups[gone].Root);
            _groups.Remove(gone);
        }
        var wantedRows = state.Groups.SelectMany(group => group.Tasks).Select(task => task.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _rows.Keys.Where(key => !wantedRows.Contains(key)).ToArray())
        {
            if (_rows[gone].Root.Parent is Panel parent) parent.Children.Remove(_rows[gone].Root);
            _rows.Remove(gone);
        }
        for (var index = 0; index < state.Groups.Count; index++)
        {
            var group = state.Groups[index];
            if (!_groups.TryGetValue(group.Id, out var view)) { view = new TaskGroupView(); _groups[group.Id] = view; }
            AutomationProperties.SetAutomationId(view.Root, "TaskGroup_" + group.Id);
            view.Heading.Text = group.Label.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
            AutomationProperties.SetName(view.Heading, group.Label);
            view.Description.Text = group.Description ?? "";
            view.Description.Visibility = group.Description == null ? Visibility.Collapsed : Visibility.Visible;
            Place(_groupsHost, view.Root, index);
            for (var position = 0; position < group.Tasks.Count; position++)
            {
                var task = group.Tasks[position];
                if (!_rows.TryGetValue(task.Key, out var row))
                {
                    row = new TaskRowView(task.Key);
                    var key = task.Key;
                    row.Name.Click += (_, _) => OpenLocation(TaskLocation(key));
                    row.Action.Click += (_, _) => OnRowAction(key);
                    _rows[task.Key] = row;
                }
                if (row.Root.Parent is Panel parent && !ReferenceEquals(parent, view.Rows)) parent.Children.Remove(row.Root);
                Place(view.Rows, row.Root, position);
                row.Root.BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6);
                row.Root.BorderThickness = new(0, 0, 0, position == group.Tasks.Count - 1 ? 0 : 1);
                RenderRow(row, task, state);
            }
        }
    }

    /// <summary>Moves an element only when its position changed, so unchanged rows keep focus and hover.</summary>
    private static void Place(Panel host, UIElement element, int index)
    {
        var existing = host.Children.IndexOf(element);
        if (existing == index) return;
        if (existing >= 0) host.Children.RemoveAt(existing);
        host.Children.Insert(Math.Min(index, host.Children.Count), element);
    }

    private void RenderRow(TaskRowView row, AdminTaskInfo task, AdminTasksState state)
    {
        var name = AdminTasksText.TaskName(task);
        if (row.NameText.Text != name) { row.NameText.Text = name; AutomationProperties.SetName(row.Name, name); }

        row.Summary.Children.Clear();
        if (task.State == AdminTaskStates.Idle)
        {
            var schedule = AdminTasksText.DescribeSchedule(task.Triggers);
            row.Summary.Children.Add(SummaryText(schedule ?? AdminTasksText.Message(task.ManualOnly ? "web.admin.tasks.row.manualOnly" : "web.admin.tasks.row.noSchedule")));
            if (task.LastExecution is { } last) row.Summary.Children.Add(SummaryText(AdminTasksText.LastRunSummary(last, state.Now)));
            if (task.LastExecution == null && schedule == null) row.Summary.Children.Add(SummaryText(AdminTasksText.Message("web.admin.tasks.row.neverRun")));
            if (task.NextRunAt is { } next)
            {
                if (AdminTasksText.IsOverdue(next, state.Now))
                {
                    var overdue = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                    overdue.Children.Add(AdminMonitorUi.Icon("alert-triangle", 12, AdminMonitorUi.Warning));
                    var label = SummaryText(AdminTasksText.Message("web.admin.tasks.row.overdue"));
                    label.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Warning); label.FontWeight = FontWeights.Medium;
                    overdue.Children.Add(label);
                    AutomationProperties.SetAutomationId(overdue, "TaskOverdue_" + task.Key);
                    row.Summary.Children.Add(overdue);
                }
                else row.Summary.Children.Add(SummaryText(AdminTasksText.Message("web.admin.tasks.row.next", new { next = AdminTasksText.NextRun(next, state.Now) })));
            }
        }
        row.Summary.Visibility = row.Summary.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(row.Summary, string.Join(" ", row.Summary.Children.Select(SummaryLabel)));

        RenderProgress(row.Progress, row.ProgressFill, row.ProgressRest, row.ProgressBar, row.ProgressLabel, row.ProgressPercent, task);

        var metrics = task.Key == AdminTasksApi.MetricsTaskKey ? state.Metrics : null;
        if (!ReferenceEquals(metrics, row.RenderedMetrics))
        {
            row.RenderedMetrics = metrics;
            row.Metrics.Child = metrics == null ? null : MetricsSummary(metrics);
        }
        row.Metrics.Visibility = metrics == null ? Visibility.Collapsed : Visibility.Visible;

        row.BadgeHost.Child = task.State == AdminTaskStates.Idle && task.LastExecution is { } execution ? StatusBadge(execution) : null;
        row.BadgeHost.Visibility = row.BadgeHost.Child == null ? Visibility.Collapsed : Visibility.Visible;

        var starting = state.Starting.Contains(task.Key);
        var stopping = state.Stopping.Contains(task.Key);
        var running = task.IsActive;
        var cancelling = task.State == AdminTaskStates.Cancelling;
        if (running || starting)
        {
            var label = AdminTasksText.Message(cancelling ? "web.admin.tasks.row.stopping" : running ? "web.admin.tasks.row.stop" : "web.admin.tasks.row.starting");
            var tone = running && !cancelling ? AdminMonitorUi.Destructive : AdminMonitorUi.Foreground;
            SetButton(row.Action, running ? "square" : null, label, tone, spinner: !running);
            row.Action.IsEnabled = running && !starting && !stopping && !cancelling;
            ToolTipService.SetToolTip(row.Action, running ? AdminTasksText.Message("web.admin.tasks.row.stopTitle") : null);
            row.Action.Tag = running ? "stop" : null;
        }
        else
        {
            SetButton(row.Action, "play", AdminTasksText.Message("web.admin.tasks.actions.runNow"), AdminMonitorUi.Foreground, spinner: false);
            row.Action.IsEnabled = !stopping;
            ToolTipService.SetToolTip(row.Action, null);
            row.Action.Tag = "run";
        }
        LayoutRow(row);
    }

    private void OnRowAction(string key)
    {
        var controller = _list;
        if (controller == null || !_rows.TryGetValue(key, out var row)) return;
        var owner = controller.State.Owner;
        if (owner is not { IsCurrent: true }) return;
        if (row.Action.Tag as string == "run") _ = controller.RunAsync(key, owner);
        else if (row.Action.Tag as string == "stop") _ = controller.StopAsync(key, owner);
    }

    private void LayoutRow(TaskRowView row)
    {
        Grid.SetRow(row.Info, 0); Grid.SetColumn(row.Info, 0); Grid.SetColumnSpan(row.Info, _narrow ? 3 : 1);
        Grid.SetRow(row.BadgeHost, _narrow ? 1 : 0); Grid.SetColumn(row.BadgeHost, _narrow ? 0 : 1);
        Grid.SetRow(row.Action, _narrow ? 2 : 0); Grid.SetColumn(row.Action, _narrow ? 0 : 2); Grid.SetColumnSpan(row.Action, _narrow ? 3 : 1);
        row.BadgeHost.HorizontalAlignment = HorizontalAlignment.Left;
        row.Action.HorizontalAlignment = _narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
    }

    private void RelayoutList()
    {
        foreach (var row in _rows.Values) { LayoutRow(row); row.RenderedMetrics = null; }
        if (_list != null) RenderList(_list.State);
    }

    private static TextBlock SummaryText(string text)
    {
        var block = AdminSettingsUi.Text(text, 12, true);
        block.TextWrapping = TextWrapping.NoWrap;
        return block;
    }

    private static string SummaryLabel(UIElement element) => element switch
    {
        TextBlock text => text.Text,
        Panel panel => string.Join(" ", panel.Children.OfType<TextBlock>().Select(text => text.Text)),
        _ => "",
    };

    /// <summary>The running/cancelling progress bar and its status line (at least 2% wide, amber while cancelling).</summary>
    private static void RenderProgress(StackPanel host, ColumnDefinition fill, ColumnDefinition rest, Border bar, TextBlock label, TextBlock percent, AdminTaskInfo task)
    {
        host.Visibility = task.IsActive ? Visibility.Visible : Visibility.Collapsed;
        if (!task.IsActive) return;
        var cancelling = task.State == AdminTaskStates.Cancelling;
        var width = AdminTasksText.ProgressBarPercent(task.Progress);
        fill.Width = new(width, GridUnitType.Star);
        rest.Width = new(Math.Max(0, 100 - width), GridUnitType.Star);
        bar.Background = AdminMonitorUi.Solid(cancelling ? Color.FromArgb(255, 0xEA, 0xB3, 0x08) : AdminMonitorUi.Primary);
        label.Text = AdminTasksText.Message(cancelling ? "web.admin.tasks.progress.cancelling" : "web.admin.tasks.progress.running");
        var showPercent = !cancelling && task.Progress > 0;
        percent.Text = showPercent ? AdminTasksText.Progress(task.Progress) : "";
        percent.Visibility = showPercent ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(host, showPercent ? label.Text + " " + percent.Text : label.Text);
    }

    /// <summary>Official TaskStatusBadge: destructive for failures (and marker runs with failed items), outline for cancelled; the error is a tooltip.</summary>
    private static FrameworkElement StatusBadge(AdminTaskExecution execution)
    {
        var tone = execution.Status == "failed" || AdminTasksText.CompletedWithErrors(execution) ? AdminMonitorUi.Red500 : AdminMonitorUi.Muted;
        var badge = AdminMonitorUi.Badge(AdminTasksText.StatusLabel(execution), tone, fontSize: 12, backgroundAlpha: execution.Status == "cancelled" ? 0 : .12,
            borderAlpha: .3, foreground: execution.Status == "failed" || AdminTasksText.CompletedWithErrors(execution) ? AdminMonitorUi.Red500 : AdminMonitorUi.Foreground, radius: 8);
        AutomationProperties.SetName(badge, AdminTasksText.StatusLabel(execution));
        if (!string.IsNullOrWhiteSpace(execution.ErrorMessage))
        {
            ToolTipService.SetToolTip(badge, execution.ErrorMessage.Trim());
            AutomationProperties.SetHelpText(badge, execution.ErrorMessage.Trim());
        }
        return badge;
    }

    /// <summary>Official RefreshMetadataSummary: five counters and the non-zero reason chips.</summary>
    private FrameworkElement MetricsSummary(AdminTaskMetrics metrics)
    {
        var content = new StackPanel { Spacing = 12 };
        var cells = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        (string Label, string Value)[] values =
        [
            (AdminTasksText.Message("web.admin.tasks.refreshMetrics.backlog"), AdminText.FormatNumber(metrics.Total)),
            (AdminTasksText.Message("web.admin.tasks.refreshMetrics.due"), AdminText.FormatNumber(metrics.Due)),
            (AdminTasksText.Message("web.admin.tasks.refreshMetrics.processing"), AdminText.FormatNumber(metrics.Leased)),
            (AdminTasksText.Message("web.admin.tasks.refreshMetrics.waitingSince"), DateText(metrics.OldestDueAt)),
            (AdminTasksText.Message("web.admin.tasks.refreshMetrics.nextClaimTimeout"), DateText(metrics.OldestLeaseExpiresAt)),
        ];
        for (var index = 0; index < values.Length; index++)
        {
            var cell = new StackPanel();
            cell.Children.Add(AdminSettingsUi.Text(values[index].Label, 12, true));
            var value = AdminSettingsUi.Text(values[index].Value, 12); value.FontWeight = FontWeights.Medium;
            cell.Children.Add(value);
            AutomationProperties.SetName(cell, values[index].Label + " " + values[index].Value);
            if (_narrow) { cells.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetRow(cell, index); }
            else { cells.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); Grid.SetColumn(cell, index); }
            cells.Children.Add(cell);
        }
        content.Children.Add(cells);
        var chips = ReasonChips(metrics, includeEmpty: false);
        if (chips != null) content.Children.Add(chips);
        AutomationProperties.SetAutomationId(content, "TaskMetricsSummary");
        return content;
    }

    private static FrameworkElement? ReasonChips(AdminTaskMetrics metrics, bool includeEmpty)
    {
        var reasons = metrics.ReasonCounts.Where(entry => entry.Count > 0).ToArray();
        if (reasons.Length == 0) return includeEmpty ? AdminSettingsUi.Text(AdminTasksText.Message("web.admin.tasks.detail.noQueuedDebt"), 14, true) : null;
        var wrap = new DashboardWrapPanel { Spacing = 8, RowSpacing = 8 };
        foreach (var entry in reasons)
        {
            var text = AdminTasksText.Message("web.admin.tasks.refreshMetrics.reasonCount", new { reason = AdminTasksText.Reason(entry.Reason), count = AdminText.FormatNumber(entry.Count) });
            var chip = AdminMonitorUi.Badge(text, AdminMonitorUi.Muted, fontSize: 12, backgroundAlpha: .15, borderAlpha: 0, foreground: AdminMonitorUi.Foreground, radius: 8);
            AutomationProperties.SetName(chip, text);
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    private static string DateText(DateTimeOffset? value) => value is { } date ? DateTimeDisplay.FormatDateTime(date, seconds: true) : AdminTasksText.EmDash;

    /// <summary>Icon (or spinner) and label as one button content; the Button itself is kept so focus survives.</summary>
    private static void SetButton(Button button, string? icon, string label, Color tone, bool spinner)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (spinner) content.Children.Add(AdminMonitorUi.Spinner(14, tone));
        else if (icon != null) content.Children.Add(AdminMonitorUi.Icon(icon, 14, tone));
        content.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.Medium, Foreground = AdminMonitorUi.Solid(tone), VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
        AutomationProperties.SetName(button, label);
        button.BorderBrush = AdminMonitorUi.Solid(tone.Equals(AdminMonitorUi.Destructive) ? AdminMonitorUi.Destructive : AdminMonitorUi.Border, tone.Equals(AdminMonitorUi.Destructive) ? .4 : .8);
    }

    private void ClearList()
    {
        foreach (var view in _groups.Values) view.Rows.Children.Clear();
        _groups.Clear(); _rows.Clear();
        _groupsHost.Children.Clear();
        _listAlert.Visibility = Visibility.Collapsed;
        _listLoading.Visibility = Visibility.Collapsed;
    }
}
