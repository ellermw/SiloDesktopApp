namespace SiloPlayer.Core.Services;

/// <summary>Recognizes retiring Silo native resources without changing signed URLs.</summary>
public static class NativeApiRetirement
{
    public static bool IsRetiredResource(string value)
    {
        var path = Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https"
            ? uri.AbsolutePath
            : value.Split('?', '#')[0];
        // Signed worker paths are opaque after these retained mounts. A token
        // may itself contain escaped API-looking text; it is never a namespace.
        if (path.StartsWith("/stream/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/downloads/file/", StringComparison.OrdinalIgnoreCase)) return false;
        // Compare path segments, including deployment prefixes and escaped spellings.
        // Worker /stream/* and /downloads/* routes are separate retained contracts.
        var segments = Uri.UnescapeDataString(path).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < segments.Length; i++)
            if (segments[i].Equals("api", StringComparison.OrdinalIgnoreCase) &&
                segments[i + 1].Equals("v1", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static void RequireCurrentResource(string value)
    {
        if (IsRetiredResource(value))
            throw new InvalidDataException("The server returned a retired API resource. Refresh this page or restart playback to obtain a current URL.");
    }
}
