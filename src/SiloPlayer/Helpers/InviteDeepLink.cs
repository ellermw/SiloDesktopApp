namespace SiloPlayer.Helpers;

/// <summary>
/// Parses the native invitation contract emitted by the current Silo WebUI:
/// silo://invite?server=&lt;origin&gt;&amp;token=&lt;token&gt;.
/// </summary>
public static class InviteDeepLink
{
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
