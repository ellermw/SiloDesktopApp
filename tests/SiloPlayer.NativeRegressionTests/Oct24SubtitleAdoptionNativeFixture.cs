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

internal static class Oct24SubtitleAdoptionNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        using var subtitles = new SubtitleServer();
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://oct24-adoption.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "oct24-subtitle-adoption")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "episode", SeriesId = "series", Versions = [Version(42), Version(43), Version(44)] });
        await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "episode");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        manager.WatchDetail.EffectiveSubtitleLanguage = "en";
        manager.WatchDetail.EffectiveSubtitleMode = "auto";
        manager.WatchDetail.EffectiveShowForcedSubtitles = false;
        Call(service, "ResolveInitialSubtitleSelection", manager.WatchDetail, manager.WatchDetail.Versions[0], manager.CurrentSession, null, null, null);
        Require((int?)Get(service, "_pendingInitialServerSubtitleIndex") == -1,
            "Startup Auto did not compare the actual granted English audio; it used the old Japanese preview default.");
        Set(service, "_pendingInitialServerSubtitleIndex", null); Set(service, "_pendingSubtitleSelection", null);
        manager.RecordLocalSubtitleSelection(0);
        var wave = Path.Combine(Program.ResultDirectory, "oct24-subtitle-adoption.wav");
        using (var writer = new BinaryWriter(File.Create(wave)))
        {
            const int samples = 60 * 8000;
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples); writer.Write(new byte[samples]);
        }
        Call(service, "WireMpvEvents");
        try
        {
            await Load(0, 0);
            var spanish = manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 1);
            await service.SetSubtitleTrackAndPersistAsync(2, "spa", spanish);
            await Until(() => service.ActiveSubtitleServerIndex == 1 && mpv.GetPropertyDouble("sid") > 0);
            service.SetPaused(true);
            await manager.ReplanQualityAsync("480p-low", 12);
            Require(wire.Replans.Last().GetProperty("selected_tracks").GetProperty("subtitle").GetProperty("id").GetString() == "sub:42:1", "Actual local subtitle pick did not reach quality replan.");
            await Load(1, 12);
            Require(service.IsPaused && mpv.GetPropertyFlag("pause") && Math.Abs(mpv.Position - 12) < 2, "Quality reload changed pause or canonical position.");
            await manager.StartReplacementSessionAsync(43, 13); await Load(-1, 13);
            await manager.StartReplacementSessionAsync(42, 14); await Load(1, 14);
            await manager.StartReplacementSessionAsync(44, 15); await Load(0, 15);
            await service.SetSubtitleTrackAndPersistAsync(0, null, null);
            await manager.ReplanQualityAsync("360p", 16); await Load(-1, 16);
            Require(wire.Replans.Any(p => p.GetProperty("operation").GetString() == "track_change" &&
                !p.GetProperty("selected_tracks").TryGetProperty("subtitle", out _)), "Explicit Off never reached the server.");
            Require(subtitles.Requests > 0, "Native player never fetched the served subtitle bytes.");
            Require(wire.PreferenceWrites == 2, "Automatic version fallback/adoption saved a viewer preference.");
            Program.Log("PASS: actual libmpv sid and PlayerService active index adopt local Spanish through quality, missing-version Off, source restoration, reordered version and explicit Off; canonical position/pause preserved; only the two viewer picks write preferences.");
        }
        finally
        {
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket");
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }

        async Task Load(int expected, double position)
        {
            var type = typeof(PlayerService).GetNestedType("PreparedPlaybackTransport", BindingFlags.NonPublic)!;
            var prepared = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [PlaybackTransportPlanner.Plan("direct", wave), wave, 0d, position, 0d, (double?)60d, true], null)!;
            Set(service, "_requestedMediaFileId", manager.CurrentSession!.MediaFileId);
            Call(service, "ApplyPreparedTransport", prepared);
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ready() => loaded.TrySetResult(); service.ContentLoaded += Ready;
            try
            {
                Call(service, "BeginMpvLoad", prepared, true);
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                await Until(() => service.ActiveSubtitleServerIndex == expected &&
                    (expected < 0 ? mpv.GetPropertyDouble("sid") <= 0 : NativeSidMatches(expected)));
                Call(service, "StopPlaybackStallWatchdog");
            }
            finally { service.ContentLoaded -= Ready; }
        }
        bool NativeSidMatches(int expected)
        {
            var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
            lock (Get(service, "_subtitleTrackStateLock")!)
                return map.TryGetValue(expected, out var sid) && mpv.GetPropertyDouble("sid") == sid && sid > 0;
        }
    }

    private static FileVersion Version(int file)
    {
        var spanishFirst = file == 44;
        return new()
        {
            FileId = file, Duration = 60, EffectiveAudioTrackIndex = 1,
            AudioTracks = [new() { Language = "en", Default = false }, new() { Language = "jpn", Default = true }],
            SubtitleTracks = file == 43 ? [new() { Index = 0, Language = "eng", Codec = "srt", External = true }]
                : [new() { Index = 0, Language = spanishFirst ? "spa" : "eng", Codec = "srt", External = true }, new() { Index = 1, Language = spanishFirst ? "eng" : "spa", Codec = "srt", External = true }],
        };
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(20); Require(ready(), "Native subtitle sid/adoption did not settle."); }
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private sealed class SubtitleServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Origin { get; }
        public int Requests;
        public SubtitleServer()
        {
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; _listener.Prefixes.Add(Origin + "/"); _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext request;
                    try { request = await _listener.GetContextAsync(); } catch (Exception) when (!_listener.IsListening) { break; }
                    Interlocked.Increment(ref Requests);
                    var bytes = Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:59,000\nNative subtitle adoption fixture\n");
                    request.Response.ContentType = "application/x-subrip"; request.Response.ContentLength64 = bytes.Length;
                    await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close();
                }
            });
        }
        public void Dispose() => _listener.Close();
    }
    private sealed class Wire(string origin) : HttpMessageHandler
    {
        private int _file = 42, _subtitle = 0;
        public List<JsonElement> Replans = [];
        public int PreferenceWrites;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "oct24-adoption.invalid") throw new InvalidOperationException("Native adoption fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("-prefs/", StringComparison.Ordinal)) { PreferenceWrites++; return Json("{}"); }
            if (path.Contains("/settings/", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start") && !path.EndsWith("/replan")) return Json("""{"outcome":"applied"}""");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = doc.RootElement;
            if (path.EndsWith("/start"))
            {
                _file = int.Parse(body.GetProperty("file_id").GetString()!);
                _subtitle = body.TryGetProperty("subtitle_track_index", out var initial) ? initial.GetInt32() : _file == 42 && Replans.Count == 0 ? 0 : -1;
            }
            else
            {
                Replans.Add(body.Clone());
                if (body.GetProperty("selected_tracks").TryGetProperty("subtitle", out var selection)) _subtitle = selection.GetProperty("index").GetInt32();
                else if (body.GetProperty("operation").GetString() == "track_change") _subtitle = -1;
            }
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "fixture",
                playback_plan = new
                {
                    plan_id = $"plan-{_file}-{Replans.Count}", plan_attempt_key = "key", session_id = "fixture", effective_media_file_id = _file,
                    delivery = "original_http", stream = new { url = origin + "/media.wav" }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { audio = new { id = $"file:{_file}:audio:0", index = 0 }, subtitle = _subtitle < 0 ? null : new { id = $"sub:{_file}:{_subtitle}", index = _subtitle } },
                    subtitle = new { mode = _subtitle < 0 ? "off" : "sidecar", track_id = _subtitle < 0 ? null : $"sub:{_file}:{_subtitle}", inventory = Version(_file).SubtitleTracks!.Select(t => new { track_id = $"sub:{_file}:{t.Index}", combined_index = t.Index, language = t.Language, codec = t.Codec, source = "external", url = origin + $"/{t.Language}.srt", label = t.Language }) },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
