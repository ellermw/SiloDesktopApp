using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>Schema fields retain drafts while conditions/validation change.</summary>
public sealed class ProviderSchemaForm : StackPanel
{
    private readonly PluginAdminForm _form;
    private readonly Dictionary<string, object?> _values;
    private readonly Dictionary<FrameworkElement, IEnumerable<PluginAdminFormCondition>?> _visibility = [];
    private readonly TextBlock _errors = new() { TextWrapping = TextWrapping.Wrap };
    public event Action? Changed;
    public bool IsValid => ProviderConnectionConfig.Validate(_form, _values).Count == 0;
    public ProviderSchemaForm(PluginAdminForm form, Dictionary<string, object?> values)
    {
        _form = form; _values = values; Spacing = 12;
        var sectionKeys = new HashSet<string>();
        foreach (var section in form.Sections ?? [])
        {
            var fields = new StackPanel { Spacing = 10 };
            foreach (var key in section.FieldKeys)
                if (form.Fields.FirstOrDefault(field => field.Key == key) is { } field) { fields.Children.Add(Field(field)); sectionKeys.Add(key); }
            FrameworkElement container = section.Collapsible
                ? new Expander { Header = section.Title, IsExpanded = !section.CollapsedDefault, Content = fields, HorizontalAlignment = HorizontalAlignment.Stretch }
                : new StackPanel { Spacing = 8, Children = { new TextBlock { Text = section.Title }, fields } };
            _visibility[container] = section.ShowWhen; Children.Add(container);
        }
        foreach (var field in form.Fields.Where(field => !sectionKeys.Contains(field.Key))) Children.Add(Field(field));
        Children.Add(_errors); Refresh();
    }
    private FrameworkElement Field(PluginAdminFormField field)
    {
        var row = new StackPanel { Spacing = 5 };
        row.Children.Add(new TextBlock { Text = field.Label, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(field.Description)) row.Children.Add(new TextBlock { Text = field.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var value = ProviderConnectionConfig.Scalar(_values.GetValueOrDefault(field.Key) ?? field.DefaultValue);
        void Set(object? next) { _values[field.Key] = next; Refresh(); Changed?.Invoke(); }
        switch (field.Control.ToUpperInvariant())
        {
            case "TOGGLE": case "SWITCH": case "CHECKBOX":
                var toggle = new ToggleSwitch { IsOn = value == "true", OnContent = "", OffContent = "" };
                toggle.Toggled += (_, _) => Set(toggle.IsOn); row.Children.Add(toggle); break;
            case "SELECT":
                var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var option in field.Options ?? [])
                { var item = new ComboBoxItem { Content = option.Label, Tag = option.Value }; combo.Items.Add(item); if (option.Value == value) combo.SelectedItem = item; }
                if (combo.SelectedItem == null && value.Length > 0) { var retained = new ComboBoxItem { Content = value, Tag = value }; combo.Items.Add(retained); combo.SelectedItem = retained; }
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string selected }) Set(selected); };
                row.Children.Add(combo); break;
            case "MULTI_SELECT":
                var current = _values.GetValueOrDefault(field.Key) ?? field.DefaultValue;
                var selectedValues = new HashSet<string>(current is System.Text.Json.JsonElement element && element.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? element.EnumerateArray().Select(DeviceSettingDefinition.Scalar) : current as IEnumerable<string> ?? []);
                foreach (var option in field.Options ?? [])
                { var check = new CheckBox { Content = option.Label, IsChecked = selectedValues.Contains(option.Value) };
                    check.Checked += (_, _) => { selectedValues.Add(option.Value); Set(selectedValues.ToArray()); };
                    check.Unchecked += (_, _) => { selectedValues.Remove(option.Value); Set(selectedValues.ToArray()); }; row.Children.Add(check); }
                break;
            default:
                if (field.Secret || field.Control.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase))
                { var secret = new PasswordBox { Password = value, PlaceholderText = field.Placeholder ?? "" }; secret.PasswordChanged += (_, _) => Set(secret.Password); row.Children.Add(secret); }
                else
                { var text = new TextBox { Text = value, PlaceholderText = field.Placeholder ?? "", AcceptsReturn = field.Multiline || field.Control.Equals("TEXTAREA", StringComparison.OrdinalIgnoreCase), TextWrapping = TextWrapping.Wrap }; text.TextChanged += (_, _) => Set(text.Text); row.Children.Add(text); }
                break;
        }
        _visibility[row] = field.ShowWhen;
        return row;
    }
    private void Refresh()
    {
        foreach (var (element, conditions) in _visibility) element.Visibility = ProviderConnectionConfig.Shown(conditions, ProviderConnectionConfig.EffectiveValues(_form, _values)) ? Visibility.Visible : Visibility.Collapsed;
        _errors.Text = string.Join("\n", ProviderConnectionConfig.Validate(_form, _values).Values);
    }
}
