using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;

/// <summary>
/// What one Home card costs on the UI thread, split into the phases a row's repeater goes through:
/// construction (template expansion), binding its item, and its first measure/arrange in the live tree.
/// Cards are made directly, in a panel inside the page's own window, with the page's services.
/// </summary>
internal static class ScrollCardCost
{
    internal static async Task<string> MeasureAsync(Panel host, IReadOnlyList<MediaItem> items, string kind)
    {
        var count = Math.Min(items.Count, 24);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Top };
        host.Children.Add(panel);
        await Task.Delay(50);
        try
        {
            var sw = Stopwatch.StartNew();
            var cards = new List<FrameworkElement>();
            for (var i = 0; i < count; i++) cards.Add(kind == "poster" ? new PosterCard() : new LandscapeCard { UsePosterAspect = true });
            var construct = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            for (var i = 0; i < count; i++)
            {
                if (cards[i] is PosterCard poster) { poster.MediaItem = items[i]; poster.SetCatalogGridLayout(178); }
                else if (cards[i] is LandscapeCard landscape) { landscape.MediaItem = items[i]; landscape.SetCardWidth(178); }
            }
            var bind = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            foreach (var card in cards) panel.Children.Add(card);
            panel.UpdateLayout();
            var layout = sw.Elapsed.TotalMilliseconds;
            // Loaded handlers run when the tree next ticks.
            sw.Restart();
            var loaded = 0;
            foreach (var card in cards) card.Loaded += (_, _) => loaded++;
            await Task.Yield();
            await Task.Delay(1);
            var loadedMs = sw.Elapsed.TotalMilliseconds;
            // A second batch of the same cards: what a warmed process (JIT, XAML type info, styles) still pays.
            sw.Restart();
            var again = new List<FrameworkElement>();
            for (var i = 0; i < count; i++) again.Add(kind == "poster" ? new PosterCard() : new LandscapeCard { UsePosterAspect = true });
            var construct2 = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            for (var i = 0; i < count; i++)
            {
                if (again[i] is PosterCard poster) { poster.MediaItem = items[i]; poster.SetCatalogGridLayout(178); }
                else if (again[i] is LandscapeCard landscape) { landscape.MediaItem = items[i]; landscape.SetCardWidth(178); }
            }
            var bind2 = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            panel.Children.Clear();
            foreach (var card in again) panel.Children.Add(card);
            panel.UpdateLayout();
            var layout2 = sw.Elapsed.TotalMilliseconds;
            return $"{kind} x{count} per card: construct {construct / count:0.00} ms, bind {bind / count:0.00} ms, first layout {layout / count:0.00} ms, "
                + $"loaded tick {loadedMs:0} ms total | warmed: construct {construct2 / count:0.00}, bind {bind2 / count:0.00}, layout {layout2 / count:0.00} ms";
        }
        finally { host.Children.Remove(panel); }
    }
}
