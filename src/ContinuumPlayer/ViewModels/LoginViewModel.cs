using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly AuthApi _authApi;
    private readonly CredentialStore _credentialStore;

    public LoginViewModel(AuthService authService, AuthApi authApi, CredentialStore credentialStore)
    {
        _authService = authService;
        _authApi = authApi;
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

    // ===== Auth Providers =====

    public ObservableCollection<AuthProvider> AuthProviders { get; } = [];

    [ObservableProperty]
    private bool _hasAuthProviders;

    [ObservableProperty]
    private bool _isSignupEnabled;

    /// <summary>
    /// Event raised when login succeeds. The caller should navigate to the profile select page.
    /// </summary>
    public event Action? LoginSucceeded;

    [RelayCommand]
    private async Task LoadAuthInfoAsync()
    {
        // Load auth providers and signup status in parallel
        var providerTask = LoadAuthProvidersAsync();
        var signupTask = LoadSignupStatusAsync();
        await Task.WhenAll(providerTask, signupTask);
    }

    private async Task LoadAuthProvidersAsync()
    {
        try
        {
            var response = await _authApi.GetAuthProvidersAsync();
            AuthProviders.Clear();
            foreach (var provider in response.Providers)
            {
                AuthProviders.Add(provider);
            }
            HasAuthProviders = AuthProviders.Count > 0;
        }
        catch
        {
            HasAuthProviders = false;
        }
    }

    private async Task LoadSignupStatusAsync()
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

            // Save refresh token only. Access token is kept in-memory and re-minted
            // from the refresh token on each app launch (matches WebUI security model
            // and prevents the 24h JWT from sitting on disk).
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
