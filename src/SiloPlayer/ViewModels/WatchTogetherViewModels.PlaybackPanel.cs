using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.ViewModels;

public partial class WatchTogetherRoomViewModel
{
    [ObservableProperty] private string? _pendingPlaybackPanelAction;
    private long _playbackPanelMutation;

    public Task StopPlaybackAsync(Func<bool>? ownsRoom = null)
        => RunPlaybackPanelMutationAsync("stop", ownsRoom);

    public Task InvokePlaybackPanelMutationAsync(string action, Func<bool> ownsRoom)
        => RunPlaybackPanelMutationAsync(action, ownsRoom);

    private async Task RunPlaybackPanelMutationAsync(string action, Func<bool>? ownsRoom = null,
        bool allowPendingInitialization = false)
    {
        var snapshot = Room;
        var roomId = RoomId;
        // The room-page Close command can end a join before its first snapshot
        // arrives. Native panel actions still require a confirmed host snapshot.
        var endingInitialization = allowPendingInitialization && action == "end" && snapshot == null
            && _initializationAuthority is { } authority && _apiClient.IsCurrentContext(authority);
        if (action is not ("stop" or "toggle-policy" or "end") || _disposed || IsBusy || ClosedReason != null
            || string.IsNullOrWhiteSpace(roomId) || ownsRoom?.Invoke() == false
            || (!endingInitialization && (snapshot?.SelfCanManageRoom != true || snapshot.RoomId != roomId))) return;
        if (action == "stop" && (snapshot?.Phase != "playing" || ConnectionState != "connected")) return;
        var context = _apiClient.CaptureContext();
        var roomToken = RoomToken;
        // End retires unfinished room initialization, preserving the existing
        // close behavior while its accepted DELETE settles.
        if (action == "end") ++_initializationGeneration;
        var generation = _initializationGeneration;
        var mutation = ++_playbackPanelMutation;
        bool Current(bool allowClosedRoom = false) => !_disposed && generation == _initializationGeneration
            && _apiClient.IsCurrentContext(context) && RoomId == roomId && RoomToken == roomToken
            && (endingInitialization ? Room == null : Room?.RoomId == roomId
                || (allowClosedRoom && Room == null && ClosedReason != null)) && ownsRoom?.Invoke() != false;
        IsBusy = true; ErrorMessage = null; PendingPlaybackPanelAction = action;
        try
        {
            if (!Current() || (!endingInitialization && (Room?.SelfCanManageRoom != true || Room.Generation != snapshot!.Generation))
                || (action == "stop" && (Room?.Phase != "playing" || ConnectionState != "connected"))) return;
            WatchTogetherRoomResponse response;
            switch (action)
            {
                case "stop":
                    response = await _playbackApi.StopWatchTogetherRoomPlaybackAsync(context, roomId);
                    break;
                case "toggle-policy":
                    var next = snapshot!.GuestControlPolicy == "guest_play_pause" ? "host_only" : "guest_play_pause";
                    response = await _playbackApi.UpdateWatchTogetherRoomPolicyAsync(context, roomId, next);
                    break;
                default:
                    await _playbackApi.CloseWatchTogetherRoomAsync(context, roomId);
                    // The room_closed socket frame can clear the snapshot before
                    // DELETE completes; that is success, not a change of owner.
                    if (!Current(allowClosedRoom: true)) return;
                    ClosedReason = "host_left";
                    await StopWebSocketAsync();
                    return;
            }
            // A newer socket generation wins an older REST response. In
            // particular Stop must not overwrite a newer playing selection.
            if (Current() && response.Room.RoomId == roomId && response.Room.Generation >= (Room?.Generation ?? 0))
                Room = response.Room;
        }
        catch (Exception)
        {
            if (Current()) SetLocalizedError(action switch
            {
                "stop" => () => WatchPartyText.Get("web.watchTogether.actions.stopFailed", "Could not stop playback"),
                "toggle-policy" => () => WatchPartyText.Get("web.watchTogether.actions.updateRoomFailed", "Failed to update room"),
                _ => () => WatchPartyText.Get("web.watchTogether.actions.endRoomFailed", "Failed to close room. Try again."),
            });
        }
        finally
        {
            if (mutation == _playbackPanelMutation)
            {
                PendingPlaybackPanelAction = null;
                IsBusy = false;
            }
        }
    }
}
