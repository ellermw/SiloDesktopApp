using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public partial class SettingsApi
{
    private VideoPlaybackRatePreferences? _videoPlaybackRate;
    public VideoPlaybackRatePreferences VideoPlaybackRate => _videoPlaybackRate ??= new(this);
    internal string? CaptureDeviceId() => client.DeviceId;
}
