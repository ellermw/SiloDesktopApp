namespace SiloPlayer.Core.Services;

/// <summary>
/// Labels and fixed palettes for server-supplied upcoming-event badges, mirroring the WebUI's
/// <c>upcomingBadgeLabel</c> and <c>upcomingBadgeClass</c> (<c>web/src/lib/upcomingEventPresentation.ts</c>).
/// Clients draw the labels in capitals.
/// </summary>
public static class UpcomingBadgePresentation
{
    /// <summary>Colors as 0xAARRGGBB: the pill background, its outline and its text.</summary>
    public readonly record struct Palette(uint Background, uint Outline, uint Foreground);

    public static string Label(string badge) => badge switch
    {
        "series_premiere" => "Premiere",
        "season_premiere" => "New Season",
        "finale" => "Finale",
        "digital" => "Digital",
        "physical" => "Physical",
        _ => badge,
    };

    /// <summary>
    /// The dark pill a badge draws on any theme (<c>bg-*-950/90</c>, <c>border-*-800/55</c>,
    /// <c>text-*-50</c>), or null for badges that use the accent or neutral style.
    /// </summary>
    public static Palette? FixedPalette(string badge) => badge switch
    {
        "finale" => new(0xE6431407, 0x8C9A3412, 0xFFFFF7ED),
        "digital" => new(0xE6082F49, 0x8C075985, 0xFFF0F9FF),
        "physical" => new(0xE6022C22, 0x8C065F46, 0xFFECFDF5),
        _ => null,
    };
}
