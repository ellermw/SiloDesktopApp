using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.HistoryImport;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Plugins;
using SiloPlayer.Core.Models.WatchProviders;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;

namespace SiloPlayer.ViewModels;

/// <summary>Represents a library with its playback preference overrides for display in the Libraries settings tab.</summary>
public partial class LibraryCardViewModel : ObservableObject
{
    private readonly SettingsApi _settingsApi;
    private readonly Action<int, bool> _visibilityChanged;
    private readonly Action<string> _showStatus;
    private readonly Action<string> _showError;
    private bool _suppressSave;

    /// <summary>Fired when the user toggles library visibility so the sidebar can update.</summary>
    public LibraryCardViewModel(Library library, LibraryPlaybackPreference? pref, SettingsApi settingsApi,
        bool enabled, Action<int, bool> visibilityChanged, Action<string> showStatus, Action<string> showError)
    {
        _settingsApi = settingsApi;
        _visibilityChanged = visibilityChanged;
        _showStatus = showStatus;
        _showError = showError;

        LibraryId = library.Id;
        LibraryName = library.Name;
        LibraryType = library.Type;

        _isEnabled = enabled;

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
        if (!_suppressSave) _visibilityChanged(LibraryId, value);
    }

    public void SetEnabledFromOwner(bool value)
    {
        _suppressSave = true;
        IsEnabled = value;
        _suppressSave = false;
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
        return code.Trim().ToLowerInvariant() switch
        {
            "" => "Profile default",
            "original" => "Original Language",
            "none" => "None",
            _ => MediaLanguageCatalog.Label(code),
        };
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
    public IReadOnlyList<SiloPlayer.Core.Models.Plugins.PluginConfigSchema> ConnectionSchemas => Connection?.ConnectionConfigSchema ?? _summary.ConnectionConfigSchema;
    public Dictionary<string, Dictionary<string, object?>> ConnectionConfigDraft { get; } = [];

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
    // Published only after an authoritative ui.title_art write or override reset.
    public static event EventHandler? TitleArtPreferenceChanged;
    private const string PlaybackAudioLanguageSettingKey = "playback.audio_language";
    private static readonly string[] ContractPlaybackSettingKeys =
    [
        "playback.preferred_quality",
        "playback.max_bitrate_kbps",
        PlaybackAudioLanguageSettingKey,
        "catalog.metadata_language",
        "catalog.metadata_language_overrides",
        "catalog.show_advisory_age",
        "home.hide_watched_items",
        "ui.theme_music_enabled",
        "ui.theme_music_loop",
        "playback.auto_skip_intro",
        "playback.intro_skip_mode",
        "player.video_skip_back_seconds", "player.video_skip_forward_seconds",
        "player.audiobook_skip_back_seconds", "player.audiobook_skip_forward_seconds",
        "playback.auto_skip_credits",
        "playback.auto_skip_recap",
        "playback.auto_play_next_preview",
        "playback.auto_play_next",
        "ui.next_up_mode",
    ];

    private readonly SettingsApi _settingsApi;
    private readonly CatalogApi _catalogApi;
    private readonly AuthApi _authApi;
    private readonly HistoryImportApi _historyImportApi;
    private readonly WatchProvidersApi _watchProvidersApi;
    private readonly AuthService _authService;
    private readonly ThemeService _themeService;
    private readonly SettingsService _settingsService;
    private readonly AccessibilityService _accessibilityService;
    private Profile? _profile;
    private bool _suppressSave;

    public SettingsViewModel(SettingsApi settingsApi, CatalogApi catalogApi, AuthApi authApi,
        HistoryImportApi historyImportApi, WatchProvidersApi watchProvidersApi,
        AuthService authService, ThemeService themeService, SettingsService settingsService,
        AccessibilityService accessibilityService)
    {
        _settingsApi = settingsApi;
        _catalogApi = catalogApi;
        _authApi = authApi;
        _historyImportApi = historyImportApi;
        _watchProvidersApi = watchProvidersApi;
        _authService = authService;
        _themeService = themeService;
        _settingsService = settingsService;
        _accessibilityService = accessibilityService;
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
    private string _dateFormat = "auto";

    [ObservableProperty]
    private string _timeFormat = "auto";

    [ObservableProperty]
    private string _textScale = "default";

    [ObservableProperty]
    private string _textWeight = "default";

    [ObservableProperty]
    private bool _highContrast;

    // ===== Playback =====
    [ObservableProperty]
    private string _qualityPreference = "";

    [ObservableProperty]
    private bool _autoSkipIntro;
    [ObservableProperty] private string _introSkipMode = "ask";
    [ObservableProperty] private int _videoSkipBackSeconds = 10;
    [ObservableProperty] private int _videoSkipForwardSeconds = 30;
    [ObservableProperty] private int _audiobookSkipBackSeconds = 10;
    [ObservableProperty] private int _audiobookSkipForwardSeconds = 30;
    [ObservableProperty] private bool _seekSettingsAvailable;
    [ObservableProperty] private string _seekSettingsStatus = "Checking seek settings compatibility…";
    private bool _suppressSeekSave;
    public int[] SeekIntervalChoices => SeekPreferences.Choices;
    public string[] IntroModeChoices => ["never", "ask", "always"];
    partial void OnIntroSkipModeChanged(string value)
    { if (!_suppressSave) _ = SaveContractProfileSettingAsync("playback.intro_skip_mode", value); }
    partial void OnVideoSkipBackSecondsChanged(int value) => SaveSeek(0, value);
    partial void OnVideoSkipForwardSecondsChanged(int value) => SaveSeek(1, value);
    partial void OnAudiobookSkipBackSecondsChanged(int value) => SaveSeek(2, value);
    partial void OnAudiobookSkipForwardSecondsChanged(int value) => SaveSeek(3, value);
    private async void SaveSeek(int index, int value)
    {
        if (_suppressSave || _suppressSeekSave || !SeekSettingsAvailable || !SeekPreferences.Choices.Contains(value)) return;
        var context = _settingsApi.CaptureContext();
        await SaveContractProfileSettingAsync(SeekPreferences.Keys[index], value);
        try
        {
            var response = await _settingsApi.GetContractEffectiveSettingsAsync(SeekPreferences.Keys);
            if (!_settingsApi.IsCurrentContext(context)) return;
            var seeks = SeekPreferences.Read(response);
            _suppressSeekSave = true;
            VideoSkipBackSeconds = seeks.VideoBack; VideoSkipForwardSeconds = seeks.VideoForward;
            AudiobookSkipBackSeconds = seeks.AudiobookBack; AudiobookSkipForwardSeconds = seeks.AudiobookForward;
        }
        catch (Exception ex) { if (_settingsApi.IsCurrentContext(context)) ShowError($"Could not reload seek settings: {ex.Message}"); }
        finally { _suppressSeekSave = false; }
    }
    [RelayCommand]
    private async Task ImportLegacySeekIntervalsAsync()
    {
        var context = _settingsApi.CaptureContext();
        var local = _settingsService.Load();
        try
        {
            foreach (var (index, value) in new[] { (2, local.AudiobookSkipBackSeconds), (3, local.AudiobookSkipForwardSeconds) })
            {
                if (!_settingsApi.IsCurrentContext(context)) return;
                if (SeekPreferences.Choices.Contains(value)) await _settingsApi.SetContractSettingValueAsync(SeekPreferences.Keys[index], "profile", value);
            }
            if (!_settingsApi.IsCurrentContext(context)) return;
            await LoadCommand.ExecuteAsync(null);
            await App.Services.GetRequiredService<PlayerService>().RefreshSeekPreferencesAsync();
            ShowStatus("Imported this PC's audiobook intervals into the selected profile");
        }
        catch (Exception ex) { if (_settingsApi.IsCurrentContext(context)) ShowError($"Import stopped; an interval may already have been saved: {ex.Message}"); }
    }
    [ObservableProperty] private bool _showAdvisoryAge;
    [ObservableProperty] private bool _hideWatchedItems;
    [ObservableProperty] private bool _themeMusicEnabled;
    [ObservableProperty] private bool _themeMusicLoop;
    partial void OnThemeMusicEnabledChanged(bool value)
    { if (!_suppressSave) { if (!value) App.Services.GetService<ThemeMusicService>()?.Stop(); _ = SaveContractProfileSettingAsync("ui.theme_music_enabled", value); } }
    partial void OnThemeMusicLoopChanged(bool value)
    { if (!_suppressSave) _ = SaveContractProfileSettingAsync("ui.theme_music_loop", value); }
    partial void OnHideWatchedItemsChanged(bool value)
    { if (!_suppressSave) _ = SaveContractProfileSettingAsync("home.hide_watched_items", value); }
    partial void OnShowAdvisoryAgeChanged(bool value)
    { if (!_suppressSave) _ = SaveContractProfileSettingAsync("catalog.show_advisory_age", value); }

    [ObservableProperty]
    private bool _autoSkipCredits;

    [ObservableProperty]
    private bool _autoSkipRecap;

    [ObservableProperty]
    private bool _autoPlayNextPreview;

    [ObservableProperty]
    private bool _autoPlayNext = true;

    [ObservableProperty]
    private string _maxPlaybackQuality = "";

    [ObservableProperty]
    private string _maxBitrateKbps = "";

    [ObservableProperty]
    private string _audioLanguage = "";

    [ObservableProperty]
    private string _preferredMetadataLanguage = "";

    private readonly SemaphoreSlim _metadataLanguageOverrideGuard = new(1, 1);
    private Dictionary<string, string> _metadataLanguageOverrides = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> MetadataLanguageOverrides => _metadataLanguageOverrides;

    // ===== Libraries =====
    [ObservableProperty]
    private bool _libraryRestrictionsEnabled;

    [ObservableProperty]
    private string _allowedLibraryIdsText = "";

    public ObservableCollection<LibraryCardViewModel> LibraryCards { get; } = [];

    [ObservableProperty]
    private int _visibleLibraryCount;

    public int TotalLibraryCount => LibraryCards.Count;
    public bool HasSelectedProfile => !string.IsNullOrWhiteSpace(_authService.SelectedProfileId);
    public bool CanShowAllLibraries => TotalLibraryCount > 0 && VisibleLibraryCount < TotalLibraryCount;
    public bool CanHideAllLibraries => VisibleLibraryCount > 0;
    private HashSet<int> _disabledLibraryIds = [];

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
                    AudioLanguage = _profile.Language;
                    PreferredMetadataLanguage = _profile.PreferredMetadataLanguage ?? "";
                    LibraryRestrictionsEnabled = _profile.LibraryRestrictionsEnabled;
                    AllowedLibraryIdsText = _profile.AllowedLibraryIds != null
                        ? string.Join(", ", _profile.AllowedLibraryIds)
                        : "";
                    SubtitleLanguage = _profile.SubtitleLanguage;
                    SubtitleMode = _profile.SubtitleMode;
                    ShowForcedSubtitles = _profile.ShowForcedSubtitles;
                }
            }

            // These settings live behind independent endpoints. The old serial chain
            // made opening Settings cost the sum of every network round trip and left
            // the page visibly filling in for several seconds. Load them concurrently,
            // while retaining the same per-setting fallback behavior.
            async Task LoadDateAsync()
            {
                try { DateFormat = NormalizeDateFormat((await _settingsApi.GetSettingAsync("ui.date_format")).Value); }
                catch { DateFormat = NormalizeDateFormat(_settingsService.Load().UiDateFormat); }
            }

            async Task LoadTimeAsync()
            {
                try { TimeFormat = NormalizeTimeFormat((await _settingsApi.GetSettingAsync("ui.time_format")).Value); }
                catch { TimeFormat = NormalizeTimeFormat(_settingsService.Load().UiTimeFormat); }
            }

            async Task LoadAccessibilityAsync()
            {
                try
                {
                    var accessibility = await _settingsApi.GetEffectiveSettingsAsync(
                        ["ui_text_scale", "ui_text_weight", "ui_high_contrast"]);
                    var scaleValue = accessibility.Settings.FirstOrDefault(x => x.Key == "ui_text_scale")?.EffectiveValue;
                    TextScale = scaleValue is "large" or "x-large" ? scaleValue : "default";
                    TextWeight = accessibility.Settings.FirstOrDefault(x => x.Key == "ui_text_weight")?.EffectiveValue == "strong" ? "strong" : "default";
                    HighContrast = string.Equals(accessibility.Settings.FirstOrDefault(x => x.Key == "ui_high_contrast")?.EffectiveValue, "true", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    var local = _settingsService.Load();
                    TextScale = local.UiTextScale;
                    TextWeight = local.UiTextWeight;
                    HighContrast = local.UiHighContrast;
                }
                _accessibilityService.Apply(TextScale, TextWeight, HighContrast);
            }

            async Task LoadNextUpAsync()
            {
                try
                {
                    var nextUp = await _settingsApi.GetSettingAsync("next_up_mode");
                    NextUpMode = string.IsNullOrWhiteSpace(nextUp.Value) ? "combined" : nextUp.Value;
                }
                catch { NextUpMode = "combined"; }
            }

            async Task LoadAutoPlayAsync()
            {
                try
                {
                    var autoPlay = await _settingsApi.GetEffectiveSettingsAsync(["playback.auto_play_next"]);
                    AutoPlayNext = !string.Equals(autoPlay.Settings.FirstOrDefault()?.EffectiveValue, "false", StringComparison.OrdinalIgnoreCase);
                }
                catch { AutoPlayNext = true; }
            }

            async Task LoadContractPlaybackAsync()
            {
                try
                {
                    SeekSettingsAvailable = false;
                    SeekSettingsStatus = "Checking seek settings compatibility…";
                    var capability = await _settingsApi.GetContractCapabilitiesAsync();
                    var supported = capability.ApiVersion == 1 && capability.Revision >= 9 && capability.SupportsBatchedEffective && capability.SupportsIdempotentWrites;
                    var keys = supported ? ContractPlaybackSettingKeys : ContractPlaybackSettingKeys.Except(SeekPreferences.Keys).ToArray();
                    var response = await _settingsApi.GetContractEffectiveSettingsAsync(keys);
                    SeekSettingsAvailable = supported && SeekPreferences.Keys.All(key => response.Settings.Any(entry => entry.Key == key));
                    SeekSettingsStatus = SeekSettingsAvailable ? "" : "This server does not support profile seek intervals.";
                    var values = response.Settings.ToDictionary(setting => setting.Key, StringComparer.Ordinal);

                    QualityPreference = ReadString(values, "playback.preferred_quality", "auto");
                    MaxBitrateKbps = ReadNullableNumber(values, "playback.max_bitrate_kbps");
                    AudioLanguage = ReadNullableString(values, PlaybackAudioLanguageSettingKey);
                    PreferredMetadataLanguage = ReadNullableString(values, "catalog.metadata_language");
                    _metadataLanguageOverrides = ReadStringMap(values, "catalog.metadata_language_overrides");
                    OnPropertyChanged(nameof(MetadataLanguageOverrides));
                    AutoSkipIntro = ReadBool(values, "playback.auto_skip_intro", AutoSkipIntro);
                    IntroSkipMode = ReadString(values, "playback.intro_skip_mode", AutoSkipIntro ? "always" : "ask");
                    var seeks = SeekPreferences.Read(response);
                    VideoSkipBackSeconds = seeks.VideoBack; VideoSkipForwardSeconds = seeks.VideoForward;
                    AudiobookSkipBackSeconds = seeks.AudiobookBack; AudiobookSkipForwardSeconds = seeks.AudiobookForward;
                    ShowAdvisoryAge = ReadBool(values, "catalog.show_advisory_age", false);
                    HideWatchedItems = ReadBool(values, "home.hide_watched_items", false);
                    ThemeMusicEnabled = ReadBool(values, "ui.theme_music_enabled", false);
                    ThemeMusicLoop = ReadBool(values, "ui.theme_music_loop", false);
                    AutoSkipCredits = ReadBool(values, "playback.auto_skip_credits", AutoSkipCredits);
                    AutoSkipRecap = ReadBool(values, "playback.auto_skip_recap", AutoSkipRecap);
                    AutoPlayNextPreview = ReadBool(values, "playback.auto_play_next_preview", AutoPlayNextPreview);
                    AutoPlayNext = ReadBool(values, "playback.auto_play_next", AutoPlayNext);
                    NextUpMode = ReadString(values, "ui.next_up_mode",
                        string.IsNullOrWhiteSpace(NextUpMode) ? "combined" : NextUpMode);
                }
                catch
                {
                    SeekSettingsAvailable = false;
                    SeekSettingsStatus = "Could not check seek settings compatibility. Reload settings to retry.";
                    // Servers predating the typed settings contract still use
                    // the profile/legacy values loaded above.
                }
            }

            async Task LoadSectionOverridesAsync()
            {
                try { SectionOverrides = System.Text.Json.JsonSerializer.Serialize((await _settingsApi.GetProfileSectionsAsync()).Overrides); }
                catch { SectionOverrides = ""; }
            }

            await Task.WhenAll(
                LoadLibraryCardsAsync(),
                _themeService.SyncFromServerAsync(),
                LoadDateAsync(),
                LoadTimeAsync(),
                LoadAccessibilityAsync(),
                LoadNextUpAsync(),
                LoadAutoPlayAsync(),
                LoadContractPlaybackAsync(),
                LoadSubtitleAppearanceAsync(),
                LoadSectionOverridesAsync());

            var localAppearance = _settingsService.Load();
            localAppearance.UiDateFormat = DateFormat;
            localAppearance.UiTimeFormat = TimeFormat;
            _settingsService.Save(localAppearance);

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

            _disabledLibraryIds = [];
            var libraryOrder = new List<int>();
            try
            {
                var disabled = await _settingsApi.GetSettingAsync("disabled_library_ids");
                _disabledLibraryIds = ParseLibraryIds(disabled.Value).ToHashSet();
            }
            catch { }
            try
            {
                var order = await _settingsApi.GetSettingAsync("library_order");
                libraryOrder = ParseLibraryIds(order.Value);
            }
            catch { }

            if (libraryOrder.Count > 0)
            {
                var positions = libraryOrder.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
                libraries = libraries.OrderBy(lib => positions.TryGetValue(lib.Id, out var index) ? index : int.MaxValue).ToList();
            }

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
                LibraryCards.Add(new LibraryCardViewModel(lib, pref, _settingsApi,
                    !_disabledLibraryIds.Contains(lib.Id), OnLibraryVisibilityChanged, ShowStatus, ShowError));
            }
            UpdateLibraryCounts();
            OnPropertyChanged(nameof(TotalLibraryCount));
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
        if (!_suppressSave) _ = SaveContractProfileSettingAsync("playback.auto_skip_intro", value);
    }

    partial void OnAutoSkipCreditsChanged(bool value)
    {
        if (!_suppressSave) _ = SaveContractProfileSettingAsync("playback.auto_skip_credits", value);
    }

    public async Task SetAccessibilityAsync(string? textScale = null, string? textWeight = null, bool? highContrast = null)
    {
        if (textScale != null) TextScale = textScale is "large" or "x-large" ? textScale : "default";
        if (textWeight != null) TextWeight = textWeight == "strong" ? "strong" : "default";
        if (highContrast.HasValue) HighContrast = highContrast.Value;

        _accessibilityService.Apply(TextScale, TextWeight, HighContrast);
        try
        {
            if (textScale != null) await _settingsApi.PutSettingAsync("ui_text_scale", TextScale);
            if (textWeight != null) await _settingsApi.PutSettingAsync("ui_text_weight", TextWeight);
            if (highContrast.HasValue) await _settingsApi.PutSettingAsync("ui_high_contrast", HighContrast ? "true" : "false");
            ShowStatus("Accessibility preference saved");
        }
        catch (Exception ex) { ShowError($"Failed to save accessibility preference: {ex.Message}"); }
    }

    private static List<int> ParseLibraryIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<int>>(json)?
                .Where(id => id > 0).Distinct().ToList() ?? [];
        }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private void OnLibraryVisibilityChanged(int libraryId, bool enabled)
    {
        if (enabled) _disabledLibraryIds.Remove(libraryId);
        else _disabledLibraryIds.Add(libraryId);
        UpdateLibraryCounts();
        _ = SaveLibraryVisibilityAsync();
    }

    private void UpdateLibraryCounts()
    {
        VisibleLibraryCount = LibraryCards.Count(card => card.IsEnabled);
        OnPropertyChanged(nameof(CanShowAllLibraries));
        OnPropertyChanged(nameof(CanHideAllLibraries));
    }

    private async Task SaveLibraryVisibilityAsync()
    {
        try
        {
            var orderedDisabled = LibraryCards.Where(card => _disabledLibraryIds.Contains(card.LibraryId)).Select(card => card.LibraryId).ToList();
            await _settingsApi.PutSettingAsync("disabled_library_ids", System.Text.Json.JsonSerializer.Serialize(orderedDisabled));

            // Keep the desktop navigation cache synchronized immediately; the
            // server value remains authoritative on the next settings load.
            var local = _settingsService.Load();
            local.HiddenLibraryIds = orderedDisabled;
            _settingsService.Save(local);
            ShowStatus("Library visibility saved");
        }
        catch (Exception ex) { ShowError($"Failed to update library visibility: {ex.Message}"); }
    }

    [RelayCommand]
    private void ShowAllLibraries()
    {
        _disabledLibraryIds.Clear();
        foreach (var card in LibraryCards) card.SetEnabledFromOwner(true);
        UpdateLibraryCounts();
        _ = SaveLibraryVisibilityAsync();
    }

    [RelayCommand]
    private void HideAllLibraries()
    {
        _disabledLibraryIds = LibraryCards.Select(card => card.LibraryId).ToHashSet();
        foreach (var card in LibraryCards) card.SetEnabledFromOwner(false);
        UpdateLibraryCounts();
        _ = SaveLibraryVisibilityAsync();
    }

    public async Task MoveLibraryAsync(int libraryId, int offset)
    {
        var oldIndex = LibraryCards.ToList().FindIndex(card => card.LibraryId == libraryId);
        var newIndex = Math.Clamp(oldIndex + offset, 0, LibraryCards.Count - 1);
        await MoveLibraryToAsync(libraryId, newIndex);
    }

    public async Task MoveLibraryToAsync(int libraryId, int newIndex)
    {
        var oldIndex = LibraryCards.ToList().FindIndex(card => card.LibraryId == libraryId);
        newIndex = Math.Clamp(newIndex, 0, LibraryCards.Count - 1);
        if (oldIndex < 0 || oldIndex == newIndex) return;

        LibraryCards.Move(oldIndex, newIndex);
        try
        {
            await _settingsApi.PutSettingAsync("library_order",
                System.Text.Json.JsonSerializer.Serialize(LibraryCards.Select(card => card.LibraryId)));
            ShowStatus("Library order saved");
        }
        catch (Exception ex)
        {
            LibraryCards.Move(newIndex, oldIndex);
            ShowError($"Failed to update library order: {ex.Message}");
        }
    }

    partial void OnAutoSkipRecapChanged(bool value)
    {
        if (!_suppressSave) _ = SaveContractProfileSettingAsync("playback.auto_skip_recap", value);
    }

    partial void OnAutoPlayNextPreviewChanged(bool value)
    {
        if (!_suppressSave) _ = SaveContractProfileSettingAsync("playback.auto_play_next_preview", value);
    }

    partial void OnAutoPlayNextChanged(bool value)
    {
        if (!_suppressSave) _ = SaveAutoPlayNextAsync(value);
    }

    private async Task SaveAutoPlayNextAsync(bool value)
    {
        try
        {
            await _settingsApi.SetContractSettingValueAsync("playback.auto_play_next", "profile", value);
            try { await _settingsApi.DeleteContractSettingValueAsync("playback.auto_play_next", "profile_device"); }
            catch { }
            ShowStatus("Auto-play preference saved");
        }
        catch (Exception ex) { ErrorMessage = $"Failed to save auto-play preference: {ex.Message}"; }
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
        await SaveContractProfileSettingAsync("playback.preferred_quality", QualityPreference);
    }

    [RelayCommand]
    private async Task SaveMaxBitrateKbpsAsync()
    {
        if (_suppressSave) return;
        if (int.TryParse(MaxBitrateKbps, out var bitrate))
            await SaveContractProfileSettingAsync("playback.max_bitrate_kbps", bitrate);
        else
            await ClearContractProfileSettingAsync("playback.max_bitrate_kbps");
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
        await SaveContractProfileSettingAsync(PlaybackAudioLanguageSettingKey,
            string.IsNullOrWhiteSpace(AudioLanguage) ? null : AudioLanguage);
    }

    [RelayCommand]
    private async Task SavePreferredMetadataLanguageAsync()
    {
        if (_suppressSave) return;
        await SaveContractProfileSettingAsync("catalog.metadata_language",
            string.IsNullOrWhiteSpace(PreferredMetadataLanguage) ? null : PreferredMetadataLanguage);
    }

    public async Task SetMetadataLanguageOverrideAsync(string sourceLanguage, string? targetLanguage)
    {
        sourceLanguage = sourceLanguage.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(sourceLanguage) || sourceLanguage == "original") return;
        var context = _settingsApi.CaptureContext();
        await _metadataLanguageOverrideGuard.WaitAsync();
        try
        {
            if (!_settingsApi.IsCurrentContext(context)) return;
            // Serialize drafts so a later rejection cannot restore a draft
            // that an earlier failed request never persisted.
            var previous = new Dictionary<string, string>(_metadataLanguageOverrides, StringComparer.OrdinalIgnoreCase);
            var draft = new Dictionary<string, string>(previous, StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(targetLanguage)) draft.Remove(sourceLanguage);
            else draft[sourceLanguage] = targetLanguage.Trim().ToLowerInvariant();
            draft = draft.OrderBy(pair => MediaLanguageCatalog.Label(pair.Key), StringComparer.CurrentCultureIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            _metadataLanguageOverrides = draft;
            OnPropertyChanged(nameof(MetadataLanguageOverrides));
            var saved = await SaveContractProfileSettingAsync("catalog.metadata_language_overrides", draft);
            if (!saved && _settingsApi.IsCurrentContext(context) && ReferenceEquals(_metadataLanguageOverrides, draft))
            {
                _metadataLanguageOverrides = previous;
                OnPropertyChanged(nameof(MetadataLanguageOverrides));
            }
        }
        finally { _metadataLanguageOverrideGuard.Release(); }
    }

    private async Task<bool> SaveContractProfileSettingAsync(string key, object? value)
    {
        var context = _settingsApi.CaptureContext();
        var saved = false;
        try
        {
            await _settingsApi.SetContractSettingValueAsync(key, "profile", value);
            saved = true;
            if (!_settingsApi.IsCurrentContext(context)) return true;
            await ClearSupportedDeviceOverrideAsync(key);
            if (!_settingsApi.IsCurrentContext(context)) return true;
            if (SeekPreferences.Keys.Contains(key))
                await App.Services.GetRequiredService<PlayerService>().RefreshSeekPreferencesAsync();
            ShowStatus("Setting saved");
        }
        catch (Exception ex)
        {
            if (_settingsApi.IsCurrentContext(context)) ShowError(saved
                ? $"Profile setting saved, but the device override could not be cleared: {ex.Message}"
                : $"Failed to save setting: {ex.Message}");
        }
        // A secondary cleanup failure cannot undo a committed profile value.
        return saved;
    }

    private async Task ClearContractProfileSettingAsync(string key)
    {
        var context = _settingsApi.CaptureContext();
        try
        {
            await _settingsApi.DeleteContractSettingValueAsync(key, "profile");
            if (!_settingsApi.IsCurrentContext(context)) return;
            await ClearSupportedDeviceOverrideAsync(key);
            if (!_settingsApi.IsCurrentContext(context)) return;
            ShowStatus("Setting reset");
        }
        catch (Exception ex)
        {
            if (_settingsApi.IsCurrentContext(context)) ShowError($"Failed to reset setting: {ex.Message}");
        }
    }

    private static string ReadString(
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key, string fallback)
    {
        if (!values.TryGetValue(key, out var entry) || entry.Value.ValueKind != JsonValueKind.String)
            return fallback;
        return entry.Value.GetString() ?? fallback;
    }

    private async Task ClearSupportedDeviceOverrideAsync(string key)
    {
        if (!SeekPreferences.SupportsDeviceOverride(key)) return;
        try { await _settingsApi.DeleteContractSettingValueAsync(key, "profile_device"); }
        catch (ApiException ex) when (ex.StatusCode == 404) { }
    }

    private static string ReadNullableString(
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key)
        => values.TryGetValue(key, out var entry) && entry.Value.ValueKind == JsonValueKind.String
            ? entry.Value.GetString() ?? ""
            : "";

    private static string ReadNullableNumber(
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key)
        => values.TryGetValue(key, out var entry) && entry.Value.ValueKind == JsonValueKind.Number
            ? entry.Value.GetRawText()
            : "";

    private static bool ReadBool(
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key, bool fallback)
        => values.TryGetValue(key, out var entry) && entry.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? entry.Value.GetBoolean()
            : fallback;

    private static Dictionary<string, string> ReadStringMap(
        IReadOnlyDictionary<string, ContractEffectiveSettingEntry> values, string key)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue(key, out var entry) || entry.Value.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in entry.Value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var value = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(property.Name) && !string.IsNullOrWhiteSpace(value))
                result[property.Name.Trim().ToLowerInvariant()] = value.Trim().ToLowerInvariant();
        }
        return result;
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

    public async Task SetDateFormatAsync(string value)
    {
        DateFormat = NormalizeDateFormat(value);
        await SaveDateTimeFormatAsync("ui.date_format", DateFormat, date: true);
    }

    public async Task SetTimeFormatAsync(string value)
    {
        TimeFormat = NormalizeTimeFormat(value);
        await SaveDateTimeFormatAsync("ui.time_format", TimeFormat, date: false);
    }

    private async Task SaveDateTimeFormatAsync(string key, string value, bool date)
    {
        var local = _settingsService.Load();
        if (date) local.UiDateFormat = value; else local.UiTimeFormat = value;
        _settingsService.Save(local);
        try
        {
            await _settingsApi.PutSettingAsync(key, value);
            ShowStatus("Date and time preference saved");
        }
        catch (Exception ex) { ErrorMessage = $"Failed to save date and time preference: {ex.Message}"; }
    }

    private static string NormalizeDateFormat(string? value)
        => value is "DD/MM/YYYY" or "MM/DD/YYYY" or "YYYY-MM-DD" ? value : "auto";

    private static string NormalizeTimeFormat(string? value)
        => value is "12h" or "24h" ? value : "auto";

    [RelayCommand]
    private async Task ResetNextUpModeAsync()
    {
        try
        {
            await _settingsApi.DeleteSettingAsync("next_up_mode");
            _suppressSave = true;
            NextUpMode = "combined";
            _suppressSave = false;
            ShowStatus("Next up preference reset");
        }
        catch (Exception ex) { ErrorMessage = $"Failed to reset next up preference: {ex.Message}"; }
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
            var connectionConfig = ProviderConnectionConfig.Build(card.ConnectionSchemas, card.ConnectionConfigDraft);
            card.Connection = await _watchProvidersApi.ConnectApiKeyAsync(card.ProviderKey, apiKey, connectionConfig);
            card.ApiKey = "";
            card.ConnectionConfigDraft.Clear();
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
    private string _subFontSize = "large";

    [ObservableProperty]
    private string _subFontColor = "#ffffff";
    [ObservableProperty] private int _subTextOpacity = 100;

    [ObservableProperty]
    private bool _subOutlineEnabled = false;

    [ObservableProperty]
    private string _subOutlineColor = "#000000";

    [ObservableProperty]
    private string _subBackgroundStyle = "shadow";

    /// <summary>Background opacity, 0-100 integer scale (matches webui).</summary>
    [ObservableProperty]
    private int _subBackgroundOpacity = 75;

    [ObservableProperty]
    private string _subBackgroundColor = "#000000";

    [ObservableProperty]
    private string _subPosition = "bottom";

    [ObservableProperty]
    private bool _hasSubtitleAppearanceDeviceOverride;

    private Core.Models.Settings.SubtitleAppearance _savedSubtitleAppearance = new();

    private async Task LoadSubtitleAppearanceAsync()
    {
        try
        {
            var response = await _settingsApi.GetContractEffectiveSettingsAsync(["playback.subtitle_appearance"]);
            var entry = response.Settings.FirstOrDefault(setting => setting.Key == "playback.subtitle_appearance");
            HasSubtitleAppearanceDeviceOverride = string.Equals(entry?.Scope, "profile_device", StringComparison.OrdinalIgnoreCase);
            ApplySubtitleAppearanceDraft(Core.Models.Settings.SubtitleAppearance.Parse(entry?.Value.GetRawText()), remember: true);
        }
        catch
        {
            HasSubtitleAppearanceDeviceOverride = false;
            ApplySubtitleAppearanceDraft(new Core.Models.Settings.SubtitleAppearance(), remember: true);
        }
    }

    private Core.Models.Settings.SubtitleAppearance BuildSubtitleAppearanceDraft() => new()
    {
        FontFamily = SubFontFamily,
        FontSize = SubFontSize,
        FontColor = SubFontColor,
        TextOpacity = SubTextOpacity,
        TextOutline = SubOutlineEnabled,
        TextOutlineColor = SubOutlineColor,
        BackgroundStyle = SubBackgroundStyle,
        BackgroundOpacity = SubBackgroundOpacity,
        BackgroundColor = SubBackgroundColor,
        Position = SubPosition,
    };

    private void ApplySubtitleAppearanceDraft(Core.Models.Settings.SubtitleAppearance appearance, bool remember)
    {
        SubFontFamily = appearance.FontFamily;
        SubFontSize = appearance.FontSize;
        SubFontColor = appearance.FontColor;
        SubTextOpacity = appearance.TextOpacity;
        SubOutlineEnabled = appearance.TextOutline;
        SubOutlineColor = appearance.TextOutlineColor;
        SubBackgroundStyle = appearance.BackgroundStyle;
        SubBackgroundOpacity = appearance.BackgroundOpacity;
        SubBackgroundColor = appearance.BackgroundColor;
        SubPosition = appearance.Position;
        if (remember) _savedSubtitleAppearance = appearance.Clone();
    }

    [RelayCommand]
    private async Task SaveSubtitleAppearanceAsync()
    {
        try
        {
            var appearance = BuildSubtitleAppearanceDraft();
            using (var document = JsonDocument.Parse(appearance.ToJson()))
            {
                await _settingsApi.SetContractSettingValueAsync("playback.subtitle_appearance", "profile_device",
                    document.RootElement.Clone());
            }
            _savedSubtitleAppearance = appearance.Clone();
            HasSubtitleAppearanceDeviceOverride = true;

            // B55 follow-up: push the saved values to mpv so they apply to
            // the currently playing session AND the next one (PlayerService
            // caches and replays on init).
            try
            {
                var player = App.Services.GetRequiredService<PlayerService>();
                player.ApplySubtitleAppearance(appearance);
            }
            catch { /* mpv not initialized yet — will be applied on first play */ }

            ShowStatus("Subtitle appearance saved");
        }
        catch (Exception ex) { ErrorMessage = $"Failed to save subtitle appearance: {ex.Message}"; }
    }

    [RelayCommand]
    private void DiscardSubtitleAppearance()
    {
        ApplySubtitleAppearanceDraft(_savedSubtitleAppearance, remember: false);
    }

    [RelayCommand]
    private async Task ResetSubtitleAppearanceAsync()
    {
        try
        {
            await _settingsApi.DeleteContractSettingValueAsync("playback.subtitle_appearance", "profile_device");
            await LoadSubtitleAppearanceAsync();
            try { App.Services.GetRequiredService<PlayerService>().ApplySubtitleAppearance(_savedSubtitleAppearance); } catch { }
            ShowStatus("Subtitle appearance reset");
        }
        catch (Exception ex) { ErrorMessage = $"Failed to reset subtitle appearance: {ex.Message}"; }
    }

    // ===== Home Screen Sections =====

    public ObservableCollection<SettingsSectionEntry> HomeSections { get; } = [];
    private readonly HashSet<string> _removedSystemSectionIds = new(StringComparer.Ordinal);
    private List<RawSectionOverride> _rawHomeOverrides = [];

    [ObservableProperty]
    private string _selectedScope = "home";

    [ObservableProperty]
    private bool _isLoadingHomeSections;

    [ObservableProperty]
    private bool _canEditHomeSections;
    private readonly Dictionary<string, string> _homeOverrideIds = [];
    private ApiRequestContext? _homeSectionsContext;
    private long _homeLoadGeneration;
    private (string Scope, string? LibraryId) _loadedHomeScope;

    [ObservableProperty]
    private string? _homeSectionsEditStateMessage = "Loading saved section state before section changes are enabled.";

    [RelayCommand]
    private async Task LoadHomeSectionsAsync()
    {
        var generation = ++_homeLoadGeneration;
        var context = _settingsApi.CaptureContext();
        var requestedScope = GetSectionScope();
        if (_homeSectionsContext != context) _homeOverrideIds.Clear();
        IsLoadingHomeSections = true;
        CanEditHomeSections = false;
        HomeSectionsEditStateMessage = "Loading saved section state before section changes are enabled.";
        HomeSections.Clear();
        try
        {
            var (scope, libraryId) = GetSectionScope();
            var settingsTask = _settingsApi.GetProfileSectionSettingsAsync(scope, libraryId);
            var overridesTask = _settingsApi.GetProfileSectionsAsync(scope, libraryId);
            await Task.WhenAll(settingsTask, overridesTask);
            if (generation != _homeLoadGeneration || !_settingsApi.IsCurrentContext(context) || GetSectionScope() != requestedScope) return;
            _homeSectionsContext = context; _loadedHomeScope = requestedScope;
            var response = settingsTask.Result;
            CanEditHomeSections = true;
            HomeSectionsEditStateMessage = null;
            HomeSections.Clear();
            foreach (var section in response.Sections.OrderBy(s => s.Position))
            {
                HomeSections.Add(section);
            }
            _removedSystemSectionIds.Clear();
            _rawHomeOverrides = overridesTask.Result.Overrides;
            foreach (var entry in overridesTask.Result.Overrides.Where(o => o.Removed == true && !string.IsNullOrWhiteSpace(o.SectionId)))
                _removedSystemSectionIds.Add(entry.SectionId!);
        }
        catch (Exception ex)
        {
            if (generation != _homeLoadGeneration || !_settingsApi.IsCurrentContext(context)) return;
            ErrorMessage = $"Failed to load home sections: {ex.Message}";
            CanEditHomeSections = false;
            HomeSectionsEditStateMessage = "Saved section state failed to load. Editing is disabled.";
        }
        finally
        {
            if (generation == _homeLoadGeneration) IsLoadingHomeSections = false;
        }
    }

    [RelayCommand]
    private async Task SaveHomeSectionsAsync(string? changedSectionId)
    {
        if (!CanEditHomeSections || _homeSectionsContext == null || !_settingsApi.IsCurrentContext(_homeSectionsContext.Value) || GetSectionScope() != _loadedHomeScope)
        { ErrorMessage = "Reload this layout before saving."; return; }
        ErrorMessage = null;
        try
        {
            // Match the WebUI's buildSectionOverrides contract: the payload is
            // rebuilt from the currently visible rows. Starting with every raw
            // override resurrected deleted custom sections on the next save.
            var overrides = HomeSectionWritePolicy.Build(HomeSections, _rawHomeOverrides, _removedSystemSectionIds, _homeOverrideIds, changedSectionId);

            var (scope, libraryId) = GetSectionScope();

            await _settingsApi.UpdateProfileSectionsAsync(new SaveOverridesRequest
            {
                Scope = scope,
                LibraryId = libraryId,
                Overrides = overrides,
            });
            if (string.Equals(scope, "home", StringComparison.OrdinalIgnoreCase))
            {
                WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                    MediaSurfaceChangeKind.HomeLayoutChanged,
                    string.Empty));
            }
            ShowStatus("Home sections saved");
        }
        catch (Exception ex)
        {
            var message = $"Failed to save home sections: {ex.Message}";
            // Restore the last server-confirmed layout after an optimistic
            // reorder/edit/delete fails, matching the WebUI rollback behavior.
            await LoadHomeSectionsAsync();
            ErrorMessage = message;
        }
    }

    [RelayCommand]
    private async Task ResetHomeSectionsAsync()
    {
        try
        {
            var (scope, libraryId) = GetSectionScope();
            await _settingsApi.ResetProfileSectionsAsync(scope, libraryId);
            if (string.Equals(scope, "home", StringComparison.OrdinalIgnoreCase))
            {
                WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                    MediaSurfaceChangeKind.HomeLayoutChanged,
                    string.Empty));
            }
            ShowStatus("Sections reset to default");
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
        if (!section.IsCustom) _removedSystemSectionIds.Add(section.Id);
        HomeSections.Remove(section);
    }

    public void AddHomeSection(SettingsSectionEntry section) => HomeSections.Add(section);

    private static SectionOverride ToWireOverride(RawSectionOverride value) => new()
    {
        Id = string.IsNullOrWhiteSpace(value.Id) ? null : value.Id,
        SectionId = string.IsNullOrWhiteSpace(value.SectionId) ? null : value.SectionId,
        Position = value.Position,
        Hidden = value.Hidden,
        Removed = value.Removed,
        SectionType = string.IsNullOrWhiteSpace(value.SectionType) ? null : value.SectionType,
        Title = string.IsNullOrWhiteSpace(value.Title) ? null : value.Title,
        Featured = value.Featured,
        ItemLimit = value.ItemLimit,
        Config = ParseConfig(value.Config),
        IsUserAdded = value.IsUserAdded,
        UserSectionType = string.IsNullOrWhiteSpace(value.UserSectionType) ? null : value.UserSectionType,
        UserConfig = ParseConfig(value.UserConfig),
        UserTitle = string.IsNullOrWhiteSpace(value.UserTitle) ? null : value.UserTitle,
    };

    private static Dictionary<string, object>? ParseConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json); }
        catch { return null; }
    }

    private (string Scope, string? LibraryId) GetSectionScope()
    {
        if (SelectedScope.StartsWith("library:", StringComparison.OrdinalIgnoreCase))
            return ("library", SelectedScope[8..]);
        return ("home", null);
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
    private ApiRequestContext? _importContext;
    private Dictionary<string, string> _importProfileNames = [];
    public bool CanImportForOthers => HistoryImportScope.CanTargetOthers(_authService.CurrentUser, _authService.SelectedProfile);
    public string ImportTargetName(HistoryImportRun run) => _importProfileNames.GetValueOrDefault(run.ProfileId)
        ?? HistoryImportScope.Label(run.ProfileId, ImportProfiles);

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

            if (!ExternalBrowserUrlPolicy.TryGetSafeUri(pinResponse.AuthUrl, out var approvalUri))
            {
                PlexAuthStatus = "";
                PlexAuthError = "The server returned an invalid Plex approval URL.";
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(approvalUri.AbsoluteUri)
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                PlexAuthStatus = "";
                PlexAuthError = $"Could not open the Plex approval page: {ex.Message}";
                return;
            }

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
        var context = _historyImportApi.CaptureContext();
        var actor = _authService.SelectedProfile ?? new Profile { Id = _authService.SelectedProfileId ?? "" };
        var target = HistoryImportScope.Target(_authService.CurrentUser, actor, ImportProfileId);
        if (string.IsNullOrEmpty(target)) { ErrorMessage = "Select an acting profile before importing."; return; }
        IsImporting = true;
        ErrorMessage = null;
        try
        {
            var request = new CreateHistoryImportRunRequest
            {
                ProfileId = target,
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

            var run = await HistoryImportStart.CreateAsync(_historyImportApi, request, consumedSession =>
            {
                if (EmbyConnectSessionId != consumedSession) return;
                EmbyConnectSessionId = null;
                SelectedEmbyConnectServer = null;
                EmbyConnectServers.Clear();
                EmbyConnectPassword = "";
            });

            if (!_historyImportApi.IsCurrentContext(context)) return;
            // Display the new run immediately and put it at the top of the history list.
            SelectedRunId = run.Id;
            DisplayRun = run;
            ImportRuns.Insert(0, run);

            ShowStatus("Import started");
        }
        catch (Exception ex)
        {
            if (_historyImportApi.IsCurrentContext(context)) ErrorMessage = $"Failed to start import: {ex.Message}";
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
        var context = _historyImportApi.CaptureContext();
        if (_importContext != context)
        {
            _importContext = context;
            ImportProfiles.Clear(); ImportRuns.Clear(); DisplayRun = null; SelectedRunId = null;
            ImportProfileId = _authService.SelectedProfileId ?? "";
            SelectedEmbySavedSource = null; SelectedPlexSavedSource = null;
            EmbyConnectSessionId = null; EmbyConnectServers.Clear();
            PlexSessionId = null; PlexOAuthServers.Clear();
        }
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
                if (!_historyImportApi.IsCurrentContext(context)) return;
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
                if (_historyImportApi.IsCurrentContext(context)) ErrorMessage = $"Failed to load saved import sources: {ex.Message}";
            }

            // Fetch the user's profiles for the import-target dropdown.
            try
            {
                var profilesResp = await _authApi.GetProfilesAsync();
                if (!_historyImportApi.IsCurrentContext(context)) return;
                _importProfileNames = profilesResp.Profiles.ToDictionary(p => p.Id, p => p.Name);
                ImportProfiles.Clear();
                foreach (var p in profilesResp.Profiles.Where(p => CanImportForOthers || p.Id == _authService.SelectedProfileId))
                    ImportProfiles.Add(p);
                if (!ImportProfiles.Any(p => p.Id == ImportProfileId)) ImportProfileId = _authService.SelectedProfileId ?? "";
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
        var context = _historyImportApi.CaptureContext();
        IsLoadingImportRuns = true;
        try
        {
            var runs = await _historyImportApi.GetImportRunsAsync();
            if (!_historyImportApi.IsCurrentContext(context)) return;
            ImportRuns.Clear();
            foreach (var run in runs.OrderByDescending(r => r.CreatedAt))
                ImportRuns.Add(run);
            // Default display run = most recent if nothing selected.
            if (DisplayRun == null) DisplayRun = ImportRuns.FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (_historyImportApi.IsCurrentContext(context)) ErrorMessage = $"Failed to load import runs: {ex.Message}";
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
        if (_importContext == null || !_historyImportApi.IsCurrentContext(_importContext.Value) ||
            (!CanImportForOthers && run.ProfileId != _authService.SelectedProfileId)) return;
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
