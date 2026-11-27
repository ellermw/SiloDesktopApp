using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SiloPlayer.Controls;

/// <summary>
/// What each Home row's cards actually show: realized cards, cards with an artwork source, sources XAML
/// has decoded, artwork revealed over the placeholder, and placeholders still showing. Read from the
/// live card state, independent of how a capture renders it.
/// </summary>
internal static class ScrollArtworkCensus
{
    internal sealed record RowCensus(string Row, int Cards, int WithSource, int Decoded, int Revealed, int Placeholder);

    internal static List<RowCensus> Take(FrameworkElement root) => Take(Rows(root));

    /// <summary>The page's section rows, found once so polling does not walk the whole page each time.</summary>
    internal static List<SectionRow> Rows(FrameworkElement root) => Descendants(root).OfType<SectionRow>().ToList();

    /// <summary>Realized cards per row, read from each row's repeater only.</summary>
    internal static int[] RealizedCards(IEnumerable<SectionRow> rows) => rows.Select(row =>
    {
        var repeater = (ItemsRepeater)row.FindName("CardsRepeater");
        var realized = 0;
        for (var i = VisualTreeHelper.GetChildrenCount(repeater) - 1; i >= 0; i--)
            if (VisualTreeHelper.GetChild(repeater, i) is UIElement child && repeater.GetElementIndex(child) >= 0) realized++;
        return realized;
    }).ToArray();

    internal static List<RowCensus> Take(IEnumerable<SectionRow> sectionRows)
    {
        var rows = new List<RowCensus>();
        foreach (var row in sectionRows)
        {
            int cards = 0, source = 0, decoded = 0, revealed = 0, placeholder = 0;
            foreach (var card in Descendants(row).OfType<UserControl>().Where(c => c is PosterCard or LandscapeCard))
            {
                var (imageName, wrapperName) = card is PosterCard ? ("PosterImage", "PosterReveal") : ("BackdropImage", "BackdropReveal");
                if (card.FindName(imageName) is not Image image || card.FindName(wrapperName) is not UIElement wrapper) continue;
                cards++;
                if (image.Source is not null) source++;
                if (image.Source is BitmapImage { PixelWidth: > 0 }) decoded++;
                if (image.Source is not null && image.Opacity >= 1 && wrapper.Opacity >= 1) revealed++;
                if (card.FindName("ArtworkFallback") is UIElement { Visibility: Visibility.Visible }) placeholder++;
            }
            rows.Add(new(row.Section?.Id ?? "?", cards, source, decoded, revealed, placeholder));
        }
        return rows;
    }

    internal static string Format(IEnumerable<RowCensus> rows) =>
        string.Join(" ", rows.Select(r => $"{r.Row}:{r.Cards}c/{r.WithSource}src/{r.Decoded}dec/{r.Revealed}shown/{r.Placeholder}ph"));

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(node, i));
        }
    }
}
