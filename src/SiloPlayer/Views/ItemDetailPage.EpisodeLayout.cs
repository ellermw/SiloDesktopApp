using Windows.Foundation;

namespace SiloPlayer.Views;

public sealed partial class ItemDetailPage
{
    private bool _episodesHorizontal;

    private double SiblingEpisodeCardWidth() => ActualWidth < 1024 ? 160
        : ActualHeight <= 450 && ActualWidth > ActualHeight ? 140
        : ActualHeight <= 650 ? 180 : 240;

    private (bool Horizontal, int Columns) EpisodeGridLayout()
    {
        var width = XamlRoot?.Content is FrameworkElement root && root.ActualWidth > 0
            ? root.ActualWidth : ActualWidth > 0 ? ActualWidth : EpisodesPanel.ActualWidth;
        var horizontal = _tvViewport != null && width < 1024;
        var shortLandscape = _tvViewport != null && width >= 1024 && ActualHeight <= 650 && width > ActualHeight;
        return (horizontal, horizontal ? ViewModel.Episodes.Count : shortLandscape ? 2 : GetEpisodeGridColumnCount(width));
    }

    private void EnsureEpisodeGridLayout()
    {
        if (ViewModel.Episodes.Count == 0) return;
        var (horizontal, columns) = EpisodeGridLayout();
        if (_episodesHorizontal != horizontal || EpisodesPanel.ColumnDefinitions.Count != columns)
            LayoutEpisodeGrid();
    }

    private void UpdateEpisodeRowCap()
    {
        // Like useGridRowCap(4), measure the real rows so long captions and
        // responsive columns cannot turn a one-row season into a clipped rail.
        var cap = _episodesHorizontal || EpisodesPanel.RowDefinitions.Count <= 4
            ? double.PositiveInfinity
            : EpisodesPanel.RowDefinitions.Take(4).Sum(row => row.ActualHeight) - 8;
        if (cap <= 0) return; // Wait for the loaded grid's first real measure.
        if (EpisodesScroll.MaxHeight != cap) EpisodesScroll.MaxHeight = cap;
    }

    private sealed class EpisodeStillAspectPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            // Grid columns supply a finite width. During an unconstrained
            // probe, keep a finite provisional measure instead of infinity.
            var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : 160;
            var size = new Size(width, width * 9 / 16);
            foreach (var child in Children) child.Measure(size);
            return size;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var rect = new Rect(0, 0, finalSize.Width, finalSize.Width * 9 / 16);
            foreach (var child in Children) child.Arrange(rect);
            return finalSize;
        }
    }
}
