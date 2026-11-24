using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public sealed partial class WatchTogetherCoordinator
{
    /// <summary>
    /// A refused playback start for the active room: starts coordinated source fallback when the official
    /// rules allow it. Returns null when nothing may be sent, so the original refusal shows at once.
    /// </summary>
    private Task<SourceFallbackOutcome>? BeginRoomSourceFallback(RoomPlaybackRefusal refusal)
    {
        if (!EnsureCurrentAuthority() || _activeRoom is not { } room) return null;
        return room.BeginSourceFallback(refusal.FileId, refusal.Reason, refusal.PlaybackAttemptId);
    }
}
