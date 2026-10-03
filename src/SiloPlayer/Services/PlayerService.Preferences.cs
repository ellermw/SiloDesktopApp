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
    private long _seekGeneration;
    public SeekPreferences SeekIntervals => _seekContext is { } context && _settingsApi.IsCurrentContext(context) ? _seekIntervals : new();
    public event Action? SeekPreferencesChanged;
    public async Task RefreshSeekPreferencesAsync(CancellationToken ct = default)
    {
        var context = _settingsApi.CaptureContext();
        var generation = Interlocked.Increment(ref _seekGeneration);
        _seekIntervals = new(); _seekContext = context; // No unscoped local values migrate between profiles.
        try
        {
            var result = await _settingsApi.GetContractEffectiveSettingsAsync(SeekPreferences.Keys, ct: ct);
            if (!_settingsApi.IsCurrentContext(context) || generation != _seekGeneration) return;
            _seekIntervals = SeekPreferences.Read(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { } // Older servers use the documented defaults.
        if (!_settingsApi.IsCurrentContext(context) || generation != _seekGeneration) return;
        PublishSeekPreferences();
    }
    public async Task SaveSeekIntervalAsync(bool audiobook, bool backward, int seconds)
    {
        if (!SeekPreferences.Choices.Contains(seconds)) return;
        var context = _settingsApi.CaptureContext();
        try
        {
            await _settingsApi.SetContractSettingValueAsync(SeekPreferences.Keys[(audiobook ? 2 : 0) + (backward ? 0 : 1)], "profile", seconds);
            if (_settingsApi.IsCurrentContext(context)) await RefreshSeekPreferencesAsync();
        }
        catch { ShowNotice("Seek interval", "Could not save this profile's seek interval. Try again.", "error"); }
    }
    private void PublishSeekPreferences()
    {
        _mpv?.SendScriptMessage("osc-set-seek-intervals", SeekIntervals.VideoBack.ToString(), SeekIntervals.VideoForward.ToString());
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() => InvokeSubscribersSafely(SeekPreferencesChanged, nameof(SeekPreferencesChanged)));
    }
}
