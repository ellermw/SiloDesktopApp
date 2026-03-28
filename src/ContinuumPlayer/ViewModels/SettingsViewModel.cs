using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Services;

namespace ContinuumPlayer.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsApi _settingsApi;
    private readonly AuthService _authService;
    private readonly ThemeService _themeService;
    private Profile? _profile;
    private bool _suppressSave;

    public SettingsViewModel(SettingsApi settingsApi, AuthService authService, ThemeService themeService)
    {
        _settingsApi = settingsApi;
        _authService = authService;
        _themeService = themeService;
    }

    /// <summary>Theme service for populating theme list and applying themes.</summary>
    public ThemeService ThemeService => _themeService;

    // ===== Loading state =====
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    // ===== Appearance =====
    [ObservableProperty]
    private string _uiTheme = "";

    // ===== Playback =====
    [ObservableProperty]
    private string _qualityPreference = "";

    [ObservableProperty]
    private bool _autoSkipIntro;

    [ObservableProperty]
    private bool _autoSkipCredits;

    [ObservableProperty]
    private string _maxPlaybackQuality = "";

    // ===== Libraries =====
    [ObservableProperty]
    private bool _libraryRestrictionsEnabled;

    [ObservableProperty]
    private string _allowedLibraryIdsText = "";

    // ===== Subtitles =====
    [ObservableProperty]
    private string _subtitleLanguage = "";

    [ObservableProperty]
    private string _subtitleMode = "";

    [ObservableProperty]
    private bool _showForcedSubtitles;

    // ===== Home Screen =====
    [ObservableProperty]
    private string _nextUpMode = "";

    [ObservableProperty]
    private string _sectionOverrides = "";

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;

        try
        {
            _suppressSave = true;

            // Load profile settings
            var profileId = _authService.SelectedProfileId;
            if (!string.IsNullOrEmpty(profileId))
            {
                var profilesResponse = await _settingsApi.GetProfilesAsync();
                _profile = profilesResponse.Profiles.FirstOrDefault(p => p.Id == profileId);

                if (_profile != null)
                {
                    QualityPreference = _profile.QualityPreference;
                    AutoSkipIntro = _profile.AutoSkipIntro;
                    AutoSkipCredits = _profile.AutoSkipCredits;
                    MaxPlaybackQuality = _profile.MaxPlaybackQuality;
                    LibraryRestrictionsEnabled = _profile.LibraryRestrictionsEnabled;
                    AllowedLibraryIdsText = _profile.AllowedLibraryIds != null
                        ? string.Join(", ", _profile.AllowedLibraryIds)
                        : "";
                    SubtitleLanguage = _profile.SubtitleLanguage;
                    SubtitleMode = _profile.SubtitleMode;
                    ShowForcedSubtitles = _profile.ShowForcedSubtitles;
                }
            }

            // Load key-value settings
            try
            {
                var theme = await _settingsApi.GetSettingAsync("ui_theme");
                UiTheme = theme.Value;
                // Apply the server-side theme if it differs from the locally saved one
                if (!string.IsNullOrEmpty(UiTheme))
                    _themeService.ApplyTheme(UiTheme);
            }
            catch { UiTheme = ""; }

            try
            {
                var nextUp = await _settingsApi.GetSettingAsync("next_up_mode");
                NextUpMode = nextUp.Value;
            }
            catch { NextUpMode = ""; }

            try
            {
                var overrides = await _settingsApi.GetSettingAsync("section_overrides:home:");
                SectionOverrides = overrides.Value;
            }
            catch { SectionOverrides = ""; }

            _suppressSave = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load settings: {ex.Message}";
            _suppressSave = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ===== Profile field save helpers =====

    partial void OnAutoSkipIntroChanged(bool value)
    {
        if (!_suppressSave) _ = SaveProfileFieldAsync("auto_skip_intro", value);
    }

    partial void OnAutoSkipCreditsChanged(bool value)
    {
        if (!_suppressSave) _ = SaveProfileFieldAsync("auto_skip_credits", value);
    }

    partial void OnShowForcedSubtitlesChanged(bool value)
    {
        if (!_suppressSave) _ = SaveProfileFieldAsync("show_forced_subtitles", value);
    }

    partial void OnLibraryRestrictionsEnabledChanged(bool value)
    {
        if (!_suppressSave) _ = SaveProfileFieldAsync("library_restrictions_enabled", value);
    }

    // ===== Commands for saving non-bool fields =====

    [RelayCommand]
    private async Task SaveQualityPreferenceAsync()
    {
        if (_suppressSave) return;
        await SaveProfileFieldAsync("quality_preference", QualityPreference);
    }

    [RelayCommand]
    private async Task SaveMaxPlaybackQualityAsync()
    {
        if (_suppressSave) return;
        await SaveProfileFieldAsync("max_playback_quality", MaxPlaybackQuality);
    }

    [RelayCommand]
    private async Task SaveSubtitleLanguageAsync()
    {
        if (_suppressSave) return;
        await SaveProfileFieldAsync("subtitle_language", SubtitleLanguage);
    }

    [RelayCommand]
    private async Task SaveSubtitleModeAsync()
    {
        if (_suppressSave) return;
        await SaveProfileFieldAsync("subtitle_mode", SubtitleMode);
    }

    [RelayCommand]
    private async Task SaveUiThemeAsync()
    {
        if (_suppressSave) return;
        try
        {
            // Apply theme colors immediately
            _themeService.ApplyTheme(UiTheme);

            // Persist to server
            await _settingsApi.PutSettingAsync("ui_theme", UiTheme);
            ShowStatus("Theme saved");
        }
        catch (Exception ex) { ErrorMessage = $"Failed to save theme: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SaveNextUpModeAsync()
    {
        if (_suppressSave) return;
        try
        {
            await _settingsApi.PutSettingAsync("next_up_mode", NextUpMode);
            ShowStatus("Next up mode saved");
        }
        catch (Exception ex) { ErrorMessage = $"Failed to save next up mode: {ex.Message}"; }
    }

    private async Task SaveProfileFieldAsync(string field, object value)
    {
        var profileId = _authService.SelectedProfileId;
        if (string.IsNullOrEmpty(profileId)) return;

        try
        {
            var updates = new Dictionary<string, object> { [field] = value };
            await _settingsApi.UpdateProfileAsync(profileId, updates);
            ShowStatus($"Saved");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save {field}: {ex.Message}";
        }
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        ErrorMessage = null;
    }
}
