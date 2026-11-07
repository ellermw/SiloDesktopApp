using System.Globalization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private VideoPlaybackRateSession? _videoPlaybackRate;
    private Func<bool>? _videoPlaybackRateOwner;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _videoSpeedDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    public double DesiredVideoPlaybackRate => _videoPlaybackRate?.DesiredRate ?? 1;
    public bool CanChangeVideoPlaybackRate => _videoPlaybackRate?.CanChange == true;
    public string? VideoPlaybackRateError => _videoPlaybackRate?.Error;
    public bool VideoPlaybackRatePending => _videoPlaybackRate?.IsSavePending == true;
    public event Action? VideoPlaybackRateChanged;

    /// <summary>Viewer intent only. Room drift and SiloCast commands use their existing native-rate paths.</summary>
    public Task SetVideoPlaybackRateAsync(double rate)
    {
        var session = _videoPlaybackRate;
        return VideoPlaybackRatePreferences.IsValidRate(rate) && session?.CanChange == true
            ? session.SetAsync(rate) : Task.CompletedTask;
    }

    private void ResetVideoPlaybackRateSession()
    {
        _videoPlaybackRate?.Retire();
        _videoPlaybackRate = null;
        _videoPlaybackRateOwner = null;
        PublishVideoPlaybackRateStatus();
    }

    private void EnsureVideoPlaybackRateSession()
    {
        if (_videoPlaybackRateOwner?.Invoke() == true) return;
        ResetVideoPlaybackRateSession();
        // Temporary identities have no desktop preference side effects, and
        // audiobooks retain their separate per-title local preference path.
        if (_closing || HasRemoteAuthority || IsAudiobook || string.IsNullOrWhiteSpace(ContentId)
            || ActiveMediaFileId == null || string.IsNullOrWhiteSpace(_apiClient.DeviceId)) return;
        var authority = _activeAuthority;
        var manager = _playbackManager;
        var context = authority.SettingsApi.CaptureContext();
        if (string.IsNullOrWhiteSpace(context.ProfileId)) return;
        var contentId = ContentId;
        var generation = Volatile.Read(ref _playRequestGeneration);
        // Source-version replacements keep the same viewer intent and pending
        // save. A new playback request, not a new media file, retires this owner.
        VideoPlaybackRateSession? session = null;
        bool Current() => !_closing && ReferenceEquals(_videoPlaybackRate, session)
            && ReferenceEquals(authority, _activeAuthority) && ReferenceEquals(manager, _playbackManager)
            && authority.SettingsApi.IsCurrentContext(context) && ContentId == contentId
            && generation == Volatile.Read(ref _playRequestGeneration);
        bool Editable() => !IsAudiobook && !HasRemoteAuthority && !IsWatchTogetherPlayback
            && State != PlayerState.Idle && !_switchingContent && !IsLoading;
        session = authority.SettingsApi.VideoPlaybackRate.CreateSession(Current, Editable,
            rate =>
            {
                if (Current() && Editable())
                    _mpv?.SetProperty("speed", rate.ToString("R", CultureInfo.InvariantCulture));
            }, () => PublishVideoPlaybackRateStatus(session));
        _videoPlaybackRate = session;
        _videoPlaybackRateOwner = Current;
        // Optional settings work must never become a playback-startup barrier.
        _ = session.LoadAsync(_authorityLifetimeCts.Token);
    }

    private void RestoreVideoPlaybackRate()
    {
        EnsureVideoPlaybackRateSession();
        _videoPlaybackRate?.Restore();
        PublishVideoPlaybackRateStatus();
    }

    private void PublishVideoPlaybackRateStatus(VideoPlaybackRateSession? expected = null)
    {
        void Publish()
        {
            if (expected != null && !ReferenceEquals(expected, _videoPlaybackRate)) return;
            InvokeSubscribersSafely(VideoPlaybackRateChanged, nameof(VideoPlaybackRateChanged));
        }
        if (_videoSpeedDispatcher?.HasThreadAccess == true) Publish();
        else _videoSpeedDispatcher?.TryEnqueue(Publish);
    }
}
