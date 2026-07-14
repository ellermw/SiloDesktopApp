namespace SiloPlayer.Core.Models.Plugins;

public class PluginCapability
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public List<string>? Subscriptions { get; set; }
    public List<PluginConfigSchema>? ConfigSchema { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

public class PluginConfigSchema
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string JsonSchema { get; set; } = "";
    public bool Required { get; set; }
    public PluginAdminForm? AdminForm { get; set; }
}

public class PluginAdminForm
{
    public List<PluginAdminFormField> Fields { get; set; } = [];
    public string? SubmitLabel { get; set; }
    public List<PluginAdminFormSection>? Sections { get; set; }
}

public class PluginAdminFormField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public string Control { get; set; } = "TEXT";
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public bool Secret { get; set; }
    public bool Multiline { get; set; }
    public object? DefaultValue { get; set; }
    public List<PluginAdminFormFieldOption>? Options { get; set; }
    public int? Rows { get; set; }
    public bool DynamicOptions { get; set; }
    public List<PluginAdminFormCondition>? ShowWhen { get; set; }
    public PluginAdminFormValidation? Validation { get; set; }
    public string? ExclusiveGroupField { get; set; }
}

public class PluginAdminFormFieldOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
}

public class PluginAdminFormCondition
{
    public string Field { get; set; } = "";
    public new List<string> Equals { get; set; } = [];
}

public class PluginAdminFormValidation
{
    public bool HasMin { get; set; }
    public double? Min { get; set; }
    public bool HasMax { get; set; }
    public double? Max { get; set; }
    public string? Pattern { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
}

public class PluginAdminFormSection
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool Collapsible { get; set; }
    public bool CollapsedDefault { get; set; }
    public List<string> FieldKeys { get; set; } = [];
    public List<PluginAdminFormCondition>? ShowWhen { get; set; }
}

public class PluginAsset
{
    public string Path { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string? Integrity { get; set; }
}
