using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Services;

/// <summary>
/// Bridges the active <see cref="WatchTogetherRoomViewModel"/> to
/// <see cref="PlayerService"/> for synchronized playback:
///   1. When local playback starts inside an active room, the session is
///      attached server-side so transport commands can be routed to it.
///   2. Inbound <c>transport_command</c> frames (play/pause/seek) are applied
///      to the local mpv player, respecting <c>execute_at</c> scheduling so
///      multiple clients fire the action at the same wall-clock moment.
///   3. Local play/pause/seek actions can be broadcast back to the room.
///   4. Periodic <c>state_report</c> (1.5s) and on-demand <c>ready</c>/
///      <c>buffering</c> messages keep the server's view of each client's
///      playhead accurate for drift detection and lagging-guest policy.
///   5. When the room phase transitions to <c>playing</c> on a new
///      <c>selection_revision</c>, start local playback of the selected item
///      automatically so all members share the experience.
/// </summary>
public sealed class WatchTogetherCoordinator
{
    private readonly PlayerService _playerService;
    private readonly DispatcherQueue _dispatcher;

    private WatchTogetherRoomViewModel? _activeRoom;
    private string? _attachedSessionId;

    // Periodic state_report timer — matches upstream's 1.5s cadence.
    private DispatcherTimer? _stateReportTimer;
    private const int StateReportIntervalMs = 1500;

    // Buffering dedup: only send buffering/ready once per state transition.
    private enum ReadyState { Idle, Ready, Buffering }
    private ReadyState _readyState = ReadyState.Idle;

    public WatchTogetherCoordinator(PlayerService playerService)
    {
        _playerService = playerService;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "WatchTogetherCoordinator must be constructed on the UI thread.");

        _playerService.SessionStarted += NotifyPlaybackStarted;
        _playerService.BufferingChanged += OnLocalBufferingChanged;
        _playerService.ContentLoaded += PushWatchTogetherOverlay;
        _playerService.WatchTogetherActionRequested += OnWatchTogetherActionRequested;
    }

    public WatchTogetherRoomViewModel? ActiveRoom => _activeRoom;

    /// <summary>Fired when the coordinator decides the local client should
    /// navigate to and start playback for a newly-selected room item. The
    /// RoomPage listens and kicks off PlayAsync — the coordinator doesn't
    /// own navigation itself.</summary>
    public event Action<string /* contentId */>? PlaybackStartRequested;

    public void SetActiveRoom(WatchTogetherRoomViewModel room)
    {
        if (ReferenceEquals(_activeRoom, room)) return;
        ClearActiveRoom();
        _activeRoom = room;
        room.TransportCommandReceived += OnTransportCommandReceived;
        room.PropertyChanged += OnRoomPropertyChanged;
        PushWatchTogetherOverlay();

        // If a session is already in flight when the room becomes active,
        // attach it immediately and kick off periodic state reporting.
        var sessionId = _playerService.Manager?.SessionId;
        if (!string.IsNullOrEmpty(sessionId))
        {
            room.AttachSession(sessionId!);
            _attachedSessionId = sessionId;
            StartStateReportTimer();
        }
    }

    public void ClearActiveRoom()
    {
        if (_activeRoom == null) return;
        _activeRoom.TransportCommandReceived -= OnTransportCommandReceived;
        _activeRoom.PropertyChanged -= OnRoomPropertyChanged;
        _activeRoom.DetachSession();
        _activeRoom = null;
        _attachedSessionId = null;
        _playerService.SetWatchTogetherOverlay(null);
        StopStateReportTimer();
        _readyState = ReadyState.Idle;
    }

    /// <summary>
    /// Called by <see cref="PlayerService"/> after a new playback session starts.
    /// Triggers <c>attach_session</c> on the active room WS so subsequent
    /// transport commands are routed to this session.
    /// </summary>
    public void NotifyPlaybackStarted(string sessionId)
    {
        if (_activeRoom == null) return;
        if (sessionId == _attachedSessionId) return;
        _activeRoom.AttachSession(sessionId);
        _attachedSessionId = sessionId;
        _readyState = ReadyState.Idle;
        StartStateReportTimer();
        PushWatchTogetherOverlay();
    }

    public void NotifyPlaybackEnded()
    {
        if (_activeRoom == null) return;
        _activeRoom.DetachSession();
        _attachedSessionId = null;
        StopStateReportTimer();
    }

    /// <summary>
    /// Broadcast a local transport action (user clicked play/pause/seek) to
    /// the room. Server adjudicates based on room policy and echoes back.
    /// </summary>
    public void RequestTransport(string action, double positionSeconds, bool isPaused)
    {
        _activeRoom?.RequestTransport(action, positionSeconds, isPaused);
    }

    // ── Room property watcher ────────────────────────────────────────────

    private void OnRoomPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_activeRoom == null) return;
        if (e.PropertyName is nameof(WatchTogetherRoomViewModel.Room)
            or nameof(WatchTogetherRoomViewModel.ConnectionState))
        {
            PushWatchTogetherOverlay();
        }
        if (e.PropertyName != nameof(WatchTogetherRoomViewModel.Room)) return;

        if (_activeRoom.ShouldAutoStartPlayback &&
            !string.IsNullOrEmpty(_activeRoom.Room?.SelectedContentId))
        {
            var contentId = _activeRoom.Room.SelectedContentId!;
            _activeRoom.AcknowledgePlaybackStart();
            _readyState = ReadyState.Idle;
            _dispatcher.TryEnqueue(() =>
            {
                try { PlaybackStartRequested?.Invoke(contentId); }
                catch { }
            });
        }

        // If we're now waiting and have buffered playback, tell the server
        // we're ready so it can release the sync gate for everyone.
        if (_activeRoom.Room?.PlaybackState == "waiting"
            && _readyState != ReadyState.Ready
            && !string.IsNullOrEmpty(_attachedSessionId)
            && _playerService.Duration > 0
            && !_playerService.IsBufferingForCache)
        {
            _readyState = ReadyState.Ready;
            _activeRoom.ReportReady(_playerService.Position, _playerService.IsPaused);
        }
    }

    private void PushWatchTogetherOverlay()
    {
        _playerService.SetWatchTogetherOverlay(
            _activeRoom?.Room,
            _activeRoom?.ConnectionState ?? "disconnected");
    }

    private void OnWatchTogetherActionRequested(string action)
    {
        var roomViewModel = _activeRoom;
        var room = roomViewModel?.Room;
        if (roomViewModel == null || room == null || !room.SelfCanManageRoom)
            return;

        switch (action)
        {
            case "invite":
                CopyInvite(room);
                break;
            case "toggle-policy":
                if (roomViewModel.TogglePolicyCommand.CanExecute(null))
                    roomViewModel.TogglePolicyCommand.Execute(null);
                break;
            case "end":
                if (roomViewModel.CloseRoomCommand.CanExecute(null))
                    roomViewModel.CloseRoomCommand.Execute(null);
                break;
        }
    }

    private static void CopyInvite(WatchTogetherRoomSnapshot room)
    {
        try
        {
            var invite = room.InvitePath;
            if (string.IsNullOrWhiteSpace(invite))
                invite = room.Code;
            else if (!Uri.TryCreate(invite, UriKind.Absolute, out _))
            {
                var apiBase = App.Services.GetRequiredService<SiloApiClient>().BaseUrl;
                var origin = new Uri(apiBase).GetLeftPart(UriPartial.Authority);
                invite = new Uri(new Uri(origin), invite).ToString();
            }

            var package = new DataPackage();
            package.SetText(invite ?? room.Code);
            Clipboard.SetContent(package);
        }
        catch
        {
            // Clipboard may be unavailable while Windows is switching secure desktops.
        }
    }

    // ── Inbound transport_command ────────────────────────────────────────

    private void OnTransportCommandReceived(WatchTogetherTransportCommand cmd)
    {
        var roomAtReceipt = _activeRoom;
        var sessionAtReceipt = _attachedSessionId;
        if (roomAtReceipt == null) return;
        if (!string.IsNullOrEmpty(cmd.SessionId) &&
            cmd.SessionId != sessionAtReceipt) return;

        // If the server scheduled this command for a future wall-clock time,
        // defer execution so all clients fire together. <=0ms → execute now.
        int delayMs = ComputeExecuteDelayMs(cmd.ExecuteAt);
        if (delayMs <= 0)
        {
            _dispatcher.TryEnqueue(() => ApplyCommandIfCurrent(cmd, roomAtReceipt, sessionAtReceipt));
        }
        else
        {
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(delayMs); } catch { }
                _dispatcher.TryEnqueue(() => ApplyCommandIfCurrent(cmd, roomAtReceipt, sessionAtReceipt));
            });
        }
    }

    private void ApplyCommandIfCurrent(
        WatchTogetherTransportCommand cmd,
        WatchTogetherRoomViewModel roomAtReceipt,
        string? sessionAtReceipt)
    {
        if (!ReferenceEquals(_activeRoom, roomAtReceipt)
            || !string.Equals(_attachedSessionId, sessionAtReceipt, StringComparison.Ordinal)
            || (!string.IsNullOrEmpty(cmd.SessionId)
                && !string.Equals(cmd.SessionId, _attachedSessionId, StringComparison.Ordinal)))
            return;

        ApplyCommand(cmd);
    }

    private int ComputeExecuteDelayMs(string? executeAtIso)
    {
        if (string.IsNullOrEmpty(executeAtIso) || _activeRoom == null) return 0;
        if (!DateTime.TryParse(executeAtIso, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var executeAt))
            return 0;
        // execute_at is server-wall-clock; convert to local by subtracting offset.
        var localExecute = executeAt.ToUniversalTime()
            - TimeSpan.FromMilliseconds(_activeRoom.ServerTimeOffsetMs);
        var delta = (int)(localExecute - DateTime.UtcNow).TotalMilliseconds;
        // Clamp — tiny negatives fire immediately, huge positives cap at 3s
        // (beyond that the server probably meant "now" and the clocks drifted).
        return Math.Clamp(delta, 0, 3000);
    }

    private void ApplyCommand(WatchTogetherTransportCommand cmd)
    {
        try
        {
            switch (cmd.Action)
            {
                case "play":
                    _playerService.SetPaused(false);
                    break;
                case "pause":
                    _playerService.SetPaused(true);
                    break;
                case "seek":
                    _playerService.SeekTo(cmd.PositionSeconds);
                    break;
            }
        }
        catch { }
    }

    // ── State reporting (1.5s polling) ───────────────────────────────────

    private void StartStateReportTimer()
    {
        if (_stateReportTimer != null) return;
        _dispatcher.TryEnqueue(() =>
        {
            if (_stateReportTimer != null) return;
            _stateReportTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(StateReportIntervalMs),
            };
            _stateReportTimer.Tick += (_, _) => TickStateReport();
            _stateReportTimer.Start();
        });
    }

    private void StopStateReportTimer()
    {
        _dispatcher.TryEnqueue(() =>
        {
            _stateReportTimer?.Stop();
            _stateReportTimer = null;
        });
    }

    private void TickStateReport()
    {
        if (_activeRoom == null || string.IsNullOrEmpty(_attachedSessionId)) return;
        if (_playerService.Duration <= 0) return; // Not yet loaded.
        _activeRoom.ReportState(_playerService.Position, _playerService.IsPaused);
    }

    // ── Buffering relay ──────────────────────────────────────────────────

    private void OnLocalBufferingChanged(bool buffering)
    {
        if (_activeRoom == null || string.IsNullOrEmpty(_attachedSessionId)) return;

        if (buffering && _readyState != ReadyState.Buffering)
        {
            _readyState = ReadyState.Buffering;
            _dispatcher.TryEnqueue(() =>
                _activeRoom?.ReportBuffering(_playerService.Position, _playerService.IsPaused));
        }
        else if (!buffering && _readyState == ReadyState.Buffering)
        {
            _readyState = ReadyState.Ready;
            _dispatcher.TryEnqueue(() =>
                _activeRoom?.ReportReady(_playerService.Position, _playerService.IsPaused));
        }
    }
}
