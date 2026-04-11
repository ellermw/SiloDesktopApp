using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.ViewModels;

public partial class SignupViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly AuthApi _authApi;
    private readonly CredentialStore _credentialStore;

    public SignupViewModel(AuthService authService, AuthApi authApi, CredentialStore credentialStore)
    {
        _authService = authService;
        _authApi = authApi;
        _credentialStore = credentialStore;
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
                InviteCode = InviteCode.Trim()
            };

            var response = await _authApi.SignupAsync(request);

            // Save refresh token only (access token stays in-memory, re-minted on launch)
            _credentialStore.SaveCredential(ServerUrl, "refresh_token", response.RefreshToken);

            // Set auth state
            _authService.SetTokens(response.AccessToken, response.RefreshToken, response.ExpiresIn);
            _authService.SetCurrentUser(response.User);

            SignupSucceeded?.Invoke();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "signup_disabled" => "Signup is not currently available.",
                "invalid_invite_code" => "The invite code is invalid or has expired.",
                "username_taken" => "This username is already taken.",
                "email_taken" => "This email is already registered.",
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
