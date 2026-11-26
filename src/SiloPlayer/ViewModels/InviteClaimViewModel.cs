using SiloPlayer.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.ViewModels;

public partial class InviteClaimViewModel(
    AuthApi authApi,
    AuthService authService,
    SettingsService settingsService,
    SettingsApi settingsApi,
    SiloApiClient apiClient) : ObservableObject
{
    private CancellationTokenSource? _lifetime;
    private InviteClaimNavigation? _navigation;
    // The transport the lookup established: a server that advertises token_in_body only ever sees the token in a body.
    private bool _tokenInBody;

    private PasswordPolicy _passwordPolicy = PasswordPolicy.Default;
    [ObservableProperty] private string? _passwordError;

    public string PasswordHint => NewPassword.Hint(_passwordPolicy);
    public void RefreshPasswordPolicyText() => OnPropertyChanged(nameof(PasswordHint));
    partial void OnPasswordChanged(string value) => PasswordError = null;
    partial void OnConfirmPasswordChanged(string value) => PasswordError = null;

    private async Task RefreshPasswordPolicyAsync(InviteClaimNavigation navigation, CancellationToken ct)
    {
        var policy = await authApi.GetPasswordPolicyAsync(navigation.ServerUrl, ct);
        if (ct.IsCancellationRequested || !ReferenceEquals(_navigation, navigation)) return;
        _passwordPolicy = policy;
        RefreshPasswordPolicyText();
    }

    [ObservableProperty] private InvitationLookupResponse? _invitation;
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _confirmPassword = "";
    // The address a link invitation asks the invitee to enter; unused when the invitation is bound to one.
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string? _loginBackgroundUrl;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSubmitting;
    [ObservableProperty] private bool _isExpired;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    /// <summary>The account exists (acceptance succeeded) but the server did not sign it in; the form is finished.</summary>
    [ObservableProperty] private bool _accountCreated;
    /// <summary>
    /// The outcome is unknown (or the invitation is gone): acceptance stays blocked until the invitation is reloaded,
    /// so an account that may already exist is never created a second time by a blind retry.
    /// </summary>
    [ObservableProperty] private bool _requiresReload;
    /// <summary>Password sign-in is off on this server; the invitation is unspent but cannot be accepted here.</summary>
    [ObservableProperty] private bool _localLoginDisabled;

    public bool EmailRequired => Invitation?.EmailRequired == true;

    partial void OnInvitationChanged(InvitationLookupResponse? value) => OnPropertyChanged(nameof(EmailRequired));

    public event Action? InvitationAccepted;

    public async Task LoadAsync(InviteClaimNavigation navigation)
    {
        Cancel();
        _lifetime = new CancellationTokenSource();
        _navigation = navigation;
        var ct = _lifetime.Token;
        _passwordPolicy = PasswordPolicy.Default;
        RefreshPasswordPolicyText();
        PasswordError = null;
        IsLoading = true;
        IsExpired = false;
        ErrorMessage = null;
        StatusMessage = null;
        Invitation = null;
        AccountCreated = false;
        RequiresReload = false;
        LocalLoginDisabled = false;
        Email = "";
        authService.ConfigureServer(navigation.ServerUrl);

        try
        {
            var invitationTask = authApi.LookupInvitationAsync(navigation.Token, _lifetime.Token);
            var brandingTask = LoadBrandingAsync(ct);
            var policyTask = RefreshPasswordPolicyAsync(navigation, ct);
            var (invitation, tokenInBody) = await invitationTask;
            if (ct.IsCancellationRequested) return;
            _tokenInBody = tokenInBody;
            Invitation = invitation;
            await Task.WhenAll(brandingTask, policyTask);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            IsExpired = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ShellText(nameof(ErrorMessage), () => ShellProblem(ex, ex is HttpRequestException
                ? "Unable to connect to this Silo server."
                : ex.Message));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadBrandingAsync(CancellationToken ct)
    {
        try
        {
            var branding = await settingsApi.GetServerBrandingAsync(ct);
            LoginBackgroundUrl = apiClient.ResolveServerUrl(branding.LoginBackgroundUrl);
        }
        catch when (!ct.IsCancellationRequested)
        {
            LoginBackgroundUrl = null;
        }
    }

    // Something before a single "@", no whitespace. The server owns the full rule and answers validation_failed.
    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 && !value.Any(char.IsWhiteSpace);
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (_navigation == null || Invitation == null || IsSubmitting) return;
        // Blocked states are final until the invitation is reloaded; pressing Create again does not retry them.
        if (AccountCreated || RequiresReload || LocalLoginDisabled) return;
        ErrorMessage = PasswordError = null;
        // A link invitation has no address yet: the invitee enters theirs.
        string? enteredEmail = Invitation.EmailRequired ? Email.Trim() : null;
        if (enteredEmail != null && !LooksLikeEmail(enteredEmail))
        {
            ErrorMessage = ShellText(nameof(ErrorMessage), () => AppStrings.Get("web.auth.invalidEmail", fallback: "Enter a valid email address, like name@example.com"));
            return;
        }
        PasswordError = NewPassword.Problem(Password, ConfirmPassword, _passwordPolicy);
        if (PasswordError is not null) return;
        var submittedPassword = Password;
        var navigation = _navigation;
        var ct = _lifetime?.Token ?? CancellationToken.None;

        IsSubmitting = true;
        try
        {
            // Sent exactly once per press. This layer never replays a request whose outcome is unknown.
            var acceptance = await authApi.AcceptInvitationAsync(
                navigation.Token,
                submittedPassword,
                enteredEmail,
                _tokenInBody,
                ct);
            if (acceptance.LoginStatus != "signed_in" || acceptance.Tokens is not { } response)
            {
                // Committed: the account exists. Finish the form and clear the password; Sign in continues from here.
                AccountCreated = true;
                Password = "";
                ConfirmPassword = "";
                StatusMessage = ShellText(nameof(StatusMessage), () => AppStrings.Get("web.auth.invite.created",
                    new Dictionary<string, object?> { ["username"] = acceptance.Username },
                    $"Your account was created. Sign in as {acceptance.Username} with the password you chose."));
                return;
            }
            var generation = authService.SetTokens(
                response.AccessToken,
                response.RefreshToken,
                response.ExpiresIn,
                expectedServerUrl: _navigation.ServerUrl);
            if (!authService.SetCurrentUser(response.User, generation))
                throw new InvalidOperationException("The authentication session changed before the invitation completed.");

            settingsService.AddServer(_navigation.ServerUrl, Invitation.ServerName);
            var settings = settingsService.Load();
            settings.LastUsername = response.User.Username;
            settings.LastUserRole = response.User.Role;
            settingsService.Save(settings);
            InvitationAccepted?.Invoke();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (ApiException ex) when (ex.ProblemType == "weak_password"
            || ex is { ErrorCode: "weak_password", ProblemType: null or "validation_failed" }
            || NewPassword.FieldProblem(ex, "body.password") is not null)
        {
            var fieldError = NewPassword.FieldProblem(ex, "body.password");
            if (fieldError is not null && !ct.IsCancellationRequested && Password == submittedPassword)
                PasswordError = fieldError;
            try { await RefreshPasswordPolicyAsync(navigation, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            if (fieldError is null && !ct.IsCancellationRequested && Password == submittedPassword)
                PasswordError = NewPassword.Problem(submittedPassword, _passwordPolicy)
                    ?? AppStrings.Get("web.auth.newPassword.policyRejected", fallback: "This password does not meet the server's password requirements.");
        }
        catch (Exception ex)
        {
            ApplyFailure(ex);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private void ApplyFailure(Exception ex)
    {
        switch (InvitationAcceptOutcome.Classify(ex))
        {
            case InvitationAcceptFailure.LocalLoginDisabled:
                LocalLoginDisabled = true;
                ErrorMessage = ShellText(nameof(ErrorMessage), () => ShellProblem(ex, AppStrings.Get("web.auth.invite.localLoginDisabledNoInviter",
                    fallback: "Password sign-in is turned off on this server, so this invitation cannot create an account. Ask the person who invited you to add you through the server's sign-in provider instead.")));
                break;
            case InvitationAcceptFailure.RateLimited:
                ErrorMessage = ShellText(nameof(ErrorMessage), () => AppStrings.Get("web.auth.forgotPassword.rateLimited", fallback: "Too many requests from this network. Wait a minute, then try again."));
                break;
            case InvitationAcceptFailure.ProfileUnavailable:
                ErrorMessage = ShellText(nameof(ErrorMessage), () => AppStrings.Get("web.auth.invite.profileUnavailable", fallback: "This server cannot create the profile required by this invitation. Ask the administrator for help."));
                break;
            case InvitationAcceptFailure.EmailTaken:
                ErrorMessage = ShellText(nameof(ErrorMessage), () => AppStrings.Get("web.auth.invite.emailTaken", fallback: "An account already uses this email address. Sign in instead, or use a different address."));
                break;
            case InvitationAcceptFailure.Validation:
                ErrorMessage = ShellText(nameof(ErrorMessage), () => ShellProblem(ex, ex.Message));
                break;
            case InvitationAcceptFailure.LinkUnavailable:
                // The invitation no longer exists for acceptance; a reload shows the unavailable page.
                RequiresReload = true;
                ErrorMessage = ShellText(nameof(ErrorMessage), () => AppStrings.Get("web.auth.invite.unavailable", fallback: "This link may have been used, revoked, or expired. Sign in if you already created your account, or ask for a fresh invitation."));
                break;
            default:
                // A 5xx, timeout or lost response: the account may exist. Block a blind retry until the invitation is reloaded.
                RequiresReload = true;
                ErrorMessage = ShellText(nameof(ErrorMessage), () => AppStrings.Get("web.auth.invite.recovery",
                    fallback: "We could not confirm the result. Your account may have been created. Try signing in, or reload this invitation before making another attempt."));
                break;
        }
    }

    public void Cancel()
    {
        var prior = Interlocked.Exchange(ref _lifetime, null);
        if (prior == null) return;
        prior.Cancel();
        prior.Dispose();
    }
}
