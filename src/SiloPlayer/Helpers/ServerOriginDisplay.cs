namespace SiloPlayer.Helpers;

/// <summary>
/// The server a password is about to be sent to, as the user should see it.
/// Login and invitation pages are otherwise styled entirely by that server
/// (its name and artwork), and a silo://invite link can name any server.
/// </summary>
public static class ServerOriginDisplay
{
    /// <summary>
    /// Formats the server's host (punycode, so look-alike Unicode names are
    /// visible), its port when not the scheme default, and a warning for
    /// plain HTTP. Returns an empty string for an unusable URL.
    /// </summary>
    public static string Format(string? serverUrl)
    {
        if (!Uri.TryCreate(serverUrl?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return "";
        var host = uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.IdnHost}]" : uri.IdnHost;
        var origin = uri.IsDefaultPort ? host : $"{host}:{uri.Port}";
        return uri.Scheme == Uri.UriSchemeHttp ? $"{origin} · Not secure (HTTP)" : origin;
    }
}
