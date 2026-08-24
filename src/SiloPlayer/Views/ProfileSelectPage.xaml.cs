using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class ProfileSelectPage : Page
{
    public ProfileSelectViewModel ViewModel { get; }

    public ProfileSelectPage()
    {
        ViewModel = App.Services.GetRequiredService<ProfileSelectViewModel>();
        this.InitializeComponent();

        ViewModel.ProfileSelected += OnProfileSelected;
        ViewModel.TasteSeedRequired += OnTasteSeedRequired;
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.IsLoading))
                DispatcherQueue.TryEnqueue(UpdatePageState);
        };
        ViewModel.Profiles.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdatePageState);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadProfilesCommand.ExecuteAsync(null);
        UpdatePageState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.CancelProfileLoad();
        base.OnNavigatedFrom(e);
    }

    private async void OnProfileSelected()
    {
        var mainWindow = App.MainWindowInstance;
        if (mainWindow == null)
        {
            await ShowNavigationFailureAsync(
                new InvalidOperationException("The application window is not available."));
            return;
        }

        var entered = mainWindow.TryEnterAuthenticatedPage(typeof(HomePage), null, out var failure);

        if (!entered)
            await ShowNavigationFailureAsync(failure ?? new InvalidOperationException("The requested page could not be opened."));
    }

    private void OnTasteSeedRequired()
    {
        var navigation = App.Services.GetRequiredService<NavigationService>();
        if (navigation.Frame?.Content is not HomePage) return;

        var mainWindow = App.MainWindowInstance;
        Exception? failure = null;
        if (mainWindow == null || !mainWindow.TryEnterAuthenticatedPage(typeof(TasteSeedPage), false, out failure))
            LocalLog.AppendLine("navigation_errors.txt", $"taste_seed_navigation_failed | {failure?.Message}");
    }

    private async Task ShowNavigationFailureAsync(Exception exception)
    {
        LocalLog.AppendLine(
            "navigation_errors.txt",
            $"profile_transition | {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception}");
        System.Diagnostics.Debug.WriteLine($"Profile navigation failed: {exception}");

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SiloPlayer",
            "navigation_errors.txt");
        var dialog = new ContentDialog
        {
            Title = "Unable to open Silo",
            Content = $"Your profile was selected, but the next page could not be opened. Please try again. Technical details were saved to:\n{logPath}",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void ProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Profile profile)
        {
            await ViewModel.SelectProfileCommand.ExecuteAsync(profile);
            if (ViewModel.IsPinRequired)
                await ShowPinDialogAsync(profile);
        }
    }

    private void UpdatePageState()
    {
        LoadingState.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
        ExistingProfilesState.Visibility = !ViewModel.IsLoading && ViewModel.Profiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyProfilesState.Visibility = !ViewModel.IsLoading && ViewModel.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task ShowPinDialogAsync(Profile profile)
    {
        var pinBox = new PasswordBox
        {
            PlaceholderText = "Enter 4-digit PIN",
            MaxLength = 4,
            PasswordRevealMode = PasswordRevealMode.Hidden,
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } },
        };
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "PIN", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(pinBox);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = $"Enter PIN for {profile.Name}",
            Content = content,
            PrimaryButtonText = "Confirm",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot,
        };
        pinBox.PasswordChanged += (_, _) => dialog.IsPrimaryButtonEnabled = pinBox.Password.Length > 0;
        dialog.Opened += (_, _) => pinBox.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                sender.IsPrimaryButtonEnabled = false;
                sender.PrimaryButtonText = "Verifying...";
                error.Visibility = Visibility.Collapsed;
                ViewModel.Pin = pinBox.Password;
                await ViewModel.VerifyPinCommand.ExecuteAsync(null);
                if (!ViewModel.IsPinRequired)
                {
                    sender.Hide();
                    return;
                }

                error.Text = string.IsNullOrWhiteSpace(ViewModel.PinErrorMessage)
                    ? "Verification failed"
                    : ViewModel.PinErrorMessage;
                error.Visibility = Visibility.Visible;
                pinBox.Password = "";
                pinBox.Focus(FocusState.Programmatic);
            }
            finally
            {
                sender.PrimaryButtonText = "Confirm";
                sender.IsPrimaryButtonEnabled = pinBox.Password.Length > 0;
                deferral.Complete();
            }
        };

        var result = await dialog.ShowAsync();
        if (ViewModel.IsPinRequired && result != ContentDialogResult.Primary)
            ViewModel.CancelPinCommand.Execute(null);
    }

    private void ProfileCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.RenderTransform = new TranslateTransform { Y = -4 };
            var cardBorder = FindDescendant<Border>(button, "ProfileCardBorder");
            if (cardBorder != null) cardBorder.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
        }
    }

    private void ProfileCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.RenderTransform = new TranslateTransform { Y = 0 };
            var cardBorder = FindDescendant<Border>(button, "ProfileCardBorder");
            if (cardBorder != null) cardBorder.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
        }
    }

    private async void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await Dialogs.ProfileEditorDialog.ShowWithContextAsync(XamlRoot, profile: null);
        if (result.Profile == null) return;

        await ViewModel.SelectProfileCommand.ExecuteAsync(result.Profile);
        if (!ViewModel.IsPinRequired) return;

        ViewModel.Pin = result.Pin;
        await ViewModel.VerifyPinCommand.ExecuteAsync(null);
        if (ViewModel.IsPinRequired)
        {
            App.Services.GetRequiredService<Services.ToastService>()
                .Error("Profile created, but PIN verification failed");
        }
    }

    private async void EditProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Profile profile)
        {
            await Dialogs.ProfileEditorDialog.ShowAsync(XamlRoot, profile);
            await ViewModel.LoadProfilesCommand.ExecuteAsync(null);
        }
    }

    private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Profile profile)
        {
            var error = new TextBlock
            {
                Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            var content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Delete profile \"{profile.Name}\"? This action cannot be undone.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    error,
                },
            };
            var dialog = new ContentDialog
            {
                Title = "Delete profile",
                Content = content,
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                PrimaryButtonStyle = (Style)Application.Current.Resources["DestructiveButtonStyle"],
                XamlRoot = this.XamlRoot,
            };

            dialog.PrimaryButtonClick += async (_, args) =>
            {
                args.Cancel = true;
                var deferral = args.GetDeferral();
                try
                {
                    dialog.IsPrimaryButtonEnabled = false;
                    dialog.PrimaryButtonText = "Deleting...";
                    error.Visibility = Visibility.Collapsed;
                    await ViewModel.DeleteProfileAsync(profile);
                    if (ViewModel.Profiles.All(candidate => candidate.Id != profile.Id))
                    {
                        args.Cancel = false;
                    }
                    else
                    {
                        error.Text = ViewModel.ErrorMessage ?? "The profile could not be deleted.";
                        error.Visibility = Visibility.Visible;
                        dialog.PrimaryButtonText = "Delete";
                        dialog.IsPrimaryButtonEnabled = true;
                    }
                }
                finally
                {
                    deferral.Complete();
                }
            };

            await dialog.ShowAsync();
        }
    }

    private async void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        var authService = App.Services.GetRequiredService<AuthService>();
        await authService.LogoutAsync();
    }


    /// <summary>
    /// Find a named descendant of type T within a parent element.
    /// </summary>
    private static T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && typed.Name == name)
                return typed;
            var found = FindChildByName<T>(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Find a descendant Border by x:Name within a parent element.
    /// </summary>
    private static T? FindDescendant<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && typed.Name == name)
                return typed;
            var found = FindDescendant<T>(child, name);
            if (found != null)
                return found;
        }
        return null;
    }
}
