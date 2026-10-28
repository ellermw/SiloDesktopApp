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

// Exercises PlayCoreAsync itself: a manager-only request test misses the
// catalog-default promotion at the real initial-start call site.
internal static class AutoAudioStartChecks
{
    internal static async Task RunAsync(string results)
    {
        var sample = Environment.GetEnvironmentVariable("SILO_CORE_AUDIO_FIXTURE_PATH")
            ?? throw new InvalidOperationException("Set SILO_CORE_AUDIO_FIXTURE_PATH to synthetic media with two audio tracks.");
        await using var origin = new MediaOrigin(await File.ReadAllBytesAsync(sample));
        foreach (var scenario in new[] { "auto", "explicit", "explicit-first", "remembered", "lower-source" })
            await CheckAsync(results, origin.Url, scenario);
        Console.WriteLine("PASS published PlayerService: real initial Auto omits default audio intent; explicit and remembered selections survive; lower-source accepted ordinal reaches native aid without preference writes.");
    }

    private static async Task CheckAsync(string results, string sample, string scenario)
    {
        using var wire = new Wire(scenario, sample);
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://auto-audio.fixture.invalid");
        using var auth = new AuthService(api, new AuthApi(api), credentialStore: null); auth.SelectProfile("fixture-profile");
        using var service = new PlayerService(new PlaybackApi(api), new CatalogApi(api), auth, api,
            new SettingsService(Path.Combine(results, "auto-audio-" + scenario)), new SettingsApi(api));
        var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Field(service, "_mpv", mpv);
        Call(service, "WireMpvEvents");
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ContentLoaded += () => loaded.TrySetResult();
        int? requestedFile = scenario == "lower-source" ? null : 42;
        // Use the same authoritative, revision-stamped preparation as a real
        // detail page. Hand-built snapshots are deliberately refetched.
        var detail = await service.GetOrFetchWatchDetailAsync("fixture-movie", fileId: requestedFile);
        var play = (Task)Call(service, "PlayCoreAsync", "fixture-movie", true, requestedFile,
            scenario == "explicit" ? (int?)1 : scenario == "explicit-first" ? 0 : null, (int?)-1, null, null, detail, null, null, CancellationToken.None, null)!;
        await play.WaitAsync(TimeSpan.FromSeconds(8));
        if (wire.Starts.Count != 1) throw new InvalidOperationException("Actual initial path did not issue one start: " + service.ErrorMessage);
        var body = wire.Starts[0];
        if (scenario is "explicit" or "explicit-first")
        {
            if (!body.TryGetProperty("audio_track_index", out var picked) || picked.GetInt32() != (scenario == "explicit" ? 1 : 0))
                throw new InvalidOperationException("Actual explicit viewer audio selection was lost.");
        }
        else if (body.TryGetProperty("audio_track_index", out _))
            throw new InvalidOperationException("Actual Auto start promoted the catalog effective index into explicit audio intent.");
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
        var manager = (PlaybackManager)GetField(service, "_playbackManager")!;
        var expectedFile = scenario == "lower-source" ? 43 : 42;
        var expectedIndex = scenario is "lower-source" or "explicit-first" ? 0 : 1;
        if (scenario == "lower-source" && (!body.TryGetProperty("allow_alternate_versions", out var allow) || !allow.GetBoolean()))
            throw new InvalidOperationException("Lower-source case was not an actual adaptable Auto start.");
        if (manager.CurrentSession?.MediaFileId != expectedFile || manager.CurrentSession.AudioTrackIndex != expectedIndex
            || (int)mpv.GetPropertyDouble("aid") != expectedIndex + 1)
            throw new InvalidOperationException("Accepted delivered-source audio selection was not applied to actual libmpv.");
        if (wire.PreferenceWrites != 0) throw new InvalidOperationException("Automatic/capability-selected track was persisted as viewer preference.");
        Call(service, "StopPlaybackStallWatchdog");
    }

    private static FileVersion Version(int id, int effective) => new()
    {
        FileId = id, Duration = 20, Resolution = "720p", Container = "mkv", CodecAudio = "aac", EffectiveAudioTrackIndex = effective,
        AudioTracks = [new() { Language = "eng", Codec = "aac", Default = true, Channels = 2 }, new() { Language = "eng", Codec = "aac", Channels = 2 }],
    };
    private static object? GetField(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Field(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);

    private sealed class Wire(string scenario, string stream) : HttpMessageHandler
    {
        internal List<JsonElement> Starts = [];
        internal int PreferenceWrites;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "auto-audio.fixture.invalid") throw new InvalidOperationException("Auto audio fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("audio-prefs")) PreferenceWrites++;
            if (path.StartsWith("/api/v2/watch/", StringComparison.Ordinal)) return Json(new WatchDetailResponse
            {
                ContentId = "fixture-movie", Type = "movie",
                EffectiveSubtitleMode = "off", EffectiveSubtitleLanguage = "",
                Versions = [Version(42, 1), Version(43, 0)],
            });
            if (path == "/api/v2/playback/capabilities") return Json(new { allowed = true, state = "available", installation_id = "11111111-1111-4111-8111-111111111111", protocol_versions = new[] { 3 }, features = new[] { "fixed_media_file_v1", "buffering_quality_v1" } });
            if (path == "/api/v2/settings/values/effective") return Json(new { settings = Array.Empty<object>() });
            if (path == "/api/v2/playback/start")
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Starts.Add(doc.RootElement.Clone());
                var file = scenario == "lower-source" ? 43 : 42; var index = scenario is "lower-source" or "explicit-first" ? 0 : 1;
                return Json(new
                {
                    protocol_version = 3, outcome = "playable", session_id = "fixture-session",
                    playback_plan = new
                    {
                        protocol_version = 3, plan_id = "plan:fixture", plan_attempt_key = "v3:fixture", session_id = "fixture-session",
                        requested_media_file_id = 42, effective_media_file_id = file, delivery = "original_http",
                        stream = new { url = stream, protocol = "http_progressive" }, timeline = new { can_seek_anywhere = true },
                        source = new { media_file_id = file, duration_seconds = 20 }, effective_recipe = new { audio_codec = "aac", video_codec = "copy" },
                        selected_tracks = new { audio = new { id = $"file:{file}:audio:{index}", index } }, subtitle = new { mode = "off", inventory = Array.Empty<object>() },
                        available_qualities = new[] { new { label = "original", preserves_source = true } }, degradation_warnings = Array.Empty<object>(),
                    },
                });
            }
            return Json(new { outcome = "applied" });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), System.Text.Encoding.UTF8, "application/json") };
    }

    private sealed class MediaOrigin : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly byte[] _media;
        private readonly Task _accept;
        internal string Url { get; }
        internal MediaOrigin(byte[] media)
        {
            _media = media; _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/stream/direct/synthetic-audio";
            _accept = AcceptAsync();
        }
        private async Task AcceptAsync()
        {
            try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = ServeAsync(client); } }
            catch (OperationCanceledException) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            try
            {
                using var stream = client.GetStream(); using var reader = new StreamReader(stream, leaveOpen: true);
                var first = await reader.ReadLineAsync(_stop.Token); long start = 0, end = _media.Length - 1; var range = false;
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                    if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                    {
                        var values = line[13..].Split('-'); range = true;
                        if (!long.TryParse(values[0], out start)) start = 0;
                        if (values.Length > 1 && long.TryParse(values[1], out var last)) end = Math.Min(last, end);
                    }
                if (start < 0 || start >= _media.Length || end < start) return;
                var headers = $"HTTP/1.1 {(range ? "206 Partial Content" : "200 OK")}\r\nContent-Type: video/x-matroska\r\nAccept-Ranges: bytes\r\nContent-Length: {end - start + 1}\r\nConnection: close\r\n";
                if (range) headers += $"Content-Range: bytes {start}-{end}/{_media.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"), _stop.Token);
                if (first?.StartsWith("HEAD ", StringComparison.Ordinal) != true)
                    await stream.WriteAsync(_media.AsMemory((int)start, (int)(end - start + 1)), _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (IOException) { }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _accept; _stop.Dispose(); }
    }
}
