using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views.Admin;

/// <summary>One task (official AdminTaskDetail.tsx): header, unavailable states, running panel, queue health and history.</summary>
public sealed partial class AdminScheduledTasksPage
{
    private readonly TextBlock _detailLoading = AdminSettingsUi.Text(AdminTasksText.Message("web.admin.tasks.detail.loading"), 14, true);
    private readonly StackPanel _unavailable = new() { Spacing = 12, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520, Margin = new(0, 48, 0, 0) };
    private readonly TextBlock _unavailableTitle = AdminSettingsUi.Text("", 20);
    private readonly TextBlock _unavailableText = AdminSettingsUi.Text("", 14, true);
    private Button? _detailRetry, _allTasks;
    private readonly StackPanel _detailContent = new() { Spacing = 24, Visibility = Visibility.Collapsed };
    private readonly StackPanel _detailRefreshError = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private Button? _detailRefreshRetry;
    private readonly TextBlock _crumbName = AdminSettingsUi.Text("", 14);
    private readonly TextBlock _detailTitle = AdminSettingsUi.Title("");
    private readonly TextBlock _detailSubtitle = AdminSettingsUi.Text("", 14, true);
    private readonly Grid _detailHeader = new() { ColumnSpacing = 16, RowSpacing = 12 };
    private readonly StackPanel _detailActions = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
    private readonly Border _categoryBadge = new() { CornerRadius = new(8), BorderThickness = new(1), Padding = new(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _categoryText = AdminSettingsUi.Text("", 12);
    private Button? _detailCommand;
    private readonly Border _scanNote = new() { CornerRadius = new(12), Padding = new(16), Visibility = Visibility.Collapsed };
    private readonly StackPanel _runningPanel = new() { Spacing = 4 };
    private readonly Border _runningHost = new() { CornerRadius = new(12), Padding = new(16), Visibility = Visibility.Collapsed };
    private readonly ColumnDefinition _runningFill = new();
    private readonly ColumnDefinition _runningRest = new();
    private readonly Border _runningBar = new() { CornerRadius = new(5) };
    private readonly TextBlock _runningLabel = AdminSettingsUi.Text("", 14, true);
    private readonly TextBlock _runningPercent = AdminSettingsUi.Text("", 14, true);
    private readonly StackPanel _queueHealth = new() { Spacing = 12, Visibility = Visibility.Collapsed };
    private readonly Border _queueHealthBody = new();
    private AdminTaskMetrics? _renderedDetailMetrics;
    private readonly StackPanel _historySection = new() { Spacing = 12 };
    private readonly StackPanel _historyError = new() { Orientation = Orientation.Horizontal, Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly Border _historyTable = new();
    private Button? _loadOlder;
    private string _renderedHistory = "";
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private bool? _renderedLibrariesLink;

    private void BuildDetail()
    {
        _body.Children.Clear();
        if (_detailHeader.Children.Count == 0) ComposeDetail();
        _body.Children.Add(_detailLoading);
        _body.Children.Add(_unavailable);
        _body.Children.Add(_detailContent);
    }

    private void ComposeDetail()
    {
        AutomationProperties.SetAutomationId(_detailLoading, "TaskLoading");
        AutomationProperties.SetLiveSetting(_detailLoading, AutomationLiveSetting.Polite);

        _unavailableTitle.FontWeight = FontWeights.SemiBold; _unavailableTitle.TextAlignment = TextAlignment.Center;
        _unavailableText.TextAlignment = TextAlignment.Center;
        AutomationProperties.SetAutomationId(_unavailableTitle, "TaskUnavailableTitle");
        AutomationProperties.SetHeadingLevel(_unavailableTitle, AutomationHeadingLevel.Level1);
        AutomationProperties.SetLiveSetting(_unavailableTitle, AutomationLiveSetting.Assertive);
        _detailRetry = AdminSettingsUi.Action(AdminTasksText.Message("common.retry"), "TaskRetry");
        _detailRetry.Click += async (_, _) => { if (_detail != null) await _detail.RetryAsync(); };
        _allTasks = AdminSettingsUi.Action(AdminTasksText.Message("web.admin.tasks.detail.allTasks"), "TaskAllTasks");
        _allTasks.Click += (_, _) => OpenLocation(new AdminRouteRequest(AdminRoute.ScheduledTasks));
        var unavailableActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        unavailableActions.Children.Add(_detailRetry); unavailableActions.Children.Add(_allTasks);
        _unavailable.Children.Add(_unavailableTitle); _unavailable.Children.Add(_unavailableText); _unavailable.Children.Add(unavailableActions);

        var crumbs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        AutomationProperties.SetName(crumbs, AdminTasksText.Message("web.admin.tasks.detail.breadcrumb.label"));
        AutomationProperties.SetLandmarkType(crumbs, AutomationLandmarkType.Navigation);
        crumbs.Children.Add(Crumb(AdminTasksText.Message("web.admin.tasks.detail.breadcrumb.admin"), "TaskCrumbAdmin", new AdminRouteRequest(AdminRoute.Dashboard)));
        crumbs.Children.Add(AdminMonitorUi.Icon("chevron-right", 14, AdminMonitorUi.Muted));
        crumbs.Children.Add(Crumb(AdminTasksText.Message("web.admin.tasks.detail.breadcrumb.tasks"), "TaskCrumbTasks", new AdminRouteRequest(AdminRoute.ScheduledTasks)));
        crumbs.Children.Add(AdminMonitorUi.Icon("chevron-right", 14, AdminMonitorUi.Muted));
        _crumbName.FontWeight = FontWeights.Medium; _crumbName.TextTrimming = TextTrimming.CharacterEllipsis; _crumbName.TextWrapping = TextWrapping.NoWrap;
        crumbs.Children.Add(_crumbName);
        var titles = new StackPanel { Spacing = 8 };
        titles.Children.Add(crumbs);
        AutomationProperties.SetAutomationId(_detailTitle, "TaskTitle");
        titles.Children.Add(_detailTitle);
        _detailSubtitle.MaxWidth = 800; _detailSubtitle.LineHeight = 22.75; _detailSubtitle.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetAutomationId(_detailSubtitle, "TaskDescription");
        titles.Children.Add(_detailSubtitle);
        _detailHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _detailHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _detailHeader.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _detailHeader.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _detailHeader.Children.Add(titles);
        _categoryBadge.BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .9);
        _categoryBadge.Child = _categoryText;
        AutomationProperties.SetAutomationId(_categoryBadge, "TaskCategory");
        _detailCommand = new Button { Height = 36, MinHeight = 0, Padding = new(14, 0, 14, 0), CornerRadius = new(10), HorizontalContentAlignment = HorizontalAlignment.Center };
        if (Application.Current.Resources.TryGetValue("OutlineButtonStyle", out var style) && style is Style outline)
        {
            _detailCommand.Style = outline; _detailCommand.Height = 36; _detailCommand.MinHeight = 0; _detailCommand.Padding = new(14, 0, 14, 0); _detailCommand.CornerRadius = new(10);
        }
        AutomationProperties.SetAutomationId(_detailCommand, "TaskCommand");
        _detailCommand.Click += (_, _) => OnDetailCommand();
        _detailActions.Children.Add(_categoryBadge); _detailActions.Children.Add(_detailCommand);
        _detailHeader.Children.Add(_detailActions);
        _detailContent.Children.Add(_detailHeader);

        var refreshErrorText = AdminSettingsUi.Text(AdminTasksText.Message("configure.native.tasks.refreshFailed"), 14, true);
        AutomationProperties.SetLiveSetting(refreshErrorText, AutomationLiveSetting.Assertive);
        AutomationProperties.SetAutomationId(_detailRefreshError, "TaskRefreshError");
        _detailRefreshRetry = AdminSettingsUi.Action(AdminTasksText.Message("common.retry"), "TaskRefreshRetry");
        _detailRefreshRetry.HorizontalAlignment = HorizontalAlignment.Left;
        _detailRefreshRetry.Click += async (_, _) => { if (_detail != null) await _detail.RetryAsync(); };
        _detailRefreshError.Children.Add(refreshErrorText);
        _detailRefreshError.Children.Add(_detailRefreshRetry);
        _detailContent.Children.Add(_detailRefreshError);

        _scanNote.Background = AdminMonitorUi.Solid(AdminMonitorUi.Surface, .5);
        AutomationProperties.SetAutomationId(_scanNote, "TaskScanLibrariesNote");
        _detailContent.Children.Add(_scanNote);

        var track = new Grid { Height = 10, CornerRadius = new(5), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .2) };
        track.ColumnDefinitions.Add(_runningFill); track.ColumnDefinitions.Add(_runningRest);
        track.Children.Add(_runningBar);
        var line = new Grid { ColumnSpacing = 12 };
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _runningPercent.FontWeight = FontWeights.Medium; Grid.SetColumn(_runningPercent, 1);
        line.Children.Add(_runningLabel); line.Children.Add(_runningPercent);
        _runningPanel.Children.Add(track); _runningPanel.Children.Add(line);
        _runningHost.Child = _runningPanel;
        _runningHost.Background = AdminMonitorUi.Solid(AdminMonitorUi.Surface, .5);
        AutomationProperties.SetAutomationId(_runningHost, "TaskRunningPanel");
        _detailContent.Children.Add(_runningHost);

        _queueHealth.Children.Add(SectionHeading(AdminTasksText.Message("web.admin.tasks.detail.queueHealth")));
        _queueHealth.Children.Add(_queueHealthBody);
        AutomationProperties.SetAutomationId(_queueHealth, "TaskQueueHealth");
        _detailContent.Children.Add(_queueHealth);

        ComposeSchedule();
        _detailContent.Children.Add(_scheduleSection);

        _historySection.Children.Add(SectionHeading(AdminTasksText.Message("web.admin.tasks.detail.executionHistory")));
        foreach (var run in AdminTasksText.Rich("web.admin.tasks.detail.history.loadError"))
        {
            if (run.Tag.Length == 0) { _historyError.Children.Add(AdminSettingsUi.Text(run.Text, 14)); continue; }
            var restart = AdminSettingsUi.Action(run.Text, "TaskHistoryRestart");
            restart.Click += async (_, _) => { if (_detail != null) await _detail.RestartHistoryAsync(); };
            _historyError.Children.Add(restart);
        }
        AutomationProperties.SetAutomationId(_historyError, "TaskHistoryError");
        AutomationProperties.SetLiveSetting(_historyError, AutomationLiveSetting.Assertive);
        _historySection.Children.Add(_historyError);
        _historySection.Children.Add(_historyTable);
        _loadOlder = AdminSettingsUi.Action(AdminTasksText.Message("web.admin.tasks.detail.history.loadOlder"), "TaskHistoryLoadOlder");
        _loadOlder.HorizontalAlignment = HorizontalAlignment.Left;
        _loadOlder.Click += async (_, _) => { if (_detail != null) await _detail.LoadOlderAsync(); };
        _historySection.Children.Add(_loadOlder);
        _detailContent.Children.Add(_historySection);
    }

    private static HyperlinkButton Crumb(string label, string id, AdminRouteRequest target)
    {
        var link = new HyperlinkButton { Content = label, Padding = new(0), MinHeight = 0, FontSize = 14, Foreground = AdminSettingsUi.Brush("SecondaryTextBrush") };
        AutomationProperties.SetAutomationId(link, id);
        AutomationProperties.SetName(link, label);
        link.Click += (_, _) => OpenLocation(target);
        return link;
    }

    private static TextBlock SectionHeading(string text)
    {
        var heading = AdminSettingsUi.Text(text, 18);
        heading.FontWeight = FontWeights.Medium;
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        return heading;
    }

    private void RenderDetail(AdminTaskDetailState state)
    {
        var current = state.IsOpen && state.Owner?.IsCurrent == true;
        _detailLoading.Visibility = current && state.Loading && state.Task == null && state.Failure == null ? Visibility.Visible : Visibility.Collapsed;
        var task = current ? state.Task : null;
        var unavailable = current && task == null && (state.NotFound || state.Failure != null);
        _unavailable.Visibility = unavailable ? Visibility.Visible : Visibility.Collapsed;
        if (unavailable)
        {
            var notFound = state.NotFound;
            AdminSettingsUi.UpdateLiveText(_unavailableTitle, AdminTasksText.Message(notFound ? "web.admin.tasks.detail.notFoundTitle" : "web.admin.tasks.detail.loadErrorTitle"));
            _unavailableText.Text = AdminTasksText.Message(notFound ? "web.admin.tasks.detail.notFoundDescription" : "web.admin.tasks.detail.loadErrorDescription");
            _detailRetry!.Visibility = notFound ? Visibility.Collapsed : Visibility.Visible;
            _detailRetry.IsEnabled = !state.Loading;
            _allTasks!.Visibility = notFound ? Visibility.Visible : Visibility.Collapsed;
        }
        _detailContent.Visibility = task != null ? Visibility.Visible : Visibility.Collapsed;
        _detailRefreshError.Visibility = task != null && state.Failure != null ? Visibility.Visible : Visibility.Collapsed;
        _detailRefreshRetry!.IsEnabled = current && !state.Refreshing;
        if (task == null)
        {
            ClearHistoryTable();
            RenderSchedule(state, null);
            return;
        }

        var name = AdminTasksText.TaskName(task);
        _crumbName.Text = name;
        _detailTitle.Text = name;
        var description = AdminTasksText.TaskDescription(task);
        _detailSubtitle.Text = description;
        _detailSubtitle.Visibility = description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _categoryText.Text = task.Category;
        AutomationProperties.SetName(_categoryBadge, task.Category);
        var running = task.IsActive;
        SetButton(_detailCommand!, running ? "square" : "play",
            AdminTasksText.Message(running ? "web.admin.tasks.detail.cancelRun" : "web.admin.tasks.actions.runNow"), AdminMonitorUi.Foreground, spinner: false);
        _detailCommand!.Tag = running ? "cancel" : "run";
        _detailCommand.IsEnabled = !state.RunPending && !state.CancelPending && task.State != AdminTaskStates.Cancelling;

        RenderScanNote(task);
        RenderProgress(_runningPanel, _runningFill, _runningRest, _runningBar, _runningLabel, _runningPercent, task);
        _runningHost.Visibility = task.IsActive ? Visibility.Visible : Visibility.Collapsed;

        var metrics = task.Key == AdminTasksApi.MetricsTaskKey ? state.Metrics : null;
        _queueHealth.Visibility = metrics == null ? Visibility.Collapsed : Visibility.Visible;
        if (!ReferenceEquals(metrics, _renderedDetailMetrics))
        {
            _renderedDetailMetrics = metrics;
            _queueHealthBody.Child = metrics == null ? null : MetricsPanel(metrics);
        }

        RenderSchedule(state, task);
        RenderHistory(state);
        LayoutDetail();
    }

    private void OnDetailCommand()
    {
        var controller = _detail;
        var owner = controller?.State.Owner;
        if (controller == null || owner is not { IsCurrent: true }) return;
        if (_detailCommand?.Tag as string == "run") _ = controller.RunAsync(owner);
        else if (_detailCommand?.Tag as string == "cancel") _ = controller.CancelAsync(owner);
    }

    /// <summary>The scan_libraries note links to Libraries only while that route has a registered native page.</summary>
    private void RenderScanNote(AdminTaskInfo task)
    {
        var shown = task.Key == "scan_libraries";
        _scanNote.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown) return;
        var linkable = AdminShellPage.CanOpen(new AdminRouteRequest(AdminRoute.Libraries));
        if (_renderedLibrariesLink == linkable && _scanNote.Child != null) return;
        _renderedLibrariesLink = linkable;
        var text = AdminSettingsUi.Text("", 14, true);
        text.LineHeight = 22.75;
        foreach (var run in AdminTasksText.Rich("web.admin.tasks.detail.scanLibrariesNote"))
        {
            if (run.Tag == "link" && linkable)
            {
                var link = new Hyperlink { Foreground = AdminSettingsUi.Brush("PrimaryTextBrush"), FontWeight = FontWeights.Medium, UnderlineStyle = UnderlineStyle.None };
                link.Inlines.Add(new Run { Text = run.Text });
                link.Click += (_, _) => OpenLocation(new AdminRouteRequest(AdminRoute.Libraries));
                AutomationProperties.SetAutomationId(link, "TaskLibrariesLink");
                text.Inlines.Add(link);
            }
            else text.Inlines.Add(new Run { Text = run.Text });
        }
        _scanNote.Child = text;
    }

    /// <summary>Official RefreshMetricsPanel: counters, reason breakdown with attempt buckets, recent errors and due samples.</summary>
    private FrameworkElement MetricsPanel(AdminTaskMetrics metrics)
    {
        var root = new StackPanel { Spacing = 16 };
        var counters = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        (string Label, string Value, bool Large)[] values =
        [
            (AdminTasksText.Message("web.admin.tasks.refreshMetrics.backlog"), AdminText.FormatNumber(metrics.Total), true),
            (AdminTasksText.Message("web.admin.tasks.refreshMetrics.due"), AdminText.FormatNumber(metrics.Due), true),
            (AdminTasksText.Message("web.admin.tasks.refreshMetrics.processing"), AdminText.FormatNumber(metrics.Leased), true),
            (AdminTasksText.Message("web.admin.tasks.refreshMetrics.waitingSince"), DateText(metrics.OldestDueAt), false),
            (AdminTasksText.Message("web.admin.tasks.refreshMetrics.nextClaimTimeout"), DateText(metrics.OldestLeaseExpiresAt), false),
        ];
        var columns = _narrow ? 1 : 5;
        for (var column = 0; column < columns; column++) counters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (var index = 0; index < values.Length; index++)
        {
            var card = new StackPanel { Spacing = 8 };
            var label = AdminMonitorUi.Eyebrow(values[index].Label, AdminMonitorUi.Muted, characterSpacing: 160, size: 12);
            label.TextWrapping = TextWrapping.Wrap;
            card.Children.Add(label);
            var value = AdminSettingsUi.Text(values[index].Value, values[index].Large ? 24 : 14);
            value.FontWeight = values[index].Large ? FontWeights.SemiBold : FontWeights.Medium;
            card.Children.Add(value);
            var panel = AdminMonitorUi.Panel(card, new Thickness(16));
            AutomationProperties.SetName(panel, values[index].Label + " " + values[index].Value);
            if (counters.RowDefinitions.Count <= index / columns) counters.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Grid.SetRow(panel, index / columns); Grid.SetColumn(panel, index % columns);
            counters.Children.Add(panel);
        }
        root.Children.Add(counters);

        var middle = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        var breakdown = new StackPanel { Spacing = 12 };
        breakdown.Children.Add(PanelHeading(AdminTasksText.Message("web.admin.tasks.detail.reasonBreakdown")));
        breakdown.Children.Add(ReasonChips(metrics, includeEmpty: true)!);
        if (metrics.AttemptBuckets.Count > 0)
        {
            var buckets = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            var bucketColumns = _narrow ? 1 : 5;
            for (var column = 0; column < bucketColumns; column++) buckets.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (var index = 0; index < metrics.AttemptBuckets.Count; index++)
            {
                var bucket = metrics.AttemptBuckets[index];
                var cell = new StackPanel { Spacing = 4 };
                var caption = AdminTasksText.Message("web.admin.tasks.detail.attemptBucket", new { range = bucket.Label });
                cell.Children.Add(AdminSettingsUi.Text(caption, 12, true));
                var count = AdminSettingsUi.Text(AdminText.FormatNumber(bucket.Count), 18); count.FontWeight = FontWeights.SemiBold;
                cell.Children.Add(count);
                var box = new Border { Child = cell, CornerRadius = new(12), Padding = new(12), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .12) };
                AutomationProperties.SetName(box, caption + " " + count.Text);
                if (buckets.RowDefinitions.Count <= index / bucketColumns) buckets.RowDefinitions.Add(new() { Height = GridLength.Auto });
                Grid.SetRow(box, index / bucketColumns); Grid.SetColumn(box, index % bucketColumns);
                buckets.Children.Add(box);
            }
            breakdown.Children.Add(buckets);
        }
        var errors = new StackPanel { Spacing = 12 };
        errors.Children.Add(PanelHeading(AdminTasksText.Message("web.admin.tasks.detail.recentErrors")));
        if (metrics.RecentErrors.Count == 0) errors.Children.Add(AdminSettingsUi.Text(AdminTasksText.Message("web.admin.tasks.detail.noRecentErrors"), 14, true));
        foreach (var entry in metrics.RecentErrors)
        {
            var item = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
            item.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); item.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var heading = new StackPanel();
            var title = AdminSettingsUi.Text(entry.Title.Length > 0 ? entry.Title : entry.ContentId, 14);
            title.FontWeight = FontWeights.Medium; title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap;
            heading.Children.Add(title);
            heading.Children.Add(AdminSettingsUi.Text(AdminTasksText.Message("web.admin.tasks.detail.recentErrorAttempts", new Dictionary<string, object?>
            {
                ["type"] = entry.Type.Length > 0 ? entry.Type : AdminTasksText.Message("web.admin.tasks.detail.itemFallback"),
                ["count"] = entry.AttemptCount, ["count_text"] = AdminText.FormatNumber(entry.AttemptCount),
            }), 12, true));
            item.Children.Add(heading);
            var when = AdminSettingsUi.Text(DateText(entry.LastAttemptAt), 12, true); Grid.SetColumn(when, 1); when.VerticalAlignment = VerticalAlignment.Top;
            item.Children.Add(when);
            var message = AdminSettingsUi.Text(entry.LastError, 12, true); message.MaxLines = 2; message.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(message, 1); Grid.SetColumnSpan(message, 2);
            item.RowDefinitions.Add(new() { Height = GridLength.Auto }); item.RowDefinitions.Add(new() { Height = GridLength.Auto });
            item.Children.Add(message);
            errors.Children.Add(new Border { Child = item, CornerRadius = new(12), Padding = new(12), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .08) });
        }
        var breakdownPanel = AdminMonitorUi.Panel(breakdown, new Thickness(16));
        var errorsPanel = AdminMonitorUi.Panel(errors, new Thickness(16));
        AutomationProperties.SetAutomationId(breakdownPanel, "TaskReasonBreakdown");
        AutomationProperties.SetAutomationId(errorsPanel, "TaskRecentErrors");
        middle.ColumnDefinitions.Add(new() { Width = new(1.2, GridUnitType.Star) });
        if (!_narrow) middle.ColumnDefinitions.Add(new() { Width = new(0.8, GridUnitType.Star) });
        middle.RowDefinitions.Add(new() { Height = GridLength.Auto });
        if (_narrow) middle.RowDefinitions.Add(new() { Height = GridLength.Auto });
        middle.Children.Add(breakdownPanel);
        Grid.SetColumn(errorsPanel, _narrow ? 0 : 1); Grid.SetRow(errorsPanel, _narrow ? 1 : 0);
        middle.Children.Add(errorsPanel);
        root.Children.Add(middle);

        var samples = new StackPanel();
        var samplesHeading = new Border { Padding = new(16, 12, 16, 12), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6), BorderThickness = new(0, 0, 0, 1) };
        samplesHeading.Child = PanelHeading(AdminTasksText.Message("web.admin.tasks.detail.dueSamples"));
        samples.Children.Add(samplesHeading);
        var table = new Grid();
        for (var column = 0; column < 4; column++) table.ColumnDefinitions.Add(new() { Width = new(column == 0 ? 2 : 1, GridUnitType.Star) });
        AddTableRow(table, header: true,
            [Cell(AdminTasksText.Message("web.admin.tasks.detail.columns.item"), header: true), Cell(AdminTasksText.Message("web.admin.tasks.detail.columns.nextRefresh"), header: true),
             Cell(AdminTasksText.Message("web.admin.tasks.detail.columns.attempts"), header: true), Cell(AdminTasksText.Message("web.admin.tasks.detail.columns.lastAttempt"), header: true)]);
        if (metrics.DueSamples.Count == 0)
        {
            var empty = Cell(AdminTasksText.Message("web.admin.tasks.detail.noDueItems"), muted: true);
            empty.HorizontalAlignment = HorizontalAlignment.Stretch;
            ((TextBlock)((Border)empty).Child).TextAlignment = TextAlignment.Center;
            AddSpanningRow(table, empty);
        }
        foreach (var entry in metrics.DueSamples)
        {
            var item = new StackPanel();
            var title = AdminSettingsUi.Text(entry.Title.Length > 0 ? entry.Title : entry.ContentId, 14); title.FontWeight = FontWeights.Medium;
            item.Children.Add(title);
            item.Children.Add(AdminSettingsUi.Text(entry.Type.Length > 0 ? entry.Type : AdminTasksText.Message("web.admin.tasks.detail.itemFallback"), 12, true));
            AddTableRow(table, header: false, [new Border { Child = item, Padding = new(16, 8, 16, 8) }, Cell(DateText(entry.NextRefreshAt)),
                Cell(AdminText.FormatNumber(entry.AttemptCount)), Cell(DateText(entry.LastAttemptAt))]);
        }
        samples.Children.Add(table);
        var samplesPanel = AdminMonitorUi.Panel(samples, new Thickness(0));
        AutomationProperties.SetAutomationId(samplesPanel, "TaskDueSamples");
        root.Children.Add(samplesPanel);
        return root;
    }

    private static TextBlock PanelHeading(string text)
    {
        var heading = AdminSettingsUi.Text(text, 14);
        heading.FontWeight = FontWeights.Medium;
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level3);
        return heading;
    }

    private static FrameworkElement Cell(string text, bool header = false, bool muted = false)
    {
        var block = AdminSettingsUi.Text(text, 14, muted);
        if (header) block.FontWeight = FontWeights.Medium;
        return new Border { Child = block, Padding = new(16, 8, 16, 8) };
    }

    private static void AddTableRow(Grid table, bool header, IReadOnlyList<FrameworkElement> cells)
    {
        var row = table.RowDefinitions.Count;
        table.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var background = new Border
        {
            BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6), BorderThickness = new(0, 0, 0, 1),
            Background = header ? AdminMonitorUi.Solid(AdminMonitorUi.Muted, .1) : null,
        };
        Grid.SetRow(background, row); Grid.SetColumnSpan(background, table.ColumnDefinitions.Count);
        table.Children.Add(background);
        for (var column = 0; column < cells.Count; column++)
        {
            Grid.SetRow(cells[column], row); Grid.SetColumn(cells[column], column);
            table.Children.Add(cells[column]);
        }
    }

    private static void AddSpanningRow(Grid table, FrameworkElement cell)
    {
        var row = table.RowDefinitions.Count;
        table.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Grid.SetRow(cell, row); Grid.SetColumnSpan(cell, table.ColumnDefinitions.Count);
        table.Children.Add(cell);
    }


    private void RenderHistory(AdminTaskDetailState state)
    {
        _historyError.Visibility = state.HistoryFailed ? Visibility.Visible : Visibility.Collapsed;
        _loadOlder!.Visibility = state.HistoryHasMore ? Visibility.Visible : Visibility.Collapsed;
        _loadOlder.IsEnabled = !state.HistoryLoading;
        var signature = string.Join("|", state.History.Select(item => item.Id)) + "#" + string.Join("|", _expanded.Order(StringComparer.Ordinal)) + "#" + (_narrow ? "n" : "w");
        if (signature == _renderedHistory && _historyTable.Child != null) return;
        _renderedHistory = signature;
        var table = new Grid();
        table.ColumnDefinitions.Add(new() { Width = new(1.4, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new() { Width = new(0.8, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new() { Width = new(1.6, GridUnitType.Star) });
        AddTableRow(table, header: true,
            [Cell(AdminTasksText.Message("web.admin.tasks.detail.history.columns.started"), header: true), Cell(AdminTasksText.Message("web.admin.tasks.detail.history.columns.duration"), header: true),
             Cell(AdminTasksText.Message("web.admin.tasks.detail.history.columns.status"), header: true), Cell(AdminTasksText.Message("web.admin.tasks.detail.history.columns.error"), header: true)]);
        if (state.History.Count == 0)
        {
            var empty = Cell(AdminTasksText.Message("web.admin.tasks.detail.history.empty"), muted: true);
            ((TextBlock)((Border)empty).Child).TextAlignment = TextAlignment.Center;
            AutomationProperties.SetAutomationId(empty, "TaskHistoryEmpty");
            AddSpanningRow(table, empty);
        }
        foreach (var execution in state.History) AddHistoryRow(table, execution);
        var panel = AdminMonitorUi.Panel(table, new Thickness(0));
        AutomationProperties.SetAutomationId(panel, "TaskHistoryTable");
        _historyTable.Child = panel;
    }

    private void AddHistoryRow(Grid table, AdminTaskExecution execution)
    {
        var id = execution.Id ?? "";
        var result = AdminTasksText.ResultText(execution);
        var expanded = result != null && _expanded.Contains(id);
        var started = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (result != null) started.Children.Add(AdminMonitorUi.Icon(expanded ? "chevron-down" : "chevron-right", 14, AdminMonitorUi.Foreground));
        started.Children.Add(AdminSettingsUi.Text(DateTimeDisplay.FormatDateTime(execution.StartedAt, seconds: true), 14));
        var startedCell = new Border { Child = started, Padding = new(16, 8, 16, 8) };
        var error = AdminTasksText.HistoryError(execution);
        var errorText = AdminSettingsUi.Text(error, 14, true);
        errorText.TextTrimming = TextTrimming.CharacterEllipsis; errorText.TextWrapping = TextWrapping.NoWrap;
        ToolTipService.SetToolTip(errorText, error);
        var badge = new Border { Child = StatusBadge(execution), Padding = new(16, 8, 16, 8) };
        var row = table.RowDefinitions.Count;
        AddTableRow(table, header: false, [startedCell, Cell(AdminTasksText.DetailDuration(execution.DurationMs)), badge, new Border { Child = errorText, Padding = new(16, 8, 16, 8) }]);
        var background = table.Children.OfType<Border>().First(child => Grid.GetRow(child) == row && Grid.GetColumnSpan(child) == table.ColumnDefinitions.Count);
        if (execution.Status == "failed") background.Background = AdminMonitorUi.Solid(AdminMonitorUi.Red500, .05);
        AutomationProperties.SetAutomationId(background, "TaskHistoryRow_" + id);
        if (result != null)
        {
            startedCell.Child = null;
            var toggle = new Button
            {
                Content = started, Padding = new(0), MinHeight = 0, BorderThickness = new(0), HorizontalAlignment = HorizontalAlignment.Left,
                Background = AdminMonitorUi.Solid(AdminMonitorUi.Foreground, 0), UseSystemFocusVisuals = true,
            };
            startedCell.Child = toggle;
            AutomationProperties.SetName(toggle, DateTimeDisplay.FormatDateTime(execution.StartedAt, seconds: true));
            AutomationProperties.SetAutomationId(toggle, "TaskHistoryToggle_" + id);
            toggle.Click += (_, _) => { if (!_expanded.Remove(id)) _expanded.Add(id); if (_detail != null) RenderHistory(_detail.State); };
            if (expanded)
            {
                var text = new TextBlock { Text = result, FontFamily = AdminMonitorUi.Mono, FontSize = 12, Foreground = AdminSettingsUi.Brush("SecondaryTextBrush"), IsTextSelectionEnabled = true };
                var detail = new Border { Child = text, Padding = new(16, 12, 16, 12), Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .08), BorderBrush = AdminMonitorUi.Solid(AdminMonitorUi.Border, .6), BorderThickness = new(0, 0, 0, 1) };
                AutomationProperties.SetAutomationId(detail, "TaskHistoryResult_" + id);
                AddSpanningRow(table, detail);
            }
        }
    }

    private void ClearHistoryTable()
    {
        _historyTable.Child = null;
        _renderedHistory = "";
        _expanded.Clear();
    }

    private void LayoutDetail()
    {
        Grid.SetRow(_detailActions, _narrow ? 1 : 0); Grid.SetColumn(_detailActions, _narrow ? 0 : 1);
        _detailActions.HorizontalAlignment = _narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private void RelayoutDetail()
    {
        LayoutDetail();
        if (_detail == null) return;
        _renderedDetailMetrics = null;
        _renderedHistory = "";
        RenderDetail(_detail.State);
    }

    private void ClearDetail()
    {
        _detailContent.Visibility = Visibility.Collapsed;
        _unavailable.Visibility = Visibility.Collapsed;
        _detailLoading.Visibility = Visibility.Collapsed;
        _queueHealthBody.Child = null; _renderedDetailMetrics = null;
        _scanNote.Child = null; _renderedLibrariesLink = null;
        ClearHistoryTable();
        ClearSchedule();
    }
}
