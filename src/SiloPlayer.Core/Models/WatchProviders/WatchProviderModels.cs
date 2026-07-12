using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Models.WatchProviders;

public static class WatchProviderAuthMethod
{
    public const string DeviceCode = "device_code";
    public const string ApiKey = "api_key";
}

public sealed class WatchProvidersResponse
{
    public List<WatchProviderSummary> Providers { get; set; } = [];
}

public sealed class WatchProviderSummary
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public WatchProviderCapabilities Capabilities { get; set; } = new();
}

public sealed class WatchProviderCapabilities
{
    public bool ImportWatched { get; set; }
    public bool ImportProgress { get; set; }
    public bool ExportWatched { get; set; }
    public bool ExportUnwatched { get; set; }
    public bool ImportFavorites { get; set; }
    public bool ExportFavorites { get; set; }
    public bool RemoveFavorites { get; set; }
    public bool ScrobblePlayback { get; set; }
}

public sealed class WatchProviderConnection
{
    public string Provider { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public WatchProviderCapabilities Capabilities { get; set; } = new();
    public string AuthMethod { get; set; } = WatchProviderAuthMethod.DeviceCode;
    public bool Connected { get; set; }
    public string? ProviderUsername { get; set; }
    public bool ImportWatchedEnabled { get; set; }
    public bool ImportProgressEnabled { get; set; }
    public bool ExportWatchedEnabled { get; set; }
    public bool ExportUnwatchedEnabled { get; set; }
    public bool ImportFavoritesEnabled { get; set; }
    public bool ExportFavoritesEnabled { get; set; }
    public bool SyncFavoriteRemovalsEnabled { get; set; }
    public bool ScrobbleEnabled { get; set; }
    public bool CredentialsConfigured { get; set; }
    public string? LastInboundSyncAt { get; set; }
    public string? LastProgressSyncAt { get; set; }
    public string? LastOutboundSyncAt { get; set; }
    public string? LastFavoritesSyncAt { get; set; }
    public string? LastScrobbleErrorAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class WatchProviderDeviceAuthSession
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = "";
    public string UserCode { get; set; } = "";
    public string VerificationUrl { get; set; } = "";
    public int IntervalSeconds { get; set; }
    public string ExpiresAt { get; set; } = "";

    [JsonPropertyName("ID")]
    public string? LegacyId { get; set; }

    [JsonPropertyName("Provider")]
    public string? LegacyProvider { get; set; }

    [JsonPropertyName("UserCode")]
    public string? LegacyUserCode { get; set; }

    [JsonPropertyName("VerificationURL")]
    public string? LegacyVerificationUrl { get; set; }

    [JsonPropertyName("IntervalSeconds")]
    public int? LegacyIntervalSeconds { get; set; }

    [JsonPropertyName("ExpiresAt")]
    public string? LegacyExpiresAt { get; set; }

    public WatchProviderDeviceAuthSession Normalize(string provider)
    {
        Id = string.IsNullOrWhiteSpace(Id) ? LegacyId ?? "" : Id;
        Provider = string.IsNullOrWhiteSpace(Provider) ? LegacyProvider ?? provider : Provider;
        UserCode = string.IsNullOrWhiteSpace(UserCode) ? LegacyUserCode ?? "" : UserCode;
        VerificationUrl = string.IsNullOrWhiteSpace(VerificationUrl) ? LegacyVerificationUrl ?? "" : VerificationUrl;
        IntervalSeconds = IntervalSeconds > 0 ? IntervalSeconds : LegacyIntervalSeconds ?? 0;
        ExpiresAt = string.IsNullOrWhiteSpace(ExpiresAt) ? LegacyExpiresAt ?? "" : ExpiresAt;
        return this;
    }
}

public sealed class WatchProviderSyncRunsResponse
{
    public List<WatchProviderSyncRun> Runs { get; set; } = [];
}

public sealed class WatchProviderSyncRun
{
    public string Id { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public string Trigger { get; set; } = "";
    public string Status { get; set; } = "";
    public string Provider { get; set; } = "";
    public int InboundWatchedFound { get; set; }
    public int InboundWatchedImported { get; set; }
    public int InboundProgressFound { get; set; }
    public int InboundProgressImported { get; set; }
    public int OutboundFound { get; set; }
    public int OutboundSent { get; set; }
    public int InboundFavoritesFound { get; set; }
    public int InboundFavoritesImported { get; set; }
    public int OutboundFavoritesFound { get; set; }
    public int OutboundFavoritesSent { get; set; }
    public int FavoriteRemovalsSent { get; set; }
    public string? Warning { get; set; }
    public string? Error { get; set; }
    public string StartedAt { get; set; } = "";
    public string? CompletedAt { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class WatchProviderManualSyncResponse
{
    public WatchProviderSyncRun Run { get; set; } = new();
    public int RetryAfterSeconds { get; set; }
}
