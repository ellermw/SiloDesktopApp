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

    public SignupViewModel(AuthService authService, AuthApi authApi)
    {
        _authService = authService;
        _authApi = authApi;
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
    private string _serverUrl = "";

    [ObservableProperty]
    private string _serverName = "";

    /// <summary>
    /// Event raised when signup succeeds. The caller should navigate to the profile select page.
    /// </summary>
    public event Action? SignupSucceeded;

    [RelayCommand]
    private async Task CheckSignupStatusAsync()
    {
        try
        {
            var status = await _authApi.GetSignupStatusAsync();
            IsSignupEnabled = status.Enabled;
        }
        catch
        {
            IsSignupEnabled = false;
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
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
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

            SignupSucceeded?.Invoke();
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
