using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Produces diagnostic-safe playback URLs. Stream path credentials, session
/// identifiers, query tokens, signatures, and fragments must never reach logs.
/// </summary>
public static partial class PlaybackUrlRedactor
{
    /// <summary>Removes signed URL authority from ordinary and relative diagnostics.</summary>
    public static string RedactSignedCredentials(string text)
    {
        text = SignedDeliveryPathRegex().Replace(text, "${prefix}<redacted>");
        return PathSegmentRegex().Replace(text, match =>
        {
            var segment = match.Value;
            var trimmed = segment.TrimEnd('.', ',', ';', ':', '!', ')', ']', '}');
            string decoded;
            try { decoded = Uri.UnescapeDataString(trimmed); }
            catch (UriFormatException) { return segment; }
            return SignedTokenRegex().IsMatch(decoded)
                ? "<redacted>" + segment[trimmed.Length..] : segment;
        });
    }

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
        path = SignedDeliveryPathRegex().Replace(path, "${prefix}<redacted>");
        path = RedactSignedCredentials(path);
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

    [GeneratedRegex("(?<prefix>/(?:stream/(?:direct|remux|transcode|subtitles)|downloads/file)/)[^/\\s?#<>\"']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SignedDeliveryPathRegex();

    [GeneratedRegex("(?<=/)[^/\\s?#<>\"']+", RegexOptions.CultureInvariant)]
    private static partial Regex PathSegmentRegex();

    [GeneratedRegex(@"^eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SignedTokenRegex();
}
