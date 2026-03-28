using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Models.Catalog;

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

public class SettingsApi(ContinuumApiClient client)
{
    public Task<SettingEntry> GetSettingAsync(string key, CancellationToken ct = default)
        => client.GetAsync<SettingEntry>($"/api/v1/settings/{Uri.EscapeDataString(key)}", ct);

    public Task PutSettingAsync(string key, string value, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/settings/{Uri.EscapeDataString(key)}", new { value }, ct);

    public Task<Profile> UpdateProfileAsync(string profileId, object updates, CancellationToken ct = default)
        => client.PatchAsync<Profile>($"/api/v1/profiles/{profileId}", updates, ct);

    public Task<ProfilesResponse> GetProfilesAsync(CancellationToken ct = default)
        => client.GetAsync<ProfilesResponse>("/api/v1/profiles", ct);

    // ===== Library Playback Preferences =====

    public Task<LibraryPlaybackPrefsResponse> GetLibraryPlaybackPrefsAsync(CancellationToken ct = default)
        => client.GetAsync<LibraryPlaybackPrefsResponse>("/api/v1/library-playback-prefs", ct);

    public Task SetLibraryPlaybackPrefsAsync(int libraryId, object prefs, CancellationToken ct = default)
        => client.PutNoContentAsync($"/api/v1/library-playback-prefs/{libraryId}", prefs, ct);

    public Task DeleteLibraryPlaybackPrefsAsync(int libraryId, CancellationToken ct = default)
        => client.DeleteAsync($"/api/v1/library-playback-prefs/{libraryId}", ct);
}
