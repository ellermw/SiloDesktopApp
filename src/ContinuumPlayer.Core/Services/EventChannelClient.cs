using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Helpers;

namespace ContinuumPlayer.Core.Services;

/// <summary>
/// WebSocket client for the Continuum server's realtime event channel at
/// <c>/api/v1/events/ws</c>. Handles the handshake (hello → subscribe → subscribed →
/// snapshot → event stream), dispatches frames to per-channel subscribers, and
/// reconnects with exponential backoff.
///
/// See <c>docs/events-channel-spec.md</c> for the protocol details.
/// </summary>
public sealed class EventChannelClient : IDisposable
{
    private readonly ContinuumApiClient _apiClient;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly object _lock = new();

    private readonly HashSet<string> _requestedChannels = new(StringComparer.OrdinalIgnoreCase);
    private string? _connectionId;

    /// <summary>Fired after the server accepts a subscribe and sends the snapshot frame.
    /// <c>data</c> is the raw JSON element — deserialize it in the subscriber based on
    /// which channel you subscribed to.</summary>
    public event Action<string /* channel */, JsonElement /* data */>? SnapshotReceived;

    /// <summary>Fired for every live event frame. <c>eventName</c> is e.g.
    /// "history_import.updated", "sessions.replaced", etc. <c>data</c> is the event payload.</summary>
    public event Action<string /* channel */, string /* eventName */, JsonElement /* data */>? EventReceived;

    /// <summary>Fired when the server sends an error frame (protocol-level, not transport).</summary>
    public event Action<string /* code */, string /* message */>? ErrorReceived;

    /// <summary>Fired whenever the underlying WebSocket state changes. Use for UI
    /// connection indicators.</summary>
    public event Action<WebSocketState>? StateChanged;

    public EventChannelClient(ContinuumApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public WebSocketState State => _ws?.State ?? WebSocketState.None;

    /// <summary>
    /// Start the client and subscribe to the given channel(s). Safe to call more than
    /// once; calling with a new channel list triggers a reconnect with the updated
    /// subscription set.
    /// </summary>
    public void Start(params string[] channels)
    {
        lock (_lock)
        {
            _requestedChannels.Clear();
            foreach (var ch in channels) _requestedChannels.Add(ch);

            if (_runTask != null && !_runTask.IsCompleted)
            {
                // Already running — force a reconnect so the new subscription takes effect.
                Log($"Start called with new channels [{string.Join(",", channels)}]; forcing reconnect");
                _cts?.Cancel();
                try { _runTask?.Wait(500); } catch { }
            }

            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunLoop(_cts.Token));
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            try { _cts?.Cancel(); } catch { }
            try { _runTask?.Wait(1000); } catch { }
            try
            {
                if (_ws?.State == WebSocketState.Open)
                    _ = _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client stop", CancellationToken.None);
            }
            catch { }
            _ws?.Dispose();
            _ws = null;
            _runTask = null;
            _requestedChannels.Clear();
        }
    }

    private async Task RunLoop(CancellationToken ct)
    {
        var backoffMs = 1000;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunOnce(ct);
                // Normal close — reset backoff and reconnect.
                backoffMs = 1000;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log($"Connection error: {ex.Message}");
            }

            if (ct.IsCancellationRequested) return;

            // Don't spam reconnects — capped exponential backoff up to 30s.
            Log($"Reconnecting in {backoffMs}ms");
            try { await Task.Delay(backoffMs, ct); } catch { return; }
            backoffMs = Math.Min(backoffMs * 2, 30_000);
        }
    }

    private async Task ConnectAndRunOnce(CancellationToken ct)
    {
        string[] channelsSnapshot;
        lock (_lock)
        {
            channelsSnapshot = _requestedChannels.ToArray();
        }
        if (channelsSnapshot.Length == 0)
        {
            Log("No channels requested, idling");
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); } catch { }
            return;
        }

        var baseUrl = _apiClient.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new InvalidOperationException("API client base URL is not set");
        }

        var wsUrl = baseUrl.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
                           .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
                           .TrimEnd('/');
        wsUrl += "/api/v1/events/ws";
        wsUrl = UrlHelper.AppendToken(wsUrl, _apiClient.AccessToken);

        _ws = new ClientWebSocket();
        try
        {
            var logUrl = wsUrl.Contains('?') ? wsUrl[..wsUrl.IndexOf('?')] : wsUrl;
            Log($"Connecting to {logUrl} (channels: {string.Join(",", channelsSnapshot)})");
            await _ws.ConnectAsync(new Uri(wsUrl), ct);
            StateChanged?.Invoke(_ws.State);
            Log("Connected");
        }
        catch
        {
            _ws?.Dispose();
            _ws = null;
            throw;
        }

        try
        {
            await ReceiveLoop(channelsSnapshot, ct);
        }
        finally
        {
            try
            {
                if (_ws?.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch { }
            _ws?.Dispose();
            _ws = null;
            StateChanged?.Invoke(WebSocketState.Closed);
        }
    }

    private async Task ReceiveLoop(string[] channelsToSubscribe, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var messageBuffer = new StringBuilder();

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync(buffer, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WebSocketException ex)
            {
                Log($"Receive error: {ex.Message}");
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Log($"Server closed connection: {result.CloseStatusDescription}");
                return;
            }

            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;

            var json = messageBuffer.ToString();
            messageBuffer.Clear();

            await DispatchFrame(json, channelsToSubscribe, ct);
        }
    }

    private async Task DispatchFrame(string json, string[] channelsToSubscribe, CancellationToken ct)
    {
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                return;

            var type = typeEl.GetString()!;
            switch (type)
            {
                case "hello":
                {
                    if (root.TryGetProperty("connection_id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        _connectionId = idEl.GetString();
                    // Client MUST send subscribe within 5 seconds — send immediately.
                    var subscribeMsg = new Dictionary<string, object>
                    {
                        ["type"] = "subscribe",
                        ["request_id"] = Guid.NewGuid().ToString("N"),
                        ["channels"] = channelsToSubscribe,
                    };
                    await SendJsonAsync(subscribeMsg, ct);
                    Log($"Sent subscribe for channels: {string.Join(",", channelsToSubscribe)}");
                    break;
                }
                case "subscribed":
                    if (root.TryGetProperty("rejected", out var rejEl) && rejEl.ValueKind == JsonValueKind.Array && rejEl.GetArrayLength() > 0)
                    {
                        foreach (var r in rejEl.EnumerateArray())
                        {
                            var ch = r.TryGetProperty("channel", out var c) ? c.GetString() : "?";
                            var msg = r.TryGetProperty("message", out var m) ? m.GetString() : "?";
                            Log($"Subscription rejected: {ch}: {msg}");
                        }
                    }
                    Log("Subscribed successfully");
                    break;
                case "snapshot":
                    if (root.TryGetProperty("channel", out var snChEl) && root.TryGetProperty("data", out var snDataEl))
                    {
                        var ch = snChEl.GetString() ?? "";
                        SnapshotReceived?.Invoke(ch, snDataEl.Clone());
                    }
                    break;
                case "event":
                    if (root.TryGetProperty("channel", out var evChEl)
                        && root.TryGetProperty("event", out var evNameEl)
                        && root.TryGetProperty("data", out var evDataEl))
                    {
                        var ch = evChEl.GetString() ?? "";
                        var evName = evNameEl.GetString() ?? "";
                        EventReceived?.Invoke(ch, evName, evDataEl.Clone());
                    }
                    break;
                case "error":
                    var code = root.TryGetProperty("code", out var cEl) ? (cEl.GetString() ?? "") : "";
                    var errMsg = root.TryGetProperty("message", out var mEl) ? (mEl.GetString() ?? "") : "";
                    Log($"Server error: {code}: {errMsg}");
                    ErrorReceived?.Invoke(code, errMsg);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Frame dispatch error: {ex.Message}");
        }
        finally
        {
            doc?.Dispose();
        }
    }

    private async Task SendJsonAsync(object obj, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var json = JsonSerializer.Serialize(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static void Log(string msg)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", "events_channel.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
