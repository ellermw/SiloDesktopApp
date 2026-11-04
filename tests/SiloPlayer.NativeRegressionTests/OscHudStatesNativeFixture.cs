using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static class OscHudStatesNativeFixture
{
    internal static async Task RunAsync(Window owner)
    {
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
        var diagnostic = Path.Combine(Program.ResultDirectory, "hud-render-observer.lua");
        await File.WriteAllTextAsync(diagnostic, await File.ReadAllTextAsync(script) + """

            -- Fixture-only observation in the same Lua scope as the real OSC.
            mp.register_script_message('osc-hud-fixture-query', function(request, name)
                compute_layout()
                local r = state.layout[name] or {}
                mp.commandv('script-message', 'silo-hud-fixture', request, require('mp.utils').format_json({
                    x=r.x or 0,y=r.y or 0,w=r.w or 0,h=r.h or 0,alpha=state.current_alpha,
                    pressed=state.pressed_control or '',outputWidth=state.osd_width,outputHeight=state.osd_height}))
            end)
            mp.commandv('script-message', 'silo-hud-fixture-ready')
            """);
        Command(player, "load-script", diagnostic);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var observations = new List<object>();
        foreach (var size in new[] { (960, 540), (1920, 1080) })
        foreach (var shade in new[] { (byte)16, (byte)235 })
        {
            owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, size.Item1, size.Item2));
            video.SynchronizePosition();
            var clip = Path.Combine(Program.ResultDirectory, $"synthetic-{shade}.y4m");
            if (!File.Exists(clip)) GenerateClip(clip, shade);
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnLoaded() => loaded.TrySetResult();
            player.FileLoaded += OnLoaded;
            try { player.LoadFile(clip); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { player.FileLoaded -= OnLoaded; }
            player.Pause(); await Until(() => player.IsPaused && player.GetPropertyFlag("vo-configured"));
            player.SendScriptMessage("osc-set-title", "Synthetic OSC verification", "Local fixture only");
            player.SendScriptMessage("osc-mouse-move", "400", "200");
            player.SendScriptMessage("osc-show");
            var geometry = await Query("btn_exit");
            for (var i = 0; geometry.Alpha < .99 && i < 40; i++) { await Task.Delay(25); geometry = await Query("btn_exit"); }
            Require(geometry.Alpha >= .99 && geometry.W > 0, "OSC did not become fully visible");
            foreach (var button in new[] { "btn_exit", "btn_minimize" })
            {
                while (intents.TryDequeue(out _)) { }
                var prefix = $"{size.Item1}-{shade}-{button}";
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
                var pressed = await Capture(prefix + "-pressed.png");
                player.SendScriptMessage("osc-mouse-up", x, y);
                player.SendScriptMessage("osc-video-click", x, y);
                await Task.Delay(80);
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
            player.Play(); await Until(() => !player.IsPaused);
            var start = player.Position; await Task.Delay(350);
            Require(player.Position > start + .1, "Playback failed to continue after chrome interaction");
        }
        await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, "hud-observations.json"), JsonSerializer.Serialize(new
        {
            script, scriptSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script))), observations,
            scope = "Actual offscreen libmpv window/ASS screenshots; scripted native-host message sequence, synthetic data. No physical input/display parity or installed-app control."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Program.Log("PASS: actual OSC top chrome hover/press/release pixels on bright/dark960/1920 video; one intent, paused continuity and resumed playback.");

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
    private sealed record Probe(double X, double Y, double W, double H, double Alpha, string Pressed, int OutputWidth, int OutputHeight);
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
    private static void GenerateClip(string path, byte shade)
    {
        using var stream = File.Create(path);
        stream.Write(Encoding.ASCII.GetBytes("YUV4MPEG2 W160 H90 F30:1 Ip A1:1 C420jpeg\n"));
        var pixels = new byte[160 * 90 * 3 / 2]; Array.Fill(pixels, (byte)128); Array.Fill(pixels, shade, 0, 160 * 90);
        for (var i = 0; i < 60; i++) { stream.Write(Encoding.ASCII.GetBytes("FRAME\n")); stream.Write(pixels); }
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
