using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private readonly StartupFrameTelemetry _startupTelemetry = new();
    private long _viewerPlaybackIntent;
    public void BeginViewerPlaybackIntent() => Interlocked.Exchange(ref _viewerPlaybackIntent, System.Diagnostics.Stopwatch.GetTimestamp());
    private void ResetStartupTelemetry()
    {
        var requested = Interlocked.Exchange(ref _viewerPlaybackIntent, 0);
        _startupTelemetry.Reset(requested > 0 ? requested : null);
    }
    private void ReportFirstOutput()
    {
        var session = _playbackManager?.CurrentSession;
        if (session?.ProtocolVersion != 3 || IsAudiobook) return;
        var data = _startupTelemetry.Observe(session.SessionId + ":" + session.PlanAttemptKey, System.Diagnostics.Stopwatch.GetTimestamp());
        if (data != null) _ = _playbackManager!.ReportRouteEventAsync(session, "first_frame", data);
    }
    private SeekPreferences _seekIntervals = new();
    private SiloPlayer.Core.Api.ApiRequestContext? _seekContext;
    private PlaybackAuthorityScope? _seekAuthority;
    private long _seekGeneration;
    public SeekPreferences SeekIntervals => _seekAuthority is { } authority && ReferenceEquals(authority, _activeAuthority) &&
        _seekContext is { } context && authority.SettingsApi.IsCurrentContext(context) ? _seekIntervals : new();
    public event Action? SeekPreferencesChanged;
    public async Task RefreshSeekPreferencesAsync(CancellationToken ct = default)
    {
        var authority = _activeAuthority;
        var api = authority.SettingsApi;
        var context = api.CaptureContext();
        var generation = Interlocked.Increment(ref _seekGeneration);
        _seekIntervals = new(); _seekContext = context; _seekAuthority = authority; // No unscoped local values migrate between profiles.
        try
        {
            var result = await api.GetContractEffectiveSettingsAsync(SeekPreferences.Keys, ct: ct);
            if (!ReferenceEquals(authority, _activeAuthority) || !api.IsCurrentContext(context) || generation != _seekGeneration) return;
            _seekIntervals = SeekPreferences.Read(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { } // Older servers use the documented defaults.
        if (!ReferenceEquals(authority, _activeAuthority) || !api.IsCurrentContext(context) || generation != _seekGeneration) return;
        PublishSeekPreferences();
    }
    public async Task SaveSeekIntervalAsync(bool audiobook, bool backward, int seconds)
    {
        if (!SeekPreferences.Choices.Contains(seconds)) return;
        var authority = _activeAuthority;
        var api = authority.SettingsApi;
        var context = api.CaptureContext();
        try
        {
            await api.SetContractSettingValueAsync(SeekPreferences.Keys[(audiobook ? 2 : 0) + (backward ? 0 : 1)], "profile", seconds);
            if (ReferenceEquals(authority, _activeAuthority) && api.IsCurrentContext(context)) await RefreshSeekPreferencesAsync();
        }
        catch { ShowNotice("Seek interval", "Could not save this profile's seek interval. Try again.", "error"); }
    }
    private void PublishSeekPreferences()
    {
        _mpv?.SendScriptMessage("osc-set-seek-intervals", SeekIntervals.VideoBack.ToString(), SeekIntervals.VideoForward.ToString());
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() => InvokeSubscribersSafely(SeekPreferencesChanged, nameof(SeekPreferencesChanged)));
    }
}
