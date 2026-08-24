using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public partial class SignupViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly AuthApi _authApi;
    private readonly SettingsApi _settingsApi;
    private readonly SiloApiClient _apiClient;

    public SignupViewModel(
        AuthService authService,
        AuthApi authApi,
        SettingsApi settingsApi,
        SiloApiClient apiClient)
    {
        _authService = authService;
        _authApi = authApi;
        _settingsApi = settingsApi;
        _apiClient = apiClient;
    }

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _email = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _confirmPassword = "";

    [ObservableProperty]
    private string _inviteCode = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSignupEnabled;

    [ObservableProperty]
    private bool _isCheckingSignupStatus;

    [ObservableProperty]
    private string _serverUrl = "";

    [ObservableProperty]
    private string _serverName = "";

    [ObservableProperty]
    private string? _loginBackgroundUrl;

    /// <summary>
    /// Event raised when signup succeeds. The caller should navigate to the profile select page.
    /// </summary>
    public bool ShowSignupForm => !IsCheckingSignupStatus && IsSignupEnabled;
    public bool ShowSignupClosed => !IsCheckingSignupStatus && !IsSignupEnabled;

    public event Action<bool>? SignupSucceeded;

    partial void OnIsSignupEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSignupForm));
        OnPropertyChanged(nameof(ShowSignupClosed));
    }

    partial void OnIsCheckingSignupStatusChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSignupForm));
        OnPropertyChanged(nameof(ShowSignupClosed));
    }

    [RelayCommand]
    private async Task CheckSignupStatusAsync()
    {
        IsCheckingSignupStatus = true;
        var brandingTask = LoadBrandingAsync();
        try
        {
            var status = await _authApi.GetSignupStatusAsync();
            IsSignupEnabled = status.Enabled;
        }
        catch
        {
            // The WebUI only renders the closed state after an authoritative
            // disabled response. A transient status-check failure must not lock
            // a valid invite out of the signup form.
            IsSignupEnabled = true;
        }
        finally
        {
            await brandingTask;
            IsCheckingSignupStatus = false;
        }
    }

    private async Task LoadBrandingAsync()
    {
        try
        {
            var branding = await _settingsApi.GetServerBrandingAsync();
            ServerName = string.IsNullOrWhiteSpace(branding.ServerName) ? "Silo" : branding.ServerName;
            LoginBackgroundUrl = _apiClient.ResolveServerUrl(branding.LoginBackgroundUrl);
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(ServerName)) ServerName = "Silo";
            LoginBackgroundUrl = null;
        }
    }

    [RelayCommand]
    private async Task SignupAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Username is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "Email is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Password is required.";
            return;
        }
        if (Password.Length < 8)
        {
            ErrorMessage = "Password must be at least 8 characters.";
            return;
        }
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }
        if (string.IsNullOrWhiteSpace(InviteCode))
        {
            ErrorMessage = "Invite code is required.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var request = new SignupRequest
            {
                Username = Username.Trim(),
                Email = Email.Trim(),
                Password = Password,
                InviteCode = InviteCode.Trim(),
                CreateDefaultProfile = true,
                DefaultProfileName = Username.Trim(),
            };

            var serverUrl = _authService.ConfiguredServerUrl;
            var response = await _authApi.SignupAsync(serverUrl, request);

            // Set auth state
            var authGeneration = _authService.SetTokens(
                response.AccessToken,
                response.RefreshToken,
                response.ExpiresIn,
                expectedServerUrl: serverUrl);
            if (!_authService.SetCurrentUser(response.User, authGeneration))
                throw new InvalidOperationException("The authentication session changed before signup completed.");

            var selectedBootstrapProfile = false;
            try
            {
                var profiles = (await _authApi.GetProfilesAsync()).Profiles;
                if (profiles.Count == 1 && !profiles[0].HasPin)
                {
                    _authService.SelectProfile(profiles[0].Id, profile: profiles[0]);
                    selectedBootstrapProfile = true;
                }
            }
            catch
            {
                // Account creation succeeded. Profile discovery is optional and
                // the profile chooser remains the safe fallback, matching WebUI.
            }

            SignupSucceeded?.Invoke(selectedBootstrapProfile);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "signup_disabled" => "Signup is not currently available.",
                "invalid_code" => "The invite code is invalid.",
                "code_exhausted" => "This invite code has reached its maximum uses.",
                "code_disabled" => "This invite code is no longer active.",
                "duplicate" => "This username or email is already registered.",
                _ => ex.Message
            };
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Unable to connect to the server. Check your network connection.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"An error occurred: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
