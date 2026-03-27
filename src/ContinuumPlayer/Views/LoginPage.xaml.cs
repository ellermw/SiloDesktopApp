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

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ServerEntry server)
        {
            ViewModel.ServerUrl = server.Url;
            ViewModel.ServerName = server.Name;
        }
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

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.LoginCommand.CanExecute(null))
        {
            // Sync password from PasswordBox since PasswordBox two-way binding needs explicit sync
            ViewModel.Password = PasswordBox.Password;
            ViewModel.LoginCommand.Execute(null);
        }
    }
}
