using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private readonly object _playbackBusyLock = new();
    private long _playbackBusyRevision;
    private volatile bool _isLoading;

    // Every assignment, including true -> true, retires an earlier claim.
    // Switching-only writers include pre-dispatch quality/version, EOF and
    // plan invalidation; they can take ownership before any new file is loaded.
    private void SetSwitchingContent(bool value)
    {
        lock (_playbackBusyLock)
        {
            ++_playbackBusyRevision;
            _switchingContent = value;
        }
    }

    private sealed record PlaybackBusyOwner(long Revision, PlaybackAuthorityScope Authority,
        PlaybackManager? Manager, PlaybackStartResponse? Session, MpvPlayer? Mpv,
        MpvVideoWindow? Video, CancellationTokenSource? LoadingToken, long PlayGeneration, long FileGeneration);

    private PlaybackBusyOwner BeginPlaybackBusy(bool loading = true, Action? claimState = null)
    {
        lock (_playbackBusyLock)
        {
            SetSwitchingContent(true);
            if (loading) IsLoading = true;
            claimState?.Invoke();
            return new(_playbackBusyRevision, _activeAuthority, _playbackManager, _playbackManager?.CurrentSession,
                _mpv, _videoWindow, _playbackCts, Volatile.Read(ref _playRequestGeneration),
                Volatile.Read(ref _playbackFileGeneration));
        }
    }

    private sealed record PlaybackBusyRelease(PlaybackBusyOwner Owner, long Revision);

    private bool OwnsPlaybackBusy(PlaybackBusyOwner owner)
    {
        lock (_playbackBusyLock)
            return owner.Revision == _playbackBusyRevision && BusyPlaybackMatches(owner);
    }

    private PlaybackBusyRelease? ReleasePlaybackBusy(PlaybackBusyOwner owner, Action? releaseState = null)
    {
        lock (_playbackBusyLock)
        {
            // A profile change retires publication but can leave this same
            // operation's flags behind. A successor's flags are never ours.
            if (owner.Revision != _playbackBusyRevision || !BusyPlaybackMatches(owner)) return null;
            releaseState?.Invoke();
            SetSwitchingContent(false);
            IsLoading = false;
            return new(owner, _playbackBusyRevision);
        }
    }

    private bool BusyPlaybackMatches(PlaybackBusyOwner owner) => !_closing &&
        ReferenceEquals(owner.Authority, _activeAuthority) && ReferenceEquals(owner.Manager, _playbackManager) &&
        ReferenceEquals(owner.Session, _playbackManager?.CurrentSession) && ReferenceEquals(owner.Mpv, _mpv) &&
        ReferenceEquals(owner.Video, _videoWindow) && ReferenceEquals(owner.LoadingToken, _playbackCts) &&
        owner.PlayGeneration == Volatile.Read(ref _playRequestGeneration) &&
        owner.FileGeneration == Volatile.Read(ref _playbackFileGeneration);

    private void RunIfBusyReleaseCurrent(PlaybackBusyRelease release, Action action)
    {
        lock (_playbackBusyLock)
        {
            if (release.Revision == _playbackBusyRevision && BusyPlaybackMatches(release.Owner)) action();
        }
    }
}
