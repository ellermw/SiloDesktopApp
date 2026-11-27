using SiloPlayer.Core.Api;
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
    private readonly object _seekSettingsGate = new();
    private ApiRequestContext? _seekContext;
    private PlaybackAuthorityScope? _seekAuthority;
    private long _seekGeneration;
    public SeekPreferences SeekIntervals
    {
        get
        {
            lock (_seekSettingsGate)
                return _seekAuthority is { } authority && ReferenceEquals(authority, _activeAuthority) &&
                    _seekContext is { } context && authority.SettingsApi.IsCurrentContext(context) ? _seekIntervals : new();
        }
    }
    public event Action? SeekPreferencesChanged;
    public async Task RefreshSeekPreferencesAsync(CancellationToken ct = default)
    {
        var authority = _activeAuthority;
        var api = authority.SettingsApi;
        var context = api.CaptureContext();
        long generation;
        lock (_seekSettingsGate) generation = Interlocked.Increment(ref _seekGeneration);
        ContractEffectiveSettingsResponse result;
        try
        {
            result = await api.GetContractEffectiveSettingsAsync(context, SeekPreferences.Keys, ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return; } // Unsupported servers use defaults for a new owner.
        lock (_seekSettingsGate)
        {
            if (!ReferenceEquals(authority, _activeAuthority) || !api.IsCurrentContext(context) || generation != _seekGeneration) return;
            AcceptSeekPreferences(authority, context, SeekPreferences.Read(result, SeekIntervals));
        }
    }

    private void ApplyLiveSeekPreferences(PlaybackAuthorityScope authority, ApiRequestContext context, ContractEffectiveSettingsResponse settings)
    {
        lock (_seekSettingsGate)
        {
            Interlocked.Increment(ref _seekGeneration);
            AcceptSeekPreferences(authority, context, SeekPreferences.Read(settings, SeekIntervals));
        }
    }

    private void AcceptSeekPreferences(PlaybackAuthorityScope authority, ApiRequestContext context, SeekPreferences preferences)
    {
        _seekIntervals = preferences;
        _seekContext = context;
        _seekAuthority = authority;
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
            var key = SeekPreferences.Keys[(audiobook ? 2 : 0) + (backward ? 0 : 1)];
            await api.SetContractSettingValueAsync(context, key, "profile", seconds);
            if (ReferenceEquals(authority, _activeAuthority) && api.IsCurrentContext(context)) api.NotifyLocalizedSettingChanged(key);
        }
        catch { ShowNotice("Seek interval", "Could not save this profile's seek interval. Try again.", "error"); }
    }
    private void PublishSeekPreferences()
    {
        _mpv?.SendScriptMessage("osc-set-seek-intervals", SeekIntervals.VideoBack.ToString(), SeekIntervals.VideoForward.ToString());
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() => InvokeSubscribersSafely(SeekPreferencesChanged, nameof(SeekPreferencesChanged)));
    }
}
