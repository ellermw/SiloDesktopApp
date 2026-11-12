using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;

internal static class PlaybackSpeedMenuNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync(Window window)
    {
        using var foreground = new PlaybackAudioMenuNativeFixture.ForegroundOwnershipProbe(WinRT.Interop.WindowNative.GetWindowHandle(window));
        window.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        using var media = new PlaybackAudioMenuNativeFixture.MediaServer(); using var wire = new Wire(media.Origin);
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://native-speed.fixture.invalid");
        client.SetProfile("synthetic"); client.SetDeviceMetadata("speed-desktop", "Fixture", "Windows");
        using var auth = new AuthService(client, new AuthApi(client));
        var playback = new PlaybackApi(client); var catalog = new CatalogApi(client);
        using var player = new PlayerService(playback, catalog, auth, client,
            new SettingsService(Path.Combine(Program.ResultDirectory, "speed-settings")), new SettingsApi(client));
        using var manager = new PlaybackManager(playback, catalog, auth, client);
        var versions = new List<FileVersion> { new() { FileId = 42, Resolution = "1080p", CodecVideo = "hevc", Duration = 60 } };
        manager.UseWatchDetail(new() { ContentId = "synthetic-speed", Type = "movie", Versions = versions });
        await manager.StartSessionAsync(42);
        using var video = new MpvVideoWindow(); using var mpv = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(window)); video.Show();
        foreground.Observe("speed-video", video.Hwnd, "video");
        mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv); mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
        Set(player, "_mpv", mpv); Set(player, "_videoWindow", video); Set(player, "_playbackManager", manager);
        Property(player, "State", PlayerState.Expanded); Property(player, "ContentId", "synthetic-speed"); Property(player, "Versions", versions);
        Call(player, "WireMpvEvents");
        var queries = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Message(string[] args)
        {
            if (args.Length > 0 && args[0] == "silo-speed-fixture-ready") ready.TrySetResult();
            if (args.Length == 3 && args[0] == "silo-speed-fixture-state" && queries.TryRemove(args[1], out var reply))
            { using var json = JsonDocument.Parse(args[2]); reply.TrySetResult(json.RootElement.Clone()); }
        }
        mpv.ScriptMessageReceived += Message;
        try
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult(); mpv.FileLoaded += Loaded;
            try { mpv.LoadFile(media.Origin + "/video.y4m"); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Loaded; }
            Call(player, "StopPlaybackStallWatchdog"); player.SetPaused(true);
            await Until(() => mpv.IsPaused && !player.IsLoading && player.CanChangeVideoPlaybackRate, "Video did not settle for Speed.");
            await wire.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            var script = Path.Combine(Program.ResultDirectory, "speed-observer.lua");
            await File.WriteAllTextAsync(script, await File.ReadAllTextAsync(Path.Combine(Program.AppDirectory, "libs/mpv/scripts/silo-osc.lua")) + """

                mp.register_script_message('speed-fixture-query',function(id)
                    compute_layout(); local r=state.layout.btn_quality or {}
                    mp.commandv('script-message','silo-speed-fixture-state',id,require('mp.utils').format_json({
                        x=r.cx or 0,y=r.cy or 0,w=r.w or 0,alpha=state.current_alpha,scope=state.native_speed_scope or '',
                        kind=state.native_menu_request and state.native_menu_request.kind or '',
                        request_focus=state.native_menu_request and state.native_menu_request.focus_name or '',focus=state.controller_focus_name or ''}))
                end)
                mp.commandv('script-message','silo-speed-fixture-ready')
                """);
            Require(Call(mpv, "Command", (object)new[] { "load-script", script }) is true, "Speed observer did not load.");
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)); Call(player, "SendQualityInfoToOsc");
            var menu = await OpenSettings(); var settingsMenu = menu;
            var list = (ListView)Get(menu, "_list")!;
            Require(list.Items.Count == 1 && Row("speed").IsEnabled && player.CanChangeVideoPlaybackRate,
                "Empty quality ladder hid or disabled normal-video Speed while optional settings read was held.");
            await PlaybackAudioMenuNativeFixture.Capture(menu, "speed-settings.png");
            Select("speed");
            await Until(() => Menu(player) is { } current && !ReferenceEquals(current, settingsMenu)
                && Get(current, "_list") is ListView { XamlRoot: not null }, "Settings did not hand off to the native Speed caller.");
            menu = Menu(player)!; list = (ListView)Get(menu, "_list")!; Watch(menu);
            Require(Gate(player).CurrentCount == 0, "Speed lost the shared native-menu lease.");
            var childOrigin = await Query();
            Require(childOrigin.GetProperty("kind").GetString() == "speed" && childOrigin.GetProperty("request_focus").GetString() == "",
                "Pointer settings handoff invented controller focus for the Speed child.");
            wire.ReadRelease.TrySetResult();
            await Until(() => player.DesiredVideoPlaybackRate == 1.35 && list.Items.Cast<ListViewItem>().Any(row => row.Tag is "rate:1.35"),
                "Valid stored custom speed is not represented by the actual native controls.");
            Require(AutomationProperties.GetName(Row("rate:1.35")).Contains("Current") && player.NativePlaybackRate == 1.35,
                "Native custom speed checkmark differs from accepted viewer desire.");
            await PlaybackAudioMenuNativeFixture.Capture(menu, "speed-custom");
            foreach (var size in new[] { (460, 700), (900, 540), (1280, 720) })
            {
                window.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, size.Item1, size.Item2)); video.SynchronizePosition();
                await Task.Delay(120); Call(menu, "Input", "end");
                Require(list.SelectedItem is ListViewItem { Tag: "rate:3" }, "Speed End did not reach the last real preset.");
                await PlaybackAudioMenuNativeFixture.Capture(menu, "speed-" + size.Item1);
            }
            var logPath = Path.Combine(Program.ResultDirectory, "mpv_log.txt"); var logOffset = ReadLog(logPath).Length;
            wire.HoldFirstWrite = true; wire.FailFirstWrite = true;
            Select("rate:1.5"); await wire.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Require(player.VideoPlaybackRatePending && AutomationProperties.GetName(Row("rate:1.5")).Contains("Pending"),
                "Actual Speed selection did not show current pending save state.");
            Select("rate:2"); Require(player.DesiredVideoPlaybackRate == 2 && player.NativePlaybackRate == 2, "Newer native manual intent did not win immediately.");
            wire.WriteRelease.TrySetResult();
            await Until(() => wire.Writes.Count == 2 && !player.VideoPlaybackRatePending, "Latest serialized Speed save did not settle.");
            Require(player.VideoPlaybackRateError == null && player.DesiredVideoPlaybackRate == 2, "Older failed save replaced current Speed feedback.");
            var actions = ReadLog(logPath)[logOffset..];
            Require(mpv.IsPaused && !actions.Contains("Run command: seek") && !actions.Contains("Set property: pause="),
                "Native paused Speed choice issued seek/pause work.");
            wire.FailWrites = true; Select("rate:0.75");
            await Until(() => player.VideoPlaybackRateError != null && !player.VideoPlaybackRatePending, "Current Speed save failure was hidden.");
            Require(Menu(player) != null && Descendants<TextBlock>((Grid)Get(menu, "_root")!).Any(text => text.Text.Contains("could not save")),
                "Current Speed failure has no visible native retry feedback.");
            await PlaybackAudioMenuNativeFixture.Capture(menu, "speed-save-error");
            wire.FailWrites = false; Select("rate:0.75");
            await Until(() => player.VideoPlaybackRateError == null && !player.VideoPlaybackRatePending, "Same-rate native retry did not recover.");
            var writesBeforeJoin = wire.Writes.Count;
            player.SetWatchTogetherPlayback(true);
            Require(player.NativePlaybackRate == 1 && player.DesiredVideoPlaybackRate == 0.75 && wire.Writes.Count == writesBeforeJoin,
                "Room attachment retained solo speed or overwrote the saved viewer preference.");
            player.NativePlaybackRate = 1.04;
            await Until(() => Menu(player) == null, "Joining a room did not retire the old solo Speed owner.");
            Require(Gate(player).CurrentCount == 1, "Room attachment leaked the retired menu lease.");
            await ReopenSpeed();
            Require(list.Items.Count == 1 && !((ListViewItem)list.Items[0]).IsEnabled, "Reopened room Speed is not a read-only saved preference.");
            Require(AutomationProperties.GetName((ListViewItem)list.Items[0]).Contains("0.75×")
                && AutomationProperties.GetName((ListViewItem)list.Items[0]).Contains("Watch Party"), "Room drift replaced viewer desire or lost the disabled reason.");
            await PlaybackAudioMenuNativeFixture.Capture(menu, "speed-room");
            var roomWrites = wire.Writes.Count; Call(menu, "Input", "activate");
            Require(wire.Writes.Count == roomWrites && player.NativePlaybackRate == 1.04, "Disabled room Speed changed or persisted a drift rate.");
            Require(Call(mpv, "Command", (object)new[] { "keypress", "ESC" }) is true, "Read-only Speed Escape command failed.");
            await Until(() => Menu(player) == null && Gate(player).CurrentCount == 1,
                "Read-only room Speed trapped Escape or leaked the shared lease.");
            await ReopenSpeed();
            player.SetWatchTogetherPlayback(false);
            await Until(() => Menu(player) == null && player.NativePlaybackRate == 0.75, "Room departure did not retire the room owner and restore desired rate.");
            Require(Gate(player).CurrentCount == 1, "Room departure leaked the native menu lease.");
            await ReopenSpeed();
            Require(list.Items.Count == 9 && list.Items.Cast<ListViewItem>().All(row => row.IsEnabled) && wire.Writes.Count == roomWrites,
                "Reopened solo Speed failed to restore editable presets without a room-drift write.");
            player.SetPaused(false); await Until(() => !mpv.IsPaused, "Playing Speed precondition failed.");
            Select("rate:1.25"); await Until(() => !player.VideoPlaybackRatePending, "Playing Speed save did not settle.");
            Require(!mpv.IsPaused && player.NativePlaybackRate == 1.25, "Native Speed forced a playing viewer to pause.");
            // A source-version replacement keeps the playback owner, even when
            // its accepted preference write is still pending. A new optional
            // read would be slow (and, on the second switch, fail).
            versions.Add(new() { FileId = 43, Resolution = "720p", CodecVideo = "hevc", Duration = 60 });
            foreach (var failRead in new[] { false, true })
            {
                wire.ReadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.FailReads = failRead; wire.HoldVersionWrite = true;
                wire.VersionWriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                wire.VersionWriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var speedSession = Get(player, "_videoPlaybackRate"); var readsBeforeSwitch = wire.SpeedReads;
                var saving = player.SetVideoPlaybackRateAsync(1.5);
                await wire.VersionWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                try
                {
                    var target = versions[failRead ? 0 : 1];
                    var previousFileGeneration = (long)Get(player, "_playbackFileGeneration")!;
                    await player.SwitchVersionAsync(target).WaitAsync(TimeSpan.FromSeconds(8));
                    await Until(() => manager.CurrentSession?.MediaFileId == target.FileId && !player.IsLoading &&
                        (long)Get(player, "_playbackFileGeneration")! > previousFileGeneration,
                        "Version replacement did not finish its real native load while the speed save was held.");
                    Require(ReferenceEquals(speedSession, Get(player, "_videoPlaybackRate")) && player.CanChangeVideoPlaybackRate &&
                        player.DesiredVideoPlaybackRate == 1.5 && player.NativePlaybackRate == 1.5 && player.VideoPlaybackRatePending,
                        "Version FILE_LOADED reset accepted speed or retired its pending preference intent.");
                    Require(wire.SpeedReads == readsBeforeSwitch && !mpv.IsPaused,
                        "Same-owner version replacement reread speed settings or paused the viewer.");
                }
                finally
                {
                    wire.HoldVersionWrite = false; wire.VersionWriteRelease.TrySetResult(); await saving;
                    wire.ReadRelease.TrySetResult();
                }
                await Until(() => Get(player, "_loadedMediaInitCts") == null, "Version media publication did not settle.");
                Require(player.DesiredVideoPlaybackRate == 1.5 && player.NativePlaybackRate == 1.5 &&
                    !player.VideoPlaybackRatePending && player.VideoPlaybackRateError == null && wire.SpeedReads == readsBeforeSwitch,
                    "Version replacement lost accepted speed after its save settled or started a redundant settings read.");
            }
            wire.FailReads = false;
            await ReopenSpeed();
            var oldFileMenu = menu; var writesBeforeLoad = wire.Writes.Count;
            var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Reloaded() => reloaded.TrySetResult(); mpv.FileLoaded += Reloaded;
            try { mpv.LoadFile(media.Origin + "/video.y4m"); await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Reloaded; }
            await Until(() => Menu(player) == null, "Actual media reload retained the old Speed presentation.");
            Call(oldFileMenu, "Input", "activate");
            Require(wire.Writes.Count == writesBeforeLoad && Gate(player).CurrentCount == 1,
                "Retired file's Speed callback wrote preferences or leaked its lease.");
            await Until(() => Get(player, "_loadedMediaInitCts") == null, "Reloaded media publication did not settle.");
            await ReopenSpeed();
            client.SetProfile("replacement"); var retired = menu;
            Call(retired, "Input", "activate"); await Until(() => Menu(player) == null, "Retired profile kept Speed presentation alive.");
            Require(Gate(player).CurrentCount == 1 && wire.Writes.Count == writesBeforeLoad, "Speed profile retirement leaked its lease or wrote stale preferences.");
            Program.Log("PASS: actual empty-ladder OSC settings→native Speed; custom value/presets, pending/latest failure/retry, no seek/pause, room drift/disabled/departure, source-version loads with a held save and slow/failing read, and profile retirement.");

            ListViewItem Row(string id) => list.Items.Cast<ListViewItem>().Single(row => (string)row.Tag == id);
            void Select(string id) { list.SelectedItem = Row(id); Call(menu, "Input", "activate"); }
            async Task ReopenSpeed()
            {
                menu = await OpenSettings(); list = (ListView)Get(menu, "_list")!;
                var parentMenu = menu; Select("speed");
                await Until(() => Menu(player) is { } child && !ReferenceEquals(child, parentMenu) &&
                    Get(child, "_list") is ListView { XamlRoot: not null }, "Reopened settings did not hand off to Speed.");
                menu = Menu(player)!; list = (ListView)Get(menu, "_list")!; Watch(menu);
            }
        }
        finally
        {
            wire.ReadRelease.TrySetResult(); wire.WriteRelease.TrySetResult(); wire.VersionWriteRelease.TrySetResult();
            if (Menu(player) is { } current) Call(current, "Input", "cancel");
            Call(player, "StopPlaybackStallWatchdog"); Call(player, "DisconnectWebSocket"); mpv.ScriptMessageReceived -= Message;
            Set(player, "_videoWindow", null); Set(player, "_mpv", null); Set(player, "_playbackManager", null);
            video.Hide(); await Task.Delay(40); foreground.Complete();
        }
        void Watch(object menu)
        { var native = (Window)Get(menu, "_window")!; foreground.WatchMenu(native, WinRT.Interop.WindowNative.GetWindowHandle(native)); }
        async Task<JsonElement> Query()
        {
            var id = Guid.NewGuid().ToString("N"); var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queries[id] = reply; mpv.SendScriptMessage("speed-fixture-query", id);
            try { return await reply.Task.WaitAsync(TimeSpan.FromSeconds(3)); } finally { queries.TryRemove(id, out _); }
        }
        async Task<object> OpenSettings()
        {
            mpv.SendScriptMessage("osc-show"); var geometry = await Query();
            var visibleDeadline = DateTime.UtcNow.AddSeconds(3);
            while (geometry.GetProperty("alpha").GetDouble() < 0.1 && DateTime.UtcNow < visibleDeadline)
            { await Task.Delay(20); geometry = await Query(); }
            Require(geometry.GetProperty("alpha").GetDouble() >= 0.1, "Actual settings gear never became visible for the pointer gesture.");
            Require(geometry.GetProperty("w").GetDouble() > 0, "Actual settings gear is absent with an empty quality ladder.");
            var x = geometry.GetProperty("x").ToString(); var y = geometry.GetProperty("y").ToString();
            mpv.SendScriptMessage("osc-mouse-move", x, y); mpv.SendScriptMessage("osc-mouse-down", x, y);
            mpv.SendScriptMessage("osc-mouse-up", x, y); mpv.SendScriptMessage("osc-video-click", x, y);
            await Until(() => Menu(player) is { } current && Get(current, "_list") is ListView { XamlRoot: not null }, "Actual Speed settings gear did not open native controls.");
            var result = Menu(player)!; Watch(result); return result;
        }
    }

    private sealed class Wire(string origin) : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<double> Writes = new();
        internal bool HoldFirstWrite, FailFirstWrite, FailWrites, HoldVersionWrite, FailReads;
        internal int SpeedReads;
        internal readonly TaskCompletionSource ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously),
            WriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), WriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously),
            VersionWriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), VersionWriteRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri?.Host == "native-speed.fixture.invalid", "Speed fixture attempted external API networking.");
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/settings/"))
            {
                if (request.Method == HttpMethod.Get)
                {
                    if (!request.RequestUri.Query.Contains("player.playback_speed")) return Json("{\"items\":[]}");
                    Interlocked.Increment(ref SpeedReads); ReadEntered.TrySetResult(); await ReadRelease.Task;
                    return Json("{\"items\":[{\"key\":\"player.playback_speed\",\"value\":1.35}]}",
                        FailReads ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
                }
                Require(request.Method == HttpMethod.Put && path.EndsWith("player.playback_speed") && request.RequestUri.Query.Contains("scope=profile_device")
                    && request.RequestUri.Query.Contains("device_id=speed-desktop") && request.RequestUri.Query.Contains("profile_id=synthetic"), "Speed wrote the wrong settings scope/device/profile.");
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); Writes.Enqueue(body.RootElement.GetProperty("value").GetDouble());
                var first = Writes.Count == 1;
                if (first && HoldFirstWrite) { WriteEntered.TrySetResult(); await WriteRelease.Task; }
                if (HoldVersionWrite) { VersionWriteEntered.TrySetResult(); await VersionWriteRelease.Task; }
                return Json("{}", FailWrites || first && FailFirstWrite ? HttpStatusCode.InternalServerError : HttpStatusCode.OK);
            }
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1","plan_active_quality_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start")) return Json("{\"outcome\":\"applied\"}");
            using var start = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var fileId = int.Parse(start.RootElement.GetProperty("file_id").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var sessionId = "speed-session-" + fileId;
            return Json(JsonSerializer.Serialize(new { protocol_version = 3, outcome = "playable", session_id = sessionId,
                server_features = new[] { "plan_active_quality_v1" }, playback_plan = new {
                    protocol_version = 3, plan_id = "speed-plan-" + fileId, plan_attempt_key = "synthetic", session_id = sessionId, effective_media_file_id = fileId,
                    delivery = "original_http", stream = new { url = origin + "/video.y4m" }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { }, subtitle = new { mode = "off", inventory = Array.Empty<object>() }, active_quality = "original", available_qualities = Array.Empty<object>() } }));
        }
        private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body) };
    }
    private static object? Menu(PlayerService player) => Get(player, "_nativeMenu");
    private static SemaphoreSlim Gate(PlayerService player) => (SemaphoreSlim)Get(player, "_playbackDialogGate")!;
    private static object? Get(object target, string name) { for (var type = target.GetType(); type != null; type = type.BaseType) if (type.GetField(name, Private) is { } field) return field.GetValue(target); return null; }
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready, string message) { var deadline = DateTime.UtcNow.AddSeconds(8); while (!ready()) { if (DateTime.UtcNow > deadline) throw new InvalidOperationException(message); await Task.Delay(20); } }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) { var child = VisualTreeHelper.GetChild(root, index); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static string ReadLog(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
}
