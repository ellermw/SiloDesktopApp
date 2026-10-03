using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Plugins;

namespace SiloPlayer.Core.Services;

/// <summary>Typed connection drafts, shared validation and payload construction for provider forms.</summary>
public static class ProviderConnectionConfig
{
    public static bool Shown(IEnumerable<PluginAdminFormCondition>? conditions, IReadOnlyDictionary<string, object?> values)
        => conditions == null || conditions.All(condition => values.TryGetValue(condition.Field, out var value)
            && condition.Equals.Contains(Scalar(value), StringComparer.Ordinal));
    public static string Scalar(object? value) => value is JsonElement element ? DeviceSettingDefinition.Scalar(element)
        : value is bool boolean ? boolean ? "true" : "false" : value is IFormattable formatted ? formatted.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : value?.ToString() ?? "";
    public static bool Entered(object? value) => value is JsonElement element ? element.ValueKind switch
        { JsonValueKind.Null or JsonValueKind.Undefined => false, JsonValueKind.String => !string.IsNullOrWhiteSpace(element.GetString()), JsonValueKind.Array => element.EnumerateArray().Any(item => Entered(item)), JsonValueKind.Object => element.EnumerateObject().Any(item => Entered(item.Value)), _ => true }
        : value is string text ? !string.IsNullOrWhiteSpace(text) : value is System.Collections.IEnumerable list ? list.Cast<object?>().Any(Entered) : value != null;
    public static PluginAdminForm? Form(PluginConfigSchema schema)
    {
        if (schema.AdminForm != null) return schema.AdminForm;
        if (string.IsNullOrWhiteSpace(schema.JsonSchema)) return null;
        try
        {
            using var doc = JsonDocument.Parse(schema.JsonSchema);
            if (!doc.RootElement.TryGetProperty("properties", out var properties)) return null;
            var required = doc.RootElement.TryGetProperty("required", out var req) ? req.EnumerateArray().Select(value => value.GetString()).ToHashSet() : [];
            var form = new PluginAdminForm();
            foreach (var property in properties.EnumerateObject())
            {
                var field = property.Value; var type = field.TryGetProperty("type", out var t) ? t.GetString() : "string";
                var hasEnum = field.TryGetProperty("enum", out var options);
                form.Fields.Add(new PluginAdminFormField
                {
                    Key = property.Name, Label = field.TryGetProperty("title", out var title) ? title.GetString() ?? property.Name : property.Name,
                    Description = field.TryGetProperty("description", out var description) ? description.GetString() : null,
                    Required = required.Contains(property.Name), DefaultValue = field.TryGetProperty("default", out var d) ? d.Clone() : null,
                    Validation = new PluginAdminFormValidation
                    {
                        Min = field.TryGetProperty("minimum", out var min) ? min.GetDouble() : null,
                        Max = field.TryGetProperty("maximum", out var max) ? max.GetDouble() : null,
                        MinLength = field.TryGetProperty("minLength", out var minLength) ? minLength.GetInt32() : null,
                        MaxLength = field.TryGetProperty("maxLength", out var maxLength) ? maxLength.GetInt32() : null,
                        Pattern = field.TryGetProperty("pattern", out var pattern) ? pattern.GetString() : null,
                    },
                    Control = hasEnum ? "SELECT" : type == "boolean" ? "TOGGLE" : type is "number" or "integer" ? "NUMBER" : "TEXT",
                    Options = hasEnum ? options.EnumerateArray().Select(value => new PluginAdminFormFieldOption { Value = DeviceSettingDefinition.Scalar(value), Label = DeviceSettingDefinition.Scalar(value) }).ToList() : null,
                });
            }
            return form;
        }
        catch (JsonException) { return null; }
    }
    public static Dictionary<string, object?> EffectiveValues(PluginAdminForm form, IReadOnlyDictionary<string, object?> values)
        => form.Fields.ToDictionary(field => field.Key, field => values.GetValueOrDefault(field.Key) ?? field.DefaultValue);

    private static object? CoerceSchemaValue(PluginConfigSchema schema, string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(schema.JsonSchema)) return value;
        using var document = JsonDocument.Parse(schema.JsonSchema);
        if (!document.RootElement.TryGetProperty("properties", out var properties) || !properties.TryGetProperty(key, out var property)) return value;
        object? Coerce(JsonElement definition, object? input)
        {
            if (!definition.TryGetProperty("type", out var type)) return input;
            var scalar = Scalar(input);
            if (type.GetString() is "array" or "object" && input is string json)
            {
                using var parsed = JsonDocument.Parse(json);
                input = parsed.RootElement.Clone();
            }
            return type.GetString() switch
            {
                "boolean" => bool.Parse(scalar),
                "integer" => long.Parse(scalar, System.Globalization.CultureInfo.InvariantCulture),
                "number" => double.Parse(scalar, System.Globalization.CultureInfo.InvariantCulture),
                "object" when input is JsonElement objectValue && objectValue.ValueKind == JsonValueKind.Object => objectValue.Clone(),
                "array" when definition.TryGetProperty("items", out var items) =>
                    (input is JsonElement element ? element.EnumerateArray().Select(item => (object?)item).ToArray()
                    : input is System.Collections.IEnumerable list ? list.Cast<object?>().ToArray() : [])
                    .Select(item => Coerce(items, item)).ToArray(),
                _ => input,
            };
        }
        try { return Coerce(property, value); }
        catch (Exception ex) when (ex is FormatException or OverflowException or JsonException or InvalidOperationException) { throw new InvalidOperationException($"Check {key}.", ex); }
    }

    public static Dictionary<string, string> Validate(PluginAdminForm form, IReadOnlyDictionary<string, object?> values)
    {
        var errors = new Dictionary<string, string>();
        var effective = EffectiveValues(form, values);
        foreach (var field in form.Fields)
        {
            if (!Shown(field.ShowWhen, effective)) continue;
            var value = values.GetValueOrDefault(field.Key) ?? field.DefaultValue;
            var text = Scalar(value);
            if (field.Required && !Entered(value)) { errors[field.Key] = $"{field.Label} is required."; continue; }
            if (!Entered(value)) continue;
            if (field.Control.Equals("SELECT", StringComparison.OrdinalIgnoreCase) && field.Options is { Count: > 0 } options && !options.Any(option => option.Value == text))
                errors[field.Key] = $"Choose a valid {field.Label}.";
            var validation = field.Validation;
            if (validation?.MinLength is int minimumLength && text.Length < minimumLength || validation?.MaxLength is int maximumLength && text.Length > maximumLength)
                errors[field.Key] = $"Check the length of {field.Label}.";
            if (!string.IsNullOrEmpty(validation?.Pattern))
            {
                try { if (!Regex.IsMatch(text, validation.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200))) errors[field.Key] = $"Check {field.Label}."; }
                catch (ArgumentException) { errors[field.Key] = $"{field.Label} has an invalid validation rule."; }
                catch (RegexMatchTimeoutException) { errors[field.Key] = $"Could not validate {field.Label}."; }
            }
            if (field.Control.Equals("NUMBER", StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)
                    || validation?.Min is double minimum && number < minimum || validation?.Max is double maximum && number > maximum)
                    errors[field.Key] = $"Check the range of {field.Label}.";
            }
        }
        return errors;
    }
    public static Dictionary<string, Dictionary<string, object?>> Build(IReadOnlyList<PluginConfigSchema> schemas, IReadOnlyDictionary<string, Dictionary<string, object?>> drafts)
    {
        var output = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var schema in schemas)
        {
            var form = Form(schema); if (form == null) { if (schema.Required) throw new InvalidOperationException($"{schema.Title} has an unsupported required configuration schema."); continue; }
            var draft = drafts.GetValueOrDefault(schema.Key) ?? [];
            if (!schema.Required && !draft.Values.Any(Entered)) continue;
            var errors = Validate(form, draft);
            if (errors.Count > 0) throw new InvalidOperationException(errors.Values.First());
            var values = new Dictionary<string, object?>();
            var effective = EffectiveValues(form, draft);
            foreach (var field in form.Fields.Where(field => Shown(field.ShowWhen, effective)))
            {
                var value = draft.GetValueOrDefault(field.Key) ?? field.DefaultValue;
                if (!Entered(value)) continue;
                if (field.Control.Equals("NUMBER", StringComparison.OrdinalIgnoreCase)) value = double.Parse(Scalar(value), System.Globalization.CultureInfo.InvariantCulture);
                else if (field.Control.Equals("TOGGLE", StringComparison.OrdinalIgnoreCase)) value = Scalar(value) == "true";
                values[field.Key] = CoerceSchemaValue(schema, field.Key, value);
            }
            output[schema.Key] = values;
        }
        return output;
    }
}
