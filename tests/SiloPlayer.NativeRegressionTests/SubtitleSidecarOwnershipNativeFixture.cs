using System.Collections.Concurrent;
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

internal static class SubtitleSidecarOwnershipNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var failures = new List<string>();
        foreach (var scenario in new[] { "newer-off", "newer-track", "reload-gap", "manual-reload-gap", "reload-cleanup", "loaded-file-cleanup", "close", "viewer-pauses", "viewer-resumes", "viewer-seeks", "oversized-chunked", "load-start-retired", "load-start-cached-track", "load-start-off", "loaded-settings-cached-track", "loaded-settings-off", "load-start-retired-bitmap" })
        {
            try
            {
                if (scenario.StartsWith("load-start-", StringComparison.Ordinal) || scenario.StartsWith("loaded-settings-", StringComparison.Ordinal)) await CheckLoadStartAsync(scenario);
                else await CheckAsync(parent, scenario);
                Program.Log("PASS: sidecar ownership " + scenario);
            }
            catch (Exception ex) { failures.Add(scenario + ": " + ex.Message); Program.Log("CASE FAIL: " + scenario + ": " + ex); }
        }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
    }

    private static async Task CheckLoadStartAsync(string scenario)
    {
        using var subtitles = new HeldSubtitleServer(false);
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, scenario)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] });
        await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        var wave = Path.Combine(Program.ResultDirectory, scenario + ".wav"); WriteWave(wave);
        subtitles.ReplacementMedia = await File.ReadAllBytesAsync(wave);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        Call(service, "SendSubtitleListToOsc");
        var seen = new ConcurrentQueue<string>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(string[] args)
        {
            if (args.FirstOrDefault() == "silo-sidecar-probe-ready") ready.TrySetResult();
            if (args.FirstOrDefault() == "silo-sidecar-probe-selection" && args.Length > 1) seen.Enqueue(args[1]);
        }
        void Loaded() => loaded.TrySetResult();
        mpv.ScriptMessageReceived += Observe; service.ContentLoaded += Loaded;
        var script = Path.Combine(Program.ResultDirectory, scenario + "-observe.lua");
        await File.WriteAllTextAsync(script, """
            local mp = require 'mp'
            mp.register_script_message('osc-set-active-subtitle', function(index)
                mp.commandv('script-message', 'silo-sidecar-probe-selection', index)
            end)
            mp.commandv('script-message', 'silo-sidecar-probe-ready')
            """);
        typeof(MpvPlayer).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mpv, [new[] { "load-script", script }]);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Task? pending = null;
        SemaphoreSlim? heldRestart = null;
        try
        {
            var retired = scenario == "load-start-retired";
            var retiredBitmap = scenario == "load-start-retired-bitmap";
            var afterFileLoaded = scenario.StartsWith("loaded-settings-", StringComparison.Ordinal);
            var selectOff = scenario.EndsWith("-off", StringComparison.Ordinal) || retiredBitmap;
            if (retiredBitmap)
            {
                var bitmap = new SubtitleTrackInfo { Index = 2, Language = "eng", Codec = "dvdsub", Source = "embedded" };
                manager.CurrentSession!.SubtitleUrls.Add(bitmap);
                heldRestart = (SemaphoreSlim)Get(service, "_transportRestartGate")!;
                await heldRestart.WaitAsync();
                var restartGeneration = (long)Get(service, "_seekRestartGeneration")!;
                pending = Task.Run(() => service.SetSubtitleTrackAndPersistAsync(3, bitmap.Language, bitmap));
                await Until(() => (long)Get(service, "_seekRestartGeneration")! > restartGeneration);
                // A competing accepted session changes the helper's captured
                // session while its initial cancel-and-drain wait is held.
                await manager.StartSessionAsync(42);
            }
            else if (retired)
            {
                // Run off the fixture dispatcher so a broken synchronous
                // sub-add cannot prevent releasing the held media response.
                pending = Task.Run(() => (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!);
                await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            }
            else
            {
                subtitles.Release.TrySetResult();
                await (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, true)!;
                if (!selectOff) await (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
                // An earlier burn-in/source choice must not outrank the
                // viewer's newer track/Off request during this load.
                Set(service, "_pendingInitialServerSubtitleIndex", 1);
            }
            await Task.Delay(100); seen.Clear();
            var preferencesBefore = wire.PreferenceWrites;
            var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession!, Version(),
                subtitles.Origin + "/replacement.wav", 0d, CancellationToken.None)!;
            await prepare;
            var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
            Call(service, "ApplyPreparedTransport", prepared);
            Call(service, "WireMpvEvents");
            var generationBeforeLoad = (long)Get(service, "_subtitleSidecarGeneration")!;
            Call(service, "BeginMpvLoad", prepared, false);
            await subtitles.MediaEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Require(!loaded.Task.IsCompleted && service.IsLoading, "Fixture did not hold the replacement before FILE_LOADED.");
            await Until(() => mpv.IsPaused);
            if (afterFileLoaded)
            {
                wire.HoldSettings = true;
                subtitles.MediaRelease.TrySetResult();
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
                await wire.SettingsEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(!service.IsLoading && (int?)Get(service, "_pendingInitialServerSubtitleIndex") == 1,
                    "Fixture missed the loaded/settings-await pending-choice interval.");
                await Task.Delay(100); seen.Clear();
            }
            if (retiredBitmap)
            {
                heldRestart!.Release(); heldRestart = null;
                await pending!.WaitAsync(TimeSpan.FromSeconds(4));
                await Task.Delay(100);
                pending = (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!;
            }
            else if (retired) subtitles.Release.TrySetResult();
            else if (!selectOff)
            {
                var english = manager.CurrentSession!.SubtitleUrls!.Single(track => track.Index == 0);
                pending = Task.Run(() => service.SetSubtitleTrackAndPersistAsync(1, english.Language, english));
            }
            else pending = Task.Run(() => (Task)Call(service, "SelectSubtitleByServerIndexAsync", -1, true)!);
            await Task.WhenAny(pending!, Task.Delay(400));
            var earlyRegistered = retired && ((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count != 0;
            var earlyPublication = seen.ToArray();
            var pausedWhileLoading = mpv.IsPaused;
            subtitles.MediaRelease.TrySetResult();
            wire.SettingsRelease.TrySetResult();
            await pending!.WaitAsync(TimeSpan.FromSeconds(6));
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(6));
            Call(service, "StopPlaybackStallWatchdog");
            var selected = selectOff ? -1 : 0;
            await Until(() => (long)Get(service, "_subtitleSidecarGeneration")! > generationBeforeLoad &&
                Get(service, "_loadedMediaInitCts") == null);
            var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
            var selectedNative = selected < 0 ? mpv.GetPropertyDouble("sid") <= 0 :
                map.TryGetValue(selected, out var sid) && mpv.GetPropertyDouble("sid") == sid;
            Program.Log($"Load-start {scenario}: early-cache={earlyRegistered}, early-OSC={string.Join(',', earlyPublication)}, " +
                $"active={service.ActiveSubtitleServerIndex}, next-load={manager.SubtitleSelectionForLoad}, sid={mpv.GetPropertyDouble("sid")}, prefs={wire.PreferenceWrites - preferencesBefore}.");
            Require(!earlyRegistered, "A pre-load sidecar registered after BeginMpvLoad retired its owner.");
            if (!afterFileLoaded)
            {
                Require(earlyPublication.Length == 0, "A subtitle completion published before the replacement FILE_LOADED: " + string.Join(',', earlyPublication));
                Require(pausedWhileLoading, "Subtitle selection resumed the replacement before FILE_LOADED.");
            }
            Require(service.ActiveSubtitleServerIndex == selected && manager.SubtitleSelectionForLoad == selected && selectedNative,
                "The replacement FILE_LOADED overwrote the latest viewer track/Off selection.");
            Require(wire.PreferenceWrites - preferencesBefore == (retiredBitmap ? 2 : 1), "The current viewer preference was lost or written twice.");
            await Until(() => !mpv.IsPaused);
        }
        finally
        {
            subtitles.Release.TrySetResult(); subtitles.MediaRelease.TrySetResult();
            wire.SettingsRelease.TrySetResult();
            heldRestart?.Release();
            if (pending != null) try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "CancelPendingFileLoadTimeout");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
            mpv.ScriptMessageReceived -= Observe; service.ContentLoaded -= Loaded;
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static async Task CheckAsync(StackPanel parent, string scenario)
    {
        using var subtitles = new HeldSubtitleServer(scenario == "oversized-chunked");
        using var wire = new Wire(subtitles.Origin); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://sidecar-ownership.invalid"); api.SetProfile("fixture");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, scenario)), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        manager.UseWatchDetail(new() { ContentId = "movie", Versions = [Version()] });
        await manager.StartSessionAsync(42);
        using var mpv = new MpvPlayer(); mpv.Initialize(16, 16); mpv.SetProperty("ao", "null"); mpv.SetProperty("video", "no");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "movie");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        var wave = Path.Combine(Program.ResultDirectory, scenario + ".wav"); WriteWave(wave);
        Set(service, "_activeTransportPlan", PlaybackTransportPlanner.Plan("direct", wave));
        mpv.LoadFile(wave); await Until(() => mpv.GetPropertyDouble("duration") > 0);
        var startPaused = scenario == "viewer-resumes";
        service.SetPaused(startPaused); await Until(() => mpv.IsPaused == startPaused);
        // Prime the same-file lifecycle, so the replacement initializer's settings
        // await genuinely precedes its next LoadSubtitles generation change.
        Call(service, "SendSubtitleListToOsc");
        var seen = new ConcurrentQueue<string>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(string[] args)
        {
            if (args.FirstOrDefault() == "silo-sidecar-probe-ready") ready.TrySetResult();
            if (args.FirstOrDefault() == "silo-sidecar-probe-selection" && args.Length > 1) seen.Enqueue(args[1]);
        }
        mpv.ScriptMessageReceived += Observe;
        var script = Path.Combine(Program.ResultDirectory, scenario + "-observe.lua");
        await File.WriteAllTextAsync(script, """
            local mp = require 'mp'
            mp.register_script_message('osc-set-active-subtitle', function(index)
                mp.commandv('script-message', 'silo-sidecar-probe-selection', index)
            end)
            mp.commandv('script-message', 'silo-sidecar-probe-ready')
            """);
        typeof(MpvPlayer).GetMethod("Command", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(mpv, [new[] { "load-script", script }]);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
        try
        {
            var persist = scenario != "reload-gap" && scenario != "reload-cleanup";
            Task pending;
            if (scenario == "reload-gap")
            {
                // Run the complete first FILE_LOADED initializer, not merely
                // its subtitle helper, before the replacement load cancels it.
                var owner = new CancellationTokenSource();
                Set(service, "_loadedMediaInitCts", owner);
                Set(service, "_pendingInitialServerSubtitleIndex", 0);
                pending = (Task)Call(service, "InitializeLoadedMediaAsync", Get(service, "_loadedMediaInitGeneration"), owner)!;
            }
            else if (scenario == "oversized-chunked") pending = (Task)Call(service, "PrefetchSubtitleSidecarAsync", manager.CurrentSession!.SubtitleUrls![0])!;
            else pending = (Task)Call(service, "SelectSubtitleByServerIndexAsync", 0, persist)!;
            await subtitles.Entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            if (scenario == "reload-gap")
            {
                // The initial list legitimately highlights its pending choice.
                // Drain it before testing publication by the retired owner.
                await Until(() => seen.Contains("0")); seen.Clear();
            }
            var uiTick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Require(parent.DispatcherQueue.TryEnqueue(() => uiTick.TrySetResult()), "UI queue rejected fixture callback.");
            await uiTick.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var generation = (long)Get(service, "_subtitleSidecarGeneration")!;
            if (scenario is "newer-off" or "newer-track")
            {
                var selected = scenario == "newer-off" ? -1 : 1;
                await ((Task)Call(service, "SelectSubtitleByServerIndexAsync", selected, true)!).WaitAsync(TimeSpan.FromSeconds(4));
                await Until(() => seen.Contains(selected.ToString()));
            }
            else if (scenario is "reload-gap" or "manual-reload-gap")
            {
                wire.HoldSettings = true;
                Call(service, "StartLoadedMediaInitialization");
                await wire.SettingsEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require((long)Get(service, "_subtitleSidecarGeneration")! == generation,
                    "Replacement already reached LoadSubtitles; fixture missed the cancellation gap.");
            }
            else if (scenario == "reload-cleanup") Call(service, "LoadSubtitles");
            else if (scenario == "close") await service.CloseAsync().WaitAsync(TimeSpan.FromSeconds(4));
            else if (scenario == "viewer-seeks")
            {
                mpv.Seek(15);
                await Until(() => mpv.GetPropertyDouble("time-pos") >= 14);
            }
            else if (scenario is "viewer-pauses" or "viewer-resumes")
            {
                service.SetPaused(!startPaused);
                await Until(() => mpv.IsPaused == !startPaused);
            }
            var pauseBeforeCompletion = mpv.IsPaused;
            subtitles.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(200); // Drain real libmpv client/Lua message delivery.
            var observations = seen.ToArray();
            if (scenario is "newer-off" or "newer-track")
            {
                var selected = scenario == "newer-off" ? -1 : 1;
                Require(service.ActiveSubtitleServerIndex == selected, "The superseded sidecar changed the server selection.");
                Require(observations.LastOrDefault() == selected.ToString(),
                    "Superseded A published stale OSC selection: " + string.Join(",", observations));
                var map = (Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!;
                Require(selected < 0 ? mpv.GetPropertyDouble("sid") <= 0 : map.TryGetValue(selected, out var sid) && mpv.GetPropertyDouble("sid") == sid,
                    "Native SID differs from the latest viewer selection.");
                Require(wire.PreferenceWrites == 1, "Superseded A persisted a stale subtitle preference.");
            }
            else if (scenario is "reload-gap" or "manual-reload-gap" or "reload-cleanup" or "close")
            {
                Require(!observations.Contains("0"), "A retired file initializer published an OSC selection: " + string.Join(",", observations));
                Require(((Dictionary<int, int>)Get(service, "_loadedExternalSubtitleSids")!).Count == 0, "A retired file initializer added a native subtitle.");
                Require(((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count == 0, "A retired prefetch registered a temporary file after cleanup.");
                Require(wire.PreferenceWrites == (scenario == "manual-reload-gap" ? 1 : 0),
                    "Retiring a loaded initializer must retain a still-current manual preference only.");
            }
            else if (scenario == "oversized-chunked")
            {
                Require(((Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!).Count == 0,
                    "A chunked response exceeding the 16 MiB cap was registered.");
                Require(((List<string>)Get(service, "_subtitleSyncFiles")!).Count == 0,
                    "The rejected chunked response left a cleanup file.");
            }
            else if (scenario == "loaded-file-cleanup")
            {
                var files = (Dictionary<int, string>)Get(service, "_prefetchedSubtitleFiles")!;
                Require(files.Count == 1 && File.Exists(files[0]), "Successful prefetch did not register its file.");
                var path = files[0]; Call(service, "LoadSubtitles");
                Require(files.Count == 0 && !File.Exists(path), "Same-session file load retained the superseded cached file.");
                Require(!((List<string>)Get(service, "_subtitleSyncFiles")!).Contains(path), "Retired cache stayed in the cleanup registry.");
            }
            else if (scenario == "viewer-seeks")
            {
                Require(mpv.GetPropertyDouble("time-pos") >= 14, "Prefetch completion undid the viewer's newer seek.");
                Require(observations.LastOrDefault() == "0", "The current selection was not published.");
            }
            else
            {
                Require(mpv.IsPaused == pauseBeforeCompletion && mpv.IsPaused == !startPaused, "Prefetch completion undid the viewer's newer pause/resume input.");
                Require(observations.LastOrDefault() == "0", "The current selection was not published.");
            }
        }
        finally
        {
            subtitles.Release.TrySetResult(); wire.SettingsRelease.TrySetResult();
            Call(service, "CancelPendingLoadedMediaInitialization"); Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket"); Call(service, "StopSubtitleSync");
            mpv.ScriptMessageReceived -= Observe;
            Set(service, "_mpv", null); Set(service, "_playbackManager", null);
        }
    }

    private static FileVersion Version() => new()
    {
        FileId = 42, Duration = 60, EffectiveAudioTrackIndex = 0,
        AudioTracks = [new() { Language = "eng", Default = true }],
        SubtitleTracks = [new() { Index = 0, Language = "eng", Codec = "srt", External = true }, new() { Index = 1, Language = "spa", Codec = "srt", External = true }],
    };
    private static void WriteWave(string path)
    {
        using var writer = new BinaryWriter(File.Create(path)); const int samples = 60 * 8000;
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(samples + 36); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(8000); writer.Write((short)1); writer.Write((short)8);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples); writer.Write(new byte[samples]);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 200 && !ready(); i++) await Task.Delay(20); Require(ready(), "Native fixture did not settle."); }
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static object? Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private sealed class HeldSubtitleServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly bool oversized;
        public string Origin { get; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MediaEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MediaRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[] ReplacementMedia { get; set; } = [];
        public HeldSubtitleServer(bool oversized)
        {
            this.oversized = oversized;
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
                        if (request.Request.Url!.AbsolutePath == "/replacement.wav")
                        {
                            MediaEntered.TrySetResult(); await MediaRelease.Task;
                            request.Response.ContentType = "audio/wav"; request.Response.ContentLength64 = ReplacementMedia.Length;
                            await request.Response.OutputStream.WriteAsync(ReplacementMedia); request.Response.Close(); return;
                        }
                        if (request.Request.Url!.AbsolutePath == "/eng.srt") { Entered.TrySetResult(); await Release.Task; }
                        if (oversized)
                        {
                            request.Response.ContentType = "application/x-subrip"; request.Response.SendChunked = true;
                            var chunk = new byte[64 * 1024];
                            for (var i = 0; i < 257; i++) await request.Response.OutputStream.WriteAsync(chunk);
                            request.Response.Close(); return;
                        }
                        var bytes = Encoding.UTF8.GetBytes("1\n00:00:00,000 --> 00:00:59,000\nNative ownership fixture\n");
                        request.Response.ContentType = "application/x-subrip"; request.Response.ContentLength64 = bytes.Length;
                        await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close();
                    }
                    catch (Exception ex) when (!listener.IsListening || ex is IOException or HttpListenerException) { }
                });
            }
        }
        public void Dispose() { Release.TrySetResult(); MediaRelease.TrySetResult(); listener.Close(); }
    }
    private sealed class Wire(string origin) : HttpMessageHandler
    {
        public int PreferenceWrites;
        public bool HoldSettings;
        public TaskCompletionSource SettingsEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SettingsRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri?.Host != "sidecar-ownership.invalid") throw new InvalidOperationException("Fixture attempted external networking.");
            var path = request.RequestUri.AbsolutePath;
            if (path.Contains("-prefs/", StringComparison.Ordinal)) { Interlocked.Increment(ref PreferenceWrites); return Json("{}"); }
            if (path.Contains("/settings/", StringComparison.Ordinal))
            {
                if (HoldSettings) { SettingsEntered.TrySetResult(); await SettingsRelease.Task.WaitAsync(ct); }
                return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            }
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start")) return Json("""{"outcome":"applied"}""");
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "fixture",
                playback_plan = new
                {
                    plan_id = "plan", plan_attempt_key = "key", session_id = "fixture", effective_media_file_id = 42,
                    delivery = "original_http", stream = new { url = origin + "/media.wav" }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { audio = new { id = "file:42:audio:0", index = 0 } },
                    subtitle = new { mode = "off", inventory = Version().SubtitleTracks!.Select(t => new { track_id = $"sub:42:{t.Index}", combined_index = t.Index, language = t.Language, codec = t.Codec, source = "external", url = origin + $"/{t.Language}.srt", label = t.Language }) },
                },
            }));
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
