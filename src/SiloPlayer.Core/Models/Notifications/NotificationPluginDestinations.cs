using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Notifications;

// Official silo-server 3b2b32168 (#3550): GET /api/v2/notifications/plugin-channels,
// /plugin-destinations and the capability's plugin_destinations member.

public sealed class NotificationPluginDestinationCapability
{
    public bool Available { get; set; }
    public int MaxPerProfile { get; set; }
}

public sealed class NotificationPluginSettingsOption
{
    public string Label { get; set; } = "";
    /// <summary>The choice, of the field's type.</summary>
    public JsonElement Value { get; set; }
}

public sealed class NotificationPluginSettingsField
{
    /// <summary>One of text, password, number, switch, select; a control this client does not know renders by <see cref="Type"/>.</summary>
    public string Control { get; set; } = "text";
    public JsonElement? DefaultValue { get; set; }
    public string? Description { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public long? MaxLength { get; set; }
    public double? Maximum { get; set; }
    public long? MinLength { get; set; }
    public double? Minimum { get; set; }
    public List<NotificationPluginSettingsOption>? Options { get; set; }
    public string? Pattern { get; set; }
    public string? Placeholder { get; set; }
    public bool Required { get; set; }
    public bool Secret { get; set; }
    /// <summary>The JSON type the value is sent as: string, integer, number or boolean.</summary>
    public string Type { get; set; } = "string";
}

public sealed class NotificationPluginSettingsForm
{
    public List<NotificationPluginSettingsField> Fields { get; set; } = [];
}

public sealed class NotificationPluginChannel
{
    public string CapabilityId { get; set; } = "";
    public string Description { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PluginId { get; set; } = "";
    public NotificationPluginSettingsForm SettingsForm { get; set; } = new();

    [JsonIgnore]
    public string Key => $"{PluginId}/{CapabilityId}";
}

public sealed class NotificationPluginChannelCollection
{
    public List<NotificationPluginChannel>? Items { get; set; }
}

public sealed class NotificationPluginDestination
{
    /// <summary>An enabled plugin provides the channel. False while that plugin is disabled or uninstalled; nothing is sent then.</summary>
    public bool Available { get; set; }
    public string CapabilityId { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public long ConsecutiveFailures { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? DisabledReason { get; set; }
    public bool Enabled { get; set; }
    /// <summary>Validator of the observed row, sent unchanged as If-Match on an update or delete.</summary>
    [JsonPropertyName("etag")]
    public string ETag { get; set; } = "";
    public string Id { get; set; } = "";
    public string? LastFailureAt { get; set; }
    public string? LastFailureMessage { get; set; }
    public string? LastSuccessAt { get; set; }
    public string Name { get; set; } = "";
    public bool NotifyNextUp { get; set; }
    public bool NotifyRequests { get; set; }
    public string PluginId { get; set; } = "";
    /// <summary>Secret settings that have a stored value; their values are never returned.</summary>
    public List<string> SecretSettingsSet { get; set; } = [];
    /// <summary>Stored non-secret settings the channel's current schema declares; empty while the channel is unavailable.</summary>
    public Dictionary<string, JsonElement> Settings { get; set; } = [];

    [JsonIgnore]
    public string ChannelKey => $"{PluginId}/{CapabilityId}";
}

public sealed class NotificationPluginDestinationCollection
{
    public List<NotificationPluginDestination>? Items { get; set; }
    public SiloPlayer.Core.Api.BrowsePage? Page { get; set; }
}
