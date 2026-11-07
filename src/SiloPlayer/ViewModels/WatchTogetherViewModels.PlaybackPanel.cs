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

    private async Task RunPlaybackPanelMutationAsync(string action, Func<bool>? ownsRoom = null)
    {
        var snapshot = Room;
        var roomId = RoomId;
        // The page's explicit Close command can end a join before its first
        // snapshot arrives. Native panel actions still require a managed room.
        var endingPendingJoin = action == "end" && ownsRoom == null && snapshot == null
            && _initializationAuthority is { } authority && _apiClient.IsCurrentContext(authority);
        if (action is not ("stop" or "toggle-policy" or "end") || _disposed || IsBusy || ClosedReason != null
            || !endingPendingJoin && snapshot?.SelfCanManageRoom != true || string.IsNullOrWhiteSpace(roomId)
            || !endingPendingJoin && snapshot?.RoomId != roomId || ownsRoom?.Invoke() == false) return;
        if (action == "stop" && (snapshot?.Phase != "playing" || ConnectionState != "connected")) return;
        var context = _apiClient.CaptureContext();
        var roomToken = RoomToken;
        // End retires unfinished room initialization, preserving the existing
        // close behavior while its accepted DELETE settles.
        if (action == "end") ++_initializationGeneration;
        var generation = _initializationGeneration;
        var mutation = ++_playbackPanelMutation;
        bool Current() => !_disposed && generation == _initializationGeneration
            && _apiClient.IsCurrentContext(context) && RoomId == roomId && RoomToken == roomToken
            && (endingPendingJoin ? Room == null : Room?.RoomId == roomId) && ownsRoom?.Invoke() != false;
        IsBusy = true; ErrorMessage = null; PendingPlaybackPanelAction = action;
        try
        {
            if (!Current() || !endingPendingJoin && (Room?.SelfCanManageRoom != true || Room.Generation != snapshot!.Generation)
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
                    if (!Current()) return;
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
            if (Current()) ErrorMessage = action switch
            {
                "stop" => "Could not stop playback. Try again.",
                "toggle-policy" => "Policy update failed. Try again.",
                _ => "Failed to close room. Try again.",
            };
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
