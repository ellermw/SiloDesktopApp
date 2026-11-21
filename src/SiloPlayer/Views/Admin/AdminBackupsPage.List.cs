using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using Present = SiloPlayer.Core.Services.Admin.AdminBackupPresentation;
using UiKit = SiloPlayer.Controls.AdminSettingsUi;

namespace SiloPlayer.Views.Admin;

public sealed partial class AdminBackupsPage
{
    private readonly AdminSettingsSaveBar _saveBar = new("Backups");
    private readonly StackPanel _header = new() { Spacing = 12 };
    private readonly StackPanel _statusArea = new() { Spacing = 6 };
    private readonly StackPanel _savedSection = new() { Spacing = 16 };
    private readonly StackPanel _uploadsSection = new() { Spacing = 12 };
    private readonly StackPanel _settingsArea = new() { Spacing = 20, MaxWidth = 1120 };
    private readonly StackPanel _settingsFields = new() { Spacing = 20 };
    private readonly StackPanel _locationArea = new() { Spacing = 8 };
    private readonly StackPanel _commandArea = new() { Spacing = 8 };
    private readonly HashSet<string> _expandedRows = new(StringComparer.Ordinal);
    private ToggleSwitch? _enabledToggle, _introToggle, _subtitlesToggle;
    private TextBox? _timeBox, _keepBox;
    private AdminLeaveGuard? _leave;
    private AdminBackupUploadSession? _upload;
    private CancellationTokenSource? _downloadCancel;
    private bool _showAll, _syncing, _linkedRowRevealed;
    private string? _selectedId;

    private void ActivateList()
    {
        var controller = App.Services.GetRequiredService<AdminBackupsController>();
        Controller = controller;
        _selectedId = _location?.Get("backup");
        _linkedRowRevealed = false;
        _showAll = false; _expandedRows.Clear();
        _leave = new(() => Controller?.State.Owner, () => Controller?.State.DirtyKeys.Count > 0, () => Controller?.Discard());
        controller.Changed += QueueListRender; controller.Notified += OnNotified; controller.Saved += OnSaved;
        BuildListSkeleton();
        RenderList(controller.State);
    }

    private void QueueListRender()
    {
        var controller = Controller; if (controller == null) return;
        DispatcherQueue.TryEnqueue(() => { if (_active && ReferenceEquals(controller, Controller)) RenderList(controller.State); });
    }

    private void BuildListSkeleton()
    {
        _body.Children.Clear();
        _body.Children.Add(_header);
        _body.Children.Add(_statusArea);
        _body.Children.Add(_savedSection);
        _body.Children.Add(_uploadsSection);
        _body.Children.Add(_settingsArea);
        _body.Children.Add(new Border { Height = 112 });
        if (!_root.Children.Contains(_saveBar)) _root.Children.Add(_saveBar);
        _saveBar.Save.Click -= OnSaveClick; _saveBar.Save.Click += OnSaveClick;
        _saveBar.Discard.Click -= OnDiscardClick; _saveBar.Discard.Click += OnDiscardClick;
        BuildSettingsEditors();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e) { if (Controller is { } controller) await controller.SaveAsync(); }
    private void OnDiscardClick(object sender, RoutedEventArgs e) => Controller?.Discard();

    private void ClearListUi()
    {
        _syncing = true;
        try
        {
            if (_timeBox != null) _timeBox.Text = "";
            if (_keepBox != null) _keepBox.Text = "";
            foreach (var toggle in new[] { _enabledToggle, _introToggle, _subtitlesToggle }) if (toggle != null) toggle.IsOn = false;
        }
        finally { _syncing = false; }
        _header.Children.Clear(); _statusArea.Children.Clear(); _savedSection.Children.Clear(); _uploadsSection.Children.Clear();
        _locationArea.Children.Clear(); _commandArea.Children.Clear();
        _saveBar.Visibility = Visibility.Collapsed;
    }

    // ── Render ───────────────────────────────────────────────────────

    private void RenderList(AdminBackupsState state)
    {
        var current = state.Owner?.IsCurrent == true;
        if (!current) { ClearListUi(); return; }
        var focused = FocusedId();
        var now = DateTimeOffset.Now;
        var list = state.List;
        var items = (IReadOnlyList<AdminBackup>?)list?.Items ?? [];
        var running = AdminBackupsController.Running(list);
        var active = list?.Active;
        var busy = list?.Busy == true || active != null || running;
        var configured = state.Capabilities?.Available ?? true;
        var selective = state.Capabilities?.SelectiveRestore == true;
        var uploads = state.Capabilities is { Upload: true } && state.Capabilities.Available;
        var keep = list?.Keep ?? (long.TryParse(state.PersistedValues.GetValueOrDefault(Present.KeepKey), out var savedKeep) && savedKeep > 0 ? savedKeep : 7);
        var enabled = list?.Enabled ?? state.PersistedValues.GetValueOrDefault(Present.EnabledKey) != "false";
        var lockReason = Present.LockBlocker(list?.Busy == true, active);
        var warnings = (IReadOnlyList<AdminBackupWarning>?)list?.Warnings ?? [];
        // A paused server's next run fails, and a Start time just saved has not reached the task yet: neither line names a run.
        var paused = Present.BackupsPaused(configured, warnings);
        var next = enabled && !paused && !state.ScheduleSettling ? Present.NextBackupRun(now, state.TaskNextRunAt) : null;
        var destinationAvailable = list?.DestinationAvailable ?? true;
        var blocker = state.StartingBackup ? null : state.UnknownOutcome == "backup" ? T("configure.native.backups.unknownOutcomeBlocked")
            : Present.BackUpNowBlocker(configured, lockReason, warnings, destinationAvailable);

        // Header: title, subtitle, Upload and Back up now with the reason it can't run.
        _header.Children.Clear();
        // On a phone-width page (below Tailwind's sm, 640 px) the actions get their own full-width row under the title, and the reason
        // "Back up now" can't run sits right under that button (official PHONE_HEADER_ACTION).
        var phone = ViewportWidth is > 0 and < 640;
        var titleRow = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        titleRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); titleRow.ColumnDefinitions.Add(new() { Width = phone ? new(0) : GridLength.Auto });
        titleRow.RowDefinitions.Add(new() { Height = GridLength.Auto }); titleRow.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var titles = new StackPanel { Spacing = 8 };
        titles.Children.Add(UiKit.Title(T("web.admin.backups.header.title")));
        titles.Children.Add(Text(T("web.admin.backups.header.subtitle"), 14, true));
        titleRow.Children.Add(titles);
        var actions = new StackPanel { Spacing = 4, HorizontalAlignment = phone ? HorizontalAlignment.Stretch : HorizontalAlignment.Right };
        var buttons = new StackPanel { Spacing = 8, HorizontalAlignment = phone ? HorizontalAlignment.Stretch : HorizontalAlignment.Right, Orientation = phone ? Orientation.Vertical : Orientation.Horizontal };
        if (uploads)
        {
            var upload = Action(T("web.admin.backups.header.upload"), "BackupsUpload");
            upload.Click += (_, _) => _ = OpenUploadDialogAsync();
            buttons.Children.Add(upload);
        }
        var backUpNow = BackUpNowButton(state, blocker, list != null);
        buttons.Children.Add(backUpNow);
        if (phone) foreach (var button in buttons.Children.OfType<Button>()) { button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Center; }
        actions.Children.Add(buttons);
        if (blocker != null)
        {
            var reason = Text(blocker, 12, true); reason.MaxWidth = phone ? double.PositiveInfinity : 288; reason.TextAlignment = phone ? TextAlignment.Left : TextAlignment.Right;
            AutomationProperties.SetAutomationId(reason, "BackupsBlocker");
            AutomationProperties.SetHelpText(backUpNow, blocker);
            actions.Children.Add(reason);
        }
        if (phone) { Grid.SetRow(actions, 1); Grid.SetColumnSpan(actions, 2); Grid.SetColumnSpan(titles, 2); }
        else Grid.SetColumn(actions, 1);
        titleRow.Children.Add(actions);
        _header.Children.Add(titleRow);

        // The server lists nothing when it can't read the destination, which says nothing about what the destination holds:
        // the status area's warning says why no backups are listed, so neither "No backups yet." nor the list is drawn.
        var destinationUnread = configured && !destinationAvailable;
        RenderStatus(state, list, now, busy, enabled, paused, next);
        RenderSaved(state, list, items, now, busy, running, keep, next, configured, selective, lockReason, blocker, destinationUnread);
        RenderUploads(state);
        RenderSettings(state, list, items, configured, destinationUnread);
        RestoreFocus(focused);
    }

    private Button BackUpNowButton(AdminBackupsState state, string? blocker, bool listed)
    {
        var button = Action(state.StartingBackup ? T("web.admin.backups.backUpNow.button") + "…" : T("web.admin.backups.backUpNow.button"), "BackupsBackUpNow", primary: true);
        button.IsEnabled = blocker == null && !state.StartingBackup && listed;
        button.Click += async (_, _) => { if (Controller is { } controller) await controller.BackUpNowAsync(); };
        return button;
    }

    private void RenderStatus(AdminBackupsState state, AdminBackupList? list, DateTimeOffset now, bool busy, bool enabled, bool paused, DateTimeOffset? next)
    {
        _statusArea.Children.Clear();
        if (list == null) return;
        AutomationProperties.SetLiveSetting(_statusArea, AutomationLiveSetting.Polite);
        var shown = Present.VisibleWarnings(list.Warnings!, AdminBackupsController.Running(list), list.Items!.Length == 0 && list.LastSuccessAt == null);
        var phrases = Present.SchedulePhrases(list.LastSuccessAt, enabled, paused, busy, next, now);
        // The status line wraps: on a phone-width window the next run goes under the last one instead of running off the page.
        var phoneStatus = ViewportWidth is > 0 and < 640;
        var line = new StackPanel { Orientation = phoneStatus ? Orientation.Vertical : Orientation.Horizontal, Spacing = phoneStatus ? 2 : 8 };
        var lastLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new(4), VerticalAlignment = VerticalAlignment.Center };
        dot.Background = list.LastSuccessAt == null ? UiKit.Brush("SecondaryTextBrush")
            : shown.Any(warning => warning.Code == "no_recent_backup") ? AdminMonitorUi.Solid(AdminMonitorUi.Amber500) : AdminMonitorUi.Solid(Windows.UI.Color.FromArgb(255, 34, 197, 94));
        lastLine.Children.Add(dot);
        var last = Text(phrases.Last); AutomationProperties.SetAutomationId(last, "BackupsLastPhrase"); lastLine.Children.Add(last);
        line.Children.Add(lastLine);
        if (phrases.Next != null)
        {
            var upcoming = Text(phrases.Next, 14, true); AutomationProperties.SetAutomationId(upcoming, "BackupsNextPhrase");
            if (phoneStatus) upcoming.Margin = new(16, 0, 0, 0);
            line.Children.Add(upcoming);
        }
        _statusArea.Children.Add(line);

        if (list.Active is { } active)
        {
            var box = new StackPanel { Spacing = 4, MaxWidth = 448, HorizontalAlignment = HorizontalAlignment.Left };
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var operation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            operation.Children.Add(Text(Present.ActiveOperationText(active)));
            if (active.Operation == "restore" && Present.ActiveRestoreRequest(active) is { } restore)
            {
                var link = new HyperlinkButton { Content = T("web.admin.backups.active.restoreProgressLink"), Padding = new(0) };
                AutomationProperties.SetAutomationId(link, "BackupsActiveRestoreLink");
                link.Click += (_, _) => AdminShellPage.Open(restore);
                operation.Children.Add(link);
            }
            row.Children.Add(operation);
            if (active.Operation == "backup")
            {
                var percent = Text(T("web.admin.backups.active.percent", new { percent = (long)Math.Round(active.Progress ?? 0) }), 14, true);
                Grid.SetColumn(percent, 1); row.Children.Add(percent);
            }
            box.Children.Add(row);
            if (active.Operation == "backup")
            {
                var progress = new ProgressBar { Minimum = 0, Maximum = 100, Value = active.Progress ?? 0 };
                AutomationProperties.SetName(progress, T("web.admin.backups.active.progressLabel"));
                AutomationProperties.SetAutomationId(progress, "BackupsActiveProgress");
                box.Children.Add(progress);
            }
            if (active.Message is { Length: > 0 } message) box.Children.Add(Text(AdminBackupValidation.SafeServerText(message), 12, true));
            _statusArea.Children.Add(box);
        }
        if (Present.LastRunProblem(list.LastRun, now) is { } problem) _statusArea.Children.Add(Status(problem, "warn", "BackupsLastRunWarning"));
        foreach (var warning in shown)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(Status(Present.WarningText(warning), Present.IsInfoWarning(warning) ? "muted" : "warn", "BackupsWarning-" + warning.Code));
            if (warning.Code == "key_not_confirmed" && Present.KeyConfirmationNeeded(list))
            {
                var confirm = Action(T("web.admin.backups.confirmKey.button"), "BackupsConfirmKeyWarning");
                confirm.Click += (_, _) => _ = OpenConfirmKeyAsync();
                row.Children.Add(confirm);
            }
            _statusArea.Children.Add(row);
        }
        if (state.ListError != null)
            _statusArea.Children.Add(Status(T("web.admin.backups.list.loadFailed", new { reason = Present.ErrorMessage(state.ListError) }), "warn", "BackupsRefreshFailed"));
    }

    private void RenderSaved(AdminBackupsState state, AdminBackupList? list, IReadOnlyList<AdminBackup> items, DateTimeOffset now, bool busy, bool running,
        long keep, DateTimeOffset? next, bool configured, bool selective, string? lockReason, string? blocker, bool destinationUnread)
    {
        _savedSection.Children.Clear();
        if (list == null && state.ListError == null)
        {
            AutomationProperties.SetName(_savedSection, T("web.admin.backups.list.loading"));
            _savedSection.Children.Add(UiKit.Skeleton(112)); _savedSection.Children.Add(UiKit.Skeleton(192));
            return;
        }
        if (list == null)
        {
            var retry = Action(T("common.tryAgain"), "BackupsListRetry");
            retry.Click += async (_, _) => { if (Controller is { } controller) await controller.RefreshAsync(); };
            _savedSection.Children.Add(Alert(T("web.admin.backups.list.loadFailed", new { reason = Present.ErrorMessage(state.ListError) }), retry));
            return;
        }
        if (state.UnknownOutcome != null)
        {
            var refresh = Action(T("configure.native.backups.refresh"), "BackupsUnknownRefresh");
            refresh.Click += async (_, _) => { if (Controller is { } controller) await controller.RefreshAsync(); };
            _savedSection.Children.Add(Alert(T("configure.native.backups.unknownOutcomeBlocked"), refresh));
        }
        if (destinationUnread) return;
        if (items.Count == 0 && running)
        {
            var stack = new StackPanel { Spacing = 4 };
            var first = Text(T("web.admin.backups.list.firstRunning")); first.FontWeight = FontWeights.Medium;
            stack.Children.Add(first); stack.Children.Add(Text(T("web.admin.backups.list.firstRunningDetail"), 14, true));
            _savedSection.Children.Add(Surface(stack));
            return;
        }
        if (items.Count == 0)
        {
            var stack = new StackPanel { Spacing = 12 };
            var empty = Text(T("web.admin.backups.list.empty")); empty.FontWeight = FontWeights.Medium;
            stack.Children.Add(empty);
            if (!configured) stack.Children.Add(Text(T("web.admin.backups.list.emptyUnconfigured"), 14, true));
            // While "Back up now" can't run, neither the sentence about it nor the button is offered here: the reason shows under the header's button.
            if (configured && blocker == null)
            {
                stack.Children.Add(Text(T("web.admin.backups.list.emptyConfigured"), 14, true));
                var button = BackUpNowButton(state, blocker, true); AutomationProperties.SetAutomationId(button, "BackupsEmptyBackUpNow"); stack.Children.Add(button);
            }
            _savedSection.Children.Add(Surface(stack));
            return;
        }
        var heading = Heading(T("web.admin.backups.list.heading"));
        _savedSection.Children.Add(heading);
        var plan = Present.RetentionPlan(items, keep);
        _savedSection.Children.Add(Text(Present.RetentionNote(keep, plan, next, running, now), 13, true));
        if (list.Encrypted == true) _savedSection.Children.Add(Text(T("web.admin.backups.list.encrypted"), 13, true));
        if (lockReason != null) _savedSection.Children.Add(Text(T("web.admin.backups.table.deleteBlocked", new { reason = lockReason }), 12, true));
        _savedSection.Children.Add(BuildTable(state, list, items, selective, lockReason));
    }

    private FrameworkElement BuildTable(AdminBackupsState state, AdminBackupList list, IReadOnlyList<AdminBackup> items, bool selective, string? lockReason)
    {
        var linkedIndex = _selectedId != null ? items.ToList().FindIndex(item => item.Id == _selectedId) : -1;
        var showAll = _showAll || linkedIndex >= Present.RowsShown;
        var rows = showAll ? items : items.Take(Present.RowsShown).ToArray();
        var table = new StackPanel { Spacing = 0 };
        // Below sm (640 px) each row stacks into a small grid, date and status on one line, size under the date, actions under both, and the header
        // is gone; the version column shows from md (768 px) on (official BackupTable).
        var phone = ViewportWidth is > 0 and < 640;
        var showVersion = ViewportWidth is <= 0 or >= 768;
        Grid Row()
        {
            var grid = new Grid { ColumnSpacing = 12, Padding = new(12, 10, 12, 10) };
            grid.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            if (showVersion) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = new(1.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            return grid;
        }
        void Cell(Grid grid, UIElement element, int column) { Grid.SetColumn((FrameworkElement)element, column); grid.Children.Add(element); }
        if (!phone)
        {
            var header = Row();
            var headings = showVersion ? new[] { ("taken", 0), ("size", 1), ("siloVersion", 2), ("status", 3), ("actions", 4) } : new[] { ("taken", 0), ("size", 1), ("status", 2), ("actions", 3) };
            foreach (var (key, column) in headings)
            {
                var label = Text(T("web.admin.backups.table." + key), 12, true); label.FontWeight = FontWeights.Medium;
                if (key == "actions") AutomationProperties.SetAccessibilityView(label, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                Cell(header, label, column);
            }
            table.Children.Add(header);
        }
        foreach (var backup in rows)
        {
            var row = phone ? new Grid { ColumnSpacing = 12, RowSpacing = 4, Padding = new(12, 12, 12, 12) } : Row();
            if (phone)
            {
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                for (var phoneRow = 0; phoneRow < 3; phoneRow++) row.RowDefinitions.Add(new() { Height = GridLength.Auto });
            }
            AutomationProperties.SetAutomationId(row, "BackupRow-" + backup.Id);
            if (backup.Id == _selectedId) row.Background = AdminMonitorUi.Solid(AdminMonitorUi.ColorOf("AccentColor", Windows.UI.Color.FromArgb(255, 167, 139, 250)), .08);
            var taken = new StackPanel { Spacing = 2 };
            taken.Children.Add(Text(AdminBackupText.DateTimeText(backup.CreatedAt!.Value)));
            taken.Children.Add(Text(Present.KindLabel(backup.Kind), 12, true));
            var otherKey = Present.EncryptedWithOtherKey(backup);
            if (otherKey)
            {
                taken.Children.Add(Text(Present.OtherKeyNote(backup), 12));
                taken.Children.Add(Text(Present.OtherKeyReason(), 12, true));
            }
            var size = Text(DashboardFormat.FileSize(backup.SizeBytes, Present.NoValue), phone ? 12 : 14, phone);
            var status = StatusCell(backup, list.Active?.BackupId == backup.Id ? list.Active : null);
            var actions = RowActions(state, backup, selective, otherKey, lockReason);
            if (phone)
            {
                Grid.SetRow((FrameworkElement)taken, 0); Grid.SetColumn(taken, 0); row.Children.Add(taken);
                var statusCell = (FrameworkElement)status; statusCell.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetRow(statusCell, 0); Grid.SetColumn(statusCell, 1); row.Children.Add(statusCell);
                Grid.SetRow(size, 1); Grid.SetColumn(size, 0); row.Children.Add(size);
                ((FrameworkElement)actions).HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetRow((FrameworkElement)actions, 2); Grid.SetColumn((FrameworkElement)actions, 0); Grid.SetColumnSpan((FrameworkElement)actions, 2); row.Children.Add(actions);
            }
            else
            {
                size.TextAlignment = TextAlignment.Right;
                var column = 0;
                Cell(row, taken, column++);
                Cell(row, size, column++);
                if (showVersion) Cell(row, Text(Present.KnownSiloVersion(backup.SiloVersion) ?? Present.NoValue, 14, true), column++);
                Cell(row, status, column++);
                Cell(row, actions, column);
            }
            table.Children.Add(new Border { Child = row, BorderThickness = new(0, 1, 0, 0), BorderBrush = UiKit.Brush("BorderBrush") });
            if (_expandedRows.Contains(backup.Id!)) table.Children.Add(new Border { Child = WarningDetails(backup), Padding = new(12, 0, 12, 12) });
        }
        var frame = new StackPanel { Spacing = 12 };
        frame.Children.Add(new Border { Child = table, CornerRadius = new(16), BorderThickness = new(1), BorderBrush = UiKit.Brush("BorderBrush"), Background = UiKit.Brush("SurfaceBrush") });
        if (items.Count > Present.RowsShown)
        {
            var toggle = Action(showAll ? T("web.admin.backups.table.showFewer") : T("web.admin.backups.table.showAll", AdminBackupText.Counted(items.Count)), "BackupsShowAll");
            toggle.Click += (_, _) => { _showAll = !showAll; if (!_showAll) _selectedId = null; QueueListRender(); };
            frame.Children.Add(toggle);
        }
        if (linkedIndex >= 0 && !_linkedRowRevealed)
        {
            var selectedId = _selectedId;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_active || _selectedId != selectedId || _linkedRowRevealed) return;
                if (Find(_body, "BackupRow-" + selectedId) is FrameworkElement target)
                {
                    _linkedRowRevealed = true;
                    target.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = .5 });
                }
            });
        }
        return frame;
    }

    private FrameworkElement StatusCell(AdminBackup backup, AdminBackupActive? active)
    {
        if (active?.Operation == "delete") return Status(T("web.admin.backups.table.deleting"), "muted");
        var label = Present.StatusLabel(backup.Status);
        switch (backup.Status)
        {
            case "complete": return Status(label, "ok", "BackupStatus-" + backup.Id);
            case "complete_with_warnings":
                var stack = new StackPanel { Spacing = 2 };
                stack.Children.Add(Status(label, "warn", "BackupStatus-" + backup.Id));
                if (backup.MissingBlobs!.Length > 0 || backup.RestoreSetOmitted!.Length > 0)
                {
                    var open = _expandedRows.Contains(backup.Id!);
                    stack.Children.Add(Disclosure(T(open ? "web.admin.backups.table.hideDetails" : "web.admin.backups.table.showDetails"), "BackupDetails-" + backup.Id, open, () =>
                    {
                        if (!_expandedRows.Remove(backup.Id!)) _expandedRows.Add(backup.Id!);
                        QueueListRender();
                    }));
                }
                return stack;
            case "in_progress":
                return Status(active?.Operation == "backup" ? T("web.admin.backups.table.statusWithProgress", new { status = label, percent = (long)Math.Round(active.Progress ?? 0) }) : label, "muted", "BackupStatus-" + backup.Id);
            case "unverified" or "unreadable": return Status(label, "warn", "BackupStatus-" + backup.Id);
            default: return Status(label, "muted", "BackupStatus-" + backup.Id);
        }
    }

    private FrameworkElement RowActions(AdminBackupsState state, AdminBackup backup, bool selective, bool otherKey, string? lockReason)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        var taken = AdminBackupText.DateTimeText(backup.CreatedAt!.Value);
        if (selective && Present.CanRestoreParts(backup))
        {
            var restore = Action(AdminBackupText.Visible("web.admin.backups.table.restore", new { taken }), "BackupRestore-" + backup.Id);
            AutomationProperties.SetName(restore, AdminBackupText.Accessible("web.admin.backups.table.restore", new { taken }));
            restore.IsEnabled = !otherKey;
            if (otherKey) AutomationProperties.SetHelpText(restore, Present.OtherKeyReason());
            restore.Click += (_, _) => AdminShellPage.Open(Present.RestoreRequest(backup.Id!));
            actions.Children.Add(restore);
        }
        if (Present.IsFinished(backup))
        {
            var preparing = state.PreparingDownloadId == backup.Id;
            var download = Action(T("web.admin.backups.download.button") + (preparing ? "…" : ""), "BackupDownload-" + backup.Id);
            download.IsEnabled = state.PreparingDownloadId == null;
            download.Click += (_, _) => _ = OpenDownloadDialogAsync(backup);
            actions.Children.Add(download);
        }
        if (backup.Status != "in_progress")
        {
            var delete = Action("✕", "BackupDelete-" + backup.Id);
            AutomationProperties.SetName(delete, T("web.admin.backups.table.deleteLabel", new { taken }));
            ToolTipService.SetToolTip(delete, T("web.admin.backups.table.deleteLabel", new { taken }));
            delete.IsEnabled = lockReason == null && state.DeletingBackupId == null;
            if (lockReason != null) AutomationProperties.SetHelpText(delete, T("web.admin.backups.table.deleteBlocked", new { reason = lockReason }));
            delete.Click += (_, _) => _ = ConfirmDeleteBackupAsync(backup);
            actions.Children.Add(delete);
        }
        return actions;
    }

    /// <summary>Official BackupWarningDetails: omitted accounts and missing files, grouped, with a plain-text copy.</summary>
    private FrameworkElement WarningDetails(AdminBackup backup)
    {
        var stack = new StackPanel { Spacing = 12, Padding = new(16), CornerRadius = new(12), Background = UiKit.Brush("AppBackgroundBrush") };
        AutomationProperties.SetAutomationId(stack, "BackupWarningDetails-" + backup.Id);
        if (backup.RestoreSetOmitted is { Length: > 0 } omitted) stack.Children.Add(OmittedAccounts(omitted, restore: false, "BackupOmitted-" + backup.Id));
        if (backup.MissingBlobs is { Length: > 0 } missing)
        {
            var key = "missing:" + backup.Id;
            var open = _expandedRows.Contains(key);
            stack.Children.Add(Status(Present.MissingHeadline(missing.Length, backup.MissingBlobsTotal ?? missing.Length), "warn"));
            stack.Children.Add(Disclosure(T(open ? "web.admin.backups.warnings.hideFiles" : "web.admin.backups.warnings.showFiles", Amount(missing.Length)), "BackupMissingFiles-" + backup.Id, open, () =>
            {
                if (!_expandedRows.Remove(key)) _expandedRows.Add(key);
                QueueListRender();
            }));
            if (open)
            {
                stack.Children.Add(Text(T("web.admin.backups.warnings.missingExplanation"), 13, true));
                foreach (var group in Present.GroupMissingBlobs(missing))
                {
                    var groupKey = key + ":" + group.Source;
                    var groupOpen = _expandedRows.Contains(groupKey);
                    var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    line.Children.Add(Text(Present.MissingGroupTitle(group), 13));
                    line.Children.Add(Disclosure(T(groupOpen ? "web.admin.backups.warnings.hideGroupFiles" : "web.admin.backups.warnings.showGroupFiles", Amount(group.Keys.Count)),
                        "BackupMissingGroup-" + group.Source, groupOpen, () => { if (!_expandedRows.Remove(groupKey)) _expandedRows.Add(groupKey); QueueListRender(); }));
                    stack.Children.Add(line);
                    if (groupOpen) stack.Children.Add(ScrollList(group.Keys));
                }
            }
        }
        var copy = Action(T("web.admin.backups.warnings.copy"), "BackupWarningsCopy-" + backup.Id);
        copy.Click += (_, _) => Copy(Present.WarningDetailsReport(backup), T("web.admin.backups.warnings.copied"), T("web.admin.backups.warnings.copyFailed"));
        stack.Children.Add(copy);
        return stack;
    }

    /// <summary>
    /// Picks a toggle label's wording for the number of items it reveals. The label prints no number, so the catalog's select matches exact
    /// counts (official amount): exactly one, exactly two, or any other number.
    /// </summary>
    internal static object Amount(int count) => new { amount = count == 1 ? "exactlyOne" : count == 2 ? "exactlyTwo" : "other" };

    private static FrameworkElement ScrollList(IEnumerable<string> lines)
    {
        var text = Text(string.Join("\n", lines), 12, true);
        text.IsTextSelectionEnabled = true; text.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
        return new ScrollViewer { Content = text, MaxHeight = 192, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>Official OmittedAccounts, worded for the Backups page or the restore chooser.</summary>
    private FrameworkElement OmittedAccounts(IReadOnlyList<AdminBackupOmitted> omitted, bool restore, string id)
    {
        var stack = new StackPanel { Spacing = 8 };
        var key = "omitted:" + id;
        var open = _expandedRows.Contains(key);
        stack.Children.Add(Status(Present.OmittedHeadline(omitted.Count, restore), "warn"));
        stack.Children.Add(Disclosure(T(open ? "web.admin.backups.warnings.hideAccounts" : "web.admin.backups.warnings.showAccounts", Amount(omitted.Count)), id, open, () =>
        {
            if (!_expandedRows.Remove(key)) _expandedRows.Add(key);
            if (restore) QueueRestoreRender(); else QueueListRender();
        }));
        if (!open) return stack;
        foreach (var group in Present.GroupOmissions(omitted))
        {
            var groupKey = key + ":" + group.Reason + ":" + group.Cause + ":" + group.Detail;
            var groupOpen = _expandedRows.Contains(groupKey);
            var box = new StackPanel { Spacing = 2, Margin = new(0, 4, 0, 0) };
            box.Children.Add(Text(Present.OmissionGroupTitle(group), 13));
            var detail = Text(group.Detail, 12, true); detail.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas");
            detail.TextTrimming = groupOpen ? TextTrimming.None : TextTrimming.CharacterEllipsis; detail.TextWrapping = groupOpen ? TextWrapping.Wrap : TextWrapping.NoWrap;
            ToolTipService.SetToolTip(detail, group.Detail);
            box.Children.Add(detail);
            box.Children.Add(Text(Present.NameSample(group.Labels), 12, true));
            if (group.Labels.Count > Present.NamesShown)
                box.Children.Add(Disclosure(groupOpen ? T("web.admin.backups.warnings.hideNames") : T("web.admin.backups.warnings.showAllNames", AdminBackupText.Counted(group.Labels.Count)),
                    id + "-names-" + group.Labels.Count, groupOpen, () => { if (!_expandedRows.Remove(groupKey)) _expandedRows.Add(groupKey); if (restore) QueueRestoreRender(); else QueueListRender(); }));
            if (groupOpen) box.Children.Add(ScrollList(group.Labels));
            stack.Children.Add(box);
        }
        return stack;
    }

    private void RenderUploads(AdminBackupsState state)
    {
        _uploadsSection.Children.Clear();
        var shown = (state.Uploads ?? []).Where(upload => upload.Status != "rejected").ToArray();
        if (shown.Length == 0) return;
        _uploadsSection.Children.Add(Heading(T("web.admin.backups.uploads.heading")));
        _uploadsSection.Children.Add(Text(T("web.admin.backups.uploads.description"), 13, true));
        var list = new StackPanel { Spacing = 0 };
        foreach (var upload in shown)
        {
            var label = Present.UploadLabel(upload);
            var row = new Grid { ColumnSpacing = 12, Padding = new(16, 12, 16, 12) };
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            AutomationProperties.SetAutomationId(row, "UploadRow-" + upload.Id);
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(Text(label));
            text.Children.Add(Text(Present.UploadDetails(upload), 12, true));
            row.Children.Add(text);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (upload.Status == "complete")
            {
                var restore = Action(AdminBackupText.Visible("web.admin.backups.uploads.restore", new { label }), "UploadRestore-" + upload.Id);
                AutomationProperties.SetName(restore, AdminBackupText.Accessible("web.admin.backups.uploads.restore", new { label }));
                restore.Click += (_, _) => AdminShellPage.Open(Present.UploadRestoreRequest(upload.Id!));
                actions.Children.Add(restore);
            }
            if (Present.FullyReceived(upload))
            {
                var check = Action(AdminBackupText.Visible("web.admin.backups.uploads.checkAgain", new { label }), "UploadCheckAgain-" + upload.Id);
                AutomationProperties.SetName(check, AdminBackupText.Accessible("web.admin.backups.uploads.checkAgain", new { label }));
                check.IsEnabled = state.CheckingUploadId == null;
                check.Click += async (_, _) => { if (Controller is { } controller) await controller.CheckUploadAgainAsync(upload.Id!); };
                actions.Children.Add(check);
            }
            var delete = Action("✕", "UploadDelete-" + upload.Id);
            AutomationProperties.SetName(delete, T("web.admin.backups.uploads.deleteLabel", new { label }));
            ToolTipService.SetToolTip(delete, T("web.admin.backups.uploads.deleteLabel", new { label }));
            delete.IsEnabled = state.DeletingUploadId == null;
            delete.Click += (_, _) => _ = ConfirmDeleteUploadAsync(upload, label);
            actions.Children.Add(delete);
            Grid.SetColumn(actions, 1); row.Children.Add(actions);
            list.Children.Add(new Border { Child = row, BorderThickness = new(0, list.Children.Count == 0 ? 0 : 1, 0, 0), BorderBrush = UiKit.Brush("BorderBrush") });
        }
        _uploadsSection.Children.Add(new Border { Child = list, CornerRadius = new(16), BorderThickness = new(1), BorderBrush = UiKit.Brush("BorderBrush"), Background = UiKit.Brush("SurfaceBrush") });
    }

    // ── Settings ─────────────────────────────────────────────────────

    private readonly Dictionary<string, AdminSettingsRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _rowErrors = new(StringComparer.Ordinal);
    private AdminSettingsGroup? _scheduleGroup, _contentGroup;

    private void BuildSettingsEditors()
    {
        _settingsArea.Children.Clear(); _settingsFields.Children.Clear(); _rows.Clear(); _rowErrors.Clear();
        _scheduleGroup = new(T("web.admin.backups.schedule.group"), T("web.admin.backups.schedule.groupDescription"));
        _enabledToggle = Toggle(Present.EnabledKey, T("web.admin.backups.schedule.enabled"));
        AddRow(_scheduleGroup, Present.EnabledKey, T("web.admin.backups.schedule.enabled"), T("web.admin.backups.schedule.enabledDescription"), _enabledToggle);
        _timeBox = UiKit.UseInputEdge(UiKit.Input(T("web.admin.backups.schedule.time"), "BackupsTime", "04:00"));
        _timeBox.TextChanged += (_, _) => { if (!_syncing) Controller?.Set(Present.TimeKey, _timeBox.Text.Trim()); };
        AddRow(_scheduleGroup, Present.TimeKey, T("web.admin.backups.schedule.time"), T("web.admin.backups.schedule.timeDescription"), _timeBox);
        _keepBox = UiKit.UseInputEdge(UiKit.Input(T("web.admin.backups.schedule.keep"), "BackupsKeep", number: true));
        _keepBox.TextChanged += (_, _) => { if (!_syncing) Controller?.Set(Present.KeepKey, _keepBox.Text.Trim()); };
        AddRow(_scheduleGroup, Present.KeepKey, T("web.admin.backups.schedule.keep"), T("web.admin.backups.schedule.keepDescription"), _keepBox, T("web.admin.backups.schedule.keepUnit"));
        _contentGroup = new(T("web.admin.backups.content.group"), T("web.admin.backups.content.groupDescription"));
        _introToggle = Toggle(Present.IntroKey, T("web.admin.backups.content.intro"));
        AddRow(_contentGroup, Present.IntroKey, T("web.admin.backups.content.intro"), T("web.admin.backups.content.introDescription"), _introToggle);
        _subtitlesToggle = Toggle(Present.SubtitlesKey, T("web.admin.backups.content.subtitles"));
        AddRow(_contentGroup, Present.SubtitlesKey, T("web.admin.backups.content.subtitles"), T("web.admin.backups.content.subtitlesDescription"), _subtitlesToggle);
        _settingsFields.Children.Add(_scheduleGroup); _settingsFields.Children.Add(_contentGroup);
        _settingsFields.Children.Add(_locationArea); _settingsFields.Children.Add(_commandArea);
        _settingsArea.Children.Add(_settingsFields);
    }

    private ToggleSwitch Toggle(string key, string label)
    {
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 };
        AutomationProperties.SetName(toggle, label);
        AutomationProperties.SetAutomationId(toggle, "Backups-" + key);
        toggle.Toggled += (_, _) => { if (!_syncing) Controller?.Set(key, toggle.IsOn ? "true" : "false"); };
        return toggle;
    }

    private void AddRow(AdminSettingsGroup group, string key, string label, string description, FrameworkElement editor, string? unit = null)
    {
        var row = new AdminSettingsRow(label, description, editor, unit);
        var error = Text("", 13); error.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); error.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        row.AddStatus(error);
        _rows[key] = row; _rowErrors[key] = error;
        group.Add(row);
    }

    private void RenderSettings(AdminBackupsState state, AdminBackupList? list, IReadOnlyList<AdminBackup> items, bool configured, bool destinationUnread)
    {
        _settingsArea.Children.Clear();
        if (state.SettingsLoading && !state.SettingsLoaded)
        {
            var loading = new StackPanel { Spacing = 16 };
            AutomationProperties.SetName(loading, T("web.admin.backups.settings.loading"));
            loading.Children.Add(UiKit.Skeleton(40)); loading.Children.Add(UiKit.Skeleton(40));
            _settingsArea.Children.Add(loading);
            _saveBar.Visibility = Visibility.Collapsed;
            return;
        }
        if (state.SettingsLoadFailed && !state.SettingsLoaded)
        {
            var retry = Action(T("common.tryAgain"), "BackupsSettingsRetry");
            retry.Click += async (_, _) => { if (Controller is { } controller) await controller.ReloadSettingsAsync(preserveDraft: false); };
            _settingsArea.Children.Add(Alert(T("web.admin.backups.settings.loadFailed"), retry));
            _saveBar.Visibility = Visibility.Collapsed;
            return;
        }
        if (state.Conflict || state.SettingsOutcomeUnknown || state.SettingsError != null)
        {
            var reload = Action(T("web.admin.settings.notifications.reload"), "BackupsSettingsReload");
            reload.Click += async (_, _) => { if (Controller is { } controller) await controller.ReloadSettingsAsync(preserveDraft: true); };
            var message = state.Conflict ? T("configure.native.backups.settingsChanged") : state.SettingsOutcomeUnknown ? T("configure.native.backups.unknownOutcome") : state.SettingsError!;
            _settingsArea.Children.Add(Alert(message, state.SettingsError != null && !state.Conflict && !state.SettingsOutcomeUnknown ? null : reload));
        }
        _settingsArea.Children.Add(_settingsFields);
        _syncing = true;
        try
        {
            bool Editable(string key) => state.SettingsLoaded && !state.Saving && !state.ManagedByEnv.Contains(key);
            string Value(string key) => state.Values.GetValueOrDefault(key, "");
            _enabledToggle!.IsOn = Value(Present.EnabledKey) != "false"; _enabledToggle.IsEnabled = Editable(Present.EnabledKey);
            _introToggle!.IsOn = Value(Present.IntroKey) == "true"; _introToggle.IsEnabled = Editable(Present.IntroKey);
            _subtitlesToggle!.IsOn = Value(Present.SubtitlesKey) != "false"; _subtitlesToggle.IsEnabled = Editable(Present.SubtitlesKey);
            if (_timeBox!.Text.Trim() != Value(Present.TimeKey)) _timeBox.Text = Value(Present.TimeKey);
            _timeBox.IsEnabled = Editable(Present.TimeKey);
            if (_keepBox!.Text.Trim() != Value(Present.KeepKey)) _keepBox.Text = Value(Present.KeepKey);
            _keepBox.IsEnabled = Editable(Present.KeepKey);
        }
        finally { _syncing = false; }
        foreach (var (key, row) in _rows)
        {
            row.State(state.DirtyKeys.Contains(key), false);
            var error = state.Errors.TryGetValue(key, out var code) ? T(code == "keep" ? "configure.native.backups.keepInvalid" : "configure.native.backups.timeInvalid") : "";
            UiKit.UpdateLiveText(_rowErrors[key], error);
            _rowErrors[key].Visibility = error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        _scheduleGroup!.State(Present.ScheduleKeys.Any(state.DirtyKeys.Contains), "");
        _contentGroup!.State(Present.ContentKeys.Any(state.DirtyKeys.Contains), "");
        RenderLocation(state, list, items, listShown: !destinationUnread);
        RenderCommand(state, items, configured);
        var writable = state.DirtyKeys.Count > 0 && state.Errors.Count == 0;
        _saveBar.State(state.DirtyKeys.Count, state.Saving, writable && !state.Conflict && !state.SettingsOutcomeUnknown && !state.SettingsLoading);
        _saveBar.SetAvailableWidth(ActualWidth);
    }

    private void RenderLocation(AdminBackupsState state, AdminBackupList? list, IReadOnlyList<AdminBackup> items, bool listShown)
    {
        _locationArea.Children.Clear();
        var destinations = state.Capabilities?.Destinations;
        var s3Offered = destinations?.Contains("s3") ?? false;
        var location = Present.DefaultLocation(destinations?.Contains("default") ?? true, list?.Destination, items, state.PersistedValues.GetValueOrDefault(Present.PrivateBucketKey, ""));
        var saved = list?.Destination ?? (state.PersistedValues.GetValueOrDefault(Present.DestinationKey) == "s3" ? "s3" : "default");
        var draft = state.DirtyKeys.Contains(Present.DestinationKey) ? (state.Values.GetValueOrDefault(Present.DestinationKey) == "s3" ? "s3" : "default") : saved;
        var needsKey = Present.KeyConfirmationNeeded(list);
        var s3Configured = state.PersistedValues.GetValueOrDefault(Present.S3Endpoint, "").Trim().Length > 0 && state.PersistedValues.GetValueOrDefault(Present.S3Bucket, "").Trim().Length > 0;
        var (ownLabel, ownDescription) = location switch
        {
            AdminBackupLocation.None => ("web.admin.backups.location.onThisServer", "web.admin.backups.location.noneDescription"),
            AdminBackupLocation.Bucket => ("web.admin.backups.location.bucket", "web.admin.backups.location.bucketDescription"),
            _ => ("web.admin.backups.location.onThisServer", "web.admin.backups.location.localDescription"),
        };
        Button ConfirmKey(string id)
        {
            var button = Action(T("web.admin.backups.confirmKey.button"), id);
            button.Click += (_, _) => _ = OpenConfirmKeyAsync();
            return button;
        }
        Button StorageLink()
        {
            var button = Action(T("web.admin.backups.location.storageSettings"), "BackupsStorageSettings");
            var request = new AdminRouteRequest(AdminRoute.StorageSettings);
            button.IsEnabled = AdminShellPage.CanOpen(request);
            button.Click += (_, _) => AdminShellPage.Open(request);
            return button;
        }
        var group = new AdminSettingsGroup(T("web.admin.backups.location.group"));
        group.State(state.DirtyKeys.Contains(Present.DestinationKey), "");
        if (!s3Offered)
        {
            FrameworkElement editor = location == AdminBackupLocation.None ? StorageLink() : needsKey ? ConfirmKey("BackupsConfirmKeyLocation") : new Border();
            group.Add(new AdminSettingsRow(location == AdminBackupLocation.None ? T("web.admin.backups.location.nowhere") : T(ownLabel), T(ownDescription), editor, nameEditor: false));
            _locationArea.Children.Add(group);
            return;
        }
        void Choose(string value)
        {
            if (value == "s3" && saved != "s3") { _ = OpenS3SetupAsync(); return; }
            if (value == saved) Controller?.Reset(Present.DestinationKey);
            else Controller?.Set(Present.DestinationKey, value);
        }
        RadioButton Option(string value, string label, string description, bool enabled, string id)
        {
            var content = new StackPanel { Spacing = 2 };
            var title = Text(label); title.FontWeight = FontWeights.Medium;
            content.Children.Add(title); content.Children.Add(Text(description, 13, true));
            var radio = new RadioButton { Content = content, GroupName = "BackupsDestination", IsChecked = draft == value, IsEnabled = enabled && !state.Saving && state.SettingsLoaded };
            AutomationProperties.SetName(radio, label); AutomationProperties.SetHelpText(radio, description); AutomationProperties.SetAutomationId(radio, id);
            radio.Checked += (_, _) => { if (!_syncing) Choose(value); };
            return radio;
        }
        _syncing = true;
        try
        {
            var options = new StackPanel { Spacing = 12, Padding = new(16) };
            AutomationProperties.SetName(options, T("web.admin.backups.location.group"));
            var own = new StackPanel { Spacing = 8 };
            own.Children.Add(Option("default", T(ownLabel), T(ownDescription), location != AdminBackupLocation.None, "BackupsDestinationDefault"));
            if (location == AdminBackupLocation.None) own.Children.Add(StorageLink());
            else if (saved == "default" && needsKey) own.Children.Add(ConfirmKey("BackupsConfirmKeyDefault"));
            options.Children.Add(own);
            var bucket = new StackPanel { Spacing = 8 };
            bucket.Children.Add(Option("s3", T("web.admin.backups.location.s3"), T("web.admin.backups.location.s3Description"), true, "BackupsDestinationS3"));
            var bucketActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(28, 0, 0, 0) };
            if (saved == "s3" && needsKey) bucketActions.Children.Add(ConfirmKey("BackupsConfirmKeyS3"));
            var setUp = Action(T(s3Configured ? "web.admin.backups.location.change" : "web.admin.backups.location.setUp"), "BackupsSetUpS3");
            setUp.IsEnabled = list != null && state.SettingsLoaded;
            setUp.Click += (_, _) => _ = OpenS3SetupAsync();
            bucketActions.Children.Add(setUp);
            bucket.Children.Add(bucketActions);
            options.Children.Add(bucket);
            group.Add(options);
        }
        finally { _syncing = false; }
        _locationArea.Children.Add(group);
        // The note points at the list above; while the destination can't be read there is no list.
        if (listShown) _locationArea.Children.Add(Text(T("web.admin.backups.location.previousStays"), 13, true));
    }

    private void RenderCommand(AdminBackupsState state, IReadOnlyList<AdminBackup> items, bool configured)
    {
        _commandArea.Children.Clear();
        if (!configured) return;
        bool Verified(AdminBackup backup) => Present.IsFinished(backup) && backup.Status != "unverified";
        var named = items.FirstOrDefault(item => item.Id == _selectedId && Verified(item)) ?? items.FirstOrDefault(Verified);
        var command = Present.RestoreCommand(named?.Location);
        var source = named == null ? "download" : Present.IsBucketLocation(named.Location!) ? "bucket" : "folder";
        var group = new AdminSettingsGroup(T("web.admin.backups.command.group"), T("web.admin.backups.command.groupDescription"));
        var box = new StackPanel { Spacing = 8, Padding = new(16) };
        var line = new Grid { ColumnSpacing = 8 };
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var code = Text(command, 13); code.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"); code.IsTextSelectionEnabled = true;
        AutomationProperties.SetAutomationId(code, "BackupsRestoreCommand");
        line.Children.Add(code);
        var copy = Action("⧉", "BackupsCopyCommand");
        AutomationProperties.SetName(copy, T("web.admin.backups.command.copy")); ToolTipService.SetToolTip(copy, T("web.admin.backups.command.copy"));
        copy.Click += (_, _) => Copy(command, T("web.admin.backups.command.copied"), T("web.admin.backups.command.copyFailed"));
        Grid.SetColumn(copy, 1); line.Children.Add(copy);
        box.Children.Add(line);
        box.Children.Add(Rich(AdminBackupText.Runs("web.admin.backups.command.runHelp", new { hint = Present.CommandHint(source, named?.Encrypted ?? false) }), 13, true));
        group.Add(box);
        _commandArea.Children.Add(group);
    }
}
