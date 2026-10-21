using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;

namespace SiloPlayer.ViewModels;

public partial class SettingsViewModel
{
    private long _nextUpPreferenceRevision;
    private string? _nextUpPreferenceSource;
    private ApiRequestContext? _nextUpPreferenceContext;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeNextUpMode))]
    [NotifyPropertyChangedFor(nameof(NextUpHasOwnChoice))]
    [NotifyPropertyChangedFor(nameof(NextUpInheritanceExplanation))]
    private bool _nextUpPreferenceLoaded;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeNextUpMode))]
    [NotifyPropertyChangedFor(nameof(NextUpHasOwnChoice))]
    [NotifyPropertyChangedFor(nameof(NextUpInheritanceExplanation))]
    private bool _isNextUpPreferencePending;
    [ObservableProperty] private string? _nextUpPreferenceError;
    public bool CanChangeNextUpMode => NextUpPreferenceLoaded && !IsNextUpPreferencePending
        && _nextUpPreferenceContext is { } context && _settingsApi.IsCurrentContext(context);
    public bool NextUpHasOwnChoice => CanChangeNextUpMode && _nextUpPreferenceSource is "profile" or "profile_device";
    public string NextUpInheritanceExplanation => CanChangeNextUpMode && _nextUpPreferenceSource == "default"
        ? $"Using the server default ({(NextUpMode == "separate" ? "Separate row" : "With Continue Watching")})." : "";
    partial void OnNextUpModeChanged(string value) => OnPropertyChanged(nameof(NextUpInheritanceExplanation));

    public void DeactivateInheritedPreferences()
    { ++_nextUpPreferenceRevision; _nextUpPreferenceContext = null; NextUpPreferenceLoaded = false; IsNextUpPreferencePending = false; }

    private void ApplyResolvedNextUpPreference(ContractEffectiveSettingEntry? entry, ApiRequestContext context)
    {
        if (entry == null || entry.Value.ValueKind != System.Text.Json.JsonValueKind.String
            || entry.Value.GetString() is not ("combined" or "separate"))
        { NextUpPreferenceLoaded = false; NextUpPreferenceError = "Next up preferences didn't load. Try again."; return; }
        _nextUpPreferenceSource = entry.Source;
        _nextUpPreferenceContext = context;
        _nextUpDeviceOverride = entry.Source == "profile_device";
        var suppress = _suppressSave; _suppressSave = true;
        try { NextUpMode = entry.Value.GetString()!; }
        finally { _suppressSave = suppress; }
        NextUpPreferenceLoaded = true;
        OnPropertyChanged(nameof(NextUpHasOwnChoice));
        OnPropertyChanged(nameof(NextUpInheritanceExplanation));
    }

    public async Task LoadNextUpPreferenceAsync()
    {
        var revision = ++_nextUpPreferenceRevision;
        var context = _settingsApi.CaptureContext();
        IsNextUpPreferencePending = true; NextUpPreferenceLoaded = false;
        NextUpPreferenceError = null;
        try
        {
            var response = await _settingsApi.GetContractEffectiveSettingsAsync(context, ["ui.next_up_mode"]);
            if (revision != _nextUpPreferenceRevision || !_settingsApi.IsCurrentContext(context)) return;
            ApplyResolvedNextUpPreference(response.Settings.FirstOrDefault(e => e.Key == "ui.next_up_mode"), context);
        }
        catch
        {
            if (revision == _nextUpPreferenceRevision && _settingsApi.IsCurrentContext(context))
            { NextUpPreferenceLoaded = false; NextUpPreferenceError = "Next up preferences didn't load. Try again."; }
        }
        finally { if (revision == _nextUpPreferenceRevision) IsNextUpPreferencePending = false; }
    }

    [RelayCommand]
    private Task RetryNextUpPreferenceAsync() => LoadNextUpPreferenceAsync();

    private async Task WriteNextUpPreferenceAsync(bool reset)
    {
        if (_suppressSave || !CanChangeNextUpMode || (reset && !NextUpHasOwnChoice)) return;
        var context = _settingsApi.CaptureContext();
        var revision = ++_nextUpPreferenceRevision;
        IsNextUpPreferencePending = true;
        try
        {
            if (reset) await new InheritedPreferencePolicy(_settingsApi).ResetAsync(context, "ui.next_up_mode", _nextUpPreferenceSource!);
            else await _settingsApi.SaveProfileDefaultAsync(context, "ui.next_up_mode", NextUpMode, _nextUpDeviceOverride);
            if (revision != _nextUpPreferenceRevision || !_settingsApi.IsCurrentContext(context)) return;
            WeakReferenceMessenger.Default.Send(new HomePreferenceChanged(context, ReloadLayout: true));
            ShowStatus(reset ? "Using server default" : "Next up mode saved");
        }
        catch (Exception ex) { if (revision == _nextUpPreferenceRevision && _settingsApi.IsCurrentContext(context)) ErrorMessage = $"Failed to update next up preference: {ex.Message}"; }
        finally
        {
            if (revision == _nextUpPreferenceRevision)
            {
                if (_settingsApi.IsCurrentContext(context)) await LoadNextUpPreferenceAsync();
                else { NextUpPreferenceLoaded = false; IsNextUpPreferencePending = false; }
            }
        }
    }
}
