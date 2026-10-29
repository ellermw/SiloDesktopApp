using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

internal static class RelayLoadOwnershipNativeFixture
{
    internal static async Task RunAsync()
    {
        using var origin = new RangeOrigin();
        using var wire = new Wire(origin.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://relay-load.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "relay-settings")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        var version = new FileVersion { FileId = 42, Duration = 60, AudioTracks = [new() { Language = "eng", Default = true }], SubtitleTracks = [] };
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [version] });
        await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        var wave = Path.Combine(Program.ResultDirectory, "relay-baseline.wav"); await File.WriteAllBytesAsync(wave, origin.Wave);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        service.SetPaused(true); await Until(() => mpv.IsPaused);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeEvents = new ConcurrentQueue<string>();
        void Observe(string[] args)
        {
            if (args.FirstOrDefault() == "silo-relay-ready") ready.TrySetResult();
            else if (args.FirstOrDefault()?.StartsWith("silo-relay-", StringComparison.Ordinal) == true)
                nativeEvents.Enqueue(string.Join(" | ", args));
        }
        var errors = 0;
        void Error(string message) { Interlocked.Increment(ref errors); Program.Log($"Relay PlaybackError while current plan={manager.CurrentSession?.PlanId}: {message}"); }
        mpv.ScriptMessageReceived += Observe; mpv.PlaybackError += Error;
        var script = Path.Combine(Program.ResultDirectory, "relay-observe.lua");
        await File.WriteAllTextAsync(script, """
            local mp = require 'mp'
            local function path() return (mp.get_property('path', ''):gsub('%?.*$', '')) end
            mp.register_event('start-file', function(e)
                mp.commandv('script-message', 'silo-relay-start', tostring(e.playlist_entry_id), path())
            end)
            mp.register_event('end-file', function(e)
                mp.commandv('script-message', 'silo-relay-end', tostring(e.playlist_entry_id), tostring(e.reason), tostring(e.file_error), path())
            end)
            mp.commandv('script-message', 'silo-relay-ready')
            """);
        typeof(MpvPlayer).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mpv, [new[] { "load-script", script }]);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Call(service, "WireMpvEvents");
        try
        {
            var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession!, version, manager.StreamUrl!, 0d, CancellationToken.None)!;
            await prepare; var a = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
            Require(Get(service, "_directStreamProxy") is DirectStreamProxy, "The outgoing plan did not use the supported fallback relay.");
            Call(service, "ApplyPreparedTransport", a); Call(service, "BeginMpvLoad", a, true);
            await origin.AEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Require(service.IsLoading && mpv.IsPaused, "Outgoing A was not held while preserving paused intent.");
            await (Task)Call(service, "SwitchQualityTierAsync", "480p", null, null)!;
            await Task.WhenAny(origin.BEntered.Task, origin.RecoveryEntered.Task).WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200);
            var beforeReleasePlan = manager.CurrentSession?.PlanId;
            var pendingPause = (bool?)Get(service, "_restorePausedAfterLoad");
            origin.Release.TrySetResult();
            await Until(() => !service.IsLoading && mpv.GetPropertyDouble("duration") > 0);
            await Task.Delay(800); // Settle old native END_FILE and any queued recovery callback.
            Call(service, "StopPlaybackStallWatchdog");
            foreach (var item in nativeEvents) Program.Log(item);
            foreach (var item in wire.Operations) Program.Log("Relay plan " + item);
            foreach (var item in origin.Requests) Program.Log("Relay origin " + item);
            Program.Log($"Relay outcome: pre-release={beforeReleasePlan}, pending-pause={pendingPause}, final-plan={manager.CurrentSession?.PlanId}, native-paused={mpv.IsPaused}, managed-paused={service.IsPaused}, errors={errors}, recoveries={wire.Recoveries}.");
            Require(errors == 0 && wire.Recoveries == 0 && manager.CurrentSession?.PlanId == "plan-b", "A retired relay failure affected the accepted replacement plan.");
            Require(mpv.IsPaused && service.IsPaused, "The paused replacement resumed after retiring the outgoing relay.");
            Program.Log("PASS: paused fallback relay replacement retains plan B and pause intent without recovering an obsolete A failure.");
        }
        finally
        {
            origin.Release.TrySetResult(); Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
            Call(service, "StopDirectStreamProxy"); Call(service, "StopHlsProxy");
            mpv.ScriptMessageReceived -= Observe; mpv.PlaybackError -= Error;
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 300 && !ready(); i++) await Task.Delay(20); Require(ready(), "Relay fixture did not settle."); }
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);

    private sealed class RangeOrigin : IDisposable
    {
        private readonly HttpListener listener = new();
        public string Origin { get; }
        public byte[] Wave { get; }
        public ConcurrentQueue<string> Requests { get; } = new();
        public TaskCompletionSource AEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RecoveryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RangeOrigin()
        {
            using var data = new MemoryStream(); using var writer = new BinaryWriter(data, Encoding.UTF8, true); const int samples = 60 * 8000;
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples); writer.Write(new byte[samples]); writer.Flush(); Wave = data.ToArray();
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; listener.Prefixes.Add(Origin + "/"); listener.Start(); _ = ListenAsync();
        }
        private async Task ListenAsync()
        {
            while (listener.IsListening)
            {
                HttpListenerContext request;
                try { request = await listener.GetContextAsync(); } catch (Exception) when (!listener.IsListening) { break; }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var path = request.Request.Url!.AbsolutePath; var etag = '"' + path.Split('/').Last() + "-480044\"";
                        request.Response.Headers["Accept-Ranges"] = "bytes"; request.Response.Headers["ETag"] = etag;
                        request.Response.ContentType = "audio/wav";
                        if (request.Request.Headers["If-Match"] is string match && match != etag) { request.Response.StatusCode = 412; request.Response.Close(); return; }
                        if (request.Request.Headers["If-None-Match"] == etag) { request.Response.StatusCode = 304; request.Response.Close(); return; }
                        long from = 0, to = Wave.Length - 1;
                        var range = request.Request.Headers["Range"];
                        if (range != null && (request.Request.Headers["If-Range"] is not string ifRange || ifRange == etag))
                        {
                            var pair = range[6..].Split('-');
                            if (pair[0].Length == 0) from = Math.Max(0, Wave.Length - long.Parse(pair[1]));
                            else { from = long.Parse(pair[0]); if (pair.Length > 1 && long.TryParse(pair[1], out var end)) to = Math.Min(to, end); }
                            if (from >= Wave.Length || from > to) { request.Response.StatusCode = 416; request.Response.Headers["Content-Range"] = $"bytes */{Wave.Length}"; request.Response.Close(); return; }
                            request.Response.StatusCode = 206; request.Response.Headers["Content-Range"] = $"bytes {from}-{to}/{Wave.Length}";
                        }
                        request.Response.ContentLength64 = to - from + 1;
                        Requests.Enqueue($"{request.Request.HttpMethod} {path} range={range ?? "none"} status={request.Response.StatusCode} bytes={from}-{to} length={to - from + 1}");
                        if (request.Request.HttpMethod != "HEAD")
                        {
                            (path.Contains("plan-a", StringComparison.Ordinal) ? AEntered : path.Contains("plan-b", StringComparison.Ordinal) ? BEntered : RecoveryEntered).TrySetResult();
                            await Release.Task;
                            await request.Response.OutputStream.WriteAsync(Wave.AsMemory((int)from, (int)(to - from + 1)));
                        }
                        request.Response.Close();
                    }
                    catch (Exception ex) when (!listener.IsListening || ex is IOException or HttpListenerException) { }
                });
            }
        }
        public void Dispose() { Release.TrySetResult(); listener.Close(); }
    }

    private sealed class Wire(string origin) : HttpMessageHandler
    {
        public ConcurrentQueue<string> Operations { get; } = new();
        public int Recoveries;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "relay-load.invalid") throw new InvalidOperationException("Fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("/settings/", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            string plan, operation;
            if (path.EndsWith("/start")) { plan = "plan-a"; operation = "start"; }
            else if (path.EndsWith("/replan"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); operation = body.RootElement.GetProperty("operation").GetString()!;
                plan = operation == "quality_change" ? "plan-b" : "recovery-" + Interlocked.Increment(ref Recoveries);
            }
            else return Json("""{"outcome":"applied"}""");
            Operations.Enqueue(operation + " => " + plan);
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "fixture",
                playback_plan = new
                {
                    plan_id = plan, plan_attempt_key = plan + "-key", session_id = "fixture", effective_media_file_id = 42,
                    delivery = "original_http", stream = new { url = origin + "/relay/original/" + plan + ".wav" },
                    timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { audio = new { id = "file:42:audio:0", index = 0 } }, subtitle = new { mode = "off", inventory = Array.Empty<object>() },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
