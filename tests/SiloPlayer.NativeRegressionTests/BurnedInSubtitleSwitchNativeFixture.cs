using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;

/// <summary>
/// A DVD/DVB track is composited into the video by the server (v3 plan
/// subtitle mode "burn_in"). Turning subtitles off, or choosing a text track,
/// must replan without it through the actual PlayerService caller, and a text
/// pick must survive a later quality reload.
/// </summary>
internal static class BurnedInSubtitleSwitchNativeFixture
{
    private const int English = 0, Spanish = 1, Dvd = 2;

    internal static async Task RunAsync(StackPanel parent)
    {
        using var media = new MediaServer(Path.Combine(Program.ResultDirectory, "burned-in-subtitle.wav"));
        using var wire = new Wire(media.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://burn-in.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "burned-in-subtitle")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] });
        await manager.StartSessionAsync(42, subtitleTrackIndex: Dvd);
        Require(manager.CurrentSession!.IsSubtitleBurnedIn, "Fixture did not start with a burned-in DVD subtitle.");
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        Call(service, "WireMpvEvents");
        try
        {
            var lifetimeCase = Environment.GetEnvironmentVariable("SILO_NATIVE_BURN_FAILURE");
            if (lifetimeCase is "queued-cancel" or "queued-owner")
            {
                var gate = (SemaphoreSlim)Get(service, "_streamRecoveryGate")!;
                using var lifetime = new CancellationTokenSource();
                await gate.WaitAsync();
                try
                {
                    var queued = (Task)Call(service, "RunStreamRecoveryAsync", 0d, "subtitle-switch-failed", manager, false, lifetime.Token)!;
                    if (lifetimeCase == "queued-cancel")
                    {
                        lifetime.Cancel();
                        await queued.WaitAsync(TimeSpan.FromSeconds(2));
                        Require(gate.CurrentCount == 0, "A canceled waiter released another recovery's ownership.");
                    }
                    else
                    {
                        Set(service, "_playbackManager", null);
                        gate.Release();
                        await queued.WaitAsync(TimeSpan.FromSeconds(2));
                        await gate.WaitAsync();
                    }
                    Require(wire.Replans.Count == 0, "A canceled or retired queued recovery replanned playback.");
                    Program.Log($"PASS: subtitle recovery {lifetimeCase} retires without changing playback or releasing another owner's gate.");
                    return;
                }
                finally { gate.Release(); }
            }
            // Off while burned in: the caller must replan, not only clear mpv's sid.
            await Loaded(() => service.SetSubtitleTrackAndPersistAsync(0, null, null));
            Require(wire.Replans.Count == 1 && IsTrackChangeWithoutSubtitle(wire.Replans[0]),
                "Off on a burned-in subtitle did not replan without the subtitle.");
            Require(!manager.CurrentSession!.IsSubtitleBurnedIn, "The burned-in subtitle stayed in the plan after Off.");

            // Burn the DVD track in again through the actual caller.
            var dvd = manager.CurrentSession.SubtitleUrls.Single(t => t.Index == Dvd);
            await Loaded(() => service.SetSubtitleTrackAndPersistAsync(Dvd + 1, dvd.Language, dvd));
            Require(manager.CurrentSession!.IsSubtitleBurnedIn, "Choosing the DVD track did not burn it in.");
            // The reload's post-load selection reselects the burned-in track; it
            // must not replan again (each replan reloads and reselects it).
            var replansAfterBurn = wire.Replans.Count;
            await Task.Delay(1500);
            Require(wire.Replans.Count == replansAfterBurn,
                $"Burn-in reloaded in a loop: {wire.Replans.Count - replansAfterBurn} further replans after the reload.");

            // Text while burned in: the server grants text instead of burn-in,
            // then native selection follows the accepted plan after reload.
            var spanish = manager.CurrentSession.SubtitleUrls.Single(t => t.Index == Spanish);
            var failureMode = Environment.GetEnvironmentVariable("SILO_NATIVE_BURN_FAILURE");
            var failSwitch = !string.IsNullOrEmpty(failureMode);
            var startPaused = failureMode?.Contains("paused-", StringComparison.Ordinal) == true;
            var selectOff = failureMode?.EndsWith("off", StringComparison.Ordinal) == true;
            var overlapRecovery = failureMode?.StartsWith("overlap-", StringComparison.Ordinal) == true;
            if (failSwitch)
            {
                service.SetPaused(startPaused);
                await Until(() => mpv.IsPaused == startPaused && service.IsPaused == startPaused);
                wire.FailNextTrackChange = true;
            }
            var replansBeforeText = wire.Replans.Count;
            SemaphoreSlim? heldRecovery = null;
            try
            {
                await Loaded(async () =>
                {
                    if (overlapRecovery)
                    {
                        heldRecovery = (SemaphoreSlim)Get(service, "_streamRecoveryGate")!;
                        await heldRecovery.WaitAsync();
                    }
                    await (selectOff
                        ? service.SetSubtitleTrackAndPersistAsync(0, null, null)
                        : service.SetSubtitleTrackAndPersistAsync(Spanish + 1, spanish.Language, spanish));
                    if (heldRecovery != null) { heldRecovery.Release(); heldRecovery = null; }
                });
            }
            finally { heldRecovery?.Release(); }
            if (failSwitch)
            {
                var expected = selectOff ? -1 : Spanish;
                await Until(() => !service.IsLoading);
                Require(!manager.CurrentSession!.IsSubtitleBurnedIn, "Failure recovery retained the outgoing burned-in subtitle.");
                Require(manager.SubtitleSelectionForLoad == expected,
                    "Failure recovery lost the viewer's requested subtitle selection.");
                await Until(() => service.ActiveSubtitleServerIndex == expected &&
                    (selectOff ? mpv.GetPropertyDouble("sid") <= 0 : NativeSidMatches(service, mpv, Spanish)));
                await Until(() => mpv.IsPaused == startPaused && service.IsPaused == startPaused);
                Require(mpv.IsPaused == startPaused && service.IsPaused == startPaused,
                    "Failure recovery used the helper's temporary pause instead of viewer intent.");
                Require(selectOff ? mpv.GetPropertyDouble("sid") <= 0 : NativeSidMatches(service, mpv, Spanish),
                    "Recovered native subtitle selection does not match the accepted plan.");
                Program.Log($"Subtitle recovery {failureMode}: next-load={manager.SubtitleSelectionForLoad}, active={service.ActiveSubtitleServerIndex}, mode={manager.CurrentSession?.SubtitleMode}, native paused={mpv.IsPaused}, viewer paused={service.IsPaused}.");
                Program.Log("PASS: failed burned-in subtitle switch retains track and pause intent through actual native recovery.");
                return;
            }
            Require(wire.Replans.Count == replansBeforeText + 1 && SelectedSubtitle(wire.Replans[^1]) == Spanish,
                "Text on a burned-in subtitle did not replan to the requested text track.");
            await Until(() => service.ActiveSubtitleServerIndex == Spanish && NativeSidMatches(service, mpv, Spanish));
            Require(manager.SubtitleSelectionForLoad == Spanish,
                $"The Off replan replaced the text pick as the viewer's choice (next load: {manager.SubtitleSelectionForLoad}).");

            // A later quality reload must carry and restore the text pick.
            await Loaded(() => (Task)Call(service, "SwitchQualityTierAsync", "480p", null, null)!);
            Require(SelectedSubtitle(wire.Replans[^1]) == Spanish, "The quality replan dropped the text subtitle.");
            await Until(() => service.ActiveSubtitleServerIndex == Spanish && NativeSidMatches(service, mpv, Spanish));
            Require(media.SubtitleRequests > 0, "Native player never fetched the served subtitle bytes.");
            Program.Log("PASS: actual PlayerService replans a burned-in DVD subtitle away for Off and for a text track, and the text pick survives a quality reload with a matching native sid.");
        }
        finally
        {
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket");
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }

        async Task Loaded(Func<Task> action)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready() => loaded.TrySetResult(); service.ContentLoaded += Ready;
            try
            {
                await action();
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8));
                Call(service, "StopPlaybackStallWatchdog");
            }
            finally { service.ContentLoaded -= Ready; }
        }
    }

    private static bool IsTrackChangeWithoutSubtitle(JsonElement replan)
        => replan.GetProperty("operation").GetString() == "track_change" && SelectedSubtitle(replan) < 0;

    private static int SelectedSubtitle(JsonElement replan)
        => replan.GetProperty("selected_tracks").TryGetProperty("subtitle", out var subtitle) &&
            subtitle.ValueKind == JsonValueKind.Object ? subtitle.GetProperty("index").GetInt32() : -1;

    private static bool NativeSidMatches(PlayerService service, MpvPlayer mpv, int expected)
    {
        var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
        lock (Get(service, "_subtitleTrackStateLock")!)
            return map.TryGetValue(expected, out var sid) && mpv.GetPropertyDouble("sid") == sid && sid > 0;
    }

    private static FileVersion Version() => new()
    {
        FileId = 42, Duration = 60,
        AudioTracks = [new() { Language = "en", Default = true }],
        SubtitleTracks =
        [
            new() { Index = English, Language = "eng", Codec = "srt", External = true },
            new() { Index = Spanish, Language = "spa", Codec = "srt", External = true },
            new() { Index = Dvd, Language = "eng", Codec = "dvd_subtitle" },
        ],
    };

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(20); Require(ready(), "Native subtitle state did not settle."); }
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);

    /// <summary>Loopback origin for the plan's stream (a silent WAV) and the text sidecars.</summary>
    private sealed class MediaServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Origin { get; }
        public int SubtitleRequests;
        public MediaServer(string wavePath)
        {
            const int samples = 60 * 8000;
            using (var writer = new BinaryWriter(File.Create(wavePath)))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples); writer.Write(new byte[samples]);
            }
            var wave = File.ReadAllBytes(wavePath);
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; _listener.Prefixes.Add(Origin + "/"); _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); } catch (Exception) when (!_listener.IsListening) { break; }
                    var isMedia = context.Request.Url!.AbsolutePath == "/media.wav";
                    if (!isMedia) Interlocked.Increment(ref SubtitleRequests);
                    // The origin ignores Range and returns the complete file.
                    var bytes = isMedia ? wave
                        : Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:59,000\nBurned-in subtitle fixture\n");
                    context.Response.ContentType = isMedia ? "audio/wav" : "application/x-subrip";
                    context.Response.ContentLength64 = bytes.Length;
                    try
                    {
                        if (context.Request.HttpMethod != "HEAD") await context.Response.OutputStream.WriteAsync(bytes);
                        context.Response.Close();
                    }
                    catch (HttpListenerException) { /* mpv closed the connection while seeking or reloading. */ }
                }
            });
        }
        public void Dispose() => _listener.Close();
    }

    /// <summary>Synthetic v3 server: track_change and quality_change replans keep the requested subtitle.</summary>
    private sealed class Wire(string origin) : HttpMessageHandler
    {
        private int _subtitle = -1;
        public bool FailNextTrackChange;
        public List<JsonElement> Replans = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "burn-in.invalid") throw new InvalidOperationException("Burn-in fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("-prefs/", StringComparison.Ordinal)) return Json("{}");
            if (path.Contains("/settings/", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start") && !path.EndsWith("/replan")) return Json("""{"outcome":"applied"}""");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = doc.RootElement;
            if (path.EndsWith("/start"))
                _subtitle = body.TryGetProperty("subtitle_track_index", out var initial) && initial.ValueKind == JsonValueKind.Number ? initial.GetInt32() : -1;
            else
            {
                Replans.Add(body.Clone());
                if (FailNextTrackChange && body.GetProperty("operation").GetString() == "track_change")
                {
                    FailNextTrackChange = false;
                    throw new InvalidOperationException("Synthetic subtitle replan failure before adoption.");
                }
                _subtitle = SelectedSubtitle(body);
            }
            var burned = _subtitle == Dvd;
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "fixture",
                playback_plan = new
                {
                    plan_id = $"plan-{Replans.Count}", plan_attempt_key = "key", session_id = "fixture", effective_media_file_id = 42,
                    delivery = "original_http", stream = new { url = origin + "/media.wav" }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { audio = new { id = "file:42:audio:0", index = 0 }, subtitle = _subtitle < 0 ? null : new { id = $"sub:42:{_subtitle}", index = _subtitle } },
                    subtitle = new
                    {
                        mode = burned ? "burn_in" : _subtitle < 0 ? "off" : "render",
                        track_id = _subtitle < 0 ? null : $"sub:42:{_subtitle}",
                        inventory = new object[]
                        {
                            new { track_id = "sub:42:0", combined_index = English, language = "eng", codec = "srt", source = "external", url = origin + "/eng.srt", label = "English", delivery = "sidecar" },
                            new { track_id = "sub:42:1", combined_index = Spanish, language = "spa", codec = "srt", source = "external", url = origin + "/spa.srt", label = "Spanish", delivery = "sidecar" },
                            new { track_id = "sub:42:2", combined_index = Dvd, language = "eng", codec = "dvd_subtitle", source = "embedded", label = "English (DVD)", delivery = "burn_in_only" },
                        },
                    },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
