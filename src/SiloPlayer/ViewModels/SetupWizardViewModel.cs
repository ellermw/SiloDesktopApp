using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly AuthApi _authApi;
    private readonly AdminApi _adminApi;
    private readonly AuthService _authService;

    public SetupWizardViewModel(AuthApi authApi, AdminApi adminApi, AuthService authService)
    {
        _authApi = authApi;
        _adminApi = adminApi;
        _authService = authService;
    }

    // ===== Step tracking =====
    // Current GitHub WebUI order: account, profile, server, integrations,
    // downloads, recommendations, library, nodes/finish.

    [ObservableProperty]
    private int _currentStep = 1;

    public int TotalSteps => 8;

    /// <summary>True after the user has explicitly skipped the library step. Persisted in-memory only.</summary>
    [ObservableProperty]
    private bool _libraryStepSkipped;

    /// <summary>True after the user has saved the server step at least once.</summary>
    [ObservableProperty]
    private bool _serverStepDone;

    [ObservableProperty] private bool _integrationsStepDone;
    [ObservableProperty] private bool _downloadsStepDone;
    [ObservableProperty] private bool _recommendationsStepDone;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _serverUrl = "";

    // ===== Step 1: Account =====

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _confirmPassword = "";

    // ===== Step 2: Profile =====

    [ObservableProperty]
    private string _profileName = "";

    // ===== Step 3: Library =====

    [ObservableProperty]
    private string _libraryName = "";

    [ObservableProperty]
    private string _libraryType = "movies";

    public ObservableCollection<string> LibraryPaths { get; } = [];

    [ObservableProperty]
    private string _newLibraryPath = "";

    [ObservableProperty]
    private bool _scanAfterCreate = true;

    [ObservableProperty] private bool _libraryEnabled = true;
    [ObservableProperty] private string _libraryMetadataLanguage = "en";
    [ObservableProperty] private bool _libraryAutoTranslateMetadata;
    [ObservableProperty] private bool _libraryChapterThumbnailsEnabled;
    [ObservableProperty] private bool _libraryChapterThumbnailsSupported = true;
    [ObservableProperty] private bool _libraryIntroDetectionEnabled;
    [ObservableProperty] private bool _libraryTrailer = true;
    [ObservableProperty] private bool _libraryTeaser = true;
    [ObservableProperty] private bool _libraryFeaturette = true;
    [ObservableProperty] private bool _libraryClip = true;
    [ObservableProperty] private bool _libraryBehindTheScenes = true;
    [ObservableProperty] private bool _libraryBloopers = true;
    [ObservableProperty] private bool _libraryOtherExtras = true;
    public ObservableCollection<Library> AddedLibraries { get; } = [];
    public ObservableCollection<SetupLibraryProviderLevel> LibraryProviderLevels { get; } = [];
    [ObservableProperty] private bool _libraryProvidersLoading;
    [ObservableProperty] private bool _libraryProviderChainDirty;
    private bool _librariesLoaded;

    // ===== Step 4: Server settings =====

    [ObservableProperty]
    private string _redisUrl = "";

    [ObservableProperty]
    private string _ffmpegPath = "";

    [ObservableProperty]
    private string _transcodeDir = "";

    // B48: Default to "auto" (recommended) — matches WebUI default.
    [ObservableProperty]
    private string _hardwareAccel = "auto";

    [ObservableProperty]
    private bool _transcodingEnabled = true;

    [ObservableProperty]
    private string _jellyfinUrl = "";

    [ObservableProperty]
    private string _jellyfinName = "";

    [ObservableProperty] private bool _jellyfinEnabled;
    [ObservableProperty] private string _jellyfinWebVersion = "";
    [ObservableProperty] private string _jellyfinWebInstallDir = "";
    private bool _serverSettingsLoaded;

    // ===== Step 4: Storage (S3) =====
    // Mirrors upstream web/src/pages/setup-wizard/steps/ServerStorageStep.tsx.
    // Two independent buckets — public (artwork, chapter thumbnails, subtitle
    // files served to clients) and private (imports/exports/internal artifacts).
    // All fields optional; users who skip can configure later via Admin Settings.

    [ObservableProperty] private bool _configureStorage;

    // Public bucket — serves client-facing assets
    [ObservableProperty] private string _s3PublicEndpoint = "";
    [ObservableProperty] private string _s3PublicBucket = "";
    [ObservableProperty] private string _s3PublicKeyPrefix = "";
    [ObservableProperty] private string _s3PublicAccessKey = "";
    [ObservableProperty] private string _s3PublicSecretKey = "";
    [ObservableProperty] private string _s3PublicUrlAuth = "presigned"; // presigned | public | cloudflare_token
    [ObservableProperty] private string _s3PublicReadEndpoint = "";

    // Private bucket — non-public internal storage
    [ObservableProperty] private string _s3PrivateEndpoint = "";
    [ObservableProperty] private string _s3PrivateBucket = "";
    [ObservableProperty] private string _s3PrivateKeyPrefix = "";
    [ObservableProperty] private string _s3PrivateAccessKey = "";
    [ObservableProperty] private string _s3PrivateSecretKey = "";

    /// <summary>Cache artwork in public asset storage instead of proxying external URLs.</summary>
    [ObservableProperty] private bool _cacheImages;

    /// <summary>True when the URL auth method needs a separate read endpoint
    /// (everything except presigned). Drives visibility of the Read Endpoint
    /// input in the S3 Public Storage section.</summary>
    public bool IsPublicReadEndpointVisible =>
        !string.IsNullOrEmpty(S3PublicUrlAuth) &&
        !S3PublicUrlAuth.Equals("presigned", StringComparison.OrdinalIgnoreCase);

    partial void OnS3PublicUrlAuthChanged(string value) =>
        OnPropertyChanged(nameof(IsPublicReadEndpointVisible));

    // ===== Step 4: Integrations =====

    public ObservableCollection<SetupSubtitleProviderItem> SubtitleProviders { get; } = [];
    private bool _integrationsLoaded;

    // ===== Step 5: Downloads =====

    [ObservableProperty] private bool _downloadsEnabled;
    [ObservableProperty] private string _downloadServerBandwidthMbps = "0";
    [ObservableProperty] private string _downloadUserBandwidthMbps = "0";
    [ObservableProperty] private string _downloadMaxConcurrentPerUser = "0";
    private bool _downloadsLoaded;

    // ===== Step 6: Recommendations =====

    [ObservableProperty] private bool _recommendationsEnabled;
    [ObservableProperty] private string _recommendationsBaseUrl = "";
    [ObservableProperty] private string _recommendationsModel = "";
    [ObservableProperty] private string _recommendationsAuthToken = "";
    private bool _recommendationsLoaded;

    // ===== Step 8: Nodes / finish =====

    public ObservableCollection<StreamNode> AddedNodes { get; } = [];
    [ObservableProperty] private string _nodeName = "";
    [ObservableProperty] private string _nodeUrl = "";
    [ObservableProperty] private string _nodeType = "proxy";
    [ObservableProperty] private bool _showNodeForm;

    // ===== Navigation helpers =====

    public bool CanGoBack => CurrentStep > 1;
    public bool CanGoNext => CurrentStep < TotalSteps;

    /// <summary>
    /// Event raised when setup is complete. The caller should navigate to the home page.
    /// </summary>
    public event Action<bool>? SetupCompleted;

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoNext));
    }

    /// <summary>
    /// B47: Derive the starting wizard step from current server state. If the
    /// admin account already exists, jump past Account; if profiles already
    /// exist, past Profile; etc. Mirrors WebUI <c>currentStep</c> derivation in
    /// SetupWizard.tsx.
    /// </summary>
    public async Task DetermineStartingStepAsync()
    {
        try
        {
            // 1. Account complete? Try fetching current user via /auth/me. If it
            //    succeeds we're already authenticated → Account is done.
            bool accountComplete = _authService.CurrentUser != null;
            if (!accountComplete)
            {
                CurrentStep = 1;
                return;
            }

            // 2. Profile complete?
            bool profileComplete = false;
            try
            {
                var profilesResponse = await _authApi.GetProfilesAsync();
                profileComplete = profilesResponse.Profiles.Count > 0;
            }
            catch
            {
                // If we can't fetch profiles, fall back to step 2
            }
            if (!profileComplete)
            {
                CurrentStep = 2;
                return;
            }

            // Remaining completion flags mirror the WebUI's wizard-session
            // progress. Library discovery happens only after those steps.
            if (!ServerStepDone)
            {
                CurrentStep = 3;
                return;
            }
            if (!IntegrationsStepDone) { CurrentStep = 4; return; }
            if (!DownloadsStepDone) { CurrentStep = 5; return; }
            if (!RecommendationsStepDone) { CurrentStep = 6; return; }

            var libraryComplete = false;
            try
            {
                libraryComplete = (await _adminApi.GetAdminLibrariesAsync()).Count > 0;
            }
            catch { }
            CurrentStep = libraryComplete || LibraryStepSkipped ? 8 : 7;
        }
        catch
        {
            // On any unexpected error, default to step 1
            CurrentStep = 1;
        }
    }

    [RelayCommand]
    private void AddLibraryPath()
    {
        if (!string.IsNullOrWhiteSpace(NewLibraryPath))
        {
            LibraryPaths.Add(NewLibraryPath.Trim());
            NewLibraryPath = "";
        }
    }

    [RelayCommand]
    private void RemoveLibraryPath(string path)
    {
        LibraryPaths.Remove(path);
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        ErrorMessage = null;

        // Validate and submit current step
        switch (CurrentStep)
        {
            case 1:
                if (!await SubmitAccountStepAsync()) return;
                break;
            case 2:
                if (!await SubmitProfileStepAsync()) return;
                break;
            case 3:
                if (!await SubmitServerStepAsync()) return;
                break;
            case 4:
                IntegrationsStepDone = true;
                break;
            case 5:
                if (!await SubmitDownloadsStepAsync()) return;
                break;
            case 6:
                if (!await SubmitRecommendationsStepAsync()) return;
                break;
            case 7:
                if (AddedLibraries.Count == 0)
                {
                    ErrorMessage = "Add at least one library or choose Skip.";
                    return;
                }
                break;
        }

        if (CurrentStep < TotalSteps)
            CurrentStep++;
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 1)
        {
            ErrorMessage = null;
            CurrentStep--;
        }
    }

    [RelayCommand]
    private async Task AddLibraryAsync()
    {
        ErrorMessage = null;
        await SubmitLibraryStepAsync();
    }

    [RelayCommand]
    private void SkipLibrary()
    {
        LibraryStepSkipped = true;
        ErrorMessage = null;
        if (CurrentStep == 7)
            CurrentStep = 8;
    }

    [RelayCommand]
    private void Finish()
    {
        ErrorMessage = null;
        SetupCompleted?.Invoke(false);
    }

    [RelayCommand]
    private void GoToAdmin() => SetupCompleted?.Invoke(true);

    [RelayCommand]
    private void ToggleNodeForm() => ShowNodeForm = !ShowNodeForm;

    [RelayCommand]
    private async Task AddNodeAsync()
    {
        if (string.IsNullOrWhiteSpace(NodeName) || string.IsNullOrWhiteSpace(NodeUrl))
        {
            ErrorMessage = "Node name and URL are required.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var node = await _adminApi.CreateNodeAsync(new CreateNodeRequest
            {
                Name = NodeName.Trim(),
                Url = NodeUrl.Trim(),
                Type = NodeType,
            });
            AddedNodes.Add(node);
            NodeName = "";
            NodeUrl = "";
            ShowNodeForm = false;
        }
        catch (Exception ex) { ErrorMessage = $"Failed to add node: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    // ===== Step submissions =====

    private async Task<bool> SubmitAccountStepAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Username is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Email is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Password is required.";
            return false;
        }
        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "Passwords do not match.";
            return false;
        }

        IsLoading = true;
        try
        {
            var request = new SetupRequest
            {
                Username = Username.Trim(),
                Email = Email.Trim(),
                Password = Password
            };

            var serverUrl = _authService.ConfiguredServerUrl;
            var response = await _authApi.SetupAsync(serverUrl, request);

            var authGeneration = _authService.SetTokens(
                response.AccessToken,
                response.RefreshToken,
                response.ExpiresIn,
                expectedServerUrl: serverUrl);
            if (!_authService.SetCurrentUser(response.User, authGeneration))
                throw new InvalidOperationException("The authentication session changed before setup completed.");

            return true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Unable to connect to the server.";
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Setup failed: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<bool> SubmitProfileStepAsync()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            ErrorMessage = "Profile name is required.";
            return false;
        }

        IsLoading = true;
        try
        {
            var profile = await _authApi.CreateProfileAsync(ProfileName.Trim());
            _authService.SelectProfile(profile.Id, profile: profile);
            return true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create profile: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<bool> SubmitLibraryStepAsync()
    {
        if (string.IsNullOrWhiteSpace(LibraryName))
        {
            ErrorMessage = "Give this library a name.";
            return false;
        }
        if (LibraryPaths.Count == 0 || !LibraryPaths.Any(path => !string.IsNullOrWhiteSpace(path)))
        {
            ErrorMessage = "Add at least one folder to scan.";
            return false;
        }

        IsLoading = true;
        try
        {
            var trailerKinds = new List<string>();
            if (LibraryTrailer) trailerKinds.Add("trailer");
            if (LibraryTeaser) trailerKinds.Add("teaser");
            if (LibraryFeaturette) trailerKinds.Add("featurette");
            if (LibraryClip) trailerKinds.Add("clip");
            if (LibraryBehindTheScenes) trailerKinds.Add("behind_the_scenes");
            if (LibraryBloopers) trailerKinds.Add("bloopers");
            if (LibraryOtherExtras) trailerKinds.Add("other");
            var body = new CreateLibraryRequest
            {
                Name = LibraryName.Trim(),
                Type = LibraryType,
                Paths = LibraryPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim()).Distinct().ToList(),
                Enabled = LibraryEnabled,
                MetadataLanguage = LibraryMetadataLanguage,
                AutoTranslateMetadata = LibraryAutoTranslateMetadata,
                ChapterThumbnailsEnabled = LibraryChapterThumbnailsSupported && LibraryChapterThumbnailsEnabled,
                IntroDetectionEnabled = LibraryIntroDetectionEnabled,
                TrailerKinds = trailerKinds,
            };

            var library = await _adminApi.CreateLibraryAsync(body);
            if (LibraryProviderChainDirty && LibraryProviderLevels.Count > 0)
            {
                await _adminApi.UpdateLibraryProvidersAsync(library.Id, new SetLibraryChainRequest
                {
                    Levels = LibraryProviderLevels.ToDictionary(
                        level => level.Level,
                        level => level.Entries.Select((entry, priority) => new SetLibraryChainEntry
                        {
                            PluginInstallationId = entry.PluginInstallationId,
                            CapabilityId = entry.CapabilityId,
                            Priority = priority,
                            Enabled = entry.Enabled,
                        }).ToList()),
                });
            }
            AddedLibraries.Add(library);
            LibraryName = "";
            LibraryPaths.Clear();
            NewLibraryPath = "";
            LibraryStepSkipped = false;
            LibraryProviderChainDirty = false;
            return true;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create library: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<bool> SubmitServerStepAsync()
    {
        IsLoading = true;
        try
        {
            // Save each non-empty setting
            var settings = new Dictionary<string, string>();

            // Server uses dotted keys (redis.url, playback.ffmpeg_path, …) —
            // see internal/config/db_loader.go. Flat underscored keys are a
            // no-op and silently lose the setting.
            if (!string.IsNullOrWhiteSpace(RedisUrl))
                settings["redis.url"] = RedisUrl.Trim();
            if (!string.IsNullOrWhiteSpace(FfmpegPath))
                settings["playback.ffmpeg_path"] = FfmpegPath.Trim();
            if (!string.IsNullOrWhiteSpace(TranscodeDir))
                settings["playback.transcode_dir"] = TranscodeDir.Trim();
            if (!string.IsNullOrWhiteSpace(HardwareAccel))
                settings["playback.hw_accel"] = HardwareAccel.Trim();
            settings["playback.transcode_enabled"] = TranscodingEnabled.ToString().ToLowerInvariant();
            settings["jellyfin_compat.enabled"] = JellyfinEnabled.ToString().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(JellyfinUrl))
                settings["jellyfin_compat.public_url"] = JellyfinUrl.Trim();
            if (!string.IsNullOrWhiteSpace(JellyfinName))
                settings["jellyfin_compat.server_name"] = JellyfinName.Trim();
            if (!string.IsNullOrWhiteSpace(JellyfinWebVersion))
                settings["jellyfin_compat.web_version"] = JellyfinWebVersion.Trim();
            if (!string.IsNullOrWhiteSpace(JellyfinWebInstallDir))
                settings["jellyfin_compat.web_install_dir"] = JellyfinWebInstallDir.Trim();

            // Storage — only when the user expanded the optional section.
            if (ConfigureStorage)
            {
                if (!string.IsNullOrWhiteSpace(S3PublicEndpoint))   settings["s3.public_endpoint"]    = S3PublicEndpoint.Trim();
                if (!string.IsNullOrWhiteSpace(S3PublicBucket))     settings["s3.public_bucket"]      = S3PublicBucket.Trim();
                if (!string.IsNullOrWhiteSpace(S3PublicKeyPrefix))  settings["s3.public_key_prefix"]  = S3PublicKeyPrefix.Trim();
                if (!string.IsNullOrWhiteSpace(S3PublicAccessKey))  settings["s3.public_access_key"]  = S3PublicAccessKey.Trim();
                if (!string.IsNullOrWhiteSpace(S3PublicSecretKey))  settings["s3.public_secret_key"]  = S3PublicSecretKey.Trim();
                if (!string.IsNullOrWhiteSpace(S3PublicUrlAuth))    settings["s3.public_url_auth"]    = S3PublicUrlAuth.Trim();
                if (!string.IsNullOrWhiteSpace(S3PublicReadEndpoint)) settings["s3.public_read_endpoint"] = S3PublicReadEndpoint.Trim();

                if (!string.IsNullOrWhiteSpace(S3PrivateEndpoint))  settings["s3.private_endpoint"]   = S3PrivateEndpoint.Trim();
                if (!string.IsNullOrWhiteSpace(S3PrivateBucket))    settings["s3.private_bucket"]     = S3PrivateBucket.Trim();
                if (!string.IsNullOrWhiteSpace(S3PrivateKeyPrefix)) settings["s3.private_key_prefix"] = S3PrivateKeyPrefix.Trim();
                if (!string.IsNullOrWhiteSpace(S3PrivateAccessKey)) settings["s3.private_access_key"] = S3PrivateAccessKey.Trim();
                if (!string.IsNullOrWhiteSpace(S3PrivateSecretKey)) settings["s3.private_secret_key"] = S3PrivateSecretKey.Trim();

                settings["metadata.cache_images"] = CacheImages.ToString().ToLowerInvariant();
            }

            foreach (var (key, value) in settings)
            {
                await _adminApi.UpdateAdminSettingAsync(key, value);
            }

            // B47: Mark server step done so DetermineStartingStepAsync can skip past it.
            ServerStepDone = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save settings: {ex.Message}";
            return false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task PrepareStepAsync(int step)
    {
        if (step == 3 && !_serverSettingsLoaded)
        {
            IsLoading = true;
            try
            {
                var settings = await _adminApi.GetAdminSettingsAsync();
                RedisUrl = GetSetting(settings, "redis.url", "");
                FfmpegPath = GetSetting(settings, "playback.ffmpeg_path", "");
                TranscodeDir = GetSetting(settings, "playback.transcode_dir", "");
                HardwareAccel = GetSetting(settings, "playback.hw_accel", "auto");
                TranscodingEnabled = !settings.TryGetValue("playback.transcode_enabled", out var transcode) ||
                    !bool.TryParse(transcode, out var transcodeEnabled) || transcodeEnabled;
                JellyfinEnabled = GetBool(settings, "jellyfin_compat.enabled");
                JellyfinUrl = GetSetting(settings, "jellyfin_compat.public_url", "");
                JellyfinName = GetSetting(settings, "jellyfin_compat.server_name", "");
                JellyfinWebVersion = GetSetting(settings, "jellyfin_compat.web_version", "");
                JellyfinWebInstallDir = GetSetting(settings, "jellyfin_compat.web_install_dir", "");

                S3PublicEndpoint = GetSetting(settings, "s3.public_endpoint", "");
                S3PublicBucket = GetSetting(settings, "s3.public_bucket", "");
                S3PublicKeyPrefix = GetSetting(settings, "s3.public_key_prefix", "");
                S3PublicUrlAuth = GetSetting(settings, "s3.public_url_auth", "presigned");
                S3PublicReadEndpoint = GetSetting(settings, "s3.public_read_endpoint", "");
                S3PrivateEndpoint = GetSetting(settings, "s3.private_endpoint", "");
                S3PrivateBucket = GetSetting(settings, "s3.private_bucket", "");
                S3PrivateKeyPrefix = GetSetting(settings, "s3.private_key_prefix", "");
                CacheImages = GetBool(settings, "metadata.cache_images");
                ConfigureStorage = !string.IsNullOrWhiteSpace(S3PublicEndpoint) ||
                    !string.IsNullOrWhiteSpace(S3PublicBucket) ||
                    !string.IsNullOrWhiteSpace(S3PrivateEndpoint) ||
                    !string.IsNullOrWhiteSpace(S3PrivateBucket);
                _serverSettingsLoaded = true;
            }
            catch (Exception ex) { ErrorMessage = $"Failed to load server settings: {ex.Message}"; }
            finally { IsLoading = false; }
        }
        else if (step == 4 && !_integrationsLoaded)
        {
            IsLoading = true;
            try
            {
                var response = await _adminApi.GetSubtitleProvidersAsync();
                SubtitleProviders.Clear();
                foreach (var provider in response.Providers.OrderBy(p => ProviderOrder(p.ProviderName)))
                {
                    SubtitleProviders.Add(new SetupSubtitleProviderItem(provider));
                }
                _integrationsLoaded = true;
            }
            catch (Exception ex) { ErrorMessage = $"Failed to load subtitle providers: {ex.Message}"; }
            finally { IsLoading = false; }
        }
        else if ((step == 5 && !_downloadsLoaded) || (step == 6 && !_recommendationsLoaded))
        {
            IsLoading = true;
            try
            {
                var settings = await _adminApi.GetAdminSettingsAsync();
                if (step == 5)
                {
                    DownloadsEnabled = GetBool(settings, "download.enabled");
                    DownloadServerBandwidthMbps = GetSetting(settings, "download.server_bandwidth_mbps", "0");
                    DownloadUserBandwidthMbps = GetSetting(settings, "download.user_bandwidth_mbps", "0");
                    DownloadMaxConcurrentPerUser = GetSetting(settings, "download.max_concurrent_per_user", "0");
                    _downloadsLoaded = true;
                }
                else
                {
                    RecommendationsEnabled = GetBool(settings, "recommendations.enabled");
                    RecommendationsBaseUrl = GetSetting(settings, "recommendations.embedding_base_url", "");
                    RecommendationsModel = GetSetting(settings, "recommendations.embedding_model", "");
                    _recommendationsLoaded = true;
                }
            }
            catch (Exception ex) { ErrorMessage = $"Failed to load settings: {ex.Message}"; }
            finally { IsLoading = false; }
        }
        else if (step == 7 && !_librariesLoaded)
        {
            IsLoading = true;
            try
            {
                var libraries = await _adminApi.GetAdminLibrariesAsync();
                AddedLibraries.Clear();
                foreach (var library in libraries.OrderBy(library => library.SortOrder))
                    AddedLibraries.Add(library);
                LibraryChapterThumbnailsSupported = libraries.FirstOrDefault()?.ChapterThumbnailsSupported ?? true;
                _librariesLoaded = true;
                await LoadLibraryProviderDefaultsAsync();
            }
            catch (Exception ex) { ErrorMessage = $"Failed to load libraries: {ex.Message}"; }
            finally { IsLoading = false; }
        }
    }

    public async Task LoadLibraryProviderDefaultsAsync()
    {
        LibraryProvidersLoading = true;
        try
        {
            var response = await _adminApi.GetLibraryProviderDefaultsAsync(LibraryType);
            LibraryProviderLevels.Clear();
            foreach (var pair in response.Levels.OrderBy(pair => ContentLevelOrder(pair.Key)))
            {
                var level = new SetupLibraryProviderLevel(pair.Key);
                foreach (var entry in pair.Value.OrderBy(entry => entry.Priority))
                {
                    var item = new SetupLibraryProviderEntry(
                        pair.Key,
                        entry.PluginInstallationId,
                        entry.CapabilityId,
                        entry.ProviderSlug,
                        entry.Enabled);
                    item.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(SetupLibraryProviderEntry.Enabled))
                            LibraryProviderChainDirty = true;
                    };
                    level.Entries.Add(item);
                }
                LibraryProviderLevels.Add(level);
            }
            LibraryProviderChainDirty = false;
        }
        catch (Exception ex)
        {
            LibraryProviderLevels.Clear();
            ErrorMessage = $"Failed to load provider defaults: {ex.Message}";
        }
        finally { LibraryProvidersLoading = false; }
    }

    public void MoveLibraryProvider(SetupLibraryProviderEntry entry, int direction)
    {
        var level = LibraryProviderLevels.FirstOrDefault(candidate => candidate.Level == entry.Level);
        if (level == null) return;
        var index = level.Entries.IndexOf(entry);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= level.Entries.Count) return;
        level.Entries.Move(index, target);
        LibraryProviderChainDirty = true;
    }

    private static int ContentLevelOrder(string level) => level switch
    {
        "movie" => 0,
        "series" => 1,
        "season" => 2,
        "episode" => 3,
        "audiobook" => 4,
        "ebook" => 5,
        "manga" => 6,
        "podcast" => 7,
        "podcast_episode" => 8,
        _ => 20,
    };

    public async Task SaveSubtitleProviderAsync(SetupSubtitleProviderItem item)
    {
        item.IsBusy = true;
        item.StatusMessage = null;
        try
        {
            await _adminApi.UpdateSubtitleProviderAsync(item.ProviderName, new SubtitleProviderUpdateRequest
            {
                Enabled = item.Enabled,
                ApiKey = string.IsNullOrWhiteSpace(item.ApiKey) ? null : item.ApiKey,
                Username = string.IsNullOrWhiteSpace(item.Username) ? null : item.Username,
                Password = string.IsNullOrWhiteSpace(item.Password) ? null : item.Password,
            });
            item.HasCredential = item.HasCredential || !string.IsNullOrWhiteSpace(item.ApiKey) ||
                (!string.IsNullOrWhiteSpace(item.Username) && !string.IsNullOrWhiteSpace(item.Password));
            item.ApiKey = item.Username = item.Password = "";
            item.StatusMessage = "Saved";
        }
        catch (Exception ex) { item.StatusMessage = ex.Message; }
        finally { item.IsBusy = false; }
    }

    public async Task TestSubtitleProviderAsync(SetupSubtitleProviderItem item)
    {
        item.IsBusy = true;
        item.StatusMessage = null;
        try
        {
            var result = await _adminApi.TestSubtitleProviderAsync(item.ProviderName);
            item.StatusMessage = result.Success ? "Connected" : result.Error ?? "Failed";
        }
        catch (Exception ex) { item.StatusMessage = ex.Message; }
        finally { item.IsBusy = false; }
    }

    private async Task<bool> SubmitDownloadsStepAsync()
    {
        return await SaveSettingsAsync(new Dictionary<string, string>
        {
            ["download.enabled"] = DownloadsEnabled.ToString().ToLowerInvariant(),
            ["download.server_bandwidth_mbps"] = DownloadServerBandwidthMbps.Trim(),
            ["download.user_bandwidth_mbps"] = DownloadUserBandwidthMbps.Trim(),
            ["download.max_concurrent_per_user"] = DownloadMaxConcurrentPerUser.Trim(),
        }, () => DownloadsStepDone = true);
    }

    private async Task<bool> SubmitRecommendationsStepAsync()
    {
        var settings = new Dictionary<string, string>
        {
            ["recommendations.enabled"] = RecommendationsEnabled.ToString().ToLowerInvariant(),
            ["recommendations.embedding_base_url"] = RecommendationsBaseUrl.Trim(),
            ["recommendations.embedding_model"] = RecommendationsModel.Trim(),
        };
        if (!string.IsNullOrWhiteSpace(RecommendationsAuthToken))
            settings["recommendations.embedding_auth_token"] = RecommendationsAuthToken;
        return await SaveSettingsAsync(settings, () => RecommendationsStepDone = true);
    }

    private async Task<bool> SaveSettingsAsync(Dictionary<string, string> settings, Action markDone)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            foreach (var setting in settings)
                await _adminApi.UpdateAdminSettingAsync(setting.Key, setting.Value);
            markDone();
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save settings: {ex.Message}";
            return false;
        }
        finally { IsLoading = false; }
    }

    private static int ProviderOrder(string name) => name switch
    {
        "opensubtitles" => 0,
        "subdl" => 1,
        "subsource" => 2,
        _ => 10,
    };

    private static string GetSetting(Dictionary<string, string> settings, string key, string fallback) =>
        settings.TryGetValue(key, out var value) ? value : fallback;

    private static bool GetBool(Dictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) && parsed;
}

public partial class SetupSubtitleProviderItem : ObservableObject
{
    public SetupSubtitleProviderItem(SubtitleProviderConfig config)
    {
        ProviderName = config.ProviderName;
        Enabled = config.Enabled;
        HasCredential = config.HasCredentials || config.HasApiKey;
    }

    public string ProviderName { get; }
    public string DisplayName => ProviderName switch
    {
        "opensubtitles" => "OpenSubtitles",
        "subdl" => "SubDL",
        "subsource" => "SubSource",
        _ => ProviderName,
    };
    public string Description => ProviderName switch
    {
        "opensubtitles" => "Largest subtitle database. Requires a free account.",
        "subdl" => "Fast, modern subtitle API with generous free tier.",
        "subsource" => "Community-driven subtitle source.",
        _ => "Subtitle provider",
    };
    public bool IsOpenSubtitles => ProviderName == "opensubtitles";
    public bool UsesApiKey => !IsOpenSubtitles;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _hasCredential;
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
}

public sealed class SetupLibraryProviderLevel
{
    public SetupLibraryProviderLevel(string level)
    {
        Level = level;
        Label = string.Join(" ", level.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    public string Level { get; }
    public string Label { get; }
    public ObservableCollection<SetupLibraryProviderEntry> Entries { get; } = [];
}

public partial class SetupLibraryProviderEntry : ObservableObject
{
    public SetupLibraryProviderEntry(
        string level,
        int pluginInstallationId,
        string capabilityId,
        string providerSlug,
        bool enabled)
    {
        Level = level;
        PluginInstallationId = pluginInstallationId;
        CapabilityId = capabilityId;
        ProviderSlug = providerSlug;
        _enabled = enabled;
    }

    public string Level { get; }
    public int PluginInstallationId { get; }
    public string CapabilityId { get; }
    public string ProviderSlug { get; }
    [ObservableProperty] private bool _enabled;
}
