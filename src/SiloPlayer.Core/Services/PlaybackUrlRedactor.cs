using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Produces diagnostic-safe playback URLs. Stream path credentials, session
/// identifiers, query tokens, signatures, and fragments must never reach logs.
/// </summary>
public static partial class PlaybackUrlRedactor
{
    public static string Redact(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "<empty-url>";

        var value = url.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            var safeAuthority = new UriBuilder(
                absolute.Scheme,
                absolute.Host,
                absolute.IsDefaultPort ? -1 : absolute.Port).Uri.GetLeftPart(UriPartial.Authority);
            return safeAuthority + RedactPath(absolute.AbsolutePath);
        }

        var pathEnd = value.IndexOfAny(['?', '#']);
        var path = pathEnd >= 0 ? value[..pathEnd] : value;
        return RedactPath(path);
    }

    private static string RedactPath(string path)
    {
        path = SignedNodeStreamRegex().Replace(path, "$1<redacted>");
        path = IntegratedStreamRegex().Replace(path, "$1<redacted>");
        path = PlaybackSessionRegex().Replace(path, "$1<redacted>");
        return path;
    }

    [GeneratedRegex(
        "(/stream/(?:direct|remux|transcode)/)[^/]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SignedNodeStreamRegex();

    [GeneratedRegex(
        "(/(?:api/v1/)?stream/)(?!direct(?:/|$)|remux(?:/|$)|transcode(?:/|$))[^/]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IntegratedStreamRegex();

    [GeneratedRegex(
        "(/(?:api/v1/)?playback/(?:transcode|ws)/)[^/]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaybackSessionRegex();
}
