using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Helpers;

namespace SiloPlayer.Core.Services;

/// <summary>
/// WebSocket client for the Silo server's realtime event channel at
/// <c>/api/v2/events/ws</c>. Handles the handshake (hello → subscribe → subscribed →
/// snapshot → event stream), dispatches frames to per-channel subscribers, and
/// reconnects with exponential backoff.
///
/// See <c>docs/events-channel-spec.md</c> for the protocol details.
/// </summary>
public sealed class EventChannelClient : IDisposable
{
    private readonly SiloApiClient _apiClient;
    private readonly AuthService _authService;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private readonly object _lock = new();
    private bool _reconnectSuppressed;

    // Ref-counted channel subscriptions — multiple features (settings/import,
    // admin/server-activity, watch-party, …) share a single WebSocket. Each call
    // to Subscribe increments the count; Dispose on the returned handle
    // decrements. The live subscription set is `keys where count > 0`.
    private readonly Dictionary<string, int> _channelRefs = new(StringComparer.OrdinalIgnoreCase);
    private string? _connectionId;
    private readonly object _snapshotLock = new();
    private readonly Dictionary<string, JsonElement> _latestSnapshots = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Current transport state for controls that subscribe after the
    /// shared channel has already connected.</summary>
    public WebSocketState CurrentState => _ws?.State ?? WebSocketState.Closed;

    /// <summary>
    /// Returns the most recent channel snapshot, including scan events applied
    /// after that snapshot arrived. This lets a page that attaches after the
    /// shared socket connected hydrate immediately instead of waiting for the
    /// next reconnect.
    /// </summary>
    public bool TryGetLatestSnapshot(string channel, out JsonElement snapshot)
    {
        lock (_snapshotLock)
        {
            if (_latestSnapshots.TryGetValue(channel, out var cached))
            {
                snapshot = cached.Clone();
                return true;
            }
        }

        snapshot = default;
        return false;
    }

    public EventChannelClient(SiloApiClient apiClient, AuthService authService)
    {
        _apiClient = apiClient;
        _authService = authService;
        _authService.TokenRefreshed += OnTokenRefreshed;
        _authService.LoggedOut += OnLoggedOut;
        _authService.UserChanged += OnUserChanged;
    }

    public WebSocketState State => _ws?.State ?? WebSocketState.None;

    /// <summary>
    /// Subscribe to one or more channels. Multiple concurrent subscribers can coexist
    /// (ref-counted): settings may watch <c>history_import</c> while admin watches
    /// <c>sessions,tasks,scans</c>. Dispose the returned handle to release this
    /// subscription's hold; channels with zero remaining refs drop out of the set
    /// and the client reconnects with the reduced union.
    /// </summary>
    public IDisposable Subscribe(params string[] channels)
    {
        if (channels.Length == 0) return new SubscriptionHandle(this, Array.Empty<string>());
        var added = new List<string>();
        lock (_lock)
        {
            bool changed = false;
            foreach (var ch in channels)
            {
                if (string.IsNullOrWhiteSpace(ch)) continue;
                if (!_channelRefs.TryGetValue(ch, out var count))
                {
                    _channelRefs[ch] = 1;
                    changed = true;
                }
                else
                {
                    _channelRefs[ch] = count + 1;
                }
                added.Add(ch);
            }
            EnsureRunning_NoLock(forceReconnect: changed);
        }
        return new SubscriptionHandle(this, added);
    }

    private void Release(IEnumerable<string> channels)
    {
        lock (_lock)
        {
            bool changed = false;
            foreach (var ch in channels)
            {
                if (_channelRefs.TryGetValue(ch, out var count))
                {
                    if (count <= 1) { _channelRefs.Remove(ch); changed = true; }
                    else _channelRefs[ch] = count - 1;
                }
            }
            if (!changed) return;
            if (_channelRefs.Count == 0)
            {
                StopInternal_NoLock();
            }
            else
            {
                // Force reconnect with reduced channel set.
                CancelCurrentRunLoop_NoLock();
                _cts = new CancellationTokenSource();
                _runTask = Task.Run(() => RunLoop(_cts.Token));
            }
        }
    }

    private void EnsureRunning_NoLock(bool forceReconnect)
    {
        if (_reconnectSuppressed || !_authService.IsLoggedIn)
            return;

        if (_runTask != null && !_runTask.IsCompleted)
        {
            if (!forceReconnect) return;
            Log($"Channel set changed; forcing reconnect");
            CancelCurrentRunLoop_NoLock();
        }
        _cts = new CancellationTokenSource();
        _runTask = Task.Run(() => RunLoop(_cts.Token));
    }

    private void StopInternal_NoLock()
    {
        CancelCurrentRunLoop_NoLock();
        try
        {
            if (_ws?.State == WebSocketState.Open)
                _ = _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client stop", CancellationToken.None);
        }
        catch { }
        _ws?.Dispose();
        _ws = null;
        _runTask = null;
    }

    private void CancelCurrentRunLoop_NoLock()
    {
        try { _cts?.Cancel(); } catch { }
        try { _ws?.Abort(); } catch { }
    }

    private void OnTokenRefreshed()
    {
        lock (_lock)
        {
            if (!_authService.IsLoggedIn || _channelRefs.Count == 0) return;
            _reconnectSuppressed = false;
            Log("Token refreshed; forcing reconnect");
            CancelCurrentRunLoop_NoLock();
            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunLoop(_cts.Token));
        }
    }

    private void OnLoggedOut()
    {
        lock (_lock)
        {
            _reconnectSuppressed = true;
            StopInternal_NoLock();
        }
    }

    private void OnUserChanged()
    {
        lock (_lock)
        {
            if (!_authService.IsLoggedIn)
            {
                _reconnectSuppressed = true;
                StopInternal_NoLock();
                return;
            }

            var wasReconnectSuppressed = _reconnectSuppressed;
            _reconnectSuppressed = false;
            if (_channelRefs.Count > 0 &&
                (wasReconnectSuppressed || _runTask == null || _runTask.IsCompleted))
            {
                EnsureRunning_NoLock(forceReconnect: false);
            }
        }
    }

    /// <summary>
    /// Legacy entry point — subscribes without returning a handle. Retained for
    /// the original <c>SettingsPage</c> import-events caller; new callers should
    /// use <see cref="Subscribe"/> and dispose the handle.
    /// </summary>
    public void Start(params string[] channels)
    {
        // Replace the "start" semantics with "ensure subscribed" — any channels
        // added via this path are anchored until Stop() is called. We track the
        // anchor set separately so Stop() only releases what Start added.
        lock (_lock)
        {
            var changed = false;
            foreach (var ch in channels)
            {
                if (string.IsNullOrWhiteSpace(ch) || !_legacyStartChannels.Add(ch))
                    continue;

                if (!_channelRefs.TryGetValue(ch, out var count))
                {
                    _channelRefs[ch] = 1;
                    changed = true;
                }
                else
                {
                    _channelRefs[ch] = count + 1;
                }
            }

            if (changed || _runTask == null || _runTask.IsCompleted)
                EnsureRunning_NoLock(forceReconnect: changed);
        }
    }

    private readonly HashSet<string> _legacyStartChannels = new(StringComparer.OrdinalIgnoreCase);

    public void Stop()
    {
        lock (_lock)
        {
            var changed = false;
            foreach (var ch in _legacyStartChannels)
            {
                if (!_channelRefs.TryGetValue(ch, out var count))
                    continue;

                if (count <= 1)
                {
                    _channelRefs.Remove(ch);
                    changed = true;
                }
                else
                {
                    _channelRefs[ch] = count - 1;
                }
            }
            _legacyStartChannels.Clear();

            if (!changed) return;
            if (_channelRefs.Count == 0)
                StopInternal_NoLock();
            else
                EnsureRunning_NoLock(forceReconnect: true);
        }
    }

    private sealed class SubscriptionHandle : IDisposable
    {
        private readonly EventChannelClient _client;
        private string[]? _channels;
        public SubscriptionHandle(EventChannelClient client, IEnumerable<string> channels)
        {
            _client = client;
            _channels = channels.ToArray();
        }
        public void Dispose()
        {
            var chans = Interlocked.Exchange(ref _channels, null);
            if (chans != null && chans.Length > 0) _client.Release(chans);
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
            channelsSnapshot = _channelRefs.Keys.ToArray();
        }
        if (channelsSnapshot.Length == 0)
        {
            Log("No channels requested, idling");
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); } catch { }
            return;
        }

        var context = _apiClient.CaptureContext();
        var baseUrl = context.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new InvalidOperationException("API client base URL is not set");
        }

        var wsUrl = baseUrl.Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
                           .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
                           .TrimEnd('/');
        wsUrl += "/api/v2/events/ws";
        var ticket = await MintEventsTicketAsync(ct).ConfigureAwait(false);
        if (!_apiClient.IsCurrentContext(context)) throw new OperationCanceledException("Event authority changed.", ct);

        var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol("silo.events.v2");
        ws.Options.AddSubProtocol("silo.ticket." + ticket);
        _ws = ws;
        try
        {
            var logUrl = wsUrl.Contains('?') ? wsUrl[..wsUrl.IndexOf('?')] : wsUrl;
            Log($"Connecting to {logUrl} (channels: {string.Join(",", channelsSnapshot)})");
            await ws.ConnectAsync(new Uri(wsUrl), ct);
            if (!_apiClient.IsCurrentContext(context)) throw new OperationCanceledException("Event authority changed.", ct);
            StateChanged?.Invoke(ws.State);
            Log("Connected");
        }
        catch
        {
            ws.Dispose();
            if (ReferenceEquals(_ws, ws))
                _ws = null;
            throw;
        }

        try
        {
            await ReceiveLoop(ws, channelsSnapshot, context, ct);
        }
        finally
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch { }
            ws.Dispose();
            if (ReferenceEquals(_ws, ws))
                _ws = null;
            StateChanged?.Invoke(WebSocketState.Closed);
        }
    }

    private async Task ReceiveLoop(
        ClientWebSocket ws,
        string[] channelsToSubscribe,
        ApiRequestContext context,
        CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var messageBuffer = new StringBuilder();

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (!_apiClient.IsCurrentContext(context)) throw new OperationCanceledException("Event authority changed.", ct);
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

            await DispatchFrame(ws, json, channelsToSubscribe, ct);
        }
    }

    private async Task DispatchFrame(
        ClientWebSocket ws,
        string json,
        string[] channelsToSubscribe,
        CancellationToken ct)
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
                    await SendJsonAsync(ws, subscribeMsg, ct);
                    Log($"Sent subscribe for channels: {string.Join(",", channelsToSubscribe)}");
                    break;
                }
                case "subscribed":
                    var notificationProfileRejected = false;
                    if (root.TryGetProperty("rejected", out var rejEl) && rejEl.ValueKind == JsonValueKind.Array && rejEl.GetArrayLength() > 0)
                    {
                        foreach (var r in rejEl.EnumerateArray())
                        {
                            var ch = r.TryGetProperty("channel", out var c) ? c.GetString() : "?";
                            var rejectCode = r.TryGetProperty("code", out var codeEl) ? codeEl.GetString() : "";
                            var msg = r.TryGetProperty("message", out var m) ? m.GetString() : "?";
                            Log($"Subscription rejected: {ch}: {msg}");
                            if (string.Equals(ch, "notifications", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(rejectCode, "profile_required", StringComparison.OrdinalIgnoreCase))
                            {
                                notificationProfileRejected = true;
                            }
                        }
                    }
                    if (notificationProfileRejected)
                    {
                        Log("Notifications subscription rejected; reconnecting to re-mint profile ticket");
                        try { ws.Abort(); } catch { }
                    }
                    Log("Subscribed successfully");
                    break;
                case "snapshot":
                    if (root.TryGetProperty("channel", out var snChEl) && root.TryGetProperty("data", out var snDataEl))
                    {
                        var ch = snChEl.GetString() ?? "";
                        var snapshot = snDataEl.Clone();
                        lock (_snapshotLock) _latestSnapshots[ch] = snapshot;
                        SnapshotReceived?.Invoke(ch, snapshot);
                    }
                    break;
                case "event":
                    if (root.TryGetProperty("channel", out var evChEl)
                        && root.TryGetProperty("event", out var evNameEl)
                        && root.TryGetProperty("data", out var evDataEl))
                    {
                        var ch = evChEl.GetString() ?? "";
                        var evName = evNameEl.GetString() ?? "";
                        var eventData = evDataEl.Clone();
                        ApplyEventToCachedSnapshot(ch, eventData);
                        EventReceived?.Invoke(ch, evName, eventData);
                    }
                    break;
                case "error":
                    var errorCode = root.TryGetProperty("code", out var cEl) ? (cEl.GetString() ?? "") : "";
                    var errMsg = root.TryGetProperty("message", out var mEl) ? (mEl.GetString() ?? "") : "";
                    Log($"Server error: {errorCode}: {errMsg}");
                    ErrorReceived?.Invoke(errorCode, errMsg);
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

    private void ApplyEventToCachedSnapshot(
        string channel,
        JsonElement eventData)
    {
        // Scans have no REST list endpoint. The WebUI therefore keeps the
        // websocket snapshot as its source of truth and folds subsequent scan
        // events into it. Mirror that behavior so late subscribers see the
        // complete current queue.
        if (!string.Equals(channel, "scans", StringComparison.OrdinalIgnoreCase)
            || eventData.ValueKind != JsonValueKind.Object
            || !eventData.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.String)
            return;

        var id = idElement.GetString();
        if (string.IsNullOrWhiteSpace(id)) return;

        var active = eventData.TryGetProperty("status", out var statusElement)
            && statusElement.ValueKind == JsonValueKind.String
            && statusElement.GetString() is "accepted" or "queued" or "running";

        lock (_snapshotLock)
        {
            if (!_latestSnapshots.TryGetValue(channel, out var cached)
                || cached.ValueKind != JsonValueKind.Array)
                return;

            var items = cached.EnumerateArray().Select(item => item.Clone()).ToList();
            var index = items.FindIndex(item =>
                item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("id", out var itemId)
                && itemId.ValueKind == JsonValueKind.String
                && string.Equals(itemId.GetString(), id, StringComparison.Ordinal));

            if (index >= 0 && active)
                items[index] = eventData.Clone();
            else if (index >= 0)
                items.RemoveAt(index);
            else if (active)
                items.Add(eventData.Clone());

            _latestSnapshots[channel] = JsonSerializer.SerializeToElement(items);
        }
    }

    private static async Task SendJsonAsync(ClientWebSocket ws, object obj, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var json = JsonSerializer.Serialize(obj);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private async Task<string> MintEventsTicketAsync(CancellationToken ct)
    {
        var response = await _apiClient.SendRequestAsync<EventsWsTicketResponse>(HttpMethod.Post,
            "/api/v2/events/ws-ticket", null, null, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response.Ticket) || response.Protocol != "silo.events.v2")
            throw new InvalidDataException("Invalid event socket ticket response.");
        return response.Ticket;
    }

    private sealed class EventsWsTicketResponse
    {
        public string Ticket { get; set; } = "";
        public string Protocol { get; set; } = "";
    }

    private static void Log(string msg)
    {
        LocalLog.AppendLine("events_channel.txt", msg);
    }

    public void Dispose()
    {
        _authService.TokenRefreshed -= OnTokenRefreshed;
        _authService.LoggedOut -= OnLoggedOut;
        _authService.UserChanged -= OnUserChanged;
        lock (_lock)
        {
            _reconnectSuppressed = true;
            _legacyStartChannels.Clear();
            _channelRefs.Clear();
            StopInternal_NoLock();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
