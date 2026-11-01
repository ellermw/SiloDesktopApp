using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty] private bool _markerDetailsSettingsAvailable;
    [ObservableProperty] private bool _showMarkerConfidence;
    [ObservableProperty] private bool _showMarkerSource;
    private ApiRequestContext? _markerDetailsSettingsContext;
    private readonly SemaphoreSlim _markerDetailsWrites = new(1, 1);
    private long _markerDetailsEdit;
    private bool _suppressMarkerDetailsSave;
    private long _markerDetailsVisit;
    private sealed class MarkerSettingEdit(bool saved)
    {
        internal bool Saved = saved;
        internal long Revision;
    }
    private readonly Dictionary<string, MarkerSettingEdit> _markerSettingEdits = new(StringComparer.Ordinal);

    public void CloseMarkerDetailsSettings()
    {
        ++_markerDetailsVisit;
        _markerDetailsSettingsContext = null;
        _markerSettingEdits.Clear();
        MarkerDetailsSettingsAvailable = false;
    }

    public async Task OpenMarkerDetailsSettingsAsync()
    {
        CloseMarkerDetailsSettings();
        var visit = _markerDetailsVisit;
        var context = _settingsApi.CaptureContext();
        _markerDetailsSettingsContext = context;
        _suppressMarkerDetailsSave = true;
        try { ShowMarkerConfidence = ShowMarkerSource = false; }
        finally { _suppressMarkerDetailsSave = false; }
        try
        {
            var preferences = await _settingsApi.GetMarkerDetailsPreferencesAsync(context);
            if (visit != _markerDetailsVisit || !_settingsApi.IsCurrentContext(context)) return;
            _suppressMarkerDetailsSave = true;
            try { ShowMarkerConfidence = preferences.ShowConfidence; ShowMarkerSource = preferences.ShowSource; }
            finally { _suppressMarkerDetailsSave = false; }
            MarkerDetailsSettingsAvailable = preferences.Supported;
        }
        catch { /* Optional/old-server settings stay hidden until a new visit. */ }
    }

    partial void OnShowMarkerConfidenceChanged(bool oldValue, bool newValue) =>
        QueueMarkerDetailsSetting(MarkerDetailsPreferences.ConfidenceKey, oldValue, newValue);
    partial void OnShowMarkerSourceChanged(bool oldValue, bool newValue) =>
        QueueMarkerDetailsSetting(MarkerDetailsPreferences.SourceKey, oldValue, newValue);

    private void QueueMarkerDetailsSetting(string key, bool oldValue, bool value)
    {
        if (_suppressMarkerDetailsSave || !MarkerDetailsSettingsAvailable ||
            _markerDetailsSettingsContext is not { } context || !_settingsApi.IsCurrentContext(context)) return;
        if (!_markerSettingEdits.TryGetValue(key, out var state))
            _markerSettingEdits[key] = state = new(oldValue);
        _ = SaveMarkerDetailsSettingAsync(context, key, value, state, ++state.Revision, ++_markerDetailsEdit);
    }

    private async Task SaveMarkerDetailsSettingAsync(ApiRequestContext context, string key, bool value,
        MarkerSettingEdit state, long revision, long edit)
    {
        await _markerDetailsWrites.WaitAsync();
        var committed = false;
        bool Current() => _settingsApi.IsCurrentContext(context) &&
            _markerDetailsSettingsContext == context &&
            _markerSettingEdits.TryGetValue(key, out var current) && ReferenceEquals(state, current);
        try
        {
            if (!Current()) return;
            await _settingsApi.SetContractSettingValueAsync(context, key, "profile", value);
            committed = true;
            if (!_settingsApi.IsCurrentContext(context)) return;
            if (Current()) state.Saved = value;
            try { await _settingsApi.DeleteContractSettingValueAsync(context, key, "profile_device"); }
            catch (ApiException ex) when (ex.StatusCode == 404) { }
            if (!_settingsApi.IsCurrentContext(context)) return;
            _settingsApi.NotifyLocalizedSettingChanged(key);
            if (!Current()) return;
            if (edit == _markerDetailsEdit) ShowStatus("Setting saved");
        }
        catch (Exception ex)
        {
            if (committed && _settingsApi.IsCurrentContext(context)) _settingsApi.NotifyLocalizedSettingChanged(key);
            if (!Current() || revision != state.Revision) return;
            if (!committed)
            {
                _suppressMarkerDetailsSave = true;
                try { if (key == MarkerDetailsPreferences.ConfidenceKey) ShowMarkerConfidence = state.Saved; else ShowMarkerSource = state.Saved; }
                finally { _suppressMarkerDetailsSave = false; }
            }
            if (!committed) _settingsApi.NotifyLocalizedSettingChanged(key);
            ShowError(committed ? "Profile setting saved, but this device's override could not be cleared. Reload settings to retry."
                : $"Failed to save setting: {ex.Message}");
        }
        finally { _markerDetailsWrites.Release(); }
    }
}
