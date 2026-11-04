using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Helpers;
using SiloPlayer.Views;
namespace SiloPlayer.Services;

/// <summary>Preserves room transport ownership for a movie intent before entering standalone playback.</summary>
public static class MoviePlaybackEntry
{
    public static async Task PlayAsync(MediaItem movie, int libraryId, ApiRequestContext owner, CancellationToken ct,
        Func<bool> isCurrentOwner, Func<string, int, ApiRequestContext, CancellationToken, Task> playStandalone)
    {
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var identity = client.CaptureIdentityContext();
        bool Current() => !ct.IsCancellationRequested && isCurrentOwner() && client.IsCurrentIdentityContext(identity)
            && identity.AuthenticationGeneration == owner.AuthenticationGeneration && identity.BaseUrl == owner.BaseUrl && identity.ProfileId == owner.ProfileId;
        if (!Current() || !BoxSetDetail.IsMovieMember(movie))
            throw new InvalidOperationException("This movie is no longer available for playback. Reopen the box set and try again.");
        var coordinator = App.Services.GetRequiredService<WatchTogetherCoordinator>();
        if (coordinator.ActiveRoom is not {} room)
        {
            await playStandalone(movie.ContentId, libraryId, owner, ct);
            return;
        }
        var roomId = room.RoomId; var roomToken = room.RoomToken;
        void RequireCurrentRoom()
        {
            if (!Current() || !ReferenceEquals(coordinator.ActiveRoom, room) || room.RoomId != roomId || room.RoomToken != roomToken)
                throw new InvalidOperationException("The current Watch Party changed. Open the room and try again.");
        }
        RequireCurrentRoom();
        var player = App.Services.GetRequiredService<PlayerService>();
        if (room.Room is { Phase: "playing" } playing && playing.SelectedContentId == movie.ContentId
            && player.ContentId == movie.ContentId && room.AttachedSessionId is {} attached && attached == player.Manager?.SessionId)
        {
            coordinator.RequestTransport("play", player.UserSeekPosition, isPaused: false);
            return;
        }
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(roomToken))
            throw new InvalidOperationException("This Watch Party is not ready yet. Open the room and try again.");
        var playback = App.Services.GetRequiredService<PlaybackApi>();
        try
        {
            if (room.IsHost && !room.IsVoteMode && room.Room?.Phase == "lobby")
            {
                var response = await playback.StageWatchTogetherRoomItemAsync(roomId, movie.ContentId, libraryId: libraryId, ct: ct,
                    expectedIdentityContext: identity);
                RequireCurrentRoom();
                room.Room = response.Room;
            }
            else
                await playback.CreateWatchTogetherSuggestionAsync(roomId, roomToken, movie.ContentId, "movie", movie.Title,
                    posterUrl: movie.PosterUrl, ct: ct, expectedIdentityContext: identity);
            RequireCurrentRoom();
            App.Services.GetRequiredService<NavigationService>().Navigate<WatchTogetherRoomPage>(new WatchTogetherRoomNavigationArgs
            { RoomId = roomId, RoomAccessToken = roomToken });
        }
        catch (OperationCanceledException ex)
        {
            throw new InvalidOperationException("Could not confirm this movie in the Watch Party. Open the room to check its selection and try again.", ex);
        }
    }
}
