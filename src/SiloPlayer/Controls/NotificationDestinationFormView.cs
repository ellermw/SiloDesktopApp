using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Controls;

/// <summary>
/// The settings fields of an add or edit destination dialog, drawn from the plugin's flat form
/// (official PluginDestinationsSection + SchemaForm): label, help text, the control, and its error under it.
/// Checks show at once, as in the WebUI; the server's field errors replace them until the next change.
/// All state and rules live in <see cref="NotificationDestinationForm"/>; this only draws and reports edits.
/// A secret is typed into a PasswordBox and is never logged or stored by this control.
/// </summary>
public sealed class NotificationDestinationFormView : StackPanel
{
    private readonly NotificationDestinationForm _form;
    private readonly Dictionary<string, TextBlock> _errors = new(StringComparer.Ordinal);
    private Dictionary<string, string> _serverErrors = new(StringComparer.Ordinal);
    private bool _building;

    public event Action? Changed;

    public NotificationDestinationFormView(NotificationDestinationForm form)
    {
        _form = form; Spacing = 14;
        Rebuild();
    }

    /// <summary>True when every client check passes.</summary>
    public bool IsValid => _form.Validate().Count == 0;

    /// <summary>The server's per-field errors from the last save, shown under their fields until the settings change.</summary>
    public void SetServerErrors(Dictionary<string, string>? errors)
    {
        _serverErrors = errors ?? new(StringComparer.Ordinal);
        RefreshErrors();
    }

    /// <summary>Draws the fields again, as after the kind changed.</summary>
    public void Rebuild()
    {
        _building = true;
        try
        {
            Children.Clear(); _errors.Clear();
            foreach (var view in _form.Fields) Children.Add(Field(view));
            RefreshErrors();
        }
        finally { _building = false; }
    }

    private FrameworkElement Field(DestinationFieldView view)
    {
        var row = new StackPanel { Spacing = 6 };
        var label = new TextBlock { Text = view.Label, FontWeight = Microsoft.UI.Text.FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
        FrameworkElement editor;
        var current = _form.EffectiveText(view);
        switch (view.Control)
        {
            case DestinationControl.Switch:
            {
                var toggle = new ToggleSwitch { IsOn = _form.Effective(view) is true, OnContent = "", OffContent = "", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Right };
                toggle.Toggled += (_, _) => Edit(view, toggle.IsOn);
                var line = new Grid { ColumnSpacing = 12 };
                line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var text = new StackPanel { Spacing = 2 }; text.Children.Add(label);
                if (!string.IsNullOrEmpty(view.Field.Description)) text.Children.Add(Help(view.Field.Description!));
                line.Children.Add(text); Grid.SetColumn(toggle, 1); line.Children.Add(toggle);
                Name(toggle, view);
                row.Children.Add(line); editor = toggle;
                break;
            }
            case DestinationControl.Select:
            {
                row.Children.Add(label);
                if (!string.IsNullOrEmpty(view.Field.Description)) row.Children.Add(Help(view.Field.Description!));
                var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch,
                    PlaceholderText = string.IsNullOrEmpty(view.Field.Placeholder) ? AppStrings.Get("web.common.schemaForm.selectPlaceholder", fallback: "Select") : view.Field.Placeholder };
                foreach (var option in view.Field.Options ?? [])
                {
                    var value = NotificationDestinationForm.OptionValue(option);
                    var item = new ComboBoxItem { Content = option.Label, Tag = value };
                    combo.Items.Add(item);
                    if (value == current) combo.SelectedItem = item;
                }
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string value }) Edit(view, value); };
                Name(combo, view); row.Children.Add(combo); editor = combo;
                break;
            }
            case DestinationControl.Password:
            {
                row.Children.Add(label);
                if (!string.IsNullOrEmpty(view.Field.Description)) row.Children.Add(Help(view.Field.Description!));
                var secret = new PasswordBox { Password = current, PlaceholderText = view.Field.Placeholder ?? "" };
                secret.PasswordChanged += (_, _) => { if (!_building) Edit(view, secret.Password); };
                Name(secret, view); row.Children.Add(secret); editor = secret;
                break;
            }
            default:
            {
                row.Children.Add(label);
                if (!string.IsNullOrEmpty(view.Field.Description)) row.Children.Add(Help(view.Field.Description!));
                var box = new TextBox { Text = current, PlaceholderText = view.Field.Placeholder ?? "", TextWrapping = TextWrapping.NoWrap };
                box.TextChanged += (_, _) => { if (!_building) Edit(view, box.Text); };
                Name(box, view); row.Children.Add(box); editor = box;
                break;
            }
        }
        var error = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["ErrorBrush"], Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(error, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _errors[view.Key] = error; row.Children.Add(error);
        return row;
    }

    private static TextBlock Help(string text) => new() { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] };

    private static void Name(FrameworkElement editor, DestinationFieldView view)
    {
        AutomationProperties.SetName(editor, view.Label);
        AutomationProperties.SetAutomationId(editor, "DestinationField_" + view.Key);
    }

    private void Edit(DestinationFieldView view, object? value)
    {
        if (_building) return;
        // WinUI reports a field's starting text as a change: a field showing the same text is not an edit.
        if (value is string text && _form.EffectiveText(view) == text) return;
        _form.SetValue(view.Key, value);
        // The server's errors and the plugin's refusal belong to the settings as they were sent.
        _serverErrors = new(StringComparer.Ordinal);
        RefreshErrors();
        Changed?.Invoke();
    }

    private void RefreshErrors()
    {
        var merged = _form.Validate();
        foreach (var (key, message) in _serverErrors) merged[key] = message;
        foreach (var (key, block) in _errors)
        {
            var has = merged.TryGetValue(key, out var text);
            block.Text = has ? text! : ""; block.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
