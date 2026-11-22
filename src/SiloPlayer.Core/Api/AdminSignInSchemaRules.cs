using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Api;

/// <summary>Native counterpart of signInSetup/configSchemaAdminForm/schemaFormUtils. Schema types preserve numeric-looking strings.</summary>
public enum AdminSignInEditor { Text, Number, Secret, Multiline, Switch, Select, MultiSelect }

public static class AdminSignInSchemaRules
{
    public static AdminSignInForm? InferForm(AdminSignInSchema schema)
    {
        var explicitForm = schema.AdminForm is { Fields.Count: > 0 } ? schema.AdminForm : null;
        try
        {
            var json = JsonNode.Parse(schema.JsonSchema) as JsonObject;
            if (json?["properties"] is not JsonObject properties) return explicitForm;
            var required = (json["required"] as JsonArray)?.Select(n => n?.ToString()).ToHashSet() ?? [];
            var form = new AdminSignInForm();
            foreach (var (key, value) in properties)
            {
                if (value is not JsonObject field) { if (explicitForm == null) return null; continue; }
                var type = TypeOf(field); var choices = field["enum"] as JsonArray;
                if (field["format"]?.ToString() == "password" || field["writeOnly"]?.ToString() == "true")
                    foreach (var declared in explicitForm?.Fields.Where(f => f.Key == key) ?? []) declared.Secret = true;
                if (type is not ("string" or "number" or "integer" or "boolean")) { if (explicitForm == null) return null; continue; }
                form.Fields.Add(new() { Key = key, Label = field["title"]?.ToString() ?? key.Replace('_', ' '), Description = field["description"]?.ToString() ?? "",
                    Required = required.Contains(key), Secret = field["format"]?.ToString() == "password" || field["writeOnly"]?.ToString() == "true",
                    Control = choices != null ? "SELECT" : type == "boolean" ? "SWITCH" : type is "integer" or "number" ? "NUMBER" : type == "array" ? "TEXTAREA" : "TEXT",
                    DefaultValue = field["default"]?.DeepClone(), Options = choices?.Select(v => new AdminSignInOption { Value = v?.ToString() ?? "", Label = v?.ToString() ?? "" }).ToList() ?? [] });
            }
            if (explicitForm != null)
            {
                explicitForm.Fields.AddRange(form.Fields.Where(f => !explicitForm.Fields.Any(e => e.Key == f.Key)));
                return explicitForm;
            }
            return form;
        }
        catch { return explicitForm; }
    }
    public static JsonObject Values(AdminSignInProvider provider, AdminSignInSchema schema)
    {
        var values = provider.GlobalConfigs.FirstOrDefault(c => c.Key == schema.Key)?.Value.DeepClone() as JsonObject ?? new();
        foreach (var field in schema.AdminForm?.Fields ?? [])
        {
            if (field.IsSecret) values.Remove(field.Key);
            else if (!values.ContainsKey(field.Key) && field.DefaultValue != null) values[field.Key] = field.DefaultValue.DeepClone();
        }
        return values;
    }
    public static bool Conditions(IEnumerable<AdminSignInCondition> conditions, JsonObject values, AdminSignInForm form) =>
        conditions.All(c => c.Matches.Contains((values[c.Field] ?? form.Fields.FirstOrDefault(f => f.Key == c.Field)?.DefaultValue)?.ToString() ?? ""));
    public static bool Visible(AdminSignInForm form, AdminSignInField field, JsonObject values)
    {
        var sections = form.Sections.Where(s => s.FieldKeys.Contains(field.Key)).ToArray();
        return Conditions(field.ShowWhen, values, form) && (sections.Length == 0 || sections.Any(s => Conditions(s.ShowWhen, values, form)));
    }
    /// <summary>Which control draws a field (official <c>SignInConfigField</c>): a secret is always a masked single-line box, even when its plugin also marks it multiline (a text area cannot mask what is typed).</summary>
    public static AdminSignInEditor EditorOf(AdminSignInSchema schema, AdminSignInField field) =>
        field.IsSecret ? AdminSignInEditor.Secret
        : field.Control == "SWITCH" ? AdminSignInEditor.Switch
        : field.Control == "SELECT" ? AdminSignInEditor.Select
        : field.Control == "MULTI_SELECT" ? AdminSignInEditor.MultiSelect
        : field.Control == "TEXTAREA" || field.Multiline || FieldType(schema, field) == "array" ? AdminSignInEditor.Multiline
        : field.Control == "NUMBER" ? AdminSignInEditor.Number : AdminSignInEditor.Text;
    public static string FieldType(AdminSignInSchema schema, AdminSignInField field)
    {
        try { return TypeOf(JsonNode.Parse(schema.JsonSchema)?["properties"]?[field.Key] as JsonObject); } catch { return ""; }
    }
    private static string TypeOf(JsonObject? schema) => schema?["type"] is JsonArray types ? types.Select(t => t?.ToString()).FirstOrDefault(t => t != "null") ?? "" : schema?["type"]?.ToString() ?? "";
    public static JsonNode? Convert(AdminSignInSchema schema, AdminSignInField field, string text)
    {
        var type = FieldType(schema, field);
        if (type == "array" || field.Control == "MULTI_SELECT")
        {
            var entries = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string itemType = "";
            try { itemType = TypeOf(JsonNode.Parse(schema.JsonSchema)?["properties"]?[field.Key]?["items"] as JsonObject); } catch { }
            var list = new JsonArray();
            foreach (var entry in entries)
                list.Add(itemType is "integer" or "number" && double.TryParse(entry, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? JsonValue.Create(number)
                    : itemType == "boolean" ? JsonValue.Create(entry == "true") : JsonValue.Create(entry));
            return list;
        }
        if (type == "boolean" || field.Control == "SWITCH") return JsonValue.Create(text == "true");
        if (type is "integer" or "number" || type == "" && field.Control == "NUMBER")
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw new ArgumentException("Invalid number.");
            return JsonValue.Create(value);
        }
        return JsonValue.Create(text);
    }
    /// <summary>Whether text being typed still means <paramref name="value"/> (a trailing line break, space or decimal point); null while it is not a value yet (a lone "-").</summary>
    public static bool? Means(AdminSignInSchema schema, AdminSignInField field, string text, JsonNode? value)
    {
        try { return JsonNode.DeepEquals(Convert(schema, field, text), value); } catch (ArgumentException) { return null; }
    }
    public static AdminSignInConfigWrite Build(AdminSignInProvider provider, AdminSignInSchema schema, JsonObject values, IReadOnlyCollection<string> clears)
    {
        // A field the deployment owns is never part of a write or a connection test (official stagedEntry deletes it, and its clear_secrets are filtered): the server keeps its own value.
        var managed = provider.ManagedConfigFields(schema.Key);
        var result = new AdminSignInConfigWrite { Key = schema.Key, ClearSecrets = clears.Where(c => !managed.Contains(c)).ToList() }; var form = schema.AdminForm ?? throw new ArgumentException("Unsupported schema.");
        foreach (var field in form.Fields.Where(f => Visible(form, f, values) && !managed.Contains(f.Key)))
        {
            // An explicit JSON null is a saved value: it round-trips as null (numeric) and never falls back to the declared default.
            var value = values.ContainsKey(field.Key) ? values[field.Key] : field.DefaultValue; var empty = value == null || value is JsonArray a && a.Count == 0 || value is JsonValue j && j.TryGetValue<string>(out var s) && string.IsNullOrWhiteSpace(s);
            if (field.IsSecret && empty) continue; // blank keeps the stored secret; clear_secrets is the only clear.
            if (!empty && field.Validation is { } validation)
            {
                var text = value!.ToString();
                if (validation.MinLength > 0 && text.Length < validation.MinLength || validation.MaxLength > 0 && text.Length > validation.MaxLength) throw new ArgumentException("Field length.");
                if (!string.IsNullOrEmpty(validation.Pattern))
                {
                    try { if (!Regex.IsMatch(text, validation.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new ArgumentException("Field pattern."); }
                    catch (RegexParseException) { }
                }
                if (field.Control == "NUMBER" && double.TryParse(text, CultureInfo.InvariantCulture, out var n) && (validation.HasMin && n < validation.Min || validation.HasMax && n > validation.Max)) throw new ArgumentException("Field range.");
            }
            var type = FieldType(schema, field);
            result.Value[field.Key] = value is JsonArray ? value.DeepClone() : empty ? (type is "number" or "integer" ? null : type == "boolean" ? JsonValue.Create(false) : JsonValue.Create("")) : value?.DeepClone();
        }
        return result;
    }
    public static IReadOnlyList<string> Missing(AdminSignInProvider provider)
    {
        var missing = new List<string>();
        foreach (var schema in provider.GlobalConfigSchema)
        {
            var saved = provider.GlobalConfigs.FirstOrDefault(c => c.Key == schema.Key);
            if (schema.AdminForm == null) { if (schema.Required && saved == null) missing.Add(schema.Title); continue; }
            var values = Values(provider, schema); var required = schema.AdminForm.Fields.Where(f => f.Required && Visible(schema.AdminForm, f, values)).ToArray();
            if (required.Length == 0 && schema.Required && saved == null) missing.Add(schema.Title);
            foreach (var field in required)
                if (field.IsSecret ? saved?.ConfiguredSecrets.Contains(field.Key) != true : values[field.Key] == null || string.IsNullOrWhiteSpace(values[field.Key]?.ToString())) missing.Add(field.Label);
        }
        return missing;
    }
}
