using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.Admin;

/// <summary>
/// POST /api/v1/admin/settings/check/{kind} request body.
/// Values carry the full admin-settings map including unsaved edits;
/// dirty_keys tells the server which keys the admin just touched so it
/// knows whether to use an overridden value or reach back to persisted
/// sensitive state.
/// </summary>
public class AdminSettingsConnectionCheckRequest
{
    [JsonPropertyName("values")]
    public Dictionary<string, string> Values { get; set; } = [];

    [JsonPropertyName("dirty_keys")]
    public List<string> DirtyKeys { get; set; } = [];
}

public class ConnectionCheckResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public class AdminSettingUpdateResponse
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("restart_required")]
    public bool RestartRequired { get; set; }
}

public class AdminRestartResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("restart_required")]
    public bool RestartRequired { get; set; }
}

public class PushRelayRegisterRequest
{
    [JsonPropertyName("relay_url")]
    public string RelayUrl { get; set; } = "";
}

public class PushRelayRegisterResponse
{
    [JsonPropertyName("relay_url")]
    public string RelayUrl { get; set; } = "";

    [JsonPropertyName("deployment_id")]
    public string DeploymentId { get; set; } = "";

    [JsonPropertyName("key_prefix")]
    public string KeyPrefix { get; set; } = "";

    [JsonPropertyName("api_key_configured")]
    public bool ApiKeyConfigured { get; set; }

    [JsonPropertyName("relay_request_id")]
    public string? RelayRequestId { get; set; }

    [JsonPropertyName("apns_topics")]
    public List<string> ApnsTopics { get; set; } = [];

    [JsonPropertyName("expires_at")]
    public string ExpiresAt { get; set; } = "";
}
