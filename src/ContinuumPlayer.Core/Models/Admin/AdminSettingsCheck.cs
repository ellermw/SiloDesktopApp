using System.Text.Json.Serialization;

namespace ContinuumPlayer.Core.Models.Admin;

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
