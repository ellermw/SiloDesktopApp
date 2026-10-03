using System.Net;
using System.Reflection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static class WatchPartyNativeFixture
{
    // Called on the hidden fixture's WinUI dispatcher, never the user's app.
    public static Task RunAsync()
    {
        using var http = new HttpClient(new NoNetworkHandler());
        var api = new SiloApiClient(http);
        api.SetBaseUrl("https://watch-party-fixture.invalid");
        var playback = new PlaybackApi(api);
        var auth = new AuthService(api, new AuthApi(api));
        using var player = new PlayerService(playback, new CatalogApi(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "watch-party")), new SettingsApi(api));
        using var room = new WatchTogetherRoomViewModel(playback, api);
        room.Room = new WatchTogetherRoomSnapshot
        {
            RoomId = "fixture-room", Phase = "playing", PlaybackState = "playing", SelectionRevision = 3,
            SelectedContentId = "fixture-movie", SelfCanManageRoom = true, SelfCanControlTransport = true,
        };
        room.ConnectionState = "connected";
        typeof(PlayerService).GetProperty(nameof(PlayerService.ContentId))!.SetValue(player, "fixture-movie");
        var coordinator = new WatchTogetherCoordinator(player);
        var starts = new List<string>();
        coordinator.PlaybackStartRequested += starts.Add;
        try
        {
            coordinator.SetActiveRoom(room);
            Require(starts.SequenceEqual(new[] { "fixture-movie" }), "Initial playing REST snapshot did not request startup.");
            coordinator.NotifyPlaybackStarted("fixture-session");
            Require(room.AttachedSessionId == "fixture-session", "Actual coordinator failed to attach the playback session.");
            Require(player.IsWatchTogetherPlayback, "Actual player did not enter room transport authority.");

            var intents = new List<(string Action, double Position)>();
            player.RoomTransportRequested += (action, position, _) => intents.Add((action, position));
            player.RequestUserPauseToggle();
            player.RequestUserSeek(125, forceResume: true);
            Require(intents.Count == 2 && intents[1] == ("seek", 125d), "Native user controls did not enter room intent boundary exactly once.");
            player.SetPaused(true);
            player.SeekRoomTarget(70);
            Require(intents.Count == 2, "Applying server transport echoed a user request.");

            // The retained same-session ID must attach after the socket changes,
            // independently of SessionStarted and its deduplication guard.
            room.ConnectionState = "reconnecting";
            room.DetachSession();
            room.ConnectionState = "connected";
            Require(room.AttachedSessionId == "fixture-session", "Socket recovery did not reattach the same native session.");
            coordinator.ClearActiveRoom();
            Require(!player.IsWatchTogetherPlayback && room.AttachedSessionId == null,
                "Leaving room retained playback authority or attachment.");
            player.RequestUserPauseToggle();
            player.RequestUserSeek(20);
            Require(intents.Count == 2, "Standalone controls still emitted room messages after leaving.");
            Program.Log("PASS: published WatchTogetherCoordinator + PlayerService start/attachment/reconnect/user intents/no echo/standalone isolation.");
        }
        finally { coordinator.ClearActiveRoom(); }
        return Task.CompletedTask;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Watch Party native fixture attempted network access.");
    }
}
