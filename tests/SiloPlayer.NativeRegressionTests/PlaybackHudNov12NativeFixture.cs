using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;

// Silo Server 0b579aa6 #3917/#3993 on the production OSC in a real offscreen
// libmpv window: title-block times and finish time, regrouped rail, compact
// flanking times, owned loading cover under the spinner, floating rebuffer
// disc and the still-watching gate on the credits countdown. Screenshots are
// saved for visual inspection; pixel checks cover the layering assumptions.
internal static class PlaybackHudNov12NativeFixture
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    internal static async Task RunAsync(Window owner)
    {
        if (owner.AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(false, false);
        owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1920, 1080));
        using var video = new MpvVideoWindow();
        using var player = new MpvPlayer();
        video.Create(WinRT.Interop.WindowNative.GetWindowHandle(owner)); video.Show();
        player.InitializeWithWindow(video.Hwnd); video.SetMpv(player);
        player.SetProperty("ao", "null"); player.SetProperty("loop-file", "inf");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
        var intents = new ConcurrentQueue<string>();
        player.ScriptMessageReceived += message =>
        {
            if (message.Length == 0) return;
            if (message[0] == "silo-hud-nov12-ready") ready.TrySetResult();
            else if (message[0] == "silo-hud-nov12" && message.Length > 2 && pending.TryRemove(message[1], out var receiver))
            {
                try { receiver.TrySetResult(JsonDocument.Parse(message[2]).RootElement.Clone()); }
                catch (Exception error) { receiver.TrySetException(error); }
            }
            else if (message[0].StartsWith("silo-", StringComparison.Ordinal))
                intents.Enqueue(string.Join('|', message));
        };
        var script = Path.Combine(Program.AppDirectory, "libs", "mpv", "scripts", "silo-osc.lua");
        var observer = Path.Combine(Program.ResultDirectory, "hud-nov12-observer.lua");
        await File.WriteAllTextAsync(observer, await File.ReadAllTextAsync(script) + """

            -- Fixture-only observation in the same Lua scope as the real OSC.
            mp.register_script_message('osc-hud-nov12-query', function(request)
                compute_layout()
                local rects = {}
                for name, r in pairs(state.layout) do
                    if type(r) == 'table' and r.w and r.h then
                        rects[name] = {x=r.x or 0, y=r.y or 0, w=r.w, h=r.h, label=r.label or ''}
                    end
                end
                local text = ((state.osc_overlay and state.osc_overlay.data) or ''):gsub('{[^}]*}', '')
                mp.commandv('script-message', 'silo-hud-nov12', request, require('mp.utils').format_json({
                    rects=rects, text=text, compact=state.layout.compact == true,
                    dividers=state.layout.rail_dividers or {}, volume=state.layout.volume_bar ~= nil,
                    countdown=state.next_ep_countdown_active == true, cover=state.loading_backdrop_shown ~= nil,
                    alpha=state.current_alpha, width=state.osd_width, height=state.osd_height}))
            end)
            mp.commandv('script-message', 'silo-hud-nov12-ready')
            """);
        Command(player, "load-script", observer);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));

        var clip = Path.Combine(Program.ResultDirectory, "hud-nov12-clip.y4m");
        GenerateClip(clip, 160, 300);
        var observations = new List<object>();

        foreach (var (width, height) in new[] { (1920, 1080), (1280, 720), (960, 540), (2560, 1440) })
        {
            owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, width, height));
            video.SynchronizePosition();
            await LoadClip();
            PublishTitle();
            player.SendScriptMessage("osc-mouse-move", (width / 3).ToString(Invariant), (height / 3).ToString(Invariant));
            player.SendScriptMessage("osc-show"); await Task.Delay(400);
            var probe = await Query();
            Require(probe.GetProperty("width").GetDouble() > 0, "OSC reported no surface.");
            var text = probe.GetProperty("text").GetString() ?? "";
            var rects = probe.GetProperty("rects");
            Require(text.Contains("Ends ", StringComparison.Ordinal), $"{width}: no finish time on the HUD.");
            Require(text.Contains("Synthetic Show", StringComparison.Ordinal), $"{width}: title missing.");
            var compact = probe.GetProperty("compact").GetBoolean();
            var seek = rects.GetProperty("seek_bar");
            if (compact)
            {
                Require(!text.Contains("0:00 / ", StringComparison.Ordinal), $"{width}: compact kept the combined times.");
                var elapsed = rects.GetProperty("time_elapsed");
                var remaining = rects.GetProperty("time_remaining");
                Require(X(elapsed) + W(elapsed) <= X(seek) && X(remaining) >= X(seek) + W(seek), $"{width}: compact times overlap the seek target.");
            }
            else
            {
                Require(text.Contains(" / ", StringComparison.Ordinal), $"{width}: full title block lacks elapsed / length.");
                foreach (var required in new[] { "btn_volume", "btn_audio", "btn_cc", "btn_chapters", "btn_more", "btn_fullscreen" })
                    Require(rects.TryGetProperty(required, out _), $"{width}: {required} missing from the full rail.");
                // Settings appears only with quality/version choices, which this clip has none of.
                var order = new[] { "btn_volume", "btn_audio", "btn_cc", "btn_chapters", "btn_quality", "btn_more", "btn_fullscreen" }
                    .Where(name => rects.TryGetProperty(name, out _)).ToArray();
                for (var i = 1; i < order.Length; i++)
                    Require(X(rects.GetProperty(order[i - 1])) + W(rects.GetProperty(order[i - 1])) <= X(rects.GetProperty(order[i])),
                        $"{width}: {order[i - 1]} not before {order[i]}.");
                foreach (var gone in new[] { "btn_pip", "btn_fill", "btn_stats" })
                    Require(!rects.TryGetProperty(gone, out _), $"{width}: {gone} stayed on the full rail.");
                var next = rects.GetProperty("btn_next_ep");
                Require(X(rects.GetProperty("btn_volume")) >= X(next) + W(next) + 8, $"{width}: rail overlaps the centred transport.");
            }
            var shot = await Capture($"hud-{width}x{height}.png");
            observations.Add(new
            {
                width, height, compact, volumeSlider = probe.GetProperty("volume").GetBoolean(),
                audioLabel = rects.TryGetProperty("btn_audio", out var audio) ? audio.GetProperty("label").GetString() : null,
                screenshot = shot.Path,
            });
        }

        // Loading cover: a known red/blue backdrop composed by the production
        // Core routine, shown as the owned bitmap under the spinner.
        owner.AppWindow.MoveAndResize(new RectInt32(-20000, -20000, 1920, 1080));
        video.SynchronizePosition();
        await LoadClip();
        var source = new byte[320 * 180 * 4];
        for (var y = 0; y < 180; y++)
        for (var x = 0; x < 320; x++)
        {
            var i = (y * 320 + x) * 4;
            if (x < 160) source[i + 2] = 255; else source[i] = 255; // left red, right blue
            source[i + 3] = 255;
        }
        var target = LoadingCoverBitmap.TargetSize(1920, 1080);
        var composed = LoadingCoverBitmap.Compose(source, 320, 180, 320 * 4, target.Width, target.Height, 1);
        var coverFile = Path.Combine(Program.ResultDirectory, "hud-nov12-cover.bgra");
        await File.WriteAllBytesAsync(coverFile, composed);
        player.SendScriptMessage("osc-set-loading-backdrop", "fixture-1", coverFile, target.Width.ToString(Invariant),
            target.Height.ToString(Invariant), (target.Width * 4).ToString(Invariant), "1920", "1080");
        player.SendScriptMessage("osc-set-loading", "true");
        await Task.Delay(400);
        var loading = await Query();
        Require(loading.GetProperty("cover").GetBoolean(), "Loading did not show the owned backdrop.");
        var coverShot = await Capture("loading-cover-1920.png");
        var left = coverShot.At(0.1, 0.5); var right = coverShot.At(0.9, 0.5);
        Require(left.R is >= 40 and <= 75 && left.G < 20 && left.B < 20, $"Left backdrop is not dimmed red: {left}.");
        Require(right.B is >= 40 and <= 75 && right.R < 20 && right.G < 20, $"Right backdrop is not dimmed blue: {right}.");
        // Inside the spinner disc: black, never the video's grey, with the ring visible.
        var centre = coverShot.At(0.5, 0.5);
        Require(centre.R < 24 && centre.G < 24 && centre.B < 24, $"Spinner disc is not black over the cover: {centre}.");
        Require(coverShot.MaxBrightnessInRing(0.5, 0.5, 15, 21) > 180, "Spinner ring is not visible over the cover.");
        player.SendScriptMessage("osc-set-loading", "false");
        await Task.Delay(300);
        Require(!(await Query()).GetProperty("cover").GetBoolean(), "The first frame left the cover up.");
        var frameShot = await Capture("first-frame-1920.png");
        var frame = frameShot.At(0.1, 0.5);
        Require(frame.R is >= 140 and <= 175 && Math.Abs(frame.R - frame.B) < 16, $"Video was not visible after the cover: {frame}.");

        // Rebuffering keeps the picture and floats the ring on a dark disc.
        player.SendScriptMessage("osc-set-buffering", "true");
        await Task.Delay(800);
        var bufferShot = await Capture("rebuffer-1920.png");
        var disc = bufferShot.At(0.5, 0.5);
        var picture = bufferShot.At(0.1, 0.5);
        Require(picture.R > 130, $"Rebuffering covered the picture: {picture}.");
        Require(disc.R < picture.R - 40, $"Rebuffer spinner lacks its dark disc: {disc} over {picture}.");
        player.SendScriptMessage("osc-set-buffering", "false");
        player.SendScriptMessage("osc-clear-loading-backdrop", "fixture-1");

        // A continuing reload must retain the picture under the same disc.
        player.SendScriptMessage("osc-set-transport-reloading", "true");
        await Task.Delay(300);
        var reloadShot = await Capture("transport-reload-1920.png");
        var reloadDisc = reloadShot.At(0.5, 0.5);
        var reloadPicture = reloadShot.At(0.1, 0.5);
        Require(reloadPicture.R > 130, $"Transport reload covered the picture: {reloadPicture}.");
        Require(reloadDisc.R < reloadPicture.R - 40, $"Transport reload lost its spinner disc: {reloadDisc} over {reloadPicture}.");
        Require(reloadShot.MaxBrightnessInRing(0.5, 0.5, 15, 21) > 180, "Transport reload spinner ring is not visible.");
        player.SendScriptMessage("osc-set-transport-reloading", "false");

        // Credits countdown: gated waits, ungated shows the card.
        player.SendScriptMessage("osc-set-markers", "{\"content_id\":\"nov12-fixture\",\"credits_start\":7,\"credits_end\":9.6}");
        player.SendScriptMessage("osc-set-next-episode-detail", "{\"season_number\":1,\"episode_number\":3,\"title\":\"Synthetic Next\"}");
        player.SendScriptMessage("osc-set-next-episode", "true");
        player.SendScriptMessage("osc-set-autoplay-gate", "true");
        Command(player, "seek", "7.5", "absolute+exact");
        player.Play(); await Until(() => !player.IsPaused);
        await Task.Delay(400);
        player.Pause(); await Until(() => player.IsPaused);
        player.SendScriptMessage("osc-show"); await Task.Delay(300);
        Require(!(await Query()).GetProperty("countdown").GetBoolean(), "A gated credits region started the countdown.");
        var gatedShot = await Capture("credits-gated-1920.png");
        player.SendScriptMessage("osc-set-autoplay-gate", "false");
        await Task.Delay(400);
        Require((await Query()).GetProperty("countdown").GetBoolean(), "An ungated credits region did not start the countdown.");
        var countdownShot = await Capture("credits-countdown-1920.png");
        player.SendScriptMessage("osc-set-autoplay-gate", "true");
        await Task.Delay(200);
        Require(!(await Query()).GetProperty("countdown").GetBoolean(), "Becoming gated left the countdown up.");
        Require(!intents.Any(value => value.StartsWith("silo-next-episode", StringComparison.Ordinal)), "Gate changes advanced the episode.");

        await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, "hud-nov12-observations.json"), JsonSerializer.Serialize(new
        {
            script, scriptSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script))), observations,
            cover = new { left = left.ToString(), right = right.ToString(), centre = centre.ToString(), coverShot.Path },
            rebuffer = new { disc = disc.ToString(), picture = picture.ToString(), bufferShot.Path },
            transportReload = new { disc = reloadDisc.ToString(), picture = reloadPicture.ToString(), reloadShot.Path },
            gated = gatedShot.Path, countdown = countdownShot.Path,
            scope = "Actual offscreen libmpv window with the published production OSC; synthetic clip and host messages. No live server, installed app, physical input or display certification.",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Program.Log("PASS: Nov-12 HUD on actual libmpv: title-block finish time, regrouped/compact rail, owned loading cover under the spinner, rebuffer disc, still-watching credits gate.");

        void PublishTitle()
        {
            player.SendScriptMessage("osc-set-title", "Synthetic Show", "S1:E2");
            player.SendScriptMessage("osc-set-end-time-format", "12h", "Ends {time}");
            player.SendScriptMessage("osc-set-episode-navigation", "true", "true", "true");
            player.SendScriptMessage("osc-set-audio-tracks", "[{\"index\":0,\"language\":\"en\",\"language_name\":\"English\",\"channels\":6},{\"index\":1,\"language\":\"fr\",\"language_name\":\"French\",\"channels\":2}]");
            player.SendScriptMessage("osc-set-active-audio", "0");
            player.SendScriptMessage("osc-set-chapters", "[{\"index\":0,\"title\":\"Cold open\",\"start_seconds\":0,\"end_seconds\":4},{\"index\":1,\"title\":\"Act one\",\"start_seconds\":4,\"end_seconds\":10}]");
        }

        async Task LoadClip()
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Loaded() => loaded.TrySetResult();
            player.FileLoaded += Loaded;
            try { player.LoadFile(clip); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
            finally { player.FileLoaded -= Loaded; }
            player.Pause(); await Until(() => player.IsPaused && player.GetPropertyFlag("vo-configured"));
            player.SendScriptMessage("osc-set-visibility", "true");
            await Task.Delay(250);
        }

        async Task<JsonElement> Query()
        {
            var request = Guid.NewGuid().ToString("N");
            var receiver = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[request] = receiver;
            try { player.SendScriptMessage("osc-hud-nov12-query", request); return await receiver.Task.WaitAsync(TimeSpan.FromSeconds(4)); }
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
            return new Frame(path, (int)decoder.PixelWidth, (int)decoder.PixelHeight, pixels.DetachPixelData());
        }
    }

    private static double X(JsonElement rect) => rect.GetProperty("x").GetDouble();
    private static double W(JsonElement rect) => rect.GetProperty("w").GetDouble();

    private readonly record struct Pixel(int R, int G, int B)
    {
        public override string ToString() => $"rgb({R},{G},{B})";
    }

    private sealed record Frame(string Path, int Width, int Height, byte[] Pixels)
    {
        internal Pixel At(double fx, double fy)
        {
            var x = Math.Clamp((int)(fx * Width), 0, Width - 1);
            var y = Math.Clamp((int)(fy * Height), 0, Height - 1);
            var i = (y * Width + x) * 4;
            return new Pixel(Pixels[i + 2], Pixels[i + 1], Pixels[i]);
        }

        // Brightest pixel in an annulus around a fractional centre (radius in frame pixels).
        internal int MaxBrightnessInRing(double fx, double fy, double inner, double outer)
        {
            var cx = fx * Width; var cy = fy * Height; var best = 0;
            for (var y = (int)(cy - outer); y <= (int)(cy + outer); y++)
            for (var x = (int)(cx - outer); x <= (int)(cx + outer); x++)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
                var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d < inner || d > outer) continue;
                var i = (y * Width + x) * 4;
                best = Math.Max(best, (Pixels[i] + Pixels[i + 1] + Pixels[i + 2]) / 3);
            }
            return best;
        }
    }

    private static void GenerateClip(string path, byte shade, int frames)
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
