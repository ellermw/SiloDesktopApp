namespace SiloPlayer.Core.Services;

public readonly record struct PosterActionGeometry(
    double TriggerSize,
    double IconSize,
    double EdgeInset,
    double Gap);

public static class PosterActionGeometryFactory
{
    private const double SmallViewportBreakpoint = 640;

    public static PosterActionGeometry Create(string? source, string? posterSize, double viewportWidth)
    {
        var personalGrid = source is "favorites" or "watchlist";
        var smallViewport = double.IsFinite(viewportWidth) && viewportWidth < SmallViewportBreakpoint;

        if (personalGrid || smallViewport)
            return new PosterActionGeometry(24, 12, 6, 2);

        return string.Equals(posterSize, "compact", StringComparison.OrdinalIgnoreCase)
            ? new PosterActionGeometry(28, 14, 8, 4)
            : new PosterActionGeometry(32, 16, 10, 6);
    }
}
