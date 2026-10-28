using static SiloPlayer.Player.MpvInterop;

namespace SiloPlayer.Player;

public sealed partial class MpvPlayer
{
    /// <summary>Network bytes for this media reader, independent of API/subtitle requests.</summary>
    public (long? Bytes, double? InputBytesPerSecond, double BufferedSeconds, double? SinceLastByteMs, bool TransportFailing, bool Seeking) ReadMediaNetworkObservation()
    {
        var reader = Volatile.Read(ref _directRequest)?.Reader;
        double? rate = null;
        using var lease = EnterNative();
        // mpv documents this as a network-layer estimate over the last second. Missing is unknown, never zero.
        if (lease.Active && mpv_get_property_int(_mpvHandle, "demuxer-cache-state/raw-input-rate", MPV_FORMAT_INT64, out var bytesPerSecond) == 0)
            rate = Math.Max(0, bytesPerSecond);
        var buffered = lease.Active && mpv_get_property_double(_mpvHandle, "demuxer-cache-duration", MPV_FORMAT_DOUBLE, out var cached) == 0 && double.IsFinite(cached)
            ? Math.Max(0, cached) : double.NaN;
        return (reader?.MediaBytesReceived, rate, buffered,
            reader?.SinceLastMediaByteMs, Volatile.Read(ref _directRequest)?.Failed == true, GetPropertyFlag("seeking"));
    }
}
