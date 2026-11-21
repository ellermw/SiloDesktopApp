using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// One global configuration entry's form (official PluginConfigForm and SchemaForm). The draft owns the values; this view
/// only draws them. Opening it never marks it edited and sends nothing: WinUI reports a control's starting value as a
/// change, and the draft compares values with the starting ones instead of counting events. A secret the server does not
/// return is never filled in; typed replacements live in the draft and its password boxes until the form is disposed.
/// </summary>
internal sealed class AdminPluginConfigView : UserControl, IDisposable
{
    private sealed record FieldRow(AdminPluginField Field, StackPanel Row, TextBlock Error, FrameworkElement Control);
    private sealed record SectionRow(AdminPluginFormSection Section, StackPanel Box, StackPanel Fields, Button? Toggle, TextBlock Title);

    private readonly AdminPluginConfigDraft _draft;
    private readonly Func<AdminPluginConfigWrite, Task<AdminPluginWriteOutcome>> _save;
    private readonly Func<AdminPluginConfigWrite, Task<AdminPluginCheck?>>? _test;
    private readonly StackPanel _root = Stack(16);
    private readonly Dictionary<string, FieldRow> _rows = new(StringComparer.Ordinal);
    private readonly List<SectionRow> _sections = [];
    private readonly List<(Border Group, List<FieldRow> Rows)> _switchGroups = [];
    private readonly Dictionary<string, bool> _opened = new(StringComparer.Ordinal);
    private readonly List<PasswordBox> _passwords = [];
    private readonly List<Action> _secretLabels = [];
    private readonly TextBlock _result = Text("", 13);
    private readonly TextBlock _summary = Text("", 13);
    private Button? _saveButton;
    private Button? _testButton;
    private bool _syncing = true;
    private bool _busy;
    private bool _disposed;
    private int _checkSerial;

    public string SchemaKey => _draft.Schema.Key;
    public string Fingerprint => _draft.Fingerprint;
    public bool IsDirty => !_disposed && _draft.IsDirty;

    internal AdminPluginConfigView(AdminPluginConfigDraft draft, Func<AdminPluginConfigWrite, Task<AdminPluginWriteOutcome>> save, Func<AdminPluginConfigWrite, Task<AdminPluginCheck?>>? test, bool saving = false)
    {
        _draft = draft; _save = save; _test = test; Content = _root; IsTabStop = false;
        AutomationProperties.SetAutomationId(this, "PluginConfig_" + draft.Schema.Key);
        if (draft.Form is not { } form)
        {
            _root.Children.Add(Text(AdminPluginsText.T("web.admin.plugins.configForm.unsupportedSchema"), 13, true)); _syncing = false; return;
        }
        var grouped = form.Sections.SelectMany(s => s.FieldKeys).ToHashSet(StringComparer.Ordinal);
        var ungrouped = form.Fields.Where(f => !grouped.Contains(f.Key)).ToList();
        if (ungrouped.Count > 0) _root.Children.Add(FieldList(ungrouped));
        foreach (var section in form.Sections)
        {
            var fields = section.FieldKeys.Select(key => form.Fields.FirstOrDefault(f => f.Key == key)).Where(f => f != null).Cast<AdminPluginField>().ToList();
            if (fields.Count == 0) continue;
            var box = Stack(12);
            var head = new Grid { ColumnSpacing = 8 };
            head.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var titles = Stack(2); var title = Text(section.Title, 14); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; titles.Children.Add(title);
            if (!string.IsNullOrEmpty(section.Description)) titles.Children.Add(Text(section.Description, 12, true));
            head.Children.Add(titles);
            Button? toggle = null;
            var fieldsHost = FieldList(fields);
            if (section.Collapsible)
            {
                toggle = AdminSettingsUi.Action(AdminPluginsText.T("web.common.schemaForm.hideSection"), "PluginConfigSection_" + draft.Schema.Key + "_" + section.Key);
                Grid.SetColumn(toggle, 1); head.Children.Add(toggle);
            }
            box.Children.Add(head); box.Children.Add(fieldsHost);
            var row = new SectionRow(section, Stack(0), fieldsHost, toggle, title);
            var border = Panel(box, 8, 16, border: true); row.Box.Children.Add(border);
            if (toggle != null) toggle.Click += (_, _) => { _opened[section.Key] = !SectionOpen(section, false); Refresh(); };
            _sections.Add(row); _root.Children.Add(row.Box);
        }
        if (draft.SavedSecrets.Count > 0) _root.Children.Add(SecretsBox());
        var actions = new LogsWrapPanel { Spacing = 12, RowSpacing = 8 };
        if (test != null)
        {
            _testButton = AdminSettingsUi.Action(AdminPluginsText.T("web.admin.common.connectionCheck.check"), "PluginConfigTest_" + draft.Schema.Key, secondary: true);
            _testButton.Click += async (_, _) => await TestAsync(); actions.Children.Add(_testButton);
        }
        _saveButton = AdminSettingsUi.Action(string.IsNullOrWhiteSpace(form.SubmitLabel) ? AdminPluginsText.T("web.admin.plugins.configForm.save") : form.SubmitLabel!, "PluginConfigSave_" + draft.Schema.Key);
        _saveButton.Click += async (_, _) => await SaveAsync(); actions.Children.Add(_saveButton);
        _root.Children.Add(actions);
        AutomationProperties.SetLiveSetting(_result, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite); AutomationProperties.SetAutomationId(_result, "PluginConfigResult_" + draft.Schema.Key);
        AutomationProperties.SetLiveSetting(_summary, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
        _root.Children.Add(_result); _root.Children.Add(_summary);
        _syncing = false; Refresh(); SetBusy(saving);
    }

    // ── layout ─────────────────────────────────────────────────────────────

    private StackPanel FieldList(IReadOnlyList<AdminPluginField> fields)
    {
        var list = Stack(16); var run = new List<AdminPluginField>();
        void Flush()
        {
            if (run.Count == 0) return;
            var rows = new List<FieldRow>(); var inner = Stack(0);
            foreach (var field in run) { var row = SwitchRow(field); rows.Add(row); inner.Children.Add(row.Row); }
            var group = Panel(inner, 8, 0, border: true); _switchGroups.Add((group, rows)); list.Children.Add(group); run = [];
        }
        foreach (var field in fields)
        {
            if (field.Control == "SWITCH") { run.Add(field); continue; }
            Flush(); var row = PlainRow(field); list.Children.Add(row.Row);
        }
        Flush();
        return list;
    }

    private static string LabelOf(AdminPluginField field) => string.IsNullOrEmpty(field.Label) ? field.Key : field.Label;
    private string Id(AdminPluginField field) => "PluginConfigField_" + _draft.Schema.Key + "_" + field.Key;

    private (StackPanel Head, TextBlock Error) Heading(AdminPluginField field, bool withLabel)
    {
        var head = Stack(4);
        if (withLabel) head.Children.Add(Text(LabelOf(field), 14));
        if (!string.IsNullOrEmpty(field.Description)) head.Children.Add(Text(field.Description, 12, true));
        var error = Text("", 12); error.Foreground = AdminMonitorUi.Solid(AdminMonitorUi.Destructive); error.Visibility = Visibility.Collapsed;
        return (head, error);
    }

    private FieldRow PlainRow(AdminPluginField field)
    {
        var row = Stack(8); var (head, error) = Heading(field, true);
        var control = BuildControl(field); row.Children.Add(head); row.Children.Add(control);
        if (_draft.IsManaged(field.Key)) row.Children.Add(Text(AdminPluginsText.T("web.admin.plugins.provisioning.managed"), 12, true));
        row.Children.Add(error);
        if (field.ShowWhen.Count > 0) row.Margin = new(12, 0, 0, 0);
        var result = new FieldRow(field, row, error, control); _rows[field.Key] = result; return result;
    }

    private FieldRow SwitchRow(AdminPluginField field)
    {
        var row = Stack(4); row.Padding = new(14, 12, 14, 12);
        var toggle = (ToggleSwitch)BuildControl(field); toggle.Header = null;
        var line = new Grid { ColumnSpacing = 12 };
        line.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var (head, error) = Heading(field, true); line.Children.Add(head); Grid.SetColumn(toggle, 1); line.Children.Add(toggle);
        row.Children.Add(line);
        if (_draft.IsManaged(field.Key)) row.Children.Add(Text(AdminPluginsText.T("web.admin.plugins.provisioning.managed"), 12, true));
        row.Children.Add(error);
        var result = new FieldRow(field, row, error, toggle); _rows[field.Key] = result; return result;
    }

    private FrameworkElement BuildControl(AdminPluginField field)
    {
        var label = LabelOf(field); var id = Id(field); var locked = _draft.IsManaged(field.Key);
        FrameworkElement control;
        if (field.Control == "SELECT")
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
            var current = _draft.Get(field) as string ?? "";
            ComboBoxItem? selected = null;
            foreach (var option in field.Options)
            {
                var item = new ComboBoxItem { Content = option.Label, Tag = option.Value }; combo.Items.Add(item);
                if (option.Value == current) selected = item;
            }
            // A saved value the list no longer offers stays visible and stays the value until the admin picks another.
            if (selected == null && current.Length > 0) { selected = new ComboBoxItem { Content = current, Tag = current }; combo.Items.Add(selected); }
            if (field.Options.Count == 0 && selected == null) combo.PlaceholderText = string.IsNullOrEmpty(field.Placeholder) ? AdminPluginsText.T("web.common.schemaForm.selectPlaceholder") : field.Placeholder;
            if (selected != null) combo.SelectedItem = selected;
            else combo.PlaceholderText = string.IsNullOrEmpty(field.Placeholder) ? AdminPluginsText.T("web.common.schemaForm.selectPlaceholder") : field.Placeholder;
            combo.SelectionChanged += (_, _) => { if (!_syncing && combo.SelectedItem is ComboBoxItem { Tag: string value }) Set(field, value); };
            control = combo;
        }
        else if (field.Control == "MULTI_SELECT")
        {
            var selected = (_draft.Get(field) as List<string> ?? []).ToHashSet(StringComparer.Ordinal);
            var panel = Stack(6);
            if (field.Options.Count == 0) panel.Children.Add(Text(AdminPluginsText.T("web.common.schemaForm.noOptions"), 12, true));
            foreach (var option in field.Options)
            {
                var check = new CheckBox { Content = option.Label, IsChecked = selected.Contains(option.Value), MinWidth = 0 };
                AutomationProperties.SetName(check, option.Label);
                void Changed() { if (_syncing) return; if (check.IsChecked == true) selected.Add(option.Value); else selected.Remove(option.Value); Set(field, field.Options.Where(o => selected.Contains(o.Value)).Select(o => o.Value).ToList()); }
                check.Checked += (_, _) => Changed(); check.Unchecked += (_, _) => Changed(); panel.Children.Add(check);
            }
            control = panel;
        }
        else if (field.Control == "SWITCH")
        {
            var toggle = new ToggleSwitch { IsOn = _draft.Get(field) is true, OnContent = "", OffContent = "", MinWidth = 0 };
            toggle.Toggled += (_, _) => { if (!_syncing) Set(field, toggle.IsOn); }; control = toggle;
        }
        else if (AdminPluginConfigDraft.IsSecret(field))
        {
            var saved = _draft.SavedSecrets.Contains(field.Key);
            var secret = new PasswordBox
            {
                PasswordRevealMode = PasswordRevealMode.Hidden, HorizontalAlignment = HorizontalAlignment.Stretch, MaxLength = 16384, MinWidth = 0,
                PlaceholderText = saved ? AdminPluginsText.T("web.admin.plugins.configForm.savedSecretPlaceholder") : field.Placeholder ?? "",
            };
            secret.PasswordChanged += (_, _) => { if (!_syncing) Set(field, secret.Password); };
            _passwords.Add(secret); control = secret;
        }
        else
        {
            var multiline = field.Multiline || field.Control == "TEXTAREA";
            var box = new TextBox
            {
                Text = _draft.Get(field) as string ?? "", PlaceholderText = field.Placeholder ?? "", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0,
                AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            };
            if (multiline) { box.MinHeight = (field.Rows > 0 ? Math.Min(field.Rows, 20) : 4) * 22; box.MaxHeight = 360; }
            if (field.Control == "NUMBER") box.InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) } };
            box.TextChanged += (_, _) => { if (!_syncing) Set(field, box.Text); };
            control = box;
        }
        AutomationProperties.SetName(control, label); AutomationProperties.SetAutomationId(control, id);
        if (control is Control native) native.IsEnabled = !locked;
        else if (locked && control is Microsoft.UI.Xaml.Controls.Panel panelControl) foreach (var child in panelControl.Children.OfType<Control>()) child.IsEnabled = false;
        return control;
    }

    private FrameworkElement SecretsBox()
    {
        var box = Stack(8);
        foreach (var key in _draft.SavedSecrets)
        {
            var field = _draft.Fields.FirstOrDefault(f => f.Key == key);
            var label = field == null ? AdminPluginPresentation.HumanizeConfigKey(key) : LabelOf(field);
            var line = new Grid { ColumnSpacing = 12 };
            line.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var status = Text("", 12, true); line.Children.Add(status);
            var canClear = _draft.CanClear(key); Button? clear = null;
            if (canClear)
            {
                clear = AdminSettingsUi.Action("", "PluginConfigClear_" + _draft.Schema.Key + "_" + key); Grid.SetColumn(clear, 1); line.Children.Add(clear);
                clear.Click += (_, _) => { _draft.ToggleClear(key); _result.Text = ""; Refresh(); };
            }
            _secretLabels.Add(() =>
            {
                var clearing = _draft.IsClearing(key);
                status.Text = AdminPluginsText.T("web.admin.plugins.configForm.savedSecretStatus", new { label, state = clearing ? "cleared" : "saved", required = field?.Required == true ? "yes" : "no" });
                if (clear != null)
                {
                    var text = AdminPluginsText.T(clearing ? "web.admin.plugins.configForm.keepSecret" : "web.admin.plugins.configForm.clearSecret");
                    clear.Content = text; AutomationProperties.SetName(clear, text + ": " + label);
                }
            });
            box.Children.Add(line);
        }
        return Panel(box, 8, 10, border: true);
    }

    // ── state ──────────────────────────────────────────────────────────────

    private void Set(AdminPluginField field, object? value)
    {
        if (_syncing || _disposed || _busy) return;
        _draft.Set(field, value); _result.Text = ""; _summary.Text = ""; _checkSerial++; Refresh();
    }

    private bool SectionOpen(AdminPluginFormSection section, bool forceOpen) => forceOpen || (_opened.TryGetValue(section.Key, out var open) ? open : !section.CollapsedDefault);

    /// <summary>Re-draws what depends on the values: visibility by condition, errors, section state and the secret rows.</summary>
    private void Refresh()
    {
        if (_disposed) return;
        var errors = _draft.Errors();
        foreach (var row in _rows.Values)
        {
            var visible = row.Field.ShowWhen.Count == 0 || _draft.Conditions(row.Field.ShowWhen);
            row.Row.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible && errors.TryGetValue(row.Field.Key, out var message)) { row.Error.Text = message; row.Error.Visibility = Visibility.Visible; }
            else { row.Error.Text = ""; row.Error.Visibility = Visibility.Collapsed; }
        }
        foreach (var (group, rows) in _switchGroups) group.Visibility = rows.Any(row => row.Row.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var section in _sections)
        {
            var visible = _draft.Conditions(section.Section.ShowWhen);
            section.Box.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            // A section holding an unresolved error opens, so setup cannot hide behind a collapsed section.
            var failing = section.Section.FieldKeys.Any(errors.ContainsKey);
            var collapsible = section.Section.Collapsible; var open = !collapsible || SectionOpen(section.Section, failing);
            section.Fields.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (section.Toggle != null)
            {
                var text = AdminPluginsText.T(open ? "web.common.schemaForm.hideSection" : "web.common.schemaForm.showSection");
                section.Toggle.Content = text; AutomationProperties.SetName(section.Toggle, text + ": " + section.Section.Title);
            }
        }
        foreach (var update in _secretLabels) update();
        if (_saveButton != null) _saveButton.IsEnabled = !_busy && !_draft.AllManaged;
    }

    /// <summary>Disables the form while a write or check is in flight, as the official fieldset does.</summary>
    internal void SetBusy(bool busy)
    {
        _busy = busy; IsEnabled = !busy; if (_saveButton != null) _saveButton.IsEnabled = !busy && !_draft.AllManaged;
    }

    private AdminPluginConfigWrite? BuildWrite()
    {
        var errors = _draft.Errors();
        if (errors.Count > 0)
        {
            _summary.Text = string.Join("  ", errors.Values.Take(3));
            // Nothing is sent while a visible field has an error; the first one takes focus so it can be fixed.
            if (_rows.TryGetValue(errors.Keys.First(), out var first)) first.Control.Focus(FocusState.Programmatic);
            return null;
        }
        _summary.Text = "";
        return _draft.Build();
    }

    private async Task SaveAsync()
    {
        if (_disposed || _busy) return;
        var write = BuildWrite(); if (write == null) return;
        SetBusy(true); _result.Text = "";
        try { await _save(write); }
        finally { write.Value.Clear(); write.ClearSecrets.Clear(); if (!_disposed) SetBusy(false); }
    }

    private async Task TestAsync()
    {
        if (_disposed || _busy || _test == null) return;
        var write = BuildWrite(); if (write == null) return;
        var serial = ++_checkSerial; SetBusy(true);
        if (_testButton != null) _testButton.Content = AdminPluginsText.T("web.admin.common.connectionCheck.checking");
        AdminPluginCheck? check = null;
        try { check = await _test(write); }
        finally
        {
            write.Value.Clear(); write.ClearSecrets.Clear();
            if (!_disposed)
            {
                SetBusy(false); if (_testButton != null) _testButton.Content = AdminPluginsText.T("web.admin.common.connectionCheck.check");
                // The result belongs to the values that were tested: an edit since then drops it.
                if (check != null && serial == _checkSerial)
                {
                    _result.Text = check.Message; _result.Foreground = AdminMonitorUi.Solid(check.Success ? AdminMonitorUi.Emerald400 : AdminMonitorUi.Destructive);
                }
            }
        }
    }

    public void FocusFirst()
    {
        foreach (var section in _sections) if (section.Section.Collapsible) _opened[section.Section.Key] = true;
        Refresh();
        _rows.Values.FirstOrDefault(row => row.Row.Visibility == Visibility.Visible)?.Control.Focus(FocusState.Programmatic);
    }

    /// <summary>Erases every typed value: password boxes, text boxes and the draft. Typed secrets do not outlive the form.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _syncing = true;
        foreach (var password in _passwords) password.Password = "";
        foreach (var row in _rows.Values) if (row.Control is TextBox box) box.Text = "";
        _draft.Dispose(); _rows.Clear(); _sections.Clear(); _passwords.Clear(); _secretLabels.Clear(); _switchGroups.Clear(); _opened.Clear();
        _result.Text = ""; _summary.Text = "";
    }
}
