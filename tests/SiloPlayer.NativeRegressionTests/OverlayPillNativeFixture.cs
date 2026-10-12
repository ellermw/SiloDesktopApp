using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;
using SiloPlayer.Services;
using SiloPlayer.Views;

internal static class OverlayPillNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var surface = new StackPanel { Width = 480, Padding = new Thickness(16), Spacing = 12,
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 38, 38, 42)) };
        parent.Children.Add(surface);
        var build = typeof(PosterCard).GetMethod("BuildBadge", BindingFlags.NonPublic | BindingFlags.Static)!;
        Border Make(string label, string preset, double scale, bool icon) => (Border)build.Invoke(null,
            new object[] { label, "resolution_hdr", new OverlayItemConfig(true, OverlayPosition.TopLeft, "#3b82f6", icon), preset, scale })!;
        try
        {
            foreach (var preset in new[] { "classic", "pill", "vibrant", "square" })
            {
                surface.Children.Add(new TextBlock { Text = preset, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
                foreach (var label in new[] { "4K", "1080P", "DV HDR10" }) row.Children.Add(Make(label, preset, 1, false));
                surface.Children.Add(row);
            }
            await Task.Delay(100);
            surface.UpdateLayout();
            await MediaParityNativeFixture.CaptureAsync(surface, "overlay-badges.png");
            surface.Children.Clear();

            var failures = new List<string>();
            var count = 0;
            foreach (var preset in new[] { "classic", "pill" })
            foreach (var scale in new[] { .7, 1d, 1.8 })
            foreach (var icon in new[] { false, true })
            foreach (var label in new[] { "4K", "1080P", "DV HDR10" })
            {
                var badge = Make(label, preset, scale, icon);
                badge.HorizontalAlignment = HorizontalAlignment.Left;
                // Paint the actual production border into a transparent mask:
                // the measured flat top must survive independently of text or theme.
                badge.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
                badge.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
                surface.Children.Add(badge);
                await Task.Delay(25);
                surface.UpdateLayout();
                var image = new RenderTargetBitmap();
                await image.RenderAsync(badge);
                var pixels = (await image.GetPixelsAsync()).ToArray();
                if (image.PixelWidth == 0 || image.PixelHeight == 0 || pixels.Length == 0)
                    throw new InvalidOperationException("Overlay shape capture is empty.");
                var ratio = image.PixelWidth / badge.ActualWidth;
                var left = (int)Math.Ceiling((badge.ActualHeight / 2 + 2) * ratio);
                var right = (int)Math.Floor((badge.ActualWidth - badge.ActualHeight / 2 - 2) * ratio);
                var y = Math.Clamp((int)Math.Ceiling(1.5 * ratio), 1, image.PixelHeight - 1);
                var minimumAlpha = 255;
                for (var x = left; x <= right; x++)
                    minimumAlpha = Math.Min(minimumAlpha, pixels[(y * image.PixelWidth + x) * 4 + 3]);
                Program.Log($"Badge {preset}/{label}/{scale:R}/icon={icon}: {badge.ActualWidth:R}x{badge.ActualHeight:R}, radius={badge.CornerRadius.TopLeft:R}, flatTopAlpha={minimumAlpha}.");
                if (right <= left || minimumAlpha < 240)
                    failures.Add($"{preset}/{label}/{scale}/icon={icon} has a curved oval top instead of the CSS pill's straight edge (alpha {minimumAlpha}).");
                // Enlarging a text line after first layout must resize the ends too.
                var text = ((StackPanel)badge.Child).Children.OfType<TextBlock>().Single();
                text.FontSize *= 1.5;
                text.LineHeight *= 1.5;
                await Task.Delay(25);
                surface.UpdateLayout();
                if (Math.Abs(badge.CornerRadius.TopLeft - Math.Min(badge.ActualWidth, badge.ActualHeight) / 2) > .01)
                    failures.Add($"{preset}/{label}/{scale} does not retain circular ends after a text-size change.");
                surface.Children.Remove(badge);
                count++;
            }
            var detail = new ItemDetailPage();
            var metadata = (StackPanel)detail.FindName("MetaPanel");
            foreach (var name in new[] { "YearBadge", "ContentRatingBadge", "AdvisoryAgeBadge", "RuntimeBadge", "SeasonCountBadge", "EpisodeCountBadge" })
            {
                var badge = (Border)detail.FindName(name);
                metadata.Children.Remove(badge);
                badge.Visibility = Visibility.Visible;
                ((TextBlock)badge.Child).Text = name == "YearBadge" ? "2026" : name == "ContentRatingBadge" ? "TV-MA" : "12 EPISODES";
                badge.HorizontalAlignment = HorizontalAlignment.Left;
                badge.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
                badge.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
                surface.Children.Add(badge);
                await Task.Delay(25); surface.UpdateLayout();
                var image = new RenderTargetBitmap(); await image.RenderAsync(badge);
                var pixels = (await image.GetPixelsAsync()).ToArray();
                var ratio = image.PixelWidth / badge.ActualWidth;
                var left = (int)Math.Ceiling((badge.ActualHeight / 2 + 2) * ratio);
                var right = (int)Math.Floor((badge.ActualWidth - badge.ActualHeight / 2 - 2) * ratio);
                var y = Math.Clamp((int)Math.Ceiling(1.5 * ratio), 1, image.PixelHeight - 1);
                var minimumAlpha = 255;
                for (var x = left; x <= right; x++) minimumAlpha = Math.Min(minimumAlpha, pixels[(y * image.PixelWidth + x) * 4 + 3]);
                Program.Log($"Detail {name}: {badge.ActualWidth:R}x{badge.ActualHeight:R}, radius={badge.CornerRadius.TopLeft:R}, flatTopAlpha={minimumAlpha}.");
                if (right <= left || minimumAlpha < 240 || Math.Abs(badge.CornerRadius.TopLeft - Math.Min(badge.ActualWidth, badge.ActualHeight) / 2) > .01)
                    failures.Add($"Actual media-page {name} renders an oval instead of a pill (alpha {minimumAlpha}).");
                surface.Children.Remove(badge);
            }
            if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
            Program.Log($"PASS: {count} rendered quality/HDR variants and six actual media-page badges retain straight pill edges and circular ends; poster ends also follow text-size changes.");
        }
        finally { parent.Children.Remove(surface); }
    }
}
