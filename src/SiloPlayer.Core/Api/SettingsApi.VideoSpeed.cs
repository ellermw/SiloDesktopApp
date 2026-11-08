using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SettingsApi
{
    private VideoPlaybackRatePreferences? _videoPlaybackRate;
    public VideoPlaybackRatePreferences VideoPlaybackRate => _videoPlaybackRate ??= new(this);
    internal SettingsRequestContext? CaptureSettingsContext(SettingsIdentityContext owner) => client.CaptureSettingsContext(owner);
}
