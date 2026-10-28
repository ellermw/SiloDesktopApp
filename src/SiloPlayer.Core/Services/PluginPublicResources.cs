using System.Net;

namespace SiloPlayer.Core.Services;

public sealed record PluginPublicResource(int Status, string Reason, byte[] Body, string Headers);

/// <summary>Reads public plugin subresources without borrowing the WebView cookie jar.</summary>
public sealed class PluginPublicResources : IDisposable
{
    private const int MaxBytes = 32 * 1024 * 1024;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false,
        UseDefaultCredentials = false,
        AutomaticDecompression = DecompressionMethods.None,
    });

    public async Task<PluginPublicResource> ReadAsync(Uri target, Uri serverOrigin, string method,
        IReadOnlyDictionary<string, string> headers, Func<bool> ownerCurrent, CancellationToken ct)
    {
        if (method is not ("GET" or "HEAD"))
            throw new InvalidDataException("External plugin resources must be public GET or HEAD requests.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var ownerWatch = new Timer(_ =>
        {
            try { if (!ownerCurrent()) deadline.Cancel(); }
            catch (ObjectDisposedException) { }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        var requireHttps = serverOrigin.Scheme == "https" || target.Scheme == "https";
        if (!ownerCurrent()) throw new OperationCanceledException("Plugin ownership changed.");
        if (target.Scheme is not ("http" or "https") || target.UserInfo.Length != 0)
            throw new InvalidDataException("External plugin resource has an unsupported address.");
        if (target.Scheme == serverOrigin.Scheme && target.Host == serverOrigin.Host && target.Port == serverOrigin.Port)
            throw new InvalidDataException("External plugin resources cannot redirect back into the connected server.");
        if (requireHttps && target.Scheme != "https")
            throw new InvalidDataException("External plugin resources cannot redirect to insecure HTTP.");
        using var request = new HttpRequestMessage(new HttpMethod(method), target);
        foreach (var header in headers)
            if (header.Key is "Accept" or "Accept-Language" or "Range" or "If-Range" or "If-None-Match" or "If-Modified-Since" or "Origin")
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
        {
            if (response.Headers.Location is not { } location)
                throw new InvalidDataException("External plugin resource redirect has no address.");
            var next = location.IsAbsoluteUri ? location : new Uri(target, location);
            if (next.Scheme is not ("http" or "https") || next.UserInfo.Length != 0 ||
                (requireHttps && next.Scheme != "https") ||
                (next.Scheme == serverOrigin.Scheme && next.Host == serverOrigin.Host && next.Port == serverOrigin.Port))
                throw new InvalidDataException("External plugin resource redirect crossed its public boundary.");
            if (!ownerCurrent()) throw new OperationCanceledException("Plugin ownership changed.");
            // Keep the browser-visible redirect, so relative CSS/image/module
            // URLs use the final URL. Its next hop is intercepted again, never
            // dispatched by the browser with its cookie jar. Chromium retains
            // its own finite redirect limit; no state-changing method is replayed.
            var redirectHeaders = Headers(response, redirect: true) + "\r\nLocation: " + next.AbsoluteUri + "\r\nContent-Length: 0";
            return new((int)response.StatusCode, response.ReasonPhrase ?? "Public redirect", [], redirectHeaders);
        }
        if (response.Content.Headers.ContentLength > MaxBytes && method != "HEAD")
            throw new InvalidDataException("External plugin resource is too large.");
        using var body = new MemoryStream();
        if (method != "HEAD")
        {
            await using var remote = await response.Content.ReadAsStreamAsync(deadline.Token);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await remote.ReadAsync(buffer, deadline.Token)) != 0)
            {
                if (body.Length + read > MaxBytes) throw new InvalidDataException("External plugin resource is too large.");
                await body.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
            }
        }
        if (!ownerCurrent()) throw new OperationCanceledException("Plugin ownership changed.");
        return new((int)response.StatusCode, response.ReasonPhrase ?? "Public resource", body.ToArray(), Headers(response));
    }

    private static string Headers(HttpResponseMessage response, bool redirect = false)
        => string.Join("\r\n", response.Headers.Concat(response.Content.Headers)
            .Where(header => !ExcludedResponseHeader(header.Key) && (!redirect ||
                (!header.Key.Equals("Location", StringComparison.OrdinalIgnoreCase) && !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))))
            .SelectMany(header => header.Value.Select(value => header.Key + ": " + value)));

    private static bool ExcludedResponseHeader(string name)
        => name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) || name.Equals("Set-Cookie2", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Connection", StringComparison.OrdinalIgnoreCase) || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) || name.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _http.Dispose();
}
