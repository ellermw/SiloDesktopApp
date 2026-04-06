using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ContinuumPlayer.Services;

/// <summary>
/// WebSocket client for the Continuum playback real-time control protocol.
/// Connects to /playback/ws/{session_id}, sends hello, receives commands,
/// responds with ack + result.
/// </summary>
public sealed class PlaybackWebSocket : IDisposable
{
    private readonly string _baseUrl;
    private readonly string _sessionId;
    private readonly string? _token;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;

    private static readonly string[] SupportedCommands =
    [
        "pause", "unpause", "play_pause", "seek", "set_volume",
        "stop", "terminate", "display_message",
        "server_restarting", "server_shutting_down"
    ];

    /// <summary>Fired when a command is received from the server.</summary>
    public event Func<WebSocketCommand, Task<CommandResult>>? CommandReceived;

    public PlaybackWebSocket(string baseUrl, string sessionId, string? token)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _sessionId = sessionId;
        _token = token;
    }

    public async Task ConnectAsync()
    {
        _cts = new CancellationTokenSource();

        var wsUrl = _baseUrl.Replace("https://", "wss://").Replace("http://", "ws://");
        wsUrl += $"/playback/ws/{_sessionId}";

        _ws = new ClientWebSocket();
        if (_token != null)
            _ws.Options.SetRequestHeader("Authorization", $"Bearer {_token}");

        try
        {
            await _ws.ConnectAsync(new Uri(wsUrl), _cts.Token);
            Log($"Connected to {wsUrl}");

            // Send hello
            await SendJsonAsync(new
            {
                type = "hello",
                session_id = _sessionId,
                client = new { name = "continuum-desktop", version = "1" },
                capabilities = new { commands = SupportedCommands }
            });

            // Start receive loop
            _ = Task.Run(() => ReceiveLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            Log($"Connect failed: {ex.Message}");
        }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        try
        {
            if (_ws?.State == WebSocketState.Open)
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None)
                    .GetAwaiter().GetResult();
        }
        catch { }
        _ws?.Dispose();
        _ws = null;
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
            if (type != "command") return;

            var commandId = root.GetProperty("command_id").GetString() ?? "";
            var name = root.GetProperty("name").GetString() ?? "";

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
            await SendJsonAsync(new
            {
                type = "ack",
                command_id = commandId,
                session_id = _sessionId,
                status = "accepted"
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
            var resultMsg = new Dictionary<string, object?>
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
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
    }

    private static void Log(string msg)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ContinuumPlayer", "websocket.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
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

public class CommandResult
{
    public string Status { get; set; } = "completed";
    public string? Error { get; set; }
}
