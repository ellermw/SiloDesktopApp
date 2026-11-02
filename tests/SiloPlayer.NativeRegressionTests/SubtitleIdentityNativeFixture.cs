using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static class SubtitleIdentityNativeFixture
{
    internal static async Task RunAsync(StackPanel parent, bool unresolvedOnly = false)
    {
        var assets = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("SILO_NATIVE_TEST_READER_FIXTURES")!, "..", "fixtures", "subtitle-identity"));
        using var subtitles = new SubtitleServer(File.ReadAllBytes(Path.Combine(assets, "english.srt")));
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://subtitle-identity.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        using var service = new PlayerService(new(api), new(api), auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "subtitle-identity")), new SettingsApi(api));
        using var manager = new PlaybackManager(new(api), new(api), auth, api);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var session = new PlaybackStartResponse { SessionId = "synthetic", ProtocolVersion = 3, MediaFileId = 42, PlayMethod = "direct" };
        session.SubtitleUrls = JsonSerializer.Deserialize<List<SubtitleTrackInfo>>("""
            [{"index":0,"language":"en","codec":"srt","source":"external"},
             {"index":1,"language":"en","codec":"webvtt","source":"embedded","stream_index":77,"container_track_id":"99"},
             {"index":2,"language":"en","codec":"subrip","source":"embedded","stream_index":1,"container_track_id":"2"},
             {"index":3,"language":"en","source":"embedded","delivery":"burn_in_only","unplayable":true},
             {"index":4,"language":"ja","codec":"subrip","source":"embedded","stream_index":2,"container_track_id":"3"},
             {"index":7,"language":"it","codec":"srt","source":"downloaded"}]
            """, options)!;
        foreach (var track in session.SubtitleUrls) track.Url = subtitles.Origin + "/captions.srt";
        typeof(PlaybackManager).GetProperty("CurrentSession")!.SetValue(manager, session);
        manager.UseWatchDetail(new() { ContentId = "movie", SeriesId = "series", Versions = [new() { FileId = 42 }] });
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        typeof(PlayerService).GetProperty("State")!.SetValue(service, PlayerState.Expanded);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(service, "movie");
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", Path.Combine(assets, "tracks.mkv")));
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mpv.FileLoaded += () => loaded.TrySetResult(); mpv.LoadFile(Path.Combine(assets, "tracks.mkv"));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5)); mpv.Pause();
        try
        {
            if (unresolvedOnly)
            {
                var previousTrack = session.SubtitleUrls.Single(t => t.Index == 2);
                await service.SetSubtitleTrackAndPersistAsync(3, previousTrack.Language, previousTrack);
                var noRoute = JsonSerializer.Deserialize<SubtitleTrackInfo>("""{"index":9,"source":"embedded","codec":"webvtt","language":"en","stream_index":99,"container_track_id":"99"}""", options)!;
                session.SubtitleUrls.Add(noRoute);
                var previousIndex = service.ActiveSubtitleServerIndex;
                var previousSid = mpv.GetPropertyDouble("sid"); var preferenceWrites = wire.PreferenceWrites;
                var noRouteReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var notice = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var retainedHighlight = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Notice(string[] message)
                {
                    if (message.FirstOrDefault() == "silo-identity-notice-ready") noRouteReady.TrySetResult();
                    if (message.FirstOrDefault() == "silo-identity-notice" && message.Length > 1) notice.TrySetResult(message[1]);
                    if (message.FirstOrDefault() == "silo-identity-retained" && message.Length > 1) retainedHighlight.TrySetResult(message[1]);
                }
                mpv.ScriptMessageReceived += Notice;
                try
                {
                    var noticeProbe = Path.Combine(Program.ResultDirectory, "identity-notice.lua");
                    await File.WriteAllTextAsync(noticeProbe, """
                        local mp = require 'mp'
                        mp.register_script_message('osc-show-notice', function(payload)
                            mp.commandv('script-message', 'silo-identity-notice', payload)
                        end)
                        mp.register_script_message('osc-set-active-subtitle', function(index)
                            mp.commandv('script-message', 'silo-identity-retained', index)
                        end)
                        mp.commandv('script-message', 'silo-identity-notice-ready')
                        """);
                    Call(mpv, "Command", (object)new[] { "load-script", noticeProbe });
                    await noRouteReady.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    await service.SetSubtitleTrackAndPersistAsync(10, "en", noRoute);
                    await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 9, true)!;
                    Require(service.ActiveSubtitleServerIndex == previousIndex && mpv.GetPropertyDouble("sid") == previousSid && wire.PreferenceWrites == preferenceWrites,
                        "Unresolved native identity without a supported URL changed the active choice or preferences.");
                    using var noticePayload = JsonDocument.Parse(await notice.Task.WaitAsync(TimeSpan.FromSeconds(4)));
                    Require(await retainedHighlight.Task.WaitAsync(TimeSpan.FromSeconds(4)) == previousIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        "Refused no-route subtitle did not restore the retained OSC highlight.");
                    Require(noticePayload.RootElement.GetProperty("message").GetString()!.Contains("another subtitle", StringComparison.OrdinalIgnoreCase),
                        "Refused no-route subtitle did not provide an actionable notice.");
                    Program.Log("PASS: unresolved identity without sidecar refuses public/callback selection, preserves previous SID/highlight/preferences and sends an actionable OSC notice.");
                }
                finally { mpv.ScriptMessageReceived -= Notice; }
                return;
            }
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var inventory = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observe(string[] message)
            {
                if (message.FirstOrDefault() == "silo-identity-ready") ready.TrySetResult();
                if (message.FirstOrDefault() == "silo-identity-inventory" && message.Length > 1) inventory.TrySetResult(message[1]);
            }
            mpv.ScriptMessageReceived += Observe;
            try
            {
                var probe = Path.Combine(Program.ResultDirectory, "identity-observe.lua");
                await File.WriteAllTextAsync(probe, """
                    local mp = require 'mp'
                    mp.register_script_message('osc-set-subtitles', function(payload)
                        mp.commandv('script-message', 'silo-identity-inventory', payload)
                    end)
                    mp.commandv('script-message', 'silo-identity-ready')
                    """);
                Call(mpv, "Command", (object)new[] { "load-script", probe });
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Call(service, "SendSubtitleListToOsc");
                using var payload = JsonDocument.Parse(await inventory.Task.WaitAsync(TimeSpan.FromSeconds(4)));
                Require(payload.RootElement.GetArrayLength() == 6 && payload.RootElement.EnumerateArray().Single(t => t.GetProperty("index").GetInt32() == 3).GetProperty("unplayable").GetBoolean(),
                    "Production OSC payload lost unavailable row or its combined ordinal.");
            }
            finally { mpv.ScriptMessageReceived -= Observe; }
            var english = session.SubtitleUrls.Single(t => t.Index == 2);
            await service.SetSubtitleTrackAndPersistAsync(3, english.Language, english);
            Require(mpv.GetPropertyDouble("sid") == 1, "Interleaved index-only inventory selected the wrong native SID instead of source container track 2.");
            Require(mpv.IsPaused, "Native subtitle selection resumed viewer-paused playback.");
            var unavailable = session.SubtitleUrls.Single(t => t.Index == 3);
            var previous = service.ActiveSubtitleServerIndex; var writes = wire.PreferenceWrites;
            await service.SetSubtitleTrackAndPersistAsync(4, "en", unavailable);
            await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 3, true)!;
            Call(service, "SelectSubtitleTrack", unavailable);
            Require(service.ActiveSubtitleServerIndex == previous && wire.PreferenceWrites == writes && wire.Replans == 0 && mpv.GetPropertyDouble("sid") == 1,
                "Forged unavailable callbacks changed selection, preferences or transport.");
            var indexOnly = session.SubtitleUrls.Single(t => t.Index == 1);
            await service.SetSubtitleTrackAndPersistAsync(2, "en", indexOnly);
            var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
            Require(map.TryGetValue(1, out var fallback) && fallback > 2 && mpv.GetPropertyDouble("sid") == fallback,
                "Absent embedded track did not execute its sidecar fallback.");
            var downloaded = session.SubtitleUrls.Single(t => t.Index == 7);
            await service.SetSubtitleTrackAndPersistAsync(8, downloaded.Language, downloaded);
            Require(map.TryGetValue(7, out var downloadedSid) && downloadedSid > fallback && mpv.GetPropertyDouble("sid") == downloadedSid,
                "Downloaded combined ordinal did not execute its sidecar independently of embedded source identity.");
            var japanese = session.SubtitleUrls.Single(t => t.Index == 4);
            await service.SetSubtitleTrackAndPersistAsync(5, "ja", japanese);
            Require(mpv.GetPropertyDouble("sid") == 2 && mpv.IsPaused, "Later native identity was shifted by sidecar/index-only inventory.");
            await service.SetSubtitleTrackAndPersistAsync(0, null, null);
            Require(mpv.GetPropertyDouble("sid") <= 0 && mpv.IsPaused, "Explicit Off changed viewer pause intent.");
            Program.Log("PASS: subtitle identity uses actual loaded Matroska tracks across index-only gap and sidecar addition; unavailable forged service/native callbacks preserve selection/preferences; Off and genuine pause survive.");
        }
        finally { Call(service, "StopSubtitleSync"); Set(service, "_mpv", null); Set(service, "_playbackManager", null); }
    }

    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class SubtitleServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Origin { get; }
        public SubtitleServer(byte[] bytes)
        {
            var reserve = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); reserve.Start();
            var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; _listener.Prefixes.Add(Origin + "/"); _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext request;
                    try { request = await _listener.GetContextAsync(); } catch (Exception) when (!_listener.IsListening) { break; }
                    request.Response.ContentType = "application/x-subrip"; request.Response.ContentLength64 = bytes.Length;
                    await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close();
                }
            });
        }
        public void Dispose() => _listener.Close();
    }
    private sealed class Wire : HttpMessageHandler
    {
        public int PreferenceWrites, Replans;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "subtitle-identity.invalid") throw new InvalidOperationException("Unexpected fixture networking.");
            if (request.RequestUri.AbsolutePath.Contains("-prefs/")) PreferenceWrites++;
            if (request.RequestUri.AbsolutePath.EndsWith("/replan")) Replans++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
