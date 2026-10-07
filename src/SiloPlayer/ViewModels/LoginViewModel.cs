using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using Windows.Storage.Streams;
using SiloPlayer.Services;
using SiloPlayer.Views;

namespace SiloPlayer.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly AuthApi _authApi;
    private readonly SettingsApi _settingsApi;
    private readonly SiloApiClient _apiClient;
    private CancellationTokenSource? _authInfoCts;
    private int _authInfoGeneration;
    private NativeOAuthHandshake? _oauthAttempt;
    private IDisposable? _oauthRegistration;
    private ApiRequestContext? _oauthContext;
    private long _oauthGeneration;
    private int _oauthRevision;
    private int _networkRevision;
    public LoginNavigationRequest? NavigationRequest { get; set; }
    [ObservableProperty] private bool _sessionRestoreUnavailable;
    public string SessionRestoreMessage => NavigationRequest?.SessionRestoreErrorCode == "provider_unavailable"
        ? "Can't reach the sign-in provider right now." : "Can't restore your session right now.";
    public void SetNavigationRequest(LoginNavigationRequest? request)
    { NavigationRequest = request; SessionRestoreUnavailable = request?.SessionRestoreUnavailable == true; OnPropertyChanged(nameof(SessionRestoreMessage)); }
    [ObservableProperty] private bool _providersReady;
    [ObservableProperty] private bool _showPasswordForm;
    [ObservableProperty] private bool _isOAuthPending;
    public bool CanStartAuthentication => ProvidersReady && !IsLoading && !IsStartingDeviceLogin;
    public bool ShouldAutoRedirect => CanStartAuthentication && !ShowPasswordForm && NetworkProviders.Count == 0 && OAuthProviders.Count == 1 &&
        string.IsNullOrEmpty(ErrorMessage) && NavigationRequest is not { SignedOut: true } and not { SwitchAccount: true } and not { SessionRestoreUnavailable: true };
    public string FormattedDeviceCode => DeviceCodeText.Format(DeviceSession?.UserCode);
    public string SpokenDeviceCode => DeviceCodeText.Spoken(DeviceSession?.UserCode);
    partial void OnProvidersReadyChanged(bool value) => OnPropertyChanged(nameof(CanStartAuthentication));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(CanStartAuthentication));
    partial void OnIsStartingDeviceLoginChanged(bool value) => OnPropertyChanged(nameof(CanStartAuthentication));
    partial void OnDeviceSessionChanged(DeviceLoginStartResponse? value)
    { OnPropertyChanged(nameof(FormattedDeviceCode)); OnPropertyChanged(nameof(SpokenDeviceCode)); }

    public LoginViewModel(
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

    [ObservableProperty]
    private string? _loginBackgroundUrl;

    [ObservableProperty]
    private bool _recoveryAvailable;
    public bool CanRecoverPassword => RecoveryAvailable && (SelectedCredentialProvider == null || SelectedCredentialProvider.Id == "local");
    partial void OnRecoveryAvailableChanged(bool value) => OnPropertyChanged(nameof(CanRecoverPassword));
    partial void OnSelectedCredentialProviderChanged(AuthProvider? value) => OnPropertyChanged(nameof(CanRecoverPassword));

    // ===== Auth Providers =====

    public ObservableCollection<AuthProvider> OAuthProviders { get; } = [];
    public ObservableCollection<AuthProvider> CredentialProviders { get; } = [];
    public ObservableCollection<NetworkSignInOption> NetworkProviders { get; } = [];
    [ObservableProperty] private bool _hasNetworkProviders;
    public bool ShowNetworkDivider => ShowPasswordForm || HasOAuthProviders;
    partial void OnShowPasswordFormChanged(bool value) => OnPropertyChanged(nameof(ShowNetworkDivider));
    partial void OnHasOAuthProvidersChanged(bool value) => OnPropertyChanged(nameof(ShowNetworkDivider));

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
    private int _deviceAttempt;
    private string? _deviceServer;
    private bool _deviceCompleted;

    /// <summary>
    /// Event raised when login succeeds. The caller should navigate to the profile select page.
    /// </summary>
    public event Action? LoginSucceeded;

    [RelayCommand]
    private async Task LoadAuthInfoAsync()
    {
        _authInfoCts?.Cancel(); _authInfoCts?.Dispose(); _authInfoCts = new();
        var generation = ++_authInfoGeneration; var context = _apiClient.CaptureContext(); var ct = _authInfoCts.Token;
        ProvidersReady = false; ShowPasswordForm = false;
        await Task.WhenAll(LoadAuthProvidersAsync(generation, context, ct), LoadSignupStatusAsync(generation, context, ct),
            LoadBrandingAsync(generation, context, ct), LoadRecoveryAvailabilityAsync(generation, context, ct));
    }

    private bool CurrentAuthInfo(int generation, ApiRequestContext context, CancellationToken ct)
        => generation == _authInfoGeneration && !ct.IsCancellationRequested && _apiClient.IsCurrentContext(context);

    private async Task LoadRecoveryAvailabilityAsync(int generation, ApiRequestContext context, CancellationToken ct)
    {
        try { var value = await _apiClient.GetUnauthenticatedAsync<PasswordResetCapability>("/api/v2/capabilities/password-reset", ct); if (CurrentAuthInfo(generation, context, ct)) RecoveryAvailable = value.State == "available"; }
        catch { if (CurrentAuthInfo(generation, context, ct)) RecoveryAvailable = false; }
    }

    private async Task LoadBrandingAsync(int generation, ApiRequestContext context, CancellationToken ct)
    {
        try
        {
            var branding = await _settingsApi.GetServerBrandingAsync(ct);
            if (!CurrentAuthInfo(generation, context, ct)) return;
            if (!string.IsNullOrWhiteSpace(branding.ServerName))
                ServerName = branding.ServerName;
            LoginSubtitle = string.IsNullOrWhiteSpace(branding.LoginSubtitle)
                ? "Sign in with an existing account."
                : branding.LoginSubtitle;
            LoginBackgroundUrl = _apiClient.ResolveServerUrl(branding.LoginBackgroundUrl);
        }
        catch
        {
            if (!CurrentAuthInfo(generation, context, ct)) return;
            if (string.IsNullOrWhiteSpace(ServerName)) ServerName = "Silo";
            LoginSubtitle = "Sign in with an existing account.";
            LoginBackgroundUrl = null;
        }
    }

    private async Task LoadAuthProvidersAsync(int generation, ApiRequestContext context, CancellationToken ct)
    {
        try
        {
            var providers = await _authApi.GetAuthProvidersAsync(ct);
            if (!CurrentAuthInfo(generation, context, ct)) return;
            OAuthProviders.Clear();
            CredentialProviders.Clear();
            NetworkProviders.Clear();

            foreach (var provider in providers)
            {
                if (provider.Mode == "network" && provider.InstallationId > 0)
                { NetworkProviders.Add(new NetworkSignInOption(provider)); continue; }
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

            var hasDirectory = CredentialProviders.Any(p => p.Id != "local");
            if (hasDirectory) CredentialProviders.Insert(0, new AuthProvider { Id = "auto", DisplayName = "Automatic", Mode = "credentials" });
            SelectedCredentialProvider = hasDirectory ? CredentialProviders[0] :
                CredentialProviders.FirstOrDefault(p => p.IsDefault) ??
                CredentialProviders.FirstOrDefault();
            HasOAuthProviders = OAuthProviders.Count > 0;
            HasNetworkProviders = NetworkProviders.Count > 0;
            HasAuthProviders = HasOAuthProviders || HasNetworkProviders;
            HasCredentialProviderPicker = CredentialProviders.Count > 1;
        }
        catch
        {
            if (!CurrentAuthInfo(generation, context, ct)) return;
            OAuthProviders.Clear();
            CredentialProviders.Clear();
            NetworkProviders.Clear(); HasNetworkProviders = false;
            SelectedCredentialProvider = null;
            HasOAuthProviders = false;
            HasAuthProviders = false;
            HasCredentialProviderPicker = false;
        }
        finally
        {
            if (CurrentAuthInfo(generation, context, ct))
            { ProvidersReady = true; ShowPasswordForm = NavigationRequest is { LocalLogin: true } || CredentialProviders.Count > 0 || OAuthProviders.Count == 0 && NetworkProviders.Count == 0; }
        }
    }

    public async Task<Uri> BeginOAuthAsync(AuthProvider provider, CancellationToken ct = default)
    {
        CancelOAuth(); ErrorMessage = null;
        var revision = _oauthRevision; IsLoading = true;
        var context = _apiClient.CaptureContext(); var server = _authService.ConfiguredServerUrl; var generation = _authService.SessionGeneration;
        var identity = await _authApi.GetServerIdentityAsync(server, ct);
        ct.ThrowIfCancellationRequested();
        if (revision != _oauthRevision || !_apiClient.IsCurrentContext(context) || generation != _authService.SessionGeneration) throw new OperationCanceledException("The authentication context changed.");
        _oauthAttempt = new NativeOAuthHandshake(server, identity.ServerId, provider.InstallationId, provider.NativeStartPath ?? "", NavigationRequest is { SwitchAccount: true });
        _oauthContext = context; _oauthGeneration = generation;
        _oauthRegistration = NativeOAuthCallbacks.Register(AcceptNativeOAuthCallbackAsync);
        IsOAuthPending = true; IsLoading = true;
        return _oauthAttempt.StartUri;
    }

    public async Task<bool> AcceptNativeOAuthCallbackAsync(string uri)
    {
        var attempt = _oauthAttempt; var context = _oauthContext;
        if (attempt == null || context == null || !_apiClient.IsCurrentContext(context.Value) ||
            _authService.SessionGeneration != _oauthGeneration || !attempt.TryConsumeCallback(uri, out var callback)) return false;
        try
        {
            if (callback.Error.Length != 0) { ErrorMessage = ExternalSignInErrors.Describe(callback.Error); return true; }
            var tokens = await _authApi.CompleteNativeOAuthAsync(attempt.ServerBase, callback.Code, attempt.CodeVerifier);
            var user = await _authApi.GetMeAsync(attempt.ServerBase, tokens.AccessToken);
            if (!ReferenceEquals(_oauthAttempt, attempt) || !_apiClient.IsCurrentContext(context.Value)) return true;
            var response = new LoginResponse { AccessToken = tokens.AccessToken, RefreshToken = tokens.RefreshToken, ExpiresIn = tokens.ExpiresIn, User = user };
            if (_authService.CompleteRecoveryLogin(response, _oauthGeneration, context.Value.BaseUrl)) LoginSucceeded?.Invoke();
        }
        catch (Exception ex) { if (ReferenceEquals(_oauthAttempt, attempt) && _apiClient.IsCurrentContext(context.Value)) ErrorMessage = ex is ApiException api ? ExternalSignInErrors.Describe(api.ErrorCode) : "Sign-in with the provider failed. Try again."; }
        finally { if (ReferenceEquals(_oauthAttempt, attempt)) CancelOAuth(); }
        return true;
    }

    public void CancelOAuth()
    { ++_oauthRevision; _oauthAttempt?.Cancel(); _oauthAttempt = null; _oauthRegistration?.Dispose(); _oauthRegistration = null; _oauthContext = null; IsOAuthPending = false; IsLoading = false; }

    public void CancelAuthFlows()
    {
        ++_authInfoGeneration; ++_networkRevision;
        foreach (var choice in NetworkProviders) choice.IsPending = false;
        _authInfoCts?.Cancel(); CancelOAuth(); CancelDeviceLogin(clearSession: true);
    }

    public async Task BeginNetworkSignInAsync(AuthProvider provider, CancellationToken ct = default)
    {
        if (!CanStartAuthentication || provider.Mode != "network" || !NetworkProviders.Any(option => ReferenceEquals(option.Provider, provider))) return;
        var revision = ++_networkRevision; var context = _apiClient.CaptureContext();
        var generation = _authService.SessionGeneration; var discovery = _authInfoGeneration;
        bool Current() => revision == _networkRevision && discovery == _authInfoGeneration &&
            !ct.IsCancellationRequested && _apiClient.IsCurrentContext(context) && generation == _authService.SessionGeneration;
        var choice = NetworkProviders.Single(option => ReferenceEquals(option.Provider, provider));
        IsLoading = true; ErrorMessage = null; choice.IsPending = true;
        try
        {
            var tokens = await _authApi.SignInWithNetworkAsync(context.BaseUrl, provider.InstallationId, ct);
            if (!Current()) return;
            var user = await _authApi.GetMeAsync(context.BaseUrl, tokens.AccessToken, ct);
            if (!Current()) return;
            var response = new LoginResponse { AccessToken = tokens.AccessToken, RefreshToken = tokens.RefreshToken, ExpiresIn = tokens.ExpiresIn, User = user };
            if (_authService.CompleteRecoveryLogin(response, generation, context.BaseUrl)) LoginSucceeded?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (Current()) ErrorMessage = ex is ApiException problem
                ? problem.StatusCode == 429 ? "Too many attempts. Wait a minute and try again." : ExternalSignInErrors.DescribeNetwork(problem.ErrorCode, provider.DisplayName)
                : "Couldn't reach the server. Try again.";
        }
        finally
        {
            if (revision == _networkRevision) { choice.IsPending = false; IsLoading = false; }
        }
    }

    public async Task StartDeviceLoginAsync()
    {
        if (!CanStartAuthentication) return;
        CancelDeviceLogin(clearSession: true);
        var attempt = ++_deviceAttempt; var context = _apiClient.CaptureContext(); var server = _authService.ConfiguredServerUrl;
        var authGeneration = _authService.SessionGeneration;
        _deviceServer = server;
        IsStartingDeviceLogin = true;
        ErrorMessage = null;
        try
        {
            _deviceLoginCts = new CancellationTokenSource();
            var ct = _deviceLoginCts.Token;
            var session = await _authApi.DeviceStartAsync(
                server,
                $"{Environment.MachineName} Silo Desktop",
                "Windows",
                ct);
            if (ct.IsCancellationRequested || attempt != _deviceAttempt || !_apiClient.IsCurrentContext(context))
            { _ = CancelAbandonedDeviceAsync(server, session.DeviceCode); return; }
            DeviceSession = session;
            ShowDeviceFallback = false;
            DeviceStatusMessage = "Waiting for approval on your phone...";
            var image = await CreateQrImageAsync(session.VerificationUriComplete);
            if (ct.IsCancellationRequested || attempt != _deviceAttempt || !_apiClient.IsCurrentContext(context)) return;
            DeviceQrImage = image;
            _ = PollDeviceLoginAsync(session, server, context, authGeneration, attempt, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (attempt != _deviceAttempt || !_apiClient.IsCurrentContext(context)) return;
            DeviceStatusMessage = "";
            ErrorMessage = $"Couldn't start phone sign-in: {ex.Message}";
            CancelDeviceLogin(clearSession: true);
        }
        finally
        {
            if (attempt == _deviceAttempt) IsStartingDeviceLogin = false;
        }
    }

    public void CancelDeviceLogin(bool clearSession = false)
    {
        ++_deviceAttempt;
        if (!_deviceCompleted && _deviceServer is { } server && DeviceSession is { } session)
            _ = CancelAbandonedDeviceAsync(server, session.DeviceCode);
        _deviceServer = null; _deviceCompleted = false;
        var cts = _deviceLoginCts;
        _deviceLoginCts = null;
        try { cts?.Cancel(); } catch { }
        cts?.Dispose();
        IsDevicePolling = false;
        IsStartingDeviceLogin = false;
        if (!clearSession) return;
        DeviceSession = null;
        DeviceQrImage = null;
        ShowDeviceFallback = false;
        DeviceStatusMessage = "";
    }

    private async Task CancelAbandonedDeviceAsync(string server, string deviceCode)
    {
        if (string.IsNullOrWhiteSpace(deviceCode)) return;
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await _authApi.DeviceCancelAsync(server, deviceCode, timeout.Token); }
        catch { /* Abandonment is best effort and never masks a new sign-in attempt. */ }
    }

    private async Task PollDeviceLoginAsync(DeviceLoginStartResponse session, string server,
        ApiRequestContext context, long authGeneration, int attempt, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, session.Interval > 0 ? session.Interval : 3));
        try
        {
            while (!ct.IsCancellationRequested && ReferenceEquals(DeviceSession, session))
            {
                IsDevicePolling = true;
                DeviceLoginPollResponse result;
                try { result = await _authApi.DevicePollAsync(server, session.DeviceCode, ct); }
                catch (Exception) when (!ct.IsCancellationRequested)
                { await Task.Delay(interval, ct); continue; }
                if (ct.IsCancellationRequested || attempt != _deviceAttempt || !_apiClient.IsCurrentContext(context) || !ReferenceEquals(DeviceSession, session)) return;
                IsDevicePolling = false;

                if (string.Equals(result.Status, "approved", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(result.AccessToken)
                    && !string.IsNullOrWhiteSpace(result.RefreshToken)
                    && result.User != null)
                {
                    var response = new LoginResponse { AccessToken = result.AccessToken, RefreshToken = result.RefreshToken,
                        ExpiresIn = result.ExpiresIn.GetValueOrDefault(), User = result.User };
                    if (!_authService.CompleteRecoveryLogin(response, authGeneration, server)) return;
                    _deviceCompleted = true;

                    DeviceStatusMessage = "Signed in. Loading profiles...";
                    LoginSucceeded?.Invoke();
                    return;
                }

                if (result.Status is "denied" or "expired" or "consumed" or "canceled")
                {
                    var message = result.Status == "denied"
                        ? "Approval was denied. Start over to try again."
                        : "This code is no longer valid. Start over to generate a new one.";
                    _deviceCompleted = true; DeviceSession = null; DeviceQrImage = null; ErrorMessage = message;
                    return;
                }

                DeviceStatusMessage = result.Status == "opened" ? "Continue on your phone." : "Waiting for approval on your phone...";
                await Task.Delay(interval, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested && attempt == _deviceAttempt && _apiClient.IsCurrentContext(context))
                DeviceStatusMessage = $"Phone sign-in failed: {ex.Message}";
        }
        finally
        {
            if (attempt == _deviceAttempt) IsDevicePolling = false;
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

    private async Task LoadSignupStatusAsync(int generation, ApiRequestContext context, CancellationToken ct)
    {
        try
        {
            var status = await _authApi.GetSignupStatusAsync(ct);
            if (CurrentAuthInfo(generation, context, ct)) IsSignupEnabled = status.Enabled;
        }
        catch
        {
            if (CurrentAuthInfo(generation, context, ct)) IsSignupEnabled = false;
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (!CanStartAuthentication || !ShowPasswordForm) return;
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
            var providerId = SelectedCredentialProvider?.Id is "auto" ? null : SelectedCredentialProvider?.Id;
            var response = await _authService.LoginAsync(Username.Trim(), Password, providerId);

            LoginSucceeded?.Invoke();
        }
        catch (ApiException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "invalid_credentials" => "Invalid username or password.",
                "user_disabled" => "This account has been disabled.",
                "local_login_disabled" => "Password sign-in is turned off for this account. Use " + string.Join(", ", OAuthProviders.Select(p => p.DisplayName).Concat(NetworkProviders.Select(p => p.Provider.DisplayName))) + " to sign in.",
                "password_expired" => "Your password at the sign-in provider has expired. Change it there before signing in.",
                "permission_denied" => "This account is disabled.",
                "not_permitted" or "account_disabled" or "provider_unavailable" => ExternalSignInErrors.Describe(ex.ErrorCode),
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
