namespace SiloPlayer.Core.Services;

public static class LibraryPosterSizing
{
    public static int GetColumnCount(double availableWidth, double targetWidth)
    {
        var target = double.IsFinite(targetWidth) ? Math.Clamp(targetWidth, 100, 300) : 180;
        var width = double.IsFinite(availableWidth) ? Math.Max(0, availableWidth) : 0;
        return Math.Max(1, (int)Math.Floor((width + 12) / (target + 12)));
    }
}
