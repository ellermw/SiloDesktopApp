using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using Windows.Foundation;

/// <summary>
/// Badge text over a see-through fill (official #5226), the hero progress rail restarting when the hero comes back (#5121) and a row heading's long
/// word breaking instead of running under Explore all (#5229), with synthetic data at 1440 and 480 wide. Selector: home-badges-hero-heading.
/// </summary>
internal static class HomeBadgesHeroHeadingNativeFixture
{
    internal static async Task RunAsync(StackPanel parent)
    {
        var differences = new List<string>();
        foreach (var width in new[] { 1440d, 480d })
        {
            await BadgesAsync(parent, width, differences);
            await HeadingsAsync(parent, width, differences);
        }
        await HeroRailAsync(parent, differences);
        if (differences.Count > 0) throw new InvalidOperationException(string.Join("\n", differences));
        Program.Log("PASS: HOME_BADGES_HERO_HEADING see-through badge text, the hero rail restart and wrapped row headings.");
    }

    // ----- Badges over artwork ------------------------------------------------------------------------------------------------

    private static async Task BadgesAsync(StackPanel parent, double width, List<string> differences)
    {
        var gallery = new StackPanel { Width = width, Padding = new Thickness(24), Spacing = 12, Background = new SolidColorBrush(ColorHelper.FromArgb(255, 17, 20, 28)) };
        var shades = new (string Label, Windows.UI.Color Color)[] { ("Dark artwork", ColorHelper.FromArgb(255, 20, 30, 70)), ("Bright artwork", ColorHelper.FromArgb(255, 235, 205, 120)) };
        var rows = new List<(string Preset, string Name, Border Badge)>();
        foreach (var (label, color) in shades)
        {
            gallery.Children.Add(new TextBlock { Text = label + ", Look > Fill: None", FontSize = 12, Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 160, 168, 184)) });
            var strip = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            var backdrop = new Border { Background = new SolidColorBrush(color), Padding = new Thickness(8), Child = strip };
            foreach (var preset in new[] { "vibrant", "classic", "pill", "minimal", "band", "plate" })
            {
                var style = new OverlayStyle { Background = "#00000000" };
                var cell = new StackPanel { Spacing = 6, Padding = new Thickness(10), Margin = new Thickness(0, 0, 8, 0) };
                var four = BuildBadge("4K", "resolution", preset, style);
                var rating = BuildBadge("IMDb 8.1", "rating_imdb", preset, style);
                cell.Children.Add(four); cell.Children.Add(rating);
                cell.Children.Add(new TextBlock { Text = preset, FontSize = 10, Foreground = new SolidColorBrush(Colors.White), Opacity = .8 });
                strip.Children.Add(cell);
                if (label == "Dark artwork") { rows.Add((preset, "4K", four)); rows.Add((preset, "rating", rating)); }
            }
            gallery.Children.Add(backdrop);
        }
        parent.Children.Add(gallery);
        try
        {
            gallery.UpdateLayout(); await Task.Delay(250); gallery.UpdateLayout();
            foreach (var (preset, name, badge) in rows)
            {
                var text = Descendants<TextBlock>(badge).First(block => block.Text.Length > 0 && block.Text.Any(char.IsLetterOrDigit));
                var colour = ((SolidColorBrush)text.Foreground).Color;
                var accentFilled = preset is "vibrant" or "classic" or "pill";
                var white = colour.R == 255 && colour.G == 255 && colour.B == 255;
                if (accentFilled && !white) differences.Add($"{width}: the {preset} '{name}' badge draws its text #{colour.R:x2}{colour.G:x2}{colour.B:x2} over artwork; it must be white.");
                if (badge.Shadow is null && preset is not "plate") differences.Add($"{width}: the {preset} '{name}' badge has no shadow lifting its text off the artwork.");
            }
            await MediaParityNativeFixture.CaptureAsync(gallery, $"badges-see-through-{width:0}.png");
        }
        finally { parent.Children.Remove(gallery); }
    }

    // ----- Row headings -------------------------------------------------------------------------------------------------------

    private static async Task HeadingsAsync(StackPanel parent, double width, List<string> differences)
    {
        var column = new StackPanel { Width = width, Spacing = 24, Background = new SolidColorBrush(ColorHelper.FromArgb(255, 17, 20, 28)) };
        var rows = new List<SectionRow>();
        foreach (var title in new[] { "Continue Watching", "Pneumonoultramicroscopicsilicovolcanoconiosis-Collection-Of-Recent-Documentaries", "Recently added in the family night picks you saved from last season" })
        {
            var row = new SectionRow
            {
                Width = width,
                Section = new() { Id = "heading-" + rows.Count, Title = title, SectionType = "continue_watching", LoadCompleted = true,
                    Items = [new() { ContentId = "heading-item-" + rows.Count, Title = "Fixture item", Type = "movie" }] },
                OnViewAll = () => { },
            };
            rows.Add(row); column.Children.Add(row);
        }
        parent.Children.Add(column);
        try
        {
            column.UpdateLayout(); await Task.Delay(350); column.UpdateLayout();
            foreach (var row in rows)
            {
                var title = (TextBlock)row.FindName("SectionTitle");
                var explore = (FrameworkElement)row.FindName("ExploreAllBtn");
                var header = (FrameworkElement)row.FindName("SectionHeader");
                var titleBounds = Bounds(title, column); var exploreBounds = Bounds(explore, column); var headerBounds = Bounds(header, column);
                if (explore.Visibility == Visibility.Visible && titleBounds.Right > exploreBounds.Left + .5)
                    differences.Add($"{width}: the heading '{Short(title.Text)}' runs to {titleBounds.Right:F0}, under Explore all at {exploreBounds.Left:F0}.");
                if (titleBounds.Right > headerBounds.Right + .5 || titleBounds.Left < headerBounds.Left - .5)
                    differences.Add($"{width}: the heading '{Short(title.Text)}' leaves its header ({titleBounds.Left:F0}..{titleBounds.Right:F0} of {headerBounds.Left:F0}..{headerBounds.Right:F0}).");
            }
            await MediaParityNativeFixture.CaptureAsync(column, $"row-headings-{width:0}.png");
        }
        finally { parent.Children.Remove(column); }
    }

    // ----- Hero progress rail --------------------------------------------------------------------------------------------------

    private static async Task HeroRailAsync(StackPanel parent, List<string> differences)
    {
        var hero = new HeroCarousel { Width = 1000 };
        var host = new Grid { Width = 1000, Height = 600 }; host.Children.Add(hero);
        parent.Children.Add(host);
        try
        {
            hero.ItemsSource = new List<MediaItem>
            {
                new() { ContentId = "rail-first", Type = "movie", Title = "First title", Overview = "A short synthetic overview.", Year = 2026 },
                new() { ContentId = "rail-second", Type = "movie", Title = "Second title" },
            };
            host.UpdateLayout(); await Task.Delay(250);
            var rail = (FrameworkElement)hero.FindName("ProgressRailFill");
            await Task.Delay(2200);
            var before = rail.Width;
            if (before < 10) { differences.Add($"the hero rail never filled while shown (width {before}); animations may be off on this machine."); return; }
            host.Children.Remove(hero); await Task.Delay(400);
            host.Children.Add(hero); host.UpdateLayout(); await Task.Delay(700);
            var after = rail.Width;
            if (after >= before - 4) differences.Add($"the hero rail must restart when the hero is shown again: {before:F1} wide before leaving, {after:F1} after 0.7 s back.");
            Program.Log($"Hero rail: {before:F1} before leaving, {after:F1} 0.7 s after coming back.");
        }
        finally { parent.Children.Remove(host); }
    }

    private static Border BuildBadge(string text, string id, string preset, OverlayStyle style)
        => (Border)typeof(SiloPlayer.App).Assembly.GetType("SiloPlayer.Controls.OverlayBadgeRenderer")!
            .GetMethod("Build", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [text, id, new SiloPlayer.Services.OverlayItemConfig(true, SiloPlayer.Services.OverlayPosition.TopLeft), preset, 1.4, style, null, null, null])!;

    private static string Short(string text) => text.Length > 28 ? text[..28] + "..." : text;

    private static Rect Bounds(FrameworkElement element, UIElement relative)
        => element.TransformToVisual(relative).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T own) yield return own;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }

}
