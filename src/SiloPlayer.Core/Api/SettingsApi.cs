using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Plugins;
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
    public Task<ServerBrandingResponse> GetServerBrandingAsync(CancellationToken ct = default)
        => client.GetUnauthenticatedAsync<ServerBrandingResponse>("/api/v1/theme/branding", ct);

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
