using Windows.Foundation;

namespace SiloPlayer.Controls;

/// <summary>
/// A single left-to-right line that behaves like a CSS flex row of <c>min-w-0 truncate</c> and <c>shrink-0</c> items (Admin Activity's viewer and
/// client lines, e130f262d AdminActivity.tsx): children keep their natural width unless the line is too narrow, in which case only the
/// children registered as <see cref="Shrinkers"/> give up width, in proportion to their natural width, and truncate. Plain StackPanels and Grids
/// cannot express this: they either overflow or stretch the trailing item to the far edge.
/// </summary>
internal sealed class AdminShrinkPanel : Panel
{
    private readonly HashSet<UIElement> _shrinkers = [];

    public double Spacing { get; set; }

    public AdminShrinkPanel()
    {
        // Fixed children that alone exceed the width must not paint past the panel edge (CSS overflow is clipped by the card). The clip follows
        // the arranged size and is set after layout, never inside it.
        SizeChanged += (_, e) => Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
    }

    /// <summary>Adds a child that may shrink below its natural width (the official <c>min-w-0 truncate</c>).</summary>
    public void AddShrinking(UIElement child) { _shrinkers.Add(child); Children.Add(child); }

    protected override Size MeasureOverride(Size available)
    {
        // A removed child's registration goes with it (a re-added one is a fixed child again unless it is added through AddShrinking).
        if (_shrinkers.Count > 0) _shrinkers.RemoveWhere(child => !Children.Contains(child));
        var width = double.IsFinite(available.Width) ? available.Width : double.PositiveInfinity;
        var visible = Children.Where(child => child.Visibility != Visibility.Collapsed).ToArray();
        foreach (var child in visible) child.Measure(new Size(double.PositiveInfinity, available.Height));
        var gaps = Math.Max(0, visible.Length - 1) * Spacing;
        var fixedWidth = visible.Where(child => !_shrinkers.Contains(child)).Sum(child => child.DesiredSize.Width);
        var wanted = visible.Where(_shrinkers.Contains).Sum(child => child.DesiredSize.Width);
        var room = double.IsInfinity(width) ? wanted : Math.Max(0, width - fixedWidth - gaps);
        var scale = wanted > room && wanted > 0 ? room / wanted : 1;
        double used = gaps, height = 0;
        foreach (var child in visible)
        {
            if (_shrinkers.Contains(child) && scale < 1) child.Measure(new Size(Math.Floor(child.DesiredSize.Width * scale), available.Height));
            used += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(width) ? used : Math.Min(width, used), height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double left = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var w = child.DesiredSize.Width;
            child.Arrange(new Rect(left, Math.Max(0, (final.Height - child.DesiredSize.Height) / 2), w, child.DesiredSize.Height));
            left += w + Spacing;
        }
        return final;
    }
}
