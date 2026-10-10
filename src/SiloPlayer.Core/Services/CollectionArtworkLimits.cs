namespace SiloPlayer.Core.Services;

public static class CollectionArtworkLimits
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public const string OversizeMessage = "Image must be 10 MB or smaller.";
}
