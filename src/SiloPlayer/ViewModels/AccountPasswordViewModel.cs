using PasswordRules = SiloPlayer.Core.Services.NewPassword;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.ViewModels;

/// <summary>A password form belongs to one visit and one authenticated profile context.</summary>
public sealed class AccountPasswordViewModel(AccountPasswordApi api) : ObservableObject
{
    private CancellationTokenSource? _lifetime;
    private ApiRequestContext? _context;
    private long _generation;
    private AccountPasswordCapability? _capability;
    private PasswordPolicy _policy = PasswordPolicy.Default;
    private string? _passwordError;
    private string _currentPassword = "", _newPassword = "", _confirmPassword = "";
    private string? _errorMessage, _successMessage;
    private bool _isLoading, _isSubmitting;
    private bool _signOutOtherSessions;

    public string CurrentPassword { get => _currentPassword; set => SetProperty(ref _currentPassword, value); }
    public string NewPassword { get => _newPassword; set { if (SetProperty(ref _newPassword, value)) PasswordError = null; } }
    public string ConfirmPassword { get => _confirmPassword; set { if (SetProperty(ref _confirmPassword, value)) PasswordError = null; } }
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string? PasswordError { get => _passwordError; private set => SetProperty(ref _passwordError, value); }
    public string? SuccessMessage { get => _successMessage; private set => SetProperty(ref _successMessage, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsSubmitting { get => _isSubmitting; private set => SetProperty(ref _isSubmitting, value); }
    public bool CanChangePassword => _context is { } context && api.IsCurrentContext(context)
        && _capability is { State: "available", Allowed: true, MinimumPasswordLength: > 0, MaximumPasswordBytes: > 0 };
    public bool CanSignOutOtherSessions => CanChangePassword && _capability?.SignOutOtherSessions == true;
    public bool SignOutOtherSessions
    {
        get => _signOutOtherSessions;
        set
        {
            if (!IsSubmitting)
                SetProperty(ref _signOutOtherSessions, value && CanSignOutOtherSessions);
        }
    }
    public string Requirements => _capability is null ? "" : PasswordRules.Hint(_policy, includeByteLimit: true);

    public async Task LoadAsync()
    {
        Deactivate();
        var generation = _generation;
        var context = api.CaptureContext();
        _context = context;
        _lifetime = new();
        var ct = _lifetime.Token;
        IsLoading = true;
        try
        {
            var capability = await api.GetCapabilityAsync(context, ct);
            if (!IsCurrent(generation, context)) return;
            var policy = capability is { Allowed: true, MinimumPasswordLength: > 0, MaximumPasswordBytes: > 0 }
                ? await api.GetPolicyAsync(context, new PasswordPolicy
                {
                    MinimumLength = capability.MinimumPasswordLength,
                    MaximumBytes = capability.MaximumPasswordBytes
                }, ct)
                : PasswordPolicy.Default;
            if (!IsCurrent(generation, context)) return;
            _capability = capability;
            _policy = policy;
            OnPropertyChanged(nameof(CanChangePassword));
            OnPropertyChanged(nameof(CanSignOutOtherSessions));
            OnPropertyChanged(nameof(Requirements));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !api.IsCurrentContext(context)) { }
        catch (Exception)
        {
            if (IsCurrent(generation, context))
                ErrorMessage = "Password settings could not be loaded. Try again.";
        }
        finally
        {
            if (generation == _generation)
            {
                if (!api.IsCurrentContext(context)) Deactivate();
                else IsLoading = false;
            }
        }
    }

    public async Task SubmitAsync()
    {
        if (IsSubmitting || IsLoading) return;
        if (_context is not { } context || !api.IsCurrentContext(context))
        {
            Deactivate();
            return;
        }
        ErrorMessage = null;
        SuccessMessage = PasswordError = null;
        if (!CanChangePassword)
            ErrorMessage = AppStrings.Get("web.settings.account.password.error.unavailable");
        else if (CurrentPassword.Length == 0)
            ErrorMessage = "Current password is required.";
        else if (NewPassword != ConfirmPassword)
            PasswordError = AppStrings.Get("web.settings.account.password.error.mismatch");
        else
            PasswordError = PasswordRules.Problem(NewPassword, _policy);
        if (ErrorMessage is not null || PasswordError is not null) return;

        var generation = _generation;
        var ct = _lifetime!.Token;
        var signOutOthers = CanSignOutOtherSessions && SignOutOtherSessions;
        var submittedPassword = NewPassword;
        IsSubmitting = true;
        try
        {
            await api.ChangePasswordAsync(context, CurrentPassword, submittedPassword, signOutOthers, ct);
            if (!IsCurrent(generation, context)) return;
            ClearPasswords();
            SetProperty(ref _signOutOtherSessions, false, nameof(SignOutOtherSessions));
            SuccessMessage = signOutOthers ? AppStrings.Get("web.settings.account.password.changedSignedOutOthers") : AppStrings.Get("web.settings.account.password.changed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !api.IsCurrentContext(context)) { }
        catch (Exception ex)
        {
            if (!IsCurrent(generation, context)) return;
            if (ex is ApiException problem && PasswordRules.FieldProblem(problem, "body.new_password") is { } fieldError)
            {
                if (NewPassword == submittedPassword) PasswordError = fieldError;
                try
                {
                    var policy = await api.GetPolicyAsync(context, _policy, ct);
                    if (!IsCurrent(generation, context)) return;
                    _policy = policy;
                    OnPropertyChanged(nameof(Requirements));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            }
            else
                ErrorMessage = ex is ApiException other ? ApiProblemText.Display(other) : "Failed to change password. Try again.";
        }
        finally
        {
            if (generation == _generation)
            {
                if (!api.IsCurrentContext(context)) Deactivate();
                else IsSubmitting = false;
            }
        }
    }

    public void Deactivate()
    {
        _generation++;
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _context = null;
        _capability = null;
        _policy = PasswordPolicy.Default;
        SetProperty(ref _signOutOtherSessions, false, nameof(SignOutOtherSessions));
        ClearPasswords();
        ErrorMessage = SuccessMessage = PasswordError = null;
        IsLoading = IsSubmitting = false;
        OnPropertyChanged(nameof(CanChangePassword));
        OnPropertyChanged(nameof(CanSignOutOtherSessions));
        OnPropertyChanged(nameof(Requirements));
    }

    private bool IsCurrent(long generation, ApiRequestContext context)
        => generation == _generation && api.IsCurrentContext(context);

    private void ClearPasswords()
    {
        CurrentPassword = NewPassword = ConfirmPassword = "";
        // The view may still have input whose native PasswordChanged event has
        // not arrived. Always clear all three controls, even for unchanged model values.
        OnPropertyChanged(nameof(CurrentPassword));
        OnPropertyChanged(nameof(NewPassword));
        OnPropertyChanged(nameof(ConfirmPassword));
    }
}
