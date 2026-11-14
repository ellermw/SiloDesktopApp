using SiloPlayer.Core.Localization;
using SiloPlayer.Services;
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
        AppLocalization.Register(this, () => { AppLocalization.ApplyDirection(this); ViewModel.RefreshLocalizedText(); });
        SizeChanged += (_, args) =>
        {
            ProfilePickerTitle.FontSize = Math.Clamp(args.NewSize.Width * .07, 35.2, 72);
            EmptyProfilesTitle.FontSize = Math.Clamp(args.NewSize.Width * .06, 33.6, 64);
            foreach (var title in new[] { ProfilePickerTitle, EmptyProfilesTitle })
            {
                title.LineHeight = title.FontSize * .95;
                title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                title.CharacterSpacing = -50;
            }
        };

        ViewModel.ProfileSelected += OnProfileSelected;
        ViewModel.TasteSeedRequired += OnTasteSeedRequired;
        ViewModel.ManageProfilesSelected += OnManageProfilesSelected;
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
        if (App.Services.GetRequiredService<AuthService>().PasswordChangeRequired)
        { App.Services.GetRequiredService<NavigationService>().Navigate<ChoosePasswordPage>(); return; }
        await ViewModel.LoadProfilesCommand.ExecuteAsync(null);
        UpdatePageState();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.CancelProfileLoad();
        _activePinDialog?.Hide();
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
        => await ShowPinDialogAsync(profile, null);

    private async Task ShowPinDialogAsync(Profile profile, string? purpose)
    {
        var pinBox = new PasswordBox
        {
            PlaceholderText = AppStrings.Get("web.profiles.pin.placeholder", fallback: "Enter 4-digit PIN"),
            MaxLength = 4,
            PasswordRevealMode = PasswordRevealMode.Hidden,
            InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.NumericPin) } },
        };
        var error = new TextBlock
        {
            Foreground = (Brush)Application.Current.Resources["ErrorBrush"],
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var content = new StackPanel { Spacing = 8 };
        TextBlock? purposeText = null;
        if (purpose != null)
        {
            purposeText = new TextBlock { Text = purpose == "add" ? "Adding a profile needs the primary profile's PIN." : "Managing profiles needs the primary profile's PIN.", TextWrapping = TextWrapping.Wrap };
            Localized.SetText(purposeText, purpose == "add" ? "web.profiles.picker.addNeedsPin" : "web.profiles.picker.manageNeedsPin");
            content.Children.Add(purposeText);
        }
        var pinLabel = new TextBlock { Text = "PIN", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Localized.SetText(pinLabel, "web.profiles.pin.label");
        content.Children.Add(pinLabel);
        content.Children.Add(pinBox);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            Title = $"Enter PIN for {profile.Name}",
            Content = content,
            PrimaryButtonText = AppStrings.Get("web.profiles.pin.confirm"),
            CloseButtonText = AppStrings.Get("common.cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot,
        };
        var verificationPending = false;
        Localized.SetPlaceholder(pinBox, "web.profiles.pin.placeholder");
        AppLocalization.Register(dialog, () =>
        {
            AppLocalization.ApplyDirection(dialog);
            pinBox.PlaceholderText = AppStrings.Get("web.profiles.pin.placeholder");
            pinLabel.Text = AppStrings.Get("web.profiles.pin.label");
            if (purposeText != null) purposeText.Text = AppStrings.Get(purpose == "add" ? "web.profiles.picker.addNeedsPin" : "web.profiles.picker.manageNeedsPin");
            dialog.Title = AppStrings.Get("web.profiles.pin.title", new Dictionary<string, object?> { ["name"] = profile.Name }, $"Enter PIN for {profile.Name}");
            dialog.PrimaryButtonText = AppStrings.Get(verificationPending ? "web.profiles.pin.verifying" : "web.profiles.pin.confirm");
            dialog.CloseButtonText = AppStrings.Get("common.cancel");
            ViewModel.RefreshLocalizedText();
            if (error.Visibility == Visibility.Visible) error.Text = ViewModel.PinErrorMessage ?? AppStrings.Get("web.profiles.pin.verifyFailed");
        });
        pinBox.PasswordChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !verificationPending && pinBox.Password.Length > 0;
        dialog.Opened += (_, _) => pinBox.Focus(FocusState.Programmatic);
        // Return the canceled primary-click event without a WinUI deferral so
        // Cancel remains usable while verification is pending.
        dialog.CloseButtonClick += (_, _) => ViewModel.CancelPinCommand.Execute(null);
        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            args.Cancel = true;
            if (verificationPending) return;
            verificationPending = true;
            try
            {
                sender.IsPrimaryButtonEnabled = false;
                sender.PrimaryButtonText = AppStrings.Get("web.profiles.pin.verifying", fallback: "Verifying...");
                error.Visibility = Visibility.Collapsed;
                ViewModel.Pin = pinBox.Password;
                await ViewModel.VerifyPinCommand.ExecuteAsync(null);
                if (!ViewModel.IsPinRequired)
                {
                    sender.Hide();
                    return;
                }

                error.Text = string.IsNullOrWhiteSpace(ViewModel.PinErrorMessage)
                    ? AppStrings.Get("web.profiles.pin.verifyFailed", fallback: "Verification failed")
                    : ViewModel.PinErrorMessage;
                error.Visibility = Visibility.Visible;
                pinBox.Password = "";
                pinBox.Focus(FocusState.Programmatic);
            }
            finally
            {
                verificationPending = false;
                sender.PrimaryButtonText = AppStrings.Get("web.profiles.pin.confirm");
                sender.IsPrimaryButtonEnabled = pinBox.Password.Length > 0;
            }
        };

        _activePinDialog = dialog;
        var result = await dialog.ShowAsync();
        if (ReferenceEquals(_activePinDialog, dialog)) _activePinDialog = null;
        if (ViewModel.IsPinRequired && result != ContentDialogResult.Primary)
            ViewModel.CancelPinCommand.Execute(null);
    }

    private void ProfileCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.RenderTransform = new TranslateTransform { Y = -4 };
            var cardBorder = FindDescendant<Border>(button, "ProfileCardBorder") ?? FindDescendant<Border>(button, "AddProfileCardBorder");
            if (cardBorder != null) cardBorder.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
        }
    }

    private void ProfileCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.RenderTransform = new TranslateTransform { Y = 0 };
            var cardBorder = FindDescendant<Border>(button, "ProfileCardBorder") ?? FindDescendant<Border>(button, "AddProfileCardBorder");
            if (cardBorder != null) cardBorder.BorderBrush = (Brush)Application.Current.Resources["BorderBrush"];
        }
    }

    private async void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel.Profiles.Count > 0) { await StartManagementAsync(true); return; }
            var result = await Dialogs.ProfileEditorDialog.ShowWithContextAsync(XamlRoot, profile: null);
            if (result.Profile == null) { await ViewModel.LoadProfilesCommand.ExecuteAsync(null); return; }

            await ViewModel.SelectProfileCommand.ExecuteAsync(result.Profile);
            if (!ViewModel.IsPinRequired) return;

            ViewModel.Pin = result.Pin;
            await ViewModel.VerifyPinCommand.ExecuteAsync(null);
            if (ViewModel.IsPinRequired)
            {
                App.Services.GetRequiredService<Services.ToastService>()
                    .Error(ViewModel.IsPinLockedOut
                        ? AppStrings.Get("web.profiles.picker.createdLockout", new Dictionary<string, object?> { ["lockout"] = ViewModel.PinErrorMessage }, $"Profile created. {ViewModel.PinErrorMessage}") : AppStrings.Get("web.profiles.picker.createdPinFailed", fallback: "Profile created, but PIN verification failed"));
            }
        }
        catch (Exception ex) { ViewModel.ErrorMessage = $"Couldn't open the new profile form: {ex.Message}"; }
    }

    private async void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelProfileLoad(); _activePinDialog?.Hide();
        var authService = App.Services.GetRequiredService<AuthService>();
        await authService.LogoutAsync();
    }

    private ContentDialog? _activePinDialog;

    private async void ManageProfilesButton_Click(object sender, RoutedEventArgs e) => await StartManagementAsync(false);

    private async Task StartManagementAsync(bool adding)
    {
        if (ViewModel.IsManagementBusy) return;
        await ViewModel.BeginManagementAsync(adding);
        if (ViewModel.IsPinRequired && ViewModel.PinProfile is { } primary)
            await ShowPinDialogAsync(primary, adding ? "add" : "manage");
        if (adding && ViewModel.AddSession is { } session && session.IsCurrent)
        {
            try
            {
                var result = await Dialogs.ProfileEditorDialog.ShowForPickerAsync(XamlRoot, session);
                await ViewModel.FinishAddAsync(session, result.Profile != null);
            }
            catch (OperationCanceledException) when (!session.IsCurrent) { }
            catch (Exception)
            {
                if (session.IsCurrent) ViewModel.ErrorMessage = AppStrings.Get("web.profiles.picker.addFormFailed", fallback: "Couldn't open the new profile form. Try again.");
            }
            finally
            {
                if (ReferenceEquals(ViewModel.AddSession, session)) ViewModel.CancelManagement();
            }
        }
    }

    private async void OnManageProfilesSelected()
    {
        var mainWindow = App.MainWindowInstance;
        Exception? failure = null;
        if (mainWindow == null || !mainWindow.TryEnterAuthenticatedPage(typeof(SettingsPage), "Profiles", out failure))
            await ShowNavigationFailureAsync(failure ?? new InvalidOperationException("The application window is not available."));
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
