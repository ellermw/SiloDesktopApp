using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using System.Text.Json;

namespace SiloPlayer.ViewModels;

public partial class SettingsViewModel
{
    private const string TitleArtKey = "ui.title_art";
    private int _titleArtRevision;
    private string? _titleArtSource;
    private ApiRequestContext? _titleArtContext;
    [ObservableProperty] private bool _titleArtSupported;
    [ObservableProperty] private bool _isLoadingTitleArt;
    [ObservableProperty] private bool _isSavingTitleArt;
    [ObservableProperty] private bool _titleArtReadFailed;
    [ObservableProperty] private string? _titleArtErrorMessage;
    [ObservableProperty] private bool _showTitleArt = true;
    [ObservableProperty] private bool _titleArtAllDevices;
    public bool CanEditTitleArt => TitleArtSupported && !IsLoadingTitleArt && !IsSavingTitleArt && !TitleArtReadFailed
        && _titleArtContext is { } context && _settingsApi.IsCurrentContext(context);
    public bool TitleArtHasOwnChoice => CanEditTitleArt && _titleArtSource is "profile" or "profile_device";
    public string TitleArtExplanation => !CanEditTitleArt ? "" : TitleArtAllDevices
        ? $"Title art is {(ShowTitleArt ? "on" : "off")} on every device signed into this profile. Changing it here changes it everywhere. Turn off “Apply to all devices” to choose per device again."
        : _titleArtSource == "default" ? $"Using the server default ({(ShowTitleArt ? "on" : "off")}). Change it here to choose for this device."
        : TitleArtHasOwnChoice ? "Only affects this device. Your other devices keep their own setting." : "";
    private void NotifyTitleArtInheritance()
    {
        OnPropertyChanged(nameof(CanEditTitleArt));
        OnPropertyChanged(nameof(TitleArtHasOwnChoice));
        OnPropertyChanged(nameof(TitleArtExplanation));
    }
    partial void OnTitleArtSupportedChanged(bool value) => NotifyTitleArtInheritance();
    partial void OnIsLoadingTitleArtChanged(bool value) => NotifyTitleArtInheritance();
    partial void OnIsSavingTitleArtChanged(bool value) => NotifyTitleArtInheritance();
    partial void OnTitleArtReadFailedChanged(bool value) => NotifyTitleArtInheritance();
    partial void OnShowTitleArtChanged(bool value) => OnPropertyChanged(nameof(TitleArtExplanation));
    partial void OnTitleArtAllDevicesChanged(bool value) => OnPropertyChanged(nameof(TitleArtExplanation));

    public void DeactivateTitleArt() { ++_titleArtRevision; _titleArtContext = null; TitleArtReadFailed = true; IsLoadingTitleArt = IsSavingTitleArt = false; }
    internal void PublishTitleArtPreferenceChanged() => TitleArtPreferenceChanged?.Invoke(this, EventArgs.Empty);

    public async Task LoadTitleArtAsync()
    {
        var revision = ++_titleArtRevision; var context = _settingsApi.CaptureContext();
        _titleArtSource = null;
        _titleArtContext = null;
        IsLoadingTitleArt = true; TitleArtReadFailed = false;
        try
        {
            var capabilities = await _settingsApi.GetContractCapabilitiesAsync(context);
            if (revision != _titleArtRevision || !_settingsApi.IsCurrentContext(context)) return;
            TitleArtSupported = capabilities.ApiVersion == 1 && capabilities.Revision >= 16 && capabilities.SupportsBatchedEffective;
            if (!TitleArtSupported) return;
            var response = await _settingsApi.GetContractEffectiveSettingsAsync(context, [TitleArtKey]);
            if (revision != _titleArtRevision || !_settingsApi.IsCurrentContext(context)) return;
            var entry = response.Settings.FirstOrDefault(value => value.Key == TitleArtKey);
            if (entry?.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidOperationException("Title art effective value missing.");
            _titleArtSource = entry.Source;
            _titleArtContext = context;
            ShowTitleArt = entry?.Value.ValueKind is not JsonValueKind.False;
            TitleArtAllDevices = entry?.Source == "profile";
        }
        catch { if (revision == _titleArtRevision && _settingsApi.IsCurrentContext(context)) { TitleArtReadFailed = true; TitleArtErrorMessage = "Title art settings could not be loaded. Try again."; } }
        finally { if (revision == _titleArtRevision) { if (!_settingsApi.IsCurrentContext(context)) TitleArtReadFailed = true; IsLoadingTitleArt = false; } }
    }

    [RelayCommand]
    private async Task ResetTitleArtAsync()
    {
        if (!TitleArtHasOwnChoice) return;
        var context = _settingsApi.CaptureContext(); var revision = _titleArtRevision;
        var source = _titleArtSource!;
        IsSavingTitleArt = true; TitleArtErrorMessage = null;
        try
        {
            await new InheritedPreferencePolicy(_settingsApi).ResetAsync(context, TitleArtKey, source);
            if (revision != _titleArtRevision || !_settingsApi.IsCurrentContext(context)) return;
            PublishTitleArtPreferenceChanged();
        }
        catch { if (revision == _titleArtRevision && _settingsApi.IsCurrentContext(context)) TitleArtErrorMessage = "Could not restore the server default. Try again."; }
        finally
        {
            if (revision == _titleArtRevision)
            {
                if (_settingsApi.IsCurrentContext(context)) await LoadTitleArtAsync();
                else TitleArtReadFailed = true;
                IsSavingTitleArt = false;
            }
        }
    }

    public async Task SaveTitleArtAsync(bool next, bool changeAllDevices = false)
    {
        if (!CanEditTitleArt) return;
        var context = _settingsApi.CaptureContext(); var revision = _titleArtRevision;
        var value = ShowTitleArt; var allDevices = TitleArtAllDevices;
        IsSavingTitleArt = true; TitleArtErrorMessage = null;
        try
        {
            if (changeAllDevices && !next)
            {
                await _settingsApi.SetContractSettingValueAsync(context, TitleArtKey, "profile_device", value);
                if (revision != _titleArtRevision || !_settingsApi.IsCurrentContext(context)) return;
                try { await _settingsApi.DeleteContractSettingValueAsync(context, TitleArtKey, "profile"); }
                catch (ApiException ex) when (ex.StatusCode == 404) { }
            }
            else await _settingsApi.SetContractSettingValueAsync(context, TitleArtKey,
                changeAllDevices || allDevices ? "profile" : "profile_device", changeAllDevices ? value : next);
            if (revision != _titleArtRevision || !_settingsApi.IsCurrentContext(context)) return;
            PublishTitleArtPreferenceChanged();
        }
        catch { if (revision == _titleArtRevision && _settingsApi.IsCurrentContext(context)) TitleArtErrorMessage = "Could not save the title art setting. Try again."; }
        finally
        {
            if (revision == _titleArtRevision)
            {
                if (_settingsApi.IsCurrentContext(context)) await LoadTitleArtAsync();
                else TitleArtReadFailed = true;
                IsSavingTitleArt = false;
            }
        }
    }
}
