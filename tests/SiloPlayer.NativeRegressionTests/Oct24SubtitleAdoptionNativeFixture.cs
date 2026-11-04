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

            // The old menu's English row is index 0, but the replacement's
            // index 0 is Spanish. Refuse it both before and after adoption.
            var preferencesBeforeSwitch = wire.PreferenceWrites;
            Set(service, "_switchingContent", true);
            Set(service, "_subtitleVersionSwitchActive", true);
            Require(!await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!,
                "The outgoing menu was accepted while a version switch was preparing.");
            Require(manager.SubtitleSelectionForLoad == 1 && wire.PreferenceWrites == preferencesBeforeSwitch,
                "A refused outgoing-menu choice changed subtitle intent or preferences.");
            // A previously reserved raw index must not outrank rebinding by
            // identity: index 1 means English on version 44, not Spanish.
            Set(service, "_pendingInitialServerSubtitleIndex", 1);
            Set(service, "_pendingInitialSubtitleFileId", 42);
            await manager.StartReplacementSessionAsync(44, 12);
            Require(!await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)!,
                "The outgoing menu was interpreted against the newly adopted file.");
            await Load(0, 12);
            Require(Get(service, "_pendingInitialServerSubtitleIndex") == null && wire.PreferenceWrites == preferencesBeforeSwitch,
                "A foreign pending index survived the load or persisted a different-language preference.");
            await manager.StartReplacementSessionAsync(42, 12); await Load(1, 12);
            await manager.StartReplacementSessionAsync(43, 13); await Load(-1, 13);
            await manager.StartReplacementSessionAsync(42, 14); await Load(1, 14);
            await manager.StartReplacementSessionAsync(44, 15); await Load(0, 15);
            await service.SetSubtitleTrackAndPersistAsync(0, null, null);
            await manager.ReplanQualityAsync("360p", 16); await Load(-1, 16);
            Require(wire.Replans.Any(p => p.GetProperty("operation").GetString() == "track_change" &&
                !p.GetProperty("selected_tracks").TryGetProperty("subtitle", out _)), "Explicit Off never reached the server.");
            Require(subtitles.Requests > 0, "Native player never fetched the served subtitle bytes.");
            Require(wire.PreferenceWrites == 2, "Automatic version fallback/adoption saved a viewer preference.");

            // Model the next-item boundary: PlayCoreAsync invalidates the old
            // menu, and StartSessionAsync (not replacement) resets track intent.
            // Old index 1 is Spanish on episode A but English on episode B.
            await manager.StartSessionAsync(42); await Load(-1, 0);
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            Property(service, "ContentId", "next-episode");
            manager.UseWatchDetail(new() { ContentId = "next-episode", SeriesId = "series", Versions = [Version(44)] });
            Property(service, "Versions", manager.WatchDetail!.Versions);
            Set(service, "_subtitleMenuPending", true);
            Set(service, "_subtitleMenuFileId", null);
            Set(service, "_subtitleVersionSwitchActive", false);
            Set(service, "_switchingContent", true);
            await manager.StartSessionAsync(44);
            Call(service, "ResolveInitialSubtitleSelection", manager.WatchDetail, Version(44), manager.CurrentSession, null, -1, null);
            Require(!await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 1, true)! &&
                manager.SubtitleSelectionForLoad == -1 && Get(service, "_pendingSubtitlePreferenceIndex") == null && wire.PreferenceWrites == 2,
                "A next-item load interpreted the outgoing episode's menu index against the new inventory.");
            await Load(-1, 0);
            await Until(() => Get(service, "_loadedMediaInitCts") == null && !(bool)Get(service, "_subtitleMenuPending")!);
            Require(!(bool)Get(service, "_subtitleMenuPending")!, "Publishing the next episode's inventory did not enable its menu.");
            Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)! &&
                service.ActiveSubtitleServerIndex == 0 && wire.PreferenceWrites == 3 &&
                manager.CurrentSession!.SubtitleUrls.Single(t => t.Index == 0).Language == "spa",
                "The newly published next-item menu did not select its own Spanish row.");

            manager.WatchDetail!.Versions.Add(Version(42));
            Property(service, "Versions", manager.WatchDetail.Versions);
            foreach (var entry in new[] { "overlay", "osc", "cast" })
            {
                var currentSpanish = manager.CurrentSession!.SubtitleUrls.Single(t => t.Language == "spa");
                await service.SetSubtitleTrackAndPersistAsync(currentSpanish.Index + 1, currentSpanish.Language, currentSpanish);
                var writes = wire.PreferenceWrites;
                var oldSid = mpv.GetPropertyDouble("sid");
                Set(service, "_switchingContent", true);
                Set(service, "_subtitleVersionSwitchActive", true);
                if (entry == "overlay") await service.SetSubtitleTrackAndPersistAsync(0, null, null);
                else if (entry == "osc")
                    Require(await (Task<bool>)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!,
                        "OSC Off was refused during a version switch.");
                else await service.ApplySiloCastControlAsync(new() { ["name"] = "select_subtitle_track" });
                Require(manager.SubtitleSelectionForLoad == -1 && wire.PreferenceWrites == writes && mpv.GetPropertyDouble("sid") == oldSid,
                    "Off during a version switch was not recorded and deferred without a premature preference write.");
                var nextFile = manager.CurrentSession.MediaFileId == 44 ? 42 : 44;
                await manager.StartReplacementSessionAsync(nextFile, 12);
                await Load(-1, 12);
                await Until(() => Get(service, "_loadedMediaInitCts") == null && wire.PreferenceWrites == writes + 1);
                Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                    wire.PreferenceWrites == writes + 1 && Get(service, "_pendingSubtitlePreferenceIndex") == null,
                    "The replacement did not execute Off and save its preference exactly once.");
            }
            // A local CC cycle is explicit viewer intent, but not a saved
            // preference change. Cycled Off must survive the next Auto grant.
            await manager.StartSessionAsync(42, subtitleTrackIndex: 0);
            var automaticWatch = new WatchDetailResponse { EffectiveSubtitleMode = "always", EffectiveSubtitleLanguage = "eng" };
            Call(service, "ResolveInitialSubtitleSelection", automaticWatch, Version(42), manager.CurrentSession, null, null, null);
            await Load(0, 0);
            await Until(() => Get(service, "_loadedMediaInitCts") == null);
            var cycleWrites = wire.PreferenceWrites;
            var cycleReplans = wire.Replans.Count;
            Require(manager.IsSubtitleSelectionAutomatic, "Cycle fixture did not establish startup Auto.");
            Call(service, "CycleSubtitleBackward");
            Require(service.ActiveSubtitleServerIndex == -1 && mpv.GetPropertyDouble("sid") <= 0 &&
                !manager.IsSubtitleSelectionAutomatic && !manager.CaptureLocalSubtitleIntent().IsAutomatic &&
                manager.SubtitleSelectionForLoad == -1 && wire.PreferenceWrites == cycleWrites,
                "The local caption cycle did not record explicit Off without saving a preference.");
            wire.AutomaticSubtitleSelection = 0;
            await manager.StartReplacementSessionAsync(44, 0);
            Require(wire.Replans.Count == cycleReplans + 1 && manager.SubtitleSelectionForLoad == -1 &&
                !manager.IsSubtitleSelectionAutomatic &&
                wire.Replans[^1].GetProperty("operation").GetString() == "track_change" &&
                !wire.Replans[^1].GetProperty("selected_tracks").TryGetProperty("subtitle", out _) &&
                manager.CurrentSession!.SubtitleMode == "off",
                "A version switch lost cycled explicit Off to the new file's Auto grant.");
            await Load(-1, 0);
            Require(wire.PreferenceWrites == cycleWrites && mpv.IsPaused,
                "Local caption cycling/version adoption saved a preference or resumed playback.");
            Program.Log("PASS: actual libmpv sid and PlayerService adopt subtitle identity across quality/version changes, refuse outgoing next-item indices until publication, retain explicit local caption cycling across Auto grants and persist only executed viewer choices.");
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
        public int? AutomaticSubtitleSelection;
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
                _subtitle = body.TryGetProperty("subtitle_track_index", out var initial) ? initial.GetInt32()
                    : AutomaticSubtitleSelection ?? (_file == 42 && Replans.Count == 0 ? 0 : -1);
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
