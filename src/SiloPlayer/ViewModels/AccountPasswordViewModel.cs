using System.Text;
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
    private string _currentPassword = "", _newPassword = "", _confirmPassword = "";
    private string? _errorMessage, _successMessage;
    private bool _isLoading, _isSubmitting;
    private bool _signOutOtherSessions;

    public string CurrentPassword { get => _currentPassword; set => SetProperty(ref _currentPassword, value); }
    public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }
    public string ConfirmPassword { get => _confirmPassword; set => SetProperty(ref _confirmPassword, value); }
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
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
    public string Requirements => _capability is { } limits
        ? $"At least {limits.MinimumPasswordLength} characters and no more than {limits.MaximumPasswordBytes} bytes."
        : "";

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
            _capability = capability;
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
        SuccessMessage = null;
        if (!CanChangePassword)
            ErrorMessage = AppStrings.Get("web.settings.account.password.error.unavailable");
        else if (CurrentPassword.Length == 0)
            ErrorMessage = "Current password is required.";
        else if (NewPassword != ConfirmPassword)
            ErrorMessage = AppStrings.Get("web.settings.account.password.error.mismatch");
        else if (NewPassword.EnumerateRunes().Count() < _capability!.MinimumPasswordLength)
            ErrorMessage = $"New password must be at least {_capability.MinimumPasswordLength} characters.";
        else if (Encoding.UTF8.GetByteCount(NewPassword) > _capability!.MaximumPasswordBytes)
            ErrorMessage = AppStrings.Get("web.settings.account.password.error.tooLong",
                new Dictionary<string, object?> { ["maximum"] = _capability.MaximumPasswordBytes.ToString("N0", AppStrings.FormatCulture) },
                $"New password must be at most {_capability.MaximumPasswordBytes} bytes.");
        if (ErrorMessage is not null) return;

        var generation = _generation;
        var ct = _lifetime!.Token;
        var signOutOthers = CanSignOutOtherSessions && SignOutOtherSessions;
        IsSubmitting = true;
        try
        {
            await api.ChangePasswordAsync(context, CurrentPassword, NewPassword, signOutOthers, ct);
            if (!IsCurrent(generation, context)) return;
            ClearPasswords();
            SetProperty(ref _signOutOtherSessions, false, nameof(SignOutOtherSessions));
            SuccessMessage = signOutOthers ? AppStrings.Get("web.settings.account.password.changedSignedOutOthers") : AppStrings.Get("web.settings.account.password.changed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !api.IsCurrentContext(context)) { }
        catch (Exception ex)
        {
            if (IsCurrent(generation, context))
                ErrorMessage = ex is ApiException problem ? ApiProblemText.Display(problem) : "Failed to change password. Try again.";
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
        SetProperty(ref _signOutOtherSessions, false, nameof(SignOutOtherSessions));
        ClearPasswords();
        ErrorMessage = SuccessMessage = null;
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
