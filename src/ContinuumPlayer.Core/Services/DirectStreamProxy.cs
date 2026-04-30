using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ContinuumPlayer.Core.Services;

public sealed class DirectStreamProxy : IDisposable
{
    private const int MaxRequestHeaderBytes = 32 * 1024;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly DirectStreamRelay _relay;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Action<string> _log;
    private Task? _acceptLoopTask;
    private string? _localUrl;
    private bool _disposed;

    public DirectStreamProxy(
        string remoteStreamUrl,
        Func<string?> accessTokenProvider,
        Action<string>? log = null,
        HttpClient? httpClient = null)
    {
        if (string.IsNullOrWhiteSpace(remoteStreamUrl))
            throw new ArgumentException("Remote stream URL is required.", nameof(remoteStreamUrl));

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _log = log ?? AppendDefaultLog;
        _httpClient = httpClient ?? CreateHttpClient();
        _ownsHttpClient = httpClient is null;

        var remoteUri = RemoveTokenQuery(new Uri(remoteStreamUrl, UriKind.Absolute));
        _relay = new DirectStreamRelay(_httpClient, remoteUri, accessTokenProvider, _log);
    }

    public string Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_localUrl is not null)
            return _localUrl;

        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _localUrl = $"http://127.0.0.1:{port}/stream";
        _acceptLoopTask = Task.Run(AcceptLoopAsync);
        _log($"Direct stream proxy listening on {_localUrl}");
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

        _cts.Dispose();

        if (_ownsHttpClient)
            _httpClient.Dispose();

        _log("Direct stream proxy stopped");
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client, _cts.Token), _cts.Token);
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
                return;

            if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(request.Path, "/stream", StringComparison.OrdinalIgnoreCase))
            {
                await WriteSimpleResponseAsync(stream, HttpStatusCode.NotFound, "Not Found", cancellationToken);
                return;
            }

            request.Headers.TryGetValue("range", out var rangeHeader);

            await _relay.RelayAsync(
                stream,
                rangeHeader,
                (headers, ct) => WriteHeadersAsync(stream, headers, ct),
                cancellationToken);
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
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
            path = path[..queryIndex];

        return new ProxyRequest(requestParts[0], path, headers);
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

        builder.Append("Accept-Ranges: bytes\r\n");
        builder.Append("\r\n");

        var bytes = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static Task WriteSimpleResponseAsync(
        Stream stream,
        HttpStatusCode statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(message);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)statusCode} {message}\r\nConnection: close\r\nContent-Length: {body.Length}\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n");

        return WriteSimpleResponseAsync(stream, header, body, cancellationToken);
    }

    private static async Task WriteSimpleResponseAsync(
        Stream stream,
        byte[] header,
        byte[] body,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(header, cancellationToken);
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
            .Where(part => !part.StartsWith("token=", StringComparison.OrdinalIgnoreCase));

        builder.Query = string.Join("&", parts);
        return builder.Uri;
    }

    private static void AppendDefaultLog(string message)
    {
        LocalLog.AppendLine("direct_stream_proxy.txt", message);
    }

    private sealed record ProxyRequest(string Method, string Path, Dictionary<string, string> Headers);
}
