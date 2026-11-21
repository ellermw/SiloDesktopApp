namespace SiloPlayer.Core.Services;

/// <summary>
/// The server's plugin-content mount. WebUI plugin pages are authorized only by the launch
/// cookie the server scopes to this parent path (<c>plugins.ContentPrefix</c>), so a plugin
/// document's own requests carry credentials to plugin pages and assets and nothing else.
/// </summary>
public static class PluginContentPaths
{
    public const string ContentPrefix = "/api/v2/plugin-content";

    /// <summary>
    /// Every request header the app adds for the account, the profile and the device (SiloApiClient.AddHeaders). A request
    /// that leaves the plugin-content parent, for example by a redirect, must not carry any of them.
    /// </summary>
    public static readonly IReadOnlyList<string> AccountHeaderNames =
    [
        "Authorization", "X-Profile-Id", "X-Profile-Token", "X-Silo-Client", "X-Silo-Client-Family", "X-Silo-Client-Version",
        "X-Silo-Device-Id", "X-Silo-Device-Name", "X-Silo-Device-Platform",
    ];

    /// <summary>The connected server's base path (empty at the root, never ending in '/').</summary>
    public static string BasePath(Uri serverBase) => serverBase.AbsolutePath.TrimEnd('/');

    /// <summary>
    /// Whether <paramref name="target"/> is on the connected server's origin and inside the
    /// plugin-content parent path, the only place the official credential reaches.
    /// Encoded separators or dot segments are refused rather than interpreted. An escaped percent sign is accepted here, as the
    /// official cookie covers such paths and one server-side decode cannot turn it into a separator.
    /// </summary>
    public static bool IsWithinContentParent(Uri serverBase, Uri target)
    {
        if (!serverBase.IsAbsoluteUri || !target.IsAbsoluteUri || target.UserInfo.Length != 0) return false;
        if (!string.Equals(serverBase.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(serverBase.Host, target.Host, StringComparison.OrdinalIgnoreCase) || serverBase.Port != target.Port) return false;
        var path = target.AbsolutePath;
        if (!HasPlainSegments(path, allowEscapedPercent: true)) return false;
        var parent = BasePath(serverBase) + ContentPrefix;
        return path.StartsWith(parent + "/", StringComparison.Ordinal);
    }

    /// <summary>False for a backslash, any percent-escaped separator, dot or NUL (and, unless allowed, an escaped percent sign), or a dot segment.</summary>
    public static bool HasPlainSegments(string escapedPath, bool allowEscapedPercent = false)
    {
        if (escapedPath.Length == 0 || escapedPath[0] != '/' || escapedPath.Contains('\\')) return false;
        foreach (var marker in allowEscapedPercent ? new[] { "%2f", "%5c", "%2e", "%00" } : new[] { "%2f", "%5c", "%2e", "%00", "%25" })
            if (escapedPath.Contains(marker, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var segment in escapedPath.Split('/'))
            if (segment is "." or "..") return false;
        return true;
    }
}
