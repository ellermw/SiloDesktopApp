using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly AuthApi _authApi;
    private readonly AdminApi _adminApi;
    private readonly AuthService _authService;
    private readonly CredentialStore _credentialStore;

    public SetupWizardViewModel(AuthApi authApi, AdminApi adminApi, AuthService authService, CredentialStore credentialStore)
    {
        _authApi = authApi;
        _adminApi = adminApi;
        _authService = authService;
        _credentialStore = credentialStore;
    }

    // ===== Step tracking =====
    // B47: We still track step as 1-5 internally for the linear visibility logic,
    // but DetermineStartingStepAsync() jumps the wizard forward when state shows
    // earlier steps already done (matches WebUI accountComplete/profileComplete/...).

    [ObservableProperty]
    private int _currentStep = 1;

    public int TotalSteps => 8;

    /// <summary>True after the user has explicitly skipped the library step. Persisted in-memory only.</summary>
    [ObservableProperty]
    private bool _libraryStepSkipped;

    /// <summary>True after the user has saved the server step at least once.</summary>
    [ObservableProperty]
    private bool _serverStepDone;

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
    private bool _transcodingEnabled;

    [ObservableProperty]
    private string _jellyfinUrl = "";

    [ObservableProperty]
    private string _jellyfinName = "";

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

    // ===== Step 5: Metadata =====

    // B49: Default to MetaDB — Continuum's primary metadata source.
    [ObservableProperty]
    private string _selectedProvider = "metadb";

    // ===== Navigation helpers =====

    public bool CanGoBack => CurrentStep > 1;
    public bool CanGoNext => CurrentStep < TotalSteps;

    /// <summary>
    /// Event raised when setup is complete. The caller should navigate to the home page.
    /// </summary>
    public event Action? SetupCompleted;

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

            // 3. Library complete? (only check if user is admin — non-admins
            //    can't see /libraries during setup anyway)
            bool libraryComplete = false;
            if (_authService.CurrentUser?.Role == "admin")
            {
                try
                {
                    var libs = await _adminApi.GetAdminLibrariesAsync();
                    libraryComplete = libs.Count > 0;
                }
                catch
                {
                    // Fall through — treat as not complete
                }
            }
            if (!libraryComplete && !LibraryStepSkipped)
            {
                CurrentStep = 3;
                return;
            }

            // 4. Server step done?
            if (!ServerStepDone)
            {
                CurrentStep = 4;
                return;
            }

            // 5. Metadata
            CurrentStep = 5;
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
                if (!await SubmitLibraryStepAsync()) return;
                break;
            case 4:
                if (!await SubmitServerStepAsync()) return;
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
    private async Task FinishAsync()
    {
        ErrorMessage = null;

        // Submit metadata step (step 5)
        if (!await SubmitMetadataStepAsync()) return;

        SetupCompleted?.Invoke();
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

        IsLoading = true;
        try
        {
            var request = new SetupRequest
            {
                Username = Username.Trim(),
                Email = Email.Trim(),
                Password = Password
            };

            var response = await _authApi.SetupAsync(request);

            // Save refresh token only (access token stays in-memory, re-minted on launch)
            _credentialStore.SaveCredential(ServerUrl, "refresh_token", response.RefreshToken);

            _authService.SetTokens(response.AccessToken, response.RefreshToken, response.ExpiresIn);
            _authService.SetCurrentUser(response.User);

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
            _authService.SelectProfile(profile.Id);
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
        // Library creation is optional during setup
        // B47: Track skip so DetermineStartingStepAsync won't bounce us back here.
        if (string.IsNullOrWhiteSpace(LibraryName))
        {
            LibraryStepSkipped = true;
            return true;
        }

        IsLoading = true;
        try
        {
            var body = new
            {
                name = LibraryName.Trim(),
                type = LibraryType,
                paths = LibraryPaths.ToList()
            };

            var library = await _adminApi.CreateLibraryAsync(body);

            if (ScanAfterCreate)
            {
                try
                {
                    await _adminApi.ScanLibraryAsync(library.Id);
                }
                catch
                {
                    // Scan failure is non-fatal during setup
                }
            }

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
            if (!string.IsNullOrWhiteSpace(JellyfinUrl))
                settings["jellyfin_compat.public_url"] = JellyfinUrl.Trim();
            if (!string.IsNullOrWhiteSpace(JellyfinName))
                settings["jellyfin_compat.server_name"] = JellyfinName.Trim();

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
                try
                {
                    await _adminApi.UpdateAdminSettingAsync(key, value);
                }
                catch
                {
                    // Individual setting failures are non-fatal during setup
                }
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

    private async Task<bool> SubmitMetadataStepAsync()
    {
        IsLoading = true;
        try
        {
            // Metadata provider configuration is best-effort during setup
            if (!string.IsNullOrWhiteSpace(SelectedProvider))
            {
                try
                {
                    await _adminApi.UpdateAdminSettingAsync("metadata_provider", SelectedProvider);
                }
                catch
                {
                    // Non-fatal
                }
            }
            return true;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
