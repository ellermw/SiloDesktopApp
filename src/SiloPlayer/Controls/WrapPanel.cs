using Windows.Foundation;

namespace SiloPlayer.Controls;

/// <summary>
/// A lightweight horizontal wrap panel for WinUI 3 (Desktop App SDK has no
/// built-in equivalent). Lays children out left-to-right in rows, wrapping to
/// the next row when the current row would exceed the available width.
/// Horizontal and vertical spacing between items / rows is configurable.
/// </summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(
            nameof(HorizontalSpacing),
            typeof(double),
            typeof(WrapPanel),
            new PropertyMetadata(6.0, OnSpacingChanged));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(
            nameof(VerticalSpacing),
            typeof(double),
            typeof(WrapPanel),
            new PropertyMetadata(6.0, OnSpacingChanged));

    /// <summary>Horizontal gap between items on the same row (default 6).</summary>
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>Vertical gap between rows (default 6).</summary>
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WrapPanel panel)
        {
            panel.InvalidateMeasure();
            panel.InvalidateArrange();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double hSpacing = HorizontalSpacing;
        double vSpacing = VerticalSpacing;

        // If the parent gave us an unconstrained width (infinity), lay out
        // everything on a single row to avoid division-by-infinity nonsense.
        bool widthBounded = !double.IsInfinity(availableSize.Width);
        double maxRowWidth = widthBounded ? availableSize.Width : double.PositiveInfinity;

        // Measure each child with the available width (but unlimited height so
        // children can report their desired size).
        var childConstraint = new Size(maxRowWidth, double.PositiveInfinity);

        double currentRowWidth = 0;
        double currentRowHeight = 0;
        double totalHeight = 0;
        double widestRow = 0;
        bool firstOnRow = true;

        foreach (var child in Children)
        {
            child.Measure(childConstraint);
            if (child.Visibility == Visibility.Collapsed)
                continue;
            var desired = child.DesiredSize;

            double withGap = firstOnRow ? desired.Width : desired.Width + hSpacing;

            if (widthBounded && !firstOnRow && currentRowWidth + withGap > maxRowWidth)
            {
                // Row break.
                widestRow = Math.Max(widestRow, currentRowWidth);
                totalHeight += currentRowHeight + vSpacing;
                currentRowWidth = desired.Width;
                currentRowHeight = desired.Height;
                firstOnRow = false;
                continue;
            }

            currentRowWidth += withGap;
            currentRowHeight = Math.Max(currentRowHeight, desired.Height);
            firstOnRow = false;
        }

        widestRow = Math.Max(widestRow, currentRowWidth);
        totalHeight += currentRowHeight;

        double finalWidth = widthBounded ? Math.Min(widestRow, maxRowWidth) : widestRow;
        return new Size(finalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double hSpacing = HorizontalSpacing;
        double vSpacing = VerticalSpacing;
        double maxRowWidth = finalSize.Width;

        double y = 0;
        for (var rowStart = 0; rowStart < Children.Count;)
        {
            // Measure the complete row before arranging it. The available row
            // height lets each child's native VerticalAlignment take effect.
            var rowEnd = rowStart;
            double rowWidth = 0;
            double rowHeight = 0;
            var visibleCount = 0;
            while (rowEnd < Children.Count)
            {
                var child = Children[rowEnd];
                if (child.Visibility == Visibility.Collapsed)
                {
                    rowEnd++;
                    continue;
                }
                var desired = child.DesiredSize;
                var withGap = desired.Width + (visibleCount == 0 ? 0 : hSpacing);
                if (visibleCount > 0 && rowWidth + withGap > maxRowWidth)
                    break;
                rowWidth += withGap;
                rowHeight = Math.Max(rowHeight, desired.Height);
                visibleCount++;
                rowEnd++;
            }
            double x = 0;
            var firstOnRow = true;
            for (var index = rowStart; index < rowEnd; index++)
            {
                var child = Children[index];
                if (child.Visibility == Visibility.Collapsed)
                {
                    child.Arrange(new Rect(0, 0, 0, 0));
                    continue;
                }
                if (!firstOnRow) x += hSpacing;
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, rowHeight));
                x += child.DesiredSize.Width;
                firstOnRow = false;
            }
            y += rowHeight + vSpacing;
            rowStart = rowEnd;
        }

        return finalSize;
    }
}
