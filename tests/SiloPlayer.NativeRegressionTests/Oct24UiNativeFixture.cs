using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;

internal static class Oct24UiNativeFixture
{
    internal static async Task RunHeroAsync(StackPanel parent)
    {
        var oldWidth = parent.Width; var oldHeight = parent.Height;
        try
        {
            foreach (var width in new[] { 460d, 639d, 640d, 1280d })
            {
                parent.Width = width; parent.Height = 900;
                parent.Measure(new Windows.Foundation.Size(width, 900)); parent.Arrange(new Windows.Foundation.Rect(0, 0, width, 900)); parent.UpdateLayout();
                var hero = new HeroCarousel { Width = width, ItemsSource = new List<MediaItem> { new() {
                    ContentId = "oct24-long-overview", Type = "movie", Year = 2026,
                    Title = "A Long Translated Feature Title With More Than One Display Line",
                    Overview = string.Join(" ", Enumerable.Repeat("Une longue histoire traduite qui garde les actions accessibles.", 35)),
                    MachineTranslatedFields = ["overview"] } } };
                parent.Children.Add(hero);
                try
                {
                    await Task.Delay(80); parent.UpdateLayout();
                    typeof(HeroCarousel).GetMethod("UpdateHeightFromWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hero, null);
                    hero.UpdateLayout();
                    var overview = (TextBlock)hero.FindName("HeroOverview");
                    var expected = width >= 640 ? 3 : 2;
                    if (overview.MaxLines != expected) throw new InvalidOperationException($"Hero overview at {width}px: expected {expected} lines, actual {overview.MaxLines}.");
                    if (overview.ActualHeight > expected * overview.LineHeight + 1) throw new InvalidOperationException($"Long translated overview exceeds the native synopsis line budget: {overview.ActualHeight}px, line height {overview.LineHeight}px.");
                    var more = (Button)hero.FindName("MoreInfoButton");
                    if (more.Visibility != Visibility.Visible || !more.IsEnabled || more.ActualHeight <= 0)
                        throw new InvalidOperationException("More Info is unavailable after the long overview layout.");
                    await MediaParityNativeFixture.CaptureAsync(hero, $"oct24-hero-overview-{width}.png");
                }
                finally { parent.Children.Remove(hero); }
            }
        }
        finally { parent.Width = oldWidth; parent.Height = oldHeight; }
    }
}
