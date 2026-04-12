using Windows.Foundation;

namespace ContinuumPlayer.Controls;

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

        double x = 0;
        double y = 0;
        double currentRowHeight = 0;
        bool firstOnRow = true;

        foreach (var child in Children)
        {
            var desired = child.DesiredSize;
            double withGap = firstOnRow ? desired.Width : desired.Width + hSpacing;

            if (!firstOnRow && x + withGap > maxRowWidth)
            {
                // Wrap to next row.
                y += currentRowHeight + vSpacing;
                x = 0;
                currentRowHeight = 0;
                firstOnRow = true;
                withGap = desired.Width;
            }

            if (!firstOnRow) x += hSpacing;

            child.Arrange(new Rect(x, y, desired.Width, desired.Height));

            x += desired.Width;
            currentRowHeight = Math.Max(currentRowHeight, desired.Height);
            firstOnRow = false;
        }

        return finalSize;
    }
}
