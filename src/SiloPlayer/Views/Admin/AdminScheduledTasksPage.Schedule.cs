using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The schedule section (official TaskSchedulePanel and TriggerFormRow): the saved triggers, or an editor whose
/// rows keep their controls by stable row identity so typing, focus and the picked unit survive every render.
/// </summary>
public sealed partial class AdminScheduledTasksPage
{
    private sealed class TriggerRowView
    {
        internal readonly long Id;
        internal readonly Border Root = new() { CornerRadius = new(12), Padding = new(12) };
        internal readonly ComboBox Type;
        internal readonly StackPanel Interval = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        internal readonly TextBox IntervalBox;
        internal readonly ComboBox Unit;
        internal readonly StackPanel Weekly = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        internal readonly ComboBox Day;
        internal readonly TimePicker Time = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 1, MinWidth = 140 };
        internal readonly TextBox Runtime;
        internal readonly Button Remove;
        internal readonly StackPanel Fields = new() { Spacing = 8 };

        internal TriggerRowView(long id)
        {
            Id = id;
            Type = AdminSettingsUi.Select(AdminTasksText.Message("configure.native.tasks.triggerType"), "TriggerType_" + id,
            [
                (AdminTaskTriggerTypes.Interval, AdminTasksText.Message("web.admin.tasks.detail.triggerType.interval")),
                (AdminTaskTriggerTypes.Daily, AdminTasksText.Message("web.admin.tasks.detail.triggerType.daily")),
                (AdminTaskTriggerTypes.Weekly, AdminTasksText.Message("web.admin.tasks.detail.triggerType.weekly")),
                (AdminTaskTriggerTypes.Startup, AdminTasksText.Message("web.admin.tasks.detail.triggerType.startup")),
            ]);
            Type.Width = 160; Type.HorizontalAlignment = HorizontalAlignment.Left;
            IntervalBox = AdminSettingsUi.Input(AdminTasksText.Message("configure.native.tasks.interval"), "TriggerInterval_" + id, number: true);
            IntervalBox.Width = 100;
            Unit = AdminSettingsUi.Select(AdminTasksText.Message("configure.native.tasks.intervalUnit"), "TriggerUnit_" + id,
            [
                (nameof(AdminTaskIntervalUnit.Seconds), AdminTasksText.Message("web.admin.tasks.detail.intervalUnit.seconds")),
                (nameof(AdminTaskIntervalUnit.Minutes), AdminTasksText.Message("web.admin.tasks.detail.intervalUnit.minutes")),
                (nameof(AdminTaskIntervalUnit.Hours), AdminTasksText.Message("web.admin.tasks.detail.intervalUnit.hours")),
            ]);
            Unit.Width = 120;
            Interval.Children.Add(IntervalBox); Interval.Children.Add(Unit);
            Day = AdminSettingsUi.Select(AdminTasksText.Message("configure.native.tasks.dayOfWeek"), "TriggerDay_" + id,
                Enumerable.Range(0, 7).Select(day => (day.ToString(System.Globalization.CultureInfo.InvariantCulture), AdminTasksText.Weekday(day))));
            Day.Width = 140;
            AutomationProperties.SetName(Time, AdminTasksText.Message("configure.native.tasks.time"));
            AutomationProperties.SetAutomationId(Time, "TriggerTime_" + id);
            Runtime = AdminSettingsUi.Input(AdminTasksText.Message("web.admin.tasks.detail.maxRuntimeLabel"), "TriggerRuntime_" + id,
                AdminTasksText.Message("web.admin.tasks.detail.maxRuntimeNone"), number: true);
            Runtime.Width = 100;
            var runtimeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            runtimeRow.Children.Add(AdminSettingsUi.Text(AdminTasksText.Message("web.admin.tasks.detail.maxRuntimeLabel"), 12, true));
            runtimeRow.Children.Add(Runtime);
            runtimeRow.Children.Add(AdminSettingsUi.Text(AdminTasksText.Message("web.admin.tasks.detail.maxRuntimeUnit"), 12, true));
            Fields.Children.Add(Type); Fields.Children.Add(Interval); Fields.Children.Add(Weekly); Fields.Children.Add(runtimeRow);
            Remove = new Button { Content = AdminMonitorUi.Icon("x", 16, AdminMonitorUi.Foreground), Width = 36, Height = 36, MinWidth = 0, MinHeight = 0, Padding = new(0), CornerRadius = new(10), VerticalAlignment = VerticalAlignment.Top };
            if (Application.Current.Resources.TryGetValue("GhostButtonStyle", out var style) && style is Style ghost) { Remove.Style = ghost; Remove.Width = 36; Remove.Height = 36; Remove.Padding = new(0); }
            AutomationProperties.SetName(Remove, AdminTasksText.Message("configure.native.tasks.removeTrigger"));
            AutomationProperties.SetAutomationId(Remove, "TriggerRemove_" + id);
            var layout = new Grid { ColumnSpacing = 12 };
            layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); layout.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            layout.Children.Add(Fields); Grid.SetColumn(Remove, 1); layout.Children.Add(Remove);
            Root.Child = layout;
            Root.Background = AdminMonitorUi.Solid(AdminMonitorUi.Muted, .06);
            AutomationProperties.SetAutomationId(Root, "TriggerRow_" + id);
        }
    }

    private readonly StackPanel _scheduleSection = new() { Spacing = 12 };
    private readonly TextBlock _scheduleError = AdminSettingsUi.Text("", 14);
    private readonly TextBlock _manualOnly = AdminSettingsUi.Text(AdminTasksText.Message("web.admin.tasks.detail.manualOnly"), 14, true);
    private readonly StackPanel _scheduleList = new();
    private Border? _scheduleView;
    private Border? _editor;
    private readonly StackPanel _triggerRows = new() { Spacing = 12 };
    private readonly Dictionary<long, TriggerRowView> _triggerViews = [];
    private readonly StackPanel _review = new() { Spacing = 8 };
    private readonly StackPanel _candidate = new() { Spacing = 8 };
    private readonly StackPanel _candidateList = new() { Spacing = 4 };
    private Button? _editSchedule, _addTrigger, _reviewSchedule, _useRevision, _saveSchedule, _cancelSchedule;
    private bool _syncingSchedule;

    private void ComposeSchedule()
    {
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(SectionHeading(AdminTasksText.Message("web.admin.tasks.detail.schedule")));
        _editSchedule = AdminSettingsUi.Action(AdminTasksText.Message("web.admin.tasks.detail.editSchedule"), "TaskEditSchedule");
        _editSchedule.Click += async (_, _) => { if (_detail != null) await _detail.BeginEditAsync(_detail.State.Owner); };
        Grid.SetColumn(_editSchedule, 1); header.Children.Add(_editSchedule);
        _scheduleSection.Children.Add(header);

        _scheduleError.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        AutomationProperties.SetAutomationId(_scheduleError, "TaskScheduleError");
        AutomationProperties.SetLiveSetting(_scheduleError, AutomationLiveSetting.Assertive);
        _scheduleSection.Children.Add(_scheduleError);
        AutomationProperties.SetAutomationId(_manualOnly, "TaskManualOnly");
        _scheduleSection.Children.Add(_manualOnly);
        _scheduleView = AdminMonitorUi.Panel(_scheduleList, new Thickness(16, 8, 16, 8));
        AutomationProperties.SetAutomationId(_scheduleView, "TaskScheduleView");
        _scheduleSection.Children.Add(_scheduleView);

        var editor = new StackPanel { Spacing = 12 };
        editor.Children.Add(_triggerRows);
        _addTrigger = AdminSettingsUi.Action("+ " + AdminTasksText.Message("web.admin.tasks.detail.addTrigger"), "TaskAddTrigger");
        AutomationProperties.SetName(_addTrigger, AdminTasksText.Message("web.admin.tasks.detail.addTrigger"));
        _addTrigger.HorizontalAlignment = HorizontalAlignment.Left;
        _addTrigger.Click += (_, _) => _detail?.UpdateDraft(draft => draft.Add());
        editor.Children.Add(_addTrigger);

        _reviewSchedule = AdminSettingsUi.Action(AdminTasksText.Message("web.admin.tasks.detail.reviewSchedule"), "TaskReviewSchedule");
        _reviewSchedule.HorizontalAlignment = HorizontalAlignment.Left;
        _reviewSchedule.Click += async (_, _) => { if (_detail != null) await _detail.ReviewAsync(_detail.State.Owner); };
        _useRevision = AdminSettingsUi.Action(AdminTasksText.Message("web.admin.tasks.detail.useRevision"), "TaskUseRevision", primary: true);
        _useRevision.HorizontalAlignment = HorizontalAlignment.Left;
        _useRevision.Click += (_, _) => _detail?.AdoptRevision(_detail.State.Owner);
        _candidate.Children.Add(AdminSettingsUi.Text(AdminTasksText.Message("web.admin.tasks.detail.currentSavedSchedule"), 14));
        _candidate.Children.Add(_candidateList);
        _candidate.Children.Add(_useRevision);
        AutomationProperties.SetAutomationId(_candidate, "TaskScheduleCandidate");
        _review.Children.Add(_reviewSchedule); _review.Children.Add(_candidate);
        editor.Children.Add(_review);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _saveSchedule = AdminSettingsUi.Action(AdminTasksText.Message("common.save"), "TaskSaveSchedule", primary: true);
        _saveSchedule.Click += async (_, _) => { if (_detail != null) await _detail.SaveAsync(_detail.State.Owner); };
        _cancelSchedule = AdminSettingsUi.Action(AdminTasksText.Message("common.cancel"), "TaskCancelSchedule");
        _cancelSchedule.BorderThickness = new(0);
        _cancelSchedule.Click += (_, _) => _detail?.CancelEdit();
        actions.Children.Add(_saveSchedule); actions.Children.Add(_cancelSchedule);
        editor.Children.Add(actions);
        _editor = AdminMonitorUi.Panel(editor, new Thickness(16));
        AutomationProperties.SetAutomationId(_editor, "TaskScheduleEditor");
        _scheduleSection.Children.Add(_editor);
    }

    private void RenderSchedule(AdminTaskDetailState state, AdminTaskInfo? task)
    {
        if (_editSchedule == null) return;
        _scheduleSection.Visibility = task == null ? Visibility.Collapsed : Visibility.Visible;
        if (task == null) { ClearSchedule(); return; }
        var editing = state.Editing && state.Draft != null;
        _editSchedule.Visibility = !task.ManualOnly && !editing ? Visibility.Visible : Visibility.Collapsed;
        _editSchedule.IsEnabled = !state.ScheduleBusy;
        var error = state.EditorError ?? "";
        AdminSettingsUi.UpdateLiveText(_scheduleError, error);
        _scheduleError.Visibility = error.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _manualOnly.Visibility = task.ManualOnly ? Visibility.Visible : Visibility.Collapsed;
        _scheduleView!.Visibility = !task.ManualOnly && !editing ? Visibility.Visible : Visibility.Collapsed;
        RenderTriggerList(_scheduleList, task.Triggers, "TaskTrigger_");
        _editor!.Visibility = editing && !task.ManualOnly ? Visibility.Visible : Visibility.Collapsed;
        if (!editing) { ClearTriggerRows(); return; }
        RenderTriggerRows(state.Draft!, state);
        var locked = state.Saving || state.ScheduleBusy;
        _addTrigger!.IsEnabled = !locked && state.Draft!.CanAdd;
        _review.Visibility = state.NeedsReview ? Visibility.Visible : Visibility.Collapsed;
        _reviewSchedule!.IsEnabled = !locked;
        _candidate.Visibility = state.Candidate != null ? Visibility.Visible : Visibility.Collapsed;
        if (state.Candidate != null) RenderTriggerList(_candidateList, state.Candidate, "TaskCandidateTrigger_");
        _useRevision!.IsEnabled = !locked;
        _saveSchedule!.IsEnabled = state.CanSave;
        _cancelSchedule!.IsEnabled = !locked;
    }

    /// <summary>Long trigger descriptions, or the official empty sentence.</summary>
    private static void RenderTriggerList(StackPanel host, IReadOnlyList<AdminTaskTrigger> triggers, string idPrefix)
    {
        string[] lines = triggers.Count == 0 ? [AdminTasksText.Message("web.admin.tasks.detail.noTriggers")] : triggers.Select(trigger => AdminTasksText.DescribeTrigger(trigger)).ToArray();
        if (host.Children.Count == lines.Length && host.Children.OfType<TextBlock>().Select(text => text.Text).SequenceEqual(lines)) return;
        host.Children.Clear();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = AdminSettingsUi.Text(lines[index], 14);
            line.Margin = new(0, 8, 0, 8);
            AutomationProperties.SetAutomationId(line, idPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            host.Children.Add(line);
        }
    }

    private void RenderTriggerRows(AdminTaskScheduleDraft draft, AdminTaskDetailState state)
    {
        var wanted = draft.Rows.Select(row => row.Id).ToHashSet();
        foreach (var gone in _triggerViews.Keys.Where(id => !wanted.Contains(id)).ToArray())
        {
            _triggerRows.Children.Remove(_triggerViews[gone].Root);
            _triggerViews.Remove(gone);
        }
        var locked = state.Saving || state.ScheduleBusy;
        _syncingSchedule = true;
        try
        {
            for (var index = 0; index < draft.Rows.Count; index++)
            {
                var row = draft.Rows[index];
                if (!_triggerViews.TryGetValue(row.Id, out var view)) { view = NewTriggerRow(row.Id); _triggerViews[row.Id] = view; }
                Place(_triggerRows, view.Root, index);
                SyncTriggerRow(view, row, locked);
            }
        }
        finally { _syncingSchedule = false; }
    }

    private TriggerRowView NewTriggerRow(long id)
    {
        var view = new TriggerRowView(id);
        view.Type.SelectionChanged += (_, _) =>
        {
            if (_syncingSchedule || view.Type.SelectedItem is not ComboBoxItem { Tag: string type }) return;
            _detail?.UpdateDraft(draft => draft.SetType(id, type));
        };
        view.IntervalBox.TextChanged += (_, _) =>
        {
            if (_syncingSchedule) return;
            var unit = SelectedUnit(view);
            var text = view.IntervalBox.Text;
            _detail?.UpdateDraft(draft => draft.SetInterval(id, text, unit));
        };
        view.Unit.SelectionChanged += (_, _) =>
        {
            if (_syncingSchedule) return;
            var unit = SelectedUnit(view);
            var text = view.IntervalBox.Text;
            _detail?.UpdateDraft(draft => draft.SetInterval(id, text, unit));
        };
        view.Day.SelectionChanged += (_, _) =>
        {
            if (_syncingSchedule || view.Day.SelectedIndex is < 0 or > 6) return;
            var day = view.Day.SelectedIndex;
            _detail?.UpdateDraft(draft => draft.SetDay(id, day));
        };
        view.Time.SelectedTimeChanged += (_, args) =>
        {
            if (_syncingSchedule || args.NewTime is not { } time) return;
            var text = $"{time.Hours:00}:{time.Minutes:00}";
            _detail?.UpdateDraft(draft => draft.SetTime(id, text));
        };
        view.Runtime.TextChanged += (_, _) =>
        {
            if (_syncingSchedule) return;
            var text = view.Runtime.Text;
            _detail?.UpdateDraft(draft => draft.SetRuntime(id, text));
        };
        view.Remove.Click += (_, _) => _detail?.UpdateDraft(draft => draft.Remove(id));
        return view;
    }

    private static AdminTaskIntervalUnit SelectedUnit(TriggerRowView view) =>
        view.Unit.SelectedItem is ComboBoxItem { Tag: string tag } && Enum.TryParse<AdminTaskIntervalUnit>(tag, out var unit) ? unit : AdminTaskIntervalUnit.Minutes;

    /// <summary>Shows the draft row without overwriting what was typed: text boxes change only when the draft holds different text.</summary>
    private static void SyncTriggerRow(TriggerRowView view, AdminTaskTriggerRow row, bool locked)
    {
        var trigger = row.Trigger;
        var typeIndex = AdminTaskTriggerTypes.All.ToList().IndexOf(trigger.Type);
        if (view.Type.SelectedIndex != typeIndex) view.Type.SelectedIndex = typeIndex;
        view.Interval.Visibility = trigger.Type == AdminTaskTriggerTypes.Interval ? Visibility.Visible : Visibility.Collapsed;
        if (view.IntervalBox.Text != row.IntervalText) view.IntervalBox.Text = row.IntervalText;
        var unitIndex = (int)row.Unit;
        if (view.Unit.SelectedIndex != unitIndex) view.Unit.SelectedIndex = unitIndex;
        var intervalInvalid = trigger.Type == AdminTaskTriggerTypes.Interval && !row.IntervalValid;
        view.IntervalBox.BorderBrush = intervalInvalid ? AdminMonitorUi.Solid(AdminMonitorUi.Destructive) : AdminMonitorUi.Solid(AdminMonitorUi.Muted, .25);
        AutomationProperties.SetHelpText(view.IntervalBox, intervalInvalid ? AdminTasksText.Message("configure.native.tasks.intervalInvalid") : "");

        var daily = trigger.Type == AdminTaskTriggerTypes.Daily;
        var weekly = trigger.Type == AdminTaskTriggerTypes.Weekly;
        view.Weekly.Visibility = daily || weekly ? Visibility.Visible : Visibility.Collapsed;
        if (weekly && !view.Weekly.Children.Contains(view.Day)) view.Weekly.Children.Insert(0, view.Day);
        if (!weekly && view.Weekly.Children.Contains(view.Day)) view.Weekly.Children.Remove(view.Day);
        if (!view.Weekly.Children.Contains(view.Time)) view.Weekly.Children.Add(view.Time);
        var day = trigger.DayOfWeek ?? 0;
        if (view.Day.SelectedIndex != day) view.Day.SelectedIndex = day;
        var time = ParseTime(trigger.TimeOfDay);
        if (view.Time.SelectedTime != time) view.Time.SelectedTime = time;

        if (view.Runtime.Text != row.RuntimeText) view.Runtime.Text = row.RuntimeText;
        view.Runtime.BorderBrush = row.RuntimeValid ? AdminMonitorUi.Solid(AdminMonitorUi.Muted, .25) : AdminMonitorUi.Solid(AdminMonitorUi.Destructive);
        AutomationProperties.SetHelpText(view.Runtime, row.RuntimeValid ? "" : AdminTasksText.Message("configure.native.tasks.runtimeInvalid"));

        foreach (var control in new Control[] { view.Type, view.IntervalBox, view.Unit, view.Day, view.Time, view.Runtime, view.Remove }) control.IsEnabled = !locked;
    }

    private static TimeSpan? ParseTime(string? value) =>
        value is { Length: 5 } && int.TryParse(value[..2], out var hours) && int.TryParse(value[3..], out var minutes) && hours is >= 0 and < 24 && minutes is >= 0 and < 60
            ? new TimeSpan(hours, minutes, 0) : null;

    private void ClearTriggerRows()
    {
        _triggerRows.Children.Clear();
        _triggerViews.Clear();
    }

    private void ClearSchedule()
    {
        ClearTriggerRows();
        _candidateList.Children.Clear();
        _scheduleList.Children.Clear();
        _scheduleError.Text = "";
    }
}
