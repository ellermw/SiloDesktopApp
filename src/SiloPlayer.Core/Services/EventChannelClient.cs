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
    private ClientWebSocket? _accessHandledSocket;
    private ApiRequestContext? _activeSocketContext;
    private int _immediateReconnectRequested;
    private bool _hasConnected;
    // The server ends every events connection after the ticket's max_connection_seconds (300).
    // A reconnect right after THAT planned end missed nothing worth reloading for (Granary and
    // the server contract: catch up only after more than five seconds without a connection);
    // treating it as a loss retired every page's reads and reloaded the app every five minutes.
    // Only a provable planned end counts. Times come from MonotonicNow, so a wall-clock step
    // cannot move the proof boundary, while sleep still counts as time away:
    // - the connection lasted its announced lifetime, allowing the measured setup (ticket mint
    //   plus upgrade) plus PlannedEndEarly, capped at a tenth of the lifetime, for a close frame
    //   (only the setup for a receive error), and at most PlannedEndLate after, so an early
    //   reset or a slept laptop still catches up;
    // - it ended without a close handshake, with no close status, or with a normal (1000) or
    //   going-away (1001) close; other explicit codes catch up, and 4001/access_changed keep their own handling;
    // - the next connection opened within QuickReconnectWindow of that end. Failed attempts
    //   keep the old end, so the gap only grows and a long outage still catches up. After a
    //   planned end the loop reconnects without its own backoff, so that delay does not spend
    //   the window, and a token refresh while reconnecting keeps the pending catch-up.
    internal static readonly TimeSpan DefaultQuickReconnectWindow = TimeSpan.FromSeconds(5);
    /// <summary>Tests about catch-up itself set it to zero, so every transport loss catches up regardless of timing.</summary>
    internal TimeSpan QuickReconnectWindow { get; set; } = DefaultQuickReconnectWindow;
    internal TimeSpan PlannedEndEarly { get; set; } = TimeSpan.FromSeconds(1);
    internal TimeSpan PlannedEndLate { get; set; } = TimeSpan.FromSeconds(5);

    private sealed class ConnectionRecord(DateTimeOffset connectedAt, int? lifetimeSeconds)
    {
        internal DateTimeOffset ConnectedAt { get; } = connectedAt;
        // Stamped before the ticket mint, so the setup covers the ticket round trip and the upgrade.
        internal DateTimeOffset ConnectStartedAt { get; init; } = connectedAt;
        internal int? LifetimeSeconds { get; } = lifetimeSeconds;
        internal DateTimeOffset? EndedAt { get; set; }
        internal bool Abrupt { get; set; }
        internal bool CloseReceived { get; set; }
        internal int? CloseCode { get; set; }
    }
    private ConnectionRecord? _endedConnection;
    // Set when the current loop lost its transport and has not reconnected yet, so a loop
    // restarted by a token refresh still catches up (or proves a quiet reconnect) once.
    private bool _pendingTransportCatchUp;

    // Environment.TickCount64 is monotonic, and on Windows it still counts sleep and
    // hibernation. Its resolution is the system timer tick, so either end of a span can be
    // one tick off; the lower lifetime bound allows two ticks for that.
    private static DateTimeOffset MonotonicNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(Environment.TickCount64);
    private static readonly TimeSpan MonotonicResolution = TimeSpan.FromMilliseconds(32);

    private bool IsPlannedEnd(ConnectionRecord? connection, out string why)
    {
        why = "no ended connection";
        if (connection is not { EndedAt: { } ended }) return false;
        if (connection.LifetimeSeconds is not int lifetimeSeconds) { why = "the ticket announced no lifetime"; return false; }
        if (!connection.Abrupt && !connection.CloseReceived) { why = "no peer end observed"; return false; }
        // 1001 is RFC 6455's "going away" (.NET names it EndpointUnavailable), a common code for
        // an orderly scheduled end; the lifetime bounds below still reject it when early or late.
        if (connection.CloseCode is int code && code != (int)WebSocketCloseStatus.NormalClosure &&
            code != (int)WebSocketCloseStatus.EndpointUnavailable && code != (int)WebSocketCloseStatus.Empty)
        { why = $"closed with code {code}"; return false; }
        var held = ended - connection.ConnectedAt;
        var lifetime = TimeSpan.FromSeconds(lifetimeSeconds);
        // The server can start its timer anywhere from the ticket mint to the upgrade response.
        // Count from before the mint for the lower bound, but keep the late bound and reconnect
        // gap relative to the open socket. Receive errors borrow only this measured setup time,
        // never the additional close-frame tolerance that could hide an early reset, and that
        // tolerance shrinks with a short lifetime so it cannot cover most of it.
        var setup = connection.ConnectedAt - connection.ConnectStartedAt;
        var closeTolerance = PlannedEndEarly < lifetime / 10 ? PlannedEndEarly : lifetime / 10;
        var early = setup + MonotonicResolution + (connection.Abrupt ? TimeSpan.Zero : closeTolerance);
        if (early >= lifetime)
        { why = $"setup {setup.TotalSeconds:0.000}s leaves nothing of a {lifetimeSeconds}s lifetime to prove"; return false; }
        if (held < lifetime - early || held > lifetime + PlannedEndLate)
        { why = $"held {held.TotalSeconds:0.000}s of a {lifetimeSeconds}s lifetime (setup {setup.TotalSeconds:0.000}s)"; return false; }
        why = $"planned end after {held.TotalSeconds:0.000}s (setup {setup.TotalSeconds:0.000}s)";
        return true;
    }

    private bool IsQuietReconnect(DateTimeOffset connectedAt, out string why)
    {
        if (QuickReconnectWindow <= TimeSpan.Zero) { why = "quick reconnects disabled"; return false; }
        if (!IsPlannedEnd(_endedConnection, out why)) return false;
        var gap = connectedAt - _endedConnection!.EndedAt!.Value;
        if (gap < TimeSpan.Zero || gap > QuickReconnectWindow) { why = $"{why}, back after {gap.TotalSeconds:0.0}s"; return false; }
        why = $"{why}, back after {gap.TotalSeconds:0.0}s";
        return true;
    }
    // Socket whose hello has been answered with a subscribe frame. Channel-set
    // changes on that socket are sent as a new subscribe frame (the server
    // replaces the set and snapshots it) instead of tearing the socket down.
    private ClientWebSocket? _subscribedSocket;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

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
    public event Action<ApiRequestContext>? AccessChanged;
    /// <summary>Transport recovery retires stale reads without implying a permission change.</summary>
    public event Action<ApiRequestContext>? ReadCatchUpRequested;

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
            if (changed) ApplyChannelChange_NoLock();
            else EnsureRunning_NoLock(forceReconnect: false);
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
            if (changed) ApplyChannelChange_NoLock();
        }
    }

    /// <summary>
    /// Applies a changed channel set. Pages subscribe on navigation, so this runs
    /// on ordinary page changes; rebinding the live socket avoids a ticket mint,
    /// a new handshake and the event gap a reconnect would open each time.
    /// </summary>
    private void ApplyChannelChange_NoLock()
    {
        if (_channelRefs.Count == 0)
        {
            StopInternal_NoLock();
            return;
        }
        if (_reconnectSuppressed || !_authService.IsLoggedIn)
            return;
        if (_runTask != null && !_runTask.IsCompleted &&
            _activeSocketContext is { } context && _apiClient.IsCurrentContext(context))
        {
            // Before hello (or during reconnect backoff) the pending subscribe reads the
            // current set itself. Keep that loop's pending transport catch-up intact.
            if (_ws is { } ws && ReferenceEquals(_subscribedSocket, ws))
                _ = ResubscribeAsync(ws, _cts?.Token ?? CancellationToken.None);
            return;
        }
        EnsureRunning_NoLock(forceReconnect: true);
    }

    private async Task ResubscribeAsync(ClientWebSocket ws, CancellationToken ct)
    {
        try
        {
            await SendSubscribeAsync(ws, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // The receive loop observes the broken socket and reconnects with
            // the current channel set.
            Log($"Resubscribe skipped: {ex.GetType().Name}");
        }
    }

    private async Task SendSubscribeAsync(ClientWebSocket ws, CancellationToken ct)
    {
        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Read the set inside the gate so whichever send runs last carries
            // the latest set, regardless of the order callers queued.
            string[] channels;
            lock (_lock)
            {
                if (!ReferenceEquals(_ws, ws) || _channelRefs.Count == 0) return;
                if (_activeSocketContext is not { } context || !_apiClient.IsCurrentContext(context))
                {
                    EnsureRunning_NoLock(forceReconnect: true);
                    return;
                }
                channels = _channelRefs.Keys.ToArray();
            }
            var subscribeMsg = new Dictionary<string, object>
            {
                ["type"] = "subscribe",
                ["request_id"] = Guid.NewGuid().ToString("N"),
                ["channels"] = channels,
            };
            await SendJsonAsync(ws, subscribeMsg, ct).ConfigureAwait(false);
            Log($"Sent subscribe for channels: {string.Join(",", channels)}");
        }
        finally
        {
            _sendGate.Release();
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
        StartRunLoop_NoLock(keepTransportCatchUp: false);
    }

    private void StartRunLoop_NoLock(bool keepTransportCatchUp)
    {
        // The replacement loop inherits a pending transport catch-up and the end it is measured
        // from; otherwise it starts fresh, so its first connection never looks like a reconnect.
        var catchUp = keepTransportCatchUp && _pendingTransportCatchUp;
        _pendingTransportCatchUp = catchUp;
        if (!catchUp) _endedConnection = null;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _runTask = Task.Run(() => RunLoop(cts.Token, catchUp));
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
        _subscribedSocket = null;
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
            StartRunLoop_NoLock(keepTransportCatchUp: true);
        }
    }

    private void OnLoggedOut()
    {
        lock (_lock)
        {
            _hasConnected = false;
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

            if (changed) ApplyChannelChange_NoLock();
            else EnsureRunning_NoLock(forceReconnect: false);
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

            if (changed) ApplyChannelChange_NoLock();
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

    private async Task RunLoop(CancellationToken ct, bool catchUp = false)
    {
        var backoffMs = 1000;
        while (!ct.IsCancellationRequested)
        {
            long? connectedAt = null;
            try
            {
                await ConnectAndRunOnce(ct, catchUp, () => connectedAt = System.Diagnostics.Stopwatch.GetTimestamp());
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && IsTimeout(ex))
            {
                // HttpClient.Timeout surfaces as a cancellation, but it is a lost connection:
                // back off and retry (catching up) instead of ending the loop for good.
                Log($"Connection timed out: {ex.Message}");
            }
            catch (OperationCanceledException)
            {
                if (!ct.IsCancellationRequested && Interlocked.Exchange(ref _immediateReconnectRequested, 0) != 0)
                { catchUp = false; SetPendingTransportCatchUp(ct, false); backoffMs = 1000; continue; }
                return;
            }
            catch (Exception ex)
            {
                Log($"Connection error: {ex.Message}");
            }
            backoffMs = ReconnectBackoff.AfterConnection(backoffMs, 1000,
                connectedAt is { } openedAt ? System.Diagnostics.Stopwatch.GetElapsedTime(openedAt) : TimeSpan.Zero);
            if (ct.IsCancellationRequested) return;
            if (Interlocked.Exchange(ref _immediateReconnectRequested, 0) != 0)
            { catchUp = false; SetPendingTransportCatchUp(ct, false); backoffMs = 1000; continue; }

            // A transport loss may have missed permission changes. A deliberate
            // channel/profile rebind starts a new loop and must not rebuild pages again.
            catchUp = true;
            SetPendingTransportCatchUp(ct, true);

            // The server's planned lifetime end: reconnect now, so our own backoff does not
            // spend the quick-reconnect window. Only a connection opened this round qualifies.
            bool plannedEnd;
            lock (_lock)
                plannedEnd = connectedAt != null && QuickReconnectWindow > TimeSpan.Zero && IsPlannedEnd(_endedConnection, out _);
            if (plannedEnd)
            {
                Log("Planned end; reconnecting now");
                continue;
            }

            // Don't spam reconnects — capped exponential backoff up to 30s.
            Log($"Reconnecting in {backoffMs}ms");
            try { await Task.Delay(backoffMs, ct); } catch { return; }
            backoffMs = Math.Min(backoffMs * 2, 30_000);
        }
    }

    private static bool IsTimeout(Exception ex)
    {
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            if (inner is TimeoutException) return true;
        return false;
    }

    private void SetPendingTransportCatchUp(CancellationToken ct, bool pending)
    {
        // A cancelled loop has been replaced; it must not overwrite its successor's state.
        lock (_lock)
            if (!ct.IsCancellationRequested) _pendingTransportCatchUp = pending;
    }

    private async Task ConnectAndRunOnce(CancellationToken ct, bool catchUp, Action? onConnected = null)
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
        var connectStartedAt = MonotonicNow();
        var (ticket, lifetimeSeconds) = await MintEventsTicketAsync(ct).ConfigureAwait(false);
        if (!_apiClient.IsCurrentContext(context)) throw new OperationCanceledException("Event authority changed.", ct);

        var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol("silo.events.v2");
        ws.Options.AddSubProtocol("silo.ticket." + ticket);
        lock (_lock) { _ws = ws; _activeSocketContext = context; }
        ConnectionRecord connection;
        try
        {
            var logUrl = wsUrl.Contains('?') ? wsUrl[..wsUrl.IndexOf('?')] : wsUrl;
            Log($"Connecting to {logUrl} (channels: {string.Join(",", channelsSnapshot)})");
            await ws.ConnectAsync(new Uri(wsUrl), ct);
            if (!_apiClient.IsCurrentContext(context)) throw new OperationCanceledException("Event authority changed.", ct);
            onConnected?.Invoke();
            connection = new ConnectionRecord(MonotonicNow(), lifetimeSeconds) { ConnectStartedAt = connectStartedAt };
            var reconnect = false;
            lock (_lock)
            {
                ct.ThrowIfCancellationRequested();
                if (!ReferenceEquals(_ws, ws) || !_apiClient.IsCurrentContext(context))
                    throw new OperationCanceledException("Event authority changed.", ct);
                reconnect = _hasConnected && catchUp;
                if (reconnect)
                {
                    var quiet = IsQuietReconnect(connection.ConnectedAt, out var why);
                    Log(quiet ? $"Quick reconnect ({why}): no catch-up" : $"Catch-up needed ({why})");
                    reconnect = !quiet;
                }
                _pendingTransportCatchUp = false;
                if (reconnect && !PublishAccessInvalidation(context, out context, transportCatchUp: true))
                    throw new OperationCanceledException("Event authority changed.", ct);
                _activeSocketContext = context;
                _hasConnected = true;
            }
            if (reconnect) _ = RefreshAccountSafelyAsync(ct);
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
            await ReceiveLoop(ws, context, ct, connection);
        }
        finally
        {
            // Only a connection that opened records an end; failed attempts keep the previous one.
            // A cancelled loop has been replaced and must not overwrite its successor's record.
            connection.EndedAt = MonotonicNow();
            lock (_lock)
            {
                if (!ct.IsCancellationRequested)
                {
                    _endedConnection = connection;
                    // Pending from the moment the socket ends, so a token refresh before the
                    // loop's backoff still carries the catch-up. Access closes handle their own.
                    _pendingTransportCatchUp = Volatile.Read(ref _immediateReconnectRequested) == 0;
                }
            }
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch { }
            ws.Dispose();
            lock (_lock)
            {
                if (ReferenceEquals(_ws, ws))
                    _ws = null;
                if (ReferenceEquals(_subscribedSocket, ws))
                    _subscribedSocket = null;
            }
            StateChanged?.Invoke(WebSocketState.Closed);
        }
    }

    private async Task ReceiveLoop(
        ClientWebSocket ws,
        ApiRequestContext context,
        CancellationToken ct,
        ConnectionRecord connection)
    {
        var buffer = new byte[16 * 1024];
        var messageBuffer = new StringBuilder();

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer, ct);
                if (!_apiClient.IsCurrentContext(context) && !ReferenceEquals(_accessHandledSocket, ws))
                    throw new OperationCanceledException("Event authority changed.", ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WebSocketException ex)
            {
                connection.Abrupt = true;
                Log($"Receive error: {ex.Message}");
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                connection.CloseReceived = true;
                connection.CloseCode = (int?)result.CloseStatus;
                if (!ct.IsCancellationRequested) HandleServerClose(ws, (int?)result.CloseStatus);
                Log($"Server closed connection: {result.CloseStatusDescription}");
                return;
            }
            if (ReferenceEquals(_accessHandledSocket, ws)) return;

            messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage) continue;

            var json = messageBuffer.ToString();
            messageBuffer.Clear();

            await DispatchFrame(ws, json, ct);
        }
    }

    private async Task DispatchFrame(
        ClientWebSocket ws,
        string json,
        CancellationToken ct)
    {
        JsonDocument? doc = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                return;

            var type = typeEl.GetString()!;
            switch (type)
            {
                case "access_changed":
                    HandleAccessChanged(ws);
                    break;
                case "hello":
                {
                    if (root.TryGetProperty("connection_id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                        _connectionId = idEl.GetString();
                    // Client MUST send subscribe within 5 seconds — send immediately.
                    lock (_lock)
                    {
                        if (ReferenceEquals(_ws, ws)) _subscribedSocket = ws;
                    }
                    await SendSubscribeAsync(ws, ct);
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

    private void HandleServerClose(ClientWebSocket ws, int? code)
    {
        if (code == 4001) HandleAccessChanged(ws);
    }

    private void HandleAccessChanged(ClientWebSocket ws)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_ws, ws) || ReferenceEquals(_accessHandledSocket, ws) ||
                !_authService.IsLoggedIn || _activeSocketContext is not { } expected ||
                !PublishAccessInvalidation(expected, out _)) return;
            _accessHandledSocket = ws;
            Interlocked.Exchange(ref _immediateReconnectRequested, 1);
        }
        _ = RefreshAccountSafelyAsync(_cts?.Token ?? CancellationToken.None);
    }

    private bool PublishAccessInvalidation(ApiRequestContext expected, out ApiRequestContext context, bool transportCatchUp = false)
    {
        if (!_apiClient.TryInvalidateAccessContext(expected, out context)) return false;
        Log($"{(transportCatchUp ? "Transport catch-up" : "Access reads invalidated")}: readGeneration={context.RequestContextGeneration}; playbackIdentityGeneration={_apiClient.CaptureIdentityContext().IdentityGeneration}");
        lock (_snapshotLock) _latestSnapshots.Clear();
        foreach (var subscriber in (transportCatchUp ? ReadCatchUpRequested : AccessChanged)?.GetInvocationList() ?? [])
        {
            try { ((Action<ApiRequestContext>)subscriber)(context); }
            catch (Exception ex) { Log($"Access refresh subscriber failed: {ex.GetType().Name}"); }
        }
        return true;
    }

    private async Task RefreshAccountSafelyAsync(CancellationToken ct)
    {
        try { await _authService.RefreshAccountAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Log($"Authoritative account refresh failed: {ex.GetType().Name}"); }
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

    private async Task<(string Ticket, int? LifetimeSeconds)> MintEventsTicketAsync(CancellationToken ct)
    {
        var response = await _apiClient.SendRequestAsync<EventsWsTicketResponse>(HttpMethod.Post,
            "/api/v2/events/ws-ticket", null, null, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response.Ticket) || response.Protocol != "silo.events.v2")
            throw new InvalidDataException("Invalid event socket ticket response.");
        // How long the server keeps this connection (300 s today). The field is advisory and
        // unverified, so it is read without throwing: absent, misnamed or unusable means no end
        // can be proven planned, so every reconnect catches up as before.
        if (ReadLifetimeSeconds(response.MaxConnectionSeconds) is int lifetime)
            return (response.Ticket, lifetime);
        if (response.MaxConnectionSeconds is { ValueKind: not JsonValueKind.Null } raw)
        {
            var text = raw.GetRawText();
            Log($"Ticket announced an unusable max_connection_seconds ({(text.Length > 64 ? text[..64] + "..." : text)}); every reconnect will catch up");
        }
        else
        {
            var fields = response.OtherFields is { Count: > 0 } other ? string.Join(",", other.Keys) : "none";
            Log($"Ticket announced no max_connection_seconds (other fields: {fields}); quick reconnects are OFF and every reconnect will catch up");
        }
        return (response.Ticket, null);
    }

    // A whole number of seconds from 1 to 3600, as a JSON number (300 or 300.0) or a numeric string.
    private static int? ReadLifetimeSeconds(JsonElement? value)
    {
        double seconds;
        switch (value)
        {
            case { ValueKind: JsonValueKind.Number } number when number.TryGetDouble(out seconds):
                break;
            case { ValueKind: JsonValueKind.String } text when double.TryParse(text.GetString(),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds):
                break;
            default:
                return null;
        }
        return seconds is >= 1 and <= 3600 && seconds == Math.Floor(seconds) ? (int)seconds : null;
    }

    private sealed class EventsWsTicketResponse
    {
        public string Ticket { get; set; } = "";
        public string Protocol { get; set; } = "";
        // Raw, so a value of an unexpected type can never fail the ticket mint.
        [System.Text.Json.Serialization.JsonPropertyName("max_connection_seconds")]
        public JsonElement? MaxConnectionSeconds { get; set; }
        // Logged when the lifetime is missing, so a renamed field is visible in events_channel.txt.
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement>? OtherFields { get; set; }
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
