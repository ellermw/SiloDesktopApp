using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.HistoryImport;

/// <summary>What an import run may rely on (<c>GET /api/v2/history-imports/capability</c>). A server without a member reads it as false.</summary>
public sealed class HistoryImportCapability
{
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("allowed")] public bool Allowed { get; set; }
    [JsonPropertyName("revision")] public long Revision { get; set; }
    [JsonPropertyName("plex_connection_fallback")] public bool PlexConnectionFallback { get; set; }
    [JsonPropertyName("max_plex_connections")] public int MaxPlexConnections { get; set; }

    /// <summary>Whether <c>emby_base_url</c> is honored for a direct Emby server sign-in.</summary>
    [JsonPropertyName("emby_custom_server")] public bool EmbyCustomServer { get; set; }
}
