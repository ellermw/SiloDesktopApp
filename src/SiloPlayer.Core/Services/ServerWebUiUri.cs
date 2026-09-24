namespace SiloPlayer.Core.Services;

public static class ServerWebUiUri
{
    public static Uri FromApiBase(string apiBaseUrl)
    {
        if (!Uri.TryCreate(apiBaseUrl?.Trim(), UriKind.Absolute, out var apiUri) ||
            apiUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("A valid HTTP or HTTPS server URL is required.", nameof(apiBaseUrl));

        var path = apiUri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/api/v2", StringComparison.OrdinalIgnoreCase))
            path = path[..^"/api/v1".Length];

        var builder = new UriBuilder(apiUri)
        {
            Path = string.IsNullOrEmpty(path) ? "/" : path.TrimEnd('/') + "/",
            Query = "",
            Fragment = "",
        };
        return builder.Uri;
    }
}
