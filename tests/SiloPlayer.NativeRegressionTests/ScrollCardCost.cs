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

/// <summary>GC handles one live Home card holds (wrappers of its XAML elements, delegates XAML holds), and whether they go when the card does.</summary>
internal static class ScrollCardHandles
{
    internal static async Task<string> MeasureAsync(Panel host, IReadOnlyList<MediaItem> items, bool landscape)
    {
        const int Count = 24;
        var before = await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"before {Count} {(landscape ? "landscape" : "poster")} cards");
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Top };
        for (var i = 0; i < Count; i++)
        {
            FrameworkElement card = landscape ? new LandscapeCard { UsePosterAspect = true } : new PosterCard();
            if (card is PosterCard poster) { poster.MediaItem = items[i % items.Count]; poster.SetCatalogGridLayout(178); }
            else if (card is LandscapeCard wide) { wide.MediaItem = items[i % items.Count]; wide.SetCardWidth(178); }
            panel.Children.Add(card);
        }
        host.Children.Add(panel);
        panel.UpdateLayout();
        await Task.Delay(1500);
        var live = await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"with {Count} live cards");
        var probe = new WeakReference(panel.Children[0]);
        host.Children.Remove(panel);
        panel = null;
        await Task.Delay(1500);
        var after = await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"after the {Count} cards left the tree");
        var afterAgain = await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync("one more full collection later");
        return $"{(landscape ? "landscape" : "poster")} card: {(live - before) / (double)Count:0} GC handles per live card; {after - before} handles left after the cards were removed, {afterAgain - before} after one more collection; first card collected={!probe.IsAlive}";
    }
    /// <summary>
    /// <summary>
    /// The full-collection cost of live cards, measured in a clean process (Home's data loaded, Home not opened):
    /// a forced collection's reference-tracker walk and GC work with 150 cards of a kind in the window, and with
    /// them gone (and what still keeps them, if anything). Also dumps one card's element tree.
    /// </summary>
    internal static async Task CostPerCardAsync(Window window, SiloPlayer.ViewModels.HomeViewModel vm)
    {
        await vm.LoadCommand.ExecuteAsync(null);
        await ScrollSmoothnessNativeFixture.Until(() => vm.Sections.Count > 4 && vm.Sections.All(s => s.LoadCompleted), 15000);
        var host = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) };
        window.Content = host;
        await Task.Delay(500);
        foreach (var landscape in new[] { false, true })
        {
            var kind = landscape ? "landscape" : "poster";
            var empty = await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"cards-gc {kind}: no cards");
            AddCards(host, landscape ? vm.Sections[0].Items : vm.Sections[2].Items, landscape);
            await Task.Delay(3000);
            var live = await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"cards-gc {kind}: 150 live cards");
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"cards-gc {kind}: 150 live cards, again");
            Program.Log($"INFO: scroll cards-gc {kind} card has {DumpTree(host, kind)} elements in its visual tree");
            host.Children.Clear();
            // XAML releases removed objects over the next frames and collections.
            await Task.Delay(3000);
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"cards-gc {kind}: cards gone");
            await Task.Delay(1000);
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"cards-gc {kind}: cards gone, again");
            ScrollHeapCensus.Take($"{kind} cards gone", landscape ? "SiloPlayer.Controls.LandscapeCard" : "SiloPlayer.Controls.PosterCard");
            Program.Log($"INFO: scroll cards-gc {kind}: {(live - empty) / 150.0:0} handles per live card");
        }
    }

    private static void AddCards(Panel host, IReadOnlyList<MediaItem> items, bool landscape)
    {
        var panel = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 15 };
        for (var i = 0; i < 150; i++)
        {
            FrameworkElement card = landscape ? new LandscapeCard() : new PosterCard();
            if (card is PosterCard poster) { poster.MediaItem = items[i % items.Count]; poster.SetCatalogGridLayout(178); }
            else if (card is LandscapeCard wide) { wide.MediaItem = items[i % items.Count]; wide.SetCardWidth(315); }
            panel.Children.Add(card);
        }
        host.Children.Add(panel);
        panel.UpdateLayout();
    }

    private static int DumpTree(Panel host, string kind)
    {
        if (host.Children.FirstOrDefault() is not Panel panel || panel.Children.FirstOrDefault() is not FrameworkElement first) return 0;
        var tree = new System.Text.StringBuilder();
        var count = 0;
        void Walk(DependencyObject node, int depth)
        {
            count++;
            var element = node as FrameworkElement;
            tree.Append(new string(' ', depth * 2)).Append(node.GetType().Name)
                .Append(element?.Name is { Length: > 0 } name ? " #" + name : "")
                .Append(node is UIElement { Visibility: Visibility.Collapsed } ? " [collapsed]" : "")
                .Append(node is UIElement { Opacity: 0 } ? " [opacity 0]" : "").Append('\n');
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                Walk(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i), depth + 1);
        }
        Walk(first, 0);
        File.AppendAllText(Path.Combine(Program.ResultDirectory, "scroll-card-tree.txt"), $"== {kind} card element tree ({count} elements)\n" + tree);
        return count;
    }

    /// <summary>
    /// What one element of each kind adds to a full collection: 1000 of a kind in the window, the second of two
    /// forced collections (walk and GC work) against the same with none (SILO_SCROLL_CARDGC=elements).
    /// </summary>
    internal static async Task ElementCostAsync(Window window)
    {
        var host = new Grid();
        window.Content = host;
        await Task.Delay(500);
        var shared = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
        var build = typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Controls.OverlayBadgeRenderer")!.GetMethod("Build", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var config = new SiloPlayer.Services.OverlayItemConfig(true, SiloPlayer.Services.OverlayPosition.TopLeft);
        var kinds = new (string Name, int Count, Func<int, UIElement?> Make, string? Xaml)[]
        {
            ("code Border, shared brush", 1000, _ => new Border { Width = 10, Height = 10, Background = shared }, null),
            ("code Border, own brush", 1000, _ => new Border { Width = 10, Height = 10, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red) }, null),
            ("code Border with a Loaded handler", 1000, _ => { var b = new Border { Width = 10, Height = 10, Background = shared }; b.Loaded += (_, _) => { }; return b; }, null),
            ("code Button with a FontIcon", 1000, _ => new Button { Content = new FontIcon { Glyph = "\uE712" } }, null),
            ("XAML Border, never touched", 1000, null!, "<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Width='10' Height='10' Background='Red'/>"),
            ("XAML Button with a FontIcon, never touched", 1000, null!, "<Button xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><FontIcon Glyph='&#xE712;'/></Button>"),
            ("DefaultArtwork", 500, _ => new DefaultArtwork { Width = 40, Height = 60 }, null),
            ("code badge (OverlayBadgeRenderer)", 500, _ => (UIElement)build.Invoke(null, ["4K", "resolution", config, "classic", 1.0, null, null, null, null])!, null),
        };
        foreach (var kind in kinds)
        {
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"elements {kind.Name}: none");
            var none = await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"elements {kind.Name}: none, again");
            var noneCost = LastCost();
            AddElements(host, kind.Count, kind.Make, kind.Xaml);
            await Task.Delay(1500);
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"elements {kind.Name}: {kind.Count}");
            var live = await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"elements {kind.Name}: {kind.Count}, again");
            var liveCost = LastCost();
            Program.Log($"INFO: scroll element cost {kind.Name}: per 1000 adds walk {(liveCost.Walk - noneCost.Walk) * 1000 / kind.Count:0.0} ms, GC work {(liveCost.Work - noneCost.Work) * 1000 / kind.Count:0.0} ms, {(live - none) / (double)kind.Count:0.0} handles each");
            host.Children.Clear();
            await Task.Delay(2500);
        }
    }

    private static (double Walk, double Work) LastCost()
    {
        var text = ScrollSmoothnessNativeFixture.Gc!.PauseOf(ScrollSmoothnessNativeFixture.Gc.LastHandles.Gc);
        var walk = System.Text.RegularExpressions.Regex.Match(text, @"walk and setup ([0-9.]+) ms");
        var work = System.Text.RegularExpressions.Regex.Match(text, @"GC work ([0-9.]+) ms");
        return (walk.Success ? double.Parse(walk.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0,
            work.Success ? double.Parse(work.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0);
    }

    private static void AddElements(Panel host, int count, Func<int, UIElement?>? make, string? xaml)
    {
        if (xaml != null)
        {
            var markup = "<StackPanel xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Orientation='Horizontal'>"
                + string.Concat(Enumerable.Repeat(xaml.Replace(" xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'", ""), count)) + "</StackPanel>";
            host.Children.Add((UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load(markup));
            return;
        }
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < count; i++) panel.Children.Add(make!(i)!);
        host.Children.Add(panel);
    }

    /// <summary>150 poster cards three ways (no item; item without artwork; item with artwork): what part of a card a full collection pays for (SILO_SCROLL_CARDGC=variants).</summary>
    internal static async Task CardVariantCostAsync(Window window, SiloPlayer.ViewModels.HomeViewModel vm)
    {
        await vm.LoadCommand.ExecuteAsync(null);
        await ScrollSmoothnessNativeFixture.Until(() => vm.Sections.Count > 4 && vm.Sections.All(s => s.LoadCompleted), 15000);
        var host = new Grid();
        window.Content = host;
        await Task.Delay(500);
        var items = vm.Sections[2].Items;
        foreach (var (name, bind, images) in new[] { ("no item", false, false), ("item, no artwork", true, false), ("item and artwork", true, true), ("no item, again", false, false) })
        {
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"variant {name}: none");
            var none = await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"variant {name}: none, again");
            var noneCost = LastCost();
            var panel = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 15 };
            for (var i = 0; i < 150; i++)
            {
                var card = new PosterCard { SuppressImageLoading = !images };
                if (bind) card.MediaItem = items[i % items.Count];
                card.SetCatalogGridLayout(178);
                panel.Children.Add(card);
            }
            host.Children.Add(panel);
            panel = null;
            await Task.Delay(3000);
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"variant {name}: 150");
            var live = await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"variant {name}: 150, again");
            var liveCost = LastCost();
            Program.Log($"INFO: scroll card variant {name}: per card walk {(liveCost.Walk - noneCost.Walk) / 150 * 1000:0} us, GC work {(liveCost.Work - noneCost.Work) / 150 * 1000:0} us, {(live - none) / 150.0:0} handles");
            host.Children.Clear();
            await Task.Delay(4000);
        }
    }

    /// <summary>Does walking the visual tree from managed code (VisualTreeHelper, as the fixture's census does) make full collections dearer? (SILO_SCROLL_CARDGC=traversal)</summary>
    internal static async Task TraversalCostAsync(Window window, SiloPlayer.ViewModels.HomeViewModel vm)
    {
        await vm.LoadCommand.ExecuteAsync(null);
        await ScrollSmoothnessNativeFixture.Until(() => vm.Sections.Count > 4 && vm.Sections.All(s => s.LoadCompleted), 15000);
        var host = new Grid();
        window.Content = host;
        await Task.Delay(500);
        var items = vm.Sections[2].Items;
        var panel = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 15 };
        for (var i = 0; i < 150; i++) { var card = new PosterCard { MediaItem = items[i % items.Count] }; card.SetCatalogGridLayout(178); panel.Children.Add(card); }
        host.Children.Add(panel);
        panel = null;
        await Task.Delay(3000);
        for (var round = 0; round < 3; round++)
        {
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"traversal round {round}: before");
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"traversal round {round}: before, again");
            var before = LastCost();
            var elements = CountDescendants(host);
            await Task.Delay(500);
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"traversal round {round}: after walking {elements} elements");
            await ScrollSmoothnessNativeFixture.HandlesAfterFullGcAsync($"traversal round {round}: after walking, again");
            var after = LastCost();
            Program.Log($"INFO: scroll traversal round {round}: walking {elements} elements from managed code changed the collection's walk {before.Walk:0.0} -> {after.Walk:0.0} ms, GC work {before.Work:0.0} -> {after.Work:0.0} ms");
        }
    }

    private static int CountDescendants(DependencyObject root)
    {
        var count = 0;
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            count++;
            for (var i = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--) stack.Push(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
        }
        return count;
    }
}
