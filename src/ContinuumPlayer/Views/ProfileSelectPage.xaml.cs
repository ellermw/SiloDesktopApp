using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;

namespace ContinuumPlayer.Views;

public sealed partial class ProfileSelectPage : Page
{
    public ProfileSelectViewModel ViewModel { get; }

    public ProfileSelectPage()
    {
        ViewModel = App.Services.GetRequiredService<ProfileSelectViewModel>();
        this.InitializeComponent();

        ViewModel.ProfileSelected += OnProfileSelected;
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.SelectedProfile))
            {
                PinProfileName.Text = ViewModel.SelectedProfile?.Name ?? "";
            }
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadProfilesCommand.ExecuteAsync(null);
    }

    private void OnProfileSelected()
    {
        try
        {
            // Show main navigation and go to home
            App.MainWindowInstance?.ShowMainNavigation();
            App.MainWindowInstance?.NavigateToHome();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation crash: {ex}");
            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = $"Navigation failed: {ex.Message}\n\n{ex.StackTrace}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
        }
    }

    private async void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Profile profile)
        {
            await ViewModel.SelectProfileCommand.ExecuteAsync(profile);
        }
    }

    private async void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Add Profile",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var nameBox = new TextBox
        {
            PlaceholderText = "Profile name",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        dialog.Content = nameBox;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            await ViewModel.CreateProfileCommand.ExecuteAsync(nameBox.Text.Trim());
        }
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        var authService = App.Services.GetRequiredService<AuthService>();
        authService.Logout();

        App.MainWindowInstance?.HideMainNavigation();
        var navigationService = App.Services.GetRequiredService<NavigationService>();
        navigationService.Navigate<ServerSelectPage>();
    }

    private void PinBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.VerifyPinCommand.CanExecute(null))
        {
            ViewModel.Pin = PinBox.Password;
            ViewModel.VerifyPinCommand.Execute(null);
        }
    }
}
