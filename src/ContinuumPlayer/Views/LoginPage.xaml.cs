using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class LoginPage : Page
{
    public LoginViewModel ViewModel { get; }

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
        nav.Navigate<SignupPage>();
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

    private async Task ShowOAuthDialogAsync(AuthProvider provider, Uri authorizeUri)
    {
        var webView = new WebView2
        {
            Width = 840,
            Height = 680,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var dialog = new ContentDialog
        {
            Title = $"Sign in with {provider.DisplayName}",
            Content = webView,
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
            DefaultButton = ContentDialogButton.Close,
        };

        webView.NavigationStarting += async (_, args) =>
        {
            if (!TryGetOAuthCompletionCode(args.Uri, out var completionCode))
                return;

            args.Cancel = true;
            try
            {
                await ViewModel.CompleteOAuthAsync(completionCode);
                dialog.Hide();
            }
            catch
            {
                dialog.Hide();
            }
        };
        webView.NavigationCompleted += (_, args) =>
        {
            if (!args.IsSuccess)
                ViewModel.ErrorMessage = $"OAuth page failed to load: {args.WebErrorStatus}";
        };

        webView.Source = authorizeUri;
        await dialog.ShowAsync();
    }

    private static bool TryGetOAuthCompletionCode(string uriText, out string code)
    {
        code = "";
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
            return false;
        if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/login/oauth-complete", StringComparison.OrdinalIgnoreCase))
            return false;

        var query = uri.Query.TrimStart('?');
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length != 2 || !string.Equals(Uri.UnescapeDataString(pieces[0]), "code", StringComparison.Ordinal))
                continue;

            code = Uri.UnescapeDataString(pieces[1].Replace('+', ' '));
            return !string.IsNullOrWhiteSpace(code);
        }

        return false;
    }
}
