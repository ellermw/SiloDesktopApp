using System.Globalization;
using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services;

/// <summary>
/// A per-page virtual origin, never the server's browser origin. Only this installation's
/// dynamic content and read-only static assets may borrow the captured native authority.
/// No cookie jar, automatic redirect following, retry, arbitrary server API access, or native script bridge.
/// </summary>
public sealed class AdminPluginContentSession : IDisposable
{
    private const int MaxResponse = 16 * 1024 * 1024;
    private const int MaxRequest = 4 * 1024 * 1024;
    private readonly SiloApiClient _client;
    private readonly MonitorAuthorityLease _lease;
    private readonly HttpClient _http;
    private readonly string _contentPath, _assetPath;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(4, 4);
    private long _responseBytes;
    private int _pending;
    private int _disposed;

    public Uri Origin { get; } = new("https://silo-admin-plugin-" + Guid.NewGuid().ToString("N") + ".invalid/");
    public Uri StartUri { get; }

    public AdminPluginContentSession(SiloApiClient client, MonitorAuthorityLease lease, AdminPluginApp app, HttpMessageHandler? handler = null)
    {
        lease.ThrowIfRetired();
        if (app.InstallationId <= 0 || !AdminPluginAppsApi.SafePath(app.RoutePath, pattern: true))
            throw new InvalidDataException("Invalid Admin plugin route.");
        _client = client;
        _lease = lease;
        // An injected handler is owned by its fixture, never by this page.
        _http = handler == null ? new HttpClient(new HttpClientHandler
        {
            UseCookies = false, AllowAutoRedirect = false, UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.None,
        }) : new HttpClient(handler, disposeHandler: false);
        var id = app.InstallationId.ToString(CultureInfo.InvariantCulture);
        _contentPath = "/api/v2/plugin-content/plugins/" + id + "/";
        _assetPath = "/api/v2/plugin-content/plugin-assets/" + id + "/";
        var route = app.RoutePath.EndsWith("/*", StringComparison.Ordinal) ? app.RoutePath[..^2] : app.RoutePath;
        StartUri = new Uri(Origin, _contentPath.TrimEnd('/') + (route.Length == 0 ? "/" : route) + "?theme=dark");
    }

    public bool CanNavigate(Uri target) => Volatile.Read(ref _disposed) == 0 && _lease.IsCurrent && Allowed(target, "GET", document: true);

    private bool Allowed(Uri target, string method, bool document)
    {
        if (!target.IsAbsoluteUri || target.Scheme != Origin.Scheme || target.Host != Origin.Host || target.Port != Origin.Port ||
            target.UserInfo.Length != 0 || target.AbsoluteUri.Length > 16384 || !AdminPluginAppsApi.SafePath(target.AbsolutePath)) return false;
        if (method is not ("GET" or "HEAD" or "POST" or "PUT" or "PATCH" or "DELETE" or "OPTIONS")) return false;
        return target.AbsolutePath.StartsWith(_contentPath, StringComparison.Ordinal) ||
            (!document && (method is "GET" or "HEAD") && target.AbsolutePath.StartsWith(_assetPath, StringComparison.Ordinal));
    }

    public async Task<PluginPublicResource> SendAsync(Uri target, string method, IReadOnlyDictionary<string, string> headers,
        Stream? body, bool document, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _lease.ThrowIfRetired();
        if (!Allowed(target, method, document)) throw new InvalidDataException("Admin plugin requests must stay within this installation's content and assets.");
        if (Interlocked.Increment(ref _pending) > 8)
        { Interlocked.Decrement(ref _pending); throw new InvalidDataException("Too many concurrent Admin plugin requests."); }
        var entered = false;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lease.Token, _stop.Token, ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            await _slots.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            var context = _lease.Context;
            var serverBase = new Uri(context.BaseUrl.TrimEnd('/') + "/");
            var serverTarget = new Uri(context.BaseUrl.TrimEnd('/') + target.PathAndQuery);
            if (!SameOrigin(serverBase, serverTarget) || serverTarget.UserInfo.Length != 0)
                throw new InvalidDataException("Invalid plugin server address.");
            using var request = _client.CreateAuthenticatedRequest(context, new HttpMethod(method), serverTarget.AbsoluteUri);
            if (body != null) request.Content = new ByteArrayContent(await ReadBoundedAsync(body, MaxRequest, deadline.Token).ConfigureAwait(false));
            foreach (var header in headers)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                else if (header.Key is "Accept" or "Accept-Language" or "Range" or "If-Range" or "If-None-Match" or "If-Modified-Since" or "If-Match")
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            _lease.ThrowIfRetired();
            // Deliberately one send, including non-idempotent form/fetch requests.
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            _lease.ThrowIfRetired();
            var outgoing = new List<string> { "Cache-Control: no-store", "X-Content-Type-Options: nosniff", "Referrer-Policy: no-referrer" };
            // The browser never sees Set-Cookie, auth headers, server locations or error bodies
            // outside this mount. Only these representation headers cross the boundary.
            foreach (var name in new[] { "Content-Type", "Content-Encoding", "Content-Range", "Accept-Ranges", "ETag", "Last-Modified", "Content-Language", "Vary" })
                if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
                    outgoing.AddRange(values.Select(value => name + ": " + value));
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                // A 307/308 (or ambiguous 301/302) must not make Chromium submit
                // a state-changing request a second time. A 303 explicitly becomes GET.
                if (method is not ("GET" or "HEAD") && response.StatusCode != HttpStatusCode.SeeOther)
                    throw new IOException("The plugin redirected a submitted change. Review its state before submitting again.");
                if (response.Headers.Location is not { } location) throw new InvalidDataException("Plugin redirect has no address.");
                var next = new Uri(serverTarget, location);
                var rootPath = serverBase.AbsolutePath.TrimEnd('/');
                if (!SameOrigin(serverBase, next) || next.UserInfo.Length != 0 || !next.AbsolutePath.StartsWith(rootPath + "/", StringComparison.Ordinal))
                    throw new InvalidDataException("Admin plugin redirect left its installation.");
                var virtualNext = new Uri(Origin, next.PathAndQuery[rootPath.Length..]);
                if (!Allowed(virtualNext, "GET", document)) throw new InvalidDataException("Admin plugin redirect left its installation.");
                outgoing.Add("Location: " + virtualNext.AbsoluteUri);
                _lease.ThrowIfRetired();
                deadline.Token.ThrowIfCancellationRequested();
                return new((int)response.StatusCode, "Plugin redirect", [], string.Join("\r\n", outgoing));
            }
            if (document)
            {
                // A fresh virtual origin plus sandbox/CSP isolates storage, cookies, workers,
                // sockets, nested frames, popups and native/server APIs, while supporting the
                // installation's own scripts, forms, relative assets and fetch endpoints.
                var scope = Origin.GetLeftPart(UriPartial.Authority) + _contentPath;
                outgoing.Add("Content-Security-Policy: sandbox allow-scripts allow-same-origin allow-forms; default-src 'self' data: blob:; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src " + scope + "; form-action " + scope + "; base-uri 'self'; object-src 'none'; frame-src 'none'; worker-src 'none'; frame-ancestors 'none'");
            }
            if (response.Content.Headers.ContentLength > MaxResponse) throw new InvalidDataException("Admin plugin response is too large.");
            await using var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            byte[] bytes = method == "HEAD" ? [] : await ReadBoundedAsync(source, MaxResponse, deadline.Token).ConfigureAwait(false);
            if (Interlocked.Add(ref _responseBytes, bytes.Length) > 128L * 1024 * 1024)
                throw new InvalidDataException("Admin plugin page exceeded its transfer budget. Reopen it to continue.");
            _lease.ThrowIfRetired();
            deadline.Token.ThrowIfCancellationRequested();
            return new((int)response.StatusCode, "Plugin response", bytes, string.Join("\r\n", outgoing));
        }
        finally { if (entered) _slots.Release(); Interlocked.Decrement(ref _pending); }
    }

    private static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && a.Host == b.Host && a.Port == b.Port;

    private static async Task<byte[]> ReadBoundedAsync(Stream source, int max, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > max) throw new InvalidDataException("Admin plugin resource exceeds its size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel(); _http.Dispose(); _stop.Dispose();
    }
}
