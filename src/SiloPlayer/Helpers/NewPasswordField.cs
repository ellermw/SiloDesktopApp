using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

internal sealed class NewPasswordField
{
    private readonly PasswordBox _input;
    private readonly TextBlock _hint = new()
    {
        FontSize = 12, TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"]
    };
    private readonly TextBlock _error = new()
    {
        FontSize = 12, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
        Foreground = (Brush)Application.Current.Resources["ErrorBrush"]
    };

    internal NewPasswordField(StackPanel group, PasswordBox input)
    {
        _input = input;
        AutomationProperties.SetAutomationId(_hint, "NewPasswordPolicyHint");
        AutomationProperties.SetAutomationId(_error, "NewPasswordPolicyError");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        group.Children.Add(_hint);
        group.Children.Add(_error);
        input.PasswordChanged += (_, _) => ShowError(null);
    }

    internal void ShowPolicy(PasswordPolicy policy)
    {
        _hint.Text = NewPassword.Hint(policy);
        UpdateHelpText();
    }

    internal void ShowError(string? message)
    {
        _error.Text = message ?? "";
        _error.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateHelpText();
    }

    private void UpdateHelpText() => AutomationProperties.SetHelpText(_input,
        _error.Visibility == Visibility.Visible ? _error.Text : _hint.Text);
}
