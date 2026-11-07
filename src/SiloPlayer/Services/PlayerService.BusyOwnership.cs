using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private readonly object _playbackBusyLock = new();
    private long _playbackBusyRevision;
    private long _mpvLoadBusyRevision;
    private volatile bool _isLoading;
    private long _roomPlaybackGeneration;
    private CancellationTokenSource? _soloVersionSwitchCts;

    private void CancelPendingSoloVersionSwitch()
    {
        var pending = Volatile.Read(ref _soloVersionSwitchCts);
        try { pending?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

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

    private readonly record struct TemporarySwitchingGuard(long Revision, bool WasSwitching);

    private TemporarySwitchingGuard BeginTemporarySwitchingContent()
    {
        lock (_playbackBusyLock)
        {
            var guard = new TemporarySwitchingGuard(_playbackBusyRevision, _switchingContent);
            // Waiting for an invalidation check is not a new playback operation:
            // it may be a no-op behind an in-flight switch's accepted replan.
            _switchingContent = true;
            return guard;
        }
    }

    private void RestoreTemporarySwitchingContent(TemporarySwitchingGuard guard)
    {
        lock (_playbackBusyLock)
        {
            // Restore only the borrowed flag, never a successor operation/load.
            if (guard.Revision == _playbackBusyRevision)
                _switchingContent = guard.WasSwitching;
        }
    }

    private sealed record PlaybackBusyOwner(long Revision, PlaybackAuthorityScope Authority,
        PlaybackManager? Manager, PlaybackStartResponse? Session, MpvPlayer? Mpv,
        MpvVideoWindow? Video, CancellationTokenSource? LoadingToken, long PlayGeneration, long FileGeneration,
        bool MatchSession = true);

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

    private PlaybackBusyOwner BeginMpvLoadBusy()
    {
        lock (_playbackBusyLock)
        {
            var owner = BeginPlaybackBusy();
            _mpvLoadBusyRevision = owner.Revision;
            return owner;
        }
    }

    private void CompleteMpvLoadBusy(long revision)
    {
        lock (_playbackBusyLock)
        {
            // FILE_LOADED belongs to the submitted load, not a selection that
            // started replanning (or claimed legacy dispatch) while it opened.
            if (revision != _playbackBusyRevision) return;
            _qualitySwitchActive = false;
            SetSwitchingContent(false);
            IsLoading = false;
        }
    }

    private sealed record PlaybackBusyRelease(PlaybackBusyOwner Owner, long Revision);

    private bool OwnsPlaybackBusy(PlaybackBusyOwner owner)
    {
        lock (_playbackBusyLock)
            return owner.Revision == _playbackBusyRevision && _playbackTerminalState == null && BusyPlaybackMatches(owner);
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

    private PlaybackBusyRelease? ReleasePlaybackBusy(PlaybackBusyOwner owner, Action? releaseState = null)
    {
        lock (_playbackBusyLock)
        {
            // A profile change retires publication but can leave this same
            // operation's flags behind. A successor's flags are never ours.
            if (owner.Revision != _playbackBusyRevision || !BusyPlaybackMatches(owner)) return null;
            ++_playbackBusyRevision;
            SetSwitchingContent(false);
            IsLoading = false;
            var releasedRevision = _playbackBusyRevision;
            releaseState?.Invoke();
            return new(owner, releasedRevision);
        }
    }

    private bool BusyPlaybackMatches(PlaybackBusyOwner owner) => !_closing &&
        ReferenceEquals(owner.Authority, _activeAuthority) && ReferenceEquals(owner.Manager, _playbackManager) &&
        (!owner.MatchSession || ReferenceEquals(owner.Session, _playbackManager?.CurrentSession)) && ReferenceEquals(owner.Mpv, _mpv) &&
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

    // Legacy OSC selection must guard EOF before UI dispatch, but that early
    // claim also needs a lifetime when dispatch/drain retires the selection.
    private void DispatchLegacyPlaybackSwitch(Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
        bool quality, Func<Func<bool>, Task> selection)
    {
        if (_playbackTerminalState != null) return;
        var authority = _activeAuthority;
        var context = authority.ApiClient.CaptureIdentityContext();
        // An in-flight switch can adopt a session before this selection finishes
        // draining. This early claim owns dispatch, not that session snapshot;
        // the switch core takes a session-bound claim once the drain completes.
        var claim = BeginPlaybackBusy(loading: false, claimState: () => _qualitySwitchActive = quality)
            with { MatchSession = false };
        bool ContextCurrent() => _playbackTerminalState == null && ReferenceEquals(authority, _activeAuthority) &&
            authority.ApiClient.IsCurrentIdentityContext(context);
        bool Current() => ContextCurrent() && OwnsPlaybackBusy(claim);
        async Task InvokeSelectionAsync()
        {
            try { if (Current()) await selection(Current); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                ReleasePlaybackBusy(claim, () =>
                {
                    _qualitySwitchActive = false;
                    if (!ContextCurrent()) return;
                    LogToFile("player_quality_switch_error.txt", error.ToString());
                    var (title, detail) = DescribePlaybackError(error);
                    ErrorMessage = detail;
                    ShowNotice(title, detail, "error");
                });
            }
            finally { ReleasePlaybackBusy(claim, () => _qualitySwitchActive = false); }
        }
        if (!dispatcher.TryEnqueue(() => _ = InvokeSelectionAsync()))
            ReleasePlaybackBusy(claim, () => _qualitySwitchActive = false);
    }
}
