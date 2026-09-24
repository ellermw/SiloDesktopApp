namespace SiloPlayer.Core.Services;

public static class PlaybackDeliveryUrl
{
    public static string Resolve(string baseUrl, string pathOrUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrUrl);
        var value = pathOrUrl.Trim();
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return value; // Preserve distributed delivery origins and signed query bytes.
        if (!value.StartsWith('/')) value = "/" + value;
        // playback-api.md explicitly projects these local delivery mounts into
        // v2 without changing st. Other API/proxy paths remain server-owned.
        if (value.StartsWith("/api/v1/stream/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("/api/v1/playback/transcode/", StringComparison.OrdinalIgnoreCase))
            value = "/api/v2/" + value[8..];
        else if (!value.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) value = "/api/v2" + value;
        return baseUrl.TrimEnd('/') + value;
    }
}
