using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;

/// <summary>Actual OSC, native choice controls and replans with generated video and an isolated fake API.</summary>
internal static class PlaybackQualityMenuNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync(Window window, string mode = "normal")
    {
        using var foreground = new PlaybackAudioMenuNativeFixture.ForegroundOwnershipProbe(WinRT.Interop.WindowNative.GetWindowHandle(window));
        window.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        using var media = new PlaybackAudioMenuNativeFixture.MediaServer();
        using var wire = new Wire(media.Origin);
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://native-quality.fixture.invalid"); api.SetProfile("synthetic");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "quality-settings")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        var versions = new List<FileVersion>
        {
            new() { FileId = 41, Resolution = "1080p", CodecVideo = "hevc", Duration = 60 },
            new() { FileId = 71, Resolution = "2160p", CodecVideo = "av1", Hdr = true, Duration = 60 },
        };
        manager.UseWatchDetail(new() { ContentId = "synthetic-quality", Type = "movie", Versions = versions });
        await manager.StartSessionAsync(41);
        using var video = new MpvVideoWindow(); using var mpv = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(window)); video.Show();
        mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv);
        mpv.SetProperty("log-file", Path.Combine(Program.ResultDirectory, "quality-mpv.log"));
        mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
        Set(service, "_mpv", mpv); Set(service, "_videoWindow", video); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "synthetic-quality"); Property(service, "Versions", versions);
        Call(service, "WireMpvEvents");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
        void Message(string[] args)
        {
            if (args.Length > 0 && args[0] == "silo-quality-fixture-ready") ready.TrySetResult();
            if (args.Length == 3 && args[0] == "silo-quality-fixture-state" && queries.TryRemove(args[1], out var response))
            { using var json = JsonDocument.Parse(args[2]); response.TrySetResult(json.RootElement.Clone()); }
        }
        mpv.ScriptMessageReceived += Message;
        try
        {
            var script = await File.ReadAllTextAsync(Path.Combine(Program.AppDirectory, "libs", "mpv", "scripts", "silo-osc.lua"));
            var observed = Path.Combine(Program.ResultDirectory, "quality-observer.lua");
            await File.WriteAllTextAsync(observed, script + """

                mp.register_script_message('quality-fixture-query',function(id,kind)
                    compute_layout()
                    local r=state.layout[kind=='stats' and 'btn_stats' or 'btn_quality'] or {}
                    mp.commandv('script-message','silo-quality-fixture-state',id,require('mp.utils').format_json({x=r.cx or 0,y=r.cy or 0,w=r.w or 0,legacy=state.quality_menu_visible or false,pending=state.native_menu_request~=nil}))
                end)
                mp.commandv('script-message','silo-quality-fixture-ready')
                """);
            Command(mpv, "load-script", observed); await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult(); mpv.FileLoaded += Loaded;
            try { mpv.LoadFile(media.Origin + "/video.y4m"); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Loaded; }
            Call(service, "StopPlaybackStallWatchdog"); service.SetPaused(true);
            await Until(() => mpv.IsPaused && mpv.GetPropertyFlag("vo-configured"), "Generated video did not settle.");
            Call(service, "SendQualityInfoToOsc");
            if (mode is "successor-failure" or "successor-success")
            {
                using var cancellation = new CancellationTokenSource();
                var preparedTask = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession,
                    versions[0], manager.StreamUrl, 0d, CancellationToken.None)!;
                await preparedTask;
                Call(service, "ApplyPreparedTransport", preparedTask.GetType().GetProperty("Result")!.GetValue(preparedTask));
                wire.HoldFailedReplan = mode == "successor-failure";
                wire.HoldSuccessfulReplan = !wire.HoldFailedReplan;
                var initialFile = (long)Get(service, "_playbackFileGeneration")!;
                var initialError = service.ErrorMessage;
                var heldMenu = await Open(); Call(heldMenu, "Input", "end"); Call(heldMenu, "Input", "activate");
                await wire.ReplanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                wire.HoldSuccessorReplan = true;
                var successor = (Task)Call(service, "RestartTransportForSeekAsync", 2d, false,
                    (long)Get(service, "_seekRestartGeneration")!, cancellation)!;
                await Until(() => ((SemaphoreSlim)Get(service, "_transportRestartGate")!).CurrentCount == 0,
                    "The real successor seek did not enter its transport gate.");
                wire.ReleaseReplan.TrySetResult();
                await wire.SuccessorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(500); await Query();
                Require(service.IsLoading && (bool)Get(service, "_switchingContent")! && !successor.IsCompleted,
                    "Quality completion cleared the held successor's loading/busy state.");
                Require((long)Get(service, "_playbackFileGeneration")! == initialFile && service.ErrorMessage == initialError,
                    "The superseded Quality attempt loaded media or published an error over its successor.");
                wire.ReleaseSuccessor.TrySetResult();
                await successor.WaitAsync(TimeSpan.FromSeconds(8));
                await Until(() => !service.IsLoading && !(bool)Get(service, "_switchingContent")!,
                    "The successor did not finish its own loading/busy lifecycle.");
                Require(mpv.IsPaused && service.IsPaused, "The successor changed genuine pause intent.");
                Program.Log($"PASS: actual native Quality {mode} retains a held same-owner seek's busy state, suppresses stale load/error, and allows its completion.");
                return;
            }
            if (mode == "release")
            {
                var claim = Call(service, "BeginPlaybackBusy", true, (Action?)null)!;
                var released = Call(service, "ReleasePlaybackBusy", claim, (Action?)null)!;
                Require(released != null && !service.IsLoading, "The current busy owner did not release.");
                var executed = 0;
                await QueuedRelease(() => executed++);
                Require(executed == 1, "A current cleanup receipt failed to execute its queued UI action.");
                var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.DispatcherQueue.TryEnqueue(() =>
                {
                    try { Call(service, "RunIfBusyReleaseCurrent", released, (Action)(() => executed++)); queued.TrySetResult(); }
                    catch (Exception error) { queued.TrySetException(error); }
                });
                // A successor can reuse every manager/session/file/token identity.
                // Even a repeated true assignment must retire old UI cleanup.
                Property(service, "IsLoading", true);
                await queued.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(executed == 1 && service.IsLoading, "Old queued UI cleanup ran after a successor loading assignment.");
                Property(service, "IsLoading", false); Call(service, "SetSwitchingContent", false);
                Program.Log("PASS: actual busy release receipt executes current queued cleanup and rejects a successor reusing the same playback identities.");
                return;

                async Task QueuedRelease(Action action)
                {
                    var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    window.DispatcherQueue.TryEnqueue(() =>
                    {
                        try { Call(service, "RunIfBusyReleaseCurrent", released, action); done.TrySetResult(); }
                        catch (Exception error) { done.TrySetException(error); }
                    });
                    await done.Task.WaitAsync(TimeSpan.FromSeconds(4));
                }
            }
            if (mode == "retired")
            {
                wire.HoldFailedReplan = true;
                var initialFile = (long)Get(service, "_playbackFileGeneration")!;
                var pendingMenu = await Open(); Call(pendingMenu, "Input", "end"); Call(pendingMenu, "Input", "activate");
                await wire.ReplanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(service.IsLoading && service.IsQualitySwitchActive, "The held quality action did not own busy state.");
                api.SetProfile("replacement"); wire.ReleaseReplan.TrySetResult();
                await wire.ReplanReplied.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Until(() => !service.IsLoading && !service.IsQualitySwitchActive && !(bool)Get(service, "_switchingContent")!,
                    "Retired quality replan stranded loading or switching state.");
                Require((long)Get(service, "_playbackFileGeneration")! == initialFile && mpv.IsPaused,
                    "Retired quality reply loaded a file or changed genuine pause.");
                Program.Log("PASS: actual held quality failure after profile retirement releases only its busy state, with no stale load or pause change.");
                return;
            }
            var menu = await Open();
            var list = (ListView)Get(menu, "_list")!;
            Require(list.Items.Count == 5, "Native quality omitted versions or server ladder/Auto.");
            await PlaybackAudioMenuNativeFixture.Capture(menu, "quality-initial");
            Require(mpv.IsPaused && wire.Qualities.IsEmpty, "Opening Quality changed playback.");
            Require((await Query()).GetProperty("legacy").GetBoolean() == false, "Native and legacy quality overlap.");
            Call(menu, "Input", "end");
            Require(list.SelectedItem is ListViewItem { Tag: "quality:720p" }, "End did not reach the last server quality.");
            Call(menu, "Input", "activate");
            await Until(() => wire.Qualities.Contains("720p") && !service.IsLoading && !service.IsQualitySwitchActive,
                "Native quality did not finish the existing replan.");
            Require(mpv.IsPaused && service.IsPaused, "Quality replan resumed a genuinely paused viewer.");
            Require(service.SelectedQualityMenuId == "720p", "Accepted quality did not become current.");
            Require(Menu(service) == null && Gate(service).CurrentCount == 1, "Selection stranded the native lease.");
            Call(service, "StopPlaybackStallWatchdog");
            // A prior request may have fallen back to another accepted file.
            // Its Requested badge must not make the version impossible to retry.
            Set(service, "_requestedMediaFileId", 71);
            Call(service, "SendQualityInfoToOsc");

            menu = await Open(); list = (ListView)Get(menu, "_list")!;
            Require(list.Items.OfType<ListViewItem>().Any(row => row.Tag is "quality:720p" &&
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(row).Contains("Current", StringComparison.Ordinal)),
                "Reopening Quality lost the accepted checkmark.");
            Call(menu, "Input", "home"); Call(menu, "Input", "down");
            Require(list.SelectedItem is ListViewItem { Tag: "file:71" }, "Version source ID was replaced by its row index.");
            service.SetPaused(false);
            Call(menu, "Input", "activate");
            await Until(() => wire.Files.Contains(71) && manager.CurrentSession?.MediaFileId == 71 && !service.IsLoading,
                "Version selection did not use the existing replacement-session workflow.");
            Require(!mpv.IsPaused && !service.IsPaused, "Version selection paused a playing viewer.");
            Call(service, "StopPlaybackStallWatchdog");

            Property(service, "IsWatchTogetherPlayback", true); Call(service, "SendQualityInfoToOsc");
            menu = await Open(); list = (ListView)Get(menu, "_list")!;
            Require(!list.Items.OfType<ListViewItem>().Any(row => row.Tag is string id && id.StartsWith("file:")), "Room exposed source-version actions.");
            Require(list.Items.OfType<ListViewItem>().Any(row => row.Tag is "quality:720p" && row.IsEnabled), "Room lost viewer quality choices.");
            var retired = menu; api.SetProfile("replacement");
            var before = wire.Qualities.Count; Call(retired, "Input", "activate");
            await Until(() => Menu(service) == null, "Retired profile kept native Quality open.");
            Require(wire.Qualities.Count == before && Gate(service).CurrentCount == 1, "Retired profile dispatched quality or leaked its lease.");
            foreground.Observe("quality-complete");
            Program.Log("PASS: native Quality actual OSC/owned choices/server ladder, Auto and accepted selection, original file IDs, paused and playing replan intent, room version lock and stale-profile cleanup. Synthetic video/API only.");
        }
        finally
        {
            wire.ReleaseReplan.TrySetResult();
            wire.ReleaseSuccessor.TrySetResult();
            mpv.ScriptMessageReceived -= Message;
            if (Menu(service) is { } menu) Call(menu, "Input", "cancel");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket");
            Set(service, "_videoWindow", null); Set(service, "_mpv", null); Set(service, "_playbackManager", null);
            video.Dispose(); mpv.Dispose(); await Task.Delay(40); foreground.Complete();
        }

        async Task<JsonElement> Query()
        {
            var id = Guid.NewGuid().ToString("N"); var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queries[id] = reply;
            try { mpv.SendScriptMessage("quality-fixture-query", id, "quality"); return await reply.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
            finally { queries.TryRemove(id, out _); }
        }
        async Task<object> Open()
        {
            mpv.SendScriptMessage("osc-show"); var geometry = await Query();
            Require(geometry.GetProperty("w").GetDouble() > 0, "Quality opener missing from actual OSC.");
            var x = geometry.GetProperty("x").ToString(); var y = geometry.GetProperty("y").ToString();
            mpv.SendScriptMessage("osc-mouse-move", x, y); mpv.SendScriptMessage("osc-mouse-down", x, y);
            mpv.SendScriptMessage("osc-mouse-up", x, y); mpv.SendScriptMessage("osc-video-click", x, y);
            await Until(() => Menu(service) is { } value && Get(value, "_list") is ListView { XamlRoot: not null },
                "Actual OSC Quality gesture produced no native menu.");
            var menu = Menu(service)!; var native = (Window)Get(menu, "_window")!;
            foreground.WatchMenu(native, WinRT.Interop.WindowNative.GetWindowHandle(native));
            foreground.Observe("quality-opened"); return menu;
        }
    }

    private sealed class Wire(string origin) : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<string> Qualities = new();
        internal readonly ConcurrentQueue<int> Files = new();
        internal bool HoldFailedReplan;
        internal bool HoldSuccessfulReplan;
        internal bool HoldSuccessorReplan;
        internal readonly TaskCompletionSource SuccessorStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseSuccessor = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReplanStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReplanReplied = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseReplan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _file = 41;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri?.Host == "native-quality.fixture.invalid", "Quality fixture attempted external API networking.");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/settings/")) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1","plan_active_quality_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start") && !path.EndsWith("/replan")) return Json("""{"outcome":"applied"}""");
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = json.RootElement;
            var quality = body.TryGetProperty("quality_preference", out var value) ? value.GetString() ?? "auto" : "auto";
            if (path.EndsWith("/replan"))
            {
                Qualities.Enqueue(quality);
                if (HoldSuccessorReplan && Qualities.Count > 1)
                {
                    SuccessorStarted.TrySetResult(); await ReleaseSuccessor.Task;
                }
                else if (HoldFailedReplan || HoldSuccessfulReplan)
                {
                    ReplanStarted.TrySetResult(); await ReleaseReplan.Task; ReplanReplied.TrySetResult();
                    if (HoldFailedReplan)
                        return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"error\":\"Synthetic retired quality failure\"}") };
                }
            }
            if (body.TryGetProperty("file_id", out var file) && int.TryParse(file.GetString(), out var requestedFile))
            { _file = requestedFile; Files.Enqueue(_file); }
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "native-quality-fixture",
                server_features = new[] { "plan_active_quality_v1" },
                playback_plan = new
                {
                    protocol_version = 3, plan_id = "quality-plan-" + Qualities.Count + "-" + Files.Count,
                    plan_attempt_key = "synthetic", session_id = "native-quality-fixture", effective_media_file_id = _file,
                    delivery = "original_http", stream = new { url = origin + "/video.y4m" },
                    timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { }, subtitle = new { mode = "off", inventory = Array.Empty<object>() },
                    active_quality = quality == "auto" ? "original" : quality,
                    available_qualities = new[] { new { label = "1080p", display_name = "1080p", bitrate_kbps = 5000 }, new { label = "720p", display_name = "720p", bitrate_kbps = 2500 } },
                },
            }));
        }
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }

    private static object? Menu(PlayerService service) => Get(service, "_nativeMenu") ?? Get(service, "_nativeAudioMenu");
    private static SemaphoreSlim Gate(PlayerService service) => (SemaphoreSlim)Get(service, "_playbackDialogGate")!;
    private static object? Get(object target, string name)
    { for (var type = target.GetType(); type != null; type = type.BaseType) if (type.GetField(name, Private) is { } field) return field.GetValue(target); return null; }
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Command(MpvPlayer player, params string[] args) => Require(Call(player, "Command", (object)args) is true, "Quality fixture native command failed.");
    private static void Require(bool success, string detail) { if (!success) throw new InvalidOperationException(detail); }
    private static async Task Until(Func<bool> ready, string failure)
    { var watch = Stopwatch.StartNew(); while (!ready()) { if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new InvalidOperationException(failure); await Task.Delay(20); } }
}
