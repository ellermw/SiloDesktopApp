using SiloPlayer.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class ChoosePasswordPage : Page
{
    private readonly AuthService _auth = App.Services.GetRequiredService<AuthService>();
    private readonly SiloApiClient _client = App.Services.GetRequiredService<SiloApiClient>();
    private readonly CancellationTokenSource _lifetime = new();
    private RequiredPasswordTransition? _flow;
    private Exception? _statusException;
    private readonly PasswordBox _temporary = new() { Header = "Temporary password" };
    private readonly PasswordBox _password = new() { Header = "New password" };
    private readonly PasswordBox _confirmation = new() { Header = "Confirm new password" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 20,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ErrorBrush"] };
    private readonly Button _save = new() { Content = "Save password", HorizontalAlignment = HorizontalAlignment.Stretch };

    public ChoosePasswordPage()
    {
        InitializeComponent();
        AppLocalization.Register(this, () =>
        {
            AppLocalization.ApplyDirection(this);
            if (_statusException is ApiException problem) _status.Text = ApiProblemText.Display(problem);
        });
        FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["ThemeFontFamily"];
        var form = new StackPanel { Spacing = 24 };
        var header = new StackPanel { Spacing = 16 };
        header.Children.Add(new TextBlock { Text = "Choose a new password", FontSize = 30,
            FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold, CharacterSpacing = -40,
            TextWrapping = TextWrapping.Wrap, LineHeight = 36, LineStackingStrategy = LineStackingStrategy.BlockLineHeight });
        header.Children.Add(new TextBlock { Text = $"An admin set a temporary password for {_auth.CurrentUser?.Username}. Choose your own to continue.",
            FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] });
        Localized.SetText(header.Children[0], "web.auth.passwordRules.chooseNewPassword");
        AppLocalization.Register((FrameworkElement)header.Children[1], () => ((TextBlock)header.Children[1]).Text = AppStrings.Get("web.auth.choosePassword.description",
            new Dictionary<string, object?> { ["username"] = _auth.CurrentUser?.Username ?? "" },
            $"An admin set a temporary password for {_auth.CurrentUser?.Username}. Choose your own to continue."));
        form.Children.Add(header);
        var content = new StackPanel { Spacing = 16 };
        var fields = new StackPanel { Spacing = 16 };
        foreach (var input in new[] { _temporary, _password, _confirmation })
        {
            input.Header = null; input.Height = 36; input.MinHeight = 36;
            input.Style = (Style)Application.Current.Resources["DarkPasswordBoxStyle"];
            input.CornerRadius = new CornerRadius(8);
        }
        StackPanel PasswordGroup(string key, string label, PasswordBox input)
        {
            var group = new StackPanel { Spacing = 8 };
            group.Children.Add(new TextBlock { Text = label, FontSize = 14, Height = 14, LineHeight = 14,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = Microsoft.UI.Text.FontWeights.Medium });
            if (ReferenceEquals(input, _password)) group.Children.Add(new TextBlock { Text = "At least 8 characters", FontSize = 12,
                Height = 16, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] });
            Localized.SetText(group.Children[0], key);
            if (ReferenceEquals(input, _password)) Localized.SetText(group.Children[1], "web.auth.passwordRules.minLength");
            group.Children.Add(PasswordReveal.Wrap(input)); return group;
        }
        fields.Children.Add(PasswordGroup("web.auth.choosePassword.temporaryPassword", "Temporary password", _temporary));
        fields.Children.Add(PasswordGroup("web.auth.passwordRules.newPassword", "New password", _password));
        fields.Children.Add(PasswordGroup("web.auth.passwordRules.confirmNewPassword", "Confirm new password", _confirmation));
        _status.Visibility = Visibility.Collapsed; content.Children.Add(_status);
        _save.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _save.Height = 36; _save.MinHeight = 36; _save.Padding = new Thickness(12, 4, 12, 4);
        fields.Children.Add(_save); content.Children.Add(fields);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Height = 20, HorizontalAlignment = HorizontalAlignment.Center };
        footer.Children.Add(new TextBlock { Text = "Not you?", FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SecondaryTextBrush"] });
        var signOut = new HyperlinkButton { Content = new TextBlock { Text = "Sign out", FontSize = 14,
            TextDecorations = Windows.UI.Text.TextDecorations.Underline, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PrimaryTextBrush"] },
            FontSize = 14, Padding = new Thickness(0), MinHeight = 0, MinWidth = 0, Height = 20 };
        Localized.SetText(footer.Children[0], "web.auth.choosePassword.notYou");
        ((TextBlock)footer.Children[0]).Visibility = Visibility.Collapsed;
        Localized.SetText((TextBlock)signOut.Content, "web.auth.choosePassword.notYou");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(signOut, "Sign out");
        Localized.SetName(signOut, "common.signOut");
        signOut.Click += async (_, _) => { await _auth.LogoutAsync(); App.Services.GetRequiredService<NavigationService>().Navigate<LoginPage>(); };
        footer.Children.Add(signOut); content.Children.Add(footer); form.Children.Add(content);
        Localized.SetContent(_save, "web.auth.passwordRules.savePassword");
        _save.Click += Save_Click;
        _temporary.Loaded += (_, _) => _temporary.Focus(FocusState.Programmatic);
        Content = AuthFormLayout.Create(form, showBrand: false, maxWidth: 384, contentInset: 24, radius: 12, verticalPadding: 24, borderThickness: 0);
    }
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (!_auth.PasswordChangeRequired)
        { App.Services.GetRequiredService<NavigationService>().Navigate<LoginPage>(); return; }
        _flow = new(_auth, _client);
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_flow == null || !_save.IsEnabled) return;
        _save.IsEnabled = false; Localized.SetContent(_save, "web.ui.saving");
        foreach (var input in new[] { _temporary, _password, _confirmation }) input.IsEnabled = false;
        _statusException = null; _status.Text = ""; _status.Visibility = Visibility.Collapsed;
        try
        {
            if (await _flow.SaveAsync(_temporary.Password, _password.Password, _confirmation.Password, _lifetime.Token))
                App.Services.GetRequiredService<NavigationService>().Navigate<ProfileSelectPage>();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_lifetime.IsCancellationRequested) { _statusException = ex; _status.Text = ex is ApiException problem ? ApiProblemText.Display(problem) : ex.Message; _status.Visibility = Visibility.Visible; } }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                _save.IsEnabled = true; Localized.SetContent(_save, "web.auth.passwordRules.savePassword");
                foreach (var input in new[] { _temporary, _password, _confirmation }) input.IsEnabled = true;
                if (_flow.PasswordSaved)
                {
                    _temporary.Password = _password.Password = _confirmation.Password = "";
                    _temporary.Visibility = _password.Visibility = _confirmation.Visibility = Visibility.Collapsed;
                    foreach (var input in new[] { _temporary, _password, _confirmation })
                        if (input.Parent is FrameworkElement surface && surface.Parent is FrameworkElement passwordGroup) passwordGroup.Visibility = Visibility.Collapsed;
                    Localized.SetContent(_save, null); _save.Content = "Retry sign-in";
                }
            }
        }
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    { _lifetime.Cancel(); base.OnNavigatedFrom(e); }
}
