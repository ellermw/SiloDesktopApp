using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Settings;

public class WebhookSyncConnection
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    [JsonPropertyName("server_id")] public string ServerId { get; set; } = "";
    [JsonPropertyName("server_name")] public string ServerName { get; set; } = "";
    [JsonPropertyName("default_profile_id")] public string DefaultProfileId { get; set; } = "";
    [JsonPropertyName("webhook_url")] public string? WebhookUrl { get; set; }
    [JsonPropertyName("actor_count")] public int ActorCount { get; set; }
    [JsonPropertyName("account_discovery_available")] public bool AccountDiscoveryAvailable { get; set; }
    [JsonPropertyName("last_webhook_received_at")] public DateTimeOffset? LastWebhookReceivedAt { get; set; }
    [JsonPropertyName("last_webhook_error_at")] public DateTimeOffset? LastWebhookErrorAt { get; set; }
    [JsonPropertyName("last_webhook_error_message")] public string? LastWebhookErrorMessage { get; set; }
}

public class CreateWebhookSyncConnectionResponse
{
    public WebhookSyncConnection Connection { get; set; } = new();
    [JsonPropertyName("webhook_url")] public string WebhookUrl { get; set; } = "";
}

public class RotateWebhookSyncResponse
{
    [JsonPropertyName("webhook_url")] public string WebhookUrl { get; set; } = "";
}

public class WebhookSyncEventLog
{
    public long Id { get; set; }
    [JsonPropertyName("received_at")] public DateTimeOffset ReceivedAt { get; set; }
    [JsonPropertyName("http_status")] public int HttpStatus { get; set; }
    public string Outcome { get; set; } = "";
    public string Summary { get; set; } = "";
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("body_excerpt")] public string? BodyExcerpt { get; set; }
    public Dictionary<string, JsonElement>? Attrs { get; set; }
}

public class WebhookSyncActorMapping
{
    public long Id { get; set; }
    [JsonPropertyName("external_actor_id")] public string ExternalActorId { get; set; } = "";
    [JsonPropertyName("external_actor_name")] public string ExternalActorName { get; set; } = "";
    [JsonPropertyName("continuum_profile_id")] public string? ProfileId { get; set; }
}

public class WebhookSyncDiscoveredActor
{
    [JsonPropertyName("external_actor_id")] public string ExternalActorId { get; set; } = "";
    [JsonPropertyName("external_actor_name")] public string ExternalActorName { get; set; } = "";
}

public class WebhookSyncActorsResponse
{
    public List<WebhookSyncActorMapping> Mappings { get; set; } = [];
    [JsonPropertyName("discovered_actors")] public List<WebhookSyncDiscoveredActor> DiscoveredActors { get; set; } = [];
    [JsonPropertyName("account_discovery_available")] public bool AccountDiscoveryAvailable { get; set; }
}
