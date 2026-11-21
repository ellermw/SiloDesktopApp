namespace SiloPlayer.Helpers;

/// <summary>
/// Where Playing Next puts the finishing picture and the column beside it for a window width.
/// Wide: the picture is 44% of the window (at most 832) and the gap between it and the column
/// sits on the window's centre line, so the pair stays centred at any aspect ratio. Until the
/// window is wide enough for that, the picture keeps the edge margin instead.
/// </summary>
internal readonly record struct PlayingNextLayout(bool Wide, double SlotWidth, double LeftMargin, double ColumnWidth)
{
    internal const double WideBreakpoint = 1024;
    internal const double MaxSlotWidth = 832;
    internal const double MaxColumnWidth = 544;
    internal const double EdgeMargin = 48;
    internal const double Gutter = 48;

    internal static PlayingNextLayout For(double windowWidth)
    {
        if (windowWidth < WideBreakpoint)
            return new(false, Math.Min(320, windowWidth - 48), 24, Column(windowWidth - 48));
        var slot = Math.Min(windowWidth * .44, MaxSlotWidth);
        var left = Math.Max(EdgeMargin, windowWidth / 2 - Gutter / 2 - slot);
        return new(true, slot, left, Column(windowWidth - left - slot - Gutter - EdgeMargin));
    }

    private static double Column(double available) => Math.Min(MaxColumnWidth, Math.Max(240, available));
}
