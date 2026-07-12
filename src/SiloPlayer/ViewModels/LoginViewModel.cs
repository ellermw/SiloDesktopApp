using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly AuthApi _authApi;

    public LoginViewModel(AuthService authService, AuthApi authApi)
    {
        _authService = authService;
        _authApi = authApi;
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

    public ObservableCollection<AuthProvider> OAuthProviders { get; } = [];
    public ObservableCollection<AuthProvider> CredentialProviders { get; } = [];

    // Kept as a compatibility alias for older source tests and XAML references.
    public ObservableCollection<AuthProvider> AuthProviders => OAuthProviders;

    [ObservableProperty]
    private AuthProvider? _selectedCredentialProvider;

    [ObservableProperty]
    private bool _hasAuthProviders;

    [ObservableProperty]
    private bool _hasOAuthProviders;

    [ObservableProperty]
    private bool _hasCredentialProviderPicker;

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
            var providers = await _authApi.GetAuthProvidersAsync();
            OAuthProviders.Clear();
            CredentialProviders.Clear();

            foreach (var provider in providers)
            {
                if (string.Equals(provider.Mode, "oauth", StringComparison.OrdinalIgnoreCase) && provider.InstallationId > 0)
                {
                    OAuthProviders.Add(provider);
                    continue;
                }

                if (string.Equals(provider.Mode, "credentials", StringComparison.OrdinalIgnoreCase))
                {
                    CredentialProviders.Add(provider);
                }
            }

            SelectedCredentialProvider =
                CredentialProviders.FirstOrDefault(p => p.IsDefault) ??
                CredentialProviders.FirstOrDefault();
            HasOAuthProviders = OAuthProviders.Count > 0;
            HasAuthProviders = HasOAuthProviders;
            HasCredentialProviderPicker = CredentialProviders.Count > 1;
        }
        catch
        {
            OAuthProviders.Clear();
            CredentialProviders.Clear();
            SelectedCredentialProvider = null;
            HasOAuthProviders = false;
            HasAuthProviders = false;
            HasCredentialProviderPicker = false;
        }
    }

    public Task<Uri> BeginOAuthAsync(AuthProvider provider, CancellationToken ct = default)
        => _authApi.StartOAuthAsync(provider.InstallationId, ct);

    public async Task CompleteOAuthAsync(string code, CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        long? authGeneration = null;

        try
        {
            var serverUrl = _authService.ConfiguredServerUrl;
            var tokens = await _authApi.CompleteOAuthAsync(serverUrl, code, ct);
            authGeneration = _authService.SetTokens(
                tokens.AccessToken,
                tokens.RefreshToken,
                tokens.ExpiresIn,
                expectedServerUrl: serverUrl);
            var user = await _authApi.GetMeAsync(ct);
            if (!_authService.SetCurrentUser(user, authGeneration.Value))
                throw new InvalidOperationException("The authentication session changed before OAuth completed.");

            LoginSucceeded?.Invoke();
        }
        catch (Exception ex)
        {
            if (authGeneration.HasValue)
                _authService.AbortAuthenticationAttempt(authGeneration.Value);
            ErrorMessage = $"OAuth sign-in failed: {ex.Message}";
            throw;
        }
        finally
        {
            IsLoading = false;
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
            var providerId = SelectedCredentialProvider?.Id;
            var response = await _authService.LoginAsync(Username.Trim(), Password, providerId);

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
