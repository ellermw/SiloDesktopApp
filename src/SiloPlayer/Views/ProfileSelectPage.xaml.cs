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
        while (ViewModel.IsPinRequired)
        {
            var pinBox = new PasswordBox { PlaceholderText = "Enter 4-digit PIN", MaxLength = 4, PasswordRevealMode = PasswordRevealMode.Hidden };
            var error = new TextBlock { Text = ViewModel.PinErrorMessage ?? "", Foreground = (Brush)Application.Current.Resources["ErrorBrush"], TextWrapping = TextWrapping.Wrap };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = $"Enter the PIN for {profile.Name}.", Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"] });
            content.Children.Add(pinBox);
            content.Children.Add(error);
            var dialog = new ContentDialog
            {
                Title = profile.Name,
                Content = content,
                PrimaryButtonText = "Confirm",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                ViewModel.CancelPinCommand.Execute(null);
                return;
            }
            ViewModel.Pin = pinBox.Password;
            await ViewModel.VerifyPinCommand.ExecuteAsync(null);
        }
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
        await ShowProfileFormDialog(null);
    }

    private async void EditProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Profile profile)
        {
            await ShowProfileFormDialog(profile);
        }
    }

    private async void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is Profile profile)
        {
            var dialog = new ContentDialog
            {
                Title = "Delete profile",
                Content = $"Delete profile \"{profile.Name}\"? This action cannot be undone.",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteProfileAsync(profile);
            }
        }
    }

    private async Task ShowProfileFormDialog(Profile? existingProfile)
    {
        var isEdit = existingProfile != null;
        var dialog = new ContentDialog
        {
            Title = isEdit ? "Edit Profile" : "New Profile",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        // Build form content
        var formPanel = new StackPanel { Spacing = 16 };

        // Name field
        var nameLabel = new TextBlock
        {
            Text = "Name",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        };
        var nameBox = new TextBox
        {
            PlaceholderText = "Profile name",
            Text = existingProfile?.Name ?? "",
            Style = (Style)Application.Current.Resources["DarkTextBoxStyle"]
        };
        var nameGroup = new StackPanel { Spacing = 6 };
        nameGroup.Children.Add(nameLabel);
        nameGroup.Children.Add(nameBox);
        formPanel.Children.Add(nameGroup);

        // PIN field
        var pinLabel = new TextBlock
        {
            Text = "PIN (optional)",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = (SolidColorBrush)Application.Current.Resources["PrimaryTextBrush"]
        };
        var pinBox = new PasswordBox
        {
            PlaceholderText = "4 digits",
            MaxLength = 4,
            Style = (Style)Application.Current.Resources["DarkPasswordBoxStyle"]
        };
        var pinGroup = new StackPanel { Spacing = 6 };
        pinGroup.Children.Add(pinLabel);
        pinGroup.Children.Add(pinBox);
        formPanel.Children.Add(pinGroup);

        // Kids profile toggle
        var kidsToggle = new ToggleSwitch
        {
            Header = "Kids profile",
            IsOn = existingProfile?.IsChild ?? false,
            OnContent = "Yes",
            OffContent = "No"
        };
        formPanel.Children.Add(kidsToggle);

        dialog.Content = formPanel;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
        {
            var pin = pinBox.Password;
            if (!string.IsNullOrEmpty(pin) &&
                (pin.Length != 4 || pin.Any(c => !char.IsDigit(c))))
            {
                ViewModel.ErrorMessage = "PIN must contain exactly 4 digits.";
                return;
            }

            if (isEdit && existingProfile != null)
            {
                // Build update dict. Only include fields the user can actually change in
                // this dialog. An empty PIN means "don't change the PIN" (user can't clear
                // it from this dialog — that would need a separate "remove PIN" flow).
                var updates = new Dictionary<string, object?>
                {
                    ["name"] = nameBox.Text.Trim(),
                };
                var authService = App.Services.GetRequiredService<AuthService>();
                if (string.Equals(authService.CurrentUser?.Role, "admin", StringComparison.OrdinalIgnoreCase))
                    updates["is_child"] = kidsToggle.IsOn;
                if (!string.IsNullOrEmpty(pin))
                {
                    updates["pin"] = pin;
                }
                await ViewModel.UpdateProfileAsync(existingProfile.Id, updates);
            }
            else
            {
                await ViewModel.CreateProfileCommand.ExecuteAsync(new CreateProfileRequest
                {
                    Name = nameBox.Text.Trim(),
                    Pin = string.IsNullOrEmpty(pin) ? null : pin,
                    IsChild = kidsToggle.IsOn,
                });
            }
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
