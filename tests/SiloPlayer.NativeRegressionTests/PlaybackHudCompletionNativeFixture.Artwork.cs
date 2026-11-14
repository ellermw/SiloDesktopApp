using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;
using SiloPlayer.Services;
using Windows.Graphics.Imaging;
using Windows.Storage;

internal static partial class PlaybackHudCompletionNativeFixture
{
    private static async Task VerifyStill(PlayerService service, Wire wire, EpisodeSpoilerPreferences preferences)
    {
        var pixels = new byte[160 * 90 * 4];
        for (var y = 0; y < 90; y++)
        for (var x = 0; x < 160; x++)
        {
            var i = (y * 160 + x) * 4;
            pixels[i + ((x / 8 + y / 8) % 2 == 0 ? 0 : 2)] = 255;
            pixels[i + 3] = 255;
        }
        using (var stream = new MemoryStream())
        {
            using var random = stream.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, random);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 160, 90, 96, 96, pixels);
            await encoder.FlushAsync(); wire.StillPng = stream.ToArray();
        }
        await preferences.EnsureLoadedAsync(); await Task.Delay(100);
        service.SetNextEpisodeHint(service.ContentId!, "private-next", "Next", "Show", "https://hud-completion.fixture.invalid/still.png",
            null, seasonNumber: 1, episodeNumber: 2, played: false, posterIsEpisodeStill: true);
        Call(service, "PublishCreditsEpisodeDetail", false);
        var key = (string)Field(service, "_creditsStillKey")!;
        await (Task)Call(service, "LoadCreditsStillAsync", key)!;
        var path = (string?)Field(service, "_creditsStillPath");
        Check(path != null, "Protected episode still never reached the owned native bitmap.");
        var blurred = await File.ReadAllBytesAsync(path!);
        Check(Variation(blurred) < 80, "Unwatched credits still bypassed the spoiler blur.");

        wire.HideImages = false; preferences.Invalidate(); await preferences.EnsureLoadedAsync(); await Task.Delay(150);
        var changedKey = (string)Field(service, "_creditsStillKey")!;
        Check(key != changedKey, "Privacy change did not retire the previous artwork owner.");
        await (Task)Call(service, "LoadCreditsStillAsync", changedKey)!;
        path = (string?)Field(service, "_creditsStillPath");
        Check(path != null && Variation(await File.ReadAllBytesAsync(path)) > 180, "Unhidden native still did not recover the original cached artwork.");

        wire.HoldStill = true;
        // A fresh URL per run: once a run's released download reached the fixture's image cache,
        // a rerun in the same result directory read it from disk and never entered the held request.
        service.SetNextEpisodeHint(service.ContentId!, "held-next", "Held", "Show", $"https://hud-completion.fixture.invalid/held-{Guid.NewGuid():N}.png",
            null, seasonNumber: 1, episodeNumber: 3, played: false, posterIsEpisodeStill: true);
        Call(service, "PublishCreditsEpisodeDetail", false);
        var held = (Task)Call(service, "LoadCreditsStillAsync", (string)Field(service, "_creditsStillKey")!)!;
        await wire.StillEntered.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Call(service, "RetireCreditsStill"); wire.StillRelease.TrySetResult(); await held;
        Check(Field(service, "_creditsStillKey") == null && Field(service, "_creditsStillPath") == null,
            "Cancellation-ignoring thumbnail completion adopted a retired credits image.");
        Program.Log("PASS: real credits image decode/cache/blur, preference change and cancellation-ignoring thumbnail retirement.");
    }

    private static int Variation(byte[] pixels)
    {
        var min = 255; var max = 0;
        for (var y = 20; y < 106; y++)
        for (var x = 30; x < 194; x++)
        {
            var value = pixels[(y * 224 + x) * 4];
            min = Math.Min(min, value); max = Math.Max(max, value);
        }
        return max - min;
    }

    // WinUI's RenderTargetBitmap excludes owned HWNDs. Compose the independently
    // captured real libmpv frame at its measured screen rectangle for review.
    private static async Task CompositeVideo(FrameworkElement root, int width, int height, byte[] pixels, PlayerService service, string name)
    {
        var video = (MpvVideoWindow?)Field(service, "_videoWindow");
        if (video == null || !IsWindowVisible(video.Hwnd)) return;
        var path = Path.Combine(Program.ResultDirectory, Path.GetFileNameWithoutExtension(name) + "-video.png");
        Check(typeof(MpvPlayer).GetMethod("Command", Flags)!.Invoke(service.Mpv,
            [(object)new[] { "screenshot-to-file", path, "window" }]) is true, "Finishing video screenshot failed.");
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var data = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        GetWindowRect(video.Hwnd, out var bounds);
        var origin = new Point(); ClientToScreen(WinRT.Interop.WindowNative.GetWindowHandle(SiloPlayer.App.MainWindowInstance!), ref origin);
        var left = bounds.Left - origin.X; var top = bounds.Top - origin.Y;
        for (var y = 0; y < decoder.PixelHeight && top + y < height; y++)
        {
            if (top + y < 0 || left < 0 || left >= width) continue;
            var count = Math.Min((int)decoder.PixelWidth, width - left) * 4;
            Buffer.BlockCopy(data, y * (int)decoder.PixelWidth * 4, pixels, ((top + y) * width + left) * 4, count);
        }
    }
}
