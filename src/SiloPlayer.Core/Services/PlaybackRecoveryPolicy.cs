using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Chooses when a failed native stream can be reopened inside the current
/// server session. Progressive direct play is byte-range seekable, so a cache
/// stall or premature EOF can usually be repaired without creating another
/// playback session. Other transports need server-side preparation and must
/// use the full replacement-session path.
/// </summary>
public static class PlaybackRecoveryPolicy
{
    public static bool CanReloadCurrentDirectSession(
        PlaybackTransportKind transportKind,
        string reason)
    {
        if (transportKind != PlaybackTransportKind.DirectProgressive)
            return false;

        return reason is "buffering-stalled" or "position-stalled" or "end-file" or
            "progress-reporting-failed";
    }
}
