using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;

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

    public string CurrentPassword { get => _currentPassword; set => SetProperty(ref _currentPassword, value); }
    public string NewPassword { get => _newPassword; set => SetProperty(ref _newPassword, value); }
    public string ConfirmPassword { get => _confirmPassword; set => SetProperty(ref _confirmPassword, value); }
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string? SuccessMessage { get => _successMessage; private set => SetProperty(ref _successMessage, value); }
    public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }
    public bool IsSubmitting { get => _isSubmitting; private set => SetProperty(ref _isSubmitting, value); }
    public bool CanChangePassword => _context is { } context && api.IsCurrentContext(context)
        && _capability is { State: "available", Allowed: true, MinimumPasswordLength: > 0, MaximumPasswordBytes: > 0 };
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
            ErrorMessage = "Password changes are unavailable for this account.";
        else if (CurrentPassword.Length == 0)
            ErrorMessage = "Current password is required.";
        else if (NewPassword != ConfirmPassword)
            ErrorMessage = "New passwords do not match.";
        else if (NewPassword.EnumerateRunes().Count() < _capability!.MinimumPasswordLength)
            ErrorMessage = $"New password must be at least {_capability.MinimumPasswordLength} characters.";
        else if (Encoding.UTF8.GetByteCount(NewPassword) > _capability!.MaximumPasswordBytes)
            ErrorMessage = $"New password must be at most {_capability.MaximumPasswordBytes} bytes.";
        if (ErrorMessage is not null) return;

        var generation = _generation;
        var ct = _lifetime!.Token;
        IsSubmitting = true;
        try
        {
            await api.ChangePasswordAsync(context, CurrentPassword, NewPassword, ct);
            if (!IsCurrent(generation, context)) return;
            ClearPasswords();
            SuccessMessage = "Password changed";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || !api.IsCurrentContext(context)) { }
        catch (Exception ex)
        {
            if (IsCurrent(generation, context))
                ErrorMessage = ex is ApiException ? ex.Message : "Failed to change password. Try again.";
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
        ClearPasswords();
        ErrorMessage = SuccessMessage = null;
        IsLoading = IsSubmitting = false;
        OnPropertyChanged(nameof(CanChangePassword));
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
