using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

public sealed partial class PasswordRecoveryPage : Page
{
    public sealed record RecoveryNavigation(string Link, long SessionGeneration);
    private readonly AuthService _auth = App.Services.GetRequiredService<AuthService>();
    private readonly PasswordRecovery _flow;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TextBox _login = new();
    // Link state also receives the selected-server activation parameter.
    private readonly TextBox _link = new();
    private readonly PasswordBox _password = new();
    private readonly PasswordBox _confirmation = new();
    private readonly TextBlock _status = Copy("");
    private readonly TextBlock _description = Copy("");
    private readonly TextBlock _eyebrow = Copy("");
    private readonly TextBlock _title = new() { Text = "Reset password", FontSize = 30, FontWeight = FontWeights.ExtraBold,
        CharacterSpacing = -40, TextWrapping = TextWrapping.Wrap, LineHeight = 36, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly Button _request = Action("Send reset link");
    private readonly Button _lookup = Action("Reload link");
    private readonly Button _save = Action("Save password");
    private readonly Button _retryAvailability = Action("Try again");
    private readonly Button _signOut = Action("Sign out");
    private readonly Button _signIn = Action("Back to sign in");
    private readonly StackPanel _requestForm = new() { Spacing = 16 };
    private readonly StackPanel _resetForm = new() { Spacing = 16 };
    private readonly StackPanel _content = new() { Spacing = 16 };
    private readonly StackPanel _hint = new() { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Height = 20 };
    private readonly ProgressRing _loading = new() { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private bool _busy;
    private bool _requestAvailable;
    private string _state = "loading";

    public PasswordRecoveryPage()
    {
        InitializeComponent();
        FontFamily = (FontFamily)Application.Current.Resources["ThemeFontFamily"];
        _flow = new(_auth, App.Services.GetRequiredService<SiloApiClient>());
        var form = new StackPanel { Spacing = 24 };
        var header = new StackPanel { Spacing = 8 };
        _eyebrow.FontFamily = new FontFamily("Cascadia Mono"); _eyebrow.FontSize = 11;
        _eyebrow.FontWeight = FontWeights.SemiBold; _eyebrow.CharacterSpacing = 100; _eyebrow.Height = _eyebrow.LineHeight = 16.5;
        _eyebrow.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        _eyebrow.Visibility = Visibility.Collapsed;
        _description.LineHeight = 24; _description.Foreground = (Brush)Application.Current.Resources["SecondaryTextBrush"];
        _description.Margin = new Thickness(0, 8, 0, 0);
        header.Children.Add(_eyebrow); header.Children.Add(_title); header.Children.Add(_description);
        form.Children.Add(header);
        _login.Style = (Style)Application.Current.Resources["DarkTextBoxStyle"];
        _login.Height = _login.MinHeight = 36; _login.CornerRadius = new CornerRadius(8);
        _requestForm.Children.Add(Field("Username or email", _login)); _requestForm.Children.Add(_request);
        foreach (var input in new[] { _password, _confirmation })
        { input.Style = (Style)Application.Current.Resources["DarkPasswordBoxStyle"]; input.Height = input.MinHeight = 36; input.CornerRadius = new CornerRadius(8); }
        _resetForm.Children.Add(Field("New password", PasswordReveal.Wrap(_password), "At least 8 characters"));
        _resetForm.Children.Add(Field("Confirm new password", PasswordReveal.Wrap(_confirmation))); _resetForm.Children.Add(_save);
        _hint.Children.Add(Copy("Remembered it?", muted: true));
        var hintLink = new HyperlinkButton { Content = new TextBlock { Text = "Sign in", FontSize = 14,
            TextDecorations = Windows.UI.Text.TextDecorations.Underline, Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"] },
            Padding = new Thickness(0), MinHeight = 0, MinWidth = 0, Height = 20 };
        hintLink.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<LoginPage>(); _hint.Children.Add(hintLink);
        _content.Children.Add(_status); _content.Children.Add(_lookup); _content.Children.Add(_requestForm); _content.Children.Add(_resetForm);
        _content.Children.Add(_retryAvailability); _content.Children.Add(_signOut); _content.Children.Add(_signIn); _content.Children.Add(_hint);
        form.Children.Add(_content);
        _request.Click += async (_, _) => await RequestAsync();
        _lookup.Click += async (_, _) => await LookupAsync();
        _save.Click += async (_, _) => await SaveAsync();
        _retryAvailability.Click += async (_, _) => await AvailabilityAsync();
        _signIn.Click += (_, _) => BackToSignIn();
        _signOut.Click += async (_, _) =>
        {
            if (_busy) return;
            var link = _link.Text;
            await _auth.LogoutAsync();
            App.Services.GetRequiredService<NavigationService>().Navigate<PasswordRecoveryPage>(new RecoveryNavigation(link, _auth.SessionGeneration));
        };
        _password.Loaded += (_, _) => { if (_state == "reset") _password.Focus(FocusState.Programmatic); };
        _login.Loaded += (_, _) => { if (_state == "request") _login.Focus(FocusState.Programmatic); };
        Content = AuthFormLayout.Create(form, showBrand: false, maxWidth: 384, contentInset: 24, radius: 12, verticalPadding: 24, borderThickness: 0);
        ((Grid)Content).Children.Add(_loading);
        Render("loading", "Reset password", "");
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var link = e.Parameter switch { string value => value, RecoveryNavigation value => value.Link, _ => null };
        if (!string.IsNullOrWhiteSpace(link)) _link.Text = link;
        if (_auth.IsLoggedIn)
        {
            Render("signed-in", "Reset password", "");
            _status.Text = $"You're signed in as {_auth.CurrentUser?.Username}. Sign out to use this reset link.";
            _status.Visibility = Visibility.Visible;
            return;
        }
        if (!string.IsNullOrWhiteSpace(_link.Text)) await LookupAsync();
        else await AvailabilityAsync();
    }

    private void Render(string state, string title, string description, string? server = null)
    {
        _state = state;
        if (Content is Grid shell) shell.Children.OfType<ScrollViewer>().Single().Visibility = state == "loading" ? Visibility.Collapsed : Visibility.Visible;
        _loading.IsActive = state == "loading"; _loading.Visibility = state == "loading" ? Visibility.Visible : Visibility.Collapsed;
        _title.Text = title; _description.Text = description;
        _description.Visibility = description.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _eyebrow.Text = server?.ToUpperInvariant() ?? ""; _eyebrow.Visibility = string.IsNullOrEmpty(server) ? Visibility.Collapsed : Visibility.Visible;
        _status.Text = ""; _status.Visibility = Visibility.Collapsed; _status.Foreground = (Brush)Application.Current.Resources["PrimaryTextBrush"];
        _requestForm.Visibility = state == "request" ? Visibility.Visible : Visibility.Collapsed;
        _resetForm.Visibility = _save.Visibility = _password.Visibility = _confirmation.Visibility = state == "reset" ? Visibility.Visible : Visibility.Collapsed;
        _login.Visibility = _request.Visibility = state == "request" ? Visibility.Visible : Visibility.Collapsed;
        _lookup.Visibility = state == "lookup-failed" || state == "reset" && _flow.NeedsReload ? Visibility.Visible : Visibility.Collapsed;
        _retryAvailability.Visibility = state == "availability-failed" ? Visibility.Visible : Visibility.Collapsed;
        _signOut.Visibility = state == "signed-in" ? Visibility.Visible : Visibility.Collapsed;
        _signIn.Visibility = (state is "sent" or "completed") || state == "link-unavailable" && _requestAvailable ? Visibility.Visible : Visibility.Collapsed;
        _signIn.Content = state == "completed" ? "Sign in" : state == "link-unavailable" ? "Request a new link" : "Back to sign in";
        _hint.Visibility = state is "request" or "reset" or "availability-failed" or "unavailable" or "lookup-failed" or "link-unavailable" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task AvailabilityAsync()
    {
        if (_busy) return;
        _busy = true; Render("loading", "Reset password", "");
        try
        {
            _requestAvailable = await _flow.CanRequestAsync(_lifetime.Token);
            var branding = await App.Services.GetRequiredService<SettingsApi>().GetServerBrandingAsync(_lifetime.Token);
            Render(_requestAvailable ? "request" : "unavailable", "Reset password", _requestAvailable
                ? "Enter your username or email. If it matches an account, we'll email that account a link to choose a new password."
                : "This server doesn't offer password resets from the sign-in page. Ask your admin to reset your password.", branding.ServerName);
            if (_requestAvailable) _login.Focus(FocusState.Programmatic);
        }
        catch (OperationCanceledException) { }
        catch { if (!_lifetime.IsCancellationRequested) Render("availability-failed", "Reset password", "The server could not be reached. Try again."); }
        finally { _busy = false; }
    }

    private async Task RequestAsync()
    {
        if (_busy || !_requestAvailable || string.IsNullOrWhiteSpace(_login.Text)) return;
        _busy = true; _request.IsEnabled = _login.IsEnabled = false; _request.Content = "Sending..."; ClearError();
        try
        {
            var login = _login.Text.Trim(); await _flow.RequestAsync(login, _lifetime.Token);
            var server = _eyebrow.Text; Render("sent", "Check your email", "", server);
            _status.Text = $"If {login} matches an account with an email address, we sent that address a link to choose a new password.\n\nNothing after a few minutes? Check your spam folder, then try again.";
            _status.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex) when (ex.StatusCode == 409)
        { _requestAvailable = false; Render("unavailable", "Reset password", "This server doesn't offer password resets from the sign-in page. Ask your admin to reset your password.", _eyebrow.Text); }
        catch (ApiException ex) when (ex.StatusCode == 429) { Error("Too many requests from this network. Wait a minute, then try again."); }
        catch { Error("Could not send the request. Try again."); }
        finally { _busy = false; _request.IsEnabled = _login.IsEnabled = true; _request.Content = "Send reset link"; }
    }

    private async Task LookupAsync()
    {
        if (_busy) return;
        _busy = true; _lookup.IsEnabled = false;
        try
        {
            var result = await _flow.LookupAsync(_link.Text, _lifetime.Token);
            var expires = DateTimeOffset.Parse(result.ExpiresAt).ToLocalTime();
            var expiry = expires.ToString("d") + ", " + expires.ToString("T");
            Render("reset", "Choose a new password", $"For {result.Username}. The link expires {expiry}. Saving signs this account out on every device.", result.ServerName);
            _password.Focus(FocusState.Programmatic);
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex) when (ex.StatusCode == 404) { await LinkUnavailableAsync(); }
        catch { if (!_lifetime.IsCancellationRequested) Render("lookup-failed", "Could not load link", "The server could not confirm this link. Try loading it again."); }
        finally { _busy = false; _lookup.IsEnabled = true; _save.IsEnabled = !_flow.NeedsReload; }
    }

    private async Task SaveAsync()
    {
        if (_busy || _flow.NeedsReload) return;
        _busy = true; _save.IsEnabled = _password.IsEnabled = _confirmation.IsEnabled = false; _save.Content = "Saving..."; ClearError();
        try
        {
            var result = await _flow.CompleteAsync(_link.Text, _password.Password, _confirmation.Password, _lifetime.Token);
            _password.Password = _confirmation.Password = "";
            if (result.LoginStatus == "signed_in")
            {
                var nav = App.Services.GetRequiredService<NavigationService>();
                if (_auth.PasswordChangeRequired) nav.Navigate<ChoosePasswordPage>(); else nav.Navigate<ProfileSelectPage>();
            }
            else { Render("completed", "Password changed", ""); _status.Text = $"Sign in as {result.Username} with your new password."; _status.Visibility = Visibility.Visible; }
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex) when (ex.StatusCode == 404) { await LinkUnavailableAsync(); }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                Error(_flow.NeedsReload ? "We could not confirm the result. Your password may have changed. Try signing in with the new password, or reload this link before trying again." : ex.Message);
                _lookup.Visibility = _flow.NeedsReload ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        finally
        {
            _busy = false; _save.IsEnabled = !_flow.NeedsReload; _password.IsEnabled = _confirmation.IsEnabled = true;
            _save.Content = "Save password";
        }
    }
    private async Task LinkUnavailableAsync()
    {
        try { _requestAvailable = await _flow.CanRequestAsync(_lifetime.Token); } catch { _requestAvailable = false; }
        Render("link-unavailable", "Link unavailable", "This link was already used, replaced by a newer one, or expired. " + (_requestAvailable ? "Request a new link, or ask your admin for one." : "Ask your admin for a new link."));
    }
    private void BackToSignIn()
    {
        var navigation = App.Services.GetRequiredService<NavigationService>();
        if (_state == "link-unavailable") navigation.Navigate<PasswordRecoveryPage>(); else navigation.Navigate<LoginPage>();
    }
    private void ClearError() { _status.Text = ""; _status.Visibility = Visibility.Collapsed; }
    private void Error(string text) { if (_lifetime.IsCancellationRequested) return; _status.Text = text; _status.Visibility = Visibility.Visible; _status.Foreground = (Brush)Application.Current.Resources["ErrorBrush"]; }
    private static TextBlock Copy(string text, bool muted = false) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14,
        LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        Foreground = (Brush)Application.Current.Resources[muted ? "SecondaryTextBrush" : "PrimaryTextBrush"] };
    private static Button Action(string title) => new() { Content = title, Height = 36, MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch,
        Padding = new Thickness(12, 4, 12, 4), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
    private static StackPanel Field(string label, FrameworkElement input, string? hint = null)
    {
        var group = new StackPanel { Spacing = 8 };
        group.Children.Add(new TextBlock { Text = label, FontSize = 14, Height = 14, LineHeight = 14,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontWeight = FontWeights.Medium });
        if (hint != null) { var copy = Copy(hint, muted: true); copy.FontSize = 12; copy.Height = copy.LineHeight = 16; group.Children.Add(copy); }
        group.Children.Add(input); return group;
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { _lifetime.Cancel(); base.OnNavigatedFrom(e); }
}




