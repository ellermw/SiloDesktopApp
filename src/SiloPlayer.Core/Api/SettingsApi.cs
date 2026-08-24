using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Models.Settings;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiloPlayer.Core.Api;

public sealed class ServerBrandingResponse
{
    [JsonPropertyName("server_name")] public string? ServerName { get; set; }
    [JsonPropertyName("login_subtitle")] public string? LoginSubtitle { get; set; }
    [JsonPropertyName("accent_color")] public string? AccentColor { get; set; }
    [JsonPropertyName("default_theme")] public string? DefaultTheme { get; set; }
    [JsonPropertyName("wordmark_url")] public string? WordmarkUrl { get; set; }
    [JsonPropertyName("mark_url")] public string? MarkUrl { get; set; }
    [JsonPropertyName("favicon_url")] public string? FaviconUrl { get; set; }
    [JsonPropertyName("login_bg_url")] public string? LoginBackgroundUrl { get; set; }
}

public sealed class BrandingAssetUploadResponse
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

public sealed class CatalogSearchStatus
{
    [JsonPropertyName("configured_provider")] public string ConfiguredProvider { get; set; } = "";
    [JsonPropertyName("active_provider")] public string ActiveProvider { get; set; } = "";
    [JsonPropertyName("meilisearch")] public CatalogSearchMeilisearchStatus Meilisearch { get; set; } = new();
    [JsonPropertyName("index")] public CatalogSearchIndexStatus Index { get; set; } = new();
    [JsonPropertyName("semantic")] public CatalogSearchSemanticStatus? Semantic { get; set; }
    [JsonPropertyName("tasks")] public List<CatalogSearchTaskLink> Tasks { get; set; } = [];
}

public sealed class CatalogSearchMeilisearchStatus
{
    [JsonPropertyName("configured")] public bool Configured { get; set; }
    [JsonPropertyName("healthy")] public bool Healthy { get; set; }
    [JsonPropertyName("circuit_state")] public string CircuitState { get; set; } = "";
    [JsonPropertyName("circuit_reason")] public string? CircuitReason { get; set; }
    [JsonPropertyName("circuit_until")] public string? CircuitUntil { get; set; }
    [JsonPropertyName("last_fallback")] public string? LastFallback { get; set; }
    [JsonPropertyName("timeout_ms")] public int TimeoutMs { get; set; }
    [JsonPropertyName("matching_strategy")] public string MatchingStrategy { get; set; } = "";
    [JsonPropertyName("index_types")] public List<string> IndexTypes { get; set; } = [];
    [JsonPropertyName("semantic_enabled")] public bool SemanticEnabled { get; set; }
    [JsonPropertyName("binary_quantized")] public bool BinaryQuantized { get; set; }
    [JsonPropertyName("semantic_ratio")] public double SemanticRatio { get; set; }
    [JsonPropertyName("embedder")] public string Embedder { get; set; } = "";
}

public sealed class CatalogSearchIndexStatus
{
    [JsonPropertyName("active_index_uid")] public string ActiveIndexUid { get; set; } = "";
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("expected_schema_version")] public int ExpectedSchemaVersion { get; set; }
    [JsonPropertyName("document_count")] public long DocumentCount { get; set; }
    [JsonPropertyName("vector_document_count")] public long VectorDocumentCount { get; set; }
    [JsonPropertyName("pending_events")] public long PendingEvents { get; set; }
    [JsonPropertyName("dead_lettered_events")] public long DeadLetteredEvents { get; set; }
    [JsonPropertyName("last_rebuild_at")] public string? LastRebuildAt { get; set; }
    [JsonPropertyName("last_sync_at")] public string? LastSyncAt { get; set; }
    [JsonPropertyName("last_processed_event_id")] public long LastProcessedEventId { get; set; }
}

public sealed class CatalogSearchSemanticStatus
{
    [JsonPropertyName("ready")] public bool Ready { get; set; }
    [JsonPropertyName("disabled_reason")] public string? DisabledReason { get; set; }
    [JsonPropertyName("vector_coverage_ratio")] public double VectorCoverageRatio { get; set; }
    [JsonPropertyName("coverage_updated_at")] public string? CoverageUpdatedAt { get; set; }
    [JsonPropertyName("per_type")] public List<CatalogSearchTypeCoverage> PerType { get; set; } = [];
    [JsonPropertyName("capability")] public CatalogSearchEmbedderCapability Capability { get; set; } = new();
}

public sealed class CatalogSearchTypeCoverage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("eligible")] public long Eligible { get; set; }
    [JsonPropertyName("vectorized")] public long Vectorized { get; set; }
    [JsonPropertyName("vector_coverage_ratio")] public double VectorCoverageRatio { get; set; }
    [JsonPropertyName("ready")] public bool Ready { get; set; }
}

public sealed class CatalogSearchEmbedderCapability
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("embedder")] public string? Embedder { get; set; }
    [JsonPropertyName("dimensions")] public int? Dimensions { get; set; }
}

public sealed class CatalogSearchTaskLink
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("href")] public string Href { get; set; } = "";
}

public sealed class JellyfinCompatStatus
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("api_state")] public string ApiState { get; set; } = "";
    [JsonPropertyName("listen")] public string Listen { get; set; } = "";
    [JsonPropertyName("public_url")] public string PublicUrl { get; set; } = "";
    [JsonPropertyName("emulated_server_version")] public string EmulatedServerVersion { get; set; } = "";
    [JsonPropertyName("server_name")] public string ServerName { get; set; } = "";
    [JsonPropertyName("web_enabled")] public bool WebEnabled { get; set; }
    [JsonPropertyName("web_state")] public string WebState { get; set; } = "";
    [JsonPropertyName("pinned_version")] public string PinnedVersion { get; set; } = "";
    [JsonPropertyName("installed_version")] public string? InstalledVersion { get; set; }
    [JsonPropertyName("source_url")] public string SourceUrl { get; set; } = "";
    [JsonPropertyName("tag")] public string? Tag { get; set; }
    [JsonPropertyName("commit_sha")] public string? CommitSha { get; set; }
    [JsonPropertyName("checksum")] public string? Checksum { get; set; }
    [JsonPropertyName("install_root")] public string InstallRoot { get; set; } = "";
    [JsonPropertyName("install_path")] public string InstallPath { get; set; } = "";
    [JsonPropertyName("installed_at")] public string? InstalledAt { get; set; }
    [JsonPropertyName("license_present")] public bool LicensePresent { get; set; }
    [JsonPropertyName("provenance_present")] public bool ProvenancePresent { get; set; }
    [JsonPropertyName("installer_ready")] public bool InstallerReady { get; set; }
    [JsonPropertyName("prerequisites")] public List<JellyfinCompatPrerequisite> Prerequisites { get; set; } = [];
    [JsonPropertyName("operation")] public JellyfinCompatOperation? Operation { get; set; }
    [JsonPropertyName("last_error")] public string? LastError { get; set; }
    [JsonPropertyName("restart_required")] public bool RestartRequired { get; set; }
}

public sealed class JellyfinCompatPrerequisite
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("command")] public string Command { get; set; } = "";
    [JsonPropertyName("available")] public bool Available { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public sealed class JellyfinCompatOperation
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("started_at")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("completed_at")] public string? CompletedAt { get; set; }
    [JsonPropertyName("phase")] public string? Phase { get; set; }
    [JsonPropertyName("progress_percent")] public double? ProgressPercent { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public sealed class MarkerProviderListResponse
{
    [JsonPropertyName("providers")] public List<MarkerProviderConfig> Providers { get; set; } = [];
}

public sealed class MarkerProviderConfig
{
    [JsonPropertyName("provider")] public string Provider { get; set; } = "";
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("source_type")] public string? SourceType { get; set; }
    [JsonPropertyName("plugin_id")] public string? PluginId { get; set; }
    [JsonPropertyName("plugin_installation_id")] public long? PluginInstallationId { get; set; }
    [JsonPropertyName("capability_id")] public string? CapabilityId { get; set; }
    [JsonPropertyName("is_submitter")] public bool IsSubmitter { get; set; }
    [JsonPropertyName("fetch_enabled")] public bool FetchEnabled { get; set; }
    [JsonPropertyName("fetch_priority")] public int FetchPriority { get; set; }
    [JsonPropertyName("contribute_enabled")] public bool ContributeEnabled { get; set; }
    [JsonPropertyName("contribute_auto_local")] public bool ContributeAutoLocal { get; set; }
    [JsonPropertyName("contribute_min_confidence")] public double ContributeMinConfidence { get; set; }
}

public sealed class MarkerProviderValidationResponse
{
    [JsonPropertyName("valid")] public bool Valid { get; set; }
    [JsonPropertyName("stats")] public MarkerProviderStats? Stats { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public sealed class MarkerProviderStats
{
    [JsonPropertyName("total")] public long Total { get; set; }
    [JsonPropertyName("accepted")] public long Accepted { get; set; }
    [JsonPropertyName("pending")] public long Pending { get; set; }
    [JsonPropertyName("rejected")] public long Rejected { get; set; }
    [JsonPropertyName("acceptance_rate")] public double AcceptanceRate { get; set; }
    [JsonPropertyName("current_streak")] public long CurrentStreak { get; set; }
    [JsonPropertyName("best_streak")] public long BestStreak { get; set; }
}

public sealed class EmailTestResult
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("duration_ms")] public long DurationMs { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public sealed class ServerNotificationChannelsResponse
{
    [JsonPropertyName("channels")] public List<ServerNotificationChannel> Channels { get; set; } = [];
}

public sealed class ServerNotificationChannel
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("url_host")] public string UrlHost { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("notify_new_movies")] public bool NotifyNewMovies { get; set; }
    [JsonPropertyName("notify_new_episodes")] public bool NotifyNewEpisodes { get; set; }
    [JsonPropertyName("notify_new_audiobooks")] public bool NotifyNewAudiobooks { get; set; }
    [JsonPropertyName("notify_new_ebooks")] public bool NotifyNewEbooks { get; set; }
    [JsonPropertyName("notify_request_submitted")] public bool NotifyRequestSubmitted { get; set; }
    [JsonPropertyName("notify_request_approved")] public bool NotifyRequestApproved { get; set; }
    [JsonPropertyName("notify_request_declined")] public bool NotifyRequestDeclined { get; set; }
    [JsonPropertyName("notify_request_fulfilled")] public bool NotifyRequestFulfilled { get; set; }
    [JsonPropertyName("consecutive_failures")] public int ConsecutiveFailures { get; set; }
    [JsonPropertyName("disabled_reason")] public string? DisabledReason { get; set; }
    [JsonPropertyName("last_success_at")] public string? LastSuccessAt { get; set; }
    [JsonPropertyName("last_failure_at")] public string? LastFailureAt { get; set; }
    [JsonPropertyName("last_failure_status")] public int? LastFailureStatus { get; set; }
    [JsonPropertyName("last_failure_message")] public string? LastFailureMessage { get; set; }
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
    [JsonPropertyName("signing_secret")] public string? SigningSecret { get; set; }
}

public sealed class ServerNotificationChannelTestResult
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("http_status")] public int? HttpStatus { get; set; }
    [JsonPropertyName("duration_ms")] public long DurationMs { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public sealed class SigningSecretResponse
{
    [JsonPropertyName("signing_secret")] public string SigningSecret { get; set; } = "";
}

public class SettingsResponse
{
    public List<SettingEntry> Settings { get; set; } = [];
}

public class SettingEntry
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class EffectiveSettingsResponse
{
    public List<EffectiveSettingEntry> Settings { get; set; } = [];
}

public class EffectiveSettingEntry
{
    public string Key { get; set; } = "";
    public string EffectiveValue { get; set; } = "";
    public string Source { get; set; } = "";
    public bool HasDeviceOverride { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string? DevicePlatform { get; set; }
}

/// <summary>
/// One value resolved through Silo's typed settings contract.  Unlike the
/// legacy settings endpoint, <see cref="Value"/> retains its JSON type.
/// </summary>
public sealed class ContractEffectiveSettingEntry
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("value")] public JsonElement Value { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "default";
    [JsonPropertyName("stored_value")] public JsonElement? StoredValue { get; set; }
    [JsonPropertyName("constrained")] public bool Constrained { get; set; }
    [JsonPropertyName("constraint_kind")] public string? ConstraintKind { get; set; }
    [JsonPropertyName("suggested_values")] public List<string> SuggestedValues { get; set; } = [];
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("device_id")] public string? DeviceId { get; set; }
    [JsonPropertyName("client_family")] public string? ClientFamily { get; set; }
    [JsonPropertyName("library_id")] public int? LibraryId { get; set; }
    [JsonPropertyName("series_id")] public string? SeriesId { get; set; }
}

public sealed class SettingsContractCapabilities
{
    [JsonPropertyName("api_version")] public int ApiVersion { get; set; }
    [JsonPropertyName("revision")] public int Revision { get; set; }
    [JsonPropertyName("contract_etag")] public string ContractEtag { get; set; } = "";
    [JsonPropertyName("definition_count")] public int DefinitionCount { get; set; }
    [JsonPropertyName("scopes")] public List<string> Scopes { get; set; } = [];
    [JsonPropertyName("client_families")] public List<string> ClientFamilies { get; set; } = [];
    [JsonPropertyName("supports_batched_effective")] public bool SupportsBatchedEffective { get; set; }
    [JsonPropertyName("supports_idempotent_writes")] public bool SupportsIdempotentWrites { get; set; }
    [JsonPropertyName("supports_atomic_shortcuts")] public bool SupportsAtomicShortcuts { get; set; }

    public bool SupportsRevision5Customization =>
        ApiVersion == 1 && Revision >= 5 &&
        SupportsBatchedEffective && SupportsIdempotentWrites &&
        ClientFamilies.Contains(SiloApiClient.ClientFamily, StringComparer.OrdinalIgnoreCase);
}

public sealed class ContractEffectiveSettingsResponse
{
    [JsonPropertyName("settings")] public List<ContractEffectiveSettingEntry> Settings { get; set; } = [];
    [JsonPropertyName("revision")] public long Revision { get; set; }
}

public sealed class UserDevice
{
    [JsonPropertyName("device_id")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("device_name")] public string DeviceName { get; set; } = "";
    [JsonPropertyName("device_platform")] public string DevicePlatform { get; set; } = "";
    [JsonPropertyName("last_seen_at")] public string LastSeenAt { get; set; } = "";
    [JsonPropertyName("profile_id")] public string ProfileId { get; set; } = "";
    [JsonPropertyName("profile_name")] public string ProfileName { get; set; } = "";
    [JsonPropertyName("is_current_device")] public bool IsCurrentDevice { get; set; }
    [JsonPropertyName("changed_count")] public int ChangedCount { get; set; }
}

public sealed class UserDeviceListResponse
{
    [JsonPropertyName("devices")] public List<UserDevice> Devices { get; set; } = [];
}

public sealed class CompatConnectInfo
{
    [JsonPropertyName("jellyfin")] public CompatJellyfinConnectInfo Jellyfin { get; set; } = new();
    [JsonPropertyName("account")] public CompatAccountConnectInfo Account { get; set; } = new();
}

public sealed class CompatJellyfinConnectInfo
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("pending_restart")] public bool PendingRestart { get; set; }
    [JsonPropertyName("public_url")] public string PublicUrl { get; set; } = "";
    [JsonPropertyName("server_name")] public string ServerName { get; set; } = "";
}

public sealed class CompatAccountConnectInfo
{
    // Older servers omit this field; the WebUI treats omission as available.
    [JsonPropertyName("password_login_available")] public bool? PasswordLoginAvailable { get; set; }
}

public class OverlayConfigResponse
{
    /// <summary>Server-wide kill switch. When false, no overlays render for any user.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>
    /// Admin default prefs as a JSON string (shape matches
    /// <c>Dictionary&lt;OverlayId, {enabled, position}&gt;</c>). Falls through
    /// to the built-in defaults if the admin hasn't customized.
    /// </summary>
    public string? Defaults { get; set; }
}

public class ThemeCatalogResponse
{
    public int Version { get; set; }
    public List<ThemeCatalogEntry> Themes { get; set; } = [];
}

public class ThemeCatalogEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string PreviewAccent { get; set; } = "#ffffff";
    public string PreviewBg { get; set; } = "#111111";
    public List<string> Tags { get; set; } = [];
    public string DownloadUrl { get; set; } = "";
    public string Version { get; set; } = "";
}

public class ThemeFileResponse
{
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public string BaseTheme { get; set; } = "";
    public Dictionary<string, string> Vars { get; set; } = [];
    public string CustomCss { get; set; } = "";
}

public class SettingsApi(SiloApiClient client)
{
    public Task<SettingsContractCapabilities> GetContractCapabilitiesAsync(CancellationToken ct = default)
        => client.GetAsync<SettingsContractCapabilities>("/api/v1/settings/contract/capabilities", ct);

    public Task<ServerBrandingResponse> GetServerBrandingAsync(CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<ServerBrandingResponse>("/api/v1/theme/branding", ct);

    public Task<BrandingAssetUploadResponse> UploadBrandingAssetAsync(
        string kind, string fileName, byte[] fileBytes, string contentType, CancellationToken ct = default)
        => client.PostMultipartAsync<BrandingAssetUploadResponse>(
            $"/api/v1/admin/branding/assets/{Uri.EscapeDataString(kind)}",
            new Dictionary<string, string?>(), "file", fileName, fileBytes, contentType, ct);

    public Task DeleteBrandingAssetAsync(string kind, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/branding/assets/{Uri.EscapeDataString(kind)}", ct);

    public Task<CatalogSearchStatus> GetCatalogSearchStatusAsync(CancellationToken ct = default)
        => client.GetAsync<CatalogSearchStatus>("/api/v1/admin/catalog/search/status", ct);

    public Task<JellyfinCompatStatus> GetJellyfinCompatStatusAsync(CancellationToken ct = default)
        => client.GetAsync<JellyfinCompatStatus>("/api/v1/admin/jellyfin-compat/status", ct);

    public Task<JellyfinCompatStatus> PatchJellyfinCompatSettingsAsync(object patch, CancellationToken ct = default)
        => client.PatchAsync<JellyfinCompatStatus>("/api/v1/admin/jellyfin-compat/settings", patch, ct);

    public Task<JellyfinCompatStatus> InstallJellyfinCompatWebAsync(string? version = null, CancellationToken ct = default)
        => client.PostAsync<JellyfinCompatStatus>("/api/v1/admin/jellyfin-compat/web/install",
            string.IsNullOrWhiteSpace(version)
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?> { ["version"] = version.Trim() }, ct);

    public Task<JellyfinCompatStatus> RemoveJellyfinCompatWebAsync(CancellationToken ct = default)
        => client.PostAsync<JellyfinCompatStatus>("/api/v1/admin/jellyfin-compat/web/remove",
            new Dictionary<string, object?>(), ct);

    public Task<MarkerProviderListResponse> GetMarkerProvidersAsync(CancellationToken ct = default)
        => client.GetAsync<MarkerProviderListResponse>("/api/v1/admin/markers/providers", ct);

    public Task<MarkerProviderConfig> UpdateMarkerProviderAsync(string provider, object patch,
        CancellationToken ct = default)
        => client.PutAsync<MarkerProviderConfig>(
            $"/api/v1/admin/markers/providers/{Uri.EscapeDataString(provider)}", patch, ct);

    public Task<MarkerProviderValidationResponse> ValidateMarkerProviderAsync(string provider,
        CancellationToken ct = default)
        => client.PostAsync<MarkerProviderValidationResponse>(
            $"/api/v1/admin/markers/providers/{Uri.EscapeDataString(provider)}/validate",
            new Dictionary<string, object?>(), ct);

    public Task<EmailTestResult> SendTestEmailAsync(string recipient, CancellationToken ct = default)
        => client.PostAsync<EmailTestResult>("/api/v1/admin/email/test",
            new Dictionary<string, object?> { ["to"] = recipient.Trim() }, ct);

    public Task<EmailTestResult> TestDiscordBotAsync(CancellationToken ct = default)
        => client.PostAsync<EmailTestResult>("/api/v1/admin/notifications/discord/test",
            new Dictionary<string, object?>(), ct);

    public Task<ServerNotificationChannelsResponse> GetServerNotificationChannelsAsync(CancellationToken ct = default)
        => client.GetAsync<ServerNotificationChannelsResponse>("/api/v1/admin/notifications/server-channels", ct);

    public Task<ServerNotificationChannel> CreateServerNotificationChannelAsync(object input, CancellationToken ct = default)
        => client.PostAsync<ServerNotificationChannel>("/api/v1/admin/notifications/server-channels", input, ct);

    public Task<ServerNotificationChannel> UpdateServerNotificationChannelAsync(string id, object input, CancellationToken ct = default)
        => client.PutAsync<ServerNotificationChannel>(
            $"/api/v1/admin/notifications/server-channels/{Uri.EscapeDataString(id)}", input, ct);

    public Task DeleteServerNotificationChannelAsync(string id, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/admin/notifications/server-channels/{Uri.EscapeDataString(id)}", ct);

    public Task<ServerNotificationChannelTestResult> TestServerNotificationChannelAsync(string id, CancellationToken ct = default)
        => client.PostAsync<ServerNotificationChannelTestResult>(
            $"/api/v1/admin/notifications/server-channels/{Uri.EscapeDataString(id)}/test",
            new Dictionary<string, object?>(), ct);

    public Task<SigningSecretResponse> RotateServerNotificationChannelSecretAsync(string id, CancellationToken ct = default)
        => client.PostAsync<SigningSecretResponse>(
            $"/api/v1/admin/notifications/server-channels/{Uri.EscapeDataString(id)}/rotate-secret",
            new Dictionary<string, object?>(), ct);

    public Task<SettingEntry> GetSettingAsync(string key, CancellationToken ct = default)
        => client.GetAsync<SettingEntry>($"/api/v1/settings/{Uri.EscapeDataString(key)}", ct);

    public Task PutSettingAsync(string key, string value, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/settings/{Uri.EscapeDataString(key)}",
            // Use dictionary body to survive .NET 8 Release trimming (feedback_build_release).
            new Dictionary<string, object?> { ["value"] = value }, ct);

    public Task DeleteSettingAsync(string key, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/settings/{Uri.EscapeDataString(key)}", ct);

    public Task<EffectiveSettingsResponse> GetEffectiveSettingsAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var joinedKeys = string.Join(",", keys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct());
        return client.GetAsync<EffectiveSettingsResponse>(
            $"/api/v1/settings/effective?keys={Uri.EscapeDataString(joinedKeys)}",
            ct);
    }

    public Task PutDeviceSettingAsync(string key, string value, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/settings/device/{Uri.EscapeDataString(key)}",
            new Dictionary<string, object?> { ["value"] = value }, ct);

    public Task DeleteDeviceSettingAsync(string key, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/settings/device/{Uri.EscapeDataString(key)}", ct);

    public Task<ContractEffectiveSettingsResponse> GetContractEffectiveSettingsAsync(
        IEnumerable<string> keys,
        string? deviceId = null,
        string? profileId = null,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        var joinedKeys = string.Join(",", keys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct());
        if (!string.IsNullOrWhiteSpace(joinedKeys))
            query.Add($"keys={Uri.EscapeDataString(joinedKeys)}");
        if (!string.IsNullOrWhiteSpace(deviceId))
            query.Add($"device_id={Uri.EscapeDataString(deviceId)}");
        if (!string.IsNullOrWhiteSpace(profileId))
            query.Add($"profile_id={Uri.EscapeDataString(profileId)}");
        var suffix = query.Count == 0 ? "" : "?" + string.Join("&", query);
        return client.GetAsync<ContractEffectiveSettingsResponse>($"/api/v1/settings/values/effective{suffix}", ct);
    }

    public Task SetContractSettingValueAsync(
        string key,
        string scope,
        object? value,
        string? deviceId = null,
        string? profileId = null,
        CancellationToken ct = default)
        => client.PutAsync<ContractEffectiveSettingEntry>(
            BuildContractSettingPath(key, scope, deviceId, profileId),
            new Dictionary<string, object?> { ["value"] = value }, ct);

    public Task DeleteContractSettingValueAsync(
        string key,
        string scope,
        string? deviceId = null,
        string? profileId = null,
        CancellationToken ct = default)
        => client.DeleteAsync(BuildContractSettingPath(key, scope, deviceId, profileId), ct);

    public Task<UserDeviceListResponse> GetUserDevicesAsync(bool household = false, CancellationToken ct = default)
        => client.GetAsync<UserDeviceListResponse>(
            household ? "/api/v1/devices?scope=household" : "/api/v1/devices", ct);

    public Task ClearUserDeviceSettingsAsync(string deviceId, string? profileId = null, CancellationToken ct = default)
        => client.DeleteAsync(BuildDevicePath(deviceId, "/settings", profileId), ct);

    public Task ForgetUserDeviceAsync(string deviceId, string? profileId = null, CancellationToken ct = default)
        => client.DeleteAsync(BuildDevicePath(deviceId, "", profileId), ct);

    public Task<CompatConnectInfo> GetCompatConnectInfoAsync(CancellationToken ct = default)
        => client.GetAsync<CompatConnectInfo>("/api/v1/compat/connect-info", ct);

    private static string BuildContractSettingPath(string key, string scope, string? deviceId, string? profileId)
    {
        var query = new List<string> { $"scope={Uri.EscapeDataString(scope)}" };
        if (!string.IsNullOrWhiteSpace(deviceId))
            query.Add($"device_id={Uri.EscapeDataString(deviceId)}");
        if (!string.IsNullOrWhiteSpace(profileId))
            query.Add($"profile_id={Uri.EscapeDataString(profileId)}");
        return $"/api/v1/settings/values/{Uri.EscapeDataString(key)}?{string.Join("&", query)}";
    }

    private static string BuildDevicePath(string deviceId, string suffix, string? profileId)
    {
        var path = $"/api/v1/devices/{Uri.EscapeDataString(deviceId)}{suffix}";
        return string.IsNullOrWhiteSpace(profileId)
            ? path
            : $"{path}?profile_id={Uri.EscapeDataString(profileId)}";
    }

    public Task<ThemeCatalogResponse> GetThemeCatalogAsync(CancellationToken ct = default)
        => client.GetAsync<ThemeCatalogResponse>("/api/v1/theme/catalog", ct);

    public Task<ThemeFileResponse> DownloadThemeAsync(string url, CancellationToken ct = default)
        => client.GetAsync<ThemeFileResponse>($"/api/v1/theme/download?url={Uri.EscapeDataString(url)}", ct);

    public Task<ThemeCatalogResponse> RefreshThemeCatalogAsync(CancellationToken ct = default)
        => client.PostAsync<ThemeCatalogResponse>("/api/v1/theme/catalog/refresh", new Dictionary<string, object?>(), ct);

    /// <summary>
    /// Fetches the admin's overlay config (kill switch + default prefs).
    /// Server: <c>GET /api/v1/settings/overlay-config</c>.
    /// </summary>
    public Task<OverlayConfigResponse> GetOverlayConfigAsync(CancellationToken ct = default)
        => client.GetAsync<OverlayConfigResponse>("/api/v1/settings/overlay-config", ct);

    public Task<Profile> UpdateProfileAsync(string profileId, object updates, CancellationToken ct = default)
        => client.PutAsync<Profile>($"/api/v1/profiles/{profileId}", updates, ct);

    public Task<ProfilesResponse> GetProfilesAsync(CancellationToken ct = default)
        => client.GetAsync<ProfilesResponse>("/api/v1/profiles", ct);

    public Task<OnboardingFlow> GetOnboardingFlowAsync(string surface = "web", CancellationToken ct = default)
        => client.GetAsync<OnboardingFlow>(
            $"/api/v1/onboarding/flow?surface={Uri.EscapeDataString(surface)}", ct);

    public Task ReportOnboardingProgressAsync(
        string tourId,
        string? lastStep = null,
        bool? completed = null,
        bool? skipped = null,
        CancellationToken ct = default)
        => client.PostNoContentAsync("/api/v1/onboarding/progress", new Dictionary<string, object?>
        {
            ["tour_id"] = tourId,
            ["last_step"] = lastStep,
            ["completed"] = completed,
            ["skipped"] = skipped,
        }, ct);

    // ===== Library Playback Preferences =====

    public Task<LibraryPlaybackPrefsResponse> GetLibraryPlaybackPrefsAsync(CancellationToken ct = default)
        => client.GetAsync<LibraryPlaybackPrefsResponse>("/api/v1/library-playback-prefs", ct);

    public Task SetLibraryPlaybackPrefsAsync(int libraryId, object prefs, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/library-playback-prefs/{libraryId}", prefs, ct);

    public Task DeleteLibraryPlaybackPrefsAsync(int libraryId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/library-playback-prefs/{libraryId}", ct);

    // ===== Plugin Settings =====

    public Task<PluginSettingsListResponse> GetPluginSettingsListAsync(CancellationToken ct = default)
        => client.GetAsync<PluginSettingsListResponse>("/api/v1/settings/plugins", ct);

    public Task<PluginSettingsDetailResponse> GetPluginSettingsAsync(int installationId, CancellationToken ct = default)
        => client.GetAsync<PluginSettingsDetailResponse>($"/api/v1/settings/plugins/{installationId}", ct);

    public Task UpdatePluginSettingsAsync(int installationId, UpdatePluginSettingsRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/settings/plugins/{installationId}", request, ct);

    // ===== Profile Sections =====

    public Task<ProfileSectionOverridesResponse> GetProfileSectionsAsync(string scope = "home", string? libraryId = null, CancellationToken ct = default)
        => client.GetAsync<ProfileSectionOverridesResponse>($"/api/v1/profile/sections{SectionQuery(scope, libraryId)}", ct);

    public Task UpdateProfileSectionsAsync(SaveOverridesRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync("/api/v1/profile/sections", request, ct);

    public Task ResetProfileSectionsAsync(string scope = "home", string? libraryId = null, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/profile/sections/reset{SectionQuery(scope, libraryId)}", ct);

    public Task<SettingsSectionsResponse> GetProfileSectionSettingsAsync(string scope = "home", string? libraryId = null, CancellationToken ct = default)
        => client.GetAsync<SettingsSectionsResponse>($"/api/v1/profile/sections/settings{SectionQuery(scope, libraryId)}", ct);

    public Task<RecipeCatalogResponse> GetRecipeCatalogAsync(CancellationToken ct = default)
        => client.GetAsync<RecipeCatalogResponse>("/api/v1/sections/recipes", ct);

    private static string SectionQuery(string scope, string? libraryId)
    {
        var query = $"?scope={Uri.EscapeDataString(scope)}";
        if (!string.IsNullOrWhiteSpace(libraryId)) query += $"&library_id={Uri.EscapeDataString(libraryId)}";
        return query;
    }
}
