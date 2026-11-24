using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Notifications;

namespace SiloPlayer.Core.Services;

public enum DestinationControl { Text, Password, Number, Switch, Select }

/// <summary>One destination setting as the form renders it (official PluginDestinationsSection destinationForm).</summary>
public sealed record DestinationFieldView(NotificationPluginSettingsField Field, DestinationControl Control, bool Secret, bool Required, bool Stored)
{
    public string Key => Field.Key;
    public string Label => string.IsNullOrEmpty(Field.Label) ? Field.Key : Field.Label;
}

/// <summary>
/// The state and rules of the add and edit destination dialog (official silo-server 3b2b32168 #3550,
/// PluginDestinationsSection.tsx): which fields render and how, the typed settings an add sends, the
/// settings an update sends (only edited ones, never a blank stored secret), Keep or Clear saved secret,
/// and the client checks. Secret values live only in <see cref="Values"/>; nothing here logs or persists them.
/// </summary>
public sealed class NotificationDestinationForm
{
    private readonly IReadOnlyList<string> _secretsSet;

    public NotificationDestinationForm(NotificationPluginChannel? channel, NotificationPluginDestination? destination)
    {
        Destination = destination;
        _secretsSet = destination?.SecretSettingsSet ?? [];
        Channel = channel;
        NotifyNextUp = destination?.NotifyNextUp ?? true;
        NotifyRequests = destination?.NotifyRequests ?? true;
        foreach (var (key, value) in destination?.Settings ?? [])
            Values[key] = ToClr(value);
    }

    public NotificationPluginDestination? Destination { get; }
    public bool Editing => Destination != null;
    public NotificationPluginChannel? Channel { get; private set; }
    /// <summary>The draft: strings and booleans the person entered, or the typed values the server returned.</summary>
    public Dictionary<string, object?> Values { get; private set; } = [];
    /// <summary>The settings changed in this dialog, the only ones an update sends.</summary>
    public HashSet<string> Edited { get; } = new(StringComparer.Ordinal);
    /// <summary>Saved optional secrets the person chose to remove on save.</summary>
    public HashSet<string> Clearing { get; } = new(StringComparer.Ordinal);
    public IReadOnlyList<string> SecretsSet => _secretsSet;
    public bool NotifyNextUp { get; set; } = true;
    public bool NotifyRequests { get; set; } = true;

    public IReadOnlyList<NotificationPluginSettingsField> Declared => Channel?.SettingsForm.Fields ?? [];

    public IReadOnlyList<DestinationFieldView> Fields
        => Declared.Select(declared =>
        {
            var stored = _secretsSet.Contains(declared.Key, StringComparer.Ordinal);
            var secret = declared.Secret || stored;
            var control = ControlFor(declared);
            return new DestinationFieldView(declared, secret && control != DestinationControl.Select ? DestinationControl.Password : control,
                secret, declared.Required && !stored, stored);
        }).ToArray();

    /// <summary>A saved optional secret can be removed instead of replaced.</summary>
    public IReadOnlyList<DestinationFieldView> Clearable
        => Fields.Where(view => view.Stored && !view.Field.Required).ToArray();

    /// <summary>The channel a new destination is for; its settings start empty and an empty name takes the channel's.</summary>
    public void ChooseChannel(NotificationPluginChannel channel, string currentName, out string name)
    {
        Channel = channel;
        Values = [];
        name = string.IsNullOrWhiteSpace(currentName) ? channel.DisplayName : currentName;
    }

    /// <summary>A control this client does not know renders by the value's type.</summary>
    public static DestinationControl ControlFor(NotificationPluginSettingsField field) => field.Control switch
    {
        "text" => DestinationControl.Text,
        "password" => DestinationControl.Password,
        "number" => DestinationControl.Number,
        "switch" => DestinationControl.Switch,
        "select" => DestinationControl.Select,
        _ => field.Type == "boolean" ? DestinationControl.Switch
            : field.Type is "integer" or "number" ? DestinationControl.Number : DestinationControl.Text,
    };

    public object? Effective(DestinationFieldView view)
        => Values.TryGetValue(view.Key, out var value) && value != null ? value
            : view.Field.DefaultValue is { } fallback ? ToClr(fallback) : null;

    public string EffectiveText(DestinationFieldView view) => TextOf(Effective(view));

    /// <summary>The text a person typed or picked, or the switch. A change cancels a pending Clear on a secret it replaces.</summary>
    public void SetValue(string key, object? next)
    {
        var previous = Values.GetValueOrDefault(key);
        if (!Equals(previous, next))
        {
            Edited.Add(key);
            // Typing a new value for a secret being cleared replaces it instead; a change to any other field leaves the clear.
            if (Clearing.Contains(key) && !(next is null || next is string { Length: 0 })) Clearing.Remove(key);
        }
        Values[key] = next;
    }

    public void ToggleClearing(string key)
    {
        if (!Clearing.Remove(key)) Clearing.Add(key);
    }

    // --- checks --------------------------------------------------------------------------------

    /// <summary>Official validateSchemaValues over the destination form. Patterns are left to the server, which reads Go RE2.</summary>
    public Dictionary<string, string> Validate()
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var view in Fields)
        {
            var raw = Effective(view);
            var label = view.Label;
            if (view.Required && IsEmpty(raw))
            {
                errors[view.Key] = AppStrings.Get("web.common.schemaForm.required", new Dictionary<string, object?> { ["label"] = label }, label + " is required");
                continue;
            }
            if (IsEmpty(raw)) continue;
            var field = view.Field;
            if (raw is string text)
            {
                if (field.MinLength is > 0 && text.Length < field.MinLength)
                    errors[view.Key] = Count("web.common.schemaForm.minLength", label, field.MinLength.Value, $"{label} must be at least {field.MinLength} characters");
                else if (field.MaxLength is > 0 && text.Length > field.MaxLength)
                    errors[view.Key] = Count("web.common.schemaForm.maxLength", label, field.MaxLength.Value, $"{label} must be at most {field.MaxLength} characters");
            }
            if (view.Control == DestinationControl.Number && raw is string numeric && numeric.Trim().Length > 0)
            {
                if (!double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    errors[view.Key] = AppStrings.Get("web.common.schemaForm.notNumber", new Dictionary<string, object?> { ["label"] = label }, label + " must be a number");
                else if (field.Minimum is { } min && number < min)
                    errors[view.Key] = AppStrings.Get("web.common.schemaForm.min", new Dictionary<string, object?> { ["label"] = label, ["min"] = Number(min) }, $"{label} must be ≥ {Number(min)}");
                else if (field.Maximum is { } max && number > max)
                    errors[view.Key] = AppStrings.Get("web.common.schemaForm.max", new Dictionary<string, object?> { ["label"] = label, ["max"] = Number(max) }, $"{label} must be ≤ {Number(max)}");
            }
        }
        return errors;
    }

    private static string Count(string key, string label, long count, string fallback)
        => AppStrings.Get(key, new Dictionary<string, object?> { ["label"] = label, ["count"] = count, ["count_text"] = count.ToString(AppStrings.FormatCulture) }, fallback);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    // --- what is sent --------------------------------------------------------------------------

    /// <summary>The settings an add sends, typed as each field declares. Without a form, the entered values as they are.</summary>
    public Dictionary<string, object?> SettingsForCreate()
        => Declared.Count > 0 ? BuildSettings(explicitClears: false) : Values.ToDictionary(pair => pair.Key, pair => pair.Value);

    /// <summary>
    /// The settings an update sends: only the ones changed in the dialog, none when none changed (null).
    /// A stored secret left blank is left out so it stays as saved; a Clear is sent in clear_secrets instead.
    /// Any other emptied field goes out as null, which removes it so the plugin falls back to its default.
    /// </summary>
    public Dictionary<string, object?>? SettingsForUpdate()
    {
        var changed = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in BuildSettings(explicitClears: true))
        {
            if (!Edited.Contains(key) || Clearing.Contains(key)) continue;
            if (_secretsSet.Contains(key, StringComparer.Ordinal) && !IsNonBlankEntry(Values.GetValueOrDefault(key))) continue;
            changed[key] = value is "" ? null : value;
        }
        return changed.Count > 0 ? changed : null;
    }

    /// <summary>The body of a PUT: name and the two reasons always, settings and clear_secrets only when there are some.</summary>
    public Dictionary<string, object?> UpdateBody(string name)
    {
        var body = new Dictionary<string, object?> { ["name"] = name.Trim() };
        if (SettingsForUpdate() is { } settings) body["settings"] = settings;
        if (Clearing.Count > 0) body["clear_secrets"] = Clearing.ToArray();
        body["notify_next_up"] = NotifyNextUp;
        body["notify_requests"] = NotifyRequests;
        return body;
    }

    private Dictionary<string, object?> BuildSettings(bool explicitClears)
    {
        var output = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var view in Fields)
        {
            var coerced = Coerce(view.Field, Effective(view));
            if (coerced == null)
            {
                if (explicitClears && !view.Secret) output[view.Key] = view.Field.Type == "string" ? "" : null;
                continue;
            }
            output[view.Key] = coerced;
        }
        return output;
    }

    private static bool IsNonBlankEntry(object? entry) => entry is not (null or string { Length: 0 });

    private static object? Coerce(NotificationPluginSettingsField field, object? raw)
    {
        switch (field.Type)
        {
            case "boolean":
                return raw switch { bool flag => flag, string text => text.Trim().Equals("true", StringComparison.OrdinalIgnoreCase),
                    null => false, long whole => whole != 0, double real => real != 0, _ => true };
            case "integer" or "number":
                if (raw is long or double) return raw;
                if (raw is string entry)
                {
                    var trimmed = entry.Trim();
                    if (trimmed.Length == 0) return null;
                    if (field.Type == "integer" && long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)) return whole;
                    if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) && double.IsFinite(real))
                        return field.Type == "number" && real == Math.Floor(real) && Math.Abs(real) < 9e15 ? (long)real : real;
                    return entry;
                }
                return raw;
            default:
                return raw is string text0 ? (text0.Trim().Length == 0 ? null : text0) : raw;
        }
    }

    private static bool IsEmpty(object? value) => value is null || value is string text && text.Trim().Length == 0;

    // --- values --------------------------------------------------------------------------------

    public static object? ToClr(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out var whole) ? whole : value.GetDouble(),
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.Clone(),
    };

    /// <summary>String(value) as the web form shows it.</summary>
    public static string TextOf(object? value) => value switch
    {
        null => "",
        string text => text,
        bool flag => flag ? "true" : "false",
        long whole => whole.ToString(CultureInfo.InvariantCulture),
        double real => real.ToString("R", CultureInfo.InvariantCulture),
        JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.GetRawText(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    public static string OptionValue(NotificationPluginSettingsOption option) => TextOf(ToClr(option.Value));
}
