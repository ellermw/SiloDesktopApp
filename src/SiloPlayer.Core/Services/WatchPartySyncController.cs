using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>The native adapter reports canonical media time, never transport-relative time.</summary>
public interface IWatchPartyPlayback
{
    double Position { get; }
    double Duration { get; }
    bool Paused { get; }
    bool Buffering { get; }
    bool Busy { get; }
    bool Loaded { get; }
    double Rate { get; set; }
    string Quality { get; }
    bool CanSeekLocally(double target);
    bool IsTargetBuffered(double target);
    void Seek(double target);
    void SetPaused(bool paused);
}

public sealed record WatchPartyMessage(string Type, string? SessionId, string? CommandId = null,
    string? Action = null, double PositionSeconds = 0, bool IsPaused = false);

/// <summary>
/// Room synchronization state machine, shared by the native coordinator and deterministic fixtures.
/// All calls occur on the UI dispatcher. Time arguments are monotonic milliseconds.
/// User intents produce messages only; applied server commands produce player actions only.
/// </summary>
public sealed class WatchPartySyncController(IWatchPartyPlayback player, Action<WatchPartyMessage> send)
{
    private WatchTogetherRoomSnapshot? _room;
    private string? _session;
    private string? _phase;
    private bool _connected;
    private long? _disconnectedAt;
    private bool _reconnectNotice;
    private WatchTogetherTransportCommand? _pending;
    private long _executeAt;
    private WatchTogetherTransportCommand? _applied;
    private readonly HashSet<string> _seenCommands = [];
    private readonly Queue<string> _commandOrder = [];
    private long _appliedAt;
    private long _lastReport = long.MinValue / 2;
    private long? _stallStarted;
    private bool _stallReported;
    private bool _recovering;
    private readonly Queue<long> _stalls = [];
    private bool _qualityOffered;
    private string? _quality;
    private double? _restoreRate;
    private double _assignedRate;
    private double? _rateTarget;
    private long _rateStarted;
    private double? _reloadTarget;
    private long _reloadStarted;
    private long _nextReload;
    private int _reloadAttempts;
    private double _reloadLead;
    private string? _nativeLandingCommand;
    private bool _seekLanded;

    public event Action<string>? Notice;
    public event Action<string>? ClosePlaybackRequested;
    public event Action? LowerQualityRequested;
    public bool HasSession => _session != null;

    public void SetSnapshot(WatchTogetherRoomSnapshot? room)
    {
        var stopped = _phase == "playing" && room?.Phase != "playing";
        var changed = _room?.RoomId != room?.RoomId || _room?.SelectionRevision != room?.SelectionRevision;
        var previous = _room;
        _room = room;
        _phase = room?.Phase;
        if (stopped && _session is { } session)
        {
            Notice?.Invoke(player.Duration > 0 && player.Duration - player.Position <= 5
                ? "Playback finished." : "The host stopped playback.");
            SetSession(null);
            ClosePlaybackRequested?.Invoke(session);
        }
        if (changed) ResetSynchronization();
        if (changed && previous != null && room?.Phase == "playing") SetSession(null);
        if (previous?.PlaybackState == "waiting" && room?.PlaybackState == "playing")
        {
            var leftBehind = room.Members.Where(m => !m.IsSelf && !m.IsReady &&
                previous.Members.Any(p => p.UserId == m.UserId && p.ProfileId == m.ProfileId && p.IsSyncing))
                .Select(m => m.DisplayName).ToArray();
            if (leftBehind.Length > 0) Notice?.Invoke($"The party continued while {string.Join(", ", leftBehind)} catches up.");
        }
    }

    public void SetSession(string? session)
    {
        if (_session == session) return;
        ResetSynchronization();
        _session = session;
        _quality = null;
        _stalls.Clear();
        _qualityOffered = false;
        Attach();
    }

    public void SetConnected(bool connected, long now)
    {
        if (_connected == connected) return;
        _connected = connected;
        if (connected)
        {
            _disconnectedAt = null;
            if (_reconnectNotice) Notice?.Invoke("Reconnected to the room.");
            _reconnectNotice = false;
            // The socket owns attachment. An unchanged playback ID still needs a new attach.
            _pending = null;
            _applied = null;
            _seenCommands.Clear();
            _commandOrder.Clear();
            _stallReported = false;
            Attach();
        }
        else
        {
            _disconnectedAt = now;
            _pending = null;
            RestoreRate();
        }
    }

    private void Attach()
    {
        if (_connected && _session != null && _room?.Phase == "playing")
            send(new("attach_session", _session));
    }

    public bool RequestTransport(string action, double position, bool paused)
    {
        if (_room?.Phase != "playing" || _session == null || !_connected)
        {
            Notice?.Invoke("Reconnecting to room. Controls are temporarily unavailable.");
            return false;
        }
        if (action is not ("play" or "pause" or "seek") || !double.IsFinite(position)) return false;
        if (action == "seek" ? !_room.SelfCanManageRoom : !_room.SelfCanControlTransport)
        {
            Notice?.Invoke(action == "seek" ? "Only the host can seek the room." : "Only the host can control playback.");
            return false;
        }
        send(new("transport_request", _session, Action: action,
            PositionSeconds: Math.Clamp(position, 0, Math.Max(player.Duration, position)), IsPaused: paused));
        return true;
    }

    public void QueueCommand(WatchTogetherTransportCommand command, long executeAt)
    {
        if (!_connected || _session == null || _room?.Phase != "playing" ||
            command.SelectionRevision != _room.SelectionRevision ||
            (!string.IsNullOrEmpty(command.SessionId) && command.SessionId != _session) ||
            command.Action is not ("play" or "pause" or "seek") ||
            !double.IsFinite(command.PositionSeconds) || string.IsNullOrEmpty(command.CommandId) ||
            !_seenCommands.Add(command.CommandId)) return;
        _commandOrder.Enqueue(command.CommandId);
        while (_commandOrder.Count > 128) _seenCommands.Remove(_commandOrder.Dequeue());
        _pending = command;
        _executeAt = executeAt;
    }

    public void Tick(long now)
    {
        if (!_connected)
        {
            if (_session != null && _disconnectedAt is { } disconnected && now - disconnected >= 2000 && !_reconnectNotice)
            {
                _reconnectNotice = true;
                Notice?.Invoke("Reconnecting to room. Controls are temporarily unavailable.");
            }
            return;
        }
        if (_session == null || _room?.Phase != "playing") return;
        if (_quality != player.Quality)
        {
            _quality = player.Quality;
            _stalls.Clear();
            _qualityOffered = false;
        }
        if (_pending != null && now >= _executeAt && player.Loaded && !player.Busy)
        {
            var command = _pending;
            _pending = null;
            Apply(command, now);
        }
        if (!player.Loaded || player.Busy) return;
        if (player.Buffering)
        {
            _stallStarted ??= now;
            if (!_stallReported && now - _stallStarted >= 2000)
            {
                _stallReported = true;
                _recovering = true;
                send(new("buffering", _session, PositionSeconds: player.Position, IsPaused: player.Paused));
                _stalls.Enqueue(now);
                while (_stalls.TryPeek(out var at) && now - at >= 300_000) _stalls.Dequeue();
                if (_stalls.Count >= 2 && !_qualityOffered)
                {
                    _qualityOffered = true;
                    LowerQualityRequested?.Invoke();
                }
            }
            return; // Do not let stale positions release the server's readiness barrier.
        }
        _stallStarted = null;
        _stallReported = false;
        if (_applied?.Action == "seek" && Math.Abs(player.Position - _applied.PositionSeconds) <= ReadyTolerance)
            _seekLanded = true;
        if (_reloadTarget is { } target && now > _reloadStarted + 250 &&
            player.Position >= target - .35 && player.Position <= target + 2)
        {
            _reloadLead = Math.Clamp((now - _reloadStarted) / 1000d, 0, 10);
            _reloadTarget = null;
            _nextReload = now + ReloadBackoff;
        }
        if (_rateTarget is { } rateTarget)
        {
            var expected = rateTarget + Math.Max(0, (now - _rateStarted) / 1000d);
            if (Math.Abs(expected - player.Position) <= .35 || player.Paused || now - _rateStarted >= 20_000)
            {
                RestoreRate();
                if (Math.Abs(expected - player.Position) <= .35) _reloadAttempts = 0;
            }
        }
        if (_pending != null || now - _appliedAt < 250) return;
        var self = _room.Members.FirstOrDefault(m => m.IsSelf);
        var readinessPending = _room.PlaybackState == "waiting" || _recovering ||
            _room.SelfIgnoreWait || self?.IsBuffering == true;
        if (readinessPending && _applied is { Action: "seek" } seekCommand && !ReadyAtTarget() &&
            now - _appliedAt >= 500 && _nativeLandingCommand != seekCommand.CommandId &&
            seekCommand.PositionSeconds - player.Position is > 0 and <= 20 &&
            player.CanSeekLocally(seekCommand.PositionSeconds))
        {
            // A remux can start on an earlier keyframe. Once mpv exposes the
            // target in its cached ranges, exact native seeking decodes/discards
            // the preroll without changing the viewer's mute, speed or pause.
            _nativeLandingCommand = seekCommand.CommandId;
            player.Seek(seekCommand.PositionSeconds);
            return;
        }
        if (readinessPending && self?.IsReady != true && ReadyAtTarget())
        {
            if (now - _lastReport >= 500)
            {
                send(new("ready", _session, _applied!.CommandId, PositionSeconds: player.Position, IsPaused: player.Paused));
                _lastReport = now;
                _recovering = false;
            }
            return;
        }
        // Waiting with a not-yet-landed seek must never send a bare state_report:
        // the server can interpret a matching state as readiness even without is_ready.
        if (readinessPending && !ReadyAtTarget()) return;
        if (now - _lastReport >= 1500)
        {
            send(new("state_report", _session, PositionSeconds: player.Position, IsPaused: player.Paused));
            _lastReport = now;
        }
    }

    private double ReadyTolerance => _room?.SelfCanManageRoom == true ? 15 : 1;
    private bool ReadyAtTarget() => _applied != null &&
        (_applied.Action != "seek" || (_seekLanded && _room?.PlaybackState != "waiting") ||
            Math.Abs(player.Position - _applied.PositionSeconds) <= ReadyTolerance);

    private long ReloadBackoff => (long)Math.Min(60_000, 10_000 * Math.Pow(2, Math.Max(0, _reloadAttempts - 1)));

    private void Apply(WatchTogetherTransportCommand command, long now)
    {
        RestoreRate();
        var delta = command.PositionSeconds - player.Position;
        var local = player.CanSeekLocally(command.PositionSeconds);
        var seek = command.Action == "seek" || (Math.Abs(delta) > .35 && local);
        if (!seek && command.Action == "play" && Math.Abs(delta) > .35)
        {
            if (Math.Abs(delta) <= 2)
            {
                _restoreRate = player.Rate;
                _assignedRate = Math.Clamp(1 + delta / 8, .9, 1.25);
                player.Rate = _assignedRate;
                _rateTarget = command.PositionSeconds;
                _rateStarted = now;
            }
            else seek = true;
        }
        if (seek)
        {
            var explicitSeek = command.Action == "seek";
            var buffered = player.IsTargetBuffered(command.PositionSeconds);
            var allowed = explicitSeek || buffered || (_reloadTarget != null
                ? now - _reloadStarted >= 30_000 : now >= _nextReload);
            if (allowed)
            {
                var target = command.PositionSeconds;
                if (!buffered && !explicitSeek)
                {
                    target = Math.Min(target + _reloadLead, Math.Max(target, player.Duration));
                    _reloadTarget = target;
                    _reloadStarted = now;
                    _reloadAttempts++;
                }
                else if (explicitSeek) _reloadTarget = null;
                // Pause first, so asynchronous native reanchors capture the room state.
                player.SetPaused(command.Action == "pause" || command.PlaybackState is "waiting" or "paused");
                player.Seek(target);
            }
        }
        if (command.Action == "play") player.SetPaused(false);
        else if (command.Action == "pause" || command.PlaybackState is "waiting" or "paused") player.SetPaused(true);
        _applied = command;
        _seekLanded = false;
        _appliedAt = now;
    }

    private void RestoreRate()
    {
        // If the viewer explicitly changed rate during convergence, keep that choice.
        if (_restoreRate is { } previous && Math.Abs(player.Rate - _assignedRate) < .001) player.Rate = previous;
        _restoreRate = null;
        _rateTarget = null;
    }

    public void ResetSynchronization()
    {
        RestoreRate();
        _pending = _applied = null;
        _seenCommands.Clear();
        _commandOrder.Clear();
        _reloadTarget = null;
        _reloadAttempts = 0;
        _reloadLead = 0;
        _nativeLandingCommand = null;
        _seekLanded = false;
        _nextReload = 0;
        _stallStarted = null;
        _stallReported = false;
        _recovering = false;
        _lastReport = long.MinValue / 2;
    }
}

