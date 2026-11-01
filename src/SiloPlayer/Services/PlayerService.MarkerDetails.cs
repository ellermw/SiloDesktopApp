using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private MarkerDetailsPreferences _markerDetails = new();
    private PlaybackAuthorityScope? _markerDetailsAuthority;
    private ApiRequestContext? _markerDetailsContext;
    private SettingsApi? _markerDetailsEvents;
    private long _markerDetailsGeneration;
    public MarkerDetailsPreferences MarkerDetails => ReferenceEquals(_markerDetailsAuthority, _activeAuthority) &&
        _markerDetailsContext is { } context && _activeAuthority.ApiClient.IsCurrentContext(context) ? _markerDetails : new();
    public event Action? MarkerDetailsChanged;

    private void ObserveMarkerDetailsPreferences()
    {
        if (ReferenceEquals(_markerDetailsEvents, _settingsApi)) return;
        if (_markerDetailsEvents is not null) _markerDetailsEvents.LocalProfileSettingChanged -= OnMarkerDetailsSettingChanged;
        _markerDetailsEvents = _settingsApi;
        _markerDetailsEvents.LocalProfileSettingChanged += OnMarkerDetailsSettingChanged;
    }

    private void OnMarkerDetailsSettingChanged(string key)
    {
        if (_closing || State == PlayerState.Idle || !MarkerDetailsPreferences.Keys.Contains(key)) return;
        _ = RefreshMarkerDetailsPreferencesAsync(_authorityLifetimeCts.Token);
    }

    public async Task RefreshMarkerDetailsPreferencesAsync(CancellationToken ct = default)
    {
        var authority = _activeAuthority;
        var context = authority.ApiClient.CaptureContext();
        var playback = Volatile.Read(ref _playRequestGeneration);
        var generation = Interlocked.Increment(ref _markerDetailsGeneration);
        bool Current() => generation == Volatile.Read(ref _markerDetailsGeneration) &&
            playback == Volatile.Read(ref _playRequestGeneration) && ReferenceEquals(authority, _activeAuthority) &&
            authority.ApiClient.IsCurrentContext(context);
        var preferences = new MarkerDetailsPreferences();
        try { preferences = await authority.SettingsApi.GetMarkerDetailsPreferencesAsync(context, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch { } // Older/unreachable settings keep these optional details off.
        if (ct.IsCancellationRequested || !Current()) return;
        _markerDetails = preferences; _markerDetailsAuthority = authority; _markerDetailsContext = context;
        SendMarkerDetailsToOsc();
        _subtitleDispatcher?.TryEnqueue(() => { if (Current()) InvokeSubscribersSafely(MarkerDetailsChanged, nameof(MarkerDetailsChanged)); });
    }

    private void ResetMarkerDetails()
    {
        Interlocked.Increment(ref _markerDetailsGeneration);
        _markerDetails = new(); _markerDetailsAuthority = null; _markerDetailsContext = null;
        SendMarkerDetailsToOsc();
    }

    private TimeRange? MarkerProvenanceFor(string kind)
    {
        // A version with no provenance must not inherit another version's
        // primary-file metadata from the top-level compatibility projection.
        var version = ActiveVersion;
        return kind switch
        {
            "intro" => version is not null ? version.Intro : WatchDetail?.Intro,
            "recap" => version is not null ? version.Recap : WatchDetail?.Recap,
            "credits" => version is not null ? version.Credits : WatchDetail?.Credits,
            "preview" => version is not null ? version.Preview : WatchDetail?.Preview,
            _ => null,
        };
    }

    private void SendMarkerDetailsToOsc()
    {
        var labels = new Dictionary<string, string>();
        foreach (var kind in new[] { "intro", "recap", "credits", "preview" })
            labels[kind] = GetMarkerDetailsLabel(kind) ?? "";
        // Kept separate from osc-set-markers: cosmetic changes must not retire
        // a pending intro seek/countdown or a live Watch Intro undo action.
        _mpv?.SendScriptMessage("osc-set-marker-details", JsonSerializer.Serialize(labels));
    }

    public string? GetMarkerDetailsLabel(string kind)
    {
        var preferences = MarkerDetails;
        return MarkerProvenance.Label(MarkerProvenanceFor(kind), preferences.ShowConfidence, preferences.ShowSource);
    }
}
