using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class SignupPage : Page
{
    public SignupViewModel ViewModel { get; }
    private ServerEntry? _server;

    public SignupPage()
    {
        ViewModel = App.Services.GetRequiredService<SignupViewModel>();
        this.InitializeComponent();

        ViewModel.SignupSucceeded += OnSignupSucceeded;
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
        else if (e.Parameter is string serverUrl)
        {
            ViewModel.ServerUrl = serverUrl;
        }

        // Surface the selected server's display name in the card header (web parity).
        if (!string.IsNullOrWhiteSpace(ViewModel.ServerName))
        {
            ServerNameTitle.Text = ViewModel.ServerName;
        }

        await ViewModel.CheckSignupStatusCommand.ExecuteAsync(null);
    }

    private void OnSignupSucceeded(bool profileSelected)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        if (profileSelected)
        {
            App.MainWindowInstance?.ShowMainNavigation();
            App.MainWindowInstance?.NavigateToHome();
        }
        else
        {
            nav.Navigate<ProfileSelectPage>();
        }
    }

    private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
    {
        var nav = App.Services.GetRequiredService<NavigationService>();
        nav.Navigate<LoginPage>(_server ?? new ServerEntry
        {
            Url = ViewModel.ServerUrl,
            Name = ViewModel.ServerName,
        });
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.Password = PasswordBox.Password;
        UpdatePasswordMismatch();
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmPassword = ConfirmPasswordBox.Password;
        UpdatePasswordMismatch();
    }

    private void UpdatePasswordMismatch()
    {
        PasswordMismatchText.Visibility = ConfirmPasswordBox.Password.Length > 0 &&
            !string.Equals(PasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.SignupCommand.CanExecute(null))
        {
            // Sync passwords from PasswordBoxes
            ViewModel.Password = PasswordBox.Password;
            ViewModel.ConfirmPassword = ConfirmPasswordBox.Password;
            ViewModel.SignupCommand.Execute(null);
        }
    }
}
