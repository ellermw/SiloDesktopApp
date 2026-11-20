using System.Text.Json;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Plugins;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Visit-owned editor. Config stays typed until Save; unchanged config is sent verbatim.</summary>
public sealed class AdminAutoscanEditorDraft : IDisposable
{
    public AdminAutoscanDescriptor Descriptor { get; }
    public PluginAdminForm Form { get; }
    public Dictionary<string, object?> Config { get; } = [];
    public bool ConfigDirty { get; set; }
    public string ConnectionId { get; set; } = "";
    public string Interval { get; set; } = "";
    public string Label { get; set; } = "";
    public string DeliveryMode { get; set; }
    public List<AdminAutoscanPathRewrite> Mappings { get; set; } = [];
    public bool MappingsDirty { get; set; }
    // Stored values of fields the descriptor never offers (the server drops secret and dynamic-option fields from source forms).
    // A changed configuration sends them back as stored: unknown keys are never cleared by an edit the admin did not make.
    private readonly Dictionary<string, string> _unmanaged = [];
    private readonly bool _creating;
    private readonly bool _hadMappings;
    private readonly bool _migratedAliases;

    public AdminAutoscanEditorDraft(AdminAutoscanSource? source, AdminAutoscanDescriptor descriptor)
    {
        Descriptor = descriptor;
        _creating = source == null; _hadMappings = source?.PathRewrites.Count > 0;
        DeliveryMode = source?.DeliveryMode ?? AdminAutoscanSourceDraft.DefaultDelivery(descriptor);
        ConnectionId = source?.ConnectionId ?? ""; Interval = source?.PollIntervalSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        Label = source?.Label ?? "";
        Mappings = source?.PathRewrites.Select(row => new AdminAutoscanPathRewrite { From = row.From, To = row.To }).ToList() ?? [];
        // Source-form min/max are meaningful only when has_min/has_max is set.
        Form = descriptor.ConfigForm == null ? new() : JsonSerializer.Deserialize<PluginAdminForm>(JsonSerializer.Serialize(descriptor.ConfigForm))!;
        // The server never offers password or dynamic-option fields on a source form; mirror that so no secret control exists here.
        var unsupported = Form.Fields.Where(field => field.Secret || field.DynamicOptions || field.Control.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase)).Select(field => field.Key).ToHashSet();
        Form.Fields.RemoveAll(field => unsupported.Contains(field.Key));
        var stored = new Dictionary<string, string>(source?.SourceConfig ?? []);
        foreach (var key in unsupported) if (stored.TryGetValue(key, out var kept)) _unmanaged[key] = kept;
        // A source whose capability offers no form at all: the editor shows none of its keys, so a change to "Sent by" must not erase them.
        if (descriptor.ConfigForm == null) foreach (var (key, value) in stored) if (key != "webhook_provider") _unmanaged[key] = value;
        if (source is { PluginId: "silo.autoscan.cephfs", CapabilityId: "cephfs" })
        {
            foreach (var (oldKey, key) in new[] { ("movie_nested_paths", "movie_flat_paths"), ("tv_nested_paths", "tv_flat_paths") })
                if (stored.Remove(oldKey, out var old))
                { stored[key] = string.Join("\n", (stored.GetValueOrDefault(key, "") + "\n" + old).Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct()); ConfigDirty = true; _migratedAliases = true; }
        }
        foreach (var field in Form.Fields)
        {
            if (field.Validation is { } validation) { if (!validation.HasMin) validation.Min = null; if (!validation.HasMax) validation.Max = null; }
            if (stored.TryGetValue(field.Key, out var value))
                Config[field.Key] = field.Control.ToUpperInvariant() switch
                { "SWITCH" or "TOGGLE" or "CHECKBOX" => value == "true", "MULTI_SELECT" => value.Split(',', StringSplitOptions.RemoveEmptyEntries), _ => value };
            else if (field.DefaultValue != null) Config[field.Key] = field.DefaultValue;
        }
        // Provider may be stored even when the descriptor no longer declares it.
        if (stored.TryGetValue("webhook_provider", out var provider)) Config["webhook_provider"] = provider;
        if (ConfigDirty) foreach (var (key, value) in stored) Config.TryAdd(key, value);
    }

    public string? Validate()
    {
        if (AdminAutoscanSourceDraft.ConnectionRequired(Descriptor, DeliveryMode) && ConnectionId.Length == 0)
            return AdminAutoscanText.T("sourceDialog.problem.chooseServer");
        if (DeliveryMode == "poll" && !AdminAutoscanSourceDraft.TryInterval(Interval, out _))
            return AdminAutoscanText.T("sourceDialog.problem.intervalInvalid");
        if (AdminAutoscanSourceDraft.MappingProblem(Mappings, DeliveryMode == "webhook", _hadMappings, _creating) is { } mapping)
            return AdminAutoscanText.T("sourceDialog.problem." + mapping);
        if (Mappings.Count > 1000 || Label.Length > 1024) return "The source exceeds its configuration limits.";
        return ConfigErrors().Values.FirstOrDefault();
    }
    public bool FieldVisible(SiloPlayer.Core.Models.Plugins.PluginAdminFormField field, IReadOnlyDictionary<string, object?> values)
    {
        var effective = ProviderConnectionConfig.EffectiveValues(Form, values);
        if (!ProviderConnectionConfig.Shown(field.ShowWhen, effective)) return false;
        var sections = Form.Sections?.Where(section => section.FieldKeys.Contains(field.Key)).ToArray() ?? [];
        return sections.Length == 0 || sections.Any(section => ProviderConnectionConfig.Shown(section.ShowWhen, effective));
    }
    public Dictionary<string, string> ConfigErrors()
    {
        var values = new Dictionary<string, object?>(Config);
        var visible = Form.Fields.Where(field => FieldVisible(field, values)).ToList();
        var errors = ProviderConnectionConfig.Validate(new() { Fields = visible }, values);
        return errors;
    }
    public AdminAutoscanSourceWrite Build(AdminAutoscanSource? live)
    {
        if (Validate() is { } error) throw new InvalidOperationException(error);
        AdminAutoscanSourceDraft.TryInterval(Interval, out var interval);
        var config = live != null && !ConfigDirty ? new Dictionary<string, string>(live.SourceConfig) : SerializeConfig();
        return new()
        {
            Enabled = live?.Enabled ?? true, DeliveryMode = live?.DeliveryMode ?? DeliveryMode,
            ConnectionId = ConnectionId.Length == 0 || live == null && (DeliveryMode == "webhook" || Descriptor.Connection == "none") ? null : ConnectionId,
            PollIntervalSeconds = DeliveryMode == "poll" ? interval : live?.PollIntervalSeconds,
            PathRewrites = Mappings.Where(r => !string.IsNullOrWhiteSpace(r.From) && !string.IsNullOrWhiteSpace(r.To)).Select(r => new AdminAutoscanPathRewrite { From = r.From.Trim(), To = r.To.Trim() }).ToList(),
            SourceConfig = config, Label = Label.Trim(),
        };
    }
    private Dictionary<string, string> SerializeConfig()
    {
        var output = new Dictionary<string, string>();
        var effective = ProviderConnectionConfig.EffectiveValues(Form, Config);
        foreach (var field in Form.Fields)
        {
            if (!FieldVisible(field, effective)) continue;
            var value = Config.GetValueOrDefault(field.Key) ?? field.DefaultValue;
            var scalar = value is JsonElement { ValueKind: JsonValueKind.Array } array ? string.Join(',', array.EnumerateArray().Select(item => ProviderConnectionConfig.Scalar(item)))
                : value is IEnumerable<string> list ? string.Join(',', list) : ProviderConnectionConfig.Scalar(value);
            // A key the admin or the stored row holds stays, even empty; one nobody touched stays absent.
            if (scalar.Length == 0 && !Config.ContainsKey(field.Key) && value is not IEnumerable<string>) continue;
            if (field.Control == "NUMBER" && double.TryParse(scalar, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
                scalar = number.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
            output[field.Key.Trim()] = scalar.Trim();
        }
        // Alias migration must retain both the existing replacement and legacy lines.
        if (_migratedAliases)
            foreach (var key in new[] { "movie_flat_paths", "tv_flat_paths" })
                if (Config.TryGetValue(key, out var value) && !output.ContainsKey(key)) output[key] = ProviderConnectionConfig.Scalar(value).Trim();
        if (Config.TryGetValue("webhook_provider", out var provider)) output["webhook_provider"] = ProviderConnectionConfig.Scalar(provider);
        // Values the form never offered go back exactly as stored.
        foreach (var (key, kept) in _unmanaged) output.TryAdd(key, kept);
        return output;
    }
    public static bool SameConfiguration(AdminAutoscanSource a, AdminAutoscanSource b) =>
        a.PluginId == b.PluginId && a.CapabilityId == b.CapabilityId && a.DeliveryMode == b.DeliveryMode && a.ConnectionId == b.ConnectionId &&
        a.PollIntervalSeconds == b.PollIntervalSeconds && a.Label == b.Label && a.SourceConfig.Count == b.SourceConfig.Count &&
        a.SourceConfig.All(pair => b.SourceConfig.TryGetValue(pair.Key, out var value) && pair.Value == value) &&
        a.PathRewrites.Select(r => (r.From, r.To)).SequenceEqual(b.PathRewrites.Select(r => (r.From, r.To)));
    public void Dispose() { Config.Clear(); _unmanaged.Clear(); Mappings.Clear(); ConnectionId = Interval = Label = ""; }
}
