using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly CredentialStore _credentialStore;

    public LoginViewModel(AuthService authService, CredentialStore credentialStore)
    {
        _authService = authService;
        _credentialStore = credentialStore;
    }

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _serverUrl = "";

    [ObservableProperty]
    private string _serverName = "";

    /// <summary>
    /// Event raised when login succeeds. The caller should navigate to the profile select page.
    /// </summary>
    public event Action? LoginSucceeded;

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Username is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Password is required.";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var response = await _authService.LoginAsync(Username.Trim(), Password);

            // Save tokens to credential store
            _credentialStore.SaveCredential(ServerUrl, "access_token", response.AccessToken);
            _credentialStore.SaveCredential(ServerUrl, "refresh_token", response.RefreshToken);

            LoginSucceeded?.Invoke();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "invalid_credentials" => "Invalid username or password.",
                "user_disabled" => "This account has been disabled.",
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
