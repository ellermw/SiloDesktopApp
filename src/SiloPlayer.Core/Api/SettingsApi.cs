using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Models.Settings;
using SiloPlayer.Core.Services;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace SiloPlayer.Core.Api;

public sealed class ServerBrandingResponse
{
    [JsonPropertyName("server_name")] public string? ServerName { get; set; }
    [JsonPropertyName("login_subtitle")] public string? LoginSubtitle { get; set; }
    [JsonPropertyName("accent_color")] public string? AccentColor { get; set; }
    [JsonPropertyName("default_theme")] public string? DefaultTheme { get; set; }
    [JsonPropertyName("wordmark_url")] public string? WordmarkUrl { get; set; }
    [JsonPropertyName("wordmark_light_url")] public string? WordmarkLightUrl { get; set; }
    [JsonPropertyName("mark_url")] public string? MarkUrl { get; set; }
    [JsonPropertyName("mark_light_url")] public string? MarkLightUrl { get; set; }
    [JsonPropertyName("favicon_url")] public string? FaviconUrl { get; set; }
    [JsonPropertyName("login_bg_url")] public string? LoginBackgroundUrl { get; set; }
}

public sealed class ServerIdentity { public string ServerId { get; set; } = ""; }

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

public sealed class SigningSecretResponse
{
    [JsonPropertyName("signing_secret")] public string SigningSecret { get; set; } = "";
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
    [JsonPropertyName("library_id"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] public int? LibraryId { get; set; }
    [JsonPropertyName("series_id")] public string? SeriesId { get; set; }
}

public sealed class SettingsContractCapabilities
{
    [JsonPropertyName("api_version")] public int ApiVersion { get; set; }
    [JsonPropertyName("manifest_revision")] public int Revision { get; set; }
    [JsonPropertyName("contract_etag")] public string ContractEtag { get; set; } = "";
    [JsonPropertyName("definition_count")] public int DefinitionCount { get; set; }
    [JsonPropertyName("scopes")] public List<string> Scopes { get; set; } = [];
    [JsonPropertyName("client_families")] public List<string> ClientFamilies { get; set; } = [];
    [JsonPropertyName("supports_batched_effective")] public bool SupportsBatchedEffective { get; set; }
    [JsonPropertyName("supports_idempotent_writes")] public bool SupportsIdempotentWrites { get; set; }
    [JsonPropertyName("supports_atomic_shortcuts")] public bool SupportsAtomicShortcuts { get; set; }
    [JsonPropertyName("supports_device_rename")] public bool SupportsDeviceRename { get; set; }

    public bool SupportsRevision5Customization =>
        ApiVersion == 1 && Revision >= 5 &&
        SupportsBatchedEffective && SupportsIdempotentWrites &&
        ClientFamilies.Contains(SiloApiClient.ClientFamily, StringComparer.OrdinalIgnoreCase);
}

public sealed class ContractEffectiveSettingsResponse
{
    [JsonPropertyName("items")] public List<ContractEffectiveSettingEntry> Settings { get; set; } = [];
    [JsonPropertyName("revision")] public long Revision { get; set; }
}

public sealed class UserDevice
{
    [JsonPropertyName("device_id")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("device_name")] public string DeviceName { get; set; } = "";
    [JsonPropertyName("custom_name")] public string? CustomName { get; set; }
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(CustomName)
        ? string.IsNullOrWhiteSpace(DeviceName) ? "Unknown device" : DeviceName : CustomName;
    [JsonPropertyName("device_platform")] public string DevicePlatform { get; set; } = "";
    [JsonPropertyName("last_seen_at")] public string LastSeenAt { get; set; } = "";
    [JsonPropertyName("profile_id")] public string ProfileId { get; set; } = "";
    [JsonPropertyName("profile_name")] public string ProfileName { get; set; } = "";
    [JsonPropertyName("is_current_device")] public bool IsCurrentDevice { get; set; }
    [JsonPropertyName("changed_count")] public int ChangedCount { get; set; }
}

public sealed class UserDeviceListResponse
{
    [JsonPropertyName("items")] public List<UserDevice> Devices { get; set; } = [];
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

public class SettingsApi(SiloApiClient client)
{
    private SubtitlePreferences? _subtitles;
    public SubtitlePreferences Subtitles => _subtitles ??= new(this);
    private readonly SemaphoreSlim _sectionWrites = new(1, 1);
    private readonly AsyncLocal<bool> _ownsSectionWrite = new();
    public async Task RunSectionMutationAsync(Func<Task> action, CancellationToken ct = default)
    {
        if (_ownsSectionWrite.Value) { await action(); return; }
        var context = client.CaptureContext();
        await _sectionWrites.WaitAsync(ct);
        try
        {
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("The selected profile changed.", ct);
            _ownsSectionWrite.Value = true;
            await action();
        }
        finally { _ownsSectionWrite.Value = false; _sectionWrites.Release(); }
    }
    public event Action<string>? LocalProfileSettingChanged;
    public void NotifyLocalizedSettingChanged(string key)
    {
        client.LocalizedReads.NotifySettingChanged(key);
        LocalProfileSettingChanged?.Invoke(key);
    }

    internal ApiRequestContext CaptureAppearanceContext() => client.CaptureContext();
    internal bool IsAppearanceContextCurrent(ApiRequestContext context) => client.IsCurrentContext(context);


    private readonly SemaphoreSlim _onboardingGuard = new(1, 1);
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<SettingsContractCapabilities> GetContractCapabilitiesAsync(CancellationToken ct = default)
        => client.GetAsync<SettingsContractCapabilities>("/api/v2/settings/contract/capabilities", ct);

    public Task<SettingsContractCapabilities> GetContractCapabilitiesAsync(ApiRequestContext context, CancellationToken ct = default)
        => client.SendRequestAsync<SettingsContractCapabilities>(context, HttpMethod.Get, "/api/v2/settings/contract/capabilities", null, ct);

    public Task<ServerBrandingResponse> GetServerBrandingAsync(CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<ServerBrandingResponse>("/api/v2/theme/branding", ct);

    public async Task<SettingEntry> GetSettingAsync(string key, CancellationToken ct = default)
    {
        var value = await client.GetAsync<ContractEffectiveSettingEntry>(BuildContractSettingPath(SettingsV2Values.CanonicalKey(key), "profile", null, null), ct);
        return new SettingEntry { Key = key, Value = SettingsV2Values.Display(value.Value) };
    }

    public Task PutSettingAsync(string key, string value, CancellationToken ct = default)
        => SetContractSettingValueAsync(SettingsV2Values.CanonicalKey(key), "profile", SettingsV2Values.Parse(key, value), ct: ct);

    public Task DeleteSettingAsync(string key, CancellationToken ct = default)
        => DeleteContractSettingValueAsync(SettingsV2Values.CanonicalKey(key), "profile", ct: ct);

    public async Task<EffectiveSettingsResponse> GetEffectiveSettingsAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        var requested = keys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct().ToArray();
        var response = await GetContractEffectiveSettingsAsync(requested.Select(SettingsV2Values.CanonicalKey), ct: ct);
        return new EffectiveSettingsResponse
        {
            Settings = requested.Select(key => (Key: key, Entry: response.Settings.FirstOrDefault(item => item.Key == SettingsV2Values.CanonicalKey(key))))
                .Where(pair => pair.Entry != null)
                .Select(pair => new EffectiveSettingEntry
                {
                    Key = pair.Key,
                    EffectiveValue = SettingsV2Values.Display(pair.Entry!.Value),
                    Source = pair.Entry.Source,
                    HasDeviceOverride = pair.Entry.Source == "profile_device",
                    DeviceId = pair.Entry.DeviceId,
                }).ToList(),
        };
    }

    public async Task SaveProfileDefaultAsync(ApiRequestContext context, string key, object? value,
        bool hasDeviceOverride, CancellationToken ct = default)
    {
        await SetContractSettingValueAsync(context, key, "profile", value, ct: ct);
        if (hasDeviceOverride) await ClearMissingSettingAsync(context, key, "profile_device", ct);
    }

    public async Task ResetProfileDefaultAsync(ApiRequestContext context, string key, CancellationToken ct = default)
    {
        await ClearMissingSettingAsync(context, key, "profile", ct);
        await ClearMissingSettingAsync(context, key, "profile_device", ct);
    }

    private async Task ClearMissingSettingAsync(ApiRequestContext context, string key, string scope, CancellationToken ct)
    {
        try { await DeleteContractSettingValueAsync(context, key, scope, ct: ct); }
        catch (ApiException ex) when (ex.StatusCode == 404) { }
    }

    public Task PutDeviceSettingAsync(string key, string value, CancellationToken ct = default)
        => SetContractSettingValueAsync(SettingsV2Values.CanonicalKey(key), "profile_device", SettingsV2Values.Parse(key, value), ct: ct);

    public Task DeleteDeviceSettingAsync(string key, CancellationToken ct = default)
        => DeleteContractSettingValueAsync(SettingsV2Values.CanonicalKey(key), "profile_device", ct: ct);

    public Task<ContractEffectiveSettingsResponse> GetContractEffectiveSettingsAsync(
        IEnumerable<string> keys,
        string? deviceId = null,
        string? profileId = null,
        CancellationToken ct = default)
        => GetContractEffectiveSettingsAsync(client.CaptureContext(), keys, deviceId, profileId, ct);

    public Task<ContractEffectiveSettingsResponse> GetContractEffectiveSettingsAsync(
        ApiRequestContext context, IEnumerable<string> keys, string? deviceId = null,
        string? profileId = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        query.AddRange(keys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct().Select(key => $"keys={Uri.EscapeDataString(key)}"));
        if (!string.IsNullOrWhiteSpace(deviceId))
            query.Add($"device_id={Uri.EscapeDataString(deviceId)}");
        if (!string.IsNullOrWhiteSpace(profileId))
            query.Add($"profile_id={Uri.EscapeDataString(profileId)}");
        var suffix = query.Count == 0 ? "" : "?" + string.Join("&", query);
        return client.SendRequestAsync<ContractEffectiveSettingsResponse>(context, HttpMethod.Get,
            $"/api/v2/settings/values/effective{suffix}", null, ct);
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

    public Task SetContractSettingValueAsync(ApiRequestContext context, string key, string scope,
        object? value, string? deviceId = null, string? profileId = null, CancellationToken ct = default)
        => client.SendRequestAsync<ContractEffectiveSettingEntry>(context, HttpMethod.Put,
            BuildContractSettingPath(key, scope, deviceId, profileId ?? context.ProfileId),
            new Dictionary<string, object?> { ["value"] = value }, ct);

    public Task DeleteContractSettingValueAsync(ApiRequestContext context, string key, string scope,
        string? deviceId = null, string? profileId = null, CancellationToken ct = default)
        => client.SendNoContentRequestAsync(context, HttpMethod.Delete,
            BuildContractSettingPath(key, scope, deviceId, profileId ?? context.ProfileId), null, ct);

    public Task DeleteContractSettingValueAsync(
        string key,
        string scope,
        string? deviceId = null,
        string? profileId = null,
        CancellationToken ct = default)
        => client.DeleteAsync(BuildContractSettingPath(key, scope, deviceId, profileId), ct);

    public Task<ContractEffectiveSettingEntry> GetContractStoredSettingAsync(string key, string scope,
        string? deviceId = null, string? profileId = null, CancellationToken ct = default)
        => client.GetAsync<ContractEffectiveSettingEntry>(BuildContractSettingPath(key, scope, deviceId, profileId), ct);

    public async Task<UserDeviceListResponse> GetUserDevicesAsync(bool household = false, CancellationToken ct = default)
        => new() { Devices = await client.GetAllItemsAsync<UserDevice>(
            household ? "/api/v2/devices?scope=household&limit=100" : "/api/v2/devices?limit=100", ct) };

    public Task<UserDevice> RenameUserDeviceAsync(ApiRequestContext context, string deviceId,
        string? customName, string? profileId = null, CancellationToken ct = default)
    {
        if (customName != null && (string.IsNullOrWhiteSpace(customName) || customName.EnumerateRunes().Count() > 120
            || customName.Any(char.IsControl)))
            throw new ArgumentException("Use a visible device name of at most 120 characters, without control characters.", nameof(customName));
        return client.SendRequestAsync<UserDevice>(context, HttpMethod.Patch,
            BuildDevicePath(deviceId, "", profileId ?? context.ProfileId),
            new Dictionary<string, object?> { ["custom_name"] = customName?.Trim() }, ct);
    }

    public Task ClearUserDeviceSettingsAsync(string deviceId, string? profileId = null, CancellationToken ct = default)
        => client.DeleteAsync(BuildDevicePath(deviceId, "/settings", profileId), ct);

    public Task ForgetUserDeviceAsync(string deviceId, string? profileId = null, CancellationToken ct = default)
        => client.DeleteAsync(BuildDevicePath(deviceId, "", profileId), ct);

    public Task<CompatConnectInfo> GetCompatConnectInfoAsync(CancellationToken ct = default)
        => client.GetAsync<CompatConnectInfo>("/api/v2/compat/connect-info", ct);

    private static string BuildContractSettingPath(string key, string scope, string? deviceId, string? profileId)
    {
        var query = new List<string> { $"scope={Uri.EscapeDataString(scope)}" };
        if (!string.IsNullOrWhiteSpace(deviceId))
            query.Add($"device_id={Uri.EscapeDataString(deviceId)}");
        if (!string.IsNullOrWhiteSpace(profileId))
            query.Add($"profile_id={Uri.EscapeDataString(profileId)}");
        return $"/api/v2/settings/values/{Uri.EscapeDataString(key)}?{string.Join("&", query)}";
    }

    private static string BuildDevicePath(string deviceId, string suffix, string? profileId)
    {
        var path = $"/api/v2/devices/{Uri.EscapeDataString(deviceId)}{suffix}";
        return string.IsNullOrWhiteSpace(profileId)
            ? path
            : $"{path}?profile_id={Uri.EscapeDataString(profileId)}";
    }

    /// <summary>
    /// Fetches the admin's overlay config (kill switch + default prefs).
    /// Server: <c>GET /api/v2/settings/overlay-config</c>.
    /// </summary>
    public Task<OverlayConfigResponse> GetOverlayConfigAsync(CancellationToken ct = default)
        => client.GetAsync<OverlayConfigResponse>("/api/v2/settings/overlay-config", ct);

    public Task<Profile> UpdateProfileAsync(string profileId, object updates, CancellationToken ct = default)
    {
        var body = JsonSerializer.SerializeToNode(updates, WireOptions)!.AsObject();
        foreach (var key in new[] { "pin", "avatar", "max_playback_quality" })
            if (body[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length == 0)
                body[key] = null;
        if (body["allowed_library_ids"] is JsonArray ids)
            body["allowed_library_ids"] = new JsonArray(ids.Select(id => JsonValue.Create(id!.ToString())).ToArray<JsonNode?>());
        return client.WithResolvedAvatar(() => client.PatchAsync<Profile>($"/api/v2/profiles/{Uri.EscapeDataString(profileId)}", JsonSerializer.SerializeToElement(body), ct));
    }

    public Task<ProfilesResponse> GetProfilesAsync(CancellationToken ct = default)
        => client.WithResolvedAvatars(() => client.GetAsync<ProfilesResponse>("/api/v2/profiles", ct));

    public Task<OnboardingFlow> GetOnboardingFlowAsync(string surface = "web", CancellationToken ct = default)
        => client.GetAsync<OnboardingFlow>(
            $"/api/v2/onboarding/flow?surface={Uri.EscapeDataString(surface)}", ct);

    public async Task ReportOnboardingProgressAsync(
        string tourId,
        string? lastStep = null,
        bool? completed = null,
        bool? skipped = null,
        CancellationToken ct = default)
    {
        await _onboardingGuard.WaitAsync(ct);
        try
        {
            var context = client.CaptureContext();
            var state = await client.GetWithETagAsync<JsonElement>("/api/v2/onboarding/state", ct);
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("The selected profile changed.", ct);
            var tag = state.ETag ?? throw new InvalidDataException("The onboarding state did not include a revision.");
            var body = new Dictionary<string, object?> { ["tour_id"] = tourId };
            if (lastStep != null) body["last_step"] = lastStep;
            if (completed.HasValue) body["completed"] = completed.Value;
            if (skipped.HasValue) body["skipped"] = skipped.Value;
            await client.PutWithETagAsync<JsonElement>("/api/v2/onboarding/progress", body, tag, ct);
        }
        finally { _onboardingGuard.Release(); }
    }

    // ===== Library Playback Preferences =====

    public Task<LibraryPlaybackPrefsResponse> GetLibraryPlaybackPrefsAsync(CancellationToken ct = default)
        => client.GetAsync<LibraryPlaybackPrefsResponse>("/api/v2/library-playback-prefs", ct);

    public Task SetLibraryPlaybackPrefsAsync(int libraryId, object prefs, CancellationToken ct = default)
        => client.SendNoContentRequestAsync(HttpMethod.Patch, $"/api/v2/library-playback-prefs/{libraryId}", prefs, null, ct);

    public Task DeleteLibraryPlaybackPrefsAsync(int libraryId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v2/library-playback-prefs/{libraryId}", ct);

    // ===== Plugin Settings =====

    public Task<PluginSettingsListResponse> GetPluginSettingsListAsync(CancellationToken ct = default)
        => client.GetAsync<PluginSettingsListResponse>("/api/v2/settings/plugins", ct);

    public Task<PluginSettingsDetailResponse> GetPluginSettingsAsync(int installationId, CancellationToken ct = default)
        => client.GetAsync<PluginSettingsDetailResponse>($"/api/v2/settings/plugins/{installationId}", ct);

    public Task UpdatePluginSettingsAsync(int installationId, UpdatePluginSettingsRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v2/settings/plugins/{installationId}", request, ct);

    // ===== Profile Sections =====
    public ApiRequestContext CaptureContext() => client.CaptureContext();
    public bool IsCurrentContext(ApiRequestContext context) => client.IsCurrentContext(context);
    public Task<ServerIdentity> GetServerIdentityAsync(CancellationToken ct = default)
        => client.GetAsync<ServerIdentity>("/api/v2/system/identity", ct);

    public Task<ProfileSectionOverridesResponse> GetProfileSectionsAsync(string scope = "home", string? libraryId = null, CancellationToken ct = default)
        => client.GetAsync<ProfileSectionOverridesResponse>($"/api/v2/profile/sections{SectionQuery(scope, libraryId)}", ct);

    public Task UpdateProfileSectionsAsync(SaveOverridesRequest request, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        var body = new Dictionary<string, object?> { ["overrides"] = JsonSerializer.SerializeToElement(request.Overrides, WireOptions) };
        return RunSectionMutationAsync(() => client.SendNoContentRequestAsync(context, HttpMethod.Put,
            $"/api/v2/profile/sections{SectionQuery(request.Scope, request.LibraryId)}", body, ct), ct);
    }

    public Task ResetProfileSectionsAsync(string scope = "home", string? libraryId = null, CancellationToken ct = default)
    {
        var context = client.CaptureContext();
        return RunSectionMutationAsync(() => client.SendNoContentRequestAsync(context, HttpMethod.Delete,
            $"/api/v2/profile/sections{SectionQuery(scope, libraryId)}", null, ct), ct);
    }

    public Task<SettingsSectionsResponse> GetProfileSectionSettingsAsync(string scope = "home", string? libraryId = null, CancellationToken ct = default)
        => client.GetAsync<SettingsSectionsResponse>($"/api/v2/profile/sections/settings{SectionQuery(scope, libraryId)}", ct);

    public async Task<RecipeCatalogResponse> GetRecipeCatalogAsync(CancellationToken ct = default)
    {
        var response = await client.GetAsync<RecipeCategories>("/api/v2/sections/recipes", ct);
        return new RecipeCatalogResponse { Categories = response.Categories.ToDictionary(category => category.Category, category => category.Recipes) };
    }

    private sealed class RecipeCategories { public List<RecipeCategory> Categories { get; set; } = []; }
    private sealed class RecipeCategory
    {
        public string Category { get; set; } = "";
        public List<RecipeDefinition> Recipes { get; set; } = [];
    }

    private static string SectionQuery(string scope, string? libraryId)
    {
        var query = $"?scope={Uri.EscapeDataString(scope)}";
        if (!string.IsNullOrWhiteSpace(libraryId)) query += $"&library_id={Uri.EscapeDataString(libraryId)}";
        return query;
    }
}
