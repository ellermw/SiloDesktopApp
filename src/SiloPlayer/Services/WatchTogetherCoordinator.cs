using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace SiloPlayer.Services;

/// <summary>UI adapter around the deterministic room sync controller. The room socket
/// owns transport authority; standalone playback never enters this controller.</summary>
public sealed class WatchTogetherCoordinator
{
    private readonly PlayerService _playerService;
    private readonly DispatcherQueue _dispatcher;
    private readonly WatchPartySyncController _sync;
    private readonly DispatcherTimer _timer;
    private readonly AuthService _auth;
    private (long Generation, string? Profile)? _roomAuthority;
    private WatchTogetherRoomViewModel? _activeRoom;
    private long _roomJoinGeneration;
    private (long Generation, string? Profile, string Server)? _joinAuthority;
    private string? _attachedSessionId;
    private static long Now => Environment.TickCount64;

    public WatchTogetherCoordinator(PlayerService playerService)
    {
        _playerService = playerService;
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("WatchTogetherCoordinator must be constructed on the UI thread.");
        _sync = new(new NativePlayback(playerService), SendRoomMessage);
        _sync.Notice += message =>
        {
            if (!EnsureCurrentAuthority()) return;
            if (_activeRoom != null) _activeRoom.NoticeMessage = message;
            playerService.ShowWatchPartyNotice(message);
        };
        _sync.LowerQualityRequested += () =>
        {
            if (EnsureCurrentAuthority()) playerService.OfferWatchPartyLowerQuality();
        };
        _sync.ClosePlaybackRequested += session =>
        {
            if (!EnsureCurrentAuthority()) return;
            if (session != _attachedSessionId || playerService.Manager?.SessionId != session) return;
            NotifyPlaybackEnded();
            _ = playerService.CloseAsync(); // Close captures final progress before releasing the matching session.
        };
        _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) =>
        {
            if (!EnsureCurrentAuthority()) return;
            _sync.Tick(Now, DateTimeOffset.UtcNow);
            _playerService.UpdateRoomSeekDisplay();
        };
        var auth = App.Services.GetRequiredService<AuthService>();
        _auth = auth;
        auth.LoggedOut += ClearActiveRoom;
        auth.ProfileVerificationRequired += ClearActiveRoom;
        auth.UserChanged += () => EnsureCurrentAuthority();
        playerService.SessionStarted += session => Dispatch(() => NotifyPlaybackStarted(session));
        playerService.ContentLoaded += () => Dispatch(PushWatchTogetherOverlay);
        playerService.WatchTogetherActionRequested += OnWatchTogetherActionRequested;
        playerService.RoomTransportRequested += RequestTransport;
        playerService.PlaybackEnded += () => Dispatch(NotifyPlaybackEnded);
        playerService.StateChanged += state =>
        {
            if (state == PlayerState.Idle) Dispatch(NotifyPlaybackEnded);
        };
    }

    private void Dispatch(Action action)
    {
        if (_dispatcher.HasThreadAccess) action();
        else _dispatcher.TryEnqueue(() => action());
    }

    public WatchTogetherRoomViewModel? ActiveRoom { get { EnsureCurrentAuthority(); return _activeRoom; } }
    public event Action<string>? PlaybackStartRequested;

    private bool EnsureCurrentAuthority()
    {
        if (_playerService.HasRemoteAuthority)
        {
            if (_activeRoom != null) ClearActiveRoom();
            return false;
        }
        if (_activeRoom == null) return false;
        if (_roomAuthority == (_auth.SessionGeneration, _auth.SelectedProfileId)) return true;
        ClearActiveRoom();
        return false;
    }

    public long BeginRoomJoin()
    {
        _joinAuthority = (_auth.SessionGeneration, _auth.SelectedProfileId, _auth.ConfiguredServerUrl);
        return ++_roomJoinGeneration;
    }

    public bool IsCurrentRoomJoin(long generation) => generation == _roomJoinGeneration
        && !_playerService.HasRemoteAuthority
        && _joinAuthority == (_auth.SessionGeneration, _auth.SelectedProfileId, _auth.ConfiguredServerUrl);

    public bool TryAdoptRoom(WatchTogetherRoomViewModel room, long generation)
    {
        if (!IsCurrentRoomJoin(generation) || !room.CanAdoptRoom) return false;
        SetActiveRoom(room);
        return ReferenceEquals(_activeRoom, room);
    }

    public void SetActiveRoom(WatchTogetherRoomViewModel room)
    {
        InvalidatePendingJoin();
        if (_playerService.HasRemoteAuthority)
        {
            if (_activeRoom != null) ClearActiveRoom();
            return;
        }
        if (ReferenceEquals(_activeRoom, room)) return;
        ClearActiveRoom();
        _activeRoom = room;
        _playerService.RoomSeekPosition = () => EnsureCurrentAuthority() ? _sync.InputSeekPosition(Now) : null;
        _playerService.RoomPlaybackStartPosition = () => EnsureCurrentAuthority() ? _sync.StartupPosition(Now) : null;
        _roomAuthority = (_auth.SessionGeneration, _auth.SelectedProfileId);
        room.TransportCommandReceived += OnTransportCommandReceived;
        room.PropertyChanged += OnRoomPropertyChanged;
        _sync.SetSnapshot(room.Room);
        _sync.SetConnected(room.ConnectionState == "connected", Now);
        _timer.Start();
        if (room.Room?.Phase == "playing" && room.Room.SelectedContentId == _playerService.ContentId &&
            _playerService.Manager?.SessionId is { } session &&
            (room.Room.SelectedFileId == null || room.Room.SelectedFileId == _playerService.ActiveMediaFileId))
        {
            NotifyPlaybackStarted(session);
            room.AcknowledgePlaybackStart();
        }
        ObserveRoom(); // Initial REST snapshot may already be playing before registration.
    }

    public void ClearActiveRoom()
    {
        InvalidatePendingJoin();
        if (_activeRoom != null)
        {
            _activeRoom.TransportCommandReceived -= OnTransportCommandReceived;
            _activeRoom.PropertyChanged -= OnRoomPropertyChanged;
            _activeRoom.DetachSession();
            _activeRoom.Dispose();
        }
        _activeRoom = null;
        _playerService.RoomSeekPosition = null;
        _playerService.RoomPlaybackStartPosition = null;
        _roomAuthority = null;
        _attachedSessionId = null;
        _sync.SetSession(null);
        _sync.SetSnapshot(null);
        _sync.SetConnected(false, Now);
        _timer.Stop();
        _playerService.SetWatchTogetherPlayback(false);
        _playerService.SetWatchTogetherOverlay(null);
    }

    private void InvalidatePendingJoin()
    {
        ++_roomJoinGeneration;
        _joinAuthority = null;
    }

    public void NotifyPlaybackStarted(string sessionId)
    {
        if (!EnsureCurrentAuthority()) return;
        if (_activeRoom?.Room is not { Phase: "playing" } room ||
            room.SelectedContentId != _playerService.ContentId) return;
        _attachedSessionId = sessionId;
        _playerService.SetWatchTogetherPlayback(true);
        _sync.SetSession(sessionId);
        PushWatchTogetherOverlay();
    }

    public void NotifyPlaybackEnded()
    {
        if (_playerService.HasRemoteAuthority)
        {
            if (_activeRoom != null) ClearActiveRoom();
            return;
        }
        _activeRoom?.DetachSession();
        _attachedSessionId = null;
        _sync.SetSession(null);
        _playerService.SetWatchTogetherPlayback(false);
    }

    public void RequestTransport(string action, double positionSeconds, bool isPaused)
    {
        if (!EnsureCurrentAuthority()) return;
        _sync.RequestTransport(action, positionSeconds, isPaused, Now);
        _playerService.UpdateRoomSeekDisplay();
    }

    private void OnRoomPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!EnsureCurrentAuthority()) return;
        if (e.PropertyName == nameof(WatchTogetherRoomViewModel.ConnectionState))
        {
            _sync.SetConnected(_activeRoom?.ConnectionState == "connected", Now);
            PushWatchTogetherOverlay();
        }
        else if (e.PropertyName == nameof(WatchTogetherRoomViewModel.Room)) ObserveRoom();
        else if (e.PropertyName == nameof(WatchTogetherRoomViewModel.ClosedReason) && _activeRoom?.ClosedReason != null)
            _sync.SetSnapshot(null);
        else if (e.PropertyName == nameof(WatchTogetherRoomViewModel.ErrorMessage) && _activeRoom?.ErrorMessage is { } error)
            _playerService.ShowWatchPartyNotice(error);
    }

    private void ObserveRoom()
    {
        if (!EnsureCurrentAuthority()) return;
        var viewModel = _activeRoom;
        if (viewModel == null) return;
        _sync.SetSnapshot(viewModel.Room);
        PushWatchTogetherOverlay();
        if (viewModel.ShouldAutoStartPlayback && viewModel.Room is { Phase: "playing", SelectedContentId: { } contentId })
        {
            viewModel.AcknowledgePlaybackStart();
            _playerService.SetWatchTogetherPlayback(true);
            if (PlaybackStartRequested != null) PlaybackStartRequested.Invoke(contentId);
            else _ = _playerService.PlayAsync(contentId, fileId: viewModel.Room.SelectedFileId, startPositionOverride: viewModel.Room.AnchorPositionSeconds);
        }
    }

    private void PushWatchTogetherOverlay()
    {
        if (!EnsureCurrentAuthority()) return;
        _playerService.SetWatchTogetherOverlay(_activeRoom?.Room, _activeRoom?.ConnectionState ?? "disconnected");
    }

    private void SendRoomMessage(WatchPartyMessage message)
    {
        if (!EnsureCurrentAuthority()) return;
        var room = _activeRoom;
        if (room == null) return;
        switch (message.Type)
        {
            case "attach_session": room.AttachSession(message.SessionId!); break;
            case "transport_request": room.RequestTransport(message.Action!, message.PositionSeconds, message.IsPaused); break;
            case "ready": room.ReportReady(message.PositionSeconds, message.IsPaused, message.CommandId); break;
            case "buffering": room.ReportBuffering(message.PositionSeconds, message.IsPaused); break;
            case "state_report": room.ReportState(message.PositionSeconds, message.IsPaused, message.CommandId); break;
        }
    }

    private void OnTransportCommandReceived(WatchTogetherTransportCommand command)
    {
        if (!EnsureCurrentAuthority()) return;
        _sync.QueueCommand(command, _activeRoom!.RoomClock, DateTimeOffset.UtcNow, Now);
        _sync.Tick(Now, DateTimeOffset.UtcNow);
        _playerService.UpdateRoomSeekDisplay();
    }

    private void OnWatchTogetherActionRequested(string action)
    {
        if (!EnsureCurrentAuthority()) return;
        var room = _activeRoom;
        if (action == "lower-quality")
        {
            _ = _playerService.AcceptWatchPartyLowerQualityAsync();
            return;
        }
        if (room?.Room?.SelfCanManageRoom != true) return;
        switch (action)
        {
            case "invite": CopyInvite(room.Room); break;
            case "toggle-policy":
                if (room.TogglePolicyCommand.CanExecute(null)) room.TogglePolicyCommand.Execute(null);
                break;
            case "end":
                if (room.CloseRoomCommand.CanExecute(null)) room.CloseRoomCommand.Execute(null);
                break;
        }
    }

    private sealed class NativePlayback(PlayerService service) : IWatchPartyPlayback
    {
        public double Position => service.Position;
        public double Duration => service.Duration;
        public bool Paused => service.IsPaused;
        public bool Buffering => service.IsBufferingForCache;
        public bool Busy => service.IsLoading || service.IsSwitchingContent || service.IsNativeSeeking;
        public bool Loaded => !service.HasRemoteAuthority && service.Mpv != null && service.Duration > 0;
        public double Rate
        {
            get => service.NativePlaybackRate;
            set { if (!service.HasRemoteAuthority) service.NativePlaybackRate = value; }
        }
        public string Quality => service.ActiveQualityTier;
        public bool CanSeekLocally(double target) => !service.HasRemoteAuthority && service.CanSeekRoomTargetLocally(target);
        public bool IsTargetBuffered(double target) => !service.HasRemoteAuthority && service.IsRoomTargetBuffered(target);
        public void Seek(double target) { if (!service.HasRemoteAuthority) service.SeekRoomTarget(target); }
        public void SetPaused(bool paused) { if (!service.HasRemoteAuthority) service.SetPaused(paused); }
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


}
