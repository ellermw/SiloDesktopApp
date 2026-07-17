using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using Windows.Storage.Streams;

namespace SiloPlayer.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly AuthApi _authApi;
    private readonly SettingsApi _settingsApi;

    public LoginViewModel(AuthService authService, AuthApi authApi, SettingsApi settingsApi)
    {
        _authService = authService;
        _authApi = authApi;
        _settingsApi = settingsApi;
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

    [ObservableProperty]
    private string _loginSubtitle = "Sign in with an existing account.";

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

    // ===== Device login ("Use your phone instead") =====

    [ObservableProperty]
    private DeviceLoginStartResponse? _deviceSession;

    [ObservableProperty]
    private BitmapImage? _deviceQrImage;

    [ObservableProperty]
    private bool _isStartingDeviceLogin;

    [ObservableProperty]
    private bool _isDevicePolling;

    [ObservableProperty]
    private bool _showDeviceFallback;

    [ObservableProperty]
    private string _deviceStatusMessage = "";

    private CancellationTokenSource? _deviceLoginCts;

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
        var brandingTask = LoadBrandingAsync();
        await Task.WhenAll(providerTask, signupTask, brandingTask);
    }

    private async Task LoadBrandingAsync()
    {
        try
        {
            var branding = await _settingsApi.GetServerBrandingAsync();
            if (!string.IsNullOrWhiteSpace(branding.ServerName))
                ServerName = branding.ServerName;
            LoginSubtitle = string.IsNullOrWhiteSpace(branding.LoginSubtitle)
                ? "Sign in with an existing account."
                : branding.LoginSubtitle;
        }
        catch
        {
            if (string.IsNullOrWhiteSpace(ServerName)) ServerName = "Silo";
            LoginSubtitle = "Sign in with an existing account.";
        }
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

    public async Task StartDeviceLoginAsync()
    {
        CancelDeviceLogin(clearSession: true);
        IsStartingDeviceLogin = true;
        ErrorMessage = null;
        try
        {
            _deviceLoginCts = new CancellationTokenSource();
            var ct = _deviceLoginCts.Token;
            var session = await _authApi.DeviceStartAsync(
                $"{Environment.MachineName} Silo Desktop",
                "Windows",
                ct);
            DeviceSession = session;
            ShowDeviceFallback = false;
            DeviceStatusMessage = "Waiting for approval on your phone...";
            DeviceQrImage = await CreateQrImageAsync(session.VerificationUriComplete);
            _ = PollDeviceLoginAsync(session, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DeviceStatusMessage = "";
            ErrorMessage = $"Couldn't start phone sign-in: {ex.Message}";
            CancelDeviceLogin(clearSession: true);
        }
        finally
        {
            IsStartingDeviceLogin = false;
        }
    }

    public void CancelDeviceLogin(bool clearSession = false)
    {
        var cts = _deviceLoginCts;
        _deviceLoginCts = null;
        try { cts?.Cancel(); } catch { }
        cts?.Dispose();
        IsDevicePolling = false;
        if (!clearSession) return;
        DeviceSession = null;
        DeviceQrImage = null;
        ShowDeviceFallback = false;
        DeviceStatusMessage = "";
    }

    private async Task PollDeviceLoginAsync(DeviceLoginStartResponse session, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, session.Interval > 0 ? session.Interval : 3));
        try
        {
            while (!ct.IsCancellationRequested && ReferenceEquals(DeviceSession, session))
            {
                IsDevicePolling = true;
                var result = await _authApi.DevicePollAsync(session.DeviceCode, ct);
                IsDevicePolling = false;

                if (string.Equals(result.Status, "approved", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(result.AccessToken)
                    && !string.IsNullOrWhiteSpace(result.RefreshToken)
                    && result.User != null)
                {
                    var serverUrl = _authService.ConfiguredServerUrl;
                    var generation = _authService.SetTokens(
                        result.AccessToken,
                        result.RefreshToken,
                        result.ExpiresIn.GetValueOrDefault(),
                        expectedServerUrl: serverUrl);
                    if (!_authService.SetCurrentUser(result.User, generation))
                        throw new InvalidOperationException("The authentication session changed before phone sign-in completed.");

                    DeviceStatusMessage = "Signed in. Loading profiles...";
                    LoginSucceeded?.Invoke();
                    return;
                }

                if (result.Status is "denied" or "expired" or "consumed")
                {
                    DeviceStatusMessage = result.Status == "denied"
                        ? "Approval was denied. Start over to try again."
                        : "This code is no longer valid. Start over to generate a new one.";
                    return;
                }

                DeviceStatusMessage = "Waiting for approval on your phone...";
                await Task.Delay(interval, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                DeviceStatusMessage = $"Phone sign-in failed: {ex.Message}";
        }
        finally
        {
            IsDevicePolling = false;
        }
    }

    private static async Task<BitmapImage> CreateQrImageAsync(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("The server did not provide a device verification URL.");

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        using var qr = new PngByteQRCode(data);
        var bytes = qr.GetGraphic(8, drawQuietZones: true);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        return image;
    }

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
