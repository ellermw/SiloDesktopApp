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

        // A progress keepalive failure can mean the server already reaped the
        // session. Reopening that session's URL only repeats the 404; it must
        // mint a replacement session instead. Local byte-stream stalls and
        // premature EOF remain safe same-session range reloads.
        return reason is "buffering-stalled" or "position-stalled" or "end-file" or "eof-reached" or "direct-transport-error";
    }
}
