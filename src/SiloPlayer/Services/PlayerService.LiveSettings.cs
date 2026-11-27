using System.Text.Json;
using System.Globalization;
using Microsoft.UI.Dispatching;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Player;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private const string IntroSkipModeKey = "playback.intro_skip_mode";
    private const string BufferingQualityModeKey = "playback.buffering_quality_mode";
    private static readonly string[] BaseLiveSettingKeys =
    [
        AutoSkipIntroSettingKey, AutoSkipRecapSettingKey, AutoSkipCreditsSettingKey,
        AutoPlayNextSettingKey, NextUpPromptTiming.SettingKey,
        SubtitlePreferences.AppearanceKey, SubtitlePreferences.OffsetKey, VideoPlaybackRatePreferences.Key
    ];
    private readonly PlaybackSettingsRefresh _livePlaybackSettings = new();
    private sealed record LiveSettingsContract(SettingsIdentityContext Owner, SettingsContractCapabilities Capabilities);
    private LiveSettingsContract? _liveSettingsContract;
    private readonly object _autoSkipSettingsGate = new();
    private long _autoSkipSettingsRevision;
    private sealed record AutoSkipSettings(bool Intro = false, bool Recap = false, bool Credits = false, string? IntroMode = null);
    private AutoSkipSettings _autoSkipSettings = new();

    private void ResetAutoSkipSettings()
    {
        lock (_autoSkipSettingsGate)
        {
            ++_autoSkipSettingsRevision;
            _autoSkipSettings = new();
            _mpv?.SendScriptMessage("osc-set-intro-mode", "never");
            _mpv?.SendScriptMessage("osc-set-auto-skip", "false", "false", "false");
        }
    }

    private static AutoSkipSettings ReadAutoSkipSettings(ContractEffectiveSettingsResponse response, AutoSkipSettings fallback)
    {
        JsonElement Value(string key) => response.Settings.FirstOrDefault(entry => entry.Key == key)?.Value ?? default;
        bool ReadBool(string key, bool previous) => Value(key) is { ValueKind: JsonValueKind.True or JsonValueKind.False } value
            ? value.GetBoolean() : previous;
        var intro = ReadBool(AutoSkipIntroSettingKey, fallback.Intro);
        var enumValue = Value(IntroSkipModeKey);
        var mode = enumValue.ValueKind == JsonValueKind.String ? enumValue.GetString() : null;
        return new(intro, ReadBool(AutoSkipRecapSettingKey, fallback.Recap),
            ReadBool(AutoSkipCreditsSettingKey, fallback.Credits), mode is "never" or "ask" or "always" ? mode : fallback.IntroMode);
    }

    private void ApplyLiveAutoSkipSettings(MpvPlayer mpv, ContractEffectiveSettingsResponse settings, long play, long file)
    {
        if (!settings.Settings.Any(entry => entry.Key is AutoSkipIntroSettingKey or AutoSkipCreditsSettingKey
            or AutoSkipRecapSettingKey or IntroSkipModeKey)) return;
        lock (_autoSkipSettingsGate)
        {
            ++_autoSkipSettingsRevision;
            PublishAutoSkipSettings(mpv, ReadAutoSkipSettings(settings, _autoSkipSettings), play, file);
        }
    }

    private void PublishAutoSkipSettings(MpvPlayer mpv, AutoSkipSettings settings, long play, long file)
    {
        _autoSkipSettings = settings;
        var playback = play.ToString(CultureInfo.InvariantCulture);
        var load = file.ToString(CultureInfo.InvariantCulture);
        mpv.SendScriptMessage("osc-set-intro-mode", settings.IntroMode ?? (settings.Intro ? "always" : "ask"), playback, load);
        mpv.SendScriptMessage("osc-set-auto-skip", settings.Intro ? "true" : "false",
            settings.Recap ? "true" : "false", settings.Credits ? "true" : "false", playback, load);
    }

    internal void ReceivePlaybackSettingChange(string channel, string eventName, JsonElement data)
    {
        if (channel != "user_settings" || eventName != "user_settings.changed" ||
            data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("key", out var key) ||
            key.ValueKind != JsonValueKind.String) return;
        string? profile = null;
        if (data.TryGetProperty("profile_id", out var changedProfile) && changedProfile.ValueKind != JsonValueKind.Null)
        {
            if (changedProfile.ValueKind != JsonValueKind.String) return;
            profile = changedProfile.GetString();
        }
        OnPlaybackSettingChanged(key.GetString()!, profile);
    }

    private void OnLocalPlaybackSettingChanged(string key) => OnPlaybackSettingChanged(key, null);

    private void CancelRetiredPlaybackSettings()
    {
        if (_playbackProgressOwner is { } owner &&
            !_localAuthority.SettingsApi.IsCurrentSettingsIdentityContext(owner.SettingsIdentity))
            _livePlaybackSettings.Cancel();
    }

    private void OnPlaybackSettingChanged(string key, string? changedProfile)
    {
        if (HasRemoteAuthority) return;
        var authority = _localAuthority;
        var api = authority.SettingsApi;
        var identity = api.CaptureSettingsIdentityContext();
        if (changedProfile is not null && identity.Identity.ProfileId != changedProfile) return;

        if (key is "playback.audio_language" or "playback.subtitle_language" or "playback.subtitle_mode" or
            "playback.show_forced_subtitles")
        {
            // These defaults are inputs to the next start. Retire held /watch
            // responses as well as hover reads, without re-reading while idle.
            _userStateRevision.Advance();
            _watchDetailPrefetches.Clear();
            return;
        }
        if (!BaseLiveSettingKeys.Contains(key) && key is not (IntroSkipModeKey or BufferingQualityModeKey) &&
            !SeekPreferences.Keys.Contains(key) && !MarkerDetailsPreferences.Keys.Contains(key)) return;

        var manager = _playbackManager;
        var session = manager?.CurrentSession;
        var owner = _playbackProgressOwner;
        var mpv = _mpv;
        var play = Volatile.Read(ref _playRequestGeneration);
        var file = Volatile.Read(ref _fileLoadGeneration);
        var speed = _videoPlaybackRate;
        var speedIntent = speed?.CaptureRefreshIntent();
        if (_closing || State == PlayerState.Idle || manager == null || session == null || mpv == null ||
            owner == null || !ReferenceEquals(owner.Manager, manager) || owner.SettingsIdentity != identity ||
            string.IsNullOrWhiteSpace(identity.Identity.ProfileId)) return;
        bool Current() => !_closing && State != PlayerState.Idle && ReferenceEquals(authority, _activeAuthority) &&
            ReferenceEquals(owner, _playbackProgressOwner) && ReferenceEquals(manager, _playbackManager) &&
            ReferenceEquals(session, manager.CurrentSession) &&
            ReferenceEquals(mpv, _mpv) && api.IsCurrentSettingsIdentityContext(identity) &&
            play == Volatile.Read(ref _playRequestGeneration) && file == Volatile.Read(ref _fileLoadGeneration);
        // File and play changes also cancel this lane directly. The guards
        // cover late responses from an already retired HTTP request.
        _ = _livePlaybackSettings.RequestAsync(changedProfile, identity, _authorityLifetimeCts.Token, Current,
            async ct =>
            {
                var contract = _liveSettingsContract;
                if (contract?.Owner != identity)
                {
                    SettingsContractCapabilities capabilities;
                    try { capabilities = await api.GetContractCapabilitiesAsync(authority.ApiClient.CaptureContext(), ct).ConfigureAwait(false); }
                    catch (ApiException ex) when (ex.StatusCode is 404 or 405 or 501)
                    {
                        capabilities = new();
                    }
                    ct.ThrowIfCancellationRequested();
                    if (!Current()) throw new OperationCanceledException(ct);
                    contract = new(identity, capabilities);
                    _liveSettingsContract = contract;
                }
                var capability = contract.Capabilities;
                var keys = new List<string>(BaseLiveSettingKeys);
                if (capability.Revision >= 7) keys.Add(IntroSkipModeKey);
                if (capability.Revision >= 9) keys.AddRange(SeekPreferences.Keys);
                if (capability.Revision >= 23) keys.Add(BufferingQualityModeKey);
                if (MarkerDetailsPreferences.Supports(capability)) keys.AddRange(MarkerDetailsPreferences.Keys);
                return await api.GetContractEffectiveSettingsAsync(identity, keys, ct).ConfigureAwait(false);
            },
            DispatchPlaybackSettingsAsync,
            settings =>
            {
                var context = authority.ApiClient.CaptureContext();
                JsonElement Value(string name) => settings.Settings.FirstOrDefault(entry => entry.Key == name)?.Value ?? default;
                ApplyLiveAutoSkipSettings(mpv, settings, play, file);
                var autoplay = Value(AutoPlayNextSettingKey);
                if (autoplay.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    ApplyLiveCreditsAutoPlay(autoplay.GetBoolean());
                if (Value(NextUpPromptTiming.SettingKey) is { ValueKind: JsonValueKind.Number } prompt)
                {
                    lock (_nextUpPromptGate)
                    {
                        ++_nextUpPromptRevision;
                        _nextUpPromptRead?.Cancel();
                        Volatile.Write(ref _nextUpPromptSeconds, NextUpPromptTiming.Parse(prompt));
                    }
                }
                if (_liveSettingsContract is { } contract && contract.Owner == identity &&
                    MarkerDetailsPreferences.Read(contract.Capabilities, settings) is { Supported: true } details)
                    ApplyLiveMarkerDetails(authority, context, details);
                if (SeekPreferences.Keys.Any(name => Value(name).ValueKind == JsonValueKind.Number))
                    ApplyLiveSeekPreferences(authority, context, settings);
                if (!IsAudiobook)
                {
                    ApplyLiveSubtitlePreferences(authority, context, settings, _liveSettingsContract is { Capabilities.Revision: >= 19 });
                    if (Value(VideoPlaybackRatePreferences.Key) is { ValueKind: JsonValueKind.Number } rate &&
                        rate.TryGetDouble(out var multiplier) && speedIntent is long intent)
                        speed?.ApplyRefresh(intent, multiplier);
                    if (Value(BufferingQualityModeKey) is { ValueKind: JsonValueKind.String } mode &&
                        mode.GetString() is "never" or "ask" or "always")
                        ApplyLiveBufferingQuality(manager, mode.GetString()!);
                }
            });
    }

    private async Task DispatchPlaybackSettingsAsync(Action publish, CancellationToken ct)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_subtitleDispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            try { publish(); completed.TrySetResult(); }
            catch (Exception ex) { completed.TrySetException(ex); }
        }) == true)
            await completed.Task.WaitAsync(ct).ConfigureAwait(false);
    }
}
