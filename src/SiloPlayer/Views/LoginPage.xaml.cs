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

    public LoginPage()
    {
        ViewModel = App.Services.GetRequiredService<LoginViewModel>();
        this.InitializeComponent();

        ViewModel.LoginSucceeded += OnLoginSucceeded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ServerEntry server)
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

        // Load auth providers and signup status
        await ViewModel.LoadAuthInfoCommand.ExecuteAsync(null);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.CancelDeviceLogin();
        base.OnNavigatedFrom(e);
    }

    private void OnLoginSucceeded()
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<ProfileSelectPage>();
    }

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

        // Intercept the server's one-time completion code before the WebUI consumes it.
        try
        {
            btn.IsEnabled = false;
            var authorizeUri = await ViewModel.BeginOAuthAsync(provider);
            await ShowOAuthDialogAsync(provider, authorizeUri);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Couldn't start OAuth sign-in: {ex.Message}";
        }
        finally
        {
            btn.IsEnabled = true;
        }
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

    private async Task ShowOAuthDialogAsync(AuthProvider provider, Uri authorizeUri)
    {
        using var lifetimeCts = new CancellationTokenSource();
        var webView = new WebView2
        {
            Width = 840,
            Height = 620,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var progress = new ProgressRing
        {
            Width = 18,
            Height = 18,
            IsActive = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusText = new TextBlock
        {
            Text = $"Opening {provider.DisplayName}...",
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var status = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Visibility = Visibility.Visible
        };
        status.Children.Add(progress);
        status.Children.Add(statusText);
        var errorText = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(status);
        content.Children.Add(errorText);
        content.Children.Add(webView);

        var dialog = new ContentDialog
        {
            Title = $"Sign in with {provider.DisplayName}",
            Content = content,
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Close,
        };

        var completing = false;
        webView.NavigationStarting += async (_, args) =>
        {
            if (completing ||
                !OAuthCompletionUrl.TryGetCode(args.Uri, ViewModel.ServerUrl, out var completionCode))
                return;

            args.Cancel = true;
            completing = true;
            progress.IsActive = true;
            statusText.Text = "Completing sign-in...";
            status.Visibility = Visibility.Visible;
            errorText.Visibility = Visibility.Collapsed;
            try
            {
                await ViewModel.CompleteOAuthAsync(completionCode, lifetimeCts.Token);
                dialog.Hide();
            }
            catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
            {
                // The user closed the sign-in window while completion was pending.
            }
            catch (Exception ex)
            {
                errorText.Text = ViewModel.ErrorMessage ?? $"OAuth sign-in failed: {ex.Message}";
                errorText.Visibility = Visibility.Visible;
                status.Visibility = Visibility.Collapsed;
                completing = false;
            }
        };
        webView.NavigationCompleted += (_, args) =>
        {
            if (!args.IsSuccess)
            {
                var message = $"OAuth page failed to load: {args.WebErrorStatus}";
                ViewModel.ErrorMessage = message;
                errorText.Text = message;
                errorText.Visibility = Visibility.Visible;
            }

            if (!completing)
            {
                progress.IsActive = false;
                status.Visibility = Visibility.Collapsed;
            }
        };
        dialog.Closed += (_, _) => lifetimeCts.Cancel();

        webView.Source = authorizeUri;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            lifetimeCts.Cancel();
            content.Children.Remove(webView);
            webView.Close();
        }
    }

}
