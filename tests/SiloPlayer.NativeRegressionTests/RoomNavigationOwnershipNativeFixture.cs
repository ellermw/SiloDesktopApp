using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;
using SiloPlayer.Views;

internal static class RoomNavigationOwnershipNativeFixture
{
    internal static async Task RunAsync()
    {
        var mode = Environment.GetEnvironmentVariable("SILO_NATIVE_ROOM_LIFECYCLE") ?? "superseded";
        var field = typeof(SiloPlayer.App).GetField("_services", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = (IServiceProvider)field.GetValue(null)!;
        using var wire = new Wire(mode); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://room-lifecycle.invalid");
        var playback = new PlaybackApi(client);
        using var auth = new AuthService(client, new AuthApi(client), credentialStore: null);
        auth.SetCurrentUser(new() { Id = "fixture", Role = "user" });
        auth.SelectProfile("fixture", profile: new() { Id = "fixture", Name = "Fixture" });
        using var player = new PlayerService(playback, new CatalogApi(client), auth, client,
            new SettingsService(Path.Combine(Program.ResultDirectory, "room-settings")), new SettingsApi(client));
        var owned = new List<WatchTogetherRoomViewModel>();
        var services = new Services(previous, new()
        {
            [typeof(SiloApiClient)] = client, [typeof(HttpClient)] = http, [typeof(PlaybackApi)] = playback,
            [typeof(CatalogApi)] = new CatalogApi(client), [typeof(AuthService)] = auth, [typeof(PlayerService)] = player,
        }, () => { var vm = new WatchTogetherRoomViewModel(playback, client); owned.Add(vm); return vm; });
        field.SetValue(null, services);
        var coordinator = new WatchTogetherCoordinator(player); services.Values[typeof(WatchTogetherCoordinator)] = coordinator;
        var frame = new Frame(); var window = new Window { Content = frame };
        try
        {
            window.AppWindow.Move(new Windows.Graphics.PointInt32(-20000, -20000));
            window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1100, 900)); window.AppWindow.Show(false);
            if (mode == "shared")
            {
                var initial = new WatchTogetherRoomViewModel(playback, client) { RoomId = "existing", RoomToken = "fixture-existing", Room = new() { RoomId = "existing", Phase = "lobby" } };
                owned.Add(initial); coordinator.SetActiveRoom(initial);
            }
            frame.Navigate(typeof(WatchTogetherRoomPage), new WatchTogetherRoomNavigationArgs { RoomId = "a", RoomAccessToken = "fixture-a" });
            var oldPage = (WatchTogetherRoomPage)frame.Content;
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (mode is "leave-only" or "clear" or "authority" or "end-join")
            {
                frame.Navigate(typeof(Page));
                if (mode == "clear") coordinator.ClearActiveRoom();
                if (mode == "authority") auth.ConfigureServer("https://room-other.invalid");
                if (mode == "end-join") await oldPage.ViewModel.CloseRoomCommand.ExecuteAsync(null);
            }
            else
            {
                frame.Navigate(typeof(WatchTogetherRoomPage), new WatchTogetherRoomNavigationArgs { RoomId = "b", RoomAccessToken = "fixture-b" });
                await Until(() => coordinator.ActiveRoom?.RoomId == "b");
            }
            wire.Release.TrySetResult(); await Task.Delay(400);
            var active = coordinator.ActiveRoom;
            var expected = mode is "clear" or "authority" or "end-join" ? null : mode == "leave-only" ? "a" : "b";
            Program.Log($"Room lifecycle {mode}: expected={expected ?? "none"}, actual={active?.RoomId ?? "none"}.");
            if (active?.RoomId != expected) throw new InvalidOperationException("A stale navigation changed authoritative room ownership.");
            var starts = (Delegate?)typeof(WatchTogetherCoordinator).GetField("PlaybackStartRequested", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(coordinator);
            var count = starts?.GetInvocationList().Length ?? 0;
            if (count != (mode is "leave-only" or "clear" or "authority" or "end-join" ? 0 : 1))
                throw new InvalidOperationException("Stale room page retained or duplicated a playback-start handler.");
            if (mode == "held-join" && wire.Requests.Any(path => path.Contains("/rooms/a/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Superseded initialization issued follow-up room requests after its held response.");
            if (mode == "leave-only")
            {
                frame.Navigate(typeof(WatchTogetherRoomPage), new WatchTogetherRoomNavigationArgs { RoomId = "a", RoomAccessToken = "fixture-a" });
                await Task.Delay(150);
                starts = (Delegate?)typeof(WatchTogetherCoordinator).GetField("PlaybackStartRequested", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(coordinator);
                if (starts?.GetInvocationList().Length != 1) throw new InvalidOperationException("Reopening the room duplicated its auto-start handler.");
            }
            Program.Log("PASS: actual room page navigation retains only the authoritative room and current page handler; current off-page room continuation is preserved.");
        }
        finally
        {
            wire.Release.TrySetResult(); frame.Navigate(typeof(Page)); await Task.Delay(100); coordinator.ClearActiveRoom();
            foreach (var room in owned) room.Dispose();
            window.Close(); field.SetValue(null, previous);
        }
    }

    private static async Task Until(Func<bool> done)
    {
        var limit = DateTime.UtcNow.AddSeconds(3);
        while (!done() && DateTime.UtcNow < limit) await Task.Delay(15);
        if (!done()) throw new InvalidOperationException("Current room did not become active.");
    }

    private sealed class Services(IServiceProvider fallback, Dictionary<Type, object> values, Func<WatchTogetherRoomViewModel> factory) : IServiceProvider
    {
        internal Dictionary<Type, object> Values => values;
        public object? GetService(Type type) => type == typeof(WatchTogetherRoomViewModel) ? factory() : values.GetValueOrDefault(type) ?? fallback.GetService(type);
    }

    private sealed class Wire(string mode) : HttpMessageHandler
    {
        private int _capabilities;
        internal List<string> Requests { get; } = [];
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "room-lifecycle.invalid") throw new InvalidOperationException("External room fixture request refused.");
            var path = uri.AbsolutePath;
            lock (Requests) Requests.Add(path);
            if (path.EndsWith("/capabilities", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _capabilities) == 1 && mode is not ("held-join" or "end-join"))
                { Started.TrySetResult(); await Release.Task.WaitAsync(ct); }
                return Json("{}");
            }
            if (path.EndsWith("/rooms/a", StringComparison.Ordinal) || path.EndsWith("/rooms/b", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Delete) return new(HttpStatusCode.NoContent);
                var id = path.EndsWith("/a", StringComparison.Ordinal) ? "a" : "b";
                if (id == "a" && mode is "held-join" or "end-join") { Started.TrySetResult(); await Release.Task.WaitAsync(ct); }
                return Json(JsonSerializer.Serialize(new { room = new { room_id = id, phase = "lobby", guest_control_policy = "host_only" } }));
            }
            if (path.Contains("ticket", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            return Json("{\"items\":[],\"suggestions\":[]}");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };
    }
}
