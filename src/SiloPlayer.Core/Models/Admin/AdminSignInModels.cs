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
    [JsonIgnore] public AdminSignInCapability? Auth => Capabilities.FirstOrDefault(c => c.Type == "auth_provider.v1");
    [JsonIgnore] public AdminSignInBinding? Binding => AuthBindings.FirstOrDefault(b => b.CapabilityId == Auth?.Id);
    [JsonIgnore] public bool Network => Auth?.SignInMode == "network";
    [JsonIgnore] public string Name => GlobalConfigs.FirstOrDefault(c => c.Key == "display_name")?.Value["value"]?.GetValue<string>() ?? Auth?.DisplayName ?? PluginId;
    public override string ToString() => nameof(AdminSignInProvider);
}
public sealed class AdminSignInCapability
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SignInMode { get; set; } = "";
    public string? CallbackUrl { get; set; }
    public string? PostLogoutRedirectUrl { get; set; }
    public JsonObject Metadata { get; set; } = new();
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
public sealed class AdminSignInForm
{
    public List<AdminSignInField> Fields { get; set; } = [];
    public List<AdminSignInSection> Sections { get; set; } = [];
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
    public List<string> FieldKeys { get; set; } = [];
    public List<AdminSignInCondition> ShowWhen { get; set; } = [];
}
public sealed class AdminSignInConfig
{
    public string Key { get; set; } = "";
    public JsonObject Value { get; set; } = new();
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
