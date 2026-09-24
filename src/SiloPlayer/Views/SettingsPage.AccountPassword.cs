using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private readonly AccountPasswordViewModel _accountPassword = new(
        new AccountPasswordApi(App.Services.GetRequiredService<SiloApiClient>()));
    private bool _syncingAccountPasswords;

    private void InitializeAccountPassword()
    {
        _accountPassword.PropertyChanged += (_, args) => UpdateAccountPasswordPanel(args.PropertyName);
        // Cached pages must never retain credentials across profile departure.
        Unloaded += (_, _) => _accountPassword.Deactivate();
        UpdateAccountPasswordPanel();
    }

    private void UpdateAccountPasswordPanel(string? changedProperty = null)
    {
        _syncingAccountPasswords = true;
        try
        {
            // PasswordChanged is dispatched asynchronously by WinUI. Updating
            // one field must not erase another field's input before its event.
            if ((changedProperty is null or nameof(AccountPasswordViewModel.CurrentPassword)) &&
                AccountCurrentPassword.Password != _accountPassword.CurrentPassword)
                AccountCurrentPassword.Password = _accountPassword.CurrentPassword;
            if ((changedProperty is null or nameof(AccountPasswordViewModel.NewPassword)) &&
                AccountNewPassword.Password != _accountPassword.NewPassword)
                AccountNewPassword.Password = _accountPassword.NewPassword;
            if ((changedProperty is null or nameof(AccountPasswordViewModel.ConfirmPassword)) &&
                AccountConfirmPassword.Password != _accountPassword.ConfirmPassword)
                AccountConfirmPassword.Password = _accountPassword.ConfirmPassword;
        }
        finally { _syncingAccountPasswords = false; }

        var allowed = _accountPassword.CanChangePassword;
        var loading = _accountPassword.IsLoading;
        AccountPasswordLoading.IsActive = loading;
        AccountPasswordLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        AccountPasswordForm.Visibility = !loading && allowed ? Visibility.Visible : Visibility.Collapsed;
        AccountCurrentPassword.IsEnabled = !_accountPassword.IsSubmitting;
        AccountNewPassword.IsEnabled = !_accountPassword.IsSubmitting;
        AccountConfirmPassword.IsEnabled = !_accountPassword.IsSubmitting;
        AccountPasswordUnavailable.Visibility = !loading && !allowed && _accountPassword.ErrorMessage is null
            ? Visibility.Visible : Visibility.Collapsed;
        AccountPasswordRequirements.Text = _accountPassword.Requirements;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(AccountNewPassword, _accountPassword.Requirements);
        AccountPasswordSubmit.Content = _accountPassword.IsSubmitting ? "Changing…" : "Change password";
        AccountPasswordSubmit.IsEnabled = allowed && !_accountPassword.IsSubmitting;
        AccountPasswordMessage.Message = _accountPassword.ErrorMessage ?? _accountPassword.SuccessMessage ?? "";
        AccountPasswordMessage.Severity = _accountPassword.ErrorMessage is null ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        AccountPasswordMessage.IsOpen = !string.IsNullOrEmpty(AccountPasswordMessage.Message);
        AccountPasswordRetry.Visibility = !loading && !allowed && _accountPassword.ErrorMessage is not null
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AccountPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingAccountPasswords) return;
        if (!_accountPassword.CanChangePassword)
        {
            // A queued input event can arrive after the profile/page departed.
            if (sender is PasswordBox box && box.Password.Length > 0) box.Password = "";
            return;
        }
        // Handle only the originating field; updating all three would replace
        // still-uncommitted input during property notification synchronization.
        if (ReferenceEquals(sender, AccountCurrentPassword)) _accountPassword.CurrentPassword = AccountCurrentPassword.Password;
        else if (ReferenceEquals(sender, AccountNewPassword)) _accountPassword.NewPassword = AccountNewPassword.Password;
        else if (ReferenceEquals(sender, AccountConfirmPassword)) _accountPassword.ConfirmPassword = AccountConfirmPassword.Password;
    }

    private async void AccountPasswordSubmit_Click(object sender, RoutedEventArgs e)
        => await _accountPassword.SubmitAsync();

    private async void AccountPasswordRetry_Click(object sender, RoutedEventArgs e)
        => await _accountPassword.LoadAsync();
}
