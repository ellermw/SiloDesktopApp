using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;

internal static class ActualTransportReloadNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(Window owner)
    {
        if (owner.AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(false, false);
        owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        string[] scenarios = ["cover-reload", "menus-response", "menus-output", "pause-response", "pause-output",
            "superseding-seek", "owner-retirement", "navigation", "replan-error",
            "quality-response", "quality-output", "chapters-response", "chapters-output", "party-response", "party-output",
            "resume-response", "resume-output", "failed-pause-next-load", "audio-selection", "replacement-session"];
        var selected = Environment.GetEnvironmentVariable("SILO_NATIVE_RELOAD_CASE");
        if (!string.IsNullOrEmpty(selected)) { Require(scenarios.Contains(selected), "Unknown scenario."); scenarios = [selected]; }
        var results = new List<object>(); var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            try { await CheckAsync(owner, scenario); Program.Log("PASS: actual transport reload " + scenario); results.Add(new { scenario, passed = true }); }
            catch (Exception error)
            {
                Program.Log("FAIL: actual transport reload " + scenario + ": " + error);
                failures.Add(scenario + ": " + error.Message); results.Add(new { scenario, passed = false, error = error.ToString() });
            }
        }
        await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, "actual-reload-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task CheckAsync(Window owner, string scenario)
    {
        using var media = new HeldMedia(); using var wire = new Wire(media.Origin, scenario); using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://reload.fixture.invalid"); api.SetProfile("synthetic");
        using var auth = new AuthService(api, new AuthApi(api)); var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, scenario, "settings")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        var version = new FileVersion { FileId = 42, Duration = 120, Chapters = [new() { Index = 0, Title = "Opening", StartSeconds = 0, EndSeconds = 30 }], AudioTracks = [
            new() { Language = "en", Codec = "aac", Channels = 2, Default = true }, new() { Language = "ja", Codec = "aac", Channels = 2 }] };
        manager.UseWatchDetail(new() { ContentId = "synthetic-reload", Type = "movie", Versions = [version, new() { FileId = 43, Duration = 120, AudioTracks = version.AudioTracks }] });
        await manager.StartSessionAsync(42, audioTrackIndex: 0);
        using var lifetime = new CancellationTokenSource(); using var video = new MpvVideoWindow(); using var mpv = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner)); video.Show();
        mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv); mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
        Set(service, "_mpv", mpv); Set(service, "_videoWindow", video); Set(service, "_playbackManager", manager); Set(service, "_playbackCts", lifetime);
        typeof(PlayerService).GetProperty("State")!.SetValue(service, PlayerState.Expanded);
        typeof(PlayerService).GetProperty("ContentId")!.SetValue(service, "synthetic-reload");
        typeof(PlayerService).GetProperty("Versions")!.SetValue(service, manager.WatchDetail!.Versions);
        Call(service, "WireMpvEvents");
        await ExerciseAsync(service, manager, version, mpv, media, wire, api, scenario);
    }
    private static async Task ExerciseAsync(PlayerService service, PlaybackManager manager, FileVersion version,
        MpvPlayer mpv, HeldMedia media, Wire wire, SiloApiClient api, string scenario)
    {
        var ready = Signal(); var queries = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
        mpv.ScriptMessageReceived += message =>
        {
            if (message.Length > 0 && message[0] == "silo-actual-reload-ready") ready.TrySetResult();
            if (message.Length > 2 && message[0] == "silo-actual-reload-state" && queries.TryRemove(message[1], out var receiver))
            {
                try { using var json = JsonDocument.Parse(message[2]); receiver.TrySetResult(json.RootElement.Clone()); }
                catch (Exception error) { receiver.TrySetException(error); }
            }
        };
        var observer = Path.Combine(Program.ResultDirectory, "actual-reload-observer.lua");
        await File.WriteAllTextAsync(observer, await File.ReadAllTextAsync(Path.Combine(Program.AppDirectory, "libs", "mpv", "scripts", "silo-osc.lua")) + """

            mp.register_script_message('actual-reload-kind', function(kind) state.actual_reload_kind = kind end)
            mp.register_script_message('actual-reload-query', function(request)
                compute_layout()
                render_playback_wait()
                local r = state.layout["btn_" .. (state.actual_reload_kind or "audio")] or {}
                local wait = state.playback_wait_overlay and state.playback_wait_overlay.data or ''
                mp.commandv('script-message', 'silo-actual-reload-state', request, require('mp.utils').format_json({
                    loading=state.playback_loading or false, reloading=state.transport_reloading or false,
                    black=wait:find('1c&H000000&', 1, true) ~= nil, overlay=#wait > 10,
                    scope=state['native_' .. (state.actual_reload_kind or 'audio') .. '_scope'] or '', sequence=(state.native_menu_request or {}).sequence or 0,
                    x=r.cx or 0, y=r.cy or 0, width=r.w or 0}))
            end)
            mp.register_script_message('actual-reload-ensure-visible', function(request)
                show_osc()
                state.current_alpha = 1
                state.last_fade_time = mp.get_time()
                mp.commandv('script-message', 'silo-actual-reload-state', request, require('mp.utils').format_json({visible=true}))
            end)
            mp.commandv('script-message', 'silo-actual-reload-ready')
            """);
        Command(mpv, "load-script", observer); await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
        try
        {
            var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession, version, manager.StreamUrl, 0d, CancellationToken.None)!;
            await prepare; var prepared = prepare.GetType().GetProperty("Result")!.GetValue(prepare);
            Call(service, "ApplyPreparedTransport", prepared); Call(service, "BeginOwnedMpvLoad", prepared, false, null, null);
            await media.Started(0).WaitAsync(TimeSpan.FromSeconds(8)); var startup = await Query("startup");
            Require(startup.GetProperty("loading").GetBoolean() && startup.GetProperty("black").GetBoolean() && !startup.GetProperty("reloading").GetBoolean(), "Actual first owned load lost its opaque first-frame cover.");
            media.Release(0);
            await Until(() => (bool)Get(service, "_sessionHasFramed")! && !service.IsLoading && Get(service, "_loadedMediaInitCts") == null, "First production output/initialization did not complete.");
            Call(service, "StopPlaybackStallWatchdog"); service.SetPaused(false);
            await Until(() => !mpv.IsPaused && !service.IsPaused, "Initial playing intent did not settle.");
            Call(service, "SendAudioTrackListToOsc");
            await UntilAsync(async () => (await Query("published")).GetProperty("scope").GetString()!.Length > 0, "Initial native menu scope did not publish.");
            await OpenAudio("baseline"); Call(Get(service, "_nativeMenu")!, "Input", "cancel");
            await Until(() => Get(service, "_nativeMenu") == null, "Baseline real menu failed to close.");
            await UntilAsync(async () => (await Query("closed")).GetProperty("sequence").GetInt64() == 0, "Baseline menu acknowledgement did not arrive.");
            var firstFile = (long)Get(service, "_playbackFileGeneration")!;
            if (scenario.StartsWith("resume-", StringComparison.Ordinal))
            { service.SetPaused(true); await Until(() => mpv.IsPaused && service.IsPaused, "Initial pause did not settle."); }
            Task? audioSwitch = null;
            if (scenario == "replacement-session")
            {
                await (Task)Call(service, "SwitchVersionCoreAsync", manager.WatchDetail!.Versions[1], null)!;
                await media.Started(3).WaitAsync(TimeSpan.FromSeconds(8));
                var replacement = await Query("replacement-first-output-held");
                Require(replacement.GetProperty("loading").GetBoolean() && replacement.GetProperty("black").GetBoolean() && !replacement.GetProperty("reloading").GetBoolean(), "A new session inherited the former session's framed state.");
                Require(!(bool)Get(service, "_sessionHasFramed")!, "New session was already marked framed.");
                // A late first-output callback while this load has not FILE_LOADED
                // must not attribute the previous transport's frame to the new session.
                ((Action)Get(service, "_mpvPlaybackRestartedHandler")!)();
                Require(!(bool)Get(service, "_sessionHasFramed")!, "Retired output attributed a frame to the replacement session.");
                media.Release(3);
                await Until(() => (bool)Get(service, "_sessionHasFramed")! && !service.IsLoading, "Replacement first output did not frame.");
                return;
            }
            if (scenario == "audio-selection") audioSwitch = (Task)Call(service, "SwitchAudioTrackCoreAsync", 1, null)!;
            else service.SeekTo(10);
            await wire.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Require(service.IsLoading && (bool)Get(service, "_switchingContent")!, "Public remux seek bypassed the actual replan busy path.");
            var held = await Query("held-response");
            // A seek replan owns the spinner for its whole round trip; an audio change claims it at load submission.
            Require(scenario == "audio-selection" || (held.GetProperty("reloading").GetBoolean() && !held.GetProperty("loading").GetBoolean() && !held.GetProperty("black").GetBoolean() && held.GetProperty("overlay").GetBoolean()), "Framed production seek showed no transparent spinner while its replan response was held.");
            if (scenario == "menus-response") await OpenAudio("held-response");
            if (scenario.EndsWith("-response", StringComparison.Ordinal) && scenario is not "menus-response" and not "pause-response" and not "resume-response") await OpenControl(scenario.Split('-')[0], "held-response");
            if (scenario == "audio-selection") await OpenAudio("held-audio-change");
            if (scenario == "resume-response") { service.SetPaused(false); await Until(() => !mpv.IsPaused, "Resume during response did not apply."); }
            if (scenario == "failed-pause-next-load") service.SetPaused(true);
            if (scenario == "pause-response") { service.SetPaused(true); await Until(() => mpv.IsPaused && service.IsPaused, "Pause during replan did not apply."); }
            if (scenario == "superseding-seek") service.SeekTo(20);
            if (scenario == "owner-retirement") api.SetProfile("replacement-synthetic");
            Task? close = scenario == "navigation" ? service.CloseAsync() : null;
            wire.FailFirst = scenario is "replan-error" or "failed-pause-next-load"; wire.ReleaseFirst.TrySetResult();
            if (scenario is "navigation" or "owner-retirement" or "replan-error")
            {
                if (close != null) await close.WaitAsync(TimeSpan.FromSeconds(8));
                await Until(() => Get(service, "_seekRestartCts") == null, "Retired/failed replan did not drain.");
                Require((long)Get(service, "_playbackFileGeneration")! == firstFile, "Retired/failed response started an obsolete native load.");
                var retired = await Query("retired-or-failed");
                Require(!retired.GetProperty("loading").GetBoolean() && !retired.GetProperty("reloading").GetBoolean(), "Retired/failed replan leaked loading state.");
                if (scenario == "navigation") Require(service.State == PlayerState.Idle, "Actual CloseAsync did not retire playback.");
                return;
            }
            if (scenario == "failed-pause-next-load")
            {
                await Until(() => Get(service, "_seekRestartCts") == null, "Failed seek did not drain.");
                service.SetPaused(false); await Until(() => !mpv.IsPaused && !service.IsPaused, "Resume after failed request did not settle.");
                service.SeekTo(20);
            }
            var outputId = scenario is "superseding-seek" or "failed-pause-next-load" ? 2 : 1;
            await media.Started(outputId).WaitAsync(TimeSpan.FromSeconds(8));
            if (scenario == "superseding-seek")
            {
                Require(wire.Positions.SequenceEqual(new[] { 10d, 20d }), "Latest public seek lost its canonical position.");
                Require(!media.WasStarted(1), "Superseded replan started its obsolete media transport.");
            }
            var reload = await Query("held-output");
            Require(reload.GetProperty("reloading").GetBoolean() && !reload.GetProperty("loading").GetBoolean() && !reload.GetProperty("black").GetBoolean() && reload.GetProperty("overlay").GetBoolean(), "Framed production seek did not show a transparent spinner while output was held.");
            if (scenario == "menus-output") await OpenAudio("held-output");
            if (scenario.EndsWith("-output", StringComparison.Ordinal) && scenario is not "menus-output" and not "pause-output" and not "resume-output") await OpenControl(scenario.Split('-')[0], "held-output");
            if (scenario == "resume-output") { service.SetPaused(false); await Until(() => !mpv.IsPaused, "Resume awaiting output did not apply."); }
            if (scenario == "pause-output") { service.SetPaused(true); await Until(() => mpv.IsPaused && service.IsPaused, "Pause awaiting output did not settle."); }
            media.Release(outputId);
            await Until(() => (bool)Get(service, "_transportHasFirstOutput")! && !service.IsLoading && Get(service, "_loadedMediaInitCts") == null, "Continuing production output did not complete.");
            Call(service, "StopPlaybackStallWatchdog"); var cleared = await Query("next-frame");
            Require(!cleared.GetProperty("loading").GetBoolean() && !cleared.GetProperty("reloading").GetBoolean() && !cleared.GetProperty("overlay").GetBoolean(), "Next actual frame did not clear the spinner.");
            if (scenario.StartsWith("pause-", StringComparison.Ordinal)) Require(mpv.IsPaused && service.IsPaused, "Latest viewer pause was overwritten by continuing reload initialization.");
            if (scenario.StartsWith("resume-", StringComparison.Ordinal) || scenario == "failed-pause-next-load") Require(!mpv.IsPaused && !service.IsPaused, "Latest explicit resume was overwritten by a prior pause.");
            if (scenario == "audio-selection")
            {
                await audioSwitch!;
                await Until(() => Get(service, "_nativeMenu") == null, "Accepted audio selection left the reopened menu's obsolete current row alive.");
                await OpenAudio("accepted-audio");
                var choices = (System.Collections.IEnumerable)Get(Get(service, "_nativeMenu")!, "_choices")!;
                var current = choices.Cast<object>().Single(row => (bool)row.GetType().GetProperty("IsCurrent")!.GetValue(row)!);
                Require((string)current.GetType().GetProperty("Id")!.GetValue(current)! == "1", "Reopened Audio menu did not reflect the accepted selection.");
            }
            Require((long)Get(service, "_playbackFileGeneration")! == firstFile + 1, "Seek loaded more than the latest owned transport.");
        }
        finally
        {
            wire.ReleaseFirst.TrySetResult(); media.ReleaseAll(); Call(service, "CancelPendingTransportRestarts");
            Call(service, "CancelPendingFileLoadTimeout"); Call(service, "CancelPendingLoadedMediaInitialization");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "RetireNativeMenus"); Call(service, "UnwireMpvEvents");
            Set(service, "_mpv", null); Set(service, "_videoWindow", null); Set(service, "_playbackManager", null);
        }
        async Task<JsonElement> Query(string label)
        {
            var request = Guid.NewGuid().ToString("N"); var receiver = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queries[request] = receiver; mpv.SendScriptMessage("actual-reload-query", request);
            var result = await receiver.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await File.AppendAllTextAsync(Path.Combine(Program.ResultDirectory, scenario + "-observations.jsonl"), JsonSerializer.Serialize(new { label, lua = result, service.IsLoading, service.IsPaused, mpvPaused = mpv.IsPaused, framed = Get(service, "_sessionHasFramed"), file = Get(service, "_playbackFileGeneration") }) + "\n");
            return result;
        }
        async Task EnsureOscVisible()
        {
            var request = Guid.NewGuid().ToString("N"); var receiver = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queries[request] = receiver; mpv.SendScriptMessage("actual-reload-ensure-visible", request);
            await receiver.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        async Task OpenControl(string kind, string label)
        {
            var slot = typeof(SiloPlayer.App).GetField("_services", BindingFlags.Static | BindingFlags.NonPublic)!;
            var previous = slot.GetValue(null);
            using var services = new ServiceCollection().AddSingleton(api).AddSingleton(service)
                .AddSingleton((AuthService)Get(service, "_authService")!).AddSingleton<WatchTogetherCoordinator>().BuildServiceProvider();
            WatchTogetherCoordinator? coordinator = null;
            using var room = new WatchTogetherRoomViewModel(new PlaybackApi(api), api)
            { RoomId = "reload-room", RoomToken = "fixture", ConnectionState = "connected", Room = new()
                { RoomId = "reload-room", Code = "TEST", Phase = "lobby", SelectedContentId = "different-content", SelfCanManageRoom = true } };
            try
            {
                if (kind == "party")
                {
                    slot.SetValue(null, services); coordinator = services.GetRequiredService<WatchTogetherCoordinator>();
                    coordinator.SetActiveRoom(room); ((DispatcherTimer)Get(coordinator, "_timer")!).Stop();
                }
                Call(service, "SendQualityInfoToOsc"); Call(service, "SendChapterListToOsc"); Call(service, "PublishNativeMenuScopes");
                mpv.SendScriptMessage("actual-reload-kind", kind);
                await UntilAsync(async () => (await Query(kind + "-scope")).GetProperty("scope").GetString()!.Length > 0, "Missing " + kind + " scope.");
                var geometry = await Query(kind + "-geometry"); Require(geometry.GetProperty("width").GetDouble() > 0, "Missing actual " + kind + " opener geometry.");
                var x = geometry.GetProperty("x").GetDouble().ToString(CultureInfo.InvariantCulture);
                var y = geometry.GetProperty("y").GetDouble().ToString(CultureInfo.InvariantCulture);
                await EnsureOscVisible(); mpv.SendScriptMessage("osc-mouse-move", x, y);
                mpv.SendScriptMessage("osc-mouse-down", x, y); mpv.SendScriptMessage("osc-mouse-up", x, y); mpv.SendScriptMessage("osc-video-click", x, y);
                await Until(() => Get(service, "_nativeMenu") is { } menu && ((ListView)Get(menu, "_list")!).XamlRoot != null, "Actual OSC " + kind + " menu rejected the " + label + " continuing reload.");
                Call(Get(service, "_nativeMenu")!, "Input", "cancel");
                await Until(() => Get(service, "_nativeMenu") == null, "Control menu failed to close.");
                await UntilAsync(async () => (await Query("control-ack")).GetProperty("sequence").GetInt64() == 0, "Control menu close acknowledgement missing.");
            }
            finally { coordinator?.ClearActiveRoom(); slot.SetValue(null, previous); mpv.SendScriptMessage("actual-reload-kind", "audio"); }
        }
        async Task OpenAudio(string label)
        {
            var ownerToken = Get(service, "_nativeMenuOwner") is { } ownerObj
                ? (string?)ownerObj.GetType().GetProperty("Token")!.GetValue(ownerObj) ?? "" : "";
            await UntilAsync(async () => (await Query("scope-" + label)).GetProperty("scope").GetString() == ownerToken,
                "Lua native audio scope did not settle to the current C# owner token during " + label + ".");
            var geometry = await Query("menu-" + label); Require(geometry.GetProperty("width").GetDouble() > 0, "Actual Audio opener has no layout.");
            var horizontal = geometry.GetProperty("x").GetDouble().ToString(CultureInfo.InvariantCulture); var vertical = geometry.GetProperty("y").GetDouble().ToString(CultureInfo.InvariantCulture);
            await EnsureOscVisible(); mpv.SendScriptMessage("osc-mouse-move", horizontal, vertical);
            mpv.SendScriptMessage("osc-mouse-down", horizontal, vertical); mpv.SendScriptMessage("osc-mouse-up", horizontal, vertical); mpv.SendScriptMessage("osc-video-click", horizontal, vertical);
            await Until(() => Get(service, "_nativeMenu") is { } menu && ((ListView)Get(menu, "_list")!).XamlRoot != null, "Actual OSC Audio gesture could not open a usable native menu during " + label + ".");
            Require(((ListView)Get(Get(service, "_nativeMenu")!, "_list")!).Items.Count == 2, "Real Audio menu lost its synthetic inventory.");
            if (!label.StartsWith("held-", StringComparison.Ordinal)) return;
            // A continuing reload must say why a track cannot be chosen, never drop the pick silently.
            var audioRows = ((System.Collections.IEnumerable)Get(Get(service, "_nativeMenu")!, "_choices")!).Cast<object>().ToArray();
            Require(audioRows.All(choice => !(bool)choice.GetType().GetProperty("IsEnabled")!.GetValue(choice)! &&
                ((string)choice.GetType().GetProperty("DisabledReason")!.GetValue(choice)!).Length > 0),
                "Audio rows stayed selectable without a reason during " + label + ".");
        }
    }
    private sealed class Wire(string origin, string scenario) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource FirstStarted = Signal(); internal readonly TaskCompletionSource ReleaseFirst = Signal();
        internal readonly ConcurrentQueue<double> Positions = new(); internal bool FailFirst; private int _replans; private int _starts;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri?.Host == "reload.fixture.invalid", "Synthetic API attempted external networking."); var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (path.Contains("/settings/")) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (!path.EndsWith("/start") && !path.EndsWith("/replan")) return Json("""{"outcome":"applied"}""");
            var attempt = 0; var position = 0d;
            if (path.EndsWith("/replan"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); position = body.RootElement.GetProperty("position_seconds").GetDouble(); Positions.Enqueue(position);
                attempt = Interlocked.Increment(ref _replans);
                if (attempt == 1) { FirstStarted.TrySetResult(); await ReleaseFirst.Task; if (FailFirst) return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") }; }
            }
            var sessionId = "reload-session"; var fileId = 42; var audio = scenario == "audio-selection" && attempt > 0 ? 1 : 0;
            if (path.EndsWith("/start") && Interlocked.Increment(ref _starts) > 1)
            { sessionId = "replacement-session"; attempt = 3; fileId = 43; }
            return Json(JsonSerializer.Serialize(new { protocol_version = 3, outcome = "playable", session_id = sessionId, playback_plan = new {
                plan_id = "reload-plan-" + attempt, plan_attempt_key = "synthetic-" + attempt, session_id = sessionId, effective_media_file_id = fileId, delivery = "server_remux_progressive",
                stream = new { url = origin + "/" + attempt + ".y4m" }, timeline = new { can_seek_anywhere = false, timeline_offset_seconds = position }, source = new { duration_seconds = 120 },
                selected_tracks = new { audio = new { id = $"file:{fileId}:audio:{audio}", index = audio }, subtitle = (object?)null }, subtitle = new { mode = "off", inventory = Array.Empty<object>() } } }));
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }
    private sealed class HeldMedia : IDisposable
    {
        private readonly HttpListener _listener = new(); private readonly ConcurrentDictionary<int, TaskCompletionSource> _started = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _release = new(); private readonly byte[] _bytes;
        internal string Origin { get; }
        internal HeldMedia()
        {
            using var clip = new MemoryStream(); clip.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W160 H90 F30:1 Ip A1:1 C420jpeg\n"));
            var pixels = new byte[160 * 90 * 3 / 2]; Array.Fill(pixels, (byte)128);
            for (var frame = 0; frame < 900; frame++) { Array.Fill(pixels, (byte)(32 + frame % 100), 0, 160 * 90); clip.Write(Encoding.ASCII.GetBytes("FRAME\n")); clip.Write(pixels); }
            _bytes = clip.ToArray(); var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = "http://127.0.0.1:" + port; _listener.Prefixes.Add(Origin + "/"); _listener.Start(); _ = Task.Run(AcceptAsync);
        }
        internal Task Started(int attempt) => _started.GetOrAdd(attempt, _ => Signal()).Task;
        internal bool WasStarted(int attempt) => _started.TryGetValue(attempt, out var started) && started.Task.IsCompleted;
        internal void Release(int attempt) => _release.GetOrAdd(attempt, _ => Signal()).TrySetResult();
        internal void ReleaseAll() { for (var attempt = 0; attempt < 8; attempt++) Release(attempt); }
        private async Task AcceptAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context; try { context = await _listener.GetContextAsync(); } catch (Exception) when (!_listener.IsListening) { break; }
                _ = RespondAsync(context);
            }
        }
        private async Task RespondAsync(HttpListenerContext context)
        {
            try
            {
                var attempt = int.Parse(Path.GetFileNameWithoutExtension(context.Request.Url!.AbsolutePath), CultureInfo.InvariantCulture); _started.GetOrAdd(attempt, _ => Signal()).TrySetResult();
                await _release.GetOrAdd(attempt, _ => Signal()).Task; context.Response.ContentType = "application/octet-stream"; context.Response.ContentLength64 = _bytes.Length;
                if (context.Request.HttpMethod != "HEAD") await context.Response.OutputStream.WriteAsync(_bytes); context.Response.Close();
            }
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or IOException) { }
        }
        public void Dispose() { ReleaseAll(); _listener.Close(); }
    }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static object? Get(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Private | BindingFlags.DeclaredOnly) is { } field) return field.GetValue(target);
            else if (type.GetProperty(name, Private | BindingFlags.DeclaredOnly) is { } property) return property.GetValue(target);
        throw new MissingFieldException(target.GetType().FullName, name);
    }
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetMethod(name, Private | BindingFlags.DeclaredOnly) is { } method) return method.Invoke(target, args);
        throw new MissingMethodException(target.GetType().FullName, name);
    }
    private static void Command(MpvPlayer player, params string[] args) => Require(Call(player, "Command", (object)args) is true, "Native command failed: " + args[0]);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> condition, string message)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(25); Require(condition(), message);
    }
    private static async Task UntilAsync(Func<Task<bool>> condition, string message)
    {
        for (var attempt = 0; attempt < 100; attempt++) { if (await condition()) return; await Task.Delay(25); } throw new InvalidOperationException(message);
    }
}
