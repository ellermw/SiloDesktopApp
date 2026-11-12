using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SiloPlayer.Player;
using SiloPlayer.Services;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class OscHudStatesNativeFixture
{
    internal static async Task RunAsync(Window owner, string? reviewCase = null, bool introOnly = false)
    {
        if (reviewCase is not (null or "all" or "fit" or "exit" or "minimize"))
            throw new ArgumentException("Unknown HUD review case.", nameof(reviewCase));
        var productionActions = reviewCase != null;
        var runPrefix = productionActions ? "service-" : "";
        using var foreground = new HudForegroundProbe(runPrefix);
        if (owner.AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(false, false);
        owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1280, 720));
        using var video = new MpvVideoWindow();
        using var player = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner)); video.Show();
        player.InitializeWithWindow(video.Hwnd); video.SetMpv(player);
        player.SetProperty("ao", "null"); player.SetProperty("loop-file", "inf");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new ConcurrentDictionary<string, TaskCompletionSource<Probe>>();
        var intents = new ConcurrentQueue<string>();
        player.ScriptMessageReceived += message =>
        {
            if (message.Length == 0) return;
            if (message[0] == "silo-hud-fixture-ready") ready.TrySetResult();
            else if (message[0] == "silo-hud-fixture" && message.Length > 2 && pending.TryRemove(message[1], out var receiver))
            {
                try { receiver.TrySetResult(JsonSerializer.Deserialize<Probe>(message[2], new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!); }
                catch (Exception error) { receiver.TrySetException(error); }
            }
            else if (message[0] is "silo-exit" or "silo-minimize" or "silo-pause-toggle") intents.Enqueue(message[0]);
        };
        var script = Path.Combine(Program.AppDirectory, "libs", "mpv", "scripts", "silo-osc.lua");
        var diagnostic = Path.Combine(Program.ResultDirectory, runPrefix + "hud-render-observer.lua");
        await File.WriteAllTextAsync(diagnostic, await File.ReadAllTextAsync(script) + """

            -- Fixture-only observation in the same Lua scope as the real OSC.
            mp.register_script_message('osc-hud-fixture-query', function(request, name)
                compute_layout()
                local r = name == 'intro' and (state.skip_rect or {}) or state.layout[name] or {}
                mp.commandv('script-message', 'silo-hud-fixture', request, require('mp.utils').format_json({
                    x=r.x or 0,y=r.y or 0,w=r.w or 0,h=r.h or 0,alpha=state.current_alpha,
                    pressed=state.pressed_control or '',focus=state.controller_focus_name or '',target=state.target_alpha,
                    skip=state.skip_visible,remaining=state.intro_prompt_remaining or -1,
                    outputWidth=state.osd_width,outputHeight=state.osd_height}))
            end)
            mp.commandv('script-message', 'silo-hud-fixture-ready')
            """);
        Command(player, "load-script", diagnostic);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
        if (introOnly)
        {
            var clip = Path.Combine(Program.ResultDirectory, "intro.y4m"); GenerateClip(clip, 48, 900);
            video.SynchronizePosition();
            await LoadClip(clip);
            player.SendScriptMessage("osc-set-intro-mode", "ask");
            player.SendScriptMessage("osc-set-markers", "{\"content_id\":\"intro-fixture\",\"intro_start\":0,\"intro_end\":30}");
            player.Play(); await Until(() => !player.IsPaused);
            await Task.Delay(5800);
            player.SendScriptMessage("osc-hide"); await Task.Delay(400);
            var expired = await Query("intro");
            await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, "intro-expired.json"), JsonSerializer.Serialize(expired));
            Require(expired.Remaining == 0 && !expired.Skip, "Ask prompt did not expire while controls were hidden.");
            player.SendScriptMessage("osc-show"); await Task.Delay(350);
            var offered = await Query("intro");
            Require(offered.Skip && offered.Remaining == 0 && offered.W > 0 && offered.H > 0 && offered.Focus == expired.Focus,
                "Revealing actual controls did not return an untimed offer without stealing focus.");
            Require(offered.Y + offered.H < (await Query("seek_bar")).Y, "Untimed intro offer overlapped the native seek hit area.");
            await Capture("intro-untimed-offer.png");
            var pauses = intents.Count(value => value == "silo-pause-toggle");
            Command(player, "keypress", "SPACE"); await Task.Delay(100);
            Require(intents.Count(value => value == "silo-pause-toggle") == pauses + 1, "Untimed intro offer captured normal Space.");
            player.SendScriptMessage("osc-hide"); await Task.Delay(350);
            Require(!(await Query("intro")).Skip, "Untimed offer outlived hidden controls.");
            player.SendScriptMessage("osc-show"); await Task.Delay(350);
            Require((await Query("intro")).Remaining == 0, "Revealing controls renewed the countdown.");
            player.SendScriptMessage("osc-set-markers", "{\"content_id\":\"replacement\",\"intro_start\":10,\"intro_end\":20}");
            await Task.Delay(150);
            Require(!(await Query("intro")).Skip, "Expired offer survived content/marker replacement.");
            Program.Log("PASS: actual offscreen OSC timed expiry, untimed control reveal/hide, unchanged focus and Space, marker/content retirement.");
            return;
        }
        var observations = new List<object>();
        foreach (var size in new[] { (960, 540), (1920, 1080) })
        foreach (var shade in new[] { (byte)16, (byte)235 })
        {
            if (reviewCase == "fit" && size.Item1 < 1920) continue;
            owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, size.Item1, size.Item2));
            video.SynchronizePosition();
            var clip = Path.Combine(Program.ResultDirectory, $"synthetic-{shade}.y4m");
            if (!File.Exists(clip)) GenerateClip(clip, shade);
            await LoadClip(clip);
            player.SendScriptMessage("osc-set-title", "Synthetic OSC verification", "Local fixture only");
            player.SendScriptMessage("osc-mouse-move", "400", "200");
            player.SendScriptMessage("osc-show");
            var geometry = await Query("btn_exit");
            for (var i = 0; geometry.Alpha < .99 && i < 40; i++) { await Task.Delay(25); geometry = await Query("btn_exit"); }
            Require(geometry.Alpha >= .99 && geometry.W > 0, "OSC did not become fully visible");
            if (reviewCase == null && size.Item1 == 960 && shade == 16)
            {
                player.SendScriptMessage("osc-controller-nav", "right");
                Require((await Query("btn_play")).Focus != "", "Controller must own focus before content replacement.");
                // Do not use osc-set-visibility to clean up on behalf of file-loaded.
                await LoadClip(clip, resetVisibility: false);
                Require((await Query("btn_play")).Focus == "", "File load retained the outgoing controller focus.");
                player.Play(); await Until(() => !player.IsPaused);
                await Task.Delay(3600);
                var hidden = await Query("btn_play");
                Require(hidden.Target == 0 && hidden.Alpha < .1,
                    "Replaced content could not auto-hide without pointer input.");
                observations.Add(new { fileLoadRetiresFocusAndHides = true });
                player.Pause(); await Until(() => player.IsPaused);
                player.SendScriptMessage("osc-show"); await Task.Delay(350);
            }
            if (size.Item1 >= 1920 && reviewCase is "all" or "fit")
            {
                // #3993 groups Fit with Picture in picture and Playback info in More.
                var more = await Query("btn_more");
                Require(more.W > 0, "Fit fixture needs the desktop More button.");
                player.SetProperty("panscan", "0");
                player.Play(); await Until(() => !player.IsPaused);
                async Task ClickFitThroughMore()
                {
                    var mx = (more.X + more.W / 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var my = (more.Y + more.H / 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    player.SendScriptMessage("osc-mouse-move", mx, my);
                    player.SendScriptMessage("osc-mouse-down", mx, my);
                    player.SendScriptMessage("osc-mouse-up", mx, my);
                    await Task.Delay(150);
                    var fit = await Query("overflow_btn_fill");
                    Require(fit.W > 0, "More did not offer Fit.");
                    var fx = (fit.X + fit.W / 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var fy = (fit.Y + fit.H / 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    player.SendScriptMessage("osc-mouse-move", fx, fy);
                    player.SendScriptMessage("osc-mouse-down", fx, fy);
                    player.SendScriptMessage("osc-mouse-up", fx, fy);
                }
                await ClickFitThroughMore();
                await Task.Delay(100);
                Require(player.GetPropertyDouble("panscan") > 0, "Fit from More did not change the video fit.");
                player.SendScriptMessage("osc-mouse-move", "400", "200");
                await Task.Delay(3600);
                var hidden = await Query("btn_more");
                await Capture($"{size.Item1}-{shade}-fit-autohide.png");
                Program.Log($"Fit native normal timer: focus={hidden.Focus}, target={hidden.Target}, alpha={hidden.Alpha}.");
                Require(hidden.Focus == "" && hidden.Target == 0 && hidden.Alpha < .1,
                    "Native Fit pointer gesture stranded controller focus and prevented actual auto-hide.");
                player.SendScriptMessage("osc-show"); await Task.Delay(250);
                await ClickFitThroughMore();
                player.SendScriptMessage("osc-mouse-leave"); await Task.Delay(850);
                hidden = await Query("btn_more");
                Require(hidden.Focus == "" && hidden.Target == 0 && hidden.Alpha < .1,
                    "Native Fit pointer leave stranded the HUD.");
                observations.Add(new { size.Item1, shade, fitNormalAndLeaveHide = true });
                if (reviewCase == "fit") continue;
            }
            foreach (var button in new[] { "btn_exit", "btn_minimize" })
            {
                if (productionActions && reviewCase != "all" && reviewCase != button[4..]) continue;
                if (productionActions) await LoadClip(clip);
                var prefix = $"{runPrefix}{size.Item1}-{shade}-{button}";
                await using var service = productionActions ? new HudServiceDispatch(owner, video, player, prefix) : null;
                while (intents.TryDequeue(out _)) { }
                var r = await Query(button);
                player.SendScriptMessage("osc-mouse-move", "400", "200");
                await Task.Delay(80);
                var normal = await Capture(prefix + "-normal.png");
                var x = (r.X + r.W / 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var y = (r.Y + r.H / 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                player.SendScriptMessage("osc-mouse-move", x, y);
                await Task.Delay(80);
                var hover = await Capture(prefix + "-hover.png");
                player.SendScriptMessage("osc-mouse-down", x, y);
                var held = await Query(button);
                await Task.Delay(80);
                if (service != null)
                {
                    Program.Log($"Held {button}: service={service.State}, visible={IsWindowVisible(video.Hwnd)}, intents={intents.Count}.");
                    Require(service.State == PlayerState.Expanded && IsWindowVisible(video.Hwnd) && intents.IsEmpty,
                        "Top chrome dispatched its real service action before the held press could render.");
                }
                var pressed = await Capture(prefix + "-pressed.png");
                player.SendScriptMessage("osc-mouse-up", x, y);
                player.SendScriptMessage("osc-video-click", x, y);
                await Task.Delay(80);
                if (service != null)
                {
                    await service.Completion.WaitAsync(TimeSpan.FromSeconds(8));
                    await Until(() => service.State == (button == "btn_exit" ? PlayerState.Idle : PlayerState.Minimized));
                    Require(intents.ToArray().SequenceEqual(new[] { button == "btn_exit" ? "silo-exit" : "silo-minimize" }),
                        "Real service gesture emitted an extra intent.");
                    var hoverDelta = DifferentPixels(normal, hover, r);
                    var pressDelta = DifferentPixels(hover, pressed, r);
                    Require(hoverDelta > 30 && pressDelta > 30 && held.Pressed == button,
                        "Real service gesture omitted visible held feedback.");
                    observations.Add(new { size.Item1, size.Item2, shade, button, hoverDelta, pressDelta, serviceState = service.State.ToString() });
                    continue;
                }
                var release = await Capture(prefix + "-release.png");
                var hoverPixels = DifferentPixels(normal, hover, r);
                var pressPixels = DifferentPixels(hover, pressed, r);
                Require(hoverPixels > 30 && pressPixels > 30, $"{button} lacks actual hover/press pixels: {hoverPixels}/{pressPixels}");
                Require(DifferentPixels(hover, release, r) < 8, "Released chrome did not return to its hover appearance");
                Require(held.Pressed == button, "Actual Lua did not retain held-control feedback");
                var released = await Query(button);
                Require(released.Pressed == "" && player.IsPaused, "Chrome release changed paused intent or retained feedback");
                Require(intents.ToArray().SequenceEqual(new[] { button == "btn_exit" ? "silo-exit" : "silo-minimize" }), "Chrome gesture dispatched an extra playback intent");
                observations.Add(new { size.Item1, size.Item2, shade, button, hoverPixels, pressPixels, paused = player.IsPaused });
            }
            if (productionActions) await LoadClip(clip);
            player.Play(); await Until(() => !player.IsPaused);
            var start = player.Position; await Task.Delay(350);
            Require(player.Position > start + .1, "Playback failed to continue after chrome interaction");
        }
        await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, runPrefix + "hud-observations.json"), JsonSerializer.Serialize(new
        {
            script, scriptSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script))), observations,
            scope = "Actual offscreen libmpv window/ASS screenshots; scripted native-host message sequence, synthetic data. No physical input/display parity or installed-app control."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Program.Log(productionActions
            ? $"PASS: actual HUD review case {reviewCase}; real service dispatch and/or Fit timer outcomes recorded."
            : "PASS: actual OSC top chrome hover/press/release pixels on bright/dark960/1920 video; one intent, paused continuity and resumed playback.");
        await Task.Delay(50);

        async Task LoadClip(string clip, bool resetVisibility = true)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult();
            player.FileLoaded += Loaded;
            try { player.LoadFile(clip); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { player.FileLoaded -= Loaded; }
            player.Pause(); await Until(() => player.IsPaused && player.GetPropertyFlag("vo-configured"));
            if (resetVisibility) player.SendScriptMessage("osc-set-visibility", "true");
            player.SendScriptMessage("osc-show"); await Task.Delay(250);
        }

        async Task<Probe> Query(string name)
        {
            var request = Guid.NewGuid().ToString("N");
            var receiver = new TaskCompletionSource<Probe>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[request] = receiver;
            try { player.SendScriptMessage("osc-hud-fixture-query", request, name); return await receiver.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
            finally { pending.TryRemove(request, out _); }
        }
        async Task<Frame> Capture(string name)
        {
            var path = Path.Combine(Program.ResultDirectory, name);
            Command(player, "screenshot-to-file", path, "window");
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return new Frame((int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData());
        }
    }

    private sealed record Frame(int Width, int Height, byte[] Pixels);
    private sealed record Probe(double X, double Y, double W, double H, double Alpha, string Pressed, int OutputWidth, int OutputHeight, string Focus, double Target, bool Skip, double Remaining);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private sealed class FixtureWire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri?.Host == "hud-review.fixture.invalid", "HUD fixture attempted external networking.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
        }
    }
    private sealed class HudServiceDispatch : IAsyncDisposable
    {
        private readonly Window _owner;
        private readonly MpvPlayer _player;
        private readonly HttpClient _http = new(new FixtureWire());
        private readonly AuthService _auth;
        private readonly PlayerService _service;
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;
        internal PlayerState State => _service.State;
        internal Task Completion => _completion.Task;

        internal HudServiceDispatch(Window owner, MpvVideoWindow video, MpvPlayer player, string prefix)
        {
            _owner = owner; _player = player;
            var api = new SiloApiClient(_http); api.SetBaseUrl("https://hud-review.fixture.invalid");
            _auth = new AuthService(api, new AuthApi(api));
            _service = new PlayerService(new PlaybackApi(api), new CatalogApi(api), _auth, api,
                new SettingsService(Path.Combine(Program.ResultDirectory, prefix + "-settings")), new SettingsApi(api));
            Set(_service, "_mpv", player); Set(_service, "_videoWindow", video);
            _service.SetState(PlayerState.Expanded);
            _player.ScriptMessageReceived += Dispatch;
        }

        private void Dispatch(string[] args)
        {
            if (args.Length == 0 || args[0] is not ("silo-exit" or "silo-minimize")) return;
            if (Interlocked.Exchange(ref _started, 1) != 0) return; // Original observer still counts duplicates.
            // Use the real service methods and dispatcher delivery from
            // OnScriptMessage without starting the account shell.
            if (!_owner.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (args[0] == "silo-exit") await _service.CloseAsync();
                    else _service.Minimize();
                    _completion.TrySetResult();
                }
                catch (Exception ex) { _completion.TrySetException(ex); }
            })) _completion.TrySetException(new InvalidOperationException("Fixture dispatcher retired."));
        }

        public async ValueTask DisposeAsync()
        {
            _player.ScriptMessageReceived -= Dispatch;
            try { if (Volatile.Read(ref _started) != 0) await Completion.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally
            {
                Set(_service, "_mpv", null); Set(_service, "_videoWindow", null);
                _service.Dispose(); _auth.Dispose(); _http.Dispose();
            }
        }
    }
    private sealed class HudForegroundProbe : IDisposable
    {
        private readonly ConcurrentQueue<long> _events = new();
        private readonly WinEventCallback _callback;
        private readonly IntPtr _hook;
        private readonly string _prefix;
        internal HudForegroundProbe(string prefix)
        {
            _prefix = prefix;
            _callback = (_, _, hwnd, _, _, _, _) => _events.Enqueue(hwnd.ToInt64());
            _hook = SetWinEventHook(3, 3, IntPtr.Zero, _callback, (uint)Environment.ProcessId, 0, 0);
            Require(_hook != IntPtr.Zero, "Cannot observe fixture foreground events.");
        }
        internal void Complete()
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
            File.WriteAllText(Path.Combine(Program.ResultDirectory, _prefix + "hud-foreground.json"),
                JsonSerializer.Serialize(new { fixtureProcess = Environment.ProcessId, ownedForegroundEvents = _events.ToArray(), foregroundProcess }));
            Require(_events.IsEmpty && foregroundProcess != Environment.ProcessId, "HUD fixture acquired foreground.");
        }
        public void Dispose()
        {
            try { Complete(); }
            finally { UnhookWinEvent(_hook); GC.KeepAlive(_callback); }
        }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventCallback(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    private static int DifferentPixels(Frame a, Frame b, Probe rect)
    {
        Require(a.Width == b.Width && a.Height == b.Height, "Capture sizes changed mid-gesture");
        var xScale = a.Width / (double)rect.OutputWidth; var yScale = a.Height / (double)rect.OutputHeight;
        var count = 0;
        for (var y = Math.Max(0, (int)(rect.Y * yScale)); y < Math.Min(a.Height, (int)((rect.Y + rect.H) * yScale)); y++)
        for (var x = Math.Max(0, (int)(rect.X * xScale)); x < Math.Min(a.Width, (int)((rect.X + rect.W) * xScale)); x++)
        {
            var offset = (y * a.Width + x) * 4;
            if (Enumerable.Range(0, 3).Any(channel => Math.Abs(a.Pixels[offset + channel] - b.Pixels[offset + channel]) > 8)) count++;
        }
        return count;
    }
    private static void GenerateClip(string path, byte shade, int frames = 60)
    {
        using var stream = File.Create(path);
        stream.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W160 H90 F30:1 Ip A1:1 C420jpeg\n"));
        var pixels = new byte[160 * 90 * 3 / 2]; Array.Fill(pixels, (byte)128); Array.Fill(pixels, shade, 0, 160 * 90);
        for (var i = 0; i < frames; i++) { stream.Write(Encoding.ASCII.GetBytes("FRAME\n")); stream.Write(pixels); }
    }
    private static void Command(MpvPlayer player, params string[] args) => Require(
        typeof(MpvPlayer).GetMethod("Command", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(player, [args]) is true,
        "Native OSC fixture command failed: " + args[0]);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate()) { if (watch.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException("Native OSC state deadline"); await Task.Delay(25); }
    }
}
