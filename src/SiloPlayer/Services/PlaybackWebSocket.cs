using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

/// <summary>
/// WebSocket client for the Silo playback real-time control protocol.
/// Connects to /playback/sessions/{session_id}/control/ws, sends hello, receives commands,
/// responds with ack + result.
/// </summary>
public sealed class PlaybackWebSocket : IDisposable
{
    private readonly string _baseUrl;
    private readonly string _sessionId;
    private readonly Func<string?> _tokenProvider;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly HashSet<string> _seenCommandIds = new(StringComparer.Ordinal);
    private readonly object _seenCommandGate = new();
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
    ];

    private static readonly string[] SupportedCommands =
    [
        "pause", "unpause", "play_pause", "seek", "set_volume",
        "stop", "terminate", "display_message",
        "server_restarting", "server_shutting_down"
    ];

    /// <summary>Fired when a command is received from the server.</summary>
    public event Func<WebSocketCommand, Task<CommandResult>>? CommandReceived;
    public event Action<PlaybackRealtimeEvent>? EventReceived;

    public PlaybackWebSocket(string baseUrl, string sessionId, string? token)
        : this(baseUrl, sessionId, () => token)
    {
    }

    public PlaybackWebSocket(string baseUrl, string sessionId, Func<string?> tokenProvider)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _sessionId = sessionId;
        _tokenProvider = tokenProvider;
    }

    public async Task ConnectAsync()
    {
        Disconnect();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            var connected = await ConnectOnceAsync(ct);
            if (ct.IsCancellationRequested) break;
            if (connected) attempt = 0;

            var delay = ReconnectDelays[Math.Min(attempt, ReconnectDelays.Length - 1)];
            attempt++;
            Log($"Reconnecting in {delay.TotalSeconds:0}s");
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    private async Task<bool> ConnectOnceAsync(CancellationToken ct)
    {
        var wsUrl = _baseUrl.Replace("https://", "wss://").Replace("http://", "ws://");
        wsUrl += $"/api/v1/playback/sessions/{_sessionId}/control/ws";
        // Pass token as query param (matching web player) — CDN may strip Auth headers on WebSocket upgrades
        wsUrl = UrlHelper.AppendToken(wsUrl, _tokenProvider());

        _ws = new ClientWebSocket();

        try
        {
            var logUrl = wsUrl.Contains('?') ? wsUrl[..wsUrl.IndexOf('?')] : wsUrl;
            Log($"Connecting to: {logUrl}");
            await _ws.ConnectAsync(new Uri(wsUrl), ct);
            Log($"Connected successfully");

            // Send hello (use dictionaries — anonymous types break with .NET trimmer)
            await SendJsonAsync(new Dictionary<string, object>
            {
                ["type"] = "hello",
                ["session_id"] = _sessionId,
                ["client"] = new Dictionary<string, object> { ["name"] = "silo-desktop", ["version"] = "1" },
                ["capabilities"] = new Dictionary<string, object> { ["commands"] = SupportedCommands }
            });

            lock (_seenCommandGate) _seenCommandIds.Clear();
            await ReceiveLoop(ct);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Connect failed: {ex.Message}");
            return false;
        }
        finally
        {
            var socket = _ws;
            _ws = null;
            try { socket?.Abort(); } catch { }
            socket?.Dispose();
        }
    }

    public void Disconnect()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        var socket = Interlocked.Exchange(ref _ws, null);
        try
        {
            if (socket?.State is WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.Connecting)
                socket.Abort();
        }
        catch { }
        socket?.Dispose();
        cts?.Dispose();
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _ws.ReceiveAsync(buffer, ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Log("Server closed connection");
                    break;
                }

                messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var message = messageBuffer.ToString();
                    messageBuffer.Clear();
                    await HandleMessage(message);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException ex)
            {
                Log($"WebSocket error: {ex.Message}");
                break;
            }
        }

        Log("Receive loop ended");
    }

    private async Task HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString();
            if (type == "event")
            {
                var eventName = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var sessionId = root.TryGetProperty("session_id", out var sessionEl) ? sessionEl.GetString() ?? "" : "";
                var eventPayload = root.TryGetProperty("payload", out var eventPayloadEl)
                    ? eventPayloadEl.Clone()
                    : default;

                if (!string.IsNullOrEmpty(eventName))
                {
                    Log($"Event received: {eventName}");
                    try
                    {
                        EventReceived?.Invoke(new PlaybackRealtimeEvent
                        {
                            SessionId = sessionId,
                            Name = eventName,
                            Payload = eventPayload
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"EventReceived handler error: {ex.Message}");
                    }
                }

                return;
            }

            if (type != "command") return;

            var commandId = root.GetProperty("command_id").GetString() ?? "";
            var name = root.GetProperty("name").GetString() ?? "";
            lock (_seenCommandGate)
            {
                if (!_seenCommandIds.Add(commandId)) return;
            }

            // Parse payload
            Dictionary<string, JsonElement>? payload = null;
            if (root.TryGetProperty("payload", out var payloadEl) && payloadEl.ValueKind == JsonValueKind.Object)
            {
                payload = new Dictionary<string, JsonElement>();
                foreach (var prop in payloadEl.EnumerateObject())
                    payload[prop.Name] = prop.Value.Clone();
            }

            var command = new WebSocketCommand
            {
                CommandId = commandId,
                Name = name,
                Payload = payload
            };

            Log($"Command received: {name} (id={commandId})");

            // Send ack immediately
            await SendJsonAsync(new Dictionary<string, object>
            {
                ["type"] = "ack",
                ["command_id"] = commandId,
                ["session_id"] = _sessionId,
                ["status"] = "accepted"
            });

            // Execute command
            var result = new CommandResult { Status = "completed" };
            if (CommandReceived != null)
            {
                try
                {
                    result = await CommandReceived(command);
                }
                catch (Exception ex)
                {
                    result = new CommandResult { Status = "rejected", Error = ex.Message };
                }
            }

            // Send result
            var resultMsg = new Dictionary<string, string>
            {
                ["type"] = "result",
                ["command_id"] = commandId,
                ["session_id"] = _sessionId,
                ["status"] = result.Status
            };
            if (result.Error != null)
                resultMsg["error"] = result.Error;

            await SendJsonAsync(resultMsg);
        }
        catch (Exception ex)
        {
            Log($"HandleMessage error: {ex.Message}");
        }
    }

    private async Task SendJsonAsync(object obj)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var json = JsonSerializer.Serialize(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
    }

    private static void Log(string msg)
    {
        LocalLog.AppendLine("websocket.txt", msg);
    }

    public void Dispose()
    {
        Disconnect();
        _cts?.Dispose();
    }
}

public class WebSocketCommand
{
    public string CommandId { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, JsonElement>? Payload { get; set; }

    public string? GetString(string key)
    {
        if (Payload == null) return null;
        // Try multiple key variants
        foreach (var k in new[] { key, key.Replace("_", "") })
        {
            if (Payload.TryGetValue(k, out var val) && val.ValueKind == JsonValueKind.String)
                return val.GetString();
        }
        return null;
    }

    public double? GetNumber(params string[] keys)
    {
        if (Payload == null) return null;
        foreach (var key in keys)
        {
            if (Payload.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.Number)
                return val.GetDouble();
        }
        return null;
    }
}

public class PlaybackRealtimeEvent
{
    public string SessionId { get; set; } = "";
    public string Name { get; set; } = "";
    public JsonElement Payload { get; set; }
}

public class CommandResult
{
    public string Status { get; set; } = "completed";
    public string? Error { get; set; }
}
