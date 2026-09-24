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

    [ObservableProperty] private InvitationLookupResponse? _invitation;
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string? _loginBackgroundUrl;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSubmitting;
    [ObservableProperty] private bool _isExpired;
    [ObservableProperty] private string? _errorMessage;

    public event Action? InvitationAccepted;

    public async Task LoadAsync(InviteClaimNavigation navigation)
    {
        Cancel();
        _lifetime = new CancellationTokenSource();
        _navigation = navigation;
        IsLoading = true;
        IsExpired = false;
        ErrorMessage = null;
        Invitation = null;
        authService.ConfigureServer(navigation.ServerUrl);

        try
        {
            var invitationTask = authApi.GetInvitationAsync(navigation.Token, _lifetime.Token);
            var brandingTask = LoadBrandingAsync(_lifetime.Token);
            Invitation = await invitationTask;
            await brandingTask;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            IsExpired = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex is HttpRequestException
                ? "Unable to connect to this Silo server."
                : ex.Message;
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

    [RelayCommand]
    private async Task AcceptAsync()
    {
        if (_navigation == null || Invitation == null || IsSubmitting) return;
        ErrorMessage = null;
        if (Password.Length < 8)
        {
            ErrorMessage = "Password must be at least 8 characters.";
            return;
        }
        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ErrorMessage = "Passwords do not match.";
            return;
        }

        IsSubmitting = true;
        try
        {
            var acceptance = await authApi.AcceptInvitationAsync(
                _navigation.Token,
                Password,
                _lifetime?.Token ?? CancellationToken.None);
            if (acceptance.LoginStatus != "signed_in" || acceptance.Tokens is not { } response)
            {
                ErrorMessage = "Your account was created. Use Sign in to continue with your new password.";
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
        catch (OperationCanceledException) when (_lifetime?.IsCancellationRequested == true) { }
        catch (ApiException ex)
        {
            ErrorMessage = ex.ErrorCode switch
            {
                "weak_password" => "Password must be at least 8 characters.",
                "already_used" or "not_found" => "This invitation is invalid or has expired.",
                _ => ex.Message,
            };
        }
        catch (Exception ex)
        {
            ErrorMessage = ex is HttpRequestException
                ? "Unable to connect to this Silo server."
                : ex.Message;
        }
        finally
        {
            IsSubmitting = false;
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
