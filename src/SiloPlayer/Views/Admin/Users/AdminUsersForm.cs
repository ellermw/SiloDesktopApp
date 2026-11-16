using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Services.Admin.Users;

namespace SiloPlayer.Views.Admin;

internal sealed class AdminUsersForm : IDisposable
{
    private readonly JsonObject _schema;
    private readonly JsonObject _initial;
    private readonly bool _create;
    private readonly Func<string, CancellationToken, Task<byte[]>>? _loadAvatar;
    private readonly CancellationToken _owner;
    private AdminUsersAvatarPicker? _avatar;
    private readonly Dictionary<string, Func<JsonNode?>> _read = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _include = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action<IReadOnlyList<(string Id, string Name)>>> _choiceWriters = new(StringComparer.Ordinal);
    internal void UpdateChoices(string key, IReadOnlyList<(string Id, string Name)> choices)
    {
        if (_choiceWriters.TryGetValue(key, out var update)) update(choices);
    }
    internal StackPanel Content { get; } = new() { Spacing = 12, MinWidth = 280, MaxWidth = 580 };

    internal AdminUsersForm(JsonObject schema, JsonObject initial, bool create, IEnumerable<string>? fields = null,
        IEnumerable<string>? locked = null, IReadOnlyDictionary<string, IReadOnlyList<(string Id, string Name)>>? choices = null,
        Func<string, CancellationToken, Task<byte[]>>? loadAvatar = null, CancellationToken owner = default)
    {
        _schema = schema; _initial = initial.Copy(); _create = create; _loadAvatar = loadAvatar; _owner = owner;
        var required = schema["required"]?.AsArray().Select(node => node!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (var pair in schema["properties"]!.AsObject())
        {
            if (fields != null && !fields.Contains(pair.Key)) continue;
            if (locked?.Contains(pair.Key) == true)
            {
                if (create && initial.ContainsKey(pair.Key)) _read[pair.Key] = () => initial[pair.Key]?.DeepClone();
                continue;
            }
            var key = pair.Key;
            var definition = pair.Value!.AsObject();
            var block = new StackPanel { Spacing = 4 };
            var label = string.Join(" ", key.Split('_').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
            block.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
            var value = initial[key];
            var typeNode = definition["type"];
            var nullable = typeNode is JsonArray types && types.Any(node => node?.GetValue<string>() == "null");
            var type = typeNode is JsonArray variants ? variants.First(node => node?.GetValue<string>() != "null")!.GetValue<string>() : typeNode?.GetValue<string>() ?? "object";
            if (_create && !required.Contains(key))
            {
                var include = new CheckBox { Content = "Set an explicit value", IsChecked = initial.ContainsKey(key) };
                block.Children.Add(include); _include[key] = include;
            }
            if (key == "avatar")
            {
                _read[key] = AvatarEditor(block, value);
            }
            else if (choices?.TryGetValue(key, out var options) == true)
            {
                if (type == "array")
                {
                    var inherit = new CheckBox { Content = "Inherit all libraries (null); unchecked with no selections means explicit none", IsChecked = value == null };
                    if (nullable) block.Children.Add(inherit);
                    var selections = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
                    var initialIds = (value as JsonArray ?? []).Select(node => node!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
                    var list = new StackPanel { Spacing = 4 }; block.Children.Add(list);
                    _choiceWriters[key] = entries =>
                    {
                        foreach (var entry in entries.Concat(initialIds.Where(id => !entries.Any(option => option.Id == id)).Select(id => (Id: id, Name: "Saved #" + id + " (not in loaded choices)"))))
                        {
                            if (selections.TryGetValue(entry.Id, out var existing)) { existing.Content = entry.Name; continue; }
                            var check = new CheckBox { Content = entry.Name, IsChecked = initialIds.Contains(entry.Id) };
                            selections[entry.Id] = check; list.Children.Add(check);
                        }
                    };
                    _choiceWriters[key](options);
                    _read[key] = () => nullable && inherit.IsChecked == true ? null : new JsonArray(selections.Where(pair => pair.Value.IsChecked == true).Select(pair => (JsonNode?)JsonValue.Create(pair.Key)).ToArray());
                }
                else
                {
                    var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 40 };
                    if (nullable) selector.Items.Add(new ComboBoxItem { Content = "Inherit", Tag = "" });
                    _choiceWriters[key] = entries =>
                    {
                        var selected = (selector.SelectedItem as ComboBoxItem)?.Tag as string ?? value.Display();
                        foreach (var entry in entries) if (!selector.Items.OfType<ComboBoxItem>().Any(item => (string)item.Tag == entry.Id)) selector.Items.Add(new ComboBoxItem { Content = entry.Name, Tag = entry.Id });
                        if (value != null && !selector.Items.OfType<ComboBoxItem>().Any(item => (string)item.Tag == value.Display())) selector.Items.Add(new ComboBoxItem { Content = "Saved #" + value.Display(), Tag = value.Display() });
                        selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? selector.Items.OfType<ComboBoxItem>().FirstOrDefault();
                    };
                    _choiceWriters[key](options);
                    _read[key] = () => selector.SelectedItem is ComboBoxItem item && ((string)item.Tag).Length > 0 ? JsonValue.Create((string)item.Tag) : null;
                    block.Children.Add(selector);
                }
            }
            else if (type == "boolean" || definition["enum"] is JsonArray)
            {
                var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 40 };
                var enumOptions = definition["enum"] as JsonArray ?? new JsonArray(true, false);
                if (nullable && !enumOptions.Any(node => node == null)) selector.Items.Add(new ComboBoxItem { Content = "Inherit", Tag = null });
                foreach (var option in enumOptions) selector.Items.Add(new ComboBoxItem { Content = option == null ? "Inherit" : option.Display(), Tag = option?.DeepClone() });
                selector.SelectedItem = selector.Items.OfType<ComboBoxItem>().FirstOrDefault(item => JsonNode.DeepEquals(item.Tag as JsonNode, value)) ?? selector.Items.OfType<ComboBoxItem>().FirstOrDefault();
                _read[key] = () => (selector.SelectedItem as ComboBoxItem)?.Tag is JsonNode node ? node.DeepClone() : null;
                block.Children.Add(selector);
            }
            else
            {
                var inherit = new CheckBox { Content = "Inherit (null, not zero/false/empty)", IsChecked = value == null };
                if (nullable) block.Children.Add(inherit);
                if (key is "password" or "pin")
                {
                    var password = new PasswordBox { MaxLength = key == "pin" ? 4 : 72, MinHeight = 40 };
                    _read[key] = () =>
                    {
                        if (key == "pin") AdminUserAvatars.ValidateEditorPin(password.Password);
                        return JsonValue.Create(password.Password);
                    };
                    block.Children.Add(password);
                }
                else
                {
                    var input = new TextBox { Text = value == null ? "" : type is "array" or "object" ? value.ToJsonString() : value.Display(),
                        AcceptsReturn = type is "object" or "array", TextWrapping = TextWrapping.Wrap, MinHeight = 40 };
                    if (definition["maxLength"] is JsonValue max) input.MaxLength = max.GetValue<int>();
                    if (type == "array") input.PlaceholderText = "JSON array of exact string IDs; [] explicitly means none";
                    _read[key] = () => nullable && inherit.IsChecked == true ? null : type switch
                    {
                        "integer" => long.TryParse(input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? JsonValue.Create(integer) : throw new ArgumentException(label + ": enter a whole number."),
                        "number" => double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? JsonValue.Create(number) : throw new ArgumentException(label + ": enter a finite number."),
                        "array" or "object" => JsonNode.Parse(input.Text),
                        _ => JsonValue.Create(input.Text)
                    };
                    block.Children.Add(input);
                }
            }
            if (definition.Text("description").Length > 0) block.Children.Add(new TextBlock { Text = definition.Text("description"), TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
            Content.Children.Add(block);
        }
    }
    private Func<JsonNode?> AvatarEditor(StackPanel block, JsonNode? initial)
    {
        _avatar = new AdminUsersAvatarPicker(initial?.GetValue<string>(), _loadAvatar ?? throw new InvalidOperationException("Avatar previews require an owned reader."), _owner);
        block.Children.Add(_avatar.Content);
        return () => _avatar.Reference is { } reference ? JsonValue.Create(reference) : null;
    }
    public void Dispose() => _avatar?.Dispose();
    internal JsonObject Read()
    {
        var result = new JsonObject();
        foreach (var pair in _read)
        {
            if (_include.TryGetValue(pair.Key, out var include) && include.IsChecked != true) continue;
            var value = pair.Value();
            if (!_create && _initial.ContainsKey(pair.Key) && JsonNode.DeepEquals(value, _initial[pair.Key])) continue;
            result[pair.Key] = value;
        }
        if (!_create && result.ContainsKey("password") && _read.TryGetValue("require_password_change", out var passwordChange))
            result["require_password_change"] = passwordChange();
        AdminUsersContract.Validate(_schema, result);
        if (result.Count == 0) throw new ArgumentException("No changes are selected.");
        return result;
    }
}
