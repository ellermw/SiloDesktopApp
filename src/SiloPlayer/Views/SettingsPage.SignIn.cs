using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Services;

namespace SiloPlayer.Views;

public sealed partial class SettingsPage
{
    private int _signInRevision;
    private CancellationTokenSource? _signInCancellation;
    private IDisposable? _signInRegistration;
    private NativeOAuthHandshake? _signInHandshake;
    private bool _signInBusy;
    private bool _signInLoaded;
    private ContentDialog? _signInDialog;
    private void InitializeAccountSignIn()
    {
        Loaded += (_, _) => SignInAuth.UserChanged += AccountSignInUserChanged;
        Unloaded += (_, _) => { SignInAuth.UserChanged -= AccountSignInUserChanged; DeactivateAccountSignIn(); };
    }
    private void AccountSignInUserChanged()
    {
        var revision = _signInRevision;
        DispatcherQueue.TryEnqueue(() =>
        {
            // A newer explicit reload already owns the page. Do not let an
            // older queued notification cancel a form started on that reload.
            if (revision == _signInRevision && AccountPasswordPanel.Visibility == Visibility.Visible)
                _ = LoadAccountSignInAsync();
        });
    }
    private AccountIdentityCollection _signInIdentities = new();
    private IReadOnlyList<AuthProvider> _signInProviders = [];
    private string? _signInMessage;
    private bool _signInMessageIsError;
    private Func<Uri, Task<bool>> _launchAccountSignIn = async uri => await Windows.System.Launcher.LaunchUriAsync(uri);
    private AuthService SignInAuth => App.Services.GetRequiredService<AuthService>();
    private SiloApiClient SignInClient => App.Services.GetRequiredService<SiloApiClient>();
    private AuthApi SignInApi => App.Services.GetRequiredService<AuthApi>();
    private bool SignInMutationAllowed => SignInAuth.CurrentUser is { } user && user.Impersonation?.Active != true &&
        !(SignInClient.AccessToken?.StartsWith("sa_", StringComparison.Ordinal) ?? false);

    private void DeactivateAccountSignIn()
    {
        ++_signInRevision;
        _signInCancellation?.Cancel(); _signInCancellation?.Dispose(); _signInCancellation = null;
        _signInHandshake?.Cancel(); _signInHandshake = null;
        _signInRegistration?.Dispose(); _signInRegistration = null;
        _signInBusy = false;
        _signInDialog?.Hide(); _signInDialog = null;
    }

    private bool CurrentSignIn(int revision, ApiRequestContext context, long generation, string? account)
        => revision == _signInRevision && SignInClient.IsCurrentContext(context) &&
           SignInAuth.SessionGeneration == generation && SignInAuth.CurrentUser?.Id == account;

    private async Task LoadAccountSignInAsync()
    {
        DeactivateAccountSignIn();
        var revision = _signInRevision; var context = SignInClient.CaptureContext();
        var generation = SignInAuth.SessionGeneration; var account = SignInAuth.CurrentUser?.Id;
        _signInCancellation = new(); var ct = _signInCancellation.Token;
        _signInLoaded = false; AccountSignInGroup.Visibility = Visibility.Visible;
        AccountSignInBody.Children.Clear();
        var progress = new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(progress, L("web.settings.accountSignIn.loading", "Loading sign-in settings"));
        AccountSignInBody.Children.Add(progress);
        try
        {
            var identities = await SignInApi.GetAccountIdentitiesAsync(context, ct);
            var providers = await SignInApi.GetAuthProvidersAsync(ct);
            if (!CurrentSignIn(revision, context, generation, account) || ct.IsCancellationRequested) return;
            _signInIdentities = identities;
            _signInProviders = providers.Where(provider => provider.InstallationId > 0 && provider.Mode is "oauth" or "credentials" or "network").ToArray();
            _signInLoaded = true;
            RenderAccountSignIn();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!CurrentSignIn(revision, context, generation, account)) return;
            AccountSignInBody.Children.Clear();
            AccountSignInBody.Children.Add(SignInText("Sign-in settings could not be loaded. Try again.", error: true));
            var retry = new Button { Content = L("common.tryAgain", "Try again") };
            retry.Click += async (_, _) => await LoadAccountSignInAsync();
            AccountSignInBody.Children.Add(retry);
        }
    }

    private static TextBlock SignInText(string text, bool error = false, double size = 14)
        => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources[error ? "ErrorBrush" : "SecondaryTextBrush"] };
    private static Border SignInSurface(StackPanel body, double padding = 12)
        => new() { Child = body, CornerRadius = new CornerRadius(10), Padding = new Thickness(padding),
            BorderThickness = new Thickness(1), BorderBrush = (Brush)Application.Current.Resources["BorderBrush"] };
    private static string IdentityName(AccountIdentity identity) => string.IsNullOrWhiteSpace(identity.ProviderName) ? L("web.settings.accountSignIn.providerTurnedOff", "Sign-in provider (turned off)") : identity.ProviderName;
    private static string SignInHistory(AccountIdentity identity)
    {
        var history = new List<string>();
        if (identity.LinkedAt is { } linked) history.Add(L("web.settings.accountSignIn.history.connected", "Connected " + DateTimeDisplay.FormatDate(linked, medium: true), Args(("date", DateTimeDisplay.FormatDate(linked, medium: true)))));
        history.Add(identity.LastSignInAt is { } signedIn ? "Last sign-in " + DateTimeDisplay.FormatDate(signedIn, medium: true) : L("web.settings.accountSignIn.history.neverUsed", "Not used to sign in yet"));
        if (identity.LastCheckedAt is { } checkedAt) history.Add("Last checked " + DateTimeDisplay.FormatDate(checkedAt, medium: true));
        return string.Join(" · ", history);
    }

    private void RenderAccountSignIn()
    {
        AccountSignInBody.Children.Clear();
        AccountSignInGroup.Visibility = _signInProviders.Count == 0 && _signInIdentities.Items.Count == 0 && _signInMessage is null ? Visibility.Collapsed : Visibility.Visible;
        if (_signInMessage is { } message) AccountSignInBody.Children.Add(SignInSurface(new StackPanel { Children = { SignInText(message, _signInMessageIsError) } }));
        foreach (var identity in _signInIdentities.Items)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(new TextBlock { Text = IdentityName(identity), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium });
            var who = string.Join(" · ", new[] { identity.Username, identity.Email }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (who.Length != 0) row.Children.Add(SignInText(who));
            row.Children.Add(SignInText(SignInHistory(identity), size: 12));
            if (!_signInIdentities.CanUnlink) row.Children.Add(SignInText(L("web.settings.accountSignIn.onlyWayIn", "This is how you sign in to this account, so it stays connected."), size: 12));
            if (SignInMutationAllowed && _signInIdentities.CanUnlink)
            {
                var disconnect = new Button { Content = L("web.settings.accountSignIn.disconnect", "Disconnect"), HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = !_signInBusy };
                disconnect.Click += async (_, _) => await DisconnectAccountIdentityAsync(identity);
                row.Children.Add(disconnect);
            }
            AccountSignInBody.Children.Add(SignInSurface(row));
        }
        if (!SignInMutationAllowed)
        {
            AccountSignInBody.Children.Add(SignInText(SignInAuth.CurrentUser?.Impersonation?.Active == true
                ? L("web.settings.accountSignIn.impersonating", "Only the account itself can change how it signs in, not while you view it as another user.")
                : L("web.settings.accountSignIn.error.permissionDenied", "This session can't change how the account signs in. Sign in to the account itself and try again.")));
            return;
        }
        var linked = _signInIdentities.Items.Select(identity => identity.InstallationId).ToHashSet();
        foreach (var provider in _signInProviders.Where(provider => !linked.Contains(provider.InstallationId)))
        {
            var button = new Button { Content = L("web.settings.accountSignIn.connectProvider", "Connect " + provider.DisplayName, Args(("provider", provider.DisplayName))), IsEnabled = !_signInBusy, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => ShowAccountConnectForm(provider);
            AccountSignInBody.Children.Add(button);
        }
    }

    private void ShowAccountConnectForm(AuthProvider provider)
    {
        if (!_signInLoaded || _signInBusy || !SignInMutationAllowed) return;
        _signInMessage = null; RenderAccountSignIn();
        var form = new StackPanel { Spacing = 16 };
        var directory = provider.Mode == "credentials";
        var network = provider.Mode == "network";
        form.Children.Add(new TextBlock { Text = L("web.settings.accountSignIn.connectProvider", "Connect " + provider.DisplayName, Args(("provider", provider.DisplayName))), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Medium });
        form.Children.Add(SignInText((network
            ? $"Confirm your Silo password to connect {(provider.NetworkIdentity?.Name is { Length: > 0 } owner ? owner + "'s " + provider.DisplayName + " account" : "the " + provider.DisplayName + " account")} on this device."
            : directory
            ? $"Confirm your Silo password, then enter your {provider.DisplayName} username and password."
            : $"Confirm your Silo password, then sign in at {provider.DisplayName}.") + $" Afterwards you sign in with {provider.DisplayName} instead of your Silo password."));
        var password = new PasswordBox { Header = L("web.settings.accountSignIn.siloPassword", "Silo password"), MaxLength = 1024 };
        form.Children.Add(password);
        var username = new TextBox { Header = L("web.settings.accountSignIn.providerUsername", provider.DisplayName + " username", Args(("provider", provider.DisplayName))), MaxLength = 256 };
        var directoryPassword = new PasswordBox { Header = L("web.settings.accountSignIn.providerPassword", provider.DisplayName + " password", Args(("provider", provider.DisplayName))), MaxLength = 1024 };
        if (directory) { form.Children.Add(username); form.Children.Add(directoryPassword); }
        var error = SignInText("", error: true); form.Children.Add(error);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var submit = new Button { Content = directory || network ? L("web.settings.accountSignIn.connect", "Connect") : "Continue to " + provider.DisplayName };
        var cancel = new Button { Content = L("common.cancel", "Cancel") };
        var cancelBrowser = new Button { Content = "Cancel sign-in", Visibility = Visibility.Collapsed };
        commands.Children.Add(submit); commands.Children.Add(cancel); commands.Children.Add(cancelBrowser); form.Children.Add(commands);
        var card = SignInSurface(form, 16); AccountSignInBody.Children.Add(card);
        cancel.Click += (_, _) => { if (!_signInBusy) RenderAccountSignIn(); };
        cancelBrowser.Click += (_, _) => { DeactivateAccountSignIn(); RenderAccountSignIn(); };
        if (!directory && !network && string.IsNullOrEmpty(provider.NativeStartPath))
        { submit.IsEnabled = false; error.Text = "This provider does not offer native sign-in at this server."; }
        submit.Click += async (_, _) =>
        {
            if (_signInBusy || !SignInMutationAllowed || !submit.IsEnabled) return;
            if (string.IsNullOrWhiteSpace(password.Password) || directory && (string.IsNullOrWhiteSpace(username.Text) || string.IsNullOrWhiteSpace(directoryPassword.Password)))
            { error.Text = "Enter the required passwords and username."; return; }
            var revision = _signInRevision; var context = SignInClient.CaptureContext();
            var generation = SignInAuth.SessionGeneration; var account = SignInAuth.CurrentUser?.Id;
            var ct = _signInCancellation?.Token ?? CancellationToken.None;
            var submittedPassword = password.Password;
            bool Current() => CurrentSignIn(revision, context, generation, account) && SignInMutationAllowed && !ct.IsCancellationRequested;
            _signInBusy = true; submit.IsEnabled = cancel.IsEnabled = false; submit.Content = L("web.settings.accountSignIn.connecting", "Connecting…"); error.Text = "";
            try
            {
                if (directory || network)
                {
                    if (network)
                        await SignInApi.LinkAccountIdentityWithNetworkAsync(context, provider.InstallationId, submittedPassword, ct);
                    else
                        await SignInApi.LinkAccountIdentityWithCredentialsAsync(context, provider.InstallationId, submittedPassword, username.Text, directoryPassword.Password, ct);
                    if (!Current()) return;
                    _signInMessage = L("web.settings.accountSignIn.connected", $"Connected {provider.DisplayName}. Sign in with it from now on.", Args(("provider", provider.DisplayName))); _signInMessageIsError = false;
                    // Start refreshes while this visit owns the callback, but
                    // never reload (and cancel) an in-flight password mutation.
                    await Task.WhenAll(LoadAccountSignInAsync(),
                        _accountPassword.IsSubmitting ? Task.CompletedTask : _accountPassword.LoadAsync());
                }
                else
                {
                    var identity = await SignInApi.GetServerIdentityAsync(context.BaseUrl, ct);
                    if (!Current()) return;
                    var ticket = await SignInApi.CreateAccountIdentityLinkTicketAsync(context, provider.InstallationId, submittedPassword, ct);
                    if (!Current()) return;
                    var attempt = new NativeOAuthHandshake(context.BaseUrl, identity.ServerId, provider.InstallationId, provider.NativeStartPath!, linkTicket: ticket.Ticket);
                    _signInHandshake = attempt;
                    _signInRegistration = NativeOAuthCallbacks.Register(async uri =>
                    {
                        if (!Current() || !ReferenceEquals(attempt, _signInHandshake) || !attempt.TryConsumeCallback(uri, out var callback)) return false;
                        try
                        {
                            if (callback.Error.Length != 0) throw new ApiException(callback.Error, ExternalSignInErrors.Describe(callback.Error, linking: true), 400);
                            await SignInApi.CompleteAccountIdentityLinkAsync(context, callback.Code, attempt.CodeVerifier, ct);
                            if (!Current()) return true;
                            _signInMessage = L("web.settings.accountSignIn.linked", "Your account is connected to the sign-in provider."); _signInMessageIsError = false;
                            await Task.WhenAll(LoadAccountSignInAsync(),
                                _accountPassword.IsSubmitting ? Task.CompletedTask : _accountPassword.LoadAsync());
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            if (Current()) { error.Text = AccountConnectError(ex, provider); _signInBusy = false; submit.IsEnabled = cancel.IsEnabled = true; submit.Content = "Continue to " + provider.DisplayName; cancelBrowser.Visibility = Visibility.Collapsed; }
                        }
                        finally
                        {
                            if (ReferenceEquals(attempt, _signInHandshake)) { _signInHandshake = null; _signInRegistration?.Dispose(); _signInRegistration = null; }
                        }
                        return true;
                    });
                    if (!await _launchAccountSignIn(attempt.StartUri)) throw new InvalidOperationException("The sign-in browser could not be opened.");
                    if (!Current()) return;
                    error.Text = "Continue connecting in your browser."; cancelBrowser.Visibility = Visibility.Visible;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (Current())
                { error.Text = AccountConnectError(ex, provider); _signInHandshake?.Cancel(); _signInHandshake = null; _signInRegistration?.Dispose(); _signInRegistration = null; }
            }
            finally
            {
                if (Current() && _signInHandshake is null)
                { _signInBusy = false; submit.IsEnabled = cancel.IsEnabled = true; submit.Content = directory || network ? L("web.settings.accountSignIn.connect", "Connect") : "Continue to " + provider.DisplayName; }
            }
        };
    }

    private static string AccountConnectError(Exception exception, AuthProvider provider)
    {
        if (exception is not ApiException problem) return L("web.settings.accountSignIn.error.unreachable", "Couldn't reach the server. Try again.");
        if (problem.StatusCode == 429) return L("web.settings.accountSignIn.error.tooManyAttempts", "Too many attempts. Wait a minute and try again.");
        return problem.ErrorCode switch
        {
            "network_identity_required" => L("web.settings.accountSignIn.error.networkIdentityRequired", $"Open this server at its {provider.DisplayName} address to connect {provider.DisplayName}.", Args(("provider", provider.DisplayName))),
            "validation_failed" when problem.ErrorLocation == "body.directory_password" => L("web.settings.accountSignIn.error.directoryRejected", $"{provider.DisplayName} didn't accept that username and password.", Args(("provider", provider.DisplayName))),
            "validation_failed" when problem.ErrorLocation == "body.password" => L("web.settings.accountSignIn.error.wrongPassword", "That isn't your current Silo password."),
            "local_password_required" => L("web.settings.accountSignIn.error.noLocalPassword", "Your account has no Silo password to confirm this with. Ask an admin to connect the provider for you."),
            "conflict" => L("web.settings.accountSignIn.error.publicAddressRequired", "Open this server at its public address to connect a sign-in provider."),
            "account_disabled" => L("web.settings.accountSignIn.error.accountDisabled", $"Your {provider.DisplayName} account is disabled.", Args(("provider", provider.DisplayName))),
            "password_expired" => L("web.settings.accountSignIn.error.passwordExpired", $"Your {provider.DisplayName} password has expired. Change it there, then try again.", Args(("provider", provider.DisplayName))),
            "permission_denied" => L("web.settings.accountSignIn.error.permissionDenied", "This session can't change how the account signs in. Sign in to the account itself and try again."),
            "not_permitted" or "provider_unavailable" or "identity_linked_elsewhere" or "already_linked" or "state_invalid" or "session_expired" => ExternalSignInErrors.Describe(problem.ErrorCode, linking: true),
            "not_found" => L("web.settings.accountSignIn.error.providerUnavailable", $"{provider.DisplayName} isn't available right now.", Args(("provider", provider.DisplayName))),
            _ => problem.Message
        };
    }

    private async Task DisconnectAccountIdentityAsync(AccountIdentity identity)
    {
        if (_signInBusy || !_signInIdentities.CanUnlink || !SignInMutationAllowed) return;
        var revision = _signInRevision; var context = SignInClient.CaptureContext();
        var generation = SignInAuth.SessionGeneration; var account = SignInAuth.CurrentUser?.Id;
        var ct = _signInCancellation?.Token ?? CancellationToken.None;
        bool Current() => CurrentSignIn(revision, context, generation, account) && SignInMutationAllowed && !ct.IsCancellationRequested;
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(SignInText(L("web.settings.accountSignIn.disconnectDescription", $"You won't be able to sign in with {IdentityName(identity)} anymore. Afterwards, sign in with your Silo password. The server only allows this while your account can still sign in another way.", Args(("provider", IdentityName(identity))))));
        var error = SignInText("", error: true); body.Children.Add(error);
        var dialog = new ContentDialog { Title = L("web.settings.accountSignIn.disconnectTitle", "Disconnect " + IdentityName(identity) + "?", Args(("provider", IdentityName(identity)))), Content = body, PrimaryButtonText = L("web.settings.accountSignIn.disconnect", "Disconnect"), CloseButtonText = L("common.cancel", "Cancel"), XamlRoot = XamlRoot };
        _signInDialog = dialog;
        dialog.Closing += (_, args) => { if (_signInBusy && Current()) args.Cancel = true; };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (_signInBusy || !Current()) return;
            var deferral = args.GetDeferral(); _signInBusy = true;
            dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = false; dialog.CloseButtonText = ""; error.Text = "";
            try
            {
                try { await SignInApi.UnlinkAccountIdentityAsync(context, identity.Id, ct); }
                catch (ApiException ex) when (ex.StatusCode == 404) { }
                if (!Current()) return;
                _signInMessage = L("web.settings.accountSignIn.disconnected", "Disconnected " + IdentityName(identity), Args(("provider", IdentityName(identity)))); _signInMessageIsError = false;
                args.Cancel = false;
                await Task.WhenAll(LoadAccountSignInAsync(),
                    _accountPassword.IsSubmitting ? Task.CompletedTask : _accountPassword.LoadAsync());
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (Current()) error.Text = ex is ApiException { ErrorCode: "last_sign_in_method" }
                    ? L("web.settings.accountSignIn.error.lastMethod", $"{IdentityName(identity)} is the only way to sign in to your account, so it can't be disconnected. Ask an admin to set a Silo password for you first.", Args(("provider", IdentityName(identity))))
                    : ex.DisplayMessage();
            }
            finally
            {
                if (revision == _signInRevision) _signInBusy = false;
                dialog.IsPrimaryButtonEnabled = true; dialog.CloseButtonText = L("common.cancel", "Cancel"); deferral.Complete();
            }
        };
        await dialog.ShowAsync();
        if (ReferenceEquals(_signInDialog, dialog)) _signInDialog = null;
    }
}
