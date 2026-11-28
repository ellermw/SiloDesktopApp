using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SiloPlayer.Controls.Admin;

/// <summary>Content-sized flex rows. Arranged children never feed their geometry back into measurement.</summary>
internal sealed class DashboardWrapPanel : Panel
{
    internal double Spacing { get; init; } = 8;
    internal double RowSpacing { get; init; } = 8;
    internal bool JustifyEnd { get; init; }
    private readonly List<(UIElement Child, Rect Bounds)> _placements = [];
    private readonly List<(int Start, int Count, double Width)> _rows = [];

    protected override Size MeasureOverride(Size available)
    {
        _placements.Clear(); _rows.Clear();
        var width = available.Width;
        double left = 0, top = 0, rowHeight = 0, occupied = 0;
        var rowStart = 0;
        void FinishRow()
        {
            if (_placements.Count == rowStart) return;
            _rows.Add((rowStart, _placements.Count - rowStart, left));
            occupied = Math.Max(occupied, left);
            top += rowHeight + RowSpacing; left = 0; rowHeight = 0; rowStart = _placements.Count;
        }
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new(width, double.PositiveInfinity));
            var size = child.DesiredSize;
            if (_placements.Count > rowStart && left + Spacing + size.Width > width + LayoutTolerance.Fit) FinishRow();
            if (_placements.Count > rowStart) left += Spacing;
            _placements.Add((child, new(left, top, size.Width, size.Height)));
            left += size.Width; rowHeight = Math.Max(rowHeight, size.Height);
        }
        FinishRow();
        return new(occupied, Math.Max(0, top - RowSpacing));
    }
    protected override Size ArrangeOverride(Size final)
    {
        foreach (var (start, count, width) in _rows)
        {
            var height = _placements.Skip(start).Take(count).Max(item => item.Bounds.Height);
            var offset = JustifyEnd ? Math.Max(0, final.Width - width) : 0;
            for (var i = start; i < start + count; i++)
            {
                var (child, bounds) = _placements[i];
                child.Arrange(new(bounds.X + offset, bounds.Y + (height - bounds.Height) / 2, bounds.Width, bounds.Height));
            }
        }
        return final;
    }
}

/// <summary>A widget title wraps beside controls until its longest word cannot fit; controls then wrap below.</summary>
internal sealed class DashboardWidgetHeader : Panel
{
    private readonly TextBlock _title;
    private readonly DashboardWrapPanel _actions;
    private bool _wrapped;
    private double _titleWidth;
    internal DashboardWidgetHeader(TextBlock title, DashboardWrapPanel actions)
    {
        _title = title; _actions = actions; Children.Add(title); Children.Add(actions);
    }
    protected override Size MeasureOverride(Size available)
    {
        // Container queries: a gated control shows only once the header itself is wide enough.
        if (double.IsFinite(available.Width))
            foreach (var gate in _actions.Children.OfType<DashboardHeaderGate>())
            {
                var visibility = available.Width + LayoutTolerance.Fit >= gate.MinContainerWidth ? Visibility.Visible : Visibility.Collapsed;
                if (gate.Visibility != visibility) gate.Visibility = visibility;
            }
        _actions.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var actionWidth = _actions.DesiredSize.Width;
        var probe = new TextBlock { FontFamily = _title.FontFamily, FontSize = _title.FontSize, FontWeight = _title.FontWeight, CharacterSpacing = _title.CharacterSpacing };
        double longest = 0;
        foreach (var word in _title.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            probe.Text = word; probe.Measure(new(double.PositiveInfinity, double.PositiveInfinity)); longest = Math.Max(longest, probe.DesiredSize.Width);
        }
        var width = double.IsFinite(available.Width) ? available.Width : longest + 8 + actionWidth;
        _wrapped = actionWidth > 0 && longest + 8 + actionWidth > width + LayoutTolerance.Fit;
        // Beside the actions the title keeps at least its longest word (the fit above allows the tolerance), so a word never breaks mid-way.
        _titleWidth = _wrapped || actionWidth == 0 ? width : Math.Max(Math.Min(width, longest), Math.Max(0, width - 8 - actionWidth));
        _title.Measure(new(_titleWidth, double.PositiveInfinity));
        _actions.Measure(new(width, double.PositiveInfinity));
        return new(width, _wrapped ? _title.DesiredSize.Height + 8 + _actions.DesiredSize.Height : Math.Max(_title.DesiredSize.Height, _actions.DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size final)
    {
        var titleHeight = _title.DesiredSize.Height; var actionsHeight = _actions.DesiredSize.Height;
        _title.Arrange(new(0, _wrapped ? 0 : Math.Max(0, (final.Height - titleHeight) / 2), _titleWidth, titleHeight));
        var actionsWidth = Math.Min(final.Width, _actions.DesiredSize.Width);
        _actions.Arrange(new(final.Width - actionsWidth, _wrapped ? titleHeight + 8 : Math.Max(0, (final.Height - actionsHeight) / 2), actionsWidth, actionsHeight));
        return final;
    }
}

/// <summary>
/// A header control that shows only while the header is at least <see cref="MinContainerWidth"/> wide: the WebUI's
/// <c>hidden @lg/card-header:inline</c> container query on the "Insights ›" links. The header measures it, not the window.
/// </summary>
internal sealed class DashboardHeaderGate : ContentControl
{
    internal DashboardHeaderGate(double minContainerWidth, UIElement content)
    {
        MinContainerWidth = minContainerWidth;
        Content = content;
        IsTabStop = false;
        Padding = new Thickness(0);
        MinWidth = 0;
        MinHeight = 0;
    }

    internal double MinContainerWidth { get; }
}
