namespace SiloPlayer.Core.Services;

public sealed record MpvNetworkBufferProfile(
    int MaxMiB,
    int BackMiB,
    int ReadAheadSeconds,
    int StreamMiB);

/// <summary>
/// Sizes mpv's progressive-download cache for the source bitrate. The default
/// remains lightweight; high-bitrate UHD remuxes receive enough headroom to
/// absorb ordinary CDN/network jitter without delaying initial playback.
/// </summary>
public static class MpvNetworkBufferSizing
{
    public static MpvNetworkBufferProfile Default { get; } = new(256, 64, 30, 1);

    public static MpvNetworkBufferProfile ForBitrateKbps(int bitrateKbps)
    {
        if (bitrateKbps >= 80_000)
            return new(512, 128, 45, 4);
        if (bitrateKbps >= 30_000)
            return new(384, 96, 40, 2);
        return Default;
    }
}
