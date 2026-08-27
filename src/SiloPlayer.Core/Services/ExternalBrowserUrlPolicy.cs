namespace SiloPlayer.Core.Services;

public static class ExternalBrowserUrlPolicy
{
    public static bool TryGetSafeUri(string? value, out Uri uri)
    {
        uri = null!;
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Any(char.IsControl))
            return false;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(parsed.Host))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
