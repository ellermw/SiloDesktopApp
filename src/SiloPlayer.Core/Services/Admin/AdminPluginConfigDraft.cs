using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Manifest-driven form values. Secrets are never populated from readback and are erased on retirement.</summary>
public sealed class AdminPluginConfigDraft : IDisposable
{
    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _clear = new(StringComparer.Ordinal);
    private readonly HashSet<string> _savedSecrets;
    public AdminPluginSchema Schema { get; }
    public AdminPluginForm? Form { get; }
    public bool IsDirty { get; private set; }
    public IReadOnlySet<string> ClearSecrets => _clear;
    public IReadOnlySet<string> SavedSecrets => _savedSecrets;
    public AdminPluginConfigDraft(AdminPluginSchema schema, AdminPluginConfig? saved)
    {
        Schema = schema; Form = Infer(schema); _savedSecrets = new(saved?.ConfiguredSecrets ?? [], StringComparer.Ordinal);
        foreach (var field in Form?.Fields ?? [])
        {
            if (IsSecret(field)) continue;
            if (saved != null && saved.Value.TryGetValue(field.Key, out var value)) _values[field.Key] = value.Clone();
            else if (field.DefaultValue.ValueKind != JsonValueKind.Undefined) _values[field.Key] = field.DefaultValue.Clone();
            else if (field.Control == "SWITCH") _values[field.Key] = JsonSerializer.SerializeToElement(false);
        }
    }
    public JsonElement Value(AdminPluginField field) => _values.TryGetValue(field.Key, out var value) ? value : default;
    public void Set(AdminPluginField field, object? value)
    {
        _values[field.Key] = JsonSerializer.SerializeToElement(value); IsDirty = true;
        if (IsSecret(field) && value is string text && !string.IsNullOrWhiteSpace(text)) _clear.Remove(field.Key);
    }
    public void ToggleClear(AdminPluginField field)
    {
        if (!IsSecret(field) || !_savedSecrets.Contains(field.Key) || field.Required) return;
        if (!_clear.Add(field.Key)) _clear.Remove(field.Key);
        IsDirty = true;
    }
    public bool Conditions(IEnumerable<AdminPluginCondition> conditions) => conditions.All(c => c.Equals.Contains(
        _values.TryGetValue(c.Field, out var v) ? Scalar(v) : Form?.Fields.FirstOrDefault(f => f.Key == c.Field) is { } f ? Scalar(f.DefaultValue) : "", StringComparer.Ordinal));
    public bool Visible(AdminPluginField field)
    {
        if (!Conditions(field.ShowWhen)) return false;
        var sections = Form?.Sections.Where(s => s.FieldKeys.Contains(field.Key)).ToArray() ?? [];
        return sections.Length == 0 || sections.Any(s => Conditions(s.ShowWhen));
    }
    public static bool IsSecret(AdminPluginField field) => field.Secret || field.Control == "PASSWORD";
    public static string Scalar(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "" : v.ValueKind == JsonValueKind.True ? "true" : v.ValueKind == JsonValueKind.False ? "false" : v.ToString();

    public AdminPluginConfigWrite Build()
    {
        if (Form == null) throw new InvalidOperationException("This plugin uses a configuration schema shape that the admin form does not support yet.");
        var result = new AdminPluginConfigWrite { Key = Schema.Key, ClearSecrets = _clear.ToList() };
        foreach (var field in Form.Fields.Where(Visible))
        {
            var raw = Value(field); var text = Scalar(raw); object? value;
            var empty = raw.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || raw.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(text) || raw.ValueKind == JsonValueKind.Array && raw.GetArrayLength() == 0;
            if (empty && IsSecret(field))
            {
                if (field.Required && !_savedSecrets.Contains(field.Key)) throw new ArgumentException(field.Label + " is required.");
                continue; // Omission preserves the server's saved secret.
            }
            if (empty)
            {
                if (field.Required) throw new ArgumentException(field.Label + " is required.");
                value = field.Control == "MULTI_SELECT" || field.ValueType.StartsWith("array", StringComparison.Ordinal) ? Array.Empty<string>() : field.ValueType == "string" ? "" : null;
            }
            else if (field.ValueType is "number" or "integer" || field.Control == "NUMBER")
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || field.ValueType == "integer" && number != Math.Truncate(number)) throw new ArgumentException(field.Label + " must be a number.");
                if (field.Validation is { } numeric && (numeric.HasMin && number < numeric.Min || numeric.HasMax && number > numeric.Max)) throw new ArgumentException(field.Label + " is outside its allowed range.");
                value = number;
            }
            else if (field.ValueType == "boolean" || field.Control == "SWITCH") value = text.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (field.ValueType.StartsWith("array", StringComparison.Ordinal) || field.Control == "MULTI_SELECT")
            {
                var items = raw.ValueKind == JsonValueKind.Array ? raw.EnumerateArray().Select(Scalar).ToArray() : [];
                value = field.ValueType switch
                {
                    "array:integer" => items.Select(s => long.Parse(s, CultureInfo.InvariantCulture)).ToArray(),
                    "array:number" => items.Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray(),
                    "array:boolean" => items.Select(s => s.Equals("true", StringComparison.OrdinalIgnoreCase)).ToArray(), _ => (object)items,
                };
            }
            else
            {
                if (field.Validation is { } validation)
                {
                    if (text.Length < validation.MinLength || validation.MaxLength > 0 && text.Length > validation.MaxLength) throw new ArgumentException(field.Label + " has an invalid length.");
                    if (validation.Pattern.Length > 0)
                    {
                        try { if (!Regex.IsMatch(text, validation.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100))) throw new ArgumentException(field.Label + " is invalid."); }
                        catch (RegexParseException) { /* Official form ignores malformed plugin patterns. */ }
                    }
                }
                value = text;
            }
            result.Value[field.Key] = JsonSerializer.SerializeToElement(value);
        }
        return result;
    }
    private static AdminPluginForm? Infer(AdminPluginSchema schema)
    {
        var explicitForm = schema.AdminForm == null ? null : JsonSerializer.Deserialize<AdminPluginForm>(JsonSerializer.Serialize(schema.AdminForm, AdminPluginsApi.WireOptions), AdminPluginsApi.WireOptions);
        var fields = explicitForm?.Fields ?? [];
        try
        {
            using var document = JsonDocument.Parse(schema.JsonSchema, new() { MaxDepth = 32 }); var root = document.RootElement;
            if (!root.TryGetProperty("type", out var kind) || kind.GetString() != "object" || !root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) return fields.Count > 0 ? explicitForm : null;
            if (properties.EnumerateObject().Count() > 200) return null;
            var required = root.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.Array ? r.EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal) : [];
            var unsupported = false;
            foreach (var p in properties.EnumerateObject())
            {
                var data = p.Value;
                var type = data.TryGetProperty("type", out var t) ? t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : t.ValueKind == JsonValueKind.Array ? t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String && x.GetString() != "null").Select(x => x.GetString()).FirstOrDefault() ?? "" : "" : "";
                var secret = data.TryGetProperty("writeOnly", out var w) && w.ValueKind == JsonValueKind.True || data.TryGetProperty("format", out var format) && format.GetString() == "password";
                var field = fields.FirstOrDefault(f => f.Key == p.Name);
                if (field == null)
                {
                    if (type is not ("string" or "integer" or "number" or "boolean")) { unsupported = true; continue; }
                    field = new() { Key = p.Name, Label = data.TryGetProperty("title", out var title) ? title.GetString() ?? p.Name : p.Name,
                        Description = data.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "", Required = required.Contains(p.Name),
                        Control = type == "boolean" ? "SWITCH" : type is "number" or "integer" ? "NUMBER" : secret ? "PASSWORD" : "TEXT" };
                    if (data.TryGetProperty("default", out var def) && !secret) field.DefaultValue = def.Clone();
                    fields.Add(field);
                }
                field.Secret |= secret;
                field.ValueType = type.Length > 0 ? type : field.Control == "NUMBER" ? "number" : field.Control == "SWITCH" ? "boolean" : field.Control == "MULTI_SELECT" ? "array" : "string";
                if (type == "array" && data.TryGetProperty("items", out var items))
                {
                    if (items.TryGetProperty("$ref", out var reference) && reference.GetString() is { } pointer && pointer.StartsWith("#/", StringComparison.Ordinal))
                    {
                        var target = root;
                        foreach (var segment in pointer[2..].Split('/')) { if (!target.TryGetProperty(segment.Replace("~1", "/").Replace("~0", "~"), out target)) break; }
                        if (target.ValueKind == JsonValueKind.Object) items = target;
                    }
                    if (items.TryGetProperty("type", out var itemType) && itemType.ValueKind == JsonValueKind.String) field.ValueType = "array:" + itemType.GetString();
                }
            }
            if (unsupported && schema.AdminForm?.Fields.Count is not > 0) return null;
            return new() { Fields = fields, Sections = explicitForm?.Sections ?? [], SubmitLabel = explicitForm?.SubmitLabel };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return fields.Count > 0 ? explicitForm : null; }
    }
    public void Dispose() { _values.Clear(); _clear.Clear(); IsDirty = false; }
}
