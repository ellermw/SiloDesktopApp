using SiloPlayer.Controls;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Services.Admin.Users;
using static SiloPlayer.Views.Admin.AdminUsersFormKit;
using static SiloPlayer.Views.Admin.AdminUsersUi;

namespace SiloPlayer.Views.Admin;

/// <summary>
/// The value of one stored setting (official <c>SettingJsonDialog</c> and the setting controls): a switch for a yes/no setting, a named choice for one with a fixed set of values, a
/// number box, a text box, and the raw value for a setting with no inline control. A setting that may be unset offers "Not set".
/// </summary>
internal sealed class AdminUsersSettingForm
{
    private readonly JsonObject _definition;
    private readonly string _type;
    private readonly bool _nullable;
    private readonly TextBox _text = new() { AcceptsReturn = false, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _choice = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _problem = Problem();
    private readonly bool _enum;

    internal FrameworkElement Content { get; }
    internal event Action? Changed;
    internal bool RawJson { get; }

    internal AdminUsersSettingForm(JsonObject definition, JsonNode? initial)
    {
        _definition = AdminUserPreferences.ValueDefinition(definition);
        var types = _definition["type"] is JsonArray array ? array.Select(node => node!.GetValue<string>()).ToArray() : [_definition.Text("type")];
        _nullable = types.Contains("null");
        _type = types.First(type => type != "null");
        _enum = _definition["enum"] is JsonArray;
        var label = definition.Text("label", definition.Text("key"));
        var stack = new StackPanel { Spacing = 12 };
        if (definition.Text("description").Length > 0) stack.Children.Add(Text(definition.Text("description"), 14, true));
        var wire = definition["value_schema"] as JsonObject;
        if (_enum || _type == "boolean" && _nullable)
        {
            var options = new List<(string Tag, string Label, JsonNode? Value)>();
            if (_nullable) options.Add(("null", T("admin.native.users.prefs.notSet"), null));
            if (_enum)
            {
                var rows = (wire?["values"] as JsonArray ?? []).OfType<JsonObject>().ToArray();
                foreach (var row in rows) options.Add((row["value"]?.ToJsonString() ?? "null", row.Text("label", row["value"].Display()), row["value"]?.DeepClone()));
            }
            else { options.Add(("true", T("web.admin.users.detail.access.value.yes"), JsonValue.Create(true))); options.Add(("false", T("web.admin.users.detail.access.value.no"), JsonValue.Create(false))); }
            foreach (var option in options) _choice.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
            _choice.SelectedItem = _choice.Items.OfType<ComboBoxItem>().FirstOrDefault(item => JsonNode.DeepEquals(item.Tag as JsonNode, initial)) ?? _choice.Items.OfType<ComboBoxItem>().First();
            _choice.SelectionChanged += (_, _) => Changed?.Invoke();
            AutomationProperties.SetAutomationId(_choice, "SettingValue"); AutomationProperties.SetName(_choice, label);
            stack.Children.Add(Field(label, _choice, _problem));
            Content = stack;
            return;
        }
        if (_type == "boolean")
        {
            var toggle = Switch(initial is JsonValue flag && flag.TryGetValue<bool>(out var on) && on);
            AutomationProperties.SetAutomationId(toggle, "SettingValue");
            _toggle = toggle; toggle.Toggled += (_, _) => Changed?.Invoke();
            stack.Children.Add(SwitchRow(label, null, toggle)); stack.Children.Add(_problem);
            Content = stack;
            return;
        }
        RawJson = _type is "object" or "array" or "json";
        if (RawJson)
        {
            _text.AcceptsReturn = true; _text.MinHeight = 120; _text.FontFamily = AdminMonitorUi.Mono; _text.FontSize = 12;
            _text.Text = initial == null ? "null" : initial.ToJsonString(new() { WriteIndented = true });
            stack.Children.Add(Text(T("web.admin.users.detail.preferences.json.description"), 12, true));
        }
        else _text.Text = initial == null ? "" : initial.Display();
        AutomationProperties.SetAutomationId(_text, "SettingValue"); AutomationProperties.SetName(_text, RawJson ? T("web.admin.users.detail.preferences.json.rawValueAria") : label);
        _text.TextChanged += (_, _) => { Say(_problem, null); Changed?.Invoke(); };
        stack.Children.Add(RawJson ? _text : Field(label, _text)); stack.Children.Add(_problem);
        Content = stack;
    }

    private readonly ToggleSwitch? _toggle;

    /// <summary>The value to store, or null with the problem shown at the field.</summary>
    internal (bool Ok, JsonNode? Value) Read()
    {
        Say(_problem, null);
        JsonNode? value;
        if (_enum || _type == "boolean" && _nullable) value = (_choice.SelectedItem as ComboBoxItem)?.Tag is JsonNode node ? node.DeepClone() : null;
        else if (_toggle != null) value = JsonValue.Create(_toggle.IsOn);
        else
        {
            var text = _text.Text.Trim();
            if (RawJson)
            {
                try { value = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { return Fail(); }
            }
            else if (text.Length == 0 && _nullable) value = null;
            else if (_type == "integer") value = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) ? JsonValue.Create(whole) : null;
            else if (_type == "number") value = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? JsonValue.Create(number) : null;
            else value = JsonValue.Create(_text.Text);
            if (value == null && !(text.Length == 0 && _nullable) && !RawJson) return Fail();
        }
        try { AdminUsersContract.ValidateValue("value", _definition, value); }
        catch (ArgumentException) { return Fail(); }
        return (true, value);

        (bool, JsonNode?) Fail() { Say(_problem, T("admin.native.users.prefs.invalidValue")); (_choice.Visibility == Visibility.Visible && (_enum || _nullable) ? (Control)_choice : _text).Focus(FocusState.Programmatic); return (false, null); }
    }
}
