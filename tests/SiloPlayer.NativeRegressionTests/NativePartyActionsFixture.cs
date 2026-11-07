using System.Net;
using System.Reflection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.ViewModels;

internal static class NativePartyActionsFixture
{
    internal static async Task RunAsync()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://native-party.invalid"); client.SetProfile("host");
        using var room = new WatchTogetherRoomViewModel(new PlaybackApi(client), client)
        {
            RoomId = "synthetic-room", RoomToken = "synthetic-proof", ConnectionState = "connected", Room = Playing(),
        };
        var method = typeof(WatchTogetherRoomViewModel).GetMethod("StopPlaybackAsync");
        Require(method != null, "Room VM has no real Stop playback action.");
        Task Stop(Func<bool>? current = null) => (Task)method!.Invoke(room, [current])!;
        wire.Hold = true;
        var stopping = Stop(); await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(room.IsBusy && room.Room!.Phase == "playing", "Pending Stop prematurely changed accepted phase or lost pending state.");
        await Stop(); Require(wire.Calls == 1, "Pending Stop dispatched twice.");
        wire.Release.TrySetResult(); await stopping;
        Require(!room.IsBusy && room.Room!.Phase == "lobby" && room.Room.SelectedContentId == "movie"
            && room.Room.SelectedFileId == 42 && room.ClosedReason == null, "Stop ended/left the room or lost the staged selection.");
        room.Room = Playing(); wire.Hold = false; wire.Fail = true;
        await Stop(); Require(room.Room!.Phase == "playing" && room.ErrorMessage?.Contains("stop") == true && !room.IsBusy,
            "Rejected Stop changed the accepted room or lost retry feedback.");
        wire.Fail = false; await Stop(); Require(room.Room!.Phase == "lobby" && room.ErrorMessage == null, "Stop retry failed.");
        room.Room = Playing(); room.Room.SelfCanManageRoom = false; var calls = wire.Calls;
        await Stop(); Require(wire.Calls == calls, "Guest dispatched host Stop.");
        room.Room = Playing(); room.ConnectionState = "reconnecting";
        await Stop(); Require(wire.Calls == calls, "Disconnected host dispatched Stop.");
        room.ConnectionState = "connected"; await Stop(() => false);
        Require(wire.Calls == calls, "Retired coordinator room dispatched Stop.");
        foreach (var replacement in new[] { "profile", "room", "coordinator" })
        {
            client.SetProfile("host"); room.RoomId = "synthetic-room"; room.Room = Playing();
            wire.ResetHold(); var current = true; stopping = Stop(() => current);
            await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (replacement == "profile") client.SetProfile("replacement");
            else if (replacement == "room") { room.RoomId = "replacement-room"; room.Room = Playing("replacement-room"); }
            else current = false;
            wire.Release.TrySetResult(); await stopping;
            Require(room.Room!.Phase == "playing" && room.ErrorMessage == null && !room.IsBusy,
                "Old Stop completion changed replacement " + replacement + ".");
        }
        Program.Log("PASS: actual Watch Party VM Stop: distinct POST, pending/deduplication, lobby/staged-item retention, failure/retry, host/connection and profile/room/coordinator ownership.");
        await VerifyPageMutationRefreshAsync();
    }

    private static async Task VerifyPageMutationRefreshAsync()
    {
        foreach (var end in new[] { false, true })
        foreach (var replaceProfile in new[] { false, true })
        {
            using var wire = new RefreshWire(end); using var http = new HttpClient(wire);
            var client = new SiloApiClient(http);
            client.SetBaseUrl("https://native-party.invalid"); client.SetProfile("host"); client.SetAccessToken("expired");
            var refreshes = 0;
            client.SetTokenRefresher(_ =>
            {
                refreshes++;
                if (replaceProfile) client.SetProfile("replacement");
                client.SetAccessToken("refreshed");
                return Task.FromResult(true);
            });
            var snapshot = Playing(); snapshot.Phase = "lobby"; snapshot.GuestControlPolicy = "host_only";
            using var room = new WatchTogetherRoomViewModel(new PlaybackApi(client), client)
            {
                RoomId = "synthetic-room", RoomToken = "synthetic-proof", Room = snapshot,
            };
            // Exercise the room-page commands, not just the API overloads.
            if (end) await room.CloseRoomCommand.ExecuteAsync(null);
            else await room.TogglePolicyCommand.ExecuteAsync(null);
            Require(refreshes == 1 && wire.Calls == (replaceProfile ? 1 : 2),
                "Page mutation did not refresh once or retried as a replacement profile.");
            Require(!room.IsBusy && room.PendingPlaybackPanelAction == null && room.ErrorMessage == null,
                "Page mutation left pending state or published an expired-token error.");
            if (replaceProfile)
                Require(ReferenceEquals(room.Room, snapshot) && room.ClosedReason == null,
                    "Retired page mutation published state for a replacement profile.");
            else if (end)
                Require(room.ClosedReason == "host_left", "Page Close room did not succeed after token refresh.");
            else
                Require(room.Room?.GuestControlPolicy == "guest_play_pause" && room.ClosedReason == null,
                    "Page Toggle policy did not succeed after token refresh.");
        }
        Program.Log("PASS: lobby page Close room and Toggle policy refresh expired access tokens once without replaying or publishing under a replacement profile.");
    }

    private sealed class RefreshWire(bool end) : HttpMessageHandler
    {
        internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Require(request.Method == (end ? HttpMethod.Delete : HttpMethod.Patch)
                && request.RequestUri!.AbsolutePath == "/api/v2/watch-together/rooms/synthetic-room" + (end ? "" : "/policy"),
                "Page mutation emitted an unrelated request.");
            Require(request.Headers.Authorization?.Parameter == (Calls == 1 ? "expired" : "refreshed")
                && request.Headers.GetValues("X-Profile-Id").Single() == "host", "Page mutation used incorrect credentials.");
            if (!end)
            {
                using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Require(body.RootElement.GetProperty("guest_control_policy").GetString() == "guest_play_pause",
                    "Policy retry lost the intended policy.");
            }
            if (Calls == 1) return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") };
            return end ? new(HttpStatusCode.NoContent) : new(HttpStatusCode.OK) { Content = new StringContent(
                "{\"room\":{\"room_id\":\"synthetic-room\",\"phase\":\"lobby\",\"generation\":2,\"self_can_manage_room\":true,\"guest_control_policy\":\"guest_play_pause\"}}") };
        }
    }

    private static WatchTogetherRoomSnapshot Playing(string roomId = "synthetic-room") => new()
    {
        RoomId = roomId, Code = "TEST1234", Phase = "playing", Generation = 1,
        SelectedContentId = "movie", SelectedFileId = 42, SelfCanManageRoom = true,
    };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Wire : HttpMessageHandler
    {
        public int Calls;
        public bool Hold, Fail;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ResetHold() { Hold = true; Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/playback/stop"), "Stop emitted an unrelated room mutation.");
            Interlocked.Increment(ref Calls);
            if (Hold) { Entered.TrySetResult(); await Release.Task; }
            return new(Fail ? HttpStatusCode.InternalServerError : HttpStatusCode.OK) { Content = new StringContent(
                "{\"room\":{\"room_id\":\"synthetic-room\",\"phase\":\"lobby\",\"generation\":2,\"self_can_manage_room\":true,\"selected_content_id\":\"movie\",\"selected_file_id\":42}}") };
        }
    }
}
