using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using U = SiloPlayer.Controls.AdminSettingsUi;
using P = SiloPlayer.Core.Services.Admin.AdminPluginPresentation;
using static SiloPlayer.Views.Admin.LogsNativeUi;

namespace SiloPlayer.Views.Admin;

/// <summary>Complete native manifest form: inferred scalar fields, descriptors, sections, conditions, typed multi-selects and write-only secrets.</summary>
internal sealed class AdminPluginConfigView : StackPanel, IDisposable
{
    private readonly AdminPluginConfigDraft _draft;
    private readonly Dictionary<AdminPluginField, (FrameworkElement Row, Control Input)> _fields = [];
    private readonly List<(AdminPluginFormSection Section, FrameworkElement Element)> _sections = [];
    private readonly List<PasswordBox> _passwords = [];
    private readonly List<Action> _clearLabels = [];
    private readonly Func<AdminPluginConfigWrite, bool, Task<bool>> _submit;
    private readonly TextBlock _error = Text("", 13);
    private bool _syncing = true;
    private bool _disposed;
    public string SchemaKey => _draft.Schema.Key;
    public bool IsDirty => _draft.IsDirty;
    internal AdminPluginConfigView(AdminPluginSchema schema, AdminPluginConfig? saved, Func<AdminPluginConfigWrite, bool, Task<bool>> submit)
    {
        _draft = new(schema, saved); _submit = submit; Spacing = 14;
        AutomationProperties.SetAutomationId(this, "PluginConfig_" + schema.Key);
        if (_draft.Form is not { } form)
        {
            Children.Add(Text("This plugin uses a configuration schema shape that the admin form does not support yet.", 13, true)); return;
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in form.Sections)
        {
            var body = Stack(12);
            if (section.Description.Length > 0) body.Children.Add(Text(P.SafeText(section.Description), 13, true));
            foreach (var key in section.FieldKeys)
                if (form.Fields.FirstOrDefault(f => f.Key == key) is { } field && used.Add(key)) body.Children.Add(BuildField(field));
            FrameworkElement element;
            if (section.Collapsible) element = new Expander { Header = P.SafeText(section.Title), Content = body, IsExpanded = !section.CollapsedDefault, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
            else { var block = Stack(10); block.Children.Add(Text(P.SafeText(section.Title), 14)); block.Children.Add(body); element = block; }
            _sections.Add((section, element)); Children.Add(element);
        }
        foreach (var field in form.Fields) if (used.Add(field.Key)) Children.Add(BuildField(field));
        var actions = new LogsWrapPanel { Spacing = 10, RowSpacing = 10 };
        var test = U.Action("Test connection", "PluginConfigTest_" + schema.Key); test.Click += async (_, _) => await SubmitAsync(true);
        var save = U.Action(string.IsNullOrWhiteSpace(form.SubmitLabel) ? "Save config" : P.SafeText(form.SubmitLabel), "PluginConfigSave_" + schema.Key); save.Click += async (_, _) => await SubmitAsync(false);
        actions.Children.Add(test); actions.Children.Add(save); Children.Add(actions);
        AutomationProperties.SetLiveSetting(_error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Assertive);
        Children.Add(_error); _syncing = false; UpdateConditions();
    }
    private FrameworkElement BuildField(AdminPluginField field)
    {
        var row = Stack(6); var label = P.SafeText(string.IsNullOrWhiteSpace(field.Label) ? field.Key : field.Label);
        if (field.Required) label += " *";
        var id = "PluginConfigField_" + _draft.Schema.Key + "_" + field.Key;
        row.Children.Add(Text(label, 13)); if (field.Description.Length > 0) row.Children.Add(Text(P.SafeText(field.Description), 12, true));
        var value = _draft.Value(field); var text = AdminPluginConfigDraft.Scalar(value); Control input;
        if (AdminPluginConfigDraft.IsSecret(field))
        {
            var secret = new PasswordBox { PlaceholderText = _draft.SavedSecrets.Contains(field.Key) ? "Saved secret — leave blank to keep" : field.Placeholder,
                PasswordRevealMode = PasswordRevealMode.Hidden, HorizontalAlignment = HorizontalAlignment.Stretch, MaxLength = 16384 };
            secret.PasswordChanged += (_, _) => Set(field, secret.Password); _passwords.Add(secret); input = secret;
            if (_draft.SavedSecrets.Contains(field.Key))
            {
                var status = Text("Saved secret", 12, true); var clear = U.Action("Clear saved secret", id + "_Clear"); clear.IsEnabled = !field.Required;
                clear.Click += (_, _) => { _draft.ToggleClear(field); UpdateConditions(); };
                _clearLabels.Add(() => { var clearing = _draft.ClearSecrets.Contains(field.Key); status.Text = clearing ? "Saved secret will be cleared" : "Saved secret"; clear.Content = clearing ? "Keep saved secret" : "Clear saved secret"; });
                row.Children.Add(status); row.Children.Add(clear);
            }
        }
        else if (field.Control == "SWITCH")
        {
            var toggle = new ToggleSwitch { IsOn = text.Equals("true", StringComparison.OrdinalIgnoreCase), OnContent = "On", OffContent = "Off" };
            toggle.Toggled += (_, _) => Set(field, toggle.IsOn); input = toggle;
        }
        else if (field.Control == "SELECT")
        {
            var combo = Select(id, field.Options.Select(o => (o.Value, P.SafeText(o.Label))), double.NaN);
            combo.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (combo.Items.Count == 0) combo.Items.Add(new ComboBoxItem { Content = "", Tag = "" });
            SetSelected(combo, text); combo.SelectionChanged += (_, _) => Set(field, Selected(combo)); input = combo;
        }
        else if (field.Control == "MULTI_SELECT")
        {
            var selected = value.ValueKind == System.Text.Json.JsonValueKind.Array ? value.EnumerateArray().Select(AdminPluginConfigDraft.Scalar).ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 220, IsMultiSelectCheckBoxEnabled = true, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            foreach (var option in field.Options.Concat(selected.Where(s => !field.Options.Any(o => o.Value == s)).Select(s => new AdminPluginOption { Value = s, Label = s })))
            {
                var item = new ListViewItem { Content = P.SafeText(option.Label), Tag = option.Value }; list.Items.Add(item); if (selected.Contains(option.Value)) list.SelectedItems.Add(item);
            }
            list.SelectionChanged += (_, _) => Set(field, list.SelectedItems.Cast<ListViewItem>().Select(item => item.Tag as string ?? "").ToArray()); input = list;
        }
        else
        {
            var box = new TextBox { Text = text, PlaceholderText = field.Placeholder, HorizontalAlignment = HorizontalAlignment.Stretch, MaxLength = 16384,
                AcceptsReturn = field.Multiline || field.Control == "TEXTAREA", TextWrapping = field.Multiline || field.Control == "TEXTAREA" ? TextWrapping.Wrap : TextWrapping.NoWrap };
            if (box.AcceptsReturn) { box.MinHeight = Math.Clamp(field.Rows, 3, 12) * 22; box.MaxHeight = 300; }
            box.TextChanged += (_, _) => Set(field, box.Text); input = box;
        }
        AutomationProperties.SetAutomationId(input, id); AutomationProperties.SetName(input, label); input.MinWidth = 0;
        row.Children.Add(input); _fields[field] = (row, input); return row;
    }
    private void Set(AdminPluginField field, object? value)
    {
        if (_syncing || _disposed) return;
        _draft.Set(field, value); _error.Text = ""; UpdateConditions();
    }
    private void UpdateConditions()
    {
        foreach (var (field, control) in _fields) control.Row.Visibility = _draft.Visible(field) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (section, element) in _sections) element.Visibility = _draft.Conditions(section.ShowWhen) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var update in _clearLabels) update();
    }
    private async Task SubmitAsync(bool test)
    {
        if (_disposed) return;
        try
        {
            var body = _draft.Build(); IsEnabled = false;
            await _submit(body, test);
            // Drop replacements even when the outcome is uncertain. They never become readback or diagnostics.
            if (!test) foreach (var secret in _passwords) secret.Password = "";
            body.Value.Clear(); body.ClearSecrets.Clear();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        { if (!_disposed) _error.Text = "Review the required fields, field types and allowed values before submitting."; }
        finally { if (!_disposed) IsEnabled = true; }
    }
    public void FocusFirst()
    {
        foreach (var (_, element) in _sections) if (element is Expander expander) expander.IsExpanded = true;
        _fields.Values.FirstOrDefault(v => v.Row.Visibility == Visibility.Visible).Input?.Focus(FocusState.Programmatic);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _syncing = true;
        foreach (var password in _passwords) password.Password = "";
        foreach (var control in _fields.Values) { if (control.Input is TextBox box) box.Text = ""; control.Input.IsEnabled = false; }
        _draft.Dispose(); _fields.Clear(); _passwords.Clear(); _clearLabels.Clear(); _sections.Clear();
    }
}
