using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

/// <summary>
/// Parses the native invitation contract emitted by the current Silo WebUI:
/// silo://invite?server=&lt;origin&gt;&amp;token=&lt;token&gt;, and the selected server's web claim link.
/// </summary>
public static class InviteDeepLink
{
    /// <summary>
    /// Accepts the native <c>silo://invite</c> handoff, or the web claim link of the selected server
    /// (<c>/invite#token</c>, or the older <c>/invite/token</c> path form). Anything else is refused.
    /// </summary>
    public static bool TryParse(string? value, string? selectedServer, out InviteClaimNavigation navigation)
    {
        if (TryParse(value, out navigation)) return true;
        navigation = default!;
        if (string.IsNullOrWhiteSpace(selectedServer) ||
            !AccountLinkToken.TryParse(value, AccountLinkToken.InvitePage, selectedServer, out var token, out _) ||
            !Uri.TryCreate(value!.Trim().Trim('"').Trim(), UriKind.Absolute, out var link) ||
            link.Scheme is not ("http" or "https"))
        {
            // A bare token has no server to claim against; only a link carries one.
            return false;
        }

        navigation = new InviteClaimNavigation(selectedServer.TrimEnd('/'), token);
        return true;
    }

    /// <summary>True for an http(s) link shaped like a web invitation link, so activation can wait for a selected server.</summary>
    public static bool IsServerLinkRoute(string? value) => AccountLinkToken.LooksLikeLink(value, AccountLinkToken.InvitePage);

    public static bool TryParse(string? value, out InviteClaimNavigation navigation)
    {
        navigation = default!;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim().Trim('"'), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "silo", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "invite", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var values = ParseQuery(uri.Query);
        if (!values.TryGetValue("server", out var server) ||
            !values.TryGetValue("token", out var token) ||
            !Uri.TryCreate(server, UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != Uri.UriSchemeHttps && serverUri.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(serverUri.UserInfo) ||
            string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        navigation = new InviteClaimNavigation(
            serverUri.GetLeftPart(UriPartial.Authority).TrimEnd('/'),
            token.Trim());
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            var value = separator >= 0 ? pair[(separator + 1)..] : "";
            result[Uri.UnescapeDataString(key.Replace('+', ' '))] =
                Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return result;
    }
}

public sealed record InviteClaimNavigation(string ServerUrl, string Token);
