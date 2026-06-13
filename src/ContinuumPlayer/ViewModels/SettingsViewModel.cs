using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.HistoryImport;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Models.Plugins;
using ContinuumPlayer.Core.Models.WatchProviders;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Services;

namespace ContinuumPlayer.ViewModels;

/// <summary>Represents a library with its playback preference overrides for display in the Libraries settings tab.</summary>
public partial class LibraryCardViewModel : ObservableObject
{
    private readonly SettingsApi _settingsApi;
    private readonly SettingsService _settingsService;
    private readonly Action<string> _showStatus;
    private readonly Action<string> _showError;
    private bool _suppressSave;

    /// <summary>Fired when the user toggles library visibility so the sidebar can update.</summary>
    public event Action? LibraryVisibilityChanged;

    public LibraryCardViewModel(Library library, LibraryPlaybackPreference? pref, SettingsApi settingsApi,
        SettingsService settingsService, Action<string> showStatus, Action<string> showError)
    {
        _settingsApi = settingsApi;
        _settingsService = settingsService;
        _showStatus = showStatus;
        _showError = showError;

        LibraryId = library.Id;
        LibraryName = library.Name;
        LibraryType = library.Type;

        // Initialise visibility from persisted settings
        var appSettings = settingsService.Load();
        _isEnabled = !appSettings.HiddenLibraryIds.Contains(library.Id);

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
    private bool _isEnabled = true;

    partial void OnIsEnabledChanged(bool value)
    {
        // Persist to settings
        var appSettings = _settingsService.Load();
        if (value)
            appSettings.HiddenLibraryIds.Remove(LibraryId);
        else if (!appSettings.HiddenLibraryIds.Contains(LibraryId))
            appSettings.HiddenLibraryIds.Add(LibraryId);
        _settingsService.Save(appSettings);
        LibraryVisibilityChanged?.Invoke();
    }

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
        ["original"] = "Original",
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

public partial class WatchProviderCardViewModel : ObservableObject
{
    private readonly WatchProviderSummary _summary;

    public WatchProviderCardViewModel(WatchProviderSummary summary)
    {
        _summary = summary;
        ProviderKey = summary.Key;
    }

    public string ProviderKey { get; }

    public string DisplayName =>
        Connection?.DisplayName
        ?? (!string.IsNullOrWhiteSpace(_summary.DisplayName) ? _summary.DisplayName : ProviderKey);

    public WatchProviderCapabilities Capabilities => Connection?.Capabilities ?? _summary.Capabilities;
    public bool Connected => Connection?.Connected == true;
    public bool CredentialsConfigured => Connection?.CredentialsConfigured == true;
    public string AuthMethod => Connection?.AuthMethod ?? WatchProviderAuthMethod.DeviceCode;
    public bool UsesApiKey => string.Equals(AuthMethod, WatchProviderAuthMethod.ApiKey, StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private WatchProviderConnection? _connection;

    [ObservableProperty]
    private WatchProviderSyncRun? _latestRun;

    [ObservableProperty]
    private WatchProviderDeviceAuthSession? _authSession;

    [ObservableProperty]
    private bool _apiKeyPromptVisible;

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private bool _isBusy;

    partial void OnConnectionChanged(WatchProviderConnection? value) => NotifyDerivedChanged();
    partial void OnAuthSessionChanged(WatchProviderDeviceAuthSession? value) => NotifyDerivedChanged();
    partial void OnApiKeyPromptVisibleChanged(bool value) => NotifyDerivedChanged();

    private void NotifyDerivedChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Capabilities));
        OnPropertyChanged(nameof(Connected));
        OnPropertyChanged(nameof(CredentialsConfigured));
        OnPropertyChanged(nameof(AuthMethod));
        OnPropertyChanged(nameof(UsesApiKey));
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private const string PlaybackAudioLanguageSettingKey = "playback.audio_language";

    private readonly SettingsApi _settingsApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthApi _authApi;
    private readonly HistoryImportApi _historyImportApi;
    private readonly WatchProvidersApi _watchProvidersApi;
    private readonly AuthService _authService;
    private readonly ThemeService _themeService;
    private readonly SettingsService _settingsService;
    private Profile? _profile;
    private bool _suppressSave;

    public SettingsViewModel(SettingsApi settingsApi, CatalogApi catalogApi, AuthApi authApi,
        HistoryImportApi historyImportApi, WatchProvidersApi watchProvidersApi,
        AuthService authService, ThemeService themeService, SettingsService settingsService)
    {
        _settingsApi = settingsApi;
        _catalogApi = catalogApi;
        _authApi = authApi;
        _historyImportApi = historyImportApi;
        _watchProvidersApi = watchProvidersApi;
        _authService = authService;
        _themeService = themeService;
        _settingsService = settingsService;
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
    private bool _autoSkipRecap;

    [ObservableProperty]
    private bool _autoPlayNextPreview;

    [ObservableProperty]
    private string _maxPlaybackQuality = "";

    [ObservableProperty]
    private string _audioLanguage = "";

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
                    AutoSkipRecap = _profile.AutoSkipRecap;
                    AutoPlayNextPreview = _profile.AutoPlayNextPreview;
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
                var audio = await _settingsApi.GetEffectiveSettingsAsync([PlaybackAudioLanguageSettingKey]);
                AudioLanguage = audio.Settings.FirstOrDefault(s => s.Key == PlaybackAudioLanguageSettingKey)?.EffectiveValue ?? "";
            }
            catch { AudioLanguage = ""; }

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
                LibraryCards.Add(new LibraryCardViewModel(lib, pref, _settingsApi, _settingsService, ShowStatus, ShowError));
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

    partial void OnAutoSkipRecapChanged(bool value)
    {
        if (!_suppressSave) _ = SaveProfileFieldAsync("auto_skip_recap", value);
    }

    partial void OnAutoPlayNextPreviewChanged(bool value)
    {
        if (!_suppressSave) _ = SaveProfileFieldAsync("auto_play_next_preview", value);
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
    private async Task SaveAudioLanguageAsync()
    {
        if (_suppressSave) return;
        await _settingsApi.PutDeviceSettingAsync(PlaybackAudioLanguageSettingKey, AudioLanguage);
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

    // ===== Auth Sessions =====

    public ObservableCollection<AuthSession> Sessions { get; } = [];

    [ObservableProperty]
    private bool _isLoadingSessions;

    [RelayCommand]
    private async Task LoadSessionsAsync()
    {
        if (IsLoadingSessions) return;
        IsLoadingSessions = true;

        try
        {
            var response = await _authApi.GetSessionsAsync();
            Sessions.Clear();
            foreach (var session in response.Sessions)
            {
                Sessions.Add(session);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load sessions: {ex.Message}";
        }
        finally
        {
            IsLoadingSessions = false;
        }
    }

    [RelayCommand]
    private async Task RevokeSessionAsync(string sessionId)
    {
        try
        {
            await _authApi.RevokeSessionAsync(sessionId);
            ShowStatus("Session revoked");
            await LoadSessionsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to revoke session: {ex.Message}";
        }
    }

    // ===== Watch Providers =====

    public ObservableCollection<WatchProviderCardViewModel> WatchProviderCards { get; } = [];

    [ObservableProperty]
    private bool _isLoadingWatchProviders;

    public async Task LoadWatchProvidersAsync()
    {
        if (IsLoadingWatchProviders) return;
        IsLoadingWatchProviders = true;
        ErrorMessage = null;

        try
        {
            var providers = await _watchProvidersApi.GetProvidersAsync();
            WatchProviderCards.Clear();

            foreach (var provider in providers.Providers)
            {
                var card = new WatchProviderCardViewModel(provider);
                WatchProviderCards.Add(card);
                await LoadWatchProviderCardStateAsync(card);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load watch providers: {ex.Message}";
        }
        finally
        {
            IsLoadingWatchProviders = false;
        }
    }

    private async Task LoadWatchProviderCardStateAsync(WatchProviderCardViewModel card)
    {
        card.IsBusy = true;
        try
        {
            card.Connection = await _watchProvidersApi.GetConnectionAsync(card.ProviderKey);
            card.AuthSession = null;
            card.LatestRun = null;

            if (card.Connection.Connected)
            {
                try
                {
                    var runs = await _watchProvidersApi.GetSyncRunsAsync(card.ProviderKey);
                    card.LatestRun = runs.Runs.FirstOrDefault();
                }
                catch
                {
                    // Sync history is nice-to-have; the card remains usable.
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load {card.DisplayName}: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    public async Task StartWatchProviderAuthAsync(WatchProviderCardViewModel card)
    {
        if (card.IsBusy) return;
        if (card.UsesApiKey)
        {
            card.ApiKeyPromptVisible = true;
            return;
        }

        card.IsBusy = true;
        ErrorMessage = null;

        try
        {
            card.AuthSession = await _watchProvidersApi.StartDeviceAuthAsync(card.ProviderKey);
            ShowStatus($"{card.DisplayName} activation started");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start {card.DisplayName} activation: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    public async Task ConnectWatchProviderApiKeyAsync(WatchProviderCardViewModel card)
    {
        if (card.IsBusy) return;
        var apiKey = card.ApiKey.Trim();
        if (apiKey.Length == 0)
        {
            ErrorMessage = $"{card.DisplayName} API key is required.";
            return;
        }

        card.IsBusy = true;
        ErrorMessage = null;

        try
        {
            card.Connection = await _watchProvidersApi.ConnectApiKeyAsync(card.ProviderKey, apiKey);
            card.ApiKey = "";
            card.ApiKeyPromptVisible = false;
            await LoadWatchProviderRunsAsync(card);
            ShowStatus($"{card.DisplayName} connected");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to connect {card.DisplayName}: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    public async Task PollWatchProviderAuthAsync(WatchProviderCardViewModel card)
    {
        if (card.IsBusy || card.AuthSession == null) return;
        card.IsBusy = true;
        ErrorMessage = null;

        try
        {
            card.Connection = await _watchProvidersApi.PollDeviceAuthAsync(card.ProviderKey, card.AuthSession.Id);
            card.AuthSession = null;
            await LoadWatchProviderRunsAsync(card);
            ShowStatus($"{card.DisplayName} connected");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to finish {card.DisplayName} activation: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    public Task UpdateWatchProviderConnectionAsync(WatchProviderCardViewModel card, string field, bool value)
        => UpdateWatchProviderConnectionAsync(card, new Dictionary<string, object?> { [field] = value });

    public async Task UpdateWatchProviderConnectionAsync(WatchProviderCardViewModel card, IDictionary<string, object?> updates)
    {
        if (card.IsBusy) return;
        card.IsBusy = true;
        ErrorMessage = null;

        try
        {
            card.Connection = await _watchProvidersApi.UpdateConnectionAsync(card.ProviderKey, updates);
            ShowStatus($"{card.DisplayName} saved");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save {card.DisplayName}: {ex.Message}";
            await LoadWatchProviderCardStateAsync(card);
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    public async Task TriggerWatchProviderSyncAsync(WatchProviderCardViewModel card)
    {
        if (card.IsBusy) return;
        card.IsBusy = true;
        ErrorMessage = null;

        try
        {
            var response = await _watchProvidersApi.TriggerSyncAsync(card.ProviderKey);
            card.LatestRun = response.Run;
            await LoadWatchProviderCardStateAsync(card);
            ShowStatus($"{card.DisplayName} sync started");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to sync {card.DisplayName}: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    public async Task DeleteWatchProviderConnectionAsync(WatchProviderCardViewModel card)
    {
        if (card.IsBusy) return;
        card.IsBusy = true;
        ErrorMessage = null;

        try
        {
            await _watchProvidersApi.DeleteConnectionAsync(card.ProviderKey);
            await LoadWatchProviderCardStateAsync(card);
            ShowStatus($"{card.DisplayName} disconnected");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to disconnect {card.DisplayName}: {ex.Message}";
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    private async Task LoadWatchProviderRunsAsync(WatchProviderCardViewModel card)
    {
        try
        {
            var runs = await _watchProvidersApi.GetSyncRunsAsync(card.ProviderKey);
            card.LatestRun = runs.Runs.FirstOrDefault();
        }
        catch
        {
            card.LatestRun = null;
        }
    }

    // ===== Subtitle Appearance =====
    // B55: model shape + defaults + JSON keys aligned with the webui
    // SubtitleAppearance type in web/src/lib/subtitleAppearance.ts so the
    // persisted `subtitle_appearance` user setting round-trips across both
    // clients. Keys are camelCase; opacity is an integer on a 0-100 scale
    // (NOT a 0-1 float); textOutline defaults to false and fontFamily
    // defaults to "sans-serif".

    [ObservableProperty]
    private string _subFontFamily = "sans-serif";

    [ObservableProperty]
    private string _subFontSize = "medium";

    [ObservableProperty]
    private string _subFontColor = "#ffffff";

    [ObservableProperty]
    private bool _subOutlineEnabled = false;

    [ObservableProperty]
    private string _subBackgroundStyle = "box";

    /// <summary>Background opacity, 0-100 integer scale (matches webui).</summary>
    [ObservableProperty]
    private int _subBackgroundOpacity = 75;

    [ObservableProperty]
    private string _subBackgroundColor = "#000000";

    [ObservableProperty]
    private string _subPosition = "bottom";

    [RelayCommand]
    private async Task SaveSubtitleAppearanceAsync()
    {
        try
        {
            var settings = new Dictionary<string, object>
            {
                ["fontFamily"] = SubFontFamily,
                ["fontSize"] = SubFontSize,
                ["fontColor"] = SubFontColor,
                ["textOutline"] = SubOutlineEnabled,
                ["backgroundStyle"] = SubBackgroundStyle,
                ["backgroundOpacity"] = SubBackgroundOpacity,
                ["backgroundColor"] = SubBackgroundColor,
                ["position"] = SubPosition,
            };
            await _settingsApi.PutSettingAsync("subtitle_appearance", System.Text.Json.JsonSerializer.Serialize(settings));

            // B55 follow-up: push the saved values to mpv so they apply to
            // the currently playing session AND the next one (PlayerService
            // caches and replays on init).
            try
            {
                var player = App.Services.GetRequiredService<PlayerService>();
                player.ApplySubtitleAppearance(new Core.Models.Settings.SubtitleAppearance
                {
                    FontFamily = SubFontFamily,
                    FontSize = SubFontSize,
                    FontColor = SubFontColor,
                    TextOutline = SubOutlineEnabled,
                    BackgroundStyle = SubBackgroundStyle,
                    BackgroundOpacity = SubBackgroundOpacity,
                    BackgroundColor = SubBackgroundColor,
                    Position = SubPosition,
                });
            }
            catch { /* mpv not initialized yet — will be applied on first play */ }

            ShowStatus("Subtitle appearance saved");
        }
        catch (Exception ex) { ErrorMessage = $"Failed to save subtitle appearance: {ex.Message}"; }
    }

    [RelayCommand]
    private void ResetSubtitleAppearance()
    {
        SubFontFamily = "sans-serif";
        SubFontSize = "medium";
        SubFontColor = "#ffffff";
        SubOutlineEnabled = false;
        SubBackgroundStyle = "box";
        SubBackgroundOpacity = 75;
        SubBackgroundColor = "#000000";
        SubPosition = "bottom";
        _ = SaveSubtitleAppearanceAsync();
    }

    // ===== Home Screen Sections =====

    public ObservableCollection<SettingsSectionEntry> HomeSections { get; } = [];

    [ObservableProperty]
    private string _selectedScope = "home";

    [ObservableProperty]
    private bool _isLoadingHomeSections;

    [RelayCommand]
    private async Task LoadHomeSectionsAsync()
    {
        if (IsLoadingHomeSections) return;
        IsLoadingHomeSections = true;
        try
        {
            var response = await _settingsApi.GetProfileSectionSettingsAsync();
            HomeSections.Clear();
            foreach (var section in response.Sections.OrderBy(s => s.Position))
            {
                HomeSections.Add(section);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load home sections: {ex.Message}";
        }
        finally
        {
            IsLoadingHomeSections = false;
        }
    }

    [RelayCommand]
    private async Task SaveHomeSectionsAsync()
    {
        try
        {
            var overrides = HomeSections.Select((s, i) => new SectionOverride
            {
                SectionId = s.Id,
                Position = i,
                Hidden = s.Hidden,
                Title = s.Title,
            }).ToList();

            await _settingsApi.UpdateProfileSectionsAsync(new SaveOverridesRequest
            {
                Scope = SelectedScope == "home" ? "home" : "library",
                LibraryId = SelectedScope == "home" ? null : SelectedScope,
                Overrides = overrides,
            });
            ShowStatus("Home sections saved");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save home sections: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ResetHomeSectionsAsync()
    {
        try
        {
            await _settingsApi.ResetProfileSectionsAsync();
            ShowStatus("Home sections reset to defaults");
            await LoadHomeSectionsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to reset home sections: {ex.Message}";
        }
    }

    public void ToggleSectionVisibility(SettingsSectionEntry section)
    {
        section.Hidden = !section.Hidden;
        // Trigger a change notification
        var idx = HomeSections.IndexOf(section);
        if (idx >= 0)
        {
            HomeSections.RemoveAt(idx);
            HomeSections.Insert(idx, section);
        }
    }

    public void MoveSectionUp(SettingsSectionEntry section)
    {
        var idx = HomeSections.IndexOf(section);
        if (idx > 0)
        {
            HomeSections.Move(idx, idx - 1);
        }
    }

    public void MoveSectionDown(SettingsSectionEntry section)
    {
        var idx = HomeSections.IndexOf(section);
        if (idx >= 0 && idx < HomeSections.Count - 1)
        {
            HomeSections.Move(idx, idx + 1);
        }
    }

    public void RemoveSection(SettingsSectionEntry section)
    {
        HomeSections.Remove(section);
    }

    // ===== History Import =====
    // Full rebuild to match WebUI 2026-04-10:
    //   - Three source types: Emby (Connect|Saved), Plex (OAuth|Saved), Jellyfin (Direct)
    //   - Per-mode credential forms (matches HistoryImportSettings.tsx 998 lines)
    //   - Profile selector (import target)
    //   - Saved sources list (admin-configured servers, filtered by source type)
    //   - Live "active run" tracking via EventChannelClient subscription
    //   - Recent runs list populated from events + initial GET

    [ObservableProperty] private string _importSourceType = "emby";        // "emby" | "plex" | "jellyfin"
    [ObservableProperty] private string _importEmbyMode = "connect";       // "connect" | "saved"
    [ObservableProperty] private string _importPlexMode = "oauth";         // "oauth" | "saved"

    /// <summary>Target profile the imported history will be attached to.</summary>
    [ObservableProperty] private string _importProfileId = "";

    public ObservableCollection<Core.Models.Auth.Profile> ImportProfiles { get; } = [];

    // ----- Emby Connect state -----
    [ObservableProperty] private string _embyConnectUsername = "";
    [ObservableProperty] private string _embyConnectPassword = "";
    [ObservableProperty] private string? _embyConnectSessionId;
    [ObservableProperty] private bool _embyConnectLoginPending;
    public ObservableCollection<HistoryImportConnectServer> EmbyConnectServers { get; } = [];
    [ObservableProperty] private HistoryImportConnectServer? _selectedEmbyConnectServer;

    // ----- Emby Saved state -----
    [ObservableProperty] private HistoryImportSource? _selectedEmbySavedSource;
    [ObservableProperty] private string _embySavedUsername = "";
    [ObservableProperty] private string _embySavedPassword = "";

    // ----- Plex OAuth state -----
    [ObservableProperty] private string? _plexSessionId;
    [ObservableProperty] private string _plexAuthStatus = "";
    [ObservableProperty] private bool _plexAuthPending;
    [ObservableProperty] private string? _plexAuthError;
    public ObservableCollection<PlexServer> PlexOAuthServers { get; } = [];
    [ObservableProperty] private PlexServer? _selectedPlexOAuthServer;

    // ----- Plex Saved state -----
    [ObservableProperty] private HistoryImportSource? _selectedPlexSavedSource;
    [ObservableProperty] private string _plexSavedToken = "";

    // ----- Jellyfin Direct state -----
    [ObservableProperty] private string _jellyfinServerUrl = "";
    [ObservableProperty] private string _jellyfinUsername = "";
    [ObservableProperty] private string _jellyfinPassword = "";

    // ----- Saved sources (admin-configured servers, shared across Emby/Plex Saved modes) -----
    public ObservableCollection<HistoryImportSource> AllImportSources { get; } = [];
    public ObservableCollection<HistoryImportSource> EmbySavedSources { get; } = [];
    public ObservableCollection<HistoryImportSource> PlexSavedSources { get; } = [];
    [ObservableProperty] private bool _isLoadingImportSources;

    // ----- Runs / history -----
    /// <summary>Current / most-recent run shown in the summary card. Updated live
    /// via events as well as from the initial GET.</summary>
    [ObservableProperty] private HistoryImportRun? _displayRun;

    /// <summary>ID of the run the user explicitly selected from the history list.
    /// Overrides "most recent" for display purposes while set.</summary>
    [ObservableProperty] private string? _selectedRunId;

    public ObservableCollection<HistoryImportRun> ImportRuns { get; } = [];

    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private bool _isLoadingImportRuns;

    /// <summary>True while the EventChannelClient has an open WebSocket to /events/ws
    /// with a live history_import subscription.</summary>
    [ObservableProperty] private bool _importEventsConnected;

    /// <summary>Login to Emby Connect with the entered credentials. On success,
    /// populates <see cref="EmbyConnectServers"/> and selects the first server.</summary>
    [RelayCommand]
    private async Task EmbyConnectLoginAsync()
    {
        if (EmbyConnectLoginPending) return;
        EmbyConnectLoginPending = true;
        ErrorMessage = null;
        try
        {
            var response = await _historyImportApi.EmbyConnectLoginAsync(new EmbyConnectLoginRequest
            {
                Username = EmbyConnectUsername,
                Password = EmbyConnectPassword,
            });
            EmbyConnectSessionId = response.ConnectSessionId;
            EmbyConnectServers.Clear();
            foreach (var server in response.Servers)
                EmbyConnectServers.Add(server);
            SelectedEmbyConnectServer = EmbyConnectServers.FirstOrDefault();
            ShowStatus($"Found {response.Servers.Count} Emby server(s)");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Emby Connect login failed: {ex.Message}";
        }
        finally
        {
            EmbyConnectLoginPending = false;
        }
    }

    /// <summary>Start the Plex OAuth PIN flow: create a pin, open the browser,
    /// then poll server for authentication up to ~2 minutes.</summary>
    [RelayCommand]
    private async Task PlexAuthStartAsync()
    {
        if (PlexAuthPending) return;
        PlexAuthPending = true;
        PlexAuthError = null;
        PlexAuthStatus = "Requesting PIN...";
        try
        {
            var pinResponse = await _historyImportApi.PlexAuthPinAsync();
            PlexSessionId = pinResponse.SessionId;
            PlexAuthStatus = $"Waiting for approval in browser (PIN: {pinResponse.PinCode})";

            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pinResponse.AuthUrl) { UseShellExecute = true }); }
            catch { /* browser launch is best-effort */ }

            // Poll for completion every 2s for up to 2 minutes.
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(2000);
                var checkResponse = await _historyImportApi.PlexAuthCheckAsync(new PlexCheckRequest { SessionId = pinResponse.SessionId });
                if (checkResponse.Authenticated)
                {
                    PlexAuthStatus = "Connected";
                    PlexOAuthServers.Clear();
                    if (checkResponse.Servers != null)
                        foreach (var server in checkResponse.Servers)
                            PlexOAuthServers.Add(server);
                    SelectedPlexOAuthServer = PlexOAuthServers.FirstOrDefault();
                    return;
                }
            }
            PlexAuthStatus = "";
            PlexAuthError = "Timed out waiting for Plex approval. Try again.";
        }
        catch (Exception ex)
        {
            PlexAuthStatus = "";
            PlexAuthError = $"Plex sign-in failed: {ex.Message}";
        }
        finally
        {
            PlexAuthPending = false;
        }
    }

    /// <summary>True when the Start Import button should be enabled for the
    /// current source + mode + profile selection. Mirrors the WebUI canStart
    /// logic in HistoryImportSettings.utils.ts.</summary>
    public bool CanStartImport
    {
        get
        {
            if (string.IsNullOrEmpty(ImportProfileId)) return false;
            return ImportSourceType switch
            {
                "emby" => ImportEmbyMode switch
                {
                    "connect" => !string.IsNullOrEmpty(EmbyConnectSessionId)
                                 && SelectedEmbyConnectServer != null,
                    "saved"   => SelectedEmbySavedSource != null
                                 && !string.IsNullOrWhiteSpace(EmbySavedUsername)
                                 && !string.IsNullOrWhiteSpace(EmbySavedPassword),
                    _ => false,
                },
                "plex" => ImportPlexMode switch
                {
                    "oauth" => SelectedPlexOAuthServer != null,
                    "saved" => SelectedPlexSavedSource != null
                                && !string.IsNullOrWhiteSpace(PlexSavedToken),
                    _ => false,
                },
                "jellyfin" => !string.IsNullOrWhiteSpace(JellyfinServerUrl)
                              && !string.IsNullOrWhiteSpace(JellyfinUsername)
                              && !string.IsNullOrWhiteSpace(JellyfinPassword),
                _ => false,
            };
        }
    }

    // Re-raise CanStartImport when any of its dependencies change.
    partial void OnImportSourceTypeChanged(string value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnImportEmbyModeChanged(string value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnImportPlexModeChanged(string value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnImportProfileIdChanged(string value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnEmbyConnectSessionIdChanged(string? value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnSelectedEmbyConnectServerChanged(HistoryImportConnectServer? value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnSelectedEmbySavedSourceChanged(HistoryImportSource? value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnEmbySavedUsernameChanged(string value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnEmbySavedPasswordChanged(string value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnSelectedPlexOAuthServerChanged(PlexServer? value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnSelectedPlexSavedSourceChanged(HistoryImportSource? value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnPlexSavedTokenChanged(string value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnJellyfinServerUrlChanged(string value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnJellyfinUsernameChanged(string value) => OnPropertyChanged(nameof(CanStartImport));
    partial void OnJellyfinPasswordChanged(string value) => OnPropertyChanged(nameof(CanStartImport));

    /// <summary>Kick off a new import run using the currently-configured source + mode.</summary>
    [RelayCommand]
    private async Task StartImportAsync()
    {
        if (IsImporting || !CanStartImport) return;
        IsImporting = true;
        ErrorMessage = null;
        try
        {
            var request = new CreateHistoryImportRunRequest
            {
                ProfileId = ImportProfileId,
                Source = ImportSourceType,
            };

            switch (ImportSourceType)
            {
                case "emby" when ImportEmbyMode == "connect":
                    request.ConnectSessionId = EmbyConnectSessionId;
                    request.ServerId = SelectedEmbyConnectServer?.ServerId;
                    break;

                case "emby" when ImportEmbyMode == "saved":
                    request.SourceId = SelectedEmbySavedSource?.Id;
                    request.Username = EmbySavedUsername;
                    request.Password = EmbySavedPassword;
                    break;

                case "plex" when ImportPlexMode == "oauth":
                    // Plex OAuth mode: WebUI sends plex_session_id + plex_server_id per the fresh source.
                    // The server then exchanges them for a local token.
                    request.PlexSessionId = PlexSessionId;
                    request.PlexServerId = SelectedPlexOAuthServer?.ClientIdentifier;
                    break;

                case "plex" when ImportPlexMode == "saved":
                    request.SourceId = SelectedPlexSavedSource?.Id;
                    request.PlexToken = PlexSavedToken;
                    break;

                case "jellyfin":
                    request.JellyfinBaseUrl = JellyfinServerUrl.Trim();
                    request.JellyfinUsername = JellyfinUsername;
                    request.JellyfinPassword = JellyfinPassword;
                    break;
            }

            var run = await _historyImportApi.CreateImportRunAsync(request);

            // Display the new run immediately and put it at the top of the history list.
            SelectedRunId = run.Id;
            DisplayRun = run;
            ImportRuns.Insert(0, run);

            ShowStatus("Import started");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to start import: {ex.Message}";
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>Load saved sources + recent runs + available profiles. Called when
    /// the Import tab is first shown.</summary>
    [RelayCommand]
    public async Task LoadImportTabAsync()
    {
        if (IsLoadingImportSources) return;
        IsLoadingImportSources = true;
        try
        {
            // Default target profile = currently-selected profile on AuthService
            if (string.IsNullOrEmpty(ImportProfileId) && !string.IsNullOrEmpty(_authService.SelectedProfileId))
                ImportProfileId = _authService.SelectedProfileId;

            // Fetch saved sources (admin-configured).
            try
            {
                var sources = await _historyImportApi.GetImportSourcesAsync();
                AllImportSources.Clear();
                EmbySavedSources.Clear();
                PlexSavedSources.Clear();
                foreach (var s in sources)
                {
                    AllImportSources.Add(s);
                    if (s.SourceType == "emby") EmbySavedSources.Add(s);
                    else if (s.SourceType == "plex") PlexSavedSources.Add(s);
                }
                SelectedEmbySavedSource ??= EmbySavedSources.FirstOrDefault();
                SelectedPlexSavedSource ??= PlexSavedSources.FirstOrDefault();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Failed to load saved import sources: {ex.Message}";
            }

            // Fetch the user's profiles for the import-target dropdown.
            try
            {
                var profilesResp = await _authApi.GetProfilesAsync();
                ImportProfiles.Clear();
                foreach (var p in profilesResp.Profiles)
                    ImportProfiles.Add(p);
            }
            catch { /* Non-fatal; dropdown will be empty */ }

            // Fetch recent runs.
            await LoadImportRunsAsync();
        }
        finally
        {
            IsLoadingImportSources = false;
        }
    }

    [RelayCommand]
    private async Task LoadImportRunsAsync()
    {
        if (IsLoadingImportRuns) return;
        IsLoadingImportRuns = true;
        try
        {
            var runs = await _historyImportApi.GetImportRunsAsync();
            ImportRuns.Clear();
            foreach (var run in runs.OrderByDescending(r => r.CreatedAt))
                ImportRuns.Add(run);
            // Default display run = most recent if nothing selected.
            if (DisplayRun == null) DisplayRun = ImportRuns.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load import runs: {ex.Message}";
        }
        finally
        {
            IsLoadingImportRuns = false;
        }
    }

    /// <summary>Merge a live event update for an import run into the view-model
    /// state — updates the recent-runs list and the currently-displayed run if
    /// it matches. Called from the EventChannelClient event handler in the page.</summary>
    public void ApplyImportRunUpdate(HistoryImportRun run)
    {
        // Update or insert in recent runs list.
        var existingIdx = -1;
        for (int i = 0; i < ImportRuns.Count; i++)
        {
            if (ImportRuns[i].Id == run.Id) { existingIdx = i; break; }
        }
        if (existingIdx >= 0) ImportRuns[existingIdx] = run;
        else ImportRuns.Insert(0, run);

        // Update display run when it's the one being tracked.
        if (SelectedRunId == run.Id) DisplayRun = run;
        else if (SelectedRunId == null && ImportRuns.Count > 0) DisplayRun = ImportRuns[0];
    }

    /// <summary>Manually pick a historical run from the list to show in the summary card.</summary>
    public void SelectRunForDisplay(HistoryImportRun run)
    {
        SelectedRunId = run.Id;
        DisplayRun = run;
    }

    // ===== Plugin Settings =====

    public ObservableCollection<PluginSettingsSummary> PluginSettingsList { get; } = [];

    /// <summary>Maps installation ID to current values for editing.</summary>
    public Dictionary<int, Dictionary<string, string>> PluginSettingsValues { get; } = new();

    [ObservableProperty]
    private bool _isLoadingPlugins;

    [RelayCommand]
    private async Task LoadPluginSettingsAsync()
    {
        if (IsLoadingPlugins) return;
        IsLoadingPlugins = true;
        try
        {
            var response = await _settingsApi.GetPluginSettingsListAsync();
            PluginSettingsList.Clear();
            PluginSettingsValues.Clear();
            foreach (var installation in response.Installations)
            {
                if (installation.UserConfigSchema.Count > 0)
                {
                    PluginSettingsList.Add(installation);
                    // Load existing values
                    try
                    {
                        var detail = await _settingsApi.GetPluginSettingsAsync(installation.Id);
                        PluginSettingsValues[installation.Id] = new Dictionary<string, string>(detail.Values);
                    }
                    catch
                    {
                        PluginSettingsValues[installation.Id] = new Dictionary<string, string>();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load plugin settings: {ex.Message}";
        }
        finally
        {
            IsLoadingPlugins = false;
        }
    }

    [RelayCommand]
    private async Task SavePluginSettingsAsync(int installationId)
    {
        try
        {
            if (PluginSettingsValues.TryGetValue(installationId, out var values))
            {
                await _settingsApi.UpdatePluginSettingsAsync(installationId, new UpdatePluginSettingsRequest { Values = values });
                ShowStatus("Plugin settings saved");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save plugin settings: {ex.Message}";
        }
    }
}
