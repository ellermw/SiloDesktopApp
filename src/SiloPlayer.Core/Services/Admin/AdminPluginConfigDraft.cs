using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A property type declared in a plugin's json_schema (official parseFieldTypes).</summary>
public enum AdminPluginFieldType { String, Integer, Number, Boolean, Array, ArrayBoolean, ArrayInteger, ArrayNumber }

/// <summary>
/// One global configuration entry's form, ported from the official PluginConfigForm, SchemaForm, schemaFormUtils and
/// configSchemaAdminForm at 3516fb41efe04d8ccf7b6d3b9df085bcb3369c9e. The draft starts from the raw saved values.
/// A secret the server does not return is never populated; it is only ever a typed replacement or an explicit clear,
/// and it is erased on <see cref="Dispose"/>. Dirty is a comparison with the starting values, never an event count.
/// </summary>
public sealed class AdminPluginConfigDraft : IDisposable
{
    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(100);
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _initial = new(StringComparer.Ordinal);
    // What the server stored for each editable, non-secret field, exactly as sent: an untouched field goes back as it came,
    // whatever the form could show of it (a value outside the options, a number outside its range, an unknown shape).
    private readonly Dictionary<string, JsonElement> _rawValues = new(StringComparer.Ordinal);
    // The stored items of an array field, each with the text the form shows for it, so a changed list keeps the items the admin did not touch.
    private readonly Dictionary<string, List<(string Text, JsonElement Raw)>> _rawItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _clear = new(StringComparer.Ordinal);
    private readonly List<string> _clearOrder = [];
    private readonly Dictionary<string, AdminPluginFieldType> _types;
    private readonly HashSet<string> _managed;
    private bool _disposed;

    public AdminPluginSchema Schema { get; }
    /// <summary>The form the admin edits, or null when the schema shape is not supported (shown as such).</summary>
    public AdminPluginForm? Form { get; }
    public IReadOnlyList<AdminPluginField> Fields => Form?.Fields ?? [];
    public IReadOnlyList<string> SavedSecrets { get; }
    public IReadOnlySet<string> Managed => _managed;
    public string Fingerprint { get; }

    public AdminPluginConfigDraft(AdminPluginSchema schema, AdminPluginConfig? saved, IEnumerable<string>? managedFields = null)
    {
        Schema = schema;
        _managed = new HashSet<string>(managedFields ?? [], StringComparer.Ordinal);
        Form = Descriptor(schema);
        _types = ParseFieldTypes(schema.JsonSchema);
        SavedSecrets = (saved?.ConfiguredSecrets ?? []).Distinct(StringComparer.Ordinal).ToArray();
        Fingerprint = Fingerprinted(schema, saved, _managed);
        foreach (var field in Fields)
        {
            var value = StartingValue(field, saved?.Value);
            _values[field.Key] = value; _initial[field.Key] = Copy(value);
            if (!IsSecret(field) && saved != null && saved.Value.TryGetValue(field.Key, out var raw))
            {
                _rawValues[field.Key] = raw.Clone();
                if (value is List<string> && raw.ValueKind == JsonValueKind.Array) _rawItems[field.Key] = raw.EnumerateArray().Select(item => (ItemText(item), item.Clone())).ToList();
            }
        }
    }

    // ── descriptor (official adminFormForConfigSchema) ─────────────────────

    private static bool IsSensitiveProperty(JsonElement property) =>
        property.ValueKind == JsonValueKind.Object &&
        (property.TryGetProperty("writeOnly", out var write) && write.ValueKind == JsonValueKind.True ||
         property.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String && format.GetString() == "password");

    private static AdminPluginField Clone(AdminPluginField field) => new()
    {
        Key = field.Key, Label = field.Label, Description = field.Description, Control = field.Control, Placeholder = field.Placeholder,
        Required = field.Required, Secret = field.Secret, Multiline = field.Multiline, Rows = field.Rows, DynamicOptions = field.DynamicOptions,
        ExclusiveGroupField = field.ExclusiveGroupField, DefaultValue = field.DefaultValue.ValueKind == JsonValueKind.Undefined ? default : field.DefaultValue.Clone(),
        Options = field.Options.Select(o => new AdminPluginOption { Value = o.Value, Label = o.Label, Description = o.Description }).ToList(),
        ShowWhen = field.ShowWhen.Select(c => new AdminPluginCondition { Field = c.Field, Values = [.. c.Values] }).ToList(),
        Validation = field.Validation,
    };

    private static AdminPluginForm CloneForm(AdminPluginForm form, IEnumerable<AdminPluginField> fields) => new()
    {
        Fields = fields.ToList(), SubmitLabel = form.SubmitLabel,
        Sections = form.Sections.Select(s => new AdminPluginFormSection
        {
            Key = s.Key, Title = s.Title, Description = s.Description, Collapsible = s.Collapsible, CollapsedDefault = s.CollapsedDefault,
            FieldKeys = [.. s.FieldKeys], ShowWhen = s.ShowWhen.Select(c => new AdminPluginCondition { Field = c.Field, Values = [.. c.Values] }).ToList(),
        }).ToList(),
    };

    internal static AdminPluginForm? Descriptor(AdminPluginSchema schema)
    {
        var explicitFields = schema.AdminForm?.Fields ?? [];
        AdminPluginForm? Explicit() => explicitFields.Count > 0 ? CloneForm(schema.AdminForm!, explicitFields.Select(Clone)) : null;
        try
        {
            using var document = JsonDocument.Parse(schema.JsonSchema ?? "", new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "object" ||
                !root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) return Explicit();
            var required = root.TryGetProperty("required", out var requiredList) && requiredList.ValueKind == JsonValueKind.Array
                ? requiredList.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal) : [];
            var inferred = new List<AdminPluginField?>();
            var sensitive = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            foreach (var property in properties.EnumerateObject())
            {
                if (++count > 400) return Explicit();
                var data = property.Value;
                if (IsSensitiveProperty(data)) sensitive.Add(property.Name);
                var propertyType = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                if (propertyType is not ("string" or "number" or "integer" or "boolean")) { inferred.Add(null); continue; }
                var secret = IsSensitiveProperty(data);
                var field = new AdminPluginField
                {
                    Key = property.Name,
                    Label = data.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String && title.GetString() is { Length: > 0 } text ? text : AdminPluginPresentation.HumanizeConfigKey(property.Name),
                    Description = data.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String ? description.GetString() : null,
                    Control = propertyType == "boolean" ? "SWITCH" : propertyType is "number" or "integer" ? "NUMBER" : secret ? "PASSWORD" : "TEXT",
                    Placeholder = "", Required = required.Contains(property.Name), Secret = secret,
                };
                // A secret never starts from a schema default: it is only ever typed.
                if (!secret && data.TryGetProperty("default", out var def) && def.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                    field.DefaultValue = def.Clone();
                inferred.Add(field);
            }
            if (explicitFields.Count > 0)
            {
                var explicitKeys = explicitFields.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
                var merged = explicitFields.Select(Clone).ToList();
                foreach (var field in merged) if (sensitive.Contains(field.Key)) field.Secret = true;
                merged.AddRange(inferred.Where(f => f != null && !explicitKeys.Contains(f.Key))!);
                return CloneForm(schema.AdminForm!, merged);
            }
            if (inferred.Any(f => f == null)) return null;
            return new AdminPluginForm { Fields = inferred.Select(f => f!).ToList(), SubmitLabel = schema.AdminForm?.SubmitLabel, Sections = schema.AdminForm?.Sections?.Select(s => s).ToList() ?? [] };
        }
        catch (JsonException) { return Explicit(); }
        catch (InvalidOperationException) { return Explicit(); }
    }

    // ── declared types (official parseFieldTypes) ──────────────────────────

    internal static Dictionary<string, AdminPluginFieldType> ParseFieldTypes(string? jsonSchema)
    {
        var result = new Dictionary<string, AdminPluginFieldType>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(jsonSchema)) return result;
        try
        {
            using var document = JsonDocument.Parse(jsonSchema, new JsonDocumentOptions { MaxDepth = 32 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) return result;
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object || !property.Value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) continue;
                switch (type.GetString())
                {
                    case "array":
                        var items = property.Value.TryGetProperty("items", out var itemSchema) ? ArrayItemType(itemSchema, root) : null;
                        result[property.Name] = items switch { "integer" => AdminPluginFieldType.ArrayInteger, "number" => AdminPluginFieldType.ArrayNumber, "boolean" => AdminPluginFieldType.ArrayBoolean, _ => AdminPluginFieldType.Array };
                        break;
                    case "string": result[property.Name] = AdminPluginFieldType.String; break;
                    case "integer": result[property.Name] = AdminPluginFieldType.Integer; break;
                    case "number": result[property.Name] = AdminPluginFieldType.Number; break;
                    case "boolean": result[property.Name] = AdminPluginFieldType.Boolean; break;
                }
            }
        }
        catch (JsonException) { result.Clear(); }
        return result;
    }

    private static JsonElement? ResolveLocalRef(JsonElement schema, JsonElement root)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("$ref", out var reference)) return schema;
        if (reference.ValueKind != JsonValueKind.String) return schema;
        var match = Regex.Match(reference.GetString() ?? "", @"^#/(\$defs|definitions)/([^/]+)$", RegexOptions.None, RegexLimit);
        if (!match.Success || !root.TryGetProperty(match.Groups[1].Value, out var defs) || defs.ValueKind != JsonValueKind.Object) return null;
        // JSON Pointer escapes "/" as "~1" and "~" as "~0" (RFC 6901).
        var name = match.Groups[2].Value.Replace("~1", "/").Replace("~0", "~");
        return defs.TryGetProperty(name, out var target) ? target : null;
    }

    private static string? ArrayItemType(JsonElement itemSchema, JsonElement root)
    {
        if (ResolveLocalRef(itemSchema, root) is not { ValueKind: JsonValueKind.Object } items) return null;
        if (items.TryGetProperty("type", out var type))
        {
            var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Where(e => e.ValueKind != JsonValueKind.String || e.GetString() != "null").ToArray() : [type];
            if (types.Length == 1 && types[0].ValueKind == JsonValueKind.String && types[0].GetString() is "string" or "integer" or "number" or "boolean") return types[0].GetString();
        }
        if (!items.TryGetProperty("enum", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0) return null;
        var list = values.EnumerateArray().ToArray();
        if (list.All(v => v.ValueKind == JsonValueKind.String)) return "string";
        if (list.All(v => v.ValueKind is JsonValueKind.True or JsonValueKind.False)) return "boolean";
        if (list.All(v => v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d == Math.Floor(d))) return "integer";
        if (list.All(v => v.ValueKind == JsonValueKind.Number)) return "number";
        return null;
    }

    // ── values ─────────────────────────────────────────────────────────────

    private static object? FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => element.GetDouble(),
        JsonValueKind.Array => element.EnumerateArray().Select(FromJson).ToList(),
        _ => null,
    };

    private static string NumberText(double number) =>
        number == Math.Floor(number) && Math.Abs(number) < 1e15 ? ((long)number).ToString(CultureInfo.InvariantCulture) : number.ToString("R", CultureInfo.InvariantCulture);

    private static object? Copy(object? value) => value is List<string> list ? new List<string>(list) : value;

    private static string ItemText(JsonElement item) => FromJson(item) switch { string s => s, bool b => b ? "true" : "false", double d => NumberText(d), null => "", _ => item.ToString() };

    private static object StartingValue(AdminPluginField field, IReadOnlyDictionary<string, JsonElement>? saved)
    {
        // A secret is only ever typed: nothing the server sent, and no default, ever fills it in.
        if (IsSecret(field)) return "";
        if (saved != null && saved.TryGetValue(field.Key, out var raw))
        {
            if (raw.ValueKind is JsonValueKind.True or JsonValueKind.False) return raw.GetBoolean();
            if (raw.ValueKind == JsonValueKind.String) return raw.GetString() ?? "";
            if (field.Control == "MULTI_SELECT" && raw.ValueKind == JsonValueKind.Array)
                return raw.EnumerateArray().Select(ItemText).ToList();
            if (raw.ValueKind == JsonValueKind.Number) return NumberText(raw.GetDouble());
        }
        var def = field.DefaultValue;
        if (def.ValueKind is JsonValueKind.True or JsonValueKind.False) return def.GetBoolean();
        if (def.ValueKind == JsonValueKind.String) return def.GetString() ?? "";
        if (def.ValueKind == JsonValueKind.Number) return NumberText(def.GetDouble());
        if (field.Control == "SWITCH") return false;
        return field.Control == "MULTI_SELECT" ? new List<string>() : "";
    }

    private static string Fingerprinted(AdminPluginSchema schema, AdminPluginConfig? saved, IEnumerable<string> managed)
    {
        var parts = new List<string> { schema.Key, schema.JsonSchema, JsonSerializer.Serialize(schema.AdminForm, AdminPluginWire.Options), string.Join('\u001f', managed.OrderBy(k => k, StringComparer.Ordinal)) };
        if (saved != null)
        {
            parts.Add(string.Join('\u001f', saved.ConfiguredSecrets.OrderBy(k => k, StringComparer.Ordinal)));
            parts.Add(string.Join('\u001f', saved.Value.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value.GetRawText())));
        }
        return string.Join('\u001e', parts);
    }

    /// <summary>The form value of a field: string, bool, or a list of strings for a multi-select.</summary>
    public object? Get(AdminPluginField field) => _values.TryGetValue(field.Key, out var value) ? value : null;

    public bool IsManaged(string key) => _managed.Contains(key);
    public bool AllManaged => Fields.Count > 0 && Fields.All(item => _managed.Contains(item.Key));

    public static bool IsSecret(AdminPluginField field) => field.Secret || field.Control == "PASSWORD";

    public void Set(AdminPluginField field, object? value)
    {
        if (_disposed || _managed.Contains(field.Key) || !_values.ContainsKey(field.Key)) return;
        _values[field.Key] = Copy(value);
        // A typed replacement for a saved secret cancels a pending clear.
        if (_clear.Contains(field.Key) && value is string text && text.Trim().Length > 0) { _clear.Remove(field.Key); _clearOrder.Remove(field.Key); }
    }

    /// <summary>
    /// Ticks or unticks one option of a multi-select (official SchemaForm: the clicked value is appended or removed and nothing else
    /// changes). Stored values the options do not list stay, in their stored order.
    /// </summary>
    public void SetOption(AdminPluginField field, string optionValue, bool selected)
    {
        if (_disposed || _managed.Contains(field.Key) || !_values.TryGetValue(field.Key, out var value) || value is not List<string> current) return;
        var next = new List<string>(current);
        if (selected) { if (!next.Contains(optionValue, StringComparer.Ordinal)) next.Add(optionValue); }
        else next.RemoveAll(item => string.Equals(item, optionValue, StringComparison.Ordinal));
        Set(field, next);
    }

    public bool IsClearing(string key) => !_managed.Contains(key) && _clear.Contains(key);

    /// <summary>Whether the saved-secret row offers Clear: not for a required field and not for a managed one.</summary>
    public bool CanClear(string key) => !_managed.Contains(key) && !(Fields.FirstOrDefault(f => f.Key == key)?.Required ?? false);

    public void ToggleClear(string key)
    {
        if (_disposed || !SavedSecrets.Contains(key) || !CanClear(key)) return;
        if (!_clear.Add(key)) { _clear.Remove(key); _clearOrder.Remove(key); } else _clearOrder.Add(key);
    }

    public IReadOnlyList<string> ClearSecrets => _clearOrder.Where(key => !_managed.Contains(key)).ToArray();

    public bool IsDirty
    {
        get
        {
            if (_disposed) return false;
            if (ClearSecrets.Count > 0) return true;
            foreach (var item in Fields)
            {
                if (_managed.Contains(item.Key)) continue;
                if (!Unchanged(item.Key)) return true;
            }
            return false;
        }
    }

    private bool Unchanged(string key)
    {
        var now = _values[key]; var before = _initial[key];
        return now is List<string> a && before is List<string> b ? a.SequenceEqual(b) : Equals(now, before);
    }

    // ── visibility (official evaluateShowWhen / fieldIsVisible) ────────────

    private static string Stringify(object? value) => value switch
    {
        null => "", bool b => b ? "true" : "false", List<string> list => string.Join(',', list), _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    public bool Conditions(IEnumerable<AdminPluginCondition>? conditions) => conditions == null || conditions.All(c =>
        c.Values.Contains(Stringify(_values.TryGetValue(c.Field, out var value) ? value : FromJson(Fields.FirstOrDefault(f => f.Key == c.Field)?.DefaultValue ?? default)), StringComparer.Ordinal));

    /// <summary>Whether the field's own condition and one of its sections' conditions hold.</summary>
    public bool Visible(AdminPluginField field)
    {
        if (!Conditions(field.ShowWhen)) return false;
        var sections = Form?.Sections.Where(s => s.FieldKeys.Contains(field.Key)).ToArray() ?? [];
        return sections.Length == 0 || sections.Any(s => Conditions(s.ShowWhen));
    }

    // ── validation (official validateSchemaValues; messages from the shared catalog) ──

    private static bool IsEmpty(object? value) => value switch { null => true, string s => s.Trim().Length == 0, List<string> l => l.Count == 0, _ => false };

    private static bool TryNumber(string text, out double number) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    /// <summary>
    /// Errors of the fields the admin can currently see and change. A hidden field never blocks a save, and neither does an
    /// environment-managed one: it is locked, never sent, and the admin could not correct it.
    /// </summary>
    public IReadOnlyDictionary<string, string> Errors()
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_disposed) return errors;
        foreach (var field in Fields)
        {
            if (!Visible(field) || _managed.Contains(field.Key)) continue;
            var raw = _values[field.Key];
            var label = string.IsNullOrEmpty(field.Label) ? field.Key : field.Label;
            if (field.Required && IsEmpty(raw) && !(IsSecret(field) && SavedSecrets.Contains(field.Key) && !_clear.Contains(field.Key))) { errors[field.Key] = AdminPluginsText.T("web.common.schemaForm.required", new { label }); continue; }
            if (IsEmpty(raw)) continue;
            var validation = field.Validation;
            if (raw is string text)
            {
                var patternFailed = false;
                if (!string.IsNullOrEmpty(validation?.Pattern))
                {
                    try { patternFailed = !Regex.IsMatch(text, validation.Pattern, RegexOptions.None, RegexLimit); }
                    catch (ArgumentException) { /* a malformed plugin pattern is no constraint */ }
                    catch (RegexMatchTimeoutException) { /* an unbounded pattern is no constraint either */ }
                }
                if (patternFailed) errors[field.Key] = AdminPluginsText.T("web.common.schemaForm.invalid", new { label });
                else if (validation is { MinLength: > 0 } && text.Length < validation.MinLength) errors[field.Key] = AdminPluginsText.T("web.common.schemaForm.minLength", AdminPluginsText.Count(validation.MinLength, ("label", label)));
                else if (validation is { MaxLength: > 0 } && text.Length > validation.MaxLength) errors[field.Key] = AdminPluginsText.T("web.common.schemaForm.maxLength", AdminPluginsText.Count(validation.MaxLength, ("label", label)));
                if (field.Control == "NUMBER" && text.Trim().Length > 0)
                {
                    if (!TryNumber(text, out var n)) errors[field.Key] = AdminPluginsText.T("web.common.schemaForm.notNumber", new { label });
                    else if (validation is { HasMin: true } && n < validation.Min) errors[field.Key] = AdminPluginsText.T("web.common.schemaForm.min", new { label, min = NumberText(validation.Min) });
                    else if (validation is { HasMax: true } && n > validation.Max) errors[field.Key] = AdminPluginsText.T("web.common.schemaForm.max", new { label, max = NumberText(validation.Max) });
                }
            }
        }
        return errors;
    }

    // ── building the request (official coerceFieldValue / buildSchemaValues, explicit clears) ──

    private static bool CoerceBoolean(object? raw) => raw switch { bool b => b, string s => s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase), null => false, _ => true };

    private static object? CoerceIntegerString(object? value) => value is string s && Regex.IsMatch(s, @"^-?[0-9]+$", RegexOptions.None, RegexLimit) && long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : value;

    private static object? CoerceNumberString(object? value)
    {
        if (value is not string s) return value;
        var trimmed = s.Trim();
        return trimmed.Length > 0 && Regex.IsMatch(trimmed, @"^-?[0-9]+(\.[0-9]+)?$", RegexOptions.None, RegexLimit) && TryNumber(trimmed, out var n) ? n : value;
    }

    private (bool Defined, object? Value) Coerce(AdminPluginField field, object? raw)
    {
        if (_types.TryGetValue(field.Key, out var type))
        {
            switch (type)
            {
                case AdminPluginFieldType.Boolean: return (true, CoerceBoolean(raw));
                case AdminPluginFieldType.Integer:
                case AdminPluginFieldType.Number:
                    if (raw is string s) { if (s.Trim().Length == 0) return (false, null); if (!TryNumber(s, out var n)) return (true, s); return (true, type == AdminPluginFieldType.Integer && n == Math.Floor(n) && Math.Abs(n) < 9e15 ? (object)(long)n : n); }
                    return (true, raw);
                case AdminPluginFieldType.String:
                    // A declared string is never turned into a number, even when it is all digits.
                    if (raw is string text) return text.Trim().Length == 0 ? (false, null) : (true, text);
                    return (true, raw);
                default:
                    return (true, (raw is List<string> l ? l : []).Select(v => CoerceItem(field, v)).ToList());
            }
        }
        // No declared type: the control decides.
        if (field.Control == "SWITCH") return (true, CoerceBoolean(raw));
        if (field.Control == "MULTI_SELECT") return (true, (raw is List<string> items ? items : []).Select(v => CoerceItem(field, v)).ToList());
        if (field.Control == "SELECT" && field.DynamicOptions)
        {
            if (raw is string select && select.Trim().Length == 0) return (false, null);
            return (true, CoerceIntegerString(raw));
        }
        if (field.Control == "NUMBER")
        {
            if (raw is string number && number.Trim().Length > 0) return (true, TryNumber(number, out var n) ? n : number);
            return (false, null);
        }
        if (raw is string plain) return plain.Trim().Length == 0 ? (false, null) : (true, plain);
        return (true, raw);
    }

    /// <summary>One list item as the field's declared item type wants it (a list with no declared type follows the control: numeric text becomes an integer).</summary>
    private object? CoerceItem(AdminPluginField field, string item) =>
        _types.TryGetValue(field.Key, out var type)
            ? type switch
            {
                AdminPluginFieldType.ArrayBoolean => CoerceBoolean(item),
                AdminPluginFieldType.ArrayInteger => CoerceIntegerString(item),
                AdminPluginFieldType.ArrayNumber => CoerceNumberString(item),
                _ => item,
            }
            : CoerceIntegerString(item);

    private bool IsArrayField(AdminPluginField field) =>
        _types.TryGetValue(field.Key, out var type) ? type is AdminPluginFieldType.Array or AdminPluginFieldType.ArrayBoolean or AdminPluginFieldType.ArrayInteger or AdminPluginFieldType.ArrayNumber
            : field.Control == "MULTI_SELECT";

    /// <summary>The list to send: an item that is still in the list and was stored goes back exactly as stored, a new one is coerced.</summary>
    private List<JsonElement> Items(AdminPluginField field, List<string> current)
    {
        var stored = _rawItems.GetValueOrDefault(field.Key) ?? [];
        var used = new bool[stored.Count]; var items = new List<JsonElement>(current.Count);
        foreach (var text in current)
        {
            var at = -1;
            for (var index = 0; index < stored.Count; index++) if (!used[index] && string.Equals(stored[index].Text, text, StringComparison.Ordinal)) { at = index; break; }
            if (at >= 0) { used[at] = true; items.Add(stored[at].Raw.Clone()); }
            else items.Add(JsonSerializer.SerializeToElement(CoerceItem(field, text)));
        }
        return items;
    }

    private object? Cleared(AdminPluginField field) =>
        _types.TryGetValue(field.Key, out var type) ? type == AdminPluginFieldType.String ? "" : null : field.Control == "NUMBER" || field.DynamicOptions ? null : "";

    /// <summary>
    /// The request body: every visible, editable field. An emptied non-secret field is an explicit clear, since a save
    /// merges into the stored entry; a blank secret is left out, which keeps the stored one; clearing a secret is
    /// the separate clear_secrets list. Managed fields are never sent. The caller checks <see cref="Errors"/> first.
    /// </summary>
    public AdminPluginConfigWrite Build()
    {
        var write = new AdminPluginConfigWrite { Key = Schema.Key, ClearSecrets = [.. ClearSecrets] };
        if (_disposed) return write; // a disposed draft holds nothing to send
        foreach (var field in Fields)
        {
            if (!Visible(field) || _managed.Contains(field.Key)) continue;
            // A field the admin never touched goes back exactly as it was stored, whatever the form could make of it: a value the
            // options do not list, a number past its range, an unknown shape, an array of numbers, a precision a double cannot hold.
            if (_rawValues.TryGetValue(field.Key, out var original) && Unchanged(field.Key)) { write.Value[field.Key] = original.Clone(); continue; }
            var raw = _values[field.Key];
            // A changed list keeps the stored items the admin did not touch as they were stored.
            if (raw is List<string> list && IsArrayField(field)) { write.Value[field.Key] = JsonSerializer.SerializeToElement(Items(field, list)); continue; }
            var (defined, value) = Coerce(field, raw);
            if (!defined)
            {
                if (!field.Secret && field.Control != "PASSWORD") write.Value[field.Key] = JsonSerializer.SerializeToElement(Cleared(field));
                continue;
            }
            write.Value[field.Key] = JsonSerializer.SerializeToElement(value);
        }
        return write;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var key in _values.Keys.ToArray()) _values[key] = null;
        _values.Clear(); _initial.Clear(); _rawValues.Clear(); _rawItems.Clear(); _clear.Clear(); _clearOrder.Clear();
    }
}

/// <summary>The wire serializer options shared by the Plugins models.</summary>
public static class AdminPluginWire
{
    public static JsonSerializerOptions Options { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, MaxDepth = 48 };
}
