using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class LoginPage : Page
{
    public LoginViewModel ViewModel { get; }
    private ServerEntry? _server;
    private CancellationTokenSource? _appearanceCancellation;

    public LoginPage()
    {
        ViewModel = App.Services.GetRequiredService<LoginViewModel>();
        this.InitializeComponent();
        PasswordReveal.WrapInParent(PasswordBox, LoginPasswordGroup);
        SizeChanged += (_, e) => LoginLayout.Width = Math.Min(448, Math.Max(1, e.NewSize.Width - 48));

        ViewModel.LoginSucceeded += OnLoginSucceeded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var request = e.Parameter as LoginNavigationRequest;
        ViewModel.SetNavigationRequest(request);
        if ((request?.Server ?? e.Parameter as ServerEntry) is { } server)
        {
            _server = server;
            ViewModel.ServerUrl = server.Url;
            ViewModel.ServerName = server.Name;
        }

        // Surface the selected server's display name in the card header (web parity:
        // web uses the server branding name as the auth card title).
        if (!string.IsNullOrWhiteSpace(ViewModel.ServerName))
        {
            ServerNameTitle.Text = ViewModel.ServerName;
        }

        _appearanceCancellation?.Cancel();
        _appearanceCancellation?.Dispose();
        _appearanceCancellation = new CancellationTokenSource();
        _ = RefreshSharedAppearanceAsync(_appearanceCancellation.Token);

        // Load auth providers and signup status
        await ViewModel.LoadAuthInfoCommand.ExecuteAsync(null);
        if (ViewModel.ShouldAutoRedirect)
            await StartOAuthAsync(ViewModel.OAuthProviders[0]);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _appearanceCancellation?.Cancel();
        _appearanceCancellation?.Dispose();
        _appearanceCancellation = null;
        ViewModel.CancelAuthFlows();
        base.OnNavigatedFrom(e);
    }

    private static async Task RefreshSharedAppearanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await App.Services.GetRequiredService<SiloPlayer.Services.ThemeService>()
                .RefreshSharedAppearanceIfStaleAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch { /* Appearance must not prevent signing in. Cinema Dark remains available. */ }
    }

    private void OnLoginSucceeded()
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (App.Services.GetRequiredService<AuthService>().PasswordChangeRequired)
            nav.Navigate<ChoosePasswordPage>();
        else nav.Navigate<ProfileSelectPage>();
    }

    private void ForgotPassword_Click(object sender, RoutedEventArgs e)
        => App.Services.GetRequiredService<NavigationService>().Navigate<PasswordRecoveryPage>();

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<ServerSelectPage>();
    }

    private void CreateAccountButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<SignupPage>(_server ?? new ServerEntry
        {
            Url = ViewModel.ServerUrl,
            Name = ViewModel.ServerName,
        });
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.Password = PasswordBox.Password;
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.LoginCommand.CanExecute(null))
        {
            // Sync password from PasswordBox since PasswordBox two-way binding needs explicit sync
            ViewModel.Password = PasswordBox.Password;
            ViewModel.LoginCommand.Execute(null);
        }
    }

    private async void AuthProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not AuthProvider provider || provider.InstallationId <= 0)
            return;

        if (!ViewModel.CanStartAuthentication) return;
        await StartOAuthAsync(provider);
    }

    private async void NetworkProviderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AuthProvider provider })
            await ViewModel.BeginNetworkSignInAsync(provider, _appearanceCancellation?.Token ?? CancellationToken.None);
    }

    private async Task StartOAuthAsync(AuthProvider provider)
    {
        try
        {
            var authorizeUri = await ViewModel.BeginOAuthAsync(provider, _appearanceCancellation?.Token ?? CancellationToken.None);
            if (!await Windows.System.Launcher.LaunchUriAsync(authorizeUri))
                throw new InvalidOperationException("The sign-in browser could not be opened.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_appearanceCancellation?.IsCancellationRequested != false) return;
            ViewModel.CancelOAuth();
            ViewModel.ErrorMessage = $"Couldn't start OAuth sign-in: {ex.Message}";
        }
    }

    private void CancelOAuth_Click(object sender, RoutedEventArgs e) => ViewModel.CancelOAuth();

    private async void RetryRestore_Click(object sender, RoutedEventArgs e)
    {
        var request = ViewModel.NavigationRequest;
        if (ViewModel.IsLoading || request?.RetryRestoreAsync is not { } retry) return;
        var cancellation = _appearanceCancellation?.Token ?? CancellationToken.None;
        bool Current() => !cancellation.IsCancellationRequested && ReferenceEquals(request, ViewModel.NavigationRequest);
        ViewModel.IsLoading = true;
        try { await retry(cancellation); }
        catch (OperationCanceledException) { }
        catch (SiloPlayer.Core.Api.ApiException ex) when (ex.StatusCode == 401)
        {
            if (!Current()) return;
            ViewModel.SetNavigationRequest(request with { SessionRestoreUnavailable = false, SessionEnded = true });
            ViewModel.ErrorMessage = "Your sign-in has expired. Sign in again.";
        }
        catch (SiloPlayer.Core.Api.ApiException ex)
        { if (Current()) ViewModel.SetNavigationRequest(request with { SessionRestoreErrorCode = ex.ErrorCode }); }
        catch (HttpRequestException) { /* The existing unavailable banner remains retryable. */ }
        finally { if (!cancellation.IsCancellationRequested) ViewModel.IsLoading = false; }
    }

    private async void StartDeviceLoginButton_Click(object sender, RoutedEventArgs e)
        => await ViewModel.StartDeviceLoginAsync();

    private void ShowDeviceFallbackButton_Click(object sender, RoutedEventArgs e)
        => ViewModel.ShowDeviceFallback = true;

    private void RestartDeviceLoginButton_Click(object sender, RoutedEventArgs e)
        => ViewModel.CancelDeviceLogin(clearSession: true);

    private async void OpenDeviceVerificationButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ViewModel.DeviceSession?.VerificationUri;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            await Windows.System.Launcher.LaunchUriAsync(uri);
    }


}
