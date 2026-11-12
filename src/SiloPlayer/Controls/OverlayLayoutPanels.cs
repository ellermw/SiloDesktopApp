using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SiloPlayer.Controls;

internal static class OverlayBadgeMeasure
{
    internal static void Limit(FrameworkElement element, double width)
    {
        width = Math.Max(0, width);
        element.MaxWidth = width;
        if (element is StackPanel unit)
        {
            var badge = unit.Children.OfType<Border>().LastOrDefault(child => child.Tag is string);
            if (badge is not null) Limit(badge, Math.Max(0, width - unit.Spacing - 1));
        }
        if (element is not Border { Child: StackPanel content } border) return;
        var text = content.Children.OfType<TextBlock>().LastOrDefault();
        if (text is null) return;
        var others = content.Children.OfType<FrameworkElement>().Where(child => child != text).ToArray();
        if (text.TextTrimming == TextTrimming.None)
        {
            text.MaxWidth = double.PositiveInfinity;
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var room = Math.Max(0, width - text.DesiredSize.Width - others.Length * content.Spacing
                - border.Padding.Left - border.Padding.Right - border.BorderThickness.Left - border.BorderThickness.Right);
            foreach (var other in others)
            {
                other.MaxWidth = room;
                if (other is Border { Child: TextBlock source } chip)
                {
                    source.MaxWidth = Math.Max(0, room - chip.Padding.Left - chip.Padding.Right);
                    source.TextTrimming = TextTrimming.CharacterEllipsis;
                }
            }
            return;
        }
        foreach (var other in others) other.Measure(new Size(width, double.PositiveInfinity));
        var used = others.Sum(child => child.DesiredSize.Width) + others.Length * content.Spacing
            + border.Padding.Left + border.Padding.Right + border.BorderThickness.Left + border.BorderThickness.Right;
        text.MaxWidth = Math.Max(0, width - used);
    }
}

internal sealed class OverlayBottomRows : Panel
{
    internal double Gap { get; set; } = 8;
    internal double StackGap { get; set; } = 4;
    internal int LeftCount { get; set; }
    private readonly List<(FrameworkElement Element, Rect Bounds)> _arranged = [];
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 10000;
        _arranged.Clear();
        var left = Children.Take(LeftCount).Cast<FrameworkElement>().ToArray();
        var right = Children.Skip(LeftCount).Cast<FrameworkElement>().ToArray();
        var levels = Math.Max(left.Length, right.Length);
        var top = 0d;
        for (var level = 0; level < levels; level++)
        {
            var startIndex = left.Length - levels + level;
            var endIndex = right.Length - levels + level;
            var start = startIndex >= 0 ? left[startIndex] : null;
            var end = endIndex >= 0 ? right[endIndex] : null;
            foreach (var child in new[] { start, end }.OfType<FrameworkElement>())
            {
                OverlayBadgeMeasure.Limit(child, width);
                child.Measure(new Size(width, double.PositiveInfinity));
            }
            var startSize = start?.DesiredSize ?? new Size();
            var endSize = end?.DesiredSize ?? new Size();
            var wraps = start is not null && end is not null && startSize.Width + endSize.Width + Gap > width + .5;
            var height = wraps ? startSize.Height + endSize.Height + StackGap : Math.Max(startSize.Height, endSize.Height);
            if (start is not null) _arranged.Add((start, new(0, top + (wraps ? 0 : height - startSize.Height), Math.Min(width, startSize.Width), startSize.Height)));
            if (end is not null) _arranged.Add((end, new(Math.Max(0, width - endSize.Width), top + height - endSize.Height, Math.Min(width, endSize.Width), endSize.Height)));
            top += height + (level + 1 < levels ? StackGap : 0);
        }
        return new(width, top);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var entry in _arranged) entry.Element.Arrange(entry.Bounds);
        return finalSize;
    }
}

internal sealed class OverlayBandMarks : Panel
{
    internal double Gap { get; set; } = 7;
    private int _visible;
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 10000;
        var used = 0d;
        var height = 0d;
        _visible = 0;
        foreach (var child in Children.Cast<FrameworkElement>())
        {
            OverlayBadgeMeasure.Limit(child, width);
            child.Measure(new Size(width, double.PositiveInfinity));
            var proposed = used + (_visible > 0 ? Gap : 0) + child.DesiredSize.Width;
            if (_visible > 0 && proposed > width + .5) break;
            used = Math.Min(width, proposed); height = Math.Max(height, child.DesiredSize.Height); _visible++;
        }
        for (var index = 0; index < Children.Count; index++)
        {
            var child = (FrameworkElement)Children[index];
            child.Opacity = index < _visible ? 1 : 0;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(child, index < _visible
                ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        }
        return new(used, height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var left = 0d;
        for (var index = _visible; index < Children.Count; index++) Children[index].Arrange(new Rect());
        for (var index = 0; index < _visible; index++)
        {
            var child = (FrameworkElement)Children[index];
            if (index > 0) left += Gap;
            var width = Math.Min(child.DesiredSize.Width, Math.Max(0, finalSize.Width - left));
            child.Arrange(new Rect(left, (finalSize.Height - child.DesiredSize.Height) / 2, width, child.DesiredSize.Height));
            left += width;
        }
        return finalSize;
    }
}

