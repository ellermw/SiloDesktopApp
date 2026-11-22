namespace SiloPlayer.Controls;

/// <summary>
/// One shared allowance for fit and wrap decisions in custom panels. Desired sizes are layout-rounded to the device pixel grid, so on a scaled
/// display (125%, 150%) a gap, border or padding that is not on the grid can make a panel's rounded width a fraction smaller than what its
/// children need. A panel therefore treats content that exceeds the room by no more than this as fitting, and never measures a child a second
/// time at exactly its own desired width (a fraction less than it needs makes text trim and nested panels wrap).
/// </summary>
internal static class LayoutTolerance
{
    /// <summary>Device-independent pixels by which content may exceed the room and still count as fitting.</summary>
    internal const double Fit = 1;

    /// <summary>
    /// Extra width a table column gets beyond its widest cell. None where the display scale is a whole number: sizes are whole pixels there,
    /// nothing is lost to rounding, and the extra would only push a table that fits exactly into scrolling sideways. One DIP otherwise.
    /// </summary>
    internal static double ColumnRoom(double rasterizationScale) =>
        AssumeFractionalScale || Math.Abs(rasterizationScale - Math.Round(rasterizationScale)) > 0.001 ? Fit : 0;

    /// <summary>Test seam: the native checks run on a display at 100% and stand in for a scaled one with fractional sizes.</summary>
    internal static bool AssumeFractionalScale { get; set; }
}
