using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

public sealed class AdminSignInCapabilities
{
    public bool Available { get; set; }
    public bool BreakGlass { get; set; }
    public bool ConnectionTest { get; set; }
    public bool LiveProviderChanges { get; set; }
}
public sealed class AdminSignInProvider
{
    public string Id { get; set; } = "";
    public string PluginId { get; set; } = "";
    public bool Enabled { get; set; }
    public List<AdminSignInCapability> Capabilities { get; set; } = [];
    public List<AdminSignInBinding> AuthBindings { get; set; } = [];
    public List<AdminSignInSchema> GlobalConfigSchema { get; set; } = [];
    public List<AdminSignInConfig> GlobalConfigs { get; set; } = [];
    /// <summary>Present only when the deployment provisions this installation (official <c>env_managed</c>); an older server never sends it, which means nothing is managed.</summary>
    public AdminSignInOwnership? EnvManaged { get; set; }
    /// <summary>Official <c>presentation</c>: only its display name is read.</summary>
    public AdminSignInPresentation? Presentation { get; set; }
    [JsonIgnore] public AdminSignInCapability? Auth => Capabilities.FirstOrDefault(c => c.Type == "auth_provider.v1");
    [JsonIgnore] public AdminSignInBinding? Binding => AuthBindings.FirstOrDefault(b => b.CapabilityId == Auth?.Id);
    [JsonIgnore] public bool Network => Auth?.SignInMode == "network";
    /// <summary>Whether the deployment owns this binding field (official <c>authBindingFieldIsManaged</c>: it reads the capability's list, which can name a field before any binding row exists).</summary>
    public bool BindingFieldManaged(string field) => Auth?.EnvManaged.Contains(field) == true;
    /// <summary>The top-level fields of one global configuration entry the deployment owns (official <c>env_managed.config_fields[key]</c>).</summary>
    public IReadOnlyList<string> ManagedConfigFields(string schemaKey) => EnvManaged?.ConfigFields.TryGetValue(schemaKey, out var fields) == true ? fields : [];
    public bool ConfigFieldManaged(string schemaKey, string field) => ManagedConfigFields(schemaKey).Contains(field);
    /// <summary>The name an admin knows the plugin by (official <c>authProviderName</c>): presentation name, else the capability's name, else the plugin id.</summary>
    [JsonIgnore] public string PluginName => FirstText(Presentation?.DisplayName, Auth?.DisplayName) ?? PluginId;
    /// <summary>The label people know the provider by (official <c>authProviderLabel</c>): the saved display_name when it is a non-blank string (any other saved shape is ignored), else the capability's declared metadata display_name, else the capability's name, else <see cref="PluginName"/>.</summary>
    [JsonIgnore] public string Name => SavedDisplayName() ?? FirstText(Auth?.Metadata["display_name"] is JsonValue declared && declared.TryGetValue<string>(out var label) ? label : null, Auth?.DisplayName) ?? PluginName;
    private static string? FirstText(params string?[] candidates) => candidates.Select(c => c?.Trim()).FirstOrDefault(c => !string.IsNullOrEmpty(c));
    private string? SavedDisplayName() =>
        GlobalConfigs.FirstOrDefault(c => c.Key == "display_name")?.Value["value"] is JsonValue saved && saved.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    public override string ToString() => nameof(AdminSignInProvider);
}
/// <summary>Official <c>AdminPluginProvisionOwnership</c>. Dictionaries and lists a server sends as null read as empty.</summary>
public sealed class AdminSignInOwnership
{
    private Dictionary<string, List<string>> _config = [], _binding = [];
    public Dictionary<string, List<string>> ConfigFields { get => _config; set => _config = value ?? []; }
    public Dictionary<string, List<string>> BindingFields { get => _binding; set => _binding = value ?? []; }
    public string? VersionPin { get; set; }
}
public sealed class AdminSignInPresentation { public string? DisplayName { get; set; } }
public sealed class AdminSignInCapability
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SignInMode { get; set; } = "";
    public string? CallbackUrl { get; set; }
    public string? PostLogoutRedirectUrl { get; set; }
    private List<string> _managed = [];
    /// <summary>The binding fields the deployment owns, which can be named before a binding exists. Absent or null means none (an older server).</summary>
    public List<string> EnvManaged { get => _managed; set => _managed = value ?? []; }
    private JsonObject _metadata = new();
    /// <summary>A server that sends null still yields an empty object: every metadata read is null-safe.</summary>
    public JsonObject Metadata { get => _metadata; set => _metadata = value ?? new(); }
}
public sealed class AdminSignInBinding
{
    public string CapabilityId { get; set; } = "";
    public bool Enabled { get; set; }
    public int DisplayOrder { get; set; } = 1;
    public bool AutoProvision { get; set; } = true;
    public bool DefaultLogin { get; set; }
    public string CallbackUrl { get; set; } = "";
    public string PostLogoutRedirectUrl { get; set; } = "";
    /// <summary>OAuth only: "installation" or "stable". Never sent back by this page (an omitted mode keeps the stored one).</summary>
    public string? CallbackMode { get; set; }
    /// <summary>An enabled OAuth binding is waiting for the public URL.</summary>
    public bool CallbackPending { get; set; }
    private List<string> _managed = [];
    public List<string> EnvManaged { get => _managed; set => _managed = value ?? []; }
}
public sealed class AdminSignInSchema
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string JsonSchema { get; set; } = "";
    public bool Required { get; set; }
    public AdminSignInForm? AdminForm { get; set; }
}
/// <summary>A plugin's own form, passed through by the server unchecked: a list it sends as null reads as empty.</summary>
public sealed class AdminSignInForm
{
    private List<AdminSignInField> _fields = [];
    private List<AdminSignInSection> _sections = [];
    public List<AdminSignInField> Fields { get => _fields; set => _fields = value ?? []; }
    public List<AdminSignInSection> Sections { get => _sections; set => _sections = value ?? []; }
}
public sealed class AdminSignInField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string Control { get; set; } = "TEXT";
    public string Placeholder { get; set; } = "";
    public bool Required { get; set; }
    public bool Secret { get; set; }
    public bool Multiline { get; set; }
    public int Rows { get; set; }
    public JsonNode? DefaultValue { get; set; }
    public List<AdminSignInOption> Options { get; set; } = [];
    public List<AdminSignInCondition> ShowWhen { get; set; } = [];
    public AdminSignInValidation? Validation { get; set; }
    public string? ExclusiveGroupField { get; set; }
    [JsonIgnore] public bool IsSecret => Secret || Control == "PASSWORD";
}
public sealed class AdminSignInOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}
public sealed class AdminSignInCondition
{
    public string Field { get; set; } = "";
    [JsonPropertyName("equals")] public List<string> Matches { get; set; } = [];
}
public sealed class AdminSignInValidation
{
    public bool HasMin { get; set; }
    public double Min { get; set; }
    public bool HasMax { get; set; }
    public double Max { get; set; }
    public int MinLength { get; set; }
    public int MaxLength { get; set; }
    public string? Pattern { get; set; }
}
public sealed class AdminSignInSection
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Collapsible { get; set; }
    public bool CollapsedDefault { get; set; }
    private List<string> _fieldKeys = [];
    private List<AdminSignInCondition> _showWhen = [];
    public List<string> FieldKeys { get => _fieldKeys; set => _fieldKeys = value ?? []; }
    public List<AdminSignInCondition> ShowWhen { get => _showWhen; set => _showWhen = value ?? []; }
}
public sealed class AdminSignInConfig
{
    public string Key { get; set; } = "";
    private JsonObject _value = new();
    /// <summary>A server that sends null still yields an empty object: every saved-value read is null-safe.</summary>
    public JsonObject Value { get => _value; set => _value = value ?? new(); }
    public List<string> ConfiguredSecrets { get; set; } = [];
    public override string ToString() => nameof(AdminSignInConfig);
}
public sealed class AdminSignInConfigWrite
{
    public string Key { get; set; } = "";
    public JsonObject Value { get; set; } = new();
    public List<string> ClearSecrets { get; set; } = [];
    public override string ToString() => nameof(AdminSignInConfigWrite);
}
public sealed class AdminSignInTestResult
{
    [JsonRequired] public bool Ok { get; set; }
    public List<AdminSignInTestStep> Steps { get; set; } = [];
    public string? CallbackUrl { get; set; }
    public override string ToString() => nameof(AdminSignInTestResult);
}
public sealed class AdminSignInTestStep
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Ok { get; set; }
    public string? Message { get; set; }
}
