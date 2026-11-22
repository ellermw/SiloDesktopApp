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

    // The rows decided in Measure (child index range, width, height). Arrange uses these and never re-derives breaks from its own width, because
    // a different rounding of the final width could break a row the measured size had kept whole.
    private readonly List<(int Start, int End, double Width, double Height)> _rows = [];
    private int _measuredChildCount = -1;

    private void ComputeRows(double maxRowWidth)
    {
        _rows.Clear();
        double hSpacing = HorizontalSpacing;
        for (var rowStart = 0; rowStart < Children.Count;)
        {
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
                // One shared tolerance: rounded desired widths can sum to a fraction more than the rounded width they were sized from.
                if (visibleCount > 0 && rowWidth + withGap > maxRowWidth + LayoutTolerance.Fit)
                    break;
                rowWidth += withGap;
                rowHeight = Math.Max(rowHeight, desired.Height);
                visibleCount++;
                rowEnd++;
            }
            _rows.Add((rowStart, rowEnd, rowWidth, rowHeight));
            rowStart = rowEnd;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double vSpacing = VerticalSpacing;

        // If the parent gave us an unconstrained width (infinity), lay out
        // everything on a single row to avoid division-by-infinity nonsense.
        bool widthBounded = !double.IsInfinity(availableSize.Width);
        double maxRowWidth = widthBounded ? availableSize.Width : double.PositiveInfinity;

        // Measure each child with the available width (but unlimited height so
        // children can report their desired size).
        var childConstraint = new Size(maxRowWidth, double.PositiveInfinity);
        foreach (var child in Children)
            child.Measure(childConstraint);

        ComputeRows(maxRowWidth);
        _measuredChildCount = Children.Count;

        double totalHeight = 0;
        double widestRow = 0;
        foreach (var row in _rows)
        {
            widestRow = Math.Max(widestRow, row.Width);
            totalHeight += row.Height;
        }
        if (_rows.Count > 1) totalHeight += (_rows.Count - 1) * vSpacing;

        double finalWidth = widthBounded ? Math.Min(widestRow, maxRowWidth) : widestRow;
        return new Size(finalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double hSpacing = HorizontalSpacing;
        double vSpacing = VerticalSpacing;

        // The children were measured in MeasureOverride, which also decided the rows. Only when the children changed without a new measure
        // (which the framework does not do) are the rows worked out again here.
        if (_measuredChildCount != Children.Count) ComputeRows(finalSize.Width);

        double y = 0;
        foreach (var (rowStart, rowEnd, _, rowHeight) in _rows)
        {
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
        }

        return finalSize;
    }
}
