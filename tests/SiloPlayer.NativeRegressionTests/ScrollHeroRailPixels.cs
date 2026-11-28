using System.Reflection;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;

/// <summary>
/// The hero's progress rail as the compositor presents it: the window's own pixels (PrintWindow with
/// PW_RENDERFULLCONTENT), at its real scale. Before the rail ran in the compositor it was a Width animation
/// on the fill, laid out in whole pixels; so at every width w the rail held at w must draw exactly what a
/// fill w wide draws. Both are drawn in place on the live hero, one after the other, and compared pixel by pixel.
/// </summary>
internal static class ScrollHeroRailPixels
{
    private static readonly int[] Widths = [1, 2, 3, 4, 5, 37, 52, 100, 142, 143, 144];

    internal static async Task<(bool? Identical, string Summary)> CompareAsync(Window window, HeroCarousel hero)
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        if (typeof(HeroCarousel).GetMethod("SetProgressRail", Private) is not { } setRail)
            return (null, "this build draws the rail with a Width animation: nothing to compare");
        var fill = (Border)hero.FindName("ProgressRailFill");
        var start = (Border)hero.FindName("ProgressRailStart");
        var body = (FrameworkElement)hero.FindName("ProgressRailBody");
        var bodyVisual = ElementCompositionPreview.GetElementVisual(body);
        var bodyClip = bodyVisual.Clip;
        var track = (FrameworkElement)hero.FindName("ProgressRailContainer");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var root = (UIElement)window.Content;
        var scale = hero.XamlRoot.RasterizationScale;
        var bounds = track.TransformToVisual(root).TransformBounds(new Windows.Foundation.Rect(0, 0, track.ActualWidth, track.ActualHeight));
        var region = (X: (int)Math.Floor(bounds.X * scale) - 3, Y: (int)Math.Floor(bounds.Y * scale) - 3,
            W: (int)Math.Ceiling(bounds.Width * scale) + 6, H: (int)Math.Ceiling(bounds.Height * scale) + 6);
        var report = new StringBuilder();
        report.AppendLine($"rail track at client px x={region.X + 3} y={region.Y + 3} ({bounds.Width}x{bounds.Height} dip, scale {scale}); values are the red channel, region padded by 3 px");

        async Task<byte[]> ShowAsync(Action draw)
        {
            draw();
            fill.UpdateLayout();
            byte[]? previous = null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                await FramesAsync(3);
                await Task.Delay(60);
                var crop = Crop(OfficialSmallChanges2NativeFixture.PrintWindow(hwnd, out var width, out var height), width, height, region);
                if (previous != null && crop.AsSpan().SequenceEqual(previous)) return crop;
                previous = crop;
            }
            return previous!;
        }

        // As before the compositor rail: the fill itself w wide, nothing slid, held still or hidden.
        Task<byte[]> AsWidthAsync(double w) => ShowAsync(() =>
        {
            setRail.Invoke(hero, [144d]);
            start.Visibility = Visibility.Collapsed;
            bodyVisual.Clip = null;
            fill.Width = w;
        });
        // The rail as the app holds it at w.
        Task<byte[]> AsRailAsync(double w) => ShowAsync(() =>
        {
            start.Visibility = Visibility.Visible;
            bodyVisual.Clip = bodyClip;
            fill.Width = 144;
            setRail.Invoke(hero, [w]);
        });

        var identical = true;
        var lines = new List<string>();
        try
        {
            // The window must be presenting before its pixels mean anything.
            var warm = await AsRailAsync(144);
            lines.Add($"presenting={warm.Any(b => b != 0)}");
            foreach (var w in Widths)
            {
                var pixels = (int)Math.Round(w * scale);
                var asWidth = await AsWidthAsync(pixels / scale);
                var asRail = await AsRailAsync(pixels / scale);
                var (differ, maxDelta) = Diff(asWidth, asRail);
                identical &= differ == 0;
                lines.Add($"w={pixels}px: {(differ == 0 ? "identical" : $"{differ} px differ (max channel delta {maxDelta})")}");
                if (differ != 0 || w is 37 or 144)
                {
                    report.AppendLine($"--- w={pixels}px drawn as a {pixels}px fill:");
                    Dump(report, asWidth, region.W, region.H);
                    report.AppendLine($"--- w={pixels}px drawn by the rail:");
                    Dump(report, asRail, region.W, region.H);
                }
            }
            // Between whole pixels: what the Width animation drew at a fractional width.
            var fractional = await AsWidthAsync(37.43 / scale);
            var whole = await AsWidthAsync(37 / scale);
            lines.Add($"a {37.43 / scale:0.##} dip Width draws as {(Diff(fractional, whole).Differ == 0 ? "exactly" : "not exactly")} the 37 px fill");
        }
        finally
        {
            start.Visibility = Visibility.Visible;
            bodyVisual.Clip = bodyClip;
            fill.Width = 144;
            setRail.Invoke(hero, [0d]);
        }
        report.Insert(0, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        await File.WriteAllTextAsync(Path.Combine(Program.ResultDirectory, "scroll-hero-rail-pixels.txt"), report.ToString());
        return (identical, string.Join("; ", lines));
    }

    private static byte[] Crop(byte[] bgra, int width, int height, (int X, int Y, int W, int H) r)
    {
        var crop = new byte[r.W * r.H * 4];
        if (bgra.Length < width * height * 4) return crop;
        for (var y = 0; y < r.H; y++)
        {
            var sy = r.Y + y;
            if (sy < 0 || sy >= height) continue;
            for (var x = 0; x < r.W; x++)
            {
                var sx = r.X + x;
                if (sx < 0 || sx >= width) continue;
                Buffer.BlockCopy(bgra, (sy * width + sx) * 4, crop, (y * r.W + x) * 4, 4);
            }
        }
        return crop;
    }

    private static (int Differ, int MaxDelta) Diff(byte[] a, byte[] b)
    {
        var differ = 0; var max = 0;
        for (var i = 0; i + 3 < a.Length; i += 4)
        {
            var d = Math.Max(Math.Abs(a[i] - b[i]), Math.Max(Math.Abs(a[i + 1] - b[i + 1]), Math.Abs(a[i + 2] - b[i + 2])));
            if (d == 0) continue;
            differ++; max = Math.Max(max, d);
        }
        return (differ, max);
    }

    private static void Dump(StringBuilder report, byte[] bgra, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            report.Append($"y{y,2}:");
            for (var x = 0; x < width; x++) report.Append($" {bgra[(y * width + x) * 4 + 2],3}");
            report.AppendLine();
        }
    }

    private static Task FramesAsync(int count)
    {
        var done = new TaskCompletionSource();
        var seen = 0;
        void OnRendering(object? sender, object e)
        {
            if (++seen < count) return;
            CompositionTarget.Rendering -= OnRendering;
            done.TrySetResult();
        }
        CompositionTarget.Rendering += OnRendering;
        return done.Task;
    }
}
