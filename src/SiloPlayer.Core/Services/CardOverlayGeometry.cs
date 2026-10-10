namespace SiloPlayer.Core.Services;

/// <summary>
/// Scales poster overlay geometry from the current WebUI's 185px visual
/// reference. Wide cards deliberately retain fixed geometry.
/// </summary>
public readonly record struct CardOverlayGeometry(
    double Scale,
    double EdgeInset,
    double StackGap)
{
    public const double PosterReferenceWidth = 185;

    public static CardOverlayGeometry ForPoster(double cardWidth, string preset = "classic")
    {
        var width = double.IsFinite(cardWidth) && cardWidth > 0
            ? cardWidth
            : PosterReferenceWidth;
        var scale = width / PosterReferenceWidth;
        return new CardOverlayGeometry(scale, 8 * scale, (preset is "minimal" or "square" ? 2 : 4) * scale);
    }

    public static CardOverlayGeometry ForWideCard()
        => new(1, 8, 4);
}
