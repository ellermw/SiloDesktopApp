using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Models.Plugins;

namespace ContinuumPlayer.Core.Api;

public class SettingsResponse
{
    public List<SettingEntry> Settings { get; set; } = [];
}

public class SettingEntry
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
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

public class SettingsApi(ContinuumApiClient client)
{
    public Task<SettingEntry> GetSettingAsync(string key, CancellationToken ct = default)
        => client.GetAsync<SettingEntry>($"/api/v1/settings/{Uri.EscapeDataString(key)}", ct);

    public Task PutSettingAsync(string key, string value, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/settings/{Uri.EscapeDataString(key)}",
            // Use dictionary body to survive .NET 8 Release trimming (feedback_build_release).
            new Dictionary<string, object?> { ["value"] = value }, ct);

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

    public Task<SettingsSectionsResponse> GetProfileSectionsAsync(CancellationToken ct = default)
        => client.GetAsync<SettingsSectionsResponse>("/api/v1/profile/sections", ct);

    public Task UpdateProfileSectionsAsync(SaveOverridesRequest request, CancellationToken ct = default)
        => client.PutNoContentAsync("/api/v1/profile/sections", request, ct);

    public Task ResetProfileSectionsAsync(CancellationToken ct = default)
        => client.DeleteAsync("/api/v1/profile/sections/reset", ct);

    public Task<SettingsSectionsResponse> GetProfileSectionSettingsAsync(CancellationToken ct = default)
        => client.GetAsync<SettingsSectionsResponse>("/api/v1/profile/sections/settings", ct);
}
