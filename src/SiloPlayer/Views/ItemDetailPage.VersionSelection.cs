using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;
using Windows.Networking.Connectivity;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class ItemDetailPage
{
    private DetailVersionSelection _detailVersions = new();
    private ApiIdentityContext? _detailVersionOwner;
    private int _versionQualityRevision;
    private int? _trackSelectionFileId;
    private ApiIdentityContext? _trackSelectionOwner;
    private long _subtitleInventoryRevision;
    private SettingsApi? _versionSettings;
    private EventChannelClient? _versionEvents;
    private SiloApiClient? _versionClient;
    private Func<(string Key, bool Metered, string Transport)> _detailNetwork = PlaybackConnectionNetwork.CaptureForQuality;
    private bool? _detailQualityMetered;

    private void ObserveVersionSelection()
    {
        StopObservingVersionSelection();
        _versionClient = App.Services.GetRequiredService<SiloApiClient>();
        _versionClient.ContextInvalidated += VersionContextChanged;
        NetworkInformation.NetworkStatusChanged += VersionNetworkChanged;
        _versionSettings = App.Services.GetService<SettingsApi>();
        if (_versionSettings != null) _versionSettings.LocalProfileSettingChanged += VersionSettingChanged;
        _versionEvents = App.Services.GetService<EventChannelClient>();
        if (_versionEvents != null) _versionEvents.EventReceived += VersionSettingEvent;
        _ = RefreshVersionQualityAsync();
    }

    private void StopObservingVersionSelection()
    {
        ++_versionQualityRevision;
        NetworkInformation.NetworkStatusChanged -= VersionNetworkChanged;
        if (_versionClient != null) _versionClient.ContextInvalidated -= VersionContextChanged;
        if (_versionSettings != null) _versionSettings.LocalProfileSettingChanged -= VersionSettingChanged;
        if (_versionEvents != null) _versionEvents.EventReceived -= VersionSettingEvent;
        _versionClient = null; _versionSettings = null; _versionEvents = null;
    }

    private void VersionContextChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!IsLoaded) return;
        EnsureDetailVersionOwner();
        RepaintDetailVersions();
        _ = RefreshVersionQualityAsync();
    });

    private void VersionSettingChanged(string key)
    {
        if (key is not ("playback.preferred_quality" or "playback.max_bitrate_kbps" or
            "playback.cellular_preferred_quality" or "playback.cellular_max_bitrate_kbps")) return;
        DispatcherQueue.TryEnqueue(() => { if (IsLoaded) _ = RefreshVersionQualityAsync(); });
    }

    private void VersionNetworkChanged(object sender)
        => DispatcherQueue.TryEnqueue(() => { if (IsLoaded) _ = RefreshVersionQualityAsync(); });

    private void VersionSettingEvent(string channel, string name, JsonElement data)
    {
        if (channel != "user_settings" || name != "user_settings.changed" || data.ValueKind != JsonValueKind.Object) return;
        var owner = App.Services.GetRequiredService<SiloApiClient>().CaptureIdentityContext();
        if (data.TryGetProperty("profile_id", out var profile) && profile.ValueKind == JsonValueKind.String
            && profile.GetString() is { Length: > 0 } id && owner.ProfileId != id) return;
        if (data.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String)
            VersionSettingChanged(key.GetString() ?? "");
    }

    private void EnsureDetailVersionOwner()
    {
        var client = App.Services.GetRequiredService<SiloApiClient>();
        if (_detailVersionOwner is { } owner && client.IsCurrentIdentityContext(owner)) return;
        _detailVersionOwner = client.CaptureIdentityContext();
        _detailVersions = new();
        _detailQualityMetered = null;
        _selectedVersionWasExplicit = false;
        _selectedVersionOwner = null;
        _detailVersions.SetQuality(_detailNetwork().Metered ? "720p"
            : App.Services.GetRequiredService<AuthService>().SelectedProfile?.QualityPreference, settled: false);
    }

    private async Task RefreshVersionQualityAsync()
    {
        if (_navigationCts is not { IsCancellationRequested: false } navigation) return;
        var contentId = _currentContentId;
        var revision = ++_versionQualityRevision;
        var client = App.Services.GetRequiredService<SiloApiClient>();
        EnsureDetailVersionOwner();
        var selection = _detailVersions;
        var context = client.CaptureContext();
        var network = _detailNetwork();
        if (_detailQualityMetered != network.Metered)
        {
            _detailQualityMetered = network.Metered;
            selection.SetQuality(network.Metered ? "720p"
                : App.Services.GetRequiredService<AuthService>().SelectedProfile?.QualityPreference, settled: false);
            RepaintDetailVersions();
        }
        try
        {
            var quality = await PlaybackQualityPreferences.ReadAsync(client,
                App.Services.GetRequiredService<AuthService>().SelectedProfile?.QualityPreference,
                navigation.Token, expectedContext: context, metered: network.Metered);
            if (revision != _versionQualityRevision || navigation.IsCancellationRequested
                || _currentContentId != contentId || !client.IsCurrentContext(context) || !ReferenceEquals(selection, _detailVersions)
                || network != _detailNetwork()) return;
            selection.SetQuality(quality.Quality, settled: true);
            RepaintDetailVersions();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // A provisional pick must not lock an edition after a failed read.
            // A later settings event, reload or revisit can settle the choice.
        }
    }

    private FileVersion? ResolveDetailVersion(IReadOnlyList<FileVersion> versions,
        IReadOnlyList<PlaybackVariant>? variants, WatchUserData? history)
    {
        EnsureDetailVersionOwner();
        return _detailVersions.Resolve(versions, variants, history,
            _watchDetail?.EffectiveVersionEditionKey ?? ViewModel.Item?.EffectiveVersionEditionKey);
    }

    private void RepaintDetailVersions()
    {
        if (ViewModel.Item?.Type is not ("movie" or "episode")) return;
        if (_watchDetail != null) { UpdatePlayButton(); UpdateQualityBadges(); }
        else if (ViewModel.Item is { } item) { UpdatePlayButtonFromItemData(item); UpdateQualityBadgesFromItemData(item); }
    }

    private void ChooseDetailAuto(List<FileVersion> versions, List<PlaybackVariant>? variants, string? edition = null)
    {
        EnsureDetailVersionOwner();
        if (edition != null) _detailVersions.SelectEdition(edition); else _detailVersions.SelectAuto();
        _selectedVersionWasExplicit = false; _selectedVersionOwner = null;
        var selected = ResolveDetailVersion(versions, variants, _watchDetail?.UserData ?? ToWatchUserData(ViewModel.Item?.UserData));
        if (selected == null) return;
        _selectedVersion = selected;
        UpdateSelectedVersionUi(selected);
        ConfigureVersionSelectors(versions, variants, selected,
            (_watchDetail?.UserData?.PositionSeconds ?? ViewModel.Item?.UserData?.PositionSeconds ?? 0) > 0);
    }

    private string DetailVersionSummary(FileVersion version)
    {
        var summary = BuildQualitySummary(version);
        if (summary.Length == 0) summary = version.FileId.ToString(System.Globalization.CultureInfo.CurrentCulture);
        return IsSelectedVersionPinned ? summary : "Auto · " + summary;
    }

    private async Task RefreshWatchDetailPresentationAsync(WatchDetailResponse watchDetail)
    {
        _watchDetail = watchDetail;
        var inventoryBeforePaint = _subtitleInventoryRevision;
        UpdatePlayButton();
        UpdateQualityBadges();
        // File changes already start an inventory read. Same-file refreshes
        // (subtitle download or return from playback) must refresh it too.
        if (_subtitleInventoryRevision == inventoryBeforePaint && _selectedVersion != null && _playableContentId is { } contentId)
            await LoadDownloadedSubtitlesAsync(contentId, _selectedVersion.FileId);
    }

    private void RetirePrePlayTrackChoices()
    {
        _trackSelectionFileId = null;
        _trackSelectionOwner = null;
        _prePlayAudioSelection?.Clear();
        _selectedAudioTrackIndex = null;
        _selectedSubtitleIndex = null;
        _selectedSubtitleSignature = null;
        ++_prePlaySubtitleWriteRevision;
        ++_subtitleInventoryRevision;
        _downloadedSubtitles = [];
    }

    private bool PrepareVersionTracks(FileVersion? version)
    {
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var changed = _trackSelectionFileId != version?.FileId || _trackSelectionOwner is not { } owner || !client.IsCurrentIdentityContext(owner);
        if (!changed) return false;
        _trackSelectionFileId = version?.FileId;
        _trackSelectionOwner = client.CaptureIdentityContext();
        _prePlayAudioSelection?.Clear();
        _selectedAudioTrackIndex = null;
        _selectedSubtitleIndex = null; _selectedSubtitleSignature = null;
        ++_prePlaySubtitleWriteRevision;
        _downloadedSubtitles = [];
        if (version != null && _playableContentId is { } contentId)
            _ = LoadDownloadedSubtitlesAsync(contentId, version.FileId);
        return true;
    }
}
