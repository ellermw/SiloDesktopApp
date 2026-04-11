using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using ContinuumPlayer.Core.Models.Auth;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.ViewModels;
using Windows.UI;

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

    private void ProfileCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            // Find the card border and edit/delete panel in the parent Grid
            var parentGrid = button.Parent as Grid;
            if (parentGrid != null)
            {
                // Apply hover lift effect via RenderTransform
                button.RenderTransform = new TranslateTransform { Y = -4 };

                // Change the avatar ring border to accent color
                var cardBorder = FindDescendant<Border>(button, "ProfileCardBorder");
                if (cardBorder != null)
                {
                    cardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0x78, 0xAE, 0xFC)); // primary/30
                }

                // Show edit/delete buttons
                var editPanel = FindChildByName<StackPanel>(parentGrid, "EditDeletePanel");
                if (editPanel != null)
                {
                    editPanel.Opacity = 1;
                }
            }
        }
    }

    private void ProfileCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            var parentGrid = button.Parent as Grid;
            if (parentGrid != null)
            {
                // Reset transform
                button.RenderTransform = new TranslateTransform { Y = 0 };

                // Reset border color
                var cardBorder = FindDescendant<Border>(button, "ProfileCardBorder");
                if (cardBorder != null)
                {
                    cardBorder.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
                }

                // Hide edit/delete buttons
                var editPanel = FindChildByName<StackPanel>(parentGrid, "EditDeletePanel");
                if (editPanel != null)
                {
                    editPanel.Opacity = 0;
                }
            }
        }
    }

    private void AddProfileCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.RenderTransform = new TranslateTransform { Y = -4 };
            AddProfileCardBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0x78, 0xAE, 0xFC));
        }
    }

    private void AddProfileCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.RenderTransform = new TranslateTransform { Y = 0 };
            AddProfileCardBorder.BorderBrush = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
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
                // Reload profiles after deletion
                await ViewModel.LoadProfilesCommand.ExecuteAsync(null);
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
            if (isEdit && existingProfile != null)
            {
                // Build update dict. Only include fields the user can actually change in
                // this dialog. An empty PIN means "don't change the PIN" (user can't clear
                // it from this dialog — that would need a separate "remove PIN" flow).
                var updates = new Dictionary<string, object?>
                {
                    ["name"] = nameBox.Text.Trim(),
                    ["is_child"] = kidsToggle.IsOn,
                };
                if (!string.IsNullOrEmpty(pinBox.Password))
                {
                    updates["pin"] = pinBox.Password;
                }
                await ViewModel.UpdateProfileAsync(existingProfile.Id, updates);
            }
            else
            {
                await ViewModel.CreateProfileCommand.ExecuteAsync(nameBox.Text.Trim());
            }
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
