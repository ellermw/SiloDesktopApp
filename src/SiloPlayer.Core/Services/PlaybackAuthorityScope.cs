using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>One playback owner's API dependencies. Browsing keeps its own scope.</summary>
public sealed class PlaybackAuthorityScope
{
    public string ScopeId { get; } = Guid.NewGuid().ToString("N");
    public SiloApiClient ApiClient { get; }
    public AuthService Auth { get; }
    public PlaybackApi PlaybackApi { get; }
    public CatalogApi CatalogApi { get; }
    public SettingsApi SettingsApi { get; }

    public PlaybackAuthorityScope(SiloApiClient apiClient, AuthService auth,
        PlaybackApi? playbackApi = null, CatalogApi? catalogApi = null, SettingsApi? settingsApi = null)
    {
        ApiClient = apiClient;
        Auth = auth;
        PlaybackApi = playbackApi ?? new PlaybackApi(apiClient);
        CatalogApi = catalogApi ?? new CatalogApi(apiClient);
        SettingsApi = settingsApi ?? new SettingsApi(apiClient);
    }
}
