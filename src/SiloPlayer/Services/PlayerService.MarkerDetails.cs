using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private readonly object _markerDetailsGate = new();
    private MarkerDetailsPreferences _markerDetails = new();
    private PlaybackAuthorityScope? _markerDetailsAuthority;
    private ApiRequestContext? _markerDetailsContext;
    private SettingsApi? _markerDetailsEvents;
    private long _markerDetailsGeneration;
    private long _markerDetailsFileGeneration;
    public MarkerDetailsPreferences MarkerDetails
    {
        get
        {
            lock (_markerDetailsGate)
                return ReferenceEquals(_markerDetailsAuthority, _activeAuthority) &&
                    _markerDetailsFileGeneration == Volatile.Read(ref _fileLoadGeneration) &&
                    _markerDetailsContext is { } context && _activeAuthority.ApiClient.IsCurrentContext(context) ? _markerDetails : new();
        }
    }
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
        var fileGeneration = Volatile.Read(ref _fileLoadGeneration);
        long generation;
        lock (_markerDetailsGate) generation = ++_markerDetailsGeneration;
        bool Current() => generation == Volatile.Read(ref _markerDetailsGeneration) &&
            fileGeneration == Volatile.Read(ref _fileLoadGeneration) &&
            playback == Volatile.Read(ref _playRequestGeneration) && ReferenceEquals(authority, _activeAuthority) &&
            authority.ApiClient.IsCurrentContext(context);
        MarkerDetailsPreferences preferences;
        try { preferences = await authority.SettingsApi.GetMarkerDetailsPreferencesAsync(context, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        catch { return; } // A failed optional read must not replace accepted preferences.
        lock (_markerDetailsGate)
        {
            if (ct.IsCancellationRequested || !Current()) return;
            _markerDetails = preferences; _markerDetailsAuthority = authority; _markerDetailsContext = context;
            _markerDetailsFileGeneration = fileGeneration;
            SendMarkerDetailsToOsc();
        }
        _subtitleDispatcher?.TryEnqueue(() =>
        {
            lock (_markerDetailsGate)
                if (Current()) InvokeSubscribersSafely(MarkerDetailsChanged, nameof(MarkerDetailsChanged));
        });
    }

    private void ResetMarkerDetails()
    {
        lock (_markerDetailsGate)
        {
            ++_markerDetailsGeneration;
            _markerDetails = new(); _markerDetailsAuthority = null; _markerDetailsContext = null;
            SendMarkerDetailsToOsc();
        }
    }

    private IEnumerable<FileVersion> MarkerVersions() => Versions
        .Concat(WatchDetail?.Versions ?? [])
        .Concat((WatchDetail?.PlaybackVariants ?? []).SelectMany(variant => variant.Parts ?? [])
            .SelectMany(part => part.Versions ?? [])).Distinct();

    private FileVersion? CurrentMarkerVersion => MarkerVersions().FirstOrDefault(version =>
        version.FileId == (_playbackManager?.CurrentSession?.MediaFileId ?? 0));

    // Playback retains the legacy top-level timing fallback. Provenance below
    // is deliberately stricter so another file's details never label that range.
    private TimeRange? MarkerTimingFor(string kind)
    {
        var version = CurrentMarkerVersion;
        return kind switch
        {
            "intro" => version?.Intro ?? WatchDetail?.Intro,
            "recap" => version?.Recap ?? WatchDetail?.Recap,
            "credits" => version?.Credits ?? WatchDetail?.Credits,
            "preview" => version?.Preview ?? WatchDetail?.Preview,
            _ => null,
        };
    }

    private TimeRange? MarkerProvenanceFor(string kind)
    {
        // A version with no provenance must not inherit another version's
        // primary-file metadata from the top-level compatibility projection.
        var version = CurrentMarkerVersion;
        if (version is null && _playbackManager?.CurrentSession is not null) return null;
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
        lock (_markerDetailsGate)
        {
            var player = _mpv;
            if (player is null) return;
            var authority = _activeAuthority;
            var context = authority.ApiClient.CaptureContext();
            var content = ContentId ?? WatchDetail?.ContentId ?? "";
            var file = _playbackManager?.CurrentSession?.MediaFileId ?? 0;
            var load = Volatile.Read(ref _fileLoadGeneration);
            var labels = new Dictionary<string, object?>
            {
                ["content_id"] = content, ["file_id"] = file, ["load_generation"] = load,
                ["preference_generation"] = _markerDetailsGeneration,
            };
            foreach (var kind in new[] { "intro", "recap", "credits", "preview" }) labels[kind] = GetMarkerDetailsLabel(kind) ?? "";
            if (!ReferenceEquals(player, _mpv) || !ReferenceEquals(authority, _activeAuthority) || !authority.ApiClient.IsCurrentContext(context)
                || load != Volatile.Read(ref _fileLoadGeneration) || file != (_playbackManager?.CurrentSession?.MediaFileId ?? 0)
                || content != (ContentId ?? WatchDetail?.ContentId ?? "")) return;
            // The receiver validates this owner too: a delayed send may arrive
            // after the next load's reset even when composition was current.
            player.SendScriptMessage("osc-set-marker-details", JsonSerializer.Serialize(labels));
        }
    }

    public string? GetMarkerDetailsLabel(string kind)
    {
        lock (_markerDetailsGate)
        {
            var preferences = MarkerDetails;
            return MarkerProvenance.Label(MarkerProvenanceFor(kind), preferences.ShowConfidence, preferences.ShowSource);
        }
    }
}
