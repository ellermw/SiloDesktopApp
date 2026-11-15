namespace SiloPlayer.Core.Services;

public enum AccountLinkFailure
{
    None,
    /// <summary>An absolute URL that is not the selected server's page for this kind of link.</summary>
    WrongServer,
    /// <summary>The server and page match, or the value is not a URL, but there is no usable token.</summary>
    InvalidToken,
}

/// <summary>
/// Reads the one-time token of a password-reset or invitation link, matching the
/// current Silo WebUI (<c>web/src/lib/linkToken.ts</c>): links carry it in the
/// fragment (<c>/reset-password#token</c>, <c>/invite#token</c>); links issued
/// earlier carry it in the path (<c>/reset-password/token</c>). A path token wins
/// over a fragment. A bare token is also accepted. Only an absolute http(s) URL
/// on the selected server (origin and optional base path) is accepted.
/// The token is a bearer credential: never log it or the link it came from.
/// </summary>
public static class AccountLinkToken
{
    public const string ResetPage = "reset-password";
    public const string InvitePage = "invite";

    /// <summary>The server's declared body-token maximum; a longer value is never a token.</summary>
    public const int MaxLength = 128;

    public static bool TryParse(string? value, string page, string? selectedServer, out string token, out AccountLinkFailure failure)
    {
        token = "";
        failure = AccountLinkFailure.InvalidToken;
        var candidate = value?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(candidate)) return false;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var link))
            return IsValidToken(candidate, out token) && Ok(out failure);

        if (!TryGetServerOrigin(selectedServer, out var origin)
            || link.Scheme is not ("http" or "https")
            || !string.Equals(link.Scheme, origin.Scheme, StringComparison.Ordinal)
            || !string.Equals(link.Authority, origin.Authority, StringComparison.OrdinalIgnoreCase)
            || link.UserInfo.Length != 0)
        {
            failure = AccountLinkFailure.WrongServer;
            return false;
        }

        var pagePath = origin.AbsolutePath.TrimEnd('/') + "/" + page;
        var path = link.AbsolutePath;
        string raw;
        if (path.StartsWith(pagePath + "/", StringComparison.Ordinal) && path.Length > pagePath.Length + 1)
        {
            // Old path form. One segment only; a single trailing slash is harmless.
            raw = path[(pagePath.Length + 1)..];
            if (raw.EndsWith('/')) raw = raw[..^1];
            if (raw.Length == 0 || raw.Contains('/')) return false;
            return IsValidToken(Uri.UnescapeDataString(raw), out token) && Ok(out failure);
        }
        if (string.Equals(path, pagePath, StringComparison.Ordinal) || string.Equals(path, pagePath + "/", StringComparison.Ordinal))
        {
            // Current form: the token is the fragment. An empty or missing fragment is a link that lost its token.
            raw = link.Fragment.Length > 1 ? link.Fragment[1..] : "";
            return IsValidToken(Uri.UnescapeDataString(raw), out token) && Ok(out failure);
        }
        failure = AccountLinkFailure.WrongServer;
        return false;
    }

    /// <summary>True for an absolute http(s) URL whose path has <paramref name="page"/> as a path segment, regardless of server.</summary>
    public static bool LooksLikeLink(string? value, string page)
        => Uri.TryCreate(value?.Trim().Trim('"').Trim(), UriKind.Absolute, out var link)
            && link.Scheme is "http" or "https"
            && (link.AbsolutePath.Contains("/" + page + "/", StringComparison.Ordinal)
                || link.AbsolutePath.EndsWith("/" + page, StringComparison.Ordinal));

    private static bool Ok(out AccountLinkFailure failure) { failure = AccountLinkFailure.None; return true; }

    private static bool TryGetServerOrigin(string? server, out Uri origin)
    {
        origin = default!;
        if (string.IsNullOrWhiteSpace(server)) return false;
        if (!Uri.TryCreate(server.TrimEnd('/') + "/", UriKind.Absolute, out var parsed)) return false;
        origin = parsed;
        return true;
    }

    private static bool IsValidToken(string value, out string token)
    {
        token = "";
        if (value.Length == 0 || value.Length > MaxLength) return false;
        foreach (var c in value)
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') return false;
        token = value;
        return true;
    }
}
