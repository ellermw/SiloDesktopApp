namespace SiloPlayer.Core.Services;

public static class OAuthCompletionUrl
{
    public static bool TryGetCode(string? uriText, string? serverUrl, out string code)
    {
        code = "";
        if (!HasValidPercentEncoding(uriText) ||
            !Uri.TryCreate(uriText, UriKind.Absolute, out var candidate) ||
            !Uri.TryCreate(serverUrl, UriKind.Absolute, out var configuredServer) ||
            !HasSameOrigin(candidate, configuredServer) ||
            !candidate.AbsolutePath.TrimEnd('/').EndsWith(
                "/login/oauth-complete",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            foreach (var part in candidate.Query.TrimStart('?')
                         .Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length != 2 ||
                    !string.Equals(
                        Uri.UnescapeDataString(pieces[0]),
                        "code",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                code = Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
                return !string.IsNullOrWhiteSpace(code);
            }
        }
        catch (UriFormatException)
        {
            code = "";
        }

        return false;
    }

    private static bool HasSameOrigin(Uri candidate, Uri configuredServer)
        => string.Equals(candidate.Scheme, configuredServer.Scheme, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(candidate.IdnHost, configuredServer.IdnHost, StringComparison.OrdinalIgnoreCase) &&
           candidate.Port == configuredServer.Port;

    private static bool HasValidPercentEncoding(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%') continue;
            if (index + 2 >= value.Length ||
                !Uri.IsHexDigit(value[index + 1]) ||
                !Uri.IsHexDigit(value[index + 2]))
            {
                return false;
            }
            index += 2;
        }
        return true;
    }
}
