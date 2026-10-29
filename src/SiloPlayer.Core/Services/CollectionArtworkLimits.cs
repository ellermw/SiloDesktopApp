namespace SiloPlayer.Core.Services;

public static class CollectionArtworkLimits
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public const string OversizeMessage = "Image must be 10 MB or smaller.";
}

/// <summary>Mirrors the server's profile avatar upload limit (profileAvatarMaxFileSize).</summary>
public static class ProfileAvatarLimits
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public const string OversizeMessage = "Avatar image must be 10 MB or smaller.";
}
