using System.Collections.ObjectModel;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.ViewModels;

// Two ViewModels backing the current Silo Watch Party page pair.

/// <summary>
/// Backs <c>WatchTogetherJoinPage</c> — create a new room or join an existing one
/// by room code / invite token. On success, the page navigates to the room page
/// passing the (room_id, room_access_token) tuple.
/// </summary>
public partial class WatchTogetherJoinViewModel : ObservableObject
{
    private readonly PlaybackApi _playbackApi;

    public WatchTogetherJoinViewModel(PlaybackApi playbackApi)
    {
        _playbackApi = playbackApi;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJoin))]
    private string _roomCode = "";
    [ObservableProperty] private string _selectionMode = "host_pick"; // host_pick | vote
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanJoin))]
    private bool _isBusy;
    [ObservableProperty] private string? _errorMessage;

    public bool CanJoin => !IsBusy && !string.IsNullOrWhiteSpace(RoomCode);

    /// <summary>Set by the page after a successful create/join so it can navigate.</summary>
    [ObservableProperty] private WatchTogetherRoomResponse? _lastResponse;

    [RelayCommand]
    private async Task CreateRoomAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            LastResponse = await _playbackApi.CreateWatchTogetherRoomAsync(SelectionMode);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create room: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task JoinByCodeAsync()
    {
        if (IsBusy) return;
        var code = RoomCode?.Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            ErrorMessage = "Enter a room code.";
            return;
        }
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            LastResponse = await _playbackApi.JoinWatchTogetherRoomAsync(code, null);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.StatusCode switch
            {
                404 => "Room not found.",
                410 => "That room is no longer active.",
                _ => ex.Message,
            };
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to join room: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    public async Task JoinByInviteTokenAsync(string inviteToken)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            LastResponse = await _playbackApi.JoinWatchTogetherRoomAsync(null, inviteToken);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.StatusCode switch
            {
                404 => "Room not found.",
                410 => "That room is no longer active.",
                _ => ex.Message,
            };
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to join room: {ex.Message}";
        }
        finally { IsBusy = false; }
    }
}

/// <summary>
/// Backs <c>WatchTogetherRoomPage</c>. Owns the room REST refresh cycle, suggestion
/// list, and a dedicated WebSocket client connected to
/// <c>/api/v1/watch-together/rooms/{roomId}/ws</c> that streams room state updates.
/// </summary>
///
/// <remarks>
/// The room page registers this VM with <c>WatchTogetherCoordinator</c>, which starts
/// native playback and applies synchronized transport commands through PlayerService.
/// </remarks>
public partial class WatchTogetherRoomViewModel : ObservableObject, IDisposable
{
    private readonly PlaybackApi _playbackApi;
    private readonly SiloApiClient _apiClient;
    private readonly DispatcherQueue? _dispatcher;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _wsCts;
    private Task? _wsRunTask;
    private readonly SemaphoreSlim _wsLifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _wsSendGate = new(1, 1);
    private bool _disposed;

    public WatchTogetherRoomViewModel(PlaybackApi playbackApi, SiloApiClient apiClient)
    {
        _playbackApi = playbackApi;
        _apiClient = apiClient;
        _dispatcher = App.MainWindowInstance?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
    }

    [ObservableProperty] private string? _roomId;
    [ObservableProperty] private string? _roomToken;
    [ObservableProperty] private WatchTogetherRoomSnapshot? _room;
    [ObservableProperty] private string _connectionState = "disconnected"; // disconnected | connecting | connected
    [ObservableProperty] private string? _closedReason;
    [ObservableProperty] private string? _noticeMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<WatchTogetherSuggestion> Suggestions { get; } = [];

    /// <summary>
    /// Fired when a <c>transport_command</c> frame arrives from the server. The
    /// callback is dispatched to the UI thread with the other room-state changes.
    /// Payload carries action (play/pause/seek),
    /// target position, and the session it applies to.
    /// </summary>
    public event Action<WatchTogetherTransportCommand>? TransportCommandReceived;

    /// <summary>Session ID attached to this room (if any). Set by <see cref="AttachSession"/>.</summary>
    public string? AttachedSessionId { get; private set; }

    /// <summary>
    /// Goes true when the room transitions to phase=playing on a NEW selection_revision,
    /// signalling the coordinator that synchronized native playback should start.
    /// Consumers should read and clear via <see cref="AcknowledgePlaybackStart"/>.
    /// </summary>
    public bool ShouldAutoStartPlayback { get; private set; }
    public int LastAutoStartRevision { get; private set; } = -1;

    public bool IsHost => Room?.SelfCanManageRoom == true;
    public bool IsVoteMode => Room?.SelectionMode == "vote";
    public bool IsPlaying => Room?.Phase == "playing";
    public bool HasRoom => Room != null;
    public string DisplayCode => Room?.Code ?? RoomId ?? "";
    public int MemberCount => Room?.MemberCount ?? 0;

    public void AcknowledgePlaybackStart()
    {
        ShouldAutoStartPlayback = false;
    }

    partial void OnRoomChanged(WatchTogetherRoomSnapshot? value)
    {
        OnPropertyChanged(nameof(IsHost));
        OnPropertyChanged(nameof(IsVoteMode));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(HasRoom));
        OnPropertyChanged(nameof(DisplayCode));
        OnPropertyChanged(nameof(MemberCount));
        UpdateSuggestionPermissions(Suggestions);

        if (value != null && value.Phase == "playing"
            && !string.IsNullOrEmpty(value.SelectedContentId)
            && value.SelectionRevision != LastAutoStartRevision)
        {
            LastAutoStartRevision = value.SelectionRevision;
            ShouldAutoStartPlayback = true;
        }
    }

    /// <summary>Initialize for a specific room and kick off the initial REST fetch + WS connect.</summary>
    public async Task InitializeAsync(string roomId, string roomToken)
    {
        RoomId = roomId;
        RoomToken = roomToken;
        ErrorMessage = null;
        ClosedReason = null;

        try
        {
            var detail = await _playbackApi.GetWatchTogetherRoomAsync(roomId, roomToken);
            Room = detail.Room;
        }
        catch (ApiException ex) when (ex.StatusCode == 404 || ex.StatusCode == 410)
        {
            ClosedReason = ex.StatusCode == 410 ? "room_closed" : "not_found";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load room: {ex.Message}";
        }

        try
        {
            var list = await _playbackApi.ListWatchTogetherSuggestionsAsync(roomId, roomToken);
            Suggestions.Clear();
            UpdateSuggestionPermissions(list.Suggestions);
            foreach (var s in list.Suggestions) Suggestions.Add(s);
        }
        catch
        {
            // Non-fatal — suggestions may be empty or vote-mode-off.
        }

        await RestartWebSocketAsync();
    }

    [RelayCommand]
    private async Task TogglePolicyAsync()
    {
        var r = Room;
        if (r == null || string.IsNullOrEmpty(RoomId)) return;
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var next = r.GuestControlPolicy == "guest_play_pause" ? "host_only" : "guest_play_pause";
            var resp = await _playbackApi.UpdateWatchTogetherRoomPolicyAsync(RoomId, next);
            Room = resp.Room;
        }
        catch (Exception ex) { ErrorMessage = $"Policy update failed: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CloseRoomAsync()
    {
        if (string.IsNullOrEmpty(RoomId) || IsBusy) return;
        IsBusy = true;
        try
        {
            await _playbackApi.CloseWatchTogetherRoomAsync(RoomId);
            ClosedReason = "host_left";
            await StopWebSocketAsync();
        }
        catch (Exception ex) { ErrorMessage = $"Failed to close room: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public async Task VoteAsync(string suggestionId)
    {
        if (string.IsNullOrEmpty(RoomId) || string.IsNullOrEmpty(RoomToken)) return;
        try
        {
            var resp = await _playbackApi.VoteWatchTogetherSuggestionAsync(RoomId, RoomToken, suggestionId);
            ReplaceSuggestions(resp.Suggestions);
        }
        catch (Exception ex) { ErrorMessage = $"Vote failed: {ex.Message}"; }
    }

    public async Task UnvoteAsync(string suggestionId)
    {
        if (string.IsNullOrEmpty(RoomId) || string.IsNullOrEmpty(RoomToken)) return;
        try
        {
            var resp = await _playbackApi.UnvoteWatchTogetherSuggestionAsync(RoomId, RoomToken, suggestionId);
            ReplaceSuggestions(resp.Suggestions);
        }
        catch (Exception ex) { ErrorMessage = $"Unvote failed: {ex.Message}"; }
    }

    public async Task DeleteSuggestionAsync(string suggestionId)
    {
        if (string.IsNullOrEmpty(RoomId) || string.IsNullOrEmpty(RoomToken)) return;
        try
        {
            var response = await _playbackApi.DeleteWatchTogetherSuggestionAsync(RoomId, RoomToken, suggestionId);
            ReplaceSuggestions(response.Suggestions);
        }
        catch (Exception ex) { ErrorMessage = $"Delete failed: {ex.Message}"; }
    }

    public async Task PromoteSuggestionAsync(string suggestionId)
    {
        if (string.IsNullOrEmpty(RoomId) || string.IsNullOrEmpty(RoomToken)) return;
        try
        {
            var resp = await _playbackApi.PromoteWatchTogetherSuggestionAsync(RoomId, RoomToken, suggestionId);
            Room = resp.Room;
        }
        catch (Exception ex) { ErrorMessage = $"Promote failed: {ex.Message}"; }
    }

    private void ReplaceSuggestions(List<WatchTogetherSuggestion> next)
    {
        UpdateSuggestionPermissions(next);
        Suggestions.Clear();
        foreach (var s in next) Suggestions.Add(s);
    }

    private void UpdateSuggestionPermissions(IEnumerable<WatchTogetherSuggestion> suggestions)
    {
        var profileId = _apiClient.ProfileId;
        foreach (var suggestion in suggestions)
        {
            suggestion.CanPromote = IsHost;
            suggestion.CanVote = IsVoteMode;
            suggestion.CanDelete = IsHost
                || (!string.IsNullOrWhiteSpace(profileId)
                    && string.Equals(suggestion.SuggesterProfileId, profileId, StringComparison.Ordinal));
        }
    }

    public WatchTogetherCapabilities Capabilities { get; private set; } = new();
    public async Task LoadCapabilitiesAsync()
    {
        try { Capabilities = await _playbackApi.GetWatchTogetherCapabilitiesAsync(); }
        catch { Capabilities = new(); }
    }
    public Task SetLobbyReadyAsync(bool ready)
    {
        if (!Capabilities.LobbyReady || Room?.Phase != "lobby" || string.IsNullOrWhiteSpace(Room.SelectedContentId) || ConnectionState != "connected")
        { ErrorMessage = "Reconnect to the room before changing readiness."; return Task.CompletedTask; }
        return SendWsMessageAsync(new Dictionary<string, object?> { ["type"] = "lobby_ready", ["ready"] = ready });
    }
    public async Task StartStagedAsync()
    {
        if (IsBusy || !IsHost || Room?.Phase != "lobby" || string.IsNullOrWhiteSpace(Room.SelectedContentId) || !Capabilities.StagedSelection) return;
        IsBusy = true; ErrorMessage = null;
        try { Room = (await _playbackApi.StartWatchTogetherRoomPlaybackAsync(RoomId!)).Room; }
        catch (Exception ex) { ErrorMessage = $"Could not start playback: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    public async Task ChangeSelectionModeAsync(string mode)
    {
        if (IsBusy || !IsHost || Room?.Phase != "lobby" || !Capabilities.SelectionModeSwitch) return;
        IsBusy = true; ErrorMessage = null;
        try { Room = (await _playbackApi.UpdateWatchTogetherSelectionModeAsync(RoomId!, mode)).Room; UpdateSuggestionPermissions(Suggestions); }
        catch (Exception ex) { ErrorMessage = $"Could not change selection mode: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    // ===== Playback sync (outbound messages) =====

    /// <summary>
    /// Announce to the room that this session is attached. Required before the
    /// server will route inbound <c>transport_command</c> frames. Called once
    /// when playback starts and the user is inside a room. Safe to call before
    /// the WS connects — queued message is not retained, but an attach triggered
    /// by PlayerService happens well after room join, so the WS is normally open.
    /// </summary>
    public void AttachSession(string sessionId)
    {
        AttachedSessionId = sessionId;
        _ = SendWsMessageAsync(new Dictionary<string, object?>
        {
            ["type"] = "attach_session",
            ["session_id"] = sessionId,
        });
    }

    public void DetachSession()
    {
        AttachedSessionId = null;
        // No explicit detach message — the server reconciles when the session
        // ends server-side. (Matches upstream useWatchTogetherPlaybackSync.)
    }

    /// <summary>
    /// Broadcast a transport request (play/pause/seek) to the room. Server
    /// adjudicates (host-only policy blocks guests) and echoes back a
    /// <c>transport_command</c> that all peers will apply via
    /// <see cref="TransportCommandReceived"/>.
    /// </summary>
    public void RequestTransport(string action, double positionSeconds, bool isPaused)
    {
        if (string.IsNullOrEmpty(AttachedSessionId)) return;
        if (ConnectionState != "connected") return;
        if (Room?.Phase != "playing" ||
            (action == "seek" ? !Room.SelfCanManageRoom : !Room.SelfCanControlTransport)) return;
        _ = SendWsMessageAsync(new Dictionary<string, object?>
        {
            ["type"] = "transport_request",
            ["action"] = action,
            ["position_seconds"] = positionSeconds,
            ["is_paused"] = isPaused,
        });
    }

    /// <summary>Periodic state report. Sent 1.5s-ish by the coordinator so the
    /// server can track each client's playhead for drift detection and lagging-
    /// guest policy. Matches upstream <c>useWatchTogetherPlaybackSync</c>
    /// <c>state_report</c>.</summary>
    public void ReportState(double positionSeconds, bool isPaused)
    {
        if (string.IsNullOrEmpty(AttachedSessionId)) return;
        if (ConnectionState != "connected") return;
        _ = SendWsMessageAsync(new Dictionary<string, object?>
        {
            ["type"] = "state_report",
            ["session_id"] = AttachedSessionId,
            ["position_seconds"] = positionSeconds,
            ["is_paused"] = isPaused,
        });
    }

    /// <summary>Signal that the local client has loaded the content and is ready
    /// to play. Sent once when room phase is "waiting" and playback buffered up.</summary>
    public void ReportReady(double positionSeconds, bool isPaused, string? commandId = null)
    {
        if (string.IsNullOrEmpty(AttachedSessionId)) return;
        if (ConnectionState != "connected") return;
        _ = SendWsMessageAsync(new Dictionary<string, object?>
        {
            ["type"] = "ready",
            ["command_id"] = commandId,
            ["session_id"] = AttachedSessionId,
            ["position_seconds"] = Math.Max(0, positionSeconds),
            ["is_paused"] = isPaused,
        });
    }

    /// <summary>Signal that local playback stalled for buffering. Server may
    /// pause other clients until this one catches up (depends on room policy).</summary>
    public void ReportBuffering(double positionSeconds, bool isPaused)
    {
        if (string.IsNullOrEmpty(AttachedSessionId)) return;
        if (ConnectionState != "connected") return;
        _ = SendWsMessageAsync(new Dictionary<string, object?>
        {
            ["type"] = "buffering",
            ["session_id"] = AttachedSessionId,
            ["position_seconds"] = Math.Max(0, positionSeconds),
            ["is_paused"] = isPaused,
        });
    }

    /// <summary>
    /// Estimated offset (server_time - client_time in ms) derived from transport
    /// command timestamps. Used to convert server-side <c>execute_at</c> into a
    /// local DateTime so clients fire transport actions in sync. Zero until the
    /// first command arrives.
    /// </summary>
    public long ServerTimeOffsetMs { get; private set; }

    private void UpdateServerTimeOffset(string? issuedAtIso)
    {
        if (string.IsNullOrEmpty(issuedAtIso)) return;
        if (!DateTime.TryParse(issuedAtIso, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var serverTime)) return;
        var now = DateTime.UtcNow;
        // Simple moving estimate — we don't have RTT measurement, so this is
        // best-effort. Refine if a scheduled execute_at drifts more than ~100ms.
        ServerTimeOffsetMs = (long)(serverTime.ToUniversalTime() - now).TotalMilliseconds;
    }

    private async Task SendWsMessageAsync(IDictionary<string, object?> body)
    {
        var ws = _ws;
        var ct = _wsCts?.Token ?? CancellationToken.None;
        if (ws == null || ws.State != WebSocketState.Open) return;
        var entered = false;
        try
        {
            await _wsSendGate.WaitAsync(ct);
            entered = true;
            if (!ReferenceEquals(ws, _ws) || ws.State != WebSocketState.Open) return;
            var json = JsonSerializer.Serialize(body);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        catch { /* best effort — next attempt will retry */ }
        finally { if (entered) _wsSendGate.Release(); }
    }

    // ===== WebSocket =====

    private async Task RestartWebSocketAsync()
    {
        await _wsLifecycleGate.WaitAsync();
        try
        {
            await StopWebSocketCoreAsync();
            if (_disposed || string.IsNullOrEmpty(RoomId) || string.IsNullOrEmpty(RoomToken))
                return;

            var cts = new CancellationTokenSource();
            _wsCts = cts;
            _wsRunTask = Task.Run(() => WebSocketRunLoopAsync(cts.Token));
        }
        finally
        {
            _wsLifecycleGate.Release();
        }
    }

    private async Task StopWebSocketAsync()
    {
        await _wsLifecycleGate.WaitAsync();
        try
        {
            await StopWebSocketCoreAsync();
        }
        finally
        {
            _wsLifecycleGate.Release();
        }
    }

    private async Task StopWebSocketCoreAsync()
    {
        var cts = _wsCts;
        var runTask = _wsRunTask;
        var ws = _ws;
        _wsCts = null;
        _wsRunTask = null;
        _ws = null;

        try { cts?.Cancel(); } catch { }
        try { ws?.Abort(); } catch { }

        if (runTask != null)
        {
            try { await runTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { }
        }

        ws?.Dispose();
        cts?.Dispose();
        SetConnectionState("disconnected");
    }

    private async Task WebSocketRunLoopAsync(CancellationToken ct)
    {
        var backoffMs = 500;
        var authority = _apiClient.CaptureContext();
        while (!ct.IsCancellationRequested && _apiClient.IsCurrentContext(authority))
        {
            try
            {
                await ConnectAndRunOnceAsync(ct);
                backoffMs = 500;
            }
            catch (OperationCanceledException) { return; }
            catch (ApiException ex) when (ex.StatusCode is 401 or 403 or 404 or 409 or 410) { return; }
            catch { /* swallow; reconnect with backoff */ }

            if (ct.IsCancellationRequested) return;
            if (!string.IsNullOrEmpty(ClosedReason)) return;
            try { await Task.Delay(backoffMs, ct); } catch { return; }
            backoffMs = Math.Min(backoffMs * 2, 5_000);
        }
    }

    private async Task ConnectAndRunOnceAsync(CancellationToken ct)
    {
        var baseUrl = _apiClient.BaseUrl;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(RoomId) || string.IsNullOrEmpty(RoomToken))
            return;

        var wsUrl = baseUrl
            .Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');
        wsUrl += $"/api/v2/watch-together/rooms/{Uri.EscapeDataString(RoomId)}/ws";
        var authority = _apiClient.CaptureContext();
        var roomId = RoomId;
        var roomToken = RoomToken;
        var ticket = await _playbackApi.CreateRoomControlTicketAsync(roomId, roomToken, ct);
        if (!_apiClient.IsCurrentContext(authority) || RoomId != roomId || RoomToken != roomToken)
            throw new OperationCanceledException("Room authority changed.", ct);
        await RunOnUiThreadAsync(() => ConnectionState = "connecting");
        var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol(ticket.Protocol);
        ws.Options.AddSubProtocol("silo.ticket." + ticket.Ticket);
        _ws = ws;
        try
        {
            await ws.ConnectAsync(new Uri(wsUrl), ct);
            if (ws.SubProtocol != ticket.Protocol) throw new InvalidOperationException("Room socket protocol was not negotiated.");
            await RunOnUiThreadAsync(() => ConnectionState = "connected");

            var buffer = new byte[16 * 1024];
            using var message = new MemoryStream();

            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch { return; }

                if (result.MessageType == WebSocketMessageType.Close) return;

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage) continue;

                var json = Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
                message.SetLength(0);

                if (!_apiClient.IsCurrentContext(authority) || RoomId != roomId || RoomToken != roomToken) return;
                await RunOnUiThreadAsync(() =>
                {
                    if (_apiClient.IsCurrentContext(authority) && RoomId == roomId && RoomToken == roomToken) HandleFrame(json);
                });
            }
        }
        finally
        {
            if (ReferenceEquals(_ws, ws))
            {
                _ws = null;
                SetConnectionState(ct.IsCancellationRequested ? "disconnected" : "reconnecting");
            }
            try { ws.Abort(); } catch { }
            ws.Dispose();
        }
    }

    private void SetConnectionState(string value)
    {
        if (_dispatcher == null || _dispatcher.HasThreadAccess)
        {
            ConnectionState = value;
            return;
        }

        _dispatcher.TryEnqueue(() => ConnectionState = value);
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (_dispatcher == null || _dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetResult();
        }

        return completion.Task;
    }

    private void HandleFrame(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();
            switch (type)
            {
                case "snapshot":
                    if (root.TryGetProperty("room", out var roomEl))
                    {
                        var snap = JsonSerializer.Deserialize<WatchTogetherRoomSnapshot>(
                            roomEl.GetRawText(), SnakeJsonOptions);
                        if (snap != null) Room = snap;
                    }
                    break;
                case "suggestions_update":
                    if (root.TryGetProperty("suggestions", out var sugEl))
                    {
                        var next = JsonSerializer.Deserialize<List<WatchTogetherSuggestion>>(
                            sugEl.GetRawText(), SnakeJsonOptions);
                        if (next != null)
                        {
                            // Preserve voted_by_me locally — the server broadcast strips it.
                            var myVotes = new HashSet<string>(
                                Suggestions.Where(s => s.VotedByMe).Select(s => s.Id));
                            foreach (var s in next)
                                s.VotedByMe = myVotes.Contains(s.Id);
                            ReplaceSuggestions(next);
                        }
                    }
                    break;
                case "room_closed":
                    Room = null;
                    ClosedReason = root.TryGetProperty("reason", out var reasonEl)
                        ? (reasonEl.GetString() ?? "room_closed")
                        : "room_closed";
                    break;
                case "transport_command":
                    try
                    {
                        var cmd = JsonSerializer.Deserialize<WatchTogetherTransportCommand>(
                            root.GetRawText(), SnakeJsonOptions);
                        if (cmd != null && !string.IsNullOrEmpty(cmd.Action))
                        {
                            UpdateServerTimeOffset(cmd.IssuedAt);
                            TransportCommandReceived?.Invoke(cmd);
                        }
                    }
                    catch { /* malformed — ignore */ }
                    break;
                case "error":
                    ErrorMessage = root.TryGetProperty("message", out var error)
                        ? error.GetString() : "The room rejected the request.";
                    break;
                // pong is received but not consumed (the client doesn't track RTT yet).
            }
        }
        catch { /* ignore malformed frames */ }
    }

    private static readonly JsonSerializerOptions SnakeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var cts = _wsCts;
        var runTask = _wsRunTask;
        var ws = _ws;
        _wsCts = null;
        _wsRunTask = null;
        _ws = null;

        try { cts?.Cancel(); } catch { }
        try { ws?.Abort(); } catch { }
        SetConnectionState("disconnected");
        _ = DisposeWebSocketResourcesAfterRunAsync(runTask, ws, cts);
    }

    private static async Task DisposeWebSocketResourcesAfterRunAsync(
        Task? runTask,
        ClientWebSocket? ws,
        CancellationTokenSource? cts)
    {
        if (runTask != null)
        {
            try { await runTask.ConfigureAwait(false); }
            catch { }
        }
        ws?.Dispose();
        cts?.Dispose();
    }
}
