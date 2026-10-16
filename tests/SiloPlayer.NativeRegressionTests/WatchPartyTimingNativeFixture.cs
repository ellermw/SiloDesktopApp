using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.ViewModels;

internal static class WatchPartyTimingNativeFixture
{
    public static async Task RunAsync()
    {
        using var listener = new HttpListener();
        var portPicker = new TcpListener(IPAddress.Loopback, 0); portPicker.Start();
        var port = ((IPEndPoint)portPicker.LocalEndpoint).Port; portPicker.Stop();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var accepting = listener.GetContextAsync();
        using var socket = new ClientWebSocket();
        var connecting = socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        var context = await accepting;
        using var server = (await context.AcceptWebSocketAsync(null)).WebSocket;
        await connecting;
        using var http = new HttpClient(new RejectNetwork());
        var api = new SiloApiClient(http); api.SetBaseUrl("https://room-timing.fixture.invalid");
        var playback = new PlaybackApi(api);
        using var auth = new AuthService(api, new AuthApi(api), credentialStore: null);
        using var player = new PlayerService(playback, new CatalogApi(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "room-timing")), new SettingsApi(api));
        using var mpv = new MpvPlayer();
        mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Field(player, "_mpv", mpv);
        Property(player, "Duration", 300d); Property(player, "Position", 100d);
        Property(player, "ContentId", "timing-movie"); Property(player, "IsPaused", false);
        using var room = new WatchTogetherRoomViewModel(playback, api)
        {
            Room = new() { RoomId = "timing-room", Phase = "playing", PlaybackState = "playing",
                SelectionRevision = 3, SelectedContentId = "timing-movie", SelfCanManageRoom = true },
            ConnectionState = "connected",
        };
        Field(room, "_ws", socket);
        using var cts = new CancellationTokenSource(); Field(room, "_wsCts", cts);
        var coordinator = new WatchTogetherCoordinator(player);
        coordinator.PlaybackStartRequested += _ => { };
        try
        {
            coordinator.SetActiveRoom(room); coordinator.NotifyPlaybackStarted("timing-session");
            ((Microsoft.UI.Xaml.DispatcherTimer)Get(coordinator, "_timer")!).Stop();
            var sync = (WatchPartySyncController)Get(coordinator, "_sync")!;
            var wall = DateTimeOffset.UtcNow;
            void Command(string id, string action, double target, string state, DateTimeOffset execute) =>
                Call(room, "HandleFrame", JsonSerializer.Serialize(new { type = "transport_command", command = new
                { command_id = id, session_id = "timing-session", selection_revision = 3, action,
                    position_seconds = target, playback_state = state, execute_at = execute.ToString("O"),
                    issued_at = wall.AddHours(-2).ToString("O") } }));
            Command("before-first-pong", "play", 100.625, "playing", wall.AddSeconds(1));
            Require(Math.Abs(mpv.GetPropertyDouble("speed") - 1) < .001, "Command ran before its initial clock deadline.");
            // The real pong handler removes server processing time and keeps the fastest sample.
            for (var i = 0; i < 4; i++)
                Call(room, "HandleFrameAt", JsonSerializer.Serialize(new { type = "pong",
                    client_sent_at = wall.AddMilliseconds(-100).ToString("O"),
                    server_received_at = wall.AddMilliseconds(940).ToString("O"),
                    server_sent_at = wall.AddMilliseconds(960).ToString("O") }), wall);
            Require(Math.Abs(room.ServerTimeOffsetMs - 1000) < .001, "VM did not consume four-timestamp room pongs.");
            sync.Tick(Environment.TickCount64);
            Require(((WatchTogetherTransportCommand?)Get(sync, "_applied"))?.CommandId == "before-first-pong" &&
                mpv.GetPropertyDouble("speed") is >= 1.01 and <= 1.04,
                "First real VM pong did not rebase the coordinator's already queued command.");
            sync.SetSession("temporary"); sync.SetSession("timing-session");
            Call(room, "StartClockPings", socket);
            var ping = await Read(server, "ping");
            Require(ping.TryGetProperty("client_sent_at", out _), "Actual VM ping omitted send timestamp.");
            Require(((Microsoft.UI.Xaml.DispatcherTimer)Get(room, "_pingTimer")!).Interval.TotalMilliseconds == 2000,
                "Native connection did not start fast room pings.");
            for (var i = 0; i < 4; i++)
                Call(room, "HandleFrameAt", JsonSerializer.Serialize(new { type = "pong",
                    client_sent_at = wall.AddMilliseconds(-100).ToString("O"),
                    server_received_at = wall.AddMilliseconds(940).ToString("O"),
                    server_sent_at = wall.AddMilliseconds(960).ToString("O") }), wall);
            Require(((Microsoft.UI.Xaml.DispatcherTimer)Get(room, "_pingTimer")!).Interval.TotalMilliseconds == 15000,
                "Four accepted pongs did not reschedule the actual native ping timer.");

            // execute_at is2s in the past after the measured+1s offset: target102 is now100+2.
            Property(player, "Position", 102d);
            Command("late-playing", "play", 100, "playing", DateTimeOffset.UtcNow.AddSeconds(-1));
            Require(Math.Abs(mpv.GetPropertyDouble("speed") - 1) < .001,
                "Coordinator clamped the past schedule or used issued_at as a clock sample.");
            Require(Math.Abs(room.ServerTimeOffsetMs - 1000) < .001, "Transport issued_at polluted sampled room clock.");
            Property(player, "Position", 101.375d);
            Command("quantized", "play", 102, "playing", DateTimeOffset.UtcNow.AddSeconds(1.2));
            sync.Tick((long)Get(sync, "_executeAt")!);
            Require(Math.Abs(mpv.GetPropertyDouble("speed") - 1.025) < .001, "Native adapter did not apply quantized2.5% rate.");
            sync.Tick(Environment.TickCount64 + 1600);
            var report = await Read(server, "state_report");
            Require(report.GetProperty("command_id").GetString() == "quantized", "Controller/coordinator/VM lost actual applied command ID.");
            player.RequestUserSeek(110);
            player.RequestUserRelativeSeek(10); player.RequestUserRelativeSeek(10);
            foreach (var target in new[] { 110d, 120d, 130d })
            {
                var request = await Read(server, "transport_request");
                Require(request.GetProperty("action").GetString() == "seek" &&
                    request.GetProperty("position_seconds").GetDouble() == target, "Actual native relative burst repeated the stale media position.");
            }
            Require(player.UserSeekPosition == 130 && player.Position == 101.375,
                "Held room request did not remain separate from actual report/progress time.");
            Command("answers-held", "seek", 130, "paused", DateTimeOffset.UtcNow.AddSeconds(3));
            Command("replaces-held", "pause", 101.375, "paused", DateTimeOffset.UtcNow.AddSeconds(1));
            sync.Tick(Environment.TickCount64 + 30);
            Require(player.UserSeekPosition == player.Position, "Applied replacement failed to release answered held request.");
            sync.SetSession("replacement");
            Require(Math.Abs(mpv.GetPropertyDouble("speed") - 1) < .001, "Native session replacement retained catch-up speed.");
            sync.Tick(Environment.TickCount64 + 4000);
            report = await Read(server, "state_report");
            Require(report.GetProperty("session_id").GetString() == "replacement" &&
                report.GetProperty("command_id").ValueKind == JsonValueKind.Null, "Replacement reported the old command.");

            // Load isolated synthetic audio so a late paused seek is checked against native media time.
            var wave = Path.Combine(Program.ResultDirectory, "room-timing.wav");
            using (var writer = new BinaryWriter(File.Create(wave)))
            {
                const int samples = 300 * 8000;
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples + 36);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
                writer.Write((short)1); writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples); writer.Write(new byte[samples]);
            }
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            mpv.FileLoaded += () => loaded.TrySetResult();
            mpv.Pause(); mpv.LoadFile(wave, startSeconds: 120);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Field(player, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
            Property(player, "IsPaused", true); Property(player, "Position", 120d);
            coordinator.NotifyPlaybackStarted("timing-session");
            Command("late-paused", "seek", 130, "paused", DateTimeOffset.UtcNow.AddSeconds(-1));
            for (var i = 0; i < 100 && Math.Abs(mpv.GetPropertyDouble("time-pos") - 130) > .1; i++) await Task.Delay(20);
            Require(Math.Abs(mpv.GetPropertyDouble("time-pos") - 130) <= .1 && mpv.GetPropertyFlag("pause"),
                "Actual native paused seek advanced its target by transport lateness or resumed playback.");
            Property(player, "Position", 130d);
            player.RequestUserSeek(140);
            Command("matched-native-seek", "seek", 140, "paused", DateTimeOffset.UtcNow.AddSeconds(1));
            sync.Tick(Environment.TickCount64 + 30);
            player.RequestUserRelativeSeek(10);
            foreach (var target in new[] { 140d, 150d })
            {
                var request = await Read(server, "transport_request");
                Require(request.GetProperty("position_seconds").GetDouble() == target,
                    "Native relative press after matching command lost the issued seek origin.");
            }
            Require(player.UserSeekPosition == 150 && player.Position == 130,
                "Controls lost the newest target while native media still lagged the seek.");
            Program.Log("PASS: actual WinUI VM pong/ping + command before first pong/dynamic coordinator schedule + late projection + libmpv quantized rate/paused seek + socket applied-ID/session replacement + native relative burst/held display/replacement.");
        }
        finally
        {
            coordinator.ClearActiveRoom(); Field(player, "_mpv", null);
            Field(room, "_ws", null); Field(room, "_wsCts", null);
        }
    }

    private static async Task<JsonElement> Read(WebSocket ws, string type)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var bytes = new byte[16384];
        while (true)
        {
            using var message = new MemoryStream(); WebSocketReceiveResult result;
            do { result = await ws.ReceiveAsync(bytes, timeout.Token); message.Write(bytes, 0, result.Count); }
            while (!result.EndOfMessage);
            using var doc = JsonDocument.Parse(message.ToArray());
            if (doc.RootElement.GetProperty("type").GetString() == type) return doc.RootElement.Clone();
        }
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private static object? Get(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static void Field(object value, string name, object? item) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, item);
    private static void Property(object value, string name, object? item) => value.GetType().GetProperty(name)!.SetValue(value, item);
    private static object? Call(object value, string name, params object?[] args) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Room timing fixture attempted account/server HTTP.");
    }
}
