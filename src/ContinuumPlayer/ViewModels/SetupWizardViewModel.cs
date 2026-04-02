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

    [ObservableProperty]
    private int _currentStep = 1;

    public int TotalSteps => 5;

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

    [ObservableProperty]
    private string _hardwareAccel = "";

    [ObservableProperty]
    private bool _transcodingEnabled;

    [ObservableProperty]
    private string _jellyfinUrl = "";

    [ObservableProperty]
    private string _jellyfinName = "";

    // ===== Step 5: Metadata =====

    [ObservableProperty]
    private string _selectedProvider = "tmdb";

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

            // Store tokens
            _credentialStore.SaveCredential(ServerUrl, "access_token", response.AccessToken);
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
        if (string.IsNullOrWhiteSpace(LibraryName))
            return true;

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

            if (!string.IsNullOrWhiteSpace(RedisUrl))
                settings["redis_url"] = RedisUrl.Trim();
            if (!string.IsNullOrWhiteSpace(FfmpegPath))
                settings["ffmpeg_path"] = FfmpegPath.Trim();
            if (!string.IsNullOrWhiteSpace(TranscodeDir))
                settings["transcode_dir"] = TranscodeDir.Trim();
            if (!string.IsNullOrWhiteSpace(HardwareAccel))
                settings["hardware_accel"] = HardwareAccel.Trim();
            settings["transcoding_enabled"] = TranscodingEnabled.ToString().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(JellyfinUrl))
                settings["jellyfin_url"] = JellyfinUrl.Trim();
            if (!string.IsNullOrWhiteSpace(JellyfinName))
                settings["jellyfin_name"] = JellyfinName.Trim();

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
