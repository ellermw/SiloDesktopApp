using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

// Coordinated source fallback (official fallbackSource of useWatchTogetherRoomConnection): this member's
// player was refused the room's selected file for a reason another version could solve, so the room is
// asked once to replace the shared source. The rules live in WatchPartySourceFallback.
public partial class WatchTogetherRoomViewModel
{
    private static readonly TimeSpan SourceFallbackTimeout = TimeSpan.FromSeconds(30);
    private readonly WatchPartySourceFallbackGate _sourceFallbackGate = new();
    // Counts socket snapshots so an HTTP answer that overlaps one cannot overwrite the member's attachment.
    private int _socketSnapshotVersion;

    /// <summary>
    /// Claims and starts the fallback for a refused playback start, or returns null when none may be sent
    /// (not the room's selected file, not a fallback reason, not connected, or the key was already used).
    /// The task never throws.
    /// </summary>
    internal Task<SourceFallbackOutcome>? BeginSourceFallback(int failedFileId, string reason, string? playbackAttemptId)
    {
        if (_disposed || ClosedReason != null) return null;
        var plan = WatchPartySourceFallback.Plan(new(RoomId, RoomToken, Room, ConnectionState, failedFileId, reason, playbackAttemptId));
        if (plan == null || !_sourceFallbackGate.TryClaim(plan.Key)) return null;
        return RunSourceFallbackAsync(plan, RoomToken!);
    }

    private async Task<SourceFallbackOutcome> RunSourceFallbackAsync(WatchPartySourceFallbackPlan plan, string roomToken)
    {
        var context = _apiClient.CaptureContext();
        bool Owns() => !_disposed && ClosedReason == null && RoomId == plan.RoomId && RoomToken == roomToken
            && _apiClient.IsCurrentContext(context);
        using var timeout = new CancellationTokenSource(SourceFallbackTimeout);
        try
        {
            var capability = await _playbackApi.GetConnectionCapabilityAsync(_playbackApi.CaptureConnectionOwner(), timeout.Token);
            if (!Owns()) return SourceFallbackOutcome.Abandoned;
            if (!WatchPartySourceFallback.Supports(capability.Features)) return SourceFallbackOutcome.NotSent;
            var socketVersion = _socketSnapshotVersion;
            var response = await _playbackApi.FallbackWatchTogetherSourceAsync(context, plan.RoomId, roomToken,
                plan.SelectionRevision, plan.FailedFileId, plan.Reason, timeout.Token);
            var outcome = SourceFallbackOutcome.Abandoned;
            await RunOnUiThreadAsync(() =>
            {
                if (!Owns()) return;
                var current = Room;
                outcome = WatchPartySourceFallback.ClassifyReply(current, response.Room, plan);
                if (outcome != SourceFallbackOutcome.Moved &&
                    WatchPartySourceFallback.ShouldAssignReply(current, response.Room, _socketSnapshotVersion != socketVersion))
                    Room = response.Room;
            });
            return outcome;
        }
        catch (Exception)
        {
            // The original playback refusal stays; the same key is never sent again.
            return Owns() ? SourceFallbackOutcome.Failed : SourceFallbackOutcome.Abandoned;
        }
    }
}
