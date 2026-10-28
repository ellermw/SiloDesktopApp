namespace SiloPlayer.Core.Services;

/// <summary>
/// Identifies vector artwork, which WinUI's BitmapImage cannot decode: the
/// server's bundled profile-avatar presets and DiceBear-generated avatars.
/// </summary>
public static class SvgArtwork
{
    public static bool IsSvgUrl(Uri uri) =>
        uri.IsAbsoluteUri &&
        (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
         (string.Equals(uri.Host, "api.dicebear.com", StringComparison.OrdinalIgnoreCase) &&
          uri.AbsolutePath.EndsWith("/svg", StringComparison.OrdinalIgnoreCase)));
}
