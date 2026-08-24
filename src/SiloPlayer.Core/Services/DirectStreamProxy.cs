using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SiloPlayer.Core.Services;

public sealed class DirectStreamProxy : IDisposable
{
    private const int MaxRequestHeaderBytes = 32 * 1024;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly DirectStreamRelay _relay;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly bool _supportsRanges;
    private readonly Action<string> _log;
    private readonly string _routeToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private readonly ConcurrentDictionary<int, Task> _clientTasks = new();
    private Task? _acceptLoopTask;
    private Task? _cleanupTask;
    private string? _localUrl;
    private int _nextClientTaskId;
    private bool _disposed;

    public DirectStreamProxy(
        string remoteStreamUrl,
        Func<string?> accessTokenProvider,
        Action<string>? log = null,
        HttpClient? httpClient = null,
        bool supportsRanges = true)
    {
        if (string.IsNullOrWhiteSpace(remoteStreamUrl))
            throw new ArgumentException("Remote stream URL is required.", nameof(remoteStreamUrl));
        ArgumentNullException.ThrowIfNull(accessTokenProvider);

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _log = log ?? AppendDefaultLog;
        _httpClient = httpClient ?? CreateHttpClient();
        _ownsHttpClient = httpClient is null;
        _supportsRanges = supportsRanges;

        var suppliedRemoteUri = new Uri(remoteStreamUrl, UriKind.Absolute);
        var isSignedNodeUrl = IsSignedNodeUrl(suppliedRemoteUri);
        var remoteUri = isSignedNodeUrl
            ? suppliedRemoteUri
            : RemoveTokenQuery(suppliedRemoteUri);
        _relay = new DirectStreamRelay(
            _httpClient,
            remoteUri,
            isSignedNodeUrl ? static () => null : accessTokenProvider,
            _log,
            supportsRanges: supportsRanges);
    }

    public DirectStreamProxy(
        string remoteStreamUrl,
        Func<string?> accessTokenProvider,
        bool supportsRanges)
        : this(
            remoteStreamUrl,
            accessTokenProvider,
            log: null,
            httpClient: null,
            supportsRanges: supportsRanges)
    {
    }

    public string Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_localUrl is not null)
            return _localUrl;

        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _localUrl = $"http://127.0.0.1:{port}/stream?k={_routeToken}";
        _acceptLoopTask = Task.Run(AcceptLoopAsync);
        _log($"Direct stream proxy listening on port {port}");
        return _localUrl;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts.Cancel();

        try
        {
            _listener.Stop();
        }
        catch
        {
        }

        // Do not dispose the CTS or HttpClient while accepted requests are still
        // unwinding. Cleanup is deliberately asynchronous so closing the player
        // cannot block the UI thread on a stalled network operation.
        _cleanupTask = CleanupAsync(_acceptLoopTask);

        _log("Direct stream proxy stopped");
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                var taskId = Interlocked.Increment(ref _nextClientTaskId);
                var task = HandleClientAsync(client, _cts.Token);
                _clientTasks[taskId] = task;
                _ = ObserveClientAsync(taskId, task);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException ex) when (_cts.IsCancellationRequested)
            {
                _log($"Direct stream proxy accept stopped: {ex.SocketErrorCode}");
                break;
            }
            catch (Exception ex)
            {
                _log($"Direct stream proxy accept failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;

        try
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, cancellationToken);

            if (request is null)
            {
                await WriteSimpleResponseAsync(
                    stream,
                    HttpStatusCode.BadRequest,
                    "Bad Request",
                    cancellationToken);
                return;
            }

            var isGet = string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase);
            var isHead = string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
            if (!isGet && !isHead)
            {
                await WriteSimpleResponseAsync(
                    stream,
                    HttpStatusCode.MethodNotAllowed,
                    "Method Not Allowed",
                    cancellationToken,
                    allow: "GET, HEAD");
                return;
            }

            if (!string.Equals(request.Path, "/stream", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetQueryParameter(request.Query, "k"), _routeToken, StringComparison.Ordinal))
            {
                await WriteSimpleResponseAsync(
                    stream,
                    HttpStatusCode.NotFound,
                    "Not Found",
                    cancellationToken,
                    headOnly: isHead);
                return;
            }

            string? rangeHeader = null;
            if (_supportsRanges)
                request.Headers.TryGetValue("range", out rangeHeader);

            var responseStarted = false;
            try
            {
                await _relay.RelayAsync(
                    stream,
                    rangeHeader,
                    (headers, ct) =>
                    {
                        responseStarted = true;
                        return WriteHeadersAsync(stream, headers, ct);
                    },
                    cancellationToken,
                    headOnly: isHead);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log($"Direct stream proxy relay failed: {ex.GetType().Name}: {ex.Message}");
                if (!responseStarted)
                {
                    await WriteSimpleResponseAsync(
                        stream,
                        HttpStatusCode.BadGateway,
                        "Upstream media stream unavailable",
                        cancellationToken,
                        headOnly: isHead);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            // mpv closes and reopens local connections during seeks and stop operations.
        }
        catch (Exception ex)
        {
            _log($"Direct stream proxy request failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

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

    private async Task CleanupAsync(Task? acceptLoopTask)
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

        if (_ownsHttpClient)
            _httpClient.Dispose();
        _cts.Dispose();
    }

    private static async Task<ProxyRequest?> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var readBuffer = new byte[4096];

        while (buffer.Length < MaxRequestHeaderBytes)
        {
            var read = await stream.ReadAsync(readBuffer, cancellationToken);
            if (read == 0)
                return null;

            buffer.Write(readBuffer, 0, read);

            if (ContainsHeaderTerminator(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)))
                break;
        }

        var requestText = Encoding.ASCII.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        var headerEnd = requestText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd < 0)
            return null;

        var lines = requestText[..headerEnd].Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
            return null;

        var requestParts = lines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (requestParts.Length < 2)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var separator = lines[i].IndexOf(':');
            if (separator <= 0)
                continue;

            headers[lines[i][..separator].Trim()] = lines[i][(separator + 1)..].Trim();
        }

        var path = requestParts[1];
        var query = "";
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            query = path[(queryIndex + 1)..];
            path = path[..queryIndex];
        }

        return new ProxyRequest(requestParts[0], path, query, headers);
    }

    private static bool ContainsHeaderTerminator(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            if (bytes[i] == (byte)'\r' &&
                bytes[i + 1] == (byte)'\n' &&
                bytes[i + 2] == (byte)'\r' &&
                bytes[i + 3] == (byte)'\n')
            {
                return true;
            }
        }

        return false;
    }

    private static async Task WriteHeadersAsync(
        Stream stream,
        DirectStreamRelayHeaders headers,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ")
            .Append((int)headers.StatusCode)
            .Append(' ')
            .Append(string.IsNullOrWhiteSpace(headers.ReasonPhrase) ? headers.StatusCode : headers.ReasonPhrase)
            .Append("\r\n");
        builder.Append("Connection: close\r\n");

        if (!string.IsNullOrWhiteSpace(headers.ContentType))
            builder.Append("Content-Type: ").Append(headers.ContentType).Append("\r\n");
        if (headers.ContentLength.HasValue)
            builder.Append("Content-Length: ").Append(headers.ContentLength.Value).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(headers.ContentRange))
            builder.Append("Content-Range: ").Append(headers.ContentRange).Append("\r\n");

        if (headers.AcceptRanges)
            builder.Append("Accept-Ranges: bytes\r\n");
        builder.Append("\r\n");

        var bytes = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static Task WriteSimpleResponseAsync(
        Stream stream,
        HttpStatusCode statusCode,
        string message,
        CancellationToken cancellationToken,
        bool headOnly = false,
        string? allow = null)
    {
        var body = Encoding.UTF8.GetBytes(message);
        var allowHeader = string.IsNullOrWhiteSpace(allow) ? "" : $"Allow: {allow}\r\n";
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)statusCode} {message}\r\nConnection: close\r\n{allowHeader}Content-Length: {body.Length}\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n");

        return WriteSimpleResponseAsync(stream, header, headOnly ? null : body, cancellationToken);
    }

    private static async Task WriteSimpleResponseAsync(
        Stream stream,
        byte[] header,
        byte[]? body,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(header, cancellationToken);
        if (body is not null)
            await stream.WriteAsync(body, cancellationToken);
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static Uri RemoveTokenQuery(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query))
            return uri;

        var builder = new UriBuilder(uri);
        var parts = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !QueryPartHasName(part, "token"));

        builder.Query = string.Join("&", parts);
        return builder.Uri;
    }

    private static string? GetQueryParameter(string query, string name)
    {
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
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

    private static bool IsSignedNodeUrl(Uri uri) =>
        uri.AbsolutePath.Contains("/stream/direct/", StringComparison.OrdinalIgnoreCase) ||
        uri.AbsolutePath.Contains("/stream/remux/", StringComparison.OrdinalIgnoreCase) ||
        uri.AbsolutePath.Contains("/stream/transcode/", StringComparison.OrdinalIgnoreCase);

    private static void AppendDefaultLog(string message)
    {
        LocalLog.AppendLine("direct_stream_proxy.txt", message);
    }

    private sealed record ProxyRequest(
        string Method,
        string Path,
        string Query,
        Dictionary<string, string> Headers);
}
