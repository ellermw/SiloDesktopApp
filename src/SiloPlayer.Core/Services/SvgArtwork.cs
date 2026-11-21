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

    /// <summary>
    /// Identifies an SVG document by the start of its body. The image cache
    /// stores every body as .img and branding URLs may have no extension, so a
    /// cached file's URL cannot select its decoder. No raster format starts
    /// with markup; an XML declaration, doctype or comment may precede the root.
    /// </summary>
    public static bool IsSvgContent(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) head = head[3..];
        while (head.Length > 0 && head[0] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') head = head[1..];
        return head.Length > 0 && head[0] == (byte)'<' && head.IndexOf("<svg"u8) >= 0;
    }
}
