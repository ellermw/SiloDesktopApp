using Windows.Foundation;

namespace SiloPlayer.Controls;

/// <summary>
/// Two children on one line that share its width like the official restore rows (#4978): each takes at most its own text width, and the
/// line is divided <see cref="FirstWeight"/>:<see cref="SecondWeight"/> from a zero basis, so whichever needs less gives the rest to the
/// other. Only when both are longer than their shares does each stop at its share. Text blocks with <c>NoWrap</c> and an ellipsis then trim
/// inside the width they are given.
/// </summary>
public sealed class ShareRowPanel : Panel
{
    public double FirstWeight { get; set; } = 3;
    public double SecondWeight { get; set; } = 2;
    public double Spacing { get; set; } = 8;

    /// <summary>The widths of the two children for the room they would like and the room there is (pure, no layout).</summary>
    public static (double First, double Second) Divide(double firstNatural, double secondNatural, double available, double firstWeight, double secondWeight, double spacing)
    {
        var room = Math.Max(0, available - spacing);
        if (double.IsInfinity(room) || firstNatural + secondNatural <= room) return (firstNatural, secondNatural);
        var firstShare = room * firstWeight / (firstWeight + secondWeight);
        var secondShare = room - firstShare;
        if (firstNatural <= firstShare) return (firstNatural, Math.Min(secondNatural, room - firstNatural));
        if (secondNatural <= secondShare) return (Math.Min(firstNatural, room - secondNatural), secondNatural);
        return (firstShare, secondShare);
    }

    private double _first, _second;

    protected override Size MeasureOverride(Size availableSize)
    {
        _first = _second = 0;
        if (Children.Count == 0) return new Size(0, 0);
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (var child in Children) child.Measure(infinite); // natural (text) widths
        if (Children.Count < 2) return Children[0].DesiredSize;
        (_first, _second) = Divide(Children[0].DesiredSize.Width, Children[1].DesiredSize.Width, availableSize.Width, FirstWeight, SecondWeight, Spacing);
        Children[0].Measure(new Size(_first, availableSize.Height));
        Children[1].Measure(new Size(_second, availableSize.Height));
        var height = Math.Max(Children[0].DesiredSize.Height, Children[1].DesiredSize.Height);
        return new Size(Children[0].DesiredSize.Width + Spacing + Children[1].DesiredSize.Width, height);
    }

    // Arranging never measures again: a measure inside an arrange pass can change the desired size and make the layout cycle.
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0) return finalSize;
        if (Children.Count < 2) { Children[0].Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height)); return finalSize; }
        var first = _first; var second = _second;
        var room = Math.Max(0, finalSize.Width - Spacing);
        if (first + second > room && first + second > 0) { var scale = room / (first + second); first *= scale; second *= scale; }
        var firstHeight = Children[0].DesiredSize.Height; var secondHeight = Children[1].DesiredSize.Height;
        var height = Math.Max(firstHeight, secondHeight);
        // The official line aligns the baselines; text of different sizes ends on nearly the same line when bottoms are aligned.
        Children[0].Arrange(new Rect(0, height - firstHeight, first, firstHeight));
        Children[1].Arrange(new Rect(first + Spacing, height - secondHeight, second, secondHeight));
        return finalSize;
    }
}
