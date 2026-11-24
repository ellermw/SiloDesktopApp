using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;

internal static partial class PlaybackAudioMenuNativeFixture
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attribute, ref int value, uint size);

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureWindow
    {
        IntPtr CreateForWindow(IntPtr hwnd, in Guid iid);
    }

    private static async Task CheckModalAlphaAsync(IntPtr hwnd)
    {
        // Capture only this synthetic dialog HWND, never a display or another application.
        var factory = Windows.Graphics.Capture.GraphicsCaptureItem.As<ICaptureWindow>();
        var pointer = factory.CreateForWindow(hwnd, new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"));
        Windows.Graphics.Capture.GraphicsCaptureItem item;
        try { item = Windows.Graphics.Capture.GraphicsCaptureItem.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
        var device = Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
        using var frames = Windows.Graphics.Capture.Direct3D11CaptureFramePool.CreateFreeThreaded(device,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        using var session = frames.CreateCaptureSession(item);
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        frames.FrameArrived += (_, _) => arrived.TrySetResult();
        session.StartCapture();
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var frame = frames.TryGetNextFrame();
        using var bitmap = Microsoft.Graphics.Canvas.CanvasBitmap.CreateFromDirect3D11Surface(device, frame.Surface);
        await bitmap.SaveAsync(System.IO.Path.Combine(Program.ResultDirectory, "modal-compositor.png"), Microsoft.Graphics.Canvas.CanvasBitmapFileFormat.Png);
        var size = bitmap.SizeInPixels;
        var sample = bitmap.GetPixelBytes((int)size.Width / 8, (int)size.Height / 8, 1, 1);
        var center = bitmap.GetPixelBytes((int)size.Width / 2, (int)size.Height / 2, 1, 1);
        Require(center[3] == 255, "AI dialog panel was absent at the center of the video.");
        Program.Log($"Native compositor dialog pixel BGRA={string.Join(",", sample)}");
        Require(sample[3] is > 0 and < 255, "AI dialog compositor backdrop was opaque or absent.");
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect bounds);

    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect bounds, IntPtr context);
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr context);

    private delegate bool ThreadWindowCallback(IntPtr hwnd, IntPtr context);
    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, ThreadWindowCallback callback, IntPtr context);

    private static async Task CheckModalDisplaysAsync(Window window, MpvVideoWindow video, IntPtr dialog, XamlRoot root)
    {
        // Exercise real monitor coordinates and DPI transitions without showing any fixture on the desktop.
        var original = window.AppWindow.Position;
        var originalSize = window.AppWindow.Size;
        var handles = new[] { WinRT.Interop.WindowNative.GetWindowHandle(window), video.Hwnd, dialog };
        var cloak = 1;
        foreach (var hwnd in handles)
            Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(hwnd, 13 /* DWMWA_CLOAK */, ref cloak, sizeof(int)));
        var scales = new HashSet<uint>();
        var displays = new List<Rect>();
        Require(EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref Rect bounds, IntPtr _) =>
        {
            displays.Add(bounds);
            return true;
        }, IntPtr.Zero) && displays.Count > 0, "Cannot enumerate monitor bounds.");

        bool OtherDisplaysCovered(Rect owner, Rect dialogDisplay)
        {
            var expected = displays.Where(screen => !screen.Equals(dialogDisplay)).Select(screen => new Rect
            {
                Left = Math.Max(owner.Left, screen.Left), Top = Math.Max(owner.Top, screen.Top),
                Right = Math.Min(owner.Right, screen.Right), Bottom = Math.Min(owner.Bottom, screen.Bottom),
            }).Where(rect => rect.Left < rect.Right && rect.Top < rect.Bottom).ToHashSet();
            var valid = true;
            return EnumThreadWindows(GetWindowThreadProcessId(dialog, out _), (hwnd, context) =>
            {
                if (GetWindow(hwnd, 4 /* GW_OWNER */) == dialog)
                {
                    valid &= IsWindowVisible(hwnd) && GetWindowRect(hwnd, out var bounds) && expected.Remove(bounds) &&
                        (GetWindowLongPtrW(hwnd, -20 /* GWL_EXSTYLE */).ToInt64() & 0x08000000) != 0 &&
                        SendMessageW(hwnd, 0x0021 /* WM_MOUSEACTIVATE */, hwnd, new IntPtr(0x02010001)).ToInt64() == 4;
                }
                return true;
            }, IntPtr.Zero) && valid && expected.Count == 0;
        }

        try
        {
            foreach (var screen in displays)
            {
                foreach (var x in new[] { screen.Left + 30, screen.Left - 120 })
                {
                    window.AppWindow.Move(new(x, screen.Top + 30));
                    video.SynchronizePosition();
                    await Until(() =>
                    {
                        if (!GetWindowRect(video.Hwnd, out var owner) || !GetWindowRect(dialog, out var modal) ||
                            !GetClientRect(dialog, out var client)) return false;
                        var scale = root.RasterizationScale;
                        return Math.Abs(root.Size.Width * scale - client.Right) <= 1 &&
                            Math.Abs(root.Size.Height * scale - client.Bottom) <= 1 &&
                            owner.Left >= x && owner.Left < x + 50 && modal.Left == Math.Max(owner.Left, screen.Left) &&
                            modal.Top == Math.Max(owner.Top, screen.Top) && modal.Right == Math.Min(owner.Right, screen.Right) &&
                            modal.Bottom == Math.Min(owner.Bottom, screen.Bottom) && OtherDisplaysCovered(owner, screen);
                    }, "AI dialog left the selected display or failed to cover video on another display without activation.");
                    scales.Add(GetDpiForWindow(dialog));
                }
            }
            var largest = displays.MaxBy(r => (long)(r.Right - r.Left) * (r.Bottom - r.Top));
            var width = largest.Right - largest.Left;
            var height = largest.Bottom - largest.Top;
            window.AppWindow.MoveAndResize(new RectInt32(largest.Left - 100, largest.Top - 100, width + 200, height + 200));
            video.SynchronizePosition();
            await Until(() => GetWindowRect(video.Hwnd, out var owner) && GetWindowRect(dialog, out var modal) &&
                owner.Left < largest.Left && owner.Right > largest.Right && owner.Top < largest.Top && owner.Bottom > largest.Bottom &&
                modal.Left == largest.Left && modal.Right == largest.Right && modal.Top == largest.Top && modal.Bottom == largest.Bottom &&
                OtherDisplaysCovered(owner, largest),
                "Oversized playback window let the AI dialog extend beyond its display or left video uncovered on another display.");
            Program.Log($"PASS: AI dialog host stayed within {width}x{height} while the scrims covered visible video on every display.");
            Program.Log($"PASS: AI dialog followed the video on {displays.Count} display(s) at {string.Join(", ", scales.Order())} DPI, including partially off-monitor placement.");
        }
        finally
        {
            // The fixture owns these windows until teardown. Keep them cloaked even on a failure.
            window.AppWindow.MoveAndResize(new RectInt32(original.X, original.Y, originalSize.Width, originalSize.Height));
            video.SynchronizePosition();
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    internal static async Task RunSubtitleDialogsAsync(Window outer)
    {
        using var foreground = new ForegroundOwnershipProbe(WinRT.Interop.WindowNative.GetWindowHandle(outer), "subtitle-dialogs");
        // Reuse the established synthetic shell, including isolated settings,
        // disabled receiver and detached account startup. No installed app starts.
        var scopeType = typeof(EpisodeQuickPlayNativeFixture).GetNestedType("LifecycleScope", BindingFlags.NonPublic)!;
        var scope = Activator.CreateInstance(scopeType, nonPublic: true)!;
        T Scope<T>(string name) => (T)scopeType.GetProperty(name, Private | BindingFlags.Public)!.GetValue(scope)!;
        var window = Scope<MainWindow>("Window");
        var service = Scope<PlayerService>("Player");
        var api = Scope<SiloApiClient>("Client");
        var auth = Scope<AuthService>("Auth");
        var wire = Scope<object>("Wire");
        using var media = new MediaServer();
        using var manager = new PlaybackManager(Scope<PlaybackApi>("Playback"), Scope<CatalogApi>("Catalog"), auth, api);
        using var video = new MpvVideoWindow();
        using var mpv = new MpvPlayer();
        Action<string[]>? boundaryObserver = null;
        TaskCompletionSource? aiStatusRelease = null;
        try
        {
            Set(wire, "AcceptStart", true);
            Set(wire, "SubtitleAiEnabled", true);
            window.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1100, 800));
            window.AppWindow.Show(false);
            await Task.Delay(150);
            var capabilityFallback = Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_DIALOG_CASE") == "capability-fallback";
            var version = Version();
            if (capabilityFallback) version.AudioTracks = [];
            manager.UseWatchDetail(new() { ContentId = "subtitle-dialog", Type = "movie", Versions = [version] });
            await manager.StartSessionAsync(42);
            video.Create(WinRT.Interop.WindowNative.GetWindowHandle(window)); video.Show();
            mpv.InitializeWithWindow(video.Hwnd); video.SetMpv(mpv);
            mpv.SetProperty("ao", "null"); mpv.SetProperty("loop-file", "inf");
            Set(service, "_mpv", mpv); Set(service, "_videoWindow", video); Set(service, "_playbackManager", manager);
            Property(service, "State", PlayerState.Expanded); Property(service, "ContentId", "subtitle-dialog");
            Property(service, "Versions", manager.WatchDetail!.Versions);
            Call(service, "WireMpvEvents");
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var initialSubtitleGeneration = (long)Get(service, "_subtitleSidecarGeneration")!;
            void Loaded() => loaded.TrySetResult();
            mpv.FileLoaded += Loaded;
            try { mpv.LoadFile(media.Origin + "/video.y4m"); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { mpv.FileLoaded -= Loaded; }
            Call(service, "StopPlaybackStallWatchdog");
            await Until(() => !service.IsLoading, "Subtitle shell file did not settle.");
            if (capabilityFallback)
                await Until(() => (long)Get(service, "_subtitleSidecarGeneration")! > initialSubtitleGeneration &&
                    Get(service, "_loadedMediaInitCts") == null,
                    "Capability fallback fixture initializer did not finish before its inventory was supplied.");
            service.SetPaused(true); await Until(() => mpv.IsPaused, "Subtitle shell pause did not settle.");
            manager.CurrentSession!.SubtitleUrls = [new()
            {
                Index = 1108, MediaFileId = 42, TrackId = "synthetic-subtitle", Language = "en",
                Source = "downloaded", Codec = "vtt", Label = "Synthetic English", Url = media.Origin + "/fixture-subtitle.vtt",
            }];
            Call(service, "SendSubtitleListToOsc");
            await (Task)Call(service, "SendSubtitleAiAvailabilityToOscAsync", CancellationToken.None)!;
            long sequence = 0;
            async Task Open()
            {
                Call(service, "PublishNativeMenuScopes");
                var owner = Get(service, "_nativeMenuOwner")!;
                var token = (string)owner.GetType().GetProperty("Token")!.GetValue(owner)!;
                var requestType = typeof(PlayerService).Assembly.GetType("SiloPlayer.Services.PlaybackMenus.PlaybackMenuRequest")!;
                var anchorType = typeof(PlayerService).Assembly.GetType("SiloPlayer.Services.PlaybackMenus.PlaybackMenuAnchor")!;
                var request = Activator.CreateInstance(requestType, [token, ++sequence, Activator.CreateInstance(anchorType, [.1d, .8d, .15d, .9d])])!;
                requestType.GetProperty("Kind", Private | BindingFlags.Public)!.SetValue(request, "subtitle");
                await (Task)Call(service, "HandleNativeMenuOpenAsync", request)!;
                Require(Menu(service) != null, "Actual Subtitle caller did not open for dialog handoff.");
            }
            if (capabilityFallback)
            {
                foreach (var retirement in new[] { "none", "visible", "pending" })
                {
                    // The menu's cached translation capability is valid. The
                    // actual dialog probe then flips to transcription only,
                    // which has no audio source on this synthetic version.
                    Set(wire, "SubtitleAiTranslationEnabled", true);
                    Set(wire, "SubtitleAiTranscriptionEnabled", false);
                    await (Task)Call(service, "SendSubtitleAiAvailabilityToOscAsync", CancellationToken.None)!;
                    await Open();
                    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    aiStatusRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    var release = aiStatusRelease;
                    Set(wire, "SubtitleAiTranslationEnabled", false);
                    Set(wire, "SubtitleAiTranscriptionEnabled", true);
                    Set(wire, "SubtitleAiStatusBoundary", (Func<CancellationToken, Task>)(ct =>
                    {
                        entered.TrySetResult();
                        return release.Task.WaitAsync(ct);
                    }));
                    foreground.Observe("capability-fallback-" + retirement + "-before");
                    Call(service, "InvokeNativeSubtitleAction", "ai");
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(4));
                    Require(Menu(service) == null && Gate(service).CurrentCount == 0 && Get(service, "_nativeSubtitleDialog") == null,
                        "The actual AI capability probe did not retain its pending modal owner.");
                    if (retirement == "pending") api.SetProfile("retired-pending-capability");
                    release.TrySetResult();
                    if (retirement == "pending")
                    {
                        await Until(() => Gate(service).CurrentCount == 1, "Retired AI capability probe stranded its dialog lease.");
                        Require(Get(service, "_nativeSubtitleDialog") == null && Menu(service) == null,
                            "Retired AI capability probe opened a stale alert.");
                    }
                    else
                    {
                        await Until(() => Get(service, "_nativeSubtitleDialog") is ContentDialog,
                            "No-compatible-source alert bypassed the actual native dialog owner.");
                        var alert = (ContentDialog)Get(service, "_nativeSubtitleDialog")!;
                        await Until(() => alert.IsLoaded && alert.IsEnabled, "Background capability alert did not restore its controls.");
                        Require(alert.Title?.ToString() == "No compatible source" && Gate(service).CurrentCount == 0 &&
                            alert.XamlRoot != null && !ReferenceEquals(alert.XamlRoot, ((FrameworkElement)window.Content).XamlRoot),
                            "Capability fallback lost its alert, supplied playback root or exact modal lease.");
                        if (retirement == "visible") api.SetProfile("retired-visible-capability");
                        else alert.Hide();
                        await Until(() => Get(service, "_nativeSubtitleDialog") == null && Gate(service).CurrentCount == 1,
                            "Capability fallback completion/retirement left its actual alert or dialog lease alive.");
                    }
                    Set(wire, "SubtitleAiStatusBoundary", null);
                    Require(mpv.IsPaused && service.IsPaused, "Capability alert changed genuine paused playback.");
                    foreground.Observe("capability-fallback-" + retirement + "-after");
                    Program.Log("PASS: actual native AI capability fallback " + retirement + " retains scoped alert ownership, background focus and genuine pause.");
                }
                return;
            }
            if (Environment.GetEnvironmentVariable("SILO_NATIVE_SUBTITLE_DIALOG_CASE") == "retired-handoff")
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var completed = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                boundaryObserver = message =>
                {
                    if (message.FirstOrDefault() == "silo-retired-dialog-ready") ready.TrySetResult();
                    if (message.FirstOrDefault() == "silo-retired-dialog-completed") completed.TrySetResult(message);
                };
                mpv.ScriptMessageReceived += boundaryObserver;
                var probe = Path.Combine(Program.ResultDirectory, "retired-dialog-probe.lua");
                await File.WriteAllTextAsync(probe, """
                    local mp = require 'mp'
                    mp.register_script_message('osc-subtitle-dialog-closed', function(token, sequence, restore)
                        if token then mp.commandv('script-message','silo-retired-dialog-completed',token,sequence,restore) end
                    end)
                    mp.commandv('script-message','silo-retired-dialog-ready')
                    """);
                Call(mpv, "Command", (object)new[] { "load-script", probe });
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(4));
                await Open();
                var request = Get(service, "_nativeMenuRequest")!;
                var token = request.GetType().GetProperty("OwnerToken")!.GetValue(request)?.ToString();
                var sequenceValue = request.GetType().GetProperty("Sequence")!.GetValue(request)?.ToString();
                var menuWindow = (Window)Get(Menu(service)!, "_window")!;
                var retiredInClose = false;
                menuWindow.Closed += (_, _) => { retiredInClose = true; api.SetProfile("retired-inside-close"); };
                Call(service, "InvokeNativeSubtitleAction", "add");
                Require(retiredInClose, "Native menu close callback did not exercise the controlled retirement boundary.");
                var completion = await completed.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Require(completion.Length == 4 && completion[1] == token && completion[2] == sequenceValue && completion[3] == "false" &&
                    Get(service, "_nativeSubtitleDialog") == null && Gate(service).CurrentCount == 1,
                    "Retirement during menu close skipped scoped dialog completion or opened a stale modal.");
                Program.Log("PASS: context retirement inside native menu close still completes the exact dialog handoff and releases input without stale focus.");
                return;
            }
            var aiAvailable = typeof(PlayerService).GetProperty("NativeSubtitleAiAvailable", Private)!;
            Require((bool)aiAvailable.GetValue(service)!, "Synthetic subtitle AI capability was not available.");
            var readContext = api.CaptureContext();
            api.InvalidateAccessContext();
            Require(!api.IsCurrentContext(readContext) && (bool)aiAvailable.GetValue(service)!,
                "Retiring reads for the same playback identity hid the cached subtitle AI action.");
            var previousCapability = Get(service, "_nativeSubtitleAiCapability");
            var statusEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            aiStatusRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var statusRelease = aiStatusRelease;
            Set(wire, "SubtitleAiStatusBoundary", (Func<CancellationToken, Task>)(ct =>
            {
                statusEntered.TrySetResult();
                return statusRelease.Task.WaitAsync(ct);
            }));
            var statusProbe = (Task)Call(service, "SendSubtitleAiAvailabilityToOscAsync", CancellationToken.None)!;
            await statusEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
            api.InvalidateAccessContext();
            statusRelease.TrySetResult();
            await statusProbe.WaitAsync(TimeSpan.FromSeconds(4));
            Set(wire, "SubtitleAiStatusBoundary", null);
            Require(!ReferenceEquals(previousCapability, Get(service, "_nativeSubtitleAiCapability")) &&
                (bool)aiAvailable.GetValue(service)!,
                "Read-context retirement during the AI status probe discarded the current playback capability.");
            foreach (var action in new[] { "add", "appearance", "ai" })
            {
                await Open();
                if (action == "ai")
                    Require(Descendants<Button>((Grid)Get(Menu(service)!, "_root")!).Single(button =>
                        button.Content?.ToString() == "Translate with AI…").Visibility == Visibility.Visible,
                        "Read-context retirement hid Translate with AI in the actual native Subtitle menu.");
                Call(service, "InvokeNativeSubtitleAction", action);
                await Until(() => Get(service, "_nativeSubtitleDialog") is ContentDialog,
                    "Subtitle " + action + " did not reach its actual authorized dialog.");
                var dialog = (ContentDialog)Get(service, "_nativeSubtitleDialog")!;
                await Until(() => dialog.IsLoaded && dialog.IsEnabled, "Background dialog did not restore its actual controls.");
                Require(Menu(service) == null && Gate(service).CurrentCount == 0,
                    "Native menu did not release its lease before the modal acquired it.");
                Require(dialog.XamlRoot != null && !ReferenceEquals(dialog.XamlRoot, ((FrameworkElement)window.Content).XamlRoot),
                    "Subtitle dialog lost its supplied player-host XamlRoot.");
                Require(mpv.IsPaused, "Subtitle modal opening changed genuine pause intent.");
                await Capture(dialog, "subtitle-dialog-" + action + ".png");
                if (action == "ai")
                {
                    Require(!(bool)dialog.GetType().GetProperty("FocusOnOpen")!.GetValue(dialog)!,
                        "Background AI dialog requested initial programmatic focus.");
                    var id = dialog.XamlRoot!.ContentIslandEnvironment.AppWindowId;
                    var host = AppWindow.GetFromWindowId(id);
                    var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(id);
                    bool CoversVideo() => GetWindowRect(video.Hwnd, out var videoBounds) && GetWindowRect(hwnd, out var bounds)
                        && bounds.Left == videoBounds.Left && bounds.Top == videoBounds.Top
                        && bounds.Right == videoBounds.Right && bounds.Bottom == videoBounds.Bottom;
                    try
                    {
                        // Exercise the proposed physical resize a 100% -> 150% DPI change can deliver.
                        var original = host.Size;
                        host.Resize(new(original.Width * 3 / 2, original.Height * 3 / 2));
                        await Until(CoversVideo, "AI modal kept a 150% host resize outside the video window.");

                        // Simulate the DPI resize from inside a follow notification, even on a single monitor.
                        var videoWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(video.Hwnd));
                        var requestedNestedResize = false;
                        void ResizeDuringFollow(AppWindow sender, AppWindowChangedEventArgs args)
                        {
                            if (!args.DidSizeChange || requestedNestedResize) return;
                            requestedNestedResize = true;
                            var size = sender.Size;
                            sender.Resize(new(size.Width * 3 / 2, size.Height * 3 / 2));
                        }
                        host.Changed += ResizeDuringFollow;
                        try
                        {
                            var size = videoWindow.Size;
                            videoWindow.Resize(new(size.Width + 32, size.Height + 32));
                            await Until(() => requestedNestedResize && CoversVideo(),
                                "AI modal did not recover from a nested host resize during owner coverage.");
                        }
                        finally { host.Changed -= ResizeDuringFollow; }

                        window.AppWindow.ResizeClient(new(960, 540));
                        video.SynchronizePosition();
                        await Until(CoversVideo, "AI modal did not follow its resized video owner.");
                        await CheckModalAlphaAsync(hwnd);
                        await CheckModalDisplaysAsync(window, video, hwnd, dialog.XamlRoot);
                        Require(mpv.IsPaused, "Modal geometry changed playback intent.");
                    }
                    finally { dialog.Hide(); }
                }
                else dialog.Hide();
                await Until(() => Get(service, "_nativeSubtitleDialog") == null && Gate(service).CurrentCount == 1,
                    "Subtitle modal completion stranded the owned gate.");
                Require(mpv.IsPaused, "Subtitle modal completion changed genuine pause intent.");
                Program.Log("PASS: actual native Subtitle " + action + " handoff closes menu, acquires owned modal, retains pause and releases lease.");
            }
            await Open();
            Call(service, "InvokeNativeSubtitleAction", "add");
            await Until(() => Get(service, "_nativeSubtitleDialog") is ContentDialog,
                "Profile-retirement Search dialog did not open.");
            api.SetProfile("replacement-profile");
            Require(!(bool)typeof(PlayerService).GetProperty("NativeSubtitleAiAvailable", Private)!.GetValue(service)!,
                "AI capability from the previous profile remained available in the replacement context.");
            await Until(() => Get(service, "_nativeSubtitleDialog") == null && Gate(service).CurrentCount == 1,
                "Profile replacement left a native Subtitle dialog or its lease alive.");
            Program.Log("PASS: profile replacement retires the actual native Subtitle modal without reviving its opener.");
        }
        finally
        {
            aiStatusRelease?.TrySetResult();
            Set(wire, "SubtitleAiStatusBoundary", null);
            if (boundaryObserver != null) mpv.ScriptMessageReceived -= boundaryObserver;
            Call(service, "UnwireMpvEvents"); Call(service, "CancelPendingLoadedMediaInitialization");
            Call(service, "CancelPendingFileLoadTimeout"); Call(service, "StopPlaybackStallWatchdog");
            Call(service, "RetireNativeMenus"); Call(service, "StopSubtitleSync");
            Set(service, "_mpv", null); Set(service, "_videoWindow", null);
            await ((IAsyncDisposable)scope).DisposeAsync();
            await Task.Delay(120);
            foreground.Complete();
        }
    }
}
