using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ContinuumPlayer.Services;

/// <summary>
/// Local HTTP proxy between mpv and the Continuum transcode server.
/// Uses raw TcpListener — zero external dependencies.
/// Retries 404s for up to 45 seconds per segment (matching HLS.js behavior).
/// </summary>
public sealed class HlsProxy : IDisposable
{
    private readonly string _remoteManifestUrl;
    private readonly string? _token;
    private readonly HttpClient _http;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _baseSegmentUrl = "";
    private int _port;

    public HlsProxy(string remoteManifestUrl, string? token)
    {
        _remoteManifestUrl = remoteManifestUrl;
        _token = token;
        // Manifest fetch uses auth; segment fetch does NOT (session UUID is the auth)
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public string Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        var lastSlash = _remoteManifestUrl.LastIndexOf('/');
        _baseSegmentUrl = lastSlash > 0 ? _remoteManifestUrl[..(lastSlash + 1)] : _remoteManifestUrl;
        var qIdx = _baseSegmentUrl.IndexOf('?');
        if (qIdx > 0) _baseSegmentUrl = _baseSegmentUrl[..qIdx];

        Task.Run(() => AcceptLoop(_cts.Token));
        Log($"Started on port {_port}");
        return $"http://127.0.0.1:{_port}/master.m3u8";
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClient(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex) { Log($"Accept error: {ex.Message}"); }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 60000;
                var stream = client.GetStream();

                var requestLine = await ReadLineAsync(stream, ct);
                if (requestLine == null) return;

                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return;
                var path = parts[1];

                // Consume remaining headers
                while (true)
                {
                    var line = await ReadLineAsync(stream, ct);
                    if (line == null || line.Length == 0) break;
                }

                if (path.Contains("master.m3u8"))
                    await ServeManifest(stream, ct);
                else
                    await ServeSegment(stream, path, ct);
            }
        }
        catch (Exception ex) { Log($"Client error: {ex.Message}"); }
    }

    private async Task ServeManifest(NetworkStream stream, CancellationToken ct)
    {
        // Fetch manifest with token as QUERY PARAM (not Bearer header).
        // The server embeds rawQuery into every segment URL in the manifest.
        // This way segment URLs automatically include ?token=XXX — matching
        // how HLS.js in the web player works.
        var manifestUrlWithToken = _remoteManifestUrl;
        if (_token != null)
            manifestUrlWithToken += (manifestUrlWithToken.Contains('?') ? "&" : "?") + $"token={Uri.EscapeDataString(_token)}";
        Log("Fetching remote manifest...");
        var manifest = await _http.GetStringAsync(manifestUrlWithToken, ct);
        Log($"Got manifest: {manifest.Length} chars");

        // Rewrite relative segment URLs to absolute proxy URLs
        // Lines like "segment/seg_00123.ts?token=..." become "/segment/seg_00123.ts?token=..."
        var sb = new StringBuilder();
        foreach (var rawLine in manifest.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("segment/"))
            {
                sb.AppendLine("/" + line);
            }
            else if (line.Contains("URI=\"segment/"))
            {
                sb.AppendLine(line.Replace("URI=\"segment/", "URI=\"/segment/"));
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/vnd.apple.mpegurl\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
        await stream.WriteAsync(body, ct);
        Log("Manifest served");
    }

    private async Task ServeSegment(NetworkStream stream, string path, CancellationToken ct)
    {
        var segPath = path.TrimStart('/');
        var remoteUrl = _baseSegmentUrl + segPath;
        // Segment URLs already have ?token=XXX embedded from the manifest
        // (server copies rawQuery from manifest request to segment URLs).
        // Don't add a second token.

        // Log full URL to a separate file for debugging
        try { File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContinuumPlayer", "last_segment_url.txt"), remoteUrl); } catch { }
        Log($"Requesting segment (full URL in last_segment_url.txt)");

        byte[]? data = null;
        string? contentType = null;

        for (int attempt = 0; attempt < 15 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                // NO Authorization header — token is in the query param (matching HLS.js).
                // CDN blocks requests with Authorization header on segment paths.
                using var segReq = new HttpRequestMessage(HttpMethod.Get, remoteUrl);
                using var response = await _http.SendAsync(segReq, ct);

                if (response.IsSuccessStatusCode)
                {
                    data = await response.Content.ReadAsByteArrayAsync(ct);
                    contentType = response.Content.Headers.ContentType?.MediaType;
                    Log($"Served {segPath}: {data.Length} bytes");
                    break;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    if (attempt == 0)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct);
                        Log($"404 on {segPath}: {body}");
                    }
                    await Task.Delay(3000, ct);
                    continue;
                }

                Log($"HTTP {(int)response.StatusCode} on {segPath}");
                await WriteResponse(stream, (int)response.StatusCode, "Error", null, null, ct);
                return;
            }
            catch (TaskCanceledException) { break; }
            catch (HttpRequestException ex)
            {
                Log($"HTTP error on {segPath}: {ex.Message}, retry {attempt + 1}");
                await Task.Delay(2000, ct);
            }
        }

        if (data != null)
            await WriteResponse(stream, 200, "OK", contentType ?? "video/mp2t", data, ct);
        else
            await WriteResponse(stream, 404, "Not Found", null, null, ct);
    }

    private static async Task WriteResponse(NetworkStream stream, int code, string reason, string? contentType, byte[]? body, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {reason}\r\n");
        if (contentType != null) sb.Append($"Content-Type: {contentType}\r\n");
        sb.Append($"Content-Length: {body?.Length ?? 0}\r\n");
        sb.Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);
        if (body != null) await stream.WriteAsync(body, ct);
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (!ct.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buf, ct);
            if (read == 0) return null;
            if (buf[0] == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append((char)buf[0]);
            if (sb.Length > 8192) return null;
        }
        return null;
    }

    private static void Log(string msg)
    {
        try
        {
            var p = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", "hls_proxy.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.AppendAllText(p, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
        _cts?.Dispose();
    }
}
