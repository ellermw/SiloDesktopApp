using System.Net;

namespace SiloPlayer.Core.Api;

/// <summary>
/// Creates the HTTP transport used for Silo API requests.
/// </summary>
public static class SiloHttpClientFactory
{
    /// <summary>
    /// Creates the hardened transport handler used by the Silo API client.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        // Authenticated Silo requests carry profile headers that .NET can retain
        // across redirects. Surface redirects to the caller instead of risking a
        // credential-bearing retry to another origin or a cleartext endpoint.
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        MaxConnectionsPerServer = 16,
        EnableMultipleHttp2Connections = true,
    };

    /// <summary>
    /// Creates a Silo API HTTP client with compression, HTTP/2, pooling, and
    /// credential-safe redirect behavior configured consistently.
    /// </summary>
    public static HttpClient CreateClient() => new(CreateHandler())
    {
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
    };
}
