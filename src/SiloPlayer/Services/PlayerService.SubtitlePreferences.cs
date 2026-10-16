using System.Globalization;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _subtitleDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private SubtitlePreferenceSnapshot? _subtitlePreferences;
    private PlaybackAuthorityScope? _subtitlePreferenceAuthority;
    private SettingsApi? _subtitlePreferenceEvents;
    private int _subtitleOffsetMs;
    private bool _subtitleOffsetSessionOverride;
    private int? _subtitleOffsetDraft;
    private long _subtitleOffsetEdit;
    private SubtitlePreferences.WriteIntent? _subtitleOffsetIntent;
    private CancellationTokenSource? _subtitleOffsetDelay;
    private Task _subtitleOffsetSaveTask = Task.CompletedTask;
    private readonly SemaphoreSlim _subtitleOffsetWrites = new(1, 1);
    public int SubtitleOffsetMs => _subtitleOffsetMs;
    public bool SubtitlePreferencesLoaded => _subtitlePreferences is { } state &&
        ReferenceEquals(_subtitlePreferenceAuthority, _activeAuthority) && _settingsApi.IsCurrentContext(state.Context);
    public string? SubtitlePreferenceError { get; private set; }
    public event Action? SubtitlePreferencesChanged;

    private void ObserveSubtitlePreferences()
    {
        if (ReferenceEquals(_subtitlePreferenceEvents, _settingsApi)) return;
        if (_subtitlePreferenceEvents != null) _subtitlePreferenceEvents.LocalProfileSettingChanged -= OnSubtitlePreferenceChanged;
        _subtitlePreferenceEvents = _settingsApi;
        _subtitlePreferenceEvents.LocalProfileSettingChanged += OnSubtitlePreferenceChanged;
    }

    private void OnSubtitlePreferenceChanged(string key)
    {
        if (_closing || State == PlayerState.Idle) return;
        if (key is not (Core.Services.SubtitlePreferences.AppearanceKey or Core.Services.SubtitlePreferences.OffsetKey)) return;
        var authority = _activeAuthority;
        var context = authority.ApiClient.CaptureContext();
        var generation = Volatile.Read(ref _playRequestGeneration);
        _subtitleDispatcher?.TryEnqueue(async () =>
        {
            if (_closing || State == PlayerState.Idle || !ReferenceEquals(authority, _activeAuthority) || !authority.ApiClient.IsCurrentContext(context)
                || generation != Volatile.Read(ref _playRequestGeneration)) return;
            try
            {
                await EnsureSubtitleAppearanceLoadedAsync(_authorityLifetimeCts.Token);
                if (generation == Volatile.Read(ref _playRequestGeneration)) PushResolvedSubtitlePreferences();
            }
            catch (OperationCanceledException) { }
        });
    }

    private void ResetSubtitlePreferenceSession()
    {
        // A same-owner next item carries the last explicit draft while its
        // durable save settles. New playback must not cancel an accepted PUT.
        var carry = !_closing && _subtitleOffsetDraft.HasValue && _subtitlePreferences is { } previous
            && ReferenceEquals(_subtitlePreferenceAuthority, _activeAuthority) && _settingsApi.IsCurrentContext(previous.Context);
        var draft = carry ? _subtitleOffsetDraft : null;
        if (!carry) Interlocked.Increment(ref _subtitleOffsetEdit);
        _subtitleOffsetDelay?.Cancel();
        _subtitleOffsetDraft = draft;
        _subtitlePreferences = null;
        _subtitleOffsetSessionOverride = false;
        _subtitleOffsetMs = draft ?? 0;
        _subtitleAppearance = null;
        if (_mpv != null)
        {
            PushSubtitleAppearanceToMpv(new());
            _mpv.SetProperty("sub-delay", (_subtitleOffsetMs / 1000d).ToString(CultureInfo.InvariantCulture));
        }
        SubtitlePreferenceError = null;
        PublishSubtitlePreferenceStatus();
    }

    private void PublishSubtitlePreferenceStatus()
    {
        _mpv?.SendScriptMessage("osc-subtitle-preferences", SubtitlePreferencesLoaded ? "true" : "false", SubtitlePreferenceError ?? "",
            _subtitleOffsetDraft.HasValue && SubtitlePreferenceError == null ? "true" : "false");
        SubtitlePreferencesChanged?.Invoke();
    }

    private void PushResolvedSubtitlePreferences()
    {
        if (!SubtitlePreferencesLoaded) return;
        if (_subtitleAppearance != null) PushSubtitleAppearanceToMpv(_subtitleAppearance);
        if (_subtitleOffsetDraft == null && !_subtitleOffsetSessionOverride) _subtitleOffsetMs = _subtitlePreferences!.OffsetMs;
        _mpv?.SetProperty("sub-delay", (_subtitleOffsetMs / 1000d).ToString(CultureInfo.InvariantCulture));
        PublishSubtitlePreferenceStatus();
    }

    /// <summary>Only explicit user nudges persist; remote session commands never call this path.</summary>
    public Task SetSubtitleOffsetAsync(int milliseconds, bool immediate = false)
    {
        if (!SubtitlePreferencesLoaded) return Task.CompletedTask;
        var state = _subtitlePreferences!;
        var authority = _activeAuthority;
        var generation = Volatile.Read(ref _playRequestGeneration);
        var edit = Interlocked.Increment(ref _subtitleOffsetEdit);
        var next = Core.Services.SubtitlePreferences.ClampOffset(milliseconds);
        var intent = authority.SettingsApi.Subtitles.ReserveOffsetIntent(state);
        _subtitleOffsetIntent = intent;
        _subtitleOffsetMs = next;
        _subtitleOffsetDraft = next;
        _subtitleOffsetSessionOverride = false;
        SubtitlePreferenceError = null;
        _mpv?.SetProperty("sub-delay", (next / 1000d).ToString(CultureInfo.InvariantCulture));
        PublishSubtitlePreferenceStatus();
        // This token belongs only to the debounce. Dispatched writes stay in
        // the queue until the server settles, including profile/device cleanup.
        _subtitleOffsetDelay?.Cancel();
        var delay = new CancellationTokenSource();
        _subtitleOffsetDelay = delay;
        return _subtitleOffsetSaveTask = SaveSubtitleOffsetDraftAsync(state, authority, next, generation, edit, intent, delay, immediate);
    }

    private Task FlushSubtitleOffsetAsync()
    {
        if (_subtitleOffsetDraft is not { } draft) return Task.CompletedTask;
        if (_subtitleOffsetDelay == null || _subtitlePreferences is not { } state || _subtitleOffsetIntent is not { } intent)
            return _subtitleOffsetSaveTask;
        _subtitleOffsetDelay.Cancel();
        var delay = new CancellationTokenSource(); _subtitleOffsetDelay = delay;
        return _subtitleOffsetSaveTask = SaveSubtitleOffsetDraftAsync(state, _activeAuthority, draft,
            Volatile.Read(ref _playRequestGeneration), _subtitleOffsetEdit, intent, delay, immediate: true);
    }

    private async Task SaveSubtitleOffsetDraftAsync(SubtitlePreferenceSnapshot state, PlaybackAuthorityScope authority,
        int milliseconds, long generation, long edit, SubtitlePreferences.WriteIntent intent, CancellationTokenSource delay, bool immediate)
    {
        try
        {
            if (!immediate) await Task.Delay(600, delay.Token);
            if (ReferenceEquals(_subtitleOffsetDelay, delay)) _subtitleOffsetDelay = null;
            await _subtitleOffsetWrites.WaitAsync();
            try
            {
                if (!ReferenceEquals(authority, _activeAuthority) || !authority.ApiClient.IsCurrentContext(state.Context)) return;
                await authority.SettingsApi.Subtitles.SaveOffsetAsync(state, milliseconds, intent: intent);
                if (edit != _subtitleOffsetEdit || !ReferenceEquals(authority, _activeAuthority)
                    || !authority.ApiClient.IsCurrentContext(state.Context)) return;
                if (!authority.SettingsApi.Subtitles.IsLatestIntent(intent))
                {
                    await RefreshSupersededSubtitleOffsetAsync(state, authority, edit, intent);
                    return;
                }
                if (_subtitlePreferences is { } current && current.Context == state.Context)
                    _subtitlePreferences = current with { OffsetMs = milliseconds,
                        OffsetSource = state.SupportsProfileTiming ? "profile" : "profile_device" };
                _subtitleOffsetDraft = null;
            }
            finally { _subtitleOffsetWrites.Release(); }
        }
        catch (OperationCanceledException) when (delay.IsCancellationRequested || !ReferenceEquals(authority, _activeAuthority)
            || !authority.ApiClient.IsCurrentContext(state.Context)) { }
        catch (Exception)
        {
            if (edit == _subtitleOffsetEdit && ReferenceEquals(authority, _activeAuthority)
                && authority.ApiClient.IsCurrentContext(state.Context))
            {
                if (!authority.SettingsApi.Subtitles.IsLatestIntent(intent))
                    await RefreshSupersededSubtitleOffsetAsync(state, authority, edit, intent);
                else SubtitlePreferenceError = "Subtitle offset could not save. Adjust again to retry.";
            }
        }
        finally
        {
            if (ReferenceEquals(_subtitleOffsetDelay, delay)) _subtitleOffsetDelay = null;
            delay.Dispose();
            // A carried current draft still owns persistence feedback in the
            // next item. This publishes status only, never old mpv values.
            if ((generation == Volatile.Read(ref _playRequestGeneration) || edit == _subtitleOffsetEdit) && ReferenceEquals(authority, _activeAuthority)
                && authority.ApiClient.IsCurrentContext(state.Context)) PublishSubtitlePreferenceStatus();
        }
    }

    private async Task RefreshSupersededSubtitleOffsetAsync(SubtitlePreferenceSnapshot state, PlaybackAuthorityScope authority,
        long edit, SubtitlePreferences.WriteIntent intent)
    {
        // The newer Settings write may still be queued behind the accepted old
        // PUT. Its result owns this draft even if the old request timed out.
        _subtitleOffsetDraft = null;
        SubtitlePreferenceError = null;
        try
        {
            await authority.SettingsApi.Subtitles.WaitForOffsetWritesAsync(intent);
            if (edit != _subtitleOffsetEdit || !ReferenceEquals(authority, _activeAuthority)
                || !authority.ApiClient.IsCurrentContext(state.Context)) return;
            var generation = Volatile.Read(ref _playRequestGeneration);
            await EnsureSubtitleAppearanceLoadedAsync(_authorityLifetimeCts.Token);
            if (edit == _subtitleOffsetEdit && generation == Volatile.Read(ref _playRequestGeneration)
                && ReferenceEquals(authority, _activeAuthority) && authority.ApiClient.IsCurrentContext(state.Context)) PushResolvedSubtitlePreferences();
        }
        catch (OperationCanceledException) when (!ReferenceEquals(authority, _activeAuthority)
            || !authority.ApiClient.IsCurrentContext(state.Context)) { }
    }
}
