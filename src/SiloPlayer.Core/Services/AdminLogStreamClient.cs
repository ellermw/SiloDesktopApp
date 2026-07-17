using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Dedicated WebSocket client for the admin log stream (webui parity with
/// useAdminLogStream.ts). Connects to <c>/api/v1/admin/logs/ws?stream=app|audit&amp;&lt;filters&gt;</c>
/// and raises events for snapshot / append / error messages. Each instance
/// owns one WebSocket; callers should open a new instance when filters change
/// and dispose/stop the previous one.
/// </summary>
public sealed class AdminLogStreamClient : IDisposable
{
    public enum Stream { App, Audit }
    public enum ConnectionState { Disconnected, Connecting, Live }

    private readonly string _baseWsUrl;
    private readonly Func<string?> _accessTokenProvider;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _readCts;

    /// <summary>Snapshot received — rows should be replaced.</summary>
    public event Action<List<OperationalLogEntry>, string?>? AppSnapshotReceived;
    public event Action<List<AuditLogEntry>, string?>? AuditSnapshotReceived;
    /// <summary>Single new entry to prepend to the list.</summary>
    public event Action<OperationalLogEntry>? AppEntryAppended;
    public event Action<AuditLogEntry>? AuditEntryAppended;
    public event Action<string>? ErrorReceived;
    public event Action<ConnectionState>? StateChanged;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>
    /// Constructs a new stream client. <paramref name="httpBaseUrl"/> is the
    /// HTTP base URL (e.g. <c>https://continuum.taverncdn.com</c>); this
    /// class converts it to the equivalent WebSocket scheme.
    /// </summary>
    public AdminLogStreamClient(string httpBaseUrl, string accessToken)
        : this(httpBaseUrl, () => accessToken)
    {
    }

    public AdminLogStreamClient(string httpBaseUrl, Func<string?> accessTokenProvider)
    {
        if (string.IsNullOrEmpty(httpBaseUrl))
            throw new ArgumentException("httpBaseUrl required", nameof(httpBaseUrl));
        var wsScheme = httpBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "wss://"
                     : httpBaseUrl.StartsWith("http://",  StringComparison.OrdinalIgnoreCase) ? "ws://"
                     : throw new ArgumentException("httpBaseUrl must be http or https", nameof(httpBaseUrl));
        _baseWsUrl = wsScheme + httpBaseUrl[(httpBaseUrl.IndexOf("://") + 3)..];
        _accessTokenProvider = accessTokenProvider ?? throw new ArgumentNullException(nameof(accessTokenProvider));
    }

    /// <summary>
    /// Opens a new WebSocket connection with the given stream + filter map.
    /// Any previous connection is closed first.
    /// </summary>
    public async Task StartAsync(Stream stream, IReadOnlyDictionary<string, string> filters, CancellationToken ct = default)
    {
        await StopAsync().ConfigureAwait(false);

        var streamParam = stream == Stream.App ? "app" : "audit";
        var query = new StringBuilder();
        query.Append("stream=").Append(streamParam);
        foreach (var (k, v) in filters)
        {
            if (string.IsNullOrEmpty(v)) continue;
            query.Append('&').Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v));
        }
        var accessToken = _accessTokenProvider();
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("No access token is available for the admin log stream.");
        query.Append("&token=").Append(Uri.EscapeDataString(accessToken));
        var url = $"{_baseWsUrl}/api/v1/admin/logs/ws?{query}";

        _ws = new ClientWebSocket();
        _readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        SetState(ConnectionState.Connecting);

        try
        {
            await _ws.ConnectAsync(new Uri(url), _readCts.Token).ConfigureAwait(false);
            SetState(ConnectionState.Live);
            _ = Task.Run(() => ReadLoopAsync(stream, _ws, _readCts.Token));
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Disconnected);
            ErrorReceived?.Invoke($"Connect failed: {ex.Message}");
        }
    }

    public Task StopAsync()
    {
        var ws = _ws;
        var cts = _readCts;
        _ws = null;
        _readCts = null;
        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }
        if (ws != null)
        {
            try
            {
                // Navigation and filter changes must never wait for a remote close
                // handshake. Cancel the read loop and abort the private socket.
                ws.Abort();
            }
            catch { }
            ws.Dispose();
        }
        SetState(ConnectionState.Disconnected);
        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync(Stream stream, ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        var accum = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                accum.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    accum.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage && !ct.IsCancellationRequested);

                if (ct.IsCancellationRequested) return;

                // Dispatch the assembled message.
                try
                {
                    accum.Position = 0;
                    DispatchMessage(stream, accum);
                }
                catch { /* malformed — skip */ }
            }
        }
        catch (OperationCanceledException) { /* normal teardown */ }
        catch (Exception ex)
        {
            ErrorReceived?.Invoke($"Stream error: {ex.Message}");
        }
        finally
        {
            SetState(ConnectionState.Disconnected);
        }
    }

    private void DispatchMessage(Stream stream, System.IO.Stream data)
    {
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return;

        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String) return;
        var type = typeEl.GetString();

        if (type == "snapshot")
        {
            var nextCursor = root.TryGetProperty("next_cursor", out var nc) && nc.ValueKind == JsonValueKind.String
                ? nc.GetString() : null;
            if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array) return;

            if (stream == Stream.App)
            {
                var list = new List<OperationalLogEntry>();
                foreach (var e in entries.EnumerateArray())
                {
                    var parsed = e.Deserialize<OperationalLogEntry>(JsonOptions);
                    if (parsed != null) list.Add(parsed);
                }
                AppSnapshotReceived?.Invoke(list, nextCursor);
            }
            else
            {
                var list = new List<AuditLogEntry>();
                foreach (var e in entries.EnumerateArray())
                {
                    var parsed = e.Deserialize<AuditLogEntry>(JsonOptions);
                    if (parsed != null) list.Add(parsed);
                }
                AuditSnapshotReceived?.Invoke(list, nextCursor);
            }
        }
        else if (type == "append")
        {
            if (!root.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.Object) return;
            if (stream == Stream.App)
            {
                var parsed = entry.Deserialize<OperationalLogEntry>(JsonOptions);
                if (parsed != null) AppEntryAppended?.Invoke(parsed);
            }
            else
            {
                var parsed = entry.Deserialize<AuditLogEntry>(JsonOptions);
                if (parsed != null) AuditEntryAppended?.Invoke(parsed);
            }
        }
        else if (type == "error")
        {
            var msg = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString() : "Log stream error";
            ErrorReceived?.Invoke(msg ?? "Log stream error");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private void SetState(ConnectionState s)
    {
        if (State == s) return;
        State = s;
        StateChanged?.Invoke(s);
    }

    public void Dispose()
    {
        _ = StopAsync();
    }
}
