using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

/// <summary>
/// Measurement only (SILO_SCROLL_OFFSCREEN=1): what WinUI does with a decoded image that sits in the live tree
/// outside the window. 68 images get their bitmaps (SetSourceAsync, DecodePixelWidth as the cards use) two
/// windows below the view; the process working set (what WinUI's collection trigger reads) and private bytes
/// are logged before, while they stay off screen, once they are scrolled into view, after they leave it, and
/// when they come back.
/// </summary>
internal static class ScrollOffscreenImages
{
    private const int Rows = 4, Columns = 17, Width = 200, Height = 300, DecodeWidth = 400;

    internal static async Task RunAsync(Window window, int clientHeight)
    {
        var grid = new StackPanel { Spacing = 8 };
        var spacer = new Border { Height = clientHeight * 2 };
        grid.Children.Add(spacer);
        var images = new List<Image>();
        for (var row = 0; row < Rows; row++)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            for (var column = 0; column < Columns; column++)
            {
                var image = new Image { Width = Width, Height = Height, Stretch = Stretch.UniformToFill };
                images.Add(image); line.Children.Add(image);
            }
            grid.Children.Add(line);
        }
        var scroller = new ScrollViewer { Content = grid };
        window.Content = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 23, 34)), Children = { scroller } };
        await ScrollSmoothnessNativeFixture.WaitLoadedAsync(scroller);
        var encoded = new List<byte[]>();
        for (var i = 0; i < images.Count; i++) encoded.Add(await EncodeAsync(i));
        await Task.Delay(1500);
        await MarkAsync("offscreen images: page with empty images, none in view");

        var opened = 0;
        foreach (var image in images) image.ImageOpened += (_, _) => opened++;
        for (var i = 0; i < images.Count; i++)
        {
            var bitmap = new BitmapImage { DecodePixelWidth = DecodeWidth, DecodePixelType = DecodePixelType.Logical };
            images[i].Source = bitmap;
            using var stream = new MemoryStream(encoded[i]);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
        }
        await ScrollSmoothnessNativeFixture.Until(() => opened == images.Count, 10000);
        var sized = images.Count(image => image.Source is BitmapImage { PixelWidth: > 0 });
        Program.Log($"INFO: scroll offscreen images: {images.Count} bitmaps set two windows below the view; ImageOpened {opened}, PixelWidth known {sized}");
        await Task.Delay(1500);
        await MarkAsync("offscreen images: bitmaps set, still off screen (1.5 s)");
        await Task.Delay(3000);
        await MarkAsync("offscreen images: still off screen (4.5 s)");

        scroller.ChangeView(null, clientHeight * 2, null, true);
        await Task.Delay(1500);
        await MarkAsync("offscreen images: scrolled into view (1.5 s)");
        await Task.Delay(3000);
        await MarkAsync("offscreen images: in view (4.5 s)");

        scroller.ChangeView(null, 0, null, true);
        await Task.Delay(1500);
        await MarkAsync("offscreen images: scrolled out of view (1.5 s)");
        await Task.Delay(5000);
        await MarkAsync("offscreen images: out of view (6.5 s)");

        scroller.ChangeView(null, clientHeight * 2, null, true);
        await Task.Delay(1500);
        await MarkAsync("offscreen images: back in view (1.5 s)");

        scroller.ChangeView(null, 0, null, true);
        await Task.Delay(1500);
        foreach (var image in images) image.Source = null;
        window.Content = new Grid();
        await Task.Delay(1500);
        await MarkAsync("offscreen images: page gone (1.5 s)");
    }

    // Process memory plus the GPU memory this process holds (dedicated and shared, from the GPU Process Memory counters).
    private static async Task MarkAsync(string when)
    {
        ScrollProcessMemory.Mark(when);
        var pid = Environment.ProcessId;
        var script = @"$s = (Get-Counter -Counter '\GPU Process Memory(pid_" + pid + @"_*)\Dedicated Usage','\GPU Process Memory(pid_" + pid + @"_*)\Shared Usage' -ErrorAction SilentlyContinue).CounterSamples; " +
            "$d = ($s | Where-Object Path -like '*dedicated usage' | Measure-Object CookedValue -Sum).Sum; $h = ($s | Where-Object Path -like '*shared usage' | Measure-Object CookedValue -Sum).Sum; " +
            "'{0:0} {1:0}' -f ($d / 1MB), ($h / 1MB)";
        var start = new System.Diagnostics.ProcessStartInfo("powershell", ["-NoProfile", "-Command", script]) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = (await process.StandardOutput.ReadToEndAsync()).Trim().Split(' ');
        await process.WaitForExitAsync();
        Program.Log($"INFO: scroll memory mark {when}: GPU dedicated {(output.Length > 0 ? output[0] : "?")} MB, GPU shared {(output.Length > 1 ? output[1] : "?")} MB");
    }

    // A 1000x1500 JPEG with a gradient and noise (compresses like artwork; each image differs).
    private static async Task<byte[]> EncodeAsync(int seed)
    {
        const int w = 1000, h = 1500;
        var pixels = new byte[w * h * 4];
        var random = new Random(seed);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                pixels[i] = (byte)((x * 255 / w + random.Next(40)) & 255);
                pixels[i + 1] = (byte)((y * 255 / h + seed * 13) & 255);
                pixels[i + 2] = (byte)((seed * 37 + random.Next(40)) & 255);
                pixels[i + 3] = 255;
            }
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, w, h, 96, 96, pixels);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        stream.Seek(0);
        await stream.ReadAsync(bytes.AsBuffer(), (uint)stream.Size, InputStreamOptions.None);
        return bytes;
    }
}
