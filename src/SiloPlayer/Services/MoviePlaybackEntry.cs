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
    public static async Task PlayAsync(MediaItem movie, int libraryId, ApiIdentityContext owner, CancellationToken ct,
        Func<bool> isCurrentOwner, Func<string, int, ApiIdentityContext, CancellationToken, Task> playStandalone)
    {
        var client = App.Services.GetRequiredService<SiloApiClient>();
        // Access revisions retire reads, not an already accepted movie intent.
        bool Current() => !ct.IsCancellationRequested && isCurrentOwner() && client.IsCurrentIdentityContext(owner);
        if (!Current() || !BoxSetDetail.IsMovieMember(movie)) return;
        var coordinator = App.Services.GetRequiredService<WatchTogetherCoordinator>();
        if (coordinator.ActiveRoom is not {} room)
        {
            await playStandalone(movie.ContentId, libraryId, owner, ct);
            return;
        }
        bool CurrentRoom() => Current() && ReferenceEquals(coordinator.ActiveRoom, room);
        if (!CurrentRoom()) return;
        var player = App.Services.GetRequiredService<PlayerService>();
        if (room.Room is { Phase: "playing" } playing && playing.SelectedContentId == movie.ContentId
            && player.ContentId == movie.ContentId && room.AttachedSessionId is {} attached && attached == player.Manager?.SessionId)
        {
            coordinator.RequestTransport("play", player.UserSeekPosition, isPaused: false);
            return;
        }
        if (room.RoomId == null || room.RoomToken == null) return;
        var playback = App.Services.GetRequiredService<PlaybackApi>();
        if (room.IsHost && !room.IsVoteMode && room.Room?.Phase == "lobby")
        {
            var response = await playback.StageWatchTogetherRoomItemAsync(room.RoomId, movie.ContentId, libraryId: libraryId, ct: ct, expectedIdentityContext: owner);
            if (!CurrentRoom()) return;
            room.Room = response.Room;
        }
        else
            await playback.SubmitWatchTogetherSuggestionAsync(room.RoomId, room.RoomToken, movie.ContentId, "movie", movie.Title,
                posterUrl: movie.PosterUrl, ct: ct, expectedIdentityContext: owner);
        if (CurrentRoom()) App.Services.GetRequiredService<NavigationService>().Navigate<WatchTogetherRoomPage>(new WatchTogetherRoomNavigationArgs
        { RoomId = room.RoomId, RoomAccessToken = room.RoomToken });
    }
}
