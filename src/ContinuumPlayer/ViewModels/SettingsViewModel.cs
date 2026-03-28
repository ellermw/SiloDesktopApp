using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Services;

namespace ContinuumPlayer.ViewModels;

/// <summary>Represents a library with its playback preference overrides for display in the Libraries settings tab.</summary>
public partial class LibraryCardViewModel : ObservableObject
{
    private readonly SettingsApi _settingsApi;
    private readonly Action<string> _showStatus;
    private readonly Action<string> _showError;
    private bool _suppressSave;

    public LibraryCardViewModel(Library library, LibraryPlaybackPreference? pref, SettingsApi settingsApi,
        Action<string> showStatus, Action<string> showError)
    {
        _settingsApi = settingsApi;
        _showStatus = showStatus;
        _showError = showError;

        LibraryId = library.Id;
        LibraryName = library.Name;
        LibraryType = library.Type;

        _suppressSave = true;
        if (pref != null)
        {
            AudioLanguage = pref.AudioLanguage ?? "";
            SubtitleLanguage = pref.SubtitleLanguage ?? "";
            SubtitleMode = pref.SubtitleMode ?? "";
            ForcedSubtitles = pref.ShowForcedSubtitles == true ? "on" : pref.ShowForcedSubtitles == false ? "off" : "";
        }
        else
        {
            AudioLanguage = "";
            SubtitleLanguage = "";
            SubtitleMode = "";
            ForcedSubtitles = "";
        }
        _suppressSave = false;
        UpdateSummary();
    }

    public int LibraryId { get; }
    public string LibraryName { get; }
    public string LibraryType { get; }

    [ObservableProperty]
    private string _audioLanguage = "";

    [ObservableProperty]
    private string _subtitleLanguage = "";

    [ObservableProperty]
    private string _subtitleMode = "";

    [ObservableProperty]
    private string _forcedSubtitles = "";

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _summaryText = "Uses profile defaults";

    [ObservableProperty]
    private bool _hasCustomOverrides;

    public bool IsAllDefaults =>
        string.IsNullOrEmpty(AudioLanguage) &&
        string.IsNullOrEmpty(SubtitleLanguage) &&
        string.IsNullOrEmpty(SubtitleMode) &&
        string.IsNullOrEmpty(ForcedSubtitles);

    private static readonly Dictionary<string, string> LanguageNames = new()
    {
        [""] = "Profile default",
        ["original"] = "Original Language",
        ["en"] = "English",
        ["es"] = "Spanish",
        ["fr"] = "French",
        ["de"] = "German",
        ["it"] = "Italian",
        ["pt"] = "Portuguese",
        ["ja"] = "Japanese",
        ["ko"] = "Korean",
        ["zh"] = "Chinese",
        ["ru"] = "Russian",
        ["ar"] = "Arabic",
        ["hi"] = "Hindi",
        ["none"] = "None",
    };

    private void UpdateSummary()
    {
        HasCustomOverrides = !IsAllDefaults;

        if (IsAllDefaults)
        {
            SummaryText = "Uses profile defaults";
            return;
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(AudioLanguage))
            parts.Add($"Audio: {GetLanguageName(AudioLanguage)}");
        if (!string.IsNullOrEmpty(SubtitleLanguage))
            parts.Add($"Subtitles: {GetLanguageName(SubtitleLanguage)}");
        if (!string.IsNullOrEmpty(SubtitleMode))
            parts.Add($"Behavior: {char.ToUpper(SubtitleMode[0]) + SubtitleMode[1..]}");
        if (!string.IsNullOrEmpty(ForcedSubtitles))
            parts.Add($"Forced: {char.ToUpper(ForcedSubtitles[0]) + ForcedSubtitles[1..]}");

        SummaryText = parts.Count > 0 ? string.Join(" \u2022 ", parts) : "Uses profile defaults";
    }

    private static string GetLanguageName(string code)
    {
        return LanguageNames.TryGetValue(code, out var name) ? name : code;
    }

    partial void OnAudioLanguageChanged(string value) => OnPrefChanged();
    partial void OnSubtitleLanguageChanged(string value) => OnPrefChanged();
    partial void OnSubtitleModeChanged(string value) => OnPrefChanged();
    partial void OnForcedSubtitlesChanged(string value) => OnPrefChanged();

    private void OnPrefChanged()
    {
        UpdateSummary();
        if (!_suppressSave)
            _ = SaveAsync();
    }

    private async Task SaveAsync()
    {
        try
        {
            if (IsAllDefaults)
            {
                await _settingsApi.DeleteLibraryPlaybackPrefsAsync(LibraryId);
                _showStatus("Reset to profile defaults");
            }
            else
            {
                var prefs = new Dictionary<string, object?>();
                prefs["audio_language"] = string.IsNullOrEmpty(AudioLanguage) ? null : AudioLanguage;
                prefs["subtitle_language"] = string.IsNullOrEmpty(SubtitleLanguage) ? null : SubtitleLanguage;
                prefs["subtitle_mode"] = string.IsNullOrEmpty(SubtitleMode) ? null : SubtitleMode;
                if (!string.IsNullOrEmpty(ForcedSubtitles))
                    prefs["show_forced_subtitles"] = ForcedSubtitles == "on";
                else
                    prefs["show_forced_subtitles"] = null;

                await _settingsApi.SetLibraryPlaybackPrefsAsync(LibraryId, prefs);
                _showStatus("Saved");
            }
        }
        catch (Exception ex)
        {
            _showError($"Failed to save library prefs: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        _suppressSave = true;
        AudioLanguage = "";
        SubtitleLanguage = "";
        SubtitleMode = "";
        ForcedSubtitles = "";
        _suppressSave = false;
        UpdateSummary();
        _ = SaveAsync();
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsApi _settingsApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;
    private readonly ThemeService _themeService;
    private Profile? _profile;
    private bool _suppressSave;

    public SettingsViewModel(SettingsApi settingsApi, CatalogApi catalogApi, AuthService authService, ThemeService themeService)
    {
        _settingsApi = settingsApi;
        _catalogApi = catalogApi;
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

    public ObservableCollection<LibraryCardViewModel> LibraryCards { get; } = [];

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

            // Load libraries and playback prefs for the Libraries tab
            await LoadLibraryCardsAsync();

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

    private async Task LoadLibraryCardsAsync()
    {
        try
        {
            var libraries = await _catalogApi.GetLibrariesAsync();

            LibraryPlaybackPrefsResponse? prefsResponse = null;
            try
            {
                prefsResponse = await _settingsApi.GetLibraryPlaybackPrefsAsync();
            }
            catch { /* Server may not support this endpoint yet */ }

            var prefsMap = new Dictionary<int, LibraryPlaybackPreference>();
            if (prefsResponse?.Preferences != null)
            {
                foreach (var p in prefsResponse.Preferences)
                    prefsMap[p.LibraryId] = p;
            }

            LibraryCards.Clear();
            foreach (var lib in libraries)
            {
                prefsMap.TryGetValue(lib.Id, out var pref);
                LibraryCards.Add(new LibraryCardViewModel(lib, pref, _settingsApi, ShowStatus, ShowError));
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: the rest of settings still work
            ErrorMessage = $"Failed to load libraries: {ex.Message}";
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
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
