using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private readonly object _playbackBusyLock = new();
    private long _playbackBusyRevision;
    private volatile bool _isLoading;

    // Only a new busy claim, including true -> true, retires an earlier one.
    // FILE_LOADED may release an older load's flags while a replan is awaiting
    // the server; that release is not a successor taking transport ownership.
    private void SetSwitchingContent(bool value)
    {
        lock (_playbackBusyLock)
        {
            if (value) ++_playbackBusyRevision;
            _switchingContent = value;
        }
    }

    private sealed record PlaybackBusyOwner(long Revision, PlaybackAuthorityScope Authority,
        PlaybackManager? Manager, PlaybackStartResponse? Session, MpvPlayer? Mpv,
        MpvVideoWindow? Video, CancellationTokenSource? LoadingToken, long PlayGeneration, long FileGeneration);

    private PlaybackBusyOwner BeginPlaybackBusy(bool loading = true)
    {
        lock (_playbackBusyLock)
        {
            SetSwitchingContent(true);
            if (loading) IsLoading = true;
            return new(_playbackBusyRevision, _activeAuthority, _playbackManager, _playbackManager?.CurrentSession,
                _mpv, _videoWindow, _playbackCts, Volatile.Read(ref _playRequestGeneration),
                Volatile.Read(ref _playbackFileGeneration));
        }
    }

    private bool OwnsPlaybackBusy(PlaybackBusyOwner owner)
    {
        lock (_playbackBusyLock)
            return owner.Revision == _playbackBusyRevision && !_closing && _playbackTerminalState == null &&
                ReferenceEquals(owner.Authority, _activeAuthority) &&
                ReferenceEquals(owner.Manager, _playbackManager) &&
                ReferenceEquals(owner.Session, _playbackManager?.CurrentSession) &&
                ReferenceEquals(owner.Mpv, _mpv) && ReferenceEquals(owner.Video, _videoWindow) &&
                ReferenceEquals(owner.LoadingToken, _playbackCts) &&
                owner.PlayGeneration == Volatile.Read(ref _playRequestGeneration) &&
                owner.FileGeneration == Volatile.Read(ref _playbackFileGeneration);
    }

    private bool ResumePlaybackBusy(PlaybackBusyOwner owner)
    {
        lock (_playbackBusyLock)
        {
            if (!OwnsPlaybackBusy(owner)) return false;
            // Reassert this claim after an earlier FILE_LOADED released flags.
            // Do not create a new claim or adopt a successor's identities.
            _switchingContent = true;
            _isLoading = true;
            return true;
        }
    }

    private bool ReleasePlaybackBusy(PlaybackBusyOwner owner, Action? releaseState = null)
    {
        lock (_playbackBusyLock)
        {
            // A profile change retires publication but can leave this same
            // operation's flags behind. A successor's flags are never ours.
            if (!OwnsPlaybackBusy(owner)) return false;
            ++_playbackBusyRevision;
            SetSwitchingContent(false);
            IsLoading = false;
            releaseState?.Invoke();
            return true;
        }
    }
}
