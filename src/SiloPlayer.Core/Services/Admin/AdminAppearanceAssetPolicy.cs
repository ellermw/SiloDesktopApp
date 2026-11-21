namespace SiloPlayer.Core.Services.Admin;

/// <summary>Admission limits for the pinned v2 branding asset write API.</summary>
public static class AdminAppearanceAssetPolicy
{
    public static bool IsAllowed(string kind, string mediaType, long length)
    {
        if (length <= 0) return false;
        return kind switch
        {
            "wordmark" or "mark" => length <= 8L * 1024 * 1024 && IsImage(mediaType),
            "login_bg" => length <= 12L * 1024 * 1024 && IsImage(mediaType),
            "favicon" => length <= 1024 * 1024 && mediaType is
                "image/png" or "image/webp" or "image/x-icon" or "image/vnd.microsoft.icon" or "image/svg+xml",
            _ => false,
        };
    }

    private static bool IsImage(string mediaType) => mediaType is "image/png" or "image/jpeg" or "image/webp";

    public static bool IsSvgUrl(Uri uri)
    {
        if (uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return true;
        if (uri.AbsolutePath != "/api/v2/branding/assets/favicon") return false;
        var versions = uri.Query.TrimStart('?').Split('&').Where(part => part.StartsWith("v=", StringComparison.Ordinal)).ToArray();
        return versions.Length == 1 && Uri.UnescapeDataString(versions[0][2..].Replace("+", " ", StringComparison.Ordinal))
            .EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }
}
