using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;

/// <summary>Exercise the real playback-info collector and its owned read-only WinUI surface.</summary>
internal static class PlaybackStatsMenuNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync(Window window)
    {
        using var foreground = new PlaybackAudioMenuNativeFixture.ForegroundOwnershipProbe(WinRT.Interop.WindowNative.GetWindowHandle(window));
        window.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        using var media = new PlaybackAudioMenuNativeFixture.MediaServer();
        using var wire = new PlaybackQualityMenuNativeFixture.Wire(media.Origin);
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://native-quality.fixture.invalid"); api.SetProfile("synthetic");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "stats-settings")), new SettingsApi(api));
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
        mpv.SetProperty("log-file", Path.Combine(Program.ResultDirectory, "stats-mpv.log"));
        mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
        Set(service, "_mpv", mpv); Set(service, "_videoWindow", video); Set(service, "_playbackManager", manager);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "synthetic-quality"); Property(service, "Versions", versions);
        Call(service, "WireMpvEvents");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
        var statsSamples = 0;
        var activations = 0;
        Task? busyOperation = null;
        void Message(string[] args)
        {
            if (args.Length == 4 && args[0] == "silo-native-menu-input" && args[3] == "activate") Interlocked.Increment(ref activations);
            if (args.Length == 4 && args[0] == "silo-native-stats-data") Interlocked.Increment(ref statsSamples);
            if (args.Length > 0 && args[0] == "silo-stats-fixture-ready") ready.TrySetResult();
            if (args.Length == 3 && args[0] == "silo-stats-fixture-state" && queries.TryRemove(args[1], out var response))
            { using var json = JsonDocument.Parse(args[2]); response.TrySetResult(json.RootElement.Clone()); }
        }
        mpv.ScriptMessageReceived += Message;
        try
        {
            var script = await File.ReadAllTextAsync(Path.Combine(Program.AppDirectory, "libs", "mpv", "scripts", "silo-osc.lua"));
            var observed = Path.Combine(Program.ResultDirectory, "stats-observer.lua");
            await File.WriteAllTextAsync(observed, script + """

                mp.register_script_message('stats-fixture-query',function(id,kind)
                    compute_layout()
                    local r=state.layout['btn_'..kind] or {}
                    local legacy=kind=='stats' and state.stats_visible or kind=='chapters' and state.chapter_menu_visible or state[kind..'_menu_visible']
                    mp.commandv('script-message','silo-stats-fixture-state',id,require('mp.utils').format_json({x=r.cx or 0,y=r.cy or 0,w=r.w or 0,legacy=legacy or false,focus=state.controller_focus_name or '',pending=state.native_menu_request~=nil}))
                end)
                mp.commandv('script-message','silo-stats-fixture-ready')
                """);
            Command(mpv, "load-script", observed); await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult(); mpv.FileLoaded += Loaded;
            try { mpv.LoadFile(media.Origin + "/video.y4m"); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Loaded; }
            Call(service, "StopPlaybackStallWatchdog"); service.SetPaused(true);
            await Until(() => mpv.IsPaused && mpv.GetPropertyFlag("vo-configured"), "Generated video did not settle.");
            Call(service, "SendQualityInfoToOsc");
            // FILE_LOADED publishes audio/quality capabilities after renderer setup. Wait
            // for its generation to settle before testing an unrelated menu interaction.
            var publication = (long)Get(service, "_nativeMenuPublication")!;
            await UntilAsync(async () => { await Task.Delay(400); var current = (long)Get(service, "_nativeMenuPublication")!; var stable = current == publication; publication = current; return stable; }, "Initial capability publication did not settle.");
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_STATS_CASE") == "busy")
            {
                wire.HoldFailedReplan = true;
                busyOperation = (Task)Call(service, "SwitchQualityTierAsync", "720p", null, null)!;
                await wire.ReplanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(service.IsLoading && service.IsQualitySwitchActive, "The real held replan did not own busy state.");
                var busyRevision = (long)Get(service, "_playbackBusyRevision")!;
                var file = (long)Get(service, "_playbackFileGeneration")!;
                var busyMenu = await Open("key");
                await Until(() => ((ListView)Get(busyMenu, "_list")!).Items.Count >= 25, "Busy Stats did not populate diagnostics.");
                Command(mpv, "keypress", "END"); Command(mpv, "keypress", "ENTER");
                await Until(() => Volatile.Read(ref activations) == 1, "Busy Stats did not receive native read-only activation.");
                Require(ReferenceEquals(Menu(service), busyMenu) && service.IsLoading && service.IsQualitySwitchActive &&
                    (bool)Get(service, "_switchingContent")! && (long)Get(service, "_playbackBusyRevision")! == busyRevision &&
                    (long)Get(service, "_playbackFileGeneration")! == file && wire.Qualities.Count == 1 && mpv.IsPaused,
                    "Read-only Stats changed the held replan's flags, file, pause or request inventory.");
                Command(mpv, "keypress", "i");
                await UntilAsync(async () => Menu(service) == null && !(await Query()).GetProperty("pending").GetBoolean(), "Busy Stats did not close.");
                Require(Gate(service).CurrentCount == 1 && service.IsLoading && service.IsQualitySwitchActive &&
                    (long)Get(service, "_playbackBusyRevision")! == busyRevision, "Stats close released a replan's busy ownership.");
                wire.ReleaseReplan.TrySetResult(); await busyOperation.WaitAsync(TimeSpan.FromSeconds(8));
                await Until(() => !service.IsLoading && !service.IsQualitySwitchActive && !(bool)Get(service, "_switchingContent")!,
                    "The real replan failed to release its own busy state after Stats closed.");
                foreground.Observe("stats-busy-complete");
                Program.Log("PASS: actual held quality replan permits read-only native Stats key open/End/Enter/second-i close, preserving its exact busy revision, file, pause and single request; replan completes its own cleanup.");
                return;
            }
            // Reconnect/access changes retire reads, not any playback menu.
            versions[0].AudioTracks = [new() { Title = "Audio one", Codec = "aac" }, new() { Title = "Audio two", Codec = "aac" }];
            Call(service, "SendAudioTrackListToOsc");
            foreach (var kind in new[] { "audio", "quality", "stats" })
            {
                var retained = await Open("pointer", kind);
                api.InvalidateAccessContext();
                await Task.Delay(250); await Query(kind);
                Require(ReferenceEquals(Menu(service), retained) && Gate(service).CurrentCount == 0,
                    "Read-context invalidation retired the live native " + kind + " menu.");
                Call(retained, "Input", "cancel");
                await UntilAsync(async () => !(await Query(kind)).GetProperty("pending").GetBoolean(), "Menu close did not acknowledge " + kind);
                var reopened = await Open("pointer", kind);
                Call(reopened, "Input", "cancel");
                await UntilAsync(async () => !(await Query(kind)).GetProperty("pending").GetBoolean(), "Reopened menu did not close " + kind);
                Require(Gate(service).CurrentCount == 1 && mpv.IsPaused && wire.Qualities.IsEmpty,
                    "Access refresh/menu reopen changed playback or leaked a lease.");
            }
            versions[0].Chapters =
            [
                new() { Index = 4, Title = "Chapter one", StartSeconds = 0, EndSeconds = 10 },
                new() { Index = 17, Title = "Chapter two", StartSeconds = 10, EndSeconds = 30 },
            ];
            Call(service, "SendChapterListToOsc");
            Call(service, "PublishNativeMenuScopes");
            // Recovery claims switching without retiring native scopes. Action
            // menus must watch both busy flags; only read-only Stats may remain.
            foreach (var kind in new[] { "audio", "quality", "chapters", "stats" })
            foreach (var loading in new[] { false, true })
            foreach (var queuedSelection in kind == "chapters" ? new[] { false, true } : new[] { false })
            {
                var retained = await Open("pointer", kind);
                Call(retained, "Input", "end");
                var position = mpv.Position;
                if (queuedSelection)
                {
                    // Simulate recovery starting between select-close and the
                    // accepted chapter delegate, independently of the owner watch.
                    var close = retained.GetType().GetMethod("Close", Private)!;
                    close.Invoke(retained, [Enum.Parse(close.GetParameters()[0].ParameterType, "Selection")]);
                }
                try
                {
                    if (loading) Property(service, "IsLoading", true);
                    else
                    {
                        Set(service, "_prematureEofRecoveryActive", true);
                        Call(service, "SetSwitchingContent", true);
                    }
                    var busyRevision = (long)Get(service, "_playbackBusyRevision")!;
                    // Also exercise dispatch independently of the menu's watch,
                    // as a selection may already be queued when recovery starts.
                    if (kind is "audio" or "quality" || queuedSelection)
                        ((Action<string>)Get(retained, "_selected")!)(kind switch
                        {
                            "audio" => "1", "chapters" => "chapter:17", _ => "quality:720p",
                        });
                    if (kind == "stats")
                    {
                        await Task.Delay(250);
                        Require(ReferenceEquals(Menu(service), retained), "Busy state retired read-only Stats.");
                    }
                    else await Until(() => Menu(service) == null, "Busy state left native " + kind + " open.");
                    Call(retained, "Input", "activate");
                    if (kind == "chapters") await Task.Delay(150);
                    Require(Math.Abs(mpv.Position - position) < .1 &&
                        (long)Get(service, "_playbackBusyRevision")! == busyRevision &&
                        service.IsLoading == loading && (bool)Get(service, "_switchingContent")! == !loading &&
                        (bool)Get(service, "_prematureEofRecoveryActive")! == !loading && wire.Qualities.IsEmpty &&
                        Gate(service).CurrentCount == (kind == "stats" ? 0 : 1) && mpv.IsPaused,
                        "A busy native menu sought, started a replan, changed recovery ownership or leaked its lease.");
                }
                finally
                {
                    Set(service, "_prematureEofRecoveryActive", false);
                    Call(service, "SetSwitchingContent", false);
                    Property(service, "IsLoading", false);
                    Call(retained, "Input", "cancel");
                }
                await UntilAsync(async () => !(await Query(kind)).GetProperty("pending").GetBoolean(),
                    "Busy menu close did not acknowledge " + kind);
            }
            var menu = await Open("pointer");
            var list = (ListView)Get(menu, "_list")!;
            await Until(() => list.Items.Count >= 25, "Native playback info did not receive the actual OSC diagnostic sections.");
            await PlaybackAudioMenuNativeFixture.Capture(menu, "stats-initial.png");
            var visibleRows = list.Items.OfType<ListViewItem>().Count(row =>
            {
                if (row.ActualHeight <= 0) return false;
                var top = row.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point()).Y;
                return top >= 0 && top + row.ActualHeight <= list.ActualHeight;
            });
            var ids = list.Items.OfType<ListViewItem>().Select(row => (string)row.Tag).ToArray();
            Require(ids.Select(id => id.Split('\n')[0]).Distinct().SequenceEqual(new[] { "PLAYER", "VIDEO INFO", "PLAYBACK STREAM INFO", "CURRENT SOURCE FILE" }), "Missing diagnostic section.");
            Require(visibleRows >= 6, "Playback info used selection-menu density instead of a compact diagnostic layout.");
            Require(mpv.IsPaused && wire.Qualities.IsEmpty, "Opening playback info changed playback.");
            await Task.Delay(150);
            Call(menu, "Input", "home");
            var viewport = Descendants<ScrollViewer>(list).OrderByDescending(view => view.ScrollableHeight).First();
            Require(viewport.ScrollableHeight > 200, "Stats has no native scroll extent.");
            // Change the native offset without a wheel event, as scrollbar/touch
            // scrolling does, while the selected row remains at the top.
            viewport.ChangeView(null, 200, null, true);
            await Until(() => Math.Abs(viewport.VerticalOffset - 200) < 1, "Could not establish a non-wheel Stats offset.");
            var scrolledOffset = viewport.VerticalOffset;
            var scrolledRow = list.Items.OfType<ListViewItem>().Single(row => (string)row.Tag == "PLAYBACK STREAM INFO\nVideo codec");
            mpv.SendScriptMessage("osc-set-stream-info", "Direct Play", "Original", "HTTP", "scrolled-codec", "aac", "0");
            await Until(() => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(scrolledRow).Contains("scrolled-codec"),
                "Stats did not refresh its scrolled native row.");
            await Task.Delay(150);
            Require(Math.Abs(viewport.VerticalOffset - scrolledOffset) < 1 && list.SelectedIndex == 0,
                "A live Stats refresh snapped non-wheel scrolling back to the selected row.");
            Command(mpv, "keypress", "END");
            await Until(() => ReferenceEquals(list.SelectedItem, list.Items[list.Items.Count - 1]), "End did not navigate Stats.");
            var last = list.SelectedItem;
            Require(ReferenceEquals(last, list.Items[list.Items.Count - 1]), "Stats End did not reach the final diagnostic row.");
            var sampleRow = list.Items.OfType<ListViewItem>().Single(row => (string)row.Tag == "PLAYBACK STREAM INFO\nVideo codec");
            Require(!ids.Contains("PLAYER\nAuto-switched from"), "Unexpected initial source fallback.");
            mpv.SendScriptMessage("osc-set-media-info", """{"requested_source":"fixture alternate"}""");
            mpv.SendScriptMessage("osc-set-stream-info", "Direct Play", "Original", "HTTP", "updated-codec", "aac", "0");
            var samples = Volatile.Read(ref statsSamples);
            await Until(() => Volatile.Read(ref statsSamples) > samples, "Open native stats did not refresh at the visible-only cadence.");
            await Until(() => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(sampleRow).Contains("updated-codec"), "Visible collector did not update the same semantic container.");
            Require(list.Items.OfType<ListViewItem>().Any(row => (string)row.Tag == "PLAYER\nAuto-switched from"), "Conditional diagnostic row was not inserted.");
            Require(ReferenceEquals(list.SelectedItem, last), "Native stats refresh replaced the focused semantic row.");
            mpv.SendScriptMessage("osc-set-media-info", "{}");
            await Until(() => !list.Items.OfType<ListViewItem>().Any(row => (string)row.Tag == "PLAYER\nAuto-switched from"), "Conditional diagnostic row did not disappear.");
            Require(ReferenceEquals(list.SelectedItem, last) && list.Items.Contains(sampleRow), "Removing an optional row replaced surviving containers.");
            await PlaybackAudioMenuNativeFixture.Capture(menu, "stats-end.png");
            Command(mpv, "keypress", "ENTER"); await Until(() => Volatile.Read(ref activations) == 1, "mpv Enter did not reach the native menu."); await Query();
            Require(ReferenceEquals(Menu(service), menu) && mpv.IsPaused && wire.Qualities.IsEmpty,
                "Read-only diagnostics ran a playback action or dismissed on activation.");
            Call(menu, "Input", "cancel");
            await Until(() => Menu(service) == null, "Stats cancel stranded the native presentation.");
            await UntilAsync(async () => !(await Query()).GetProperty("pending").GetBoolean(), "Stats close did not retire the OSC collector.");
            samples = Volatile.Read(ref statsSamples); await Task.Delay(1200);
            Require(Volatile.Read(ref statsSamples) == samples, "Native stats kept sampling after close.");
            Require(Gate(service).CurrentCount == 1 && mpv.IsPaused, "Stats close changed pause or leaked its lease.");
            foreach (var route in new[] { "key", "controller" })
            {
                var reopened = await Open(route); await Until(() => ((ListView)Get(reopened, "_list")!).Items.Count >= 25, route + " did not populate Stats.");
                if (route == "key") Command(mpv, "keypress", "i");
                else Require(Call(reopened, "HandleKeyboardInput", Windows.System.VirtualKey.I) is true,
                    "The actual native Stats keyboard bridge did not consume its custom I dismiss key.");
                await UntilAsync(async () => !(await Query()).GetProperty("pending").GetBoolean(), "Unretired " + route);
            }
            foreground.Observe("stats-complete");
            Program.Log($"PASS: {ids.Length} rows, four sections, {visibleRows} fully visible; pointer, key and controller openers.");
            Program.Log("PASS: native playback info actual OSC collector/native bounded scrolling, all four sections, live semantic-row retention, read-only activation, closed collector cadence and genuine pause. Synthetic only.");
        }
        finally
        {
            wire.ReleaseReplan.TrySetResult();
            if (busyOperation != null) await busyOperation.WaitAsync(TimeSpan.FromSeconds(8));
            mpv.ScriptMessageReceived -= Message;
            if (Menu(service) is { } menu) Call(menu, "Input", "cancel");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket");
            Set(service, "_videoWindow", null); Set(service, "_mpv", null); Set(service, "_playbackManager", null);
            video.Dispose(); mpv.Dispose(); await Task.Delay(40); foreground.Complete();
        }

        async Task<JsonElement> Query(string kind = "stats")
        {
            var id = Guid.NewGuid().ToString("N"); var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queries[id] = reply;
            try { mpv.SendScriptMessage("stats-fixture-query", id, kind); return await reply.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
            finally { queries.TryRemove(id, out _); }
        }
        async Task<object> Open(string route, string kind = "stats")
        {
            mpv.SendScriptMessage("osc-show"); var geometry = await Query(kind);
            Require(geometry.GetProperty("w").GetDouble() > 0, kind + " opener missing from actual OSC.");
            var x = geometry.GetProperty("x").ToString(); var y = geometry.GetProperty("y").ToString();
            if (route == "controller")
            {
                for (var i = 0; i < 40 && (await Query()).GetProperty("focus").GetString() != "btn_stats"; i++) mpv.SendScriptMessage("osc-controller-nav", "right");
                Require((await Query()).GetProperty("focus").GetString() == "btn_stats", "Controller did not reach Stats.");
                mpv.SendScriptMessage("osc-controller-activate");
            }
            else if (route == "key") Command(mpv, "keypress", "i");
            else
            {
                mpv.SendScriptMessage("osc-mouse-move", x, y); mpv.SendScriptMessage("osc-mouse-down", x, y);
                mpv.SendScriptMessage("osc-mouse-up", x, y); mpv.SendScriptMessage("osc-video-click", x, y);
            }
            await Until(() => Menu(service) is { } value && Get(value, "_list") is ListView { XamlRoot: not null },
                "Actual OSC " + route + " produced no native " + kind + " menu.");
            Require(!(await Query(kind)).GetProperty("legacy").GetBoolean(), "Legacy " + kind + " overlapped its native menu.");
            var menu = Menu(service)!; var native = (Window)Get(menu, "_window")!;
            foreground.WatchMenu(native, WinRT.Interop.WindowNative.GetWindowHandle(native));
            foreground.Observe("stats-opened"); return menu;
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static object? Menu(PlayerService service) => Get(service, "_nativeMenu");
    private static SemaphoreSlim Gate(PlayerService service) => (SemaphoreSlim)Get(service, "_playbackDialogGate")!;
    private static object? Get(object target, string name)
    { for (var type = target.GetType(); type != null; type = type.BaseType) if (type.GetField(name, Private) is { } field) return field.GetValue(target); return null; }
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, Private)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    private static void Command(MpvPlayer player, params string[] args) => Require(Call(player, "Command", (object)args) is true, "Stats fixture native command failed.");
    private static void Require(bool success, string detail) { if (!success) throw new InvalidOperationException(detail); }
    private static async Task Until(Func<bool> ready, string failure)
    { var watch = Stopwatch.StartNew(); while (!ready()) { if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new InvalidOperationException(failure); await Task.Delay(20); } }
    private static async Task UntilAsync(Func<Task<bool>> ready, string failure)
    { var watch = Stopwatch.StartNew(); while (!await ready()) { if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new InvalidOperationException(failure); await Task.Delay(20); } }
}
