using Windows.Foundation;

namespace SiloPlayer.Controls;

/// <summary>Centers each wrapped row, including the final partial profile row.</summary>
public sealed class CenteredWrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 20;
    public double VerticalSpacing { get; set; } = 20;

    protected override Size MeasureOverride(Size availableSize)
    {
        var constraint = new Size(availableSize.Width, double.PositiveInfinity);
        foreach (var child in Children) child.Measure(constraint);
        var rows = Rows(availableSize.Width);
        return new Size(rows.Count == 0 ? 0 : rows.Max(row => row.Width),
            rows.Sum(row => row.Height) + Math.Max(0, rows.Count - 1) * VerticalSpacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0d;
        foreach (var row in Rows(finalSize.Width))
        {
            var x = (finalSize.Width - row.Width) / 2;
            foreach (var child in row.Items)
            {
                child.Arrange(new Rect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
                x += child.DesiredSize.Width + HorizontalSpacing;
            }
            y += row.Height + VerticalSpacing;
        }
        return finalSize;
    }

    private List<Row> Rows(double width)
    {
        var rows = new List<Row>(); var current = new Row();
        foreach (var child in Children.Where(child => child.Visibility != Visibility.Collapsed))
        {
            var desired = child.DesiredSize;
            var needed = desired.Width + (current.Items.Count == 0 ? 0 : HorizontalSpacing);
            if (current.Items.Count > 0 && current.Width + needed > width)
            { rows.Add(current); current = new Row(); needed = desired.Width; }
            current.Items.Add(child); current.Width += needed; current.Height = Math.Max(current.Height, desired.Height);
        }
        if (current.Items.Count > 0) rows.Add(current);
        return rows;
    }
    private sealed class Row
    {
        internal readonly List<UIElement> Items = [];
        internal double Width, Height;
    }
}
