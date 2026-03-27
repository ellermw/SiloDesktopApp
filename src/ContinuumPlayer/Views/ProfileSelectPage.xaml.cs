using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Auth;
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
        // Show main navigation and go to home
        App.MainWindowInstance?.ShowMainNavigation();
        App.MainWindowInstance?.NavigateToHome();
    }

    private async void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Profile profile)
        {
            await ViewModel.SelectProfileCommand.ExecuteAsync(profile);
        }
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
