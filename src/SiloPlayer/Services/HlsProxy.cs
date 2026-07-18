using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

/// <summary>
/// Loopback HTTP proxy used by mpv for integrated and distributed HLS playback.
/// Every playlist URI remains on the loopback origin so access-token rotation,
/// nested playlists, byte ranges, init segments, and signed node URLs share one
/// deterministic transport path.
/// </summary>
public sealed class HlsProxy : IDisposable
{
    private static readonly Regex PlaylistUriRegex = new(
        "URI=\"(?<uri>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Uri _remoteManifestUri;
    private readonly Uri? _authenticationOrigin;
    private readonly Func<string?> _accessTokenProvider;
    private readonly HttpClient _http;
    private readonly string _routeToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private readonly ConcurrentDictionary<int, Task> _clientTasks = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private Task? _cleanupTask;
    private int _port;
    private int _nextClientTaskId;
    private bool _started;
    private bool _everStarted;
    private bool _disposed;

    public HlsProxy(string remoteManifestUrl, string? accessToken)
        : this(remoteManifestUrl, () => accessToken)
    {
    }

    public HlsProxy(string remoteManifestUrl, Func<string?> accessTokenProvider)
    {
        if (!Uri.TryCreate(remoteManifestUrl, UriKind.Absolute, out var remoteManifestUri) ||
            !IsSupportedRemoteUri(remoteManifestUri))
        {
            throw new ArgumentException(
                "The remote HLS manifest must be an absolute HTTP or HTTPS URL.",
                nameof(remoteManifestUrl));
        }

        _remoteManifestUri = remoteManifestUri;
        _authenticationOrigin = IsSignedNodeUrl(remoteManifestUri) ? null : remoteManifestUri;
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public string Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_everStarted)
            throw new InvalidOperationException("The HLS proxy has already been started.");

        var cts = new CancellationTokenSource();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
        }
        catch
        {
            listener.Dispose();
            cts.Dispose();
            throw;
        }

        _cts = cts;
        _listener = listener;
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _started = true;
        _everStarted = true;
        _acceptLoopTask = AcceptLoopAsync(listener, cts.Token);
        Log($"Started on port {_port}");
        return $"http://127.0.0.1:{_port}/master.m3u8?k={_routeToken}";
    }

    public void Stop()
    {
        if (!_started)
            return;

        _started = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { (_listener as IDisposable)?.Dispose(); } catch { }
        _listener = null;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                var taskId = Interlocked.Increment(ref _nextClientTaskId);
                var task = HandleClientAsync(client, ct);
                _clientTasks[taskId] = task;
                _ = ObserveClientAsync(taskId, task);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Log($"Accept error: {ex.Message}");
                if (!ct.IsCancellationRequested)
                    await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                // HLS consists of many small playlist/init/segment responses.
                // Avoid Nagle-delaying the loopback hop between mpv and the
                // proxy, especially during startup and after a seek.
                client.NoDelay = true;
                client.ReceiveTimeout = 10_000;
                client.SendTimeout = 60_000;
                var stream = client.GetStream();
                using var reader = new StreamReader(
                    stream,
                    Encoding.ASCII,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4096,
                    leaveOpen: true);

                var request = await ReadRequestAsync(reader, ct).ConfigureAwait(false);
                if (request == null)
                {
                    await WriteResponseAsync(stream, 400, "Bad Request", "text/plain", null, false, ct).ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain", null, false, ct).ConfigureAwait(false);
                    return;
                }

                var headOnly = string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
                var remoteUrl = ResolveRemoteUrl(request.Target);
                if (remoteUrl == null)
                {
                    await WriteResponseAsync(stream, 404, "Not Found", "text/plain", null, headOnly, ct).ConfigureAwait(false);
                    return;
                }

                request.Headers.TryGetValue("Range", out var range);
                await ServeRemoteAsync(stream, remoteUrl, range, headOnly, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (IOException) { }
        catch (Exception ex) { Log($"Client error: {ex.Message}"); }
    }

    private async Task ServeRemoteAsync(
        NetworkStream stream,
        string rawRemoteUrl,
        string? range,
        bool headOnly,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 15 && !ct.IsCancellationRequested; attempt++)
        {
            var remoteUrl = ApplyCurrentAuthentication(rawRemoteUrl);
            var responseStarted = false;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, remoteUrl);
                if (!string.IsNullOrWhiteSpace(range) && RangeHeaderValue.TryParse(range, out var parsedRange))
                    request.Headers.Range = parsedRange;

                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct).ConfigureAwait(false);

                if (ShouldRetryStatus(response.StatusCode) && attempt < 14)
                {
                    if (attempt == 0)
                    {
                        Log(
                            $"HTTP {(int)response.StatusCode} waiting for {PlaybackUrlRedactor.Redact(remoteUrl)}");
                    }

                    await Task.Delay(GetStatusRetryDelay(response.StatusCode), ct).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorHeaders = GetForwardHeaders(response);
                    await WriteResponseHeaderAsync(
                        stream,
                        (int)response.StatusCode,
                        response.ReasonPhrase ?? "Error",
                        response.Content.Headers.ContentType?.MediaType,
                        null,
                        errorHeaders,
                        ct).ConfigureAwait(false);
                    return;
                }

                var effectiveRemoteUri = response.RequestMessage?.RequestUri ?? new Uri(remoteUrl);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                if (IsPlaylist(effectiveRemoteUri.AbsoluteUri, contentType))
                {
                    var playlist = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var rewritten = RewritePlaylist(playlist, effectiveRemoteUri);
                    var body = Encoding.UTF8.GetBytes(rewritten);
                    await WriteResponseAsync(
                        stream,
                        (int)response.StatusCode,
                        response.ReasonPhrase ?? "OK",
                        "application/vnd.apple.mpegurl",
                        body,
                        headOnly,
                        ct).ConfigureAwait(false);
                    return;
                }

                var extraHeaders = GetForwardHeaders(response);

                await WriteResponseHeaderAsync(
                    stream,
                    (int)response.StatusCode,
                    response.ReasonPhrase ?? "OK",
                    contentType,
                    response.Content.Headers.ContentLength,
                    extraHeaders,
                    ct).ConfigureAwait(false);
                responseStarted = true;
                if (!headOnly)
                {
                    await using var remoteStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await remoteStream.CopyToAsync(stream, ct).ConfigureAwait(false);
                }
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (OperationCanceledException ex)
            {
                if (responseStarted)
                    return;
                if (attempt >= 14)
                {
                    Log($"HTTP timeout after {attempt + 1} attempts: {ex.Message}");
                    await WriteResponseAsync(stream, 504, "Gateway Timeout", "text/plain", null, headOnly, ct)
                        .ConfigureAwait(false);
                    return;
                }

                Log($"HTTP timeout: {ex.Message}; retry {attempt + 1}");
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                if (responseStarted)
                    return;
                if (attempt >= 14)
                {
                    Log($"HTTP failure after {attempt + 1} attempts: {ex.Message}");
                    await WriteResponseAsync(stream, 502, "Bad Gateway", "text/plain", null, headOnly, ct)
                        .ConfigureAwait(false);
                    return;
                }

                Log($"HTTP error: {ex.Message}; retry {attempt + 1}");
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
        }

        await WriteResponseAsync(stream, 404, "Not Found", "text/plain", null, headOnly, ct).ConfigureAwait(false);
    }

    private string? ResolveRemoteUrl(string target)
    {
        if (!Uri.TryCreate("http://127.0.0.1" + target, UriKind.Absolute, out var localUri))
            return null;
        if (!string.Equals(GetQueryParameter(localUri, "k"), _routeToken, StringComparison.Ordinal))
            return null;
        if (string.Equals(localUri.AbsolutePath, "/master.m3u8", StringComparison.OrdinalIgnoreCase))
            return _remoteManifestUri.AbsoluteUri;
        if (!string.Equals(localUri.AbsolutePath, "/resource", StringComparison.OrdinalIgnoreCase))
            return null;

        var url = GetQueryParameter(localUri, "url");
        return Uri.TryCreate(url, UriKind.Absolute, out var remoteUri) && IsSupportedRemoteUri(remoteUri)
            ? remoteUri.AbsoluteUri
            : null;
    }

    private string RewritePlaylist(string playlist, Uri playlistUri)
    {
        var builder = new StringBuilder(playlist.Length + 256);
        foreach (var rawLine in playlist.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                builder.AppendLine();
            }
            else if (line[0] == '#')
            {
                builder.AppendLine(PlaylistUriRegex.Replace(
                    line,
                    match => $"URI=\"{CreateLocalResourceUrl(match.Groups["uri"].Value, playlistUri)}\""));
            }
            else
            {
                builder.AppendLine(CreateLocalResourceUrl(line, playlistUri));
            }
        }
        return builder.ToString();
    }

    private string CreateLocalResourceUrl(string value, Uri playlistUri)
    {
        value = value.Trim();
        Uri remote;
        try
        {
            remote = Uri.TryCreate(value, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri(playlistUri, value);
        }
        catch (UriFormatException)
        {
            return value;
        }

        if (!IsSupportedRemoteUri(remote))
            return value;

        // Integrated manifests often copy their query string into every child
        // URI. Keep user credentials out of the loopback playlist entirely;
        // ApplyCurrentAuthentication adds the latest token only at fetch time.
        if (_authenticationOrigin is not null && HasSameOrigin(remote, _authenticationOrigin))
        {
            remote = RemoveQueryParameter(remote, "token");
        }
        else if (!IsSignedNodeUrl(remote))
        {
            // Silo's distributed node credential is carried in /stream/*/{token},
            // never in a cross-origin ?token query. Strip every such query value,
            // including a token from before an account refresh, rather than risk
            // disclosing a user credential to a playlist-controlled origin.
            remote = RemoveQueryParameter(remote, "token");
        }

        return "/resource?k=" + _routeToken + "&url=" + Uri.EscapeDataString(remote.AbsoluteUri);
    }

    private string ApplyCurrentAuthentication(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !IsSupportedRemoteUri(uri) ||
            IsSignedNodeUrl(uri) ||
            _authenticationOrigin is null ||
            !HasSameOrigin(uri, _authenticationOrigin))
            return url;

        var token = _accessTokenProvider();
        if (string.IsNullOrWhiteSpace(token))
            return url;

        var queryParts = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !QueryPartHasName(part, "token"))
            .ToList();
        queryParts.Add("token=" + Uri.EscapeDataString(token));
        var builder = new UriBuilder(uri) { Query = string.Join("&", queryParts) };
        return builder.Uri.AbsoluteUri;
    }

    private static bool IsSignedNodeUrl(Uri uri) =>
        uri.AbsolutePath.Contains("/stream/direct/", StringComparison.OrdinalIgnoreCase) ||
        uri.AbsolutePath.Contains("/stream/remux/", StringComparison.OrdinalIgnoreCase) ||
        uri.AbsolutePath.Contains("/stream/transcode/", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedRemoteUri(Uri uri) =>
        (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
        string.IsNullOrEmpty(uri.UserInfo);

    private static bool HasSameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static string? GetQueryParameter(Uri uri, string name)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length != 2)
                continue;

            try
            {
                if (string.Equals(Uri.UnescapeDataString(pieces[0]), name, StringComparison.Ordinal))
                    return Uri.UnescapeDataString(pieces[1]);
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        return null;
    }

    private static Uri RemoveQueryParameter(Uri uri, string name)
    {
        var retainedParts = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !QueryPartHasName(part, name));
        var builder = new UriBuilder(uri) { Query = string.Join("&", retainedParts) };
        return builder.Uri;
    }

    private static bool QueryPartHasName(string part, string name)
    {
        var encodedName = part.Split('=', 2)[0];
        try
        {
            return string.Equals(
                Uri.UnescapeDataString(encodedName),
                name,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static Dictionary<string, string> GetForwardHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>();
        if (response.Content.Headers.ContentRange != null)
            headers["Content-Range"] = response.Content.Headers.ContentRange.ToString();
        if (response.Headers.AcceptRanges.Count > 0)
            headers["Accept-Ranges"] = string.Join(", ", response.Headers.AcceptRanges);
        return headers;
    }

    private static bool ShouldRetryStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.NotFound or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static TimeSpan GetStatusRetryDelay(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.NotFound
            ? TimeSpan.FromSeconds(3)
            : TimeSpan.FromSeconds(1);

    private static bool IsPlaylist(string url, string contentType) =>
        url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ||
        contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase);

    private static async Task<ProxyRequest?> ReadRequestAsync(StreamReader reader, CancellationToken ct)
    {
        var requestLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine) || requestLine.Length > 8192)
            return null;

        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var totalLength = requestLine.Length;
        for (var count = 0; count < 100; count++)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null)
                return null;
            totalLength += line.Length;
            if (totalLength > 64 * 1024)
                return null;
            if (line.Length == 0)
                return new ProxyRequest(parts[0], parts[1], headers);

            var separator = line.IndexOf(':');
            if (separator > 0)
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return null;
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int code,
        string reason,
        string? contentType,
        byte[]? body,
        bool headOnly,
        CancellationToken ct)
    {
        await WriteResponseHeaderAsync(
            stream,
            code,
            reason,
            contentType,
            body?.Length,
            null,
            ct).ConfigureAwait(false);
        if (!headOnly && body != null)
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    private static async Task WriteResponseHeaderAsync(
        NetworkStream stream,
        int code,
        string reason,
        string? contentType,
        long? contentLength,
        IReadOnlyDictionary<string, string>? extraHeaders,
        CancellationToken ct)
    {
        var builder = new StringBuilder();
        builder.Append($"HTTP/1.1 {code} {reason}\r\n");
        if (contentType != null) builder.Append($"Content-Type: {contentType}\r\n");
        if (contentLength.HasValue) builder.Append($"Content-Length: {contentLength.Value}\r\n");
        if (extraHeaders != null)
        {
            foreach (var (name, value) in extraHeaders)
                builder.Append($"{name}: {value}\r\n");
        }
        builder.Append("Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(builder.ToString()), ct).ConfigureAwait(false);
    }

    private static void Log(string message) => LocalLog.AppendLine("hls_proxy.txt", message);

    private async Task ObserveClientAsync(int taskId, Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // HandleClientAsync logs unexpected request failures itself.
        }
        finally
        {
            _clientTasks.TryRemove(taskId, out _);
        }
    }

    private async Task CleanupAsync(Task? acceptLoopTask, CancellationTokenSource? cts)
    {
        if (acceptLoopTask is not null)
        {
            try { await acceptLoopTask.ConfigureAwait(false); }
            catch { }
        }

        var tasks = _clientTasks.Values.ToArray();
        if (tasks.Length > 0)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { }
        }
        _clientTasks.Clear();

        _http.Dispose();
        cts?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _cleanupTask = CleanupAsync(_acceptLoopTask, _cts);
        _cts = null;
        _acceptLoopTask = null;
    }

    private sealed record ProxyRequest(
        string Method,
        string Target,
        Dictionary<string, string> Headers);
}
