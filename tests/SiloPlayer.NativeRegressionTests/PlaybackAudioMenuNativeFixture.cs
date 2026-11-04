using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

/// <summary>
/// Drives the real OSC, PlayerService and owned native Audio menu using only
/// generated local video and a synthetic API. No installed application, account,
/// system input injection, foreground changes or production setup are used.
/// </summary>
internal static class PlaybackAudioMenuNativeFixture
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync(Window owner)
    {
        using var foreground = new ForegroundOwnershipProbe(WinRT.Interop.WindowNative.GetWindowHandle(owner));
        if (owner.AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(false, false);
        owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        var compactClickCase = Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE") == "compact-keyboard-click";
        if (compactClickCase) owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 460, 700));
        using var media = new MediaServer();
        var directFailureCase = Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE") == "own-load-failure";
        var mediaPath = directFailureCase ? "/stream/direct/synthetic-video.y4m" : "/video.y4m";
        using var wire = new Wire(media.Origin, mediaPath);
        using var http = new HttpClient(wire);
        var api = new SiloApiClient(http); api.SetBaseUrl("https://native-audio.fixture.invalid"); api.SetProfile("synthetic");
        using var auth = new AuthService(api, new AuthApi(api));
        var playback = new PlaybackApi(api); var catalog = new CatalogApi(api);
        using var service = new PlayerService(playback, catalog, auth, api,
            new SettingsService(Path.Combine(Program.ResultDirectory, "native-audio-settings")), new SettingsApi(api));
        using var manager = new PlaybackManager(playback, catalog, auth, api);
        var version = Version();
        manager.UseWatchDetail(new() { ContentId = "synthetic-video", Type = "movie", Versions = [version] });
        await manager.StartSessionAsync(42, audioTrackIndex: 37);
        using var video = new MpvVideoWindow();
        using var mpv = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner)); video.Show();
        foreground.Observe("video-shown", video.Hwnd, "video");
        mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv);
        mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
        Set(service, "_mpv", mpv); Set(service, "_playbackManager", manager); Set(service, "_videoWindow", video);
        Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "synthetic-video");
        Property(service, "Versions", manager.WatchDetail!.Versions);
        Call(service, "WireMpvEvents");
        var queries = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retainedDirectCallbacks = new List<object>();
        void Message(string[] args)
        {
            if (args.Length == 0) return;
            if (args[0] == "silo-audio-fixture-ready") ready.TrySetResult();
            if (args.Length > 2 && args[0] == "silo-audio-fixture-state" && queries.TryRemove(args[1], out var receiver))
            {
                try { using var json = JsonDocument.Parse(args[2]); receiver.TrySetResult(json.RootElement.Clone()); }
                catch (Exception ex) { receiver.TrySetException(ex); }
            }
        }
        mpv.ScriptMessageReceived += Message;
        try
        {
            var script = Path.Combine(Program.AppDirectory, "libs", "mpv", "scripts", "silo-osc.lua");
            var observed = Path.Combine(Program.ResultDirectory, "native-audio-observer.lua");
            await File.WriteAllTextAsync(observed, """
                local fixture_mp = require('mp')
                local fixture_register = fixture_mp.register_script_message
                local fixture_close_consume = ''
                fixture_mp.register_script_message = function(name, callback)
                    if name == 'osc-native-menu-closed' then
                        return fixture_register(name, function(token, sequence, consume)
                            fixture_close_consume = consume
                            return callback(token, sequence, consume)
                        end)
                    end
                    return fixture_register(name, callback)
                end

                """ + await File.ReadAllTextAsync(script) + """

                mp.register_script_message('osc-audio-fixture-query', function(request)
                    compute_layout()
                    local r = state.layout.btn_audio or state.layout.btn_more or {}
                    local pending = state.native_menu_request or {}
                    local compactAudio = {}
                    for _, item in ipairs(state.compact_menu_items or {}) do
                        if item.name == 'btn_audio' then compactAudio = item end
                    end
                    mp.commandv('script-message','silo-audio-fixture-state',request,require('mp.utils').format_json({
                        x=r.cx or 0,y=r.cy or 0,w=r.w or 0,h=r.h or 0,
                        width=state.osd_width,height=state.osd_height,scope=state.native_audio_scope or '',
                        sequence=pending.sequence or 0,legacy=state.audio_menu_visible or false,
                        notice=state.notice_visible or false,noticeTitle=state.notice_title or '',
                        closeConsume=fixture_close_consume,
                        compactX=(compactAudio.x or 0)+(compactAudio.w or 0)/2,
                        compactY=(compactAudio.y or 0)+(compactAudio.h or 0)/2}))
                end)
                mp.commandv('script-message','silo-audio-fixture-ready')
                """);
            Command(mpv, "load-script", observed);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult(); mpv.FileLoaded += Loaded;
            try { mpv.LoadFile(media.Origin + mediaPath); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Loaded; }
            Call(service, "StopPlaybackStallWatchdog");
            service.SetPaused(true);
            await Until(() => mpv.IsPaused && mpv.GetPropertyFlag("vo-configured"), "Synthetic video did not settle.");
            Call(service, "SendAudioTrackListToOsc");
            mpv.SendScriptMessage("osc-show");
            await Task.Delay(150);

            var menu = await OpenFromOsc();
            var hwnd = MenuHwnd(menu);
            var list = (ListView)Get(menu, "_list")!;
            if (compactClickCase || Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE") == "keyboard-click")
            {
                Call(menu, "Input", "cancel");
                await Until(() => Menu(service) == null, "Keyboard cancel did not dismiss Audio.");
                while ((await Query()).GetProperty("sequence").GetInt64() != 0) await Task.Delay(1);
                Require((await Query()).GetProperty("closeConsume").GetString() == "false",
                    "Keyboard dismissal incorrectly claimed a pointer gesture.");
                SendMessageW(video.Hwnd, 0x0201, IntPtr.Zero, new IntPtr((100 << 16) | 100));
                SendMessageW(video.Hwnd, 0x0202, IntPtr.Zero, new IntPtr((100 << 16) | 100));
                await Until(() => !mpv.IsPaused && !service.IsPaused,
                    "A fresh video click immediately after keyboard dismissal was swallowed.");
                Program.Log("PASS: keyboard dismissal acknowledgement permits an immediate real video HWND down/up click.");
                return;
            }
            Require(list.Items.Count == 72, "Native menu omitted source audio tracks.");
            Require(list.SelectedIndex == 37, "The current source track was not selected on open.");
            Require(GetWindow(hwnd, 4) == video.Hwnd && (GetWindowLongPtrW(hwnd, -20).ToInt64() & 0x40000) == 0
                && (GetWindowLongPtrW(hwnd, -20).ToInt64() & 0x80) != 0, "Native menu is not an owned tool window without an extra taskbar entry.");
            Require(IsWindowVisible(hwnd) && IsWindowVisible(video.Hwnd), "Native Audio or its synthetic video is not shown.");
            Require(GetWindowRect(video.Hwnd, out var videoRect) && GetWindowRect(hwnd, out var menuRect)
                && menuRect.Left >= videoRect.Left && menuRect.Right <= videoRect.Right
                && menuRect.Top >= videoRect.Top && menuRect.Bottom <= videoRect.Bottom,
                "Native audio menu escaped its offscreen video bounds.");
            var current = (ListViewItem)list.Items[37];
            Require(AutomationProperties.GetName(current).Contains("Current selection", StringComparison.Ordinal)
                && AutomationProperties.GetName(current).Contains("Japanese", StringComparison.Ordinal), "Current metadata/check is missing from the accessible native row.");
            var menuProbe = Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE");
            if (menuProbe == "geometry")
            {
                var originals = ((System.Collections.IEnumerable)Get(menu, "_choices")!).Cast<object>().ToArray();
                var choiceType = originals[0].GetType();
                var one = Array.CreateInstance(choiceType, 1);
                one.SetValue(Activator.CreateInstance(choiceType, ["pending", "Loading choices…", "", Array.Empty<string>(), false, false, "Loading", false, true]), 0);
                Require(GetWindowRect(hwnd, out var initial), "Menu bounds missing.");
                Call(menu, "UpdateChoices", one);
                await Until(() => GetWindowRect(hwnd, out var small) && small.Bottom - small.Top < initial.Bottom - initial.Top - 80,
                    "Native menu did not shrink after its live inventory became one placeholder.");
                Require(((FrameworkElement)((ListViewItem)list.Items[0]).Content).Opacity < .8 &&
                    Descendants<TextBlock>((DependencyObject)((ListViewItem)list.Items[0]).Content).Any(text => text.Text == "Loading"),
                    "Disabled native choice lost its muted appearance or visible explanation.");
                var restored = Array.CreateInstance(choiceType, originals.Length);
                for (var i = 0; i < originals.Length; i++) restored.SetValue(originals[i], i);
                Call(menu, "UpdateChoices", restored);
                await Until(() => GetWindowRect(hwnd, out var grown) && grown.Bottom - grown.Top >= initial.Bottom - initial.Top,
                    "Native menu did not grow after its live inventory expanded.");
                Require(GetWindowRect(video.Hwnd, out var surface) && GetWindowRect(hwnd, out var bounds) && bounds.Left >= surface.Left && bounds.Right <= surface.Right && bounds.Top >= surface.Top && bounds.Bottom <= surface.Bottom,
                    "Live inventory growth escaped the playback owner.");
                Call(menu, "Input", "cancel");
                Program.Log("PASS: actual native common menu re-evaluates one/many choice geometry within its owner.");
                return;
            }
            if (menuProbe == "resize")
            {
                Call(menu, "Input", "end");
                await Until(() => SelectedRowFits(list), "Initial keyboard row did not enter view.");
                await Task.Delay(100);
                var viewport = Descendants<ScrollViewer>(list).OrderByDescending(view => view.ScrollableHeight).First();
                // Model the stale native offset observed during footer reflow;
                // selection stays last and no wheel intent has been received.
                list.ScrollIntoView(list.Items[0], ScrollIntoViewAlignment.Leading); list.UpdateLayout();
                await Until(() => viewport.VerticalOffset < 1, "Resize probe could not establish its stale offset.");
                owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 460, 300)); video.SynchronizePosition();
                await Task.Delay(100);
                await Until(() => SelectedRowFits(list), "Native selected row escaped the resized list viewport.");
                // Actual handler + native viewport; no system input injection.
                Call(menu, "OnListPointerWheel", list, null);
                list.ScrollIntoView(list.Items[0], ScrollIntoViewAlignment.Leading); list.UpdateLayout();
                await Until(() => viewport.VerticalOffset < 1, "Wheel probe did not reach its native view.");
                owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 500, 340)); video.SynchronizePosition();
                await Task.Delay(150);
                Require(viewport.VerticalOffset < 1, "Resize reset deliberate wheel view.");
                Call(menu, "Input", "end"); await Until(() => SelectedRowFits(list), "Keyboard navigation did not resume selected-row visibility.");
                Call(menu, "Input", "cancel");
                Program.Log("PASS: actual native common menu repairs keyboard viewport alignment after resize and preserves modeled wheel intent.");
                return;
            }
            foreach (var size in new[] { (900, 540), (460, 700), (1920, 1080) })
            {
                owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, size.Item1, size.Item2));
                video.SynchronizePosition();
                await Until(() => GetWindowRect(video.Hwnd, out var surface) && GetWindowRect(hwnd, out var bounds)
                    && bounds.Left >= surface.Left && bounds.Right <= surface.Right && bounds.Top >= surface.Top && bounds.Bottom <= surface.Bottom,
                    $"Audio menu escaped its resized {size.Item1}x{size.Item2} owner.");
                Require(ReferenceEquals(Menu(service), menu), "Native owner resize replaced or stranded Audio ownership.");
                if (size.Item1 == 460) await Capture(menu, "audio-narrow.png");
            }
            owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
            video.SynchronizePosition();
            Call(menu, "Input", "end");
            await Until(() => list.SelectedIndex == 71 && Descendants<ScrollViewer>(list).Any(s => s.VerticalOffset > 0),
                "End did not reach the last track through native scrolling.");
            await Capture(menu, "audio-end.png");
            Call(menu, "Input", "home");
            Require(list.SelectedIndex == 0, "Home did not reach the first source track.");
            await Capture(menu, "audio-home.png");
            Require(mpv.IsPaused && service.IsPaused, "Opening or navigating Audio changed genuine paused intent.");
            Call(menu, "Input", "cancel");
            await Until(() => Menu(service) == null && !IsWindow(hwnd), "Escape/cancel stranded the owned Audio window.");
            Require(Gate(service).CurrentCount == 1, "Audio close did not release exactly its dialog lease.");
            Require(Get(video, "SurfaceChanged") == null && Get(video, "_menuDismissal") == null,
                "Audio close retained its native owner subscription or dismissal hook.");

            // A second request replaces the first. Its late close/selection cannot
            // clear the new request or choose a track.
            menu = await OpenFromOsc(); var old = menu;
            var first = await Query();
            var token = first.GetProperty("scope").GetString()!;
            var sequence = first.GetProperty("sequence").GetInt64();
            Host("silo-native-audio-open", token, (sequence + 1).ToString(), "1000", "620", "40", "40", "1280", "720");
            await Until(() => Menu(service) is { } replacement && !ReferenceEquals(replacement, old), "Newer open did not replace the prior menu.");
            menu = Menu(service)!;
            Call(video, "ClearMenuDismissal", Get(old, "_dismiss"));
            Call(old, "Input", "activate");
            Require(ReferenceEquals(Menu(service), menu) && Gate(service).CurrentCount == 0 && wire.AudioPicks.Count == 0,
                "Retired menu callback affected its replacement.");
            Host("silo-native-menu-input", token, sequence.ToString(), "cancel");
            await Task.Delay(60);
            Require(ReferenceEquals(Menu(service), menu), "Stale cancel closed a newer menu.");
            Call(menu, "Input", "cancel");
            // The synthetic newer request deliberately bypassed Lua's local
            // counter. Retire that synthetic owner explicitly; unchanged
            // metadata publication correctly retains a live menu owner.
            Call(service, "RetireNativeMenus");
            Call(service, "SendAudioTrackListToOsc");

            // Native activation dismissal is consumed before a video down/up can
            // become osc-video-click. These messages target only our owned HWND.
            menu = await OpenFromOsc(); hwnd = MenuHwnd(menu);
            foreground.Observe("left-activation-dismiss-before");
            var activation = SendMessageW(video.Hwnd, 0x0021, video.Hwnd, new IntPtr((0x0201 << 16) | 1));
            Require(activation == new IntPtr(2), "WM_MOUSEACTIVATE did not eat the dismissal click.");
            SendMessageW(video.Hwnd, 0x0202, IntPtr.Zero, new IntPtr((100 << 16) | 100));
            var fullscreen = 0;
            void Fullscreen() => fullscreen++;
            video.FullscreenToggleRequested += Fullscreen;
            try
            {
                SendMessageW(video.Hwnd, 0x0203, new IntPtr(1), new IntPtr((100 << 16) | 100));
                SendMessageW(video.Hwnd, 0x0202, IntPtr.Zero, new IntPtr((100 << 16) | 100));
            }
            finally { video.FullscreenToggleRequested -= Fullscreen; }
            await Until(() => Menu(service) == null && !IsWindow(hwnd), "Activation dismissal left the native menu open.");
            Require((await Query()).GetProperty("closeConsume").GetString() == "true",
                "Actual outside pointer dismissal did not acknowledge its gesture.");
            Require(mpv.IsPaused && service.IsPaused && wire.AudioPicks.Count == 0 && fullscreen == 0, "Dismissal/double-click changed paused playback, fullscreen or selected audio.");
            foreground.Observe("left-activation-dismiss-after");

            menu = await OpenFromOsc();
            foreground.Observe("right-activation-dismiss-before");
            activation = SendMessageW(video.Hwnd, 0x0021, video.Hwnd, new IntPtr((0x0204 << 16) | 1));
            Require(activation == new IntPtr(2), "Right activation did not eat the dismissal click.");
            SendMessageW(video.Hwnd, 0x0205, IntPtr.Zero, new IntPtr((100 << 16) | 100));
            await Until(() => Menu(service) == null, "Right activation dismissal left the native menu open.");
            Require(mpv.IsPaused && service.IsPaused, "Right dismissal changed paused playback.");
            foreground.Observe("right-activation-dismiss-after");

            menu = await OpenFromOsc();
            SendMessageW(video.Hwnd, 0x0201, new IntPtr(1), new IntPtr((100 << 16) | 100));
            SendMessageW(video.Hwnd, 0x0202, IntPtr.Zero, new IntPtr((100 << 16) | 100));
            await Until(() => Menu(service) == null, "Already-active video dismissal did not close the menu.");
            Require(mpv.IsPaused && service.IsPaused, "Already-active video dismissal leaked pause.");

            // Unrelated playback dialogs keep their own lease; native Audio may
            // not release, steal, or display over them.
            await Gate(service).WaitAsync();
            try
            {
                await TriggerOsc(); await Task.Delay(80);
                Require(Menu(service) == null && Gate(service).CurrentCount == 0, "Audio stole or released an unrelated dialog gate.");
            }
            finally { Gate(service).Release(); }

            menu = await OpenFromOsc(); hwnd = MenuHwnd(menu);
            foreground.Observe("native-window-close-before");
            ((Window)Get(menu, "_window")!).Close();
            foreground.Observe("native-window-close-after");
            await Until(() => Menu(service) == null && !IsWindow(hwnd), "Native Window.Close stranded Audio ownership.");
            Call(menu, "Input", "cancel");
            Require(Gate(service).CurrentCount == 1, "Late close released the native Audio lease twice.");

            var originalTracks = version.AudioTracks!;
            version.AudioTracks = [originalTracks[0]];
            Call(service, "SendAudioTrackListToOsc");
            await TriggerOsc(); await Task.Delay(60);
            Require(Menu(service) == null && (await Query()).GetProperty("scope").GetString() == "",
                "A one-track inventory still offered the native Audio workflow.");
            version.AudioTracks = originalTracks;
            Call(service, "SendAudioTrackListToOsc");

            // Synthetic replacement of the actual file identity rejects an old
            // selection/request; it is not a physical load or timeout cancel.
            menu = await OpenFromOsc();
            var retired = menu; var ownerState = await Query();
            foreground.Observe("synthetic-file-retirement-before");
            Set(service, "_playbackFileGeneration", (long)Get(service, "_playbackFileGeneration")! + 1);
            Call(retired, "Input", "activate");
            await Until(() => Menu(service) == null, "A stale file menu did not retire before selection.");
            Require(wire.AudioPicks.Count == 0, "A stale file menu selected a track.");
            Host("silo-native-audio-open", ownerState.GetProperty("scope").GetString()!, "999", "1000", "620", "40", "40", "1280", "720");
            await Task.Delay(60); Require(Menu(service) == null, "Old file scope reopened a native menu.");
            Call(service, "SendAudioTrackListToOsc");
            menu = await OpenFromOsc();
            foreground.Observe("profile-retirement-before");
            api.SetProfile("replacement"); Call(menu, "Input", "activate");
            await Until(() => Menu(service) == null, "A stale profile menu did not retire.");
            Require(wire.AudioPicks.Count == 0, "A retired profile selected audio.");
            await manager.StartSessionAsync(42, audioTrackIndex: 37);
            Call(service, "SendAudioTrackListToOsc");

            if (directFailureCase)
            {
                var originalOpen = Get(mpv, "_directOpen");
                Require(originalOpen != null, "Signed direct fixture did not register the real native stream callback.");
                retainedDirectCallbacks.Add(originalOpen!);
                var loadFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observeFailure = (_, args) =>
                {
                    if (args.Exception is InvalidOperationException { Message: "Cannot register direct file reader." })
                        loadFailure.TrySetResult();
                };
                var initialLoad = (long)Get(service, "_playbackFileGeneration")!;
                AppDomain.CurrentDomain.FirstChanceException += observeFailure;
                try
                {
                    // Keep the registered delegate rooted while deliberately
                    // exercising libmpv's duplicate-protocol registration error
                    // inside the real accepted Audio selection's BeginMpvLoad.
                    Set(mpv, "_directOpen", null);
                    menu = await OpenFromOsc();
                    Call(menu, "Input", "end"); Call(menu, "Input", "activate");
                    await loadFailure.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Program.Log("Audio fixture observed actual native duplicate-registration exception inside the accepted source71 load.");
                    Require(wire.AudioPicks.Single() == 71 && (long)Get(service, "_playbackFileGeneration")! > initialLoad,
                        "Controlled native registration failure did not occur inside this selection's actual media load.");
                    await Until(() => !service.IsLoading && !(bool)Get(service, "_switchingContent")!,
                        "An Audio selection's own BeginMpvLoad exception stranded loading/busy ownership.");
                    var state = await Query();
                    Require(!string.IsNullOrEmpty(service.ErrorMessage) && state.GetProperty("notice").GetBoolean(),
                        "An Audio selection's own native load failure did not publish its current error and retry notice.");
                    Require(Menu(service) == null && Gate(service).CurrentCount == 1,
                        "Owned load failure stranded native menu presentation or its lease.");
                    Program.Log("PASS: actual native Audio source71 selection handles its own BeginMpvLoad exception from controlled duplicate silo-direct registration, clears loading/busy and shows a current error/notice; original and attempted callbacks remain rooted through native disposal. This does not identify a production failure cause.");
                }
                finally
                {
                    AppDomain.CurrentDomain.FirstChanceException -= observeFailure;
                    if (Get(mpv, "_directOpen") is { } attemptedOpen) retainedDirectCallbacks.Add(attemptedOpen);
                    Set(mpv, "_directOpen", originalOpen);
                }
                return;
            }

            if (Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE") == "retired-presentation")
            {
                service.SetPaused(false);
                await Until(() => !mpv.IsPaused && !service.IsPaused, "Fixture did not enter playing intent.");
                var initialLoad = (long)Get(service, "_playbackFileGeneration")!;
                wire.HoldSuccessfulReplan = true;
                menu = await OpenFromOsc();
                Call(menu, "Input", "end"); Call(menu, "Input", "activate");
                await wire.ReplanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(wire.AudioPicks.Single() == 71, "Actual native keyboard selection did not retain the source index.");
                // This is the real presentation retirement helper invoked by
                // mode/layout transitions. Avoid moving any fixture fullscreen.
                Call(service, "RetireNativeMenus");
                wire.ReleaseReplan.TrySetResult();
                await wire.ReplanReplied.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Until(() => !service.IsLoading && !(bool)Get(service, "_switchingContent")!,
                    "Retiring native Audio presentation stranded an already accepted selection in loading/busy state.");
                await Until(() => !mpv.IsPaused && !service.IsPaused,
                    "Accepted Audio selection did not restore playing intent after presentation retirement.");
                Require((long)Get(service, "_playbackFileGeneration")! > initialLoad
                    && manager.CurrentSession?.AudioTrackIndex == 71 && !mpv.IsPaused && !service.IsPaused,
                    "Accepted Audio selection did not finish its load after presentation retirement.");
                Require(Menu(service) == null && Gate(service).CurrentCount == 1,
                    "Presentation retirement stranded a native menu or lease.");
                Program.Log("PASS: actual native keyboard Audio selection continues its accepted source71 replan across the real presentation retirement helper, clears loading/busy and retains playing intent. No physical mode transition asserted.");
                return;
            }

            var retirementCase = Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE");
            if (retirementCase is "retired-replan" or "retired-success" or "retired-cancellation"
                or "successor-seek" or "successor-recovery" or "live-successor-seek" or "live-successor-recovery"
                or "live-successor-success-seek")
            {
                service.SetPaused(false);
                await Until(() => !mpv.IsPaused && !service.IsPaused, "Fixture did not enter playing intent.");
                var initialError = service.ErrorMessage;
                var initialLoad = (long)Get(service, "_playbackFileGeneration")!;
                wire.HoldSuccessfulReplan = retirementCase is "retired-success" or "live-successor-success-seek";
                wire.HoldFailedReplan = !wire.HoldSuccessfulReplan;
                using var cancellation = new CancellationTokenSource();
                if (retirementCase == "retired-cancellation") Set(service, "_playbackCts", cancellation);
                if (retirementCase!.Contains("successor", StringComparison.Ordinal))
                {
                    var prepare = (Task)Call(service, "PreparePlaybackTransportAsync", manager.CurrentSession,
                        version, manager.StreamUrl, 0d, CancellationToken.None)!;
                    await prepare;
                    Call(service, "ApplyPreparedTransport", prepare.GetType().GetProperty("Result")!.GetValue(prepare));
                }
                menu = await OpenFromOsc();
                Call(menu, "Input", "end"); Call(menu, "Input", "activate");
                await wire.ReplanStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(wire.AudioPicks.Single() == 71, "Actual native keyboard selection did not retain the source index.");
                var originalScope = (await Query()).GetProperty("scope").GetString();
                Task? successor = null;
                if (retirementCase!.Contains("successor", StringComparison.Ordinal))
                {
                    wire.HoldSuccessorReplan = true;
                    successor = retirementCase.EndsWith("seek", StringComparison.Ordinal)
                        ? (Task)Call(service, "RestartTransportForSeekAsync", 2d, false,
                            (long)Get(service, "_seekRestartGeneration")!, cancellation)!
                        : (Task)Call(service, "RecoverInterruptedStreamAsync", 2d, "playback-error", null)!;
                    await Until(() => retirementCase.EndsWith("seek", StringComparison.Ordinal)
                        ? ((SemaphoreSlim)Get(service, "_transportRestartGate")!).CurrentCount == 0
                        : (bool)Get(service, "_prematureEofRecoveryActive")!, "Successor did not enter its actual busy flow.");
                }
                if (retirementCase == "retired-cancellation") cancellation.Cancel();
                else if (successor != null && !retirementCase!.StartsWith("live-", StringComparison.Ordinal))
                {
                    // Retire only Audio's inventory while keeping the queued
                    // real seek/recovery's API session authorized. A profile
                    // switch correctly cancels both operations in PlaybackApi.
                    version.AudioTracks = version.AudioTracks!.Take(2).ToList();
                }
                else if (successor == null) api.SetProfile("retired-during-replan");
                wire.ReleaseReplan.TrySetResult();
                await wire.ReplanReplied.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // Observe the actual asynchronous error publication, including a
                // Lua round trip, after the held reply has been delivered.
                await Task.Delay(500);
                var after = await Query();
                if (successor == null)
                    Require(!service.IsLoading && !(bool)Get(service, "_switchingContent")!,
                        "A retired profile's native Audio replan stranded loading/busy state.");
                else
                {
                    Program.Log($"Successor probe: kind={retirementCase}, status={successor.Status}, requests={wire.AudioPicks.Count}, loading={service.IsLoading}, switching={Get(service, "_switchingContent")}, replanGate={((SemaphoreSlim)Get(manager, "_replanGuard")!).CurrentCount}, seekGate={((SemaphoreSlim)Get(service, "_transportRestartGate")!).CurrentCount}.");
                    await wire.SuccessorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Require(service.IsLoading && (bool)Get(service, "_switchingContent")! && !successor.IsCompleted,
                        "Retired Audio released a successor's still-pending loading/busy state.");
                }
                Require(service.ErrorMessage == initialError && !after.GetProperty("notice").GetBoolean(),
                    "A retired profile's native Audio replan published an error or notice.");
                Require((long)Get(service, "_playbackFileGeneration")! == initialLoad,
                    "A retired profile's native Audio replan began loading media.");
                Require(after.GetProperty("scope").GetString() is "" || after.GetProperty("scope").GetString() == originalScope,
                    "A retired profile's native Audio replan published a new Audio owner.");
                if (successor != null)
                {
                    wire.ReleaseSuccessor.TrySetResult();
                    await successor.WaitAsync(TimeSpan.FromSeconds(8));
                    await Until(() => !service.IsLoading && !(bool)Get(service, "_switchingContent")!,
                        "Successor did not finish its own loading/busy lifecycle.");
                }
                Program.Log($"PASS: {retirementCase}: actual Audio replan retirement releases only its own busy flags, suppresses stale publication/load, preserves pending successor flags and lets that successor complete.");
                if (retirementCase == "retired-cancellation") Set(service, "_playbackCts", null);
                return;
            }

            // Actual UIA Invoke uses the immutable source index through the real
            // replan caller, with native audio IDs deliberately irrelevant.
            service.SetPaused(false);
            await Until(() => !mpv.IsPaused && !service.IsPaused, "Fixture did not enter playing intent.");
            var latePreferenceCase = Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE") == "late-preference";
            wire.HoldPreference = latePreferenceCase;
            menu = await OpenFromOsc(); list = (ListView)Get(menu, "_list")!;
            Call(menu, "Input", "end");
            var last = (ListViewItem)list.Items[71];
            await Until(() => last.XamlRoot != null && last.ActualHeight > 0, "Last native audio row was not realized.");
            list.ItemClick += (_, args) => Program.Log("Audio fixture observed actual ItemClick payload: " + args.ClickedItem?.GetType().FullName);
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_SKIP_UIA") == "1")
            {
                foreground.Observe("keyboard-invoke-before");
                Call(menu, "Input", "activate");
                foreground.Observe("keyboard-invoke-after");
            }
            else
            {
                foreground.Observe("uia-peer-before");
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(last) ?? new ListViewItemAutomationPeer(last);
                if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                    throw new InvalidOperationException("Actual Audio row has no UIA Invoke pattern.");
                // WinUI's UIA Invoke explicitly asks to activate its window
                // before raising ItemClick. Isolate that framework request on
                // the interactive development desktop; this does not certify
                // normal UIA focus/activation or change product behavior.
                using (var veto = new OwnMenuActivationVeto(MenuHwnd(menu)))
                {
                    foreground.Observe("uia-invoke-before");
                    invoke.Invoke();
                    foreground.Observe("uia-invoke-after");
                }
                foreground.Observe("uia-isolation-ended");
            }
            await Until(() => wire.AudioPicks.Count == 1, "Native Audio Invoke did not reach the existing replan caller.");
            foreground.Observe("accepted-replan-observed");
            Require(wire.AudioPicks.Single() == 71, "Native Audio reinterpreted a source index as an mpv track ID.");
            await Until(() => !service.IsLoading && !mpv.IsPaused && !service.IsPaused, "Accepted Audio replan did not preserve playing intent.");
            if (latePreferenceCase)
            {
                await wire.PreferenceStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Delay(100); // FILE_LOADED initialization has its own publication.
                var publication = (long)Get(service, "_nativeMenuPublication")!;
                wire.ReleasePreference.TrySetResult();
                await Until(() => (long)Get(service, "_nativeMenuPublication")! > publication,
                    "Accepted Audio did not finish publication after FILE_LOADED and a late preference failure.");
                Require(!service.IsLoading && !(bool)Get(service, "_switchingContent")! &&
                    !(await Query()).GetProperty("notice").GetBoolean(),
                    "Late optional preference failure changed accepted playback busy/error state.");
            }
            foreground.Observe("accepted-replan-loaded");
            Require(Menu(service) == null && Gate(service).CurrentCount == 1, "Accepted Audio selection stranded a menu or gate.");
            Require((await Query()).GetProperty("closeConsume").GetString() == "false",
                "UIA/keyboard Audio selection incorrectly claimed a pointer gesture.");
            var position = mpv.Position; await Task.Delay(250);
            Require(mpv.Position > position || position > 3.7 && mpv.Position < position, "Synthetic playback stopped after Audio selection.");
            Call(service, "SendAudioTrackListToOsc");
            menu = await OpenFromOsc(); hwnd = MenuHwnd(menu);
            foreground.Observe("owner-hide-before");
            video.Hide();
            foreground.Observe("owner-hide-after");
            await Until(() => Menu(service) == null && !IsWindow(hwnd), "Hiding the native owner stranded its Audio menu.");
            Require(Gate(service).CurrentCount == 1, "Owner hide failed to release the Audio dialog lease.");
            foreground.Observe("normal-complete");
            Program.Log("PASS: native Audio actual OSC/service/owned window, current metadata, 72-row native scroll and source-index replan; stale callbacks/file/profile/dialog ownership, pointer dismissal, pause/play continuity and HWND/gate cleanup. UIA command routing uses explicit activation isolation; foreground is checked separately.");
        }
        finally
        {
            wire.ReleaseReplan.TrySetResult();
            wire.ReleaseSuccessor.TrySetResult();
            wire.ReleasePreference.TrySetResult();
            mpv.ScriptMessageReceived -= Message;
            if (Menu(service) is { } menu) Call(menu, "Input", "cancel");
            Call(service, "StopPlaybackStallWatchdog"); Call(service, "DisconnectWebSocket");
            Set(service, "_videoWindow", null); Set(service, "_mpv", null); Set(service, "_playbackManager", null);
            video.Dispose();
            mpv.Dispose();
            foreach (var callback in retainedDirectCallbacks) GC.KeepAlive(callback);
            // Out-of-context WinEvents run on this UI thread. Pump queued events
            // after native cleanup before checking only our process's ownership.
            await Task.Delay(40);
            foreground.Complete();
        }

        async Task<JsonElement> Query()
        {
            var id = Guid.NewGuid().ToString("N"); var receiver = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queries[id] = receiver;
            try { mpv.SendScriptMessage("osc-audio-fixture-query", id); return await receiver.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
            finally { queries.TryRemove(id, out _); }
        }
        async Task TriggerOsc()
        {
            foreground.Observe("osc-trigger-before");
            var geometry = await Query();
            Require(geometry.GetProperty("w").GetDouble() > 0, "Actual Audio opener is unavailable.");
            var x = geometry.GetProperty("x").ToString(); var y = geometry.GetProperty("y").ToString();
            mpv.SendScriptMessage("osc-show");
            mpv.SendScriptMessage("osc-mouse-move", x, y); mpv.SendScriptMessage("osc-mouse-down", x, y);
            mpv.SendScriptMessage("osc-mouse-up", x, y); mpv.SendScriptMessage("osc-video-click", x, y);
            foreground.Observe("osc-trigger-sent");
        }
        async Task<object> OpenFromOsc()
        {
            if (compactClickCase)
            {
                var opener = await Query();
                NativeClick(opener.GetProperty("x").GetDouble(), opener.GetProperty("y").GetDouble());
                await Task.Delay(600); // Isolate Audio from More's existing gesture timer.
                if (Menu(service) is { } parent && RequestKind() == "more")
                {
                    foreground.WatchMenu((Window)Get(parent, "_window")!, MenuHwnd(parent));
                    var rows = (ListView)Get(parent, "_list")!;
                    Require(rows.Items.OfType<ListViewItem>().Any(row => row.Tag is "audio" && row.IsEnabled),
                        "Native More did not expose its enabled Audio action.");
                    // Execute the actual native pointer-selection close/handoff
                    // boundary; this is not a physical pointer-input assertion.
                    Call(parent, "Select", "audio", true);
                    await Until(() => !ReferenceEquals(Menu(service), parent) && RequestKind() == "audio",
                        "Native More did not hand off its pointer-selected Audio action.");
                }
                else
                {
                    var compact = await Query();
                    Require(compact.GetProperty("compactX").GetDouble() > 0, "Compact Audio row was unavailable.");
                    NativeClick(compact.GetProperty("compactX").GetDouble(), compact.GetProperty("compactY").GetDouble());
                }
            }
            else await TriggerOsc();
            await Until(() => RequestKind() == "audio" && Menu(service) is { } shown &&
                Get(shown, "_list") is ListView { XamlRoot: not null, Items.Count: 72 },
                "Actual OSC Audio gesture produced no native Audio menu.");
            foreground.WatchMenu((Window)Get(Menu(service)!, "_window")!, MenuHwnd(Menu(service)!));
            foreground.Observe("menu-opened", MenuHwnd(Menu(service)!), "audio-menu");
            return Menu(service)!;
        }
        string? RequestKind()
        {
            var request = Get(service, "_nativeMenuRequest");
            return request?.GetType().GetProperty("Kind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(request) as string;
        }
        void NativeClick(double x, double y)
        {
            // Deliver the actual OSC pointer protocol without Windows
            // capture/focus changes on an offscreen interactive desktop.
            var px = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var py = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
            mpv.SendScriptMessage("osc-mouse-move", px, py);
            mpv.SendScriptMessage("osc-mouse-down", px, py);
            mpv.SendScriptMessage("osc-mouse-up", px, py);
            mpv.SendScriptMessage("osc-video-click", px, py);
        }
        void Host(params string[] args) => Call(service, "OnScriptMessage", (object)args);
    }

    private static object? Menu(PlayerService service) => typeof(PlayerService).GetField("_nativeMenu", Private)?.GetValue(service);
    private static bool SelectedRowFits(ListView list)
    {
        if (list.SelectedItem is not FrameworkElement row || row.ActualHeight <= 0 || list.ActualHeight <= 0) return false;
        var bounds = row.TransformToVisual(list).TransformBounds(new Windows.Foundation.Rect(0, 0, row.ActualWidth, row.ActualHeight));
        return bounds.Top >= -1 && bounds.Bottom <= list.ActualHeight + 1;
    }
    private static SemaphoreSlim Gate(PlayerService service) => (SemaphoreSlim)Get(service, "_playbackDialogGate")!;
    private static IntPtr MenuHwnd(object menu)
    {
        for (var type = menu.GetType(); type != null; type = type.BaseType)
            if (type.GetProperty("Hwnd", Private | BindingFlags.DeclaredOnly) is { } property) return (IntPtr)property.GetValue(menu)!;
        throw new MissingMemberException("Native menu HWND");
    }
    private static object? Get(object target, string field)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetField(field, Private | BindingFlags.DeclaredOnly) is { } member) return member.GetValue(target);
        throw new MissingFieldException(target.GetType().FullName, field);
    }
    private static void Set(object target, string field, object? value) => target.GetType().GetField(field, Private)!.SetValue(target, value);
    private static void Property(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
    private static object? Call(object target, string method, params object?[] args)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
            if (type.GetMethod(method, Private | BindingFlags.DeclaredOnly) is { } member) return member.Invoke(target, args);
        throw new MissingMethodException(target.GetType().FullName, method);
    }
    private static void Command(MpvPlayer player, params string[] args) => Require(Call(player, "Command", (object)args) is true, "Native Audio fixture command failed: " + args[0]);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> ready, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!ready()) { if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new InvalidOperationException(message); await Task.Delay(20); }
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
    internal static async Task Capture(object menu, string name)
    {
        var root = (Grid)Get(menu, "_root")!;
        await Until(() => root.ActualWidth > 0 && root.ActualHeight > 0, "Native Audio layout has no size.");
        await Task.Delay(80);
        var target = new RenderTargetBitmap(); await target.RenderAsync(root);
        var pixels = await target.GetPixelsAsync();
        var audioCase = Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE");
        var path = Path.Combine(Program.ResultDirectory, string.IsNullOrEmpty(audioCase) ? name : audioCase + "-" + name);
        await File.WriteAllBytesAsync(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        using var reader = DataReader.FromBuffer(pixels); var data = new byte[pixels.Length]; reader.ReadBytes(data);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)target.PixelWidth, (uint)target.PixelHeight, 96, 96, data);
        await encoder.FlushAsync();
    }

    private static FileVersion Version() => new()
    {
        FileId = 42, Duration = 60, AudioTracks = Enumerable.Range(0, 72).Select(i => new AudioTrackInfo
        {
            Title = i == 71 ? "A deliberately long multilingual audio label — 日本語 — " + new string('x', 180) : i == 1 ? "" : i == 2 ? "Duplicate title" : $"Audio source {i}",
            Language = i == 37 ? "ja" : "en", Codec = "aac", Channels = 2, Layout = "stereo", Default = i == 0,
        }).ToList(),
    };

    private sealed class Wire(string origin, string mediaPath) : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<int> AudioPicks = new();
        internal bool HoldFailedReplan;
        internal bool HoldSuccessfulReplan;
        internal bool HoldSuccessorReplan;
        internal bool HoldPreference;
        internal readonly TaskCompletionSource PreferenceStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleasePreference = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool SuccessorEntered;
        internal readonly TaskCompletionSource SuccessorStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseSuccessor = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReplanStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseReplan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReplanReplied = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _audio = 37;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri?.Host == "native-audio.fixture.invalid", "Synthetic Audio API attempted external networking.");
            var path = request.RequestUri!.AbsolutePath;
            if (HoldPreference && request.Method == HttpMethod.Put && path.Contains("/audio-prefs/", StringComparison.Ordinal))
            {
                PreferenceStarted.TrySetResult();
                await ReleasePreference.Task;
                return new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") };
            }
            if (path.Contains("/settings/", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            if (path.EndsWith("/capabilities")) return Json("""{"allowed":true,"state":"available","protocol_versions":[3],"features":["fixed_media_file_v1"],"installation_id":"11111111-1111-4111-8111-111111111111"}""");
            if (!path.EndsWith("/start") && !path.EndsWith("/replan")) return Json("""{"outcome":"applied"}""");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = doc.RootElement;
            if (path.EndsWith("/replan"))
            {
                _audio = body.GetProperty("selected_tracks").GetProperty("audio").GetProperty("index").GetInt32();
                AudioPicks.Enqueue(_audio);
                if (AudioPicks.Count > 1 && HoldSuccessorReplan)
                {
                    SuccessorEntered = true;
                    SuccessorStarted.TrySetResult();
                    await ReleaseSuccessor.Task;
                }
                else if (HoldFailedReplan || HoldSuccessfulReplan)
                {
                    ReplanStarted.TrySetResult();
                    // Deliberately return a reply after context retirement,
                    // modelling an HTTP failure already in flight.
                    await ReleaseReplan.Task;
                    ReplanReplied.TrySetResult();
                    if (HoldFailedReplan)
                        return new(HttpStatusCode.InternalServerError)
                            { Content = new StringContent("{\"error\":\"Synthetic late audio replan failure\"}", Encoding.UTF8, "application/json") };
                }
            }
            return Json(JsonSerializer.Serialize(new
            {
                protocol_version = 3, outcome = "playable", session_id = "native-audio-fixture",
                playback_plan = new
                {
                    plan_id = $"audio-plan-{AudioPicks.Count}", plan_attempt_key = "synthetic", session_id = "native-audio-fixture", effective_media_file_id = 42,
                    delivery = "original_http", stream = new { url = origin + mediaPath }, timeline = new { can_seek_anywhere = true }, source = new { duration_seconds = 60 },
                    selected_tracks = new { audio = new { id = $"file:42:audio:{_audio}", index = _audio }, subtitle = (object?)null },
                    subtitle = new { mode = "off", inventory = Array.Empty<object>() },
                },
            }));
        }
        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }

    internal sealed class MediaServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        internal string Origin { get; }
        internal MediaServer()
        {
            using var clip = new MemoryStream(); clip.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W160 H90 F30:1 Ip A1:1 C420jpeg\n"));
            var pixels = new byte[160 * 90 * 3 / 2]; Array.Fill(pixels, (byte)128);
            for (var frame = 0; frame < 900; frame++)
            {
                Array.Fill(pixels, (byte)(32 + frame % 100), 0, 160 * 90);
                clip.Write(Encoding.ASCII.GetBytes("FRAME\n")); clip.Write(pixels);
            }
            var bytes = clip.ToArray();
            var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Origin = $"http://127.0.0.1:{port}"; _listener.Prefixes.Add(Origin + "/"); _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await _listener.GetContextAsync(); } catch (Exception) when (!_listener.IsListening) { break; }
                    if (context.Request.Url?.AbsolutePath == "/fixture-subtitle.vtt")
                    {
                        var subtitle = Encoding.UTF8.GetBytes("WEBVTT\n\n00:00.000 --> 01:00.000\nSynthetic subtitle\n");
                        context.Response.ContentType = "text/vtt"; context.Response.ContentLength64 = subtitle.Length;
                        try { await context.Response.OutputStream.WriteAsync(subtitle); context.Response.Close(); } catch (HttpListenerException) { }
                        continue;
                    }
                    var from = 0; var through = bytes.Length - 1;
                    context.Response.ContentType = "application/octet-stream";
                    context.Response.Headers["Accept-Ranges"] = "bytes";
                    context.Response.Headers["ETag"] = "\"synthetic-audio-video-v1\"";
                    var range = context.Request.Headers["Range"];
                    if (range?.StartsWith("bytes=", StringComparison.Ordinal) == true)
                    {
                        var edges = range[6..].Split('-');
                        if (edges.Length == 2 && int.TryParse(edges[0], out from) && from >= 0 && from < bytes.Length)
                        {
                            if (int.TryParse(edges[1], out var requestedEnd)) through = Math.Min(through, requestedEnd);
                            context.Response.StatusCode = 206;
                            context.Response.Headers["Content-Range"] = $"bytes {from}-{through}/{bytes.Length}";
                        }
                        else
                        {
                            context.Response.StatusCode = 416;
                            context.Response.Headers["Content-Range"] = $"bytes */{bytes.Length}";
                            context.Response.ContentLength64 = 0;
                            context.Response.Close();
                            continue;
                        }
                    }
                    context.Response.ContentLength64 = through - from + 1;
                    try { if (context.Request.HttpMethod != "HEAD") await context.Response.OutputStream.WriteAsync(bytes.AsMemory(from, through - from + 1)); context.Response.Close(); }
                    catch (HttpListenerException) { }
                }
            });
        }
        public void Dispose() => _listener.Close();
    }

    /// <summary>
    /// Observe only foreground events generated by this fixture process. A user
    /// switching between unrelated applications is not a fixture activation.
    /// No external window title, content or control is inspected.
    /// </summary>
    internal sealed class ForegroundOwnershipProbe : IDisposable
    {
        private readonly uint _processId = (uint)Environment.ProcessId;
        private readonly ConcurrentQueue<ForegroundEvent> _ownedEvents = new();
        private readonly List<ForegroundSample> _samples = [];
        private readonly Dictionary<long, string> _roles = [];
        private readonly HashSet<Window> _watchedMenus = [];
        private string _phase = "constructor";
        private bool _callbackFailed;
        private readonly WinEventCallback _callback;
        private GCHandle _callbackRoot;
        private IntPtr _hook;

        internal ForegroundOwnershipProbe(IntPtr main)
        {
            _roles[main.ToInt64()] = "fixture-main";
            _callback = (_, _, hwnd, _, _, thread, eventTime) =>
            {
                // The hook filters the originating PID. Delivery is queued:
                // the owned HWND may already be closed or reused by now.
                try
                {
                    if (hwnd == IntPtr.Zero) return;
                    var current = GetForegroundWindow();
                    GetWindowThreadProcessId(current, out var process);
                    _ownedEvents.Enqueue(new(hwnd.ToInt64(), thread, eventTime, Environment.TickCount64,
                        _phase, current.ToInt64(), process));
                }
                catch { _callbackFailed = true; }
            };
            _callbackRoot = GCHandle.Alloc(_callback);
            // EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT; nonzero process
            // filter avoids observing other processes' foreground event streams.
            _hook = SetWinEventHook(3, 3, IntPtr.Zero, _callback, _processId, 0, 0);
            if (_hook == IntPtr.Zero)
            {
                _callbackRoot.Free();
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot observe fixture foreground ownership.");
            }
            Observe("before-fixture");
        }

        internal void Observe(string stage, IntPtr hwnd = default, string? role = null)
        {
            _phase = stage;
            if (hwnd != IntPtr.Zero && role != null) _roles[hwnd.ToInt64()] = role;
            var current = GetForegroundWindow();
            GetWindowThreadProcessId(current, out var process);
            _samples.Add(new(stage, current.ToInt64(), process, process == _processId, Environment.TickCount64));
        }

        internal void WatchMenu(Window menu, IntPtr hwnd)
        {
            if (!_watchedMenus.Add(menu)) return;
            _roles[hwnd.ToInt64()] = "audio-menu";
            void Activated(object sender, WindowActivatedEventArgs args)
                => Observe("menu-activated-" + args.WindowActivationState, hwnd, "audio-menu");
            void Closed(object sender, WindowEventArgs args)
            {
                Observe("menu-closed", hwnd, "audio-menu");
                menu.Activated -= Activated; menu.Closed -= Closed;
                _watchedMenus.Remove(menu);
            }
            menu.Activated += Activated; menu.Closed += Closed;
        }

        internal void Complete()
        {
            Observe("after-fixture-cleanup");
            var first = _samples[0]; var last = _samples[^1];
            var ownedEvents = _ownedEvents.Select(observation => new
            {
                hwnd = observation.Hwnd,
                role = _roles.GetValueOrDefault(observation.Hwnd, "other-fixture-window"),
                observation.EventThread, observation.EventTime, observation.CallbackTick,
                observation.PhaseAtDelivery, observation.ForegroundAtDelivery, observation.ForegroundProcessAtDelivery,
            }).ToArray();
            var caseName = Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_CASE");
            if (string.IsNullOrEmpty(caseName)) caseName = "normal";
            File.WriteAllText(Path.Combine(Program.ResultDirectory, caseName + "-foreground.json"), JsonSerializer.Serialize(new
            {
                fixtureProcessId = _processId, first, last, samples = _samples,
                ownedForegroundEvents = ownedEvents,
                callbackFailed = _callbackFailed,
                skipUia = Environment.GetEnvironmentVariable("SILO_NATIVE_AUDIO_SKIP_UIA") == "1",
                externalForegroundChanged = first.Hwnd != last.Hwnd && !first.BelongsToFixture && !last.BelongsToFixture,
                scope = "Own-process foreground WinEvents and explicit HWND/PID snapshots only; no external titles/content or foreground control.",
            }, new JsonSerializerOptions { WriteIndented = true }));
            Program.Log($"Audio foreground {caseName}: initial HWND={first.Hwnd}, PID={first.ProcessId}; final HWND={last.Hwnd}, PID={last.ProcessId}; fixture PID={_processId}; owned events={ownedEvents.Length}.");
            Require(!_callbackFailed && ownedEvents.Length == 0 && !_samples.Any(sample => sample.BelongsToFixture),
                "Offscreen native Audio verification activated a fixture-owned foreground window.");
        }

        public void Dispose()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWinEvent(_hook); _hook = IntPtr.Zero;
            _callbackRoot.Free(); GC.KeepAlive(_callback);
        }

        private sealed record ForegroundSample(string Stage, long Hwnd, uint ProcessId, bool BelongsToFixture, long Tick);
        private sealed record ForegroundEvent(long Hwnd, uint EventThread, uint EventTime, long CallbackTick,
            string PhaseAtDelivery, long ForegroundAtDelivery, uint ForegroundProcessAtDelivery);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void WinEventCallback(IntPtr hook, uint eventId, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    }

    /// <summary>
    /// Test isolation for the framework's explicit UIA Invoke activation.
    /// Applies only to this exact menu HWND, current fixture PID and UI thread.
    /// </summary>
    private sealed class OwnMenuActivationVeto : IDisposable
    {
        private readonly IntPtr _menu;
        private readonly uint _process = (uint)Environment.ProcessId;
        private readonly uint _thread = GetCurrentThreadId();
        private readonly List<object> _attempts = [];
        private readonly CbtCallback _callback;
        private GCHandle _callbackRoot;
        private IntPtr _hook;
        private bool _callbackFailed;

        internal int BlockedCount => _attempts.Count;

        internal OwnMenuActivationVeto(IntPtr menu)
        {
            _menu = menu;
            var targetThread = GetWindowThreadProcessId(menu, out var process);
            Require(process == _process && targetThread == _thread, "UIA isolation target is not on the fixture's own UI thread.");
            _callback = (code, window, data) =>
            {
                if (code == 5 && window == _menu) // HCBT_ACTIVATE
                {
                    GetWindowThreadProcessId(window, out var targetProcess);
                    if (targetProcess == _process)
                    {
                        try
                        {
                            var foreground = GetForegroundWindow();
                            GetWindowThreadProcessId(foreground, out var foregroundProcess);
                            _attempts.Add(new { tick = Environment.TickCount64, hwnd = window.ToInt64(),
                                fixtureThread = _thread, foreground = foreground.ToInt64(), foregroundProcess });
                        }
                        catch { _callbackFailed = true; }
                        return new IntPtr(1); // Veto only this owned activation.
                    }
                }
                return CallNextHookEx(_hook, code, window, data);
            };
            _callbackRoot = GCHandle.Alloc(_callback);
            _hook = SetWindowsHookExW(5, _callback, IntPtr.Zero, _thread); // WH_CBT, own thread only
            if (_hook == IntPtr.Zero)
            {
                _callbackRoot.Free();
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot isolate owned UIA activation.");
            }
        }

        public void Dispose()
        {
            if (_hook == IntPtr.Zero) return;
            if (!UnhookWindowsHookEx(_hook))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot remove owned UIA activation isolation.");
            _hook = IntPtr.Zero;
            _callbackRoot.Free(); GC.KeepAlive(_callback);
            File.WriteAllText(Path.Combine(Program.ResultDirectory, "normal-uia-veto.json"), JsonSerializer.Serialize(new
            {
                hwnd = _menu.ToInt64(), fixtureProcess = _process, fixtureThread = _thread,
                blockedActivationAttempts = _attempts, callbackFailed = _callbackFailed,
                scope = "Only the synchronous explicit UIA Invoke call; unhooked before awaiting backend completion. UIA focus/activation was blocked in this isolated fixture and is not certified. No external HWND or thread is vetoed.",
            }, new JsonSerializerOptions { WriteIndented = true }));
            Require(!_callbackFailed, "Owned UIA isolation callback failed.");
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr CbtCallback(int code, IntPtr window, IntPtr data);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookExW(int hook, CbtCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr window, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
