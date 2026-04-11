using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models;
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
        if (sender is not Button btn || btn.Tag is not string providerId || string.IsNullOrEmpty(providerId))
            return;

        // Open the provider authorize URL in the default browser. The server handles the
        // OAuth round-trip and redirects back; the user will need to copy the resulting
        // refresh token back into the app manually for now. (Proper deep-link callback
        // handling is a future task — tracked under OAuth flow completion.)
        var url = $"{ViewModel.ServerUrl.TrimEnd('/')}/api/v1/auth/providers/{Uri.EscapeDataString(providerId)}/authorize";
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Couldn't open browser: {ex.Message}";
        }
    }
}
