using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using System.Text.Json;

namespace SiloPlayer.ViewModels;

public partial class SettingsViewModel
{
    private const string TitleArtKey = "ui.title_art";
    private int _titleArtRevision;
    [ObservableProperty] private bool _titleArtSupported;
    [ObservableProperty] private bool _isLoadingTitleArt;
    [ObservableProperty] private bool _isSavingTitleArt;
    [ObservableProperty] private bool _titleArtReadFailed;
    [ObservableProperty] private string? _titleArtErrorMessage;
    [ObservableProperty] private bool _showTitleArt = true;
    [ObservableProperty] private bool _titleArtAllDevices;
    public bool CanEditTitleArt => TitleArtSupported && !IsLoadingTitleArt && !IsSavingTitleArt && !TitleArtReadFailed;
    public string TitleArtExplanation => TitleArtAllDevices
        ? $"Title art is {(ShowTitleArt ? "on" : "off")} on every device signed into this profile. Changing it here changes it everywhere. Turn off “Apply to all devices” to choose per device again."
        : "Only affects this device. Your other devices keep their own setting.";
    partial void OnTitleArtSupportedChanged(bool value) => OnPropertyChanged(nameof(CanEditTitleArt));
    partial void OnIsLoadingTitleArtChanged(bool value) => OnPropertyChanged(nameof(CanEditTitleArt));
    partial void OnIsSavingTitleArtChanged(bool value) => OnPropertyChanged(nameof(CanEditTitleArt));
    partial void OnTitleArtReadFailedChanged(bool value) => OnPropertyChanged(nameof(CanEditTitleArt));
    partial void OnShowTitleArtChanged(bool value) => OnPropertyChanged(nameof(TitleArtExplanation));
    partial void OnTitleArtAllDevicesChanged(bool value) => OnPropertyChanged(nameof(TitleArtExplanation));

    public void DeactivateTitleArt() { ++_titleArtRevision; TitleArtReadFailed = true; IsLoadingTitleArt = IsSavingTitleArt = false; }
    internal void PublishTitleArtPreferenceChanged() => TitleArtPreferenceChanged?.Invoke(this, EventArgs.Empty);

    public async Task LoadTitleArtAsync()
    {
        var revision = ++_titleArtRevision; var context = _settingsApi.CaptureContext();
        IsLoadingTitleArt = true; TitleArtReadFailed = false;
        try
        {
            var capabilities = await _settingsApi.GetContractCapabilitiesAsync();
            if (revision != _titleArtRevision || !_settingsApi.IsCurrentContext(context)) return;
            TitleArtSupported = capabilities.ApiVersion == 1 && capabilities.Revision >= 16 && capabilities.SupportsBatchedEffective;
            if (!TitleArtSupported) return;
            var response = await _settingsApi.GetContractEffectiveSettingsAsync([TitleArtKey]);
            if (revision != _titleArtRevision || !_settingsApi.IsCurrentContext(context)) return;
            var entry = response.Settings.FirstOrDefault(value => value.Key == TitleArtKey);
            ShowTitleArt = entry?.Value.ValueKind is not JsonValueKind.False;
            TitleArtAllDevices = entry?.Source == "profile";
        }
        catch { if (revision == _titleArtRevision && _settingsApi.IsCurrentContext(context)) { TitleArtReadFailed = true; TitleArtErrorMessage = "Title art settings could not be loaded. Try again."; } }
        finally { if (revision == _titleArtRevision) { if (!_settingsApi.IsCurrentContext(context)) TitleArtReadFailed = true; IsLoadingTitleArt = false; } }
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
