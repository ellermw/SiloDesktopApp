using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminInviteCodesViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminInviteCodesViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<InviteCode> InviteCodes { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _signupEnabled;

    // ===== Load =====

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var codesTask = _adminApi.GetInviteCodesAsync();
            var settingsTask = _adminApi.GetAdminSettingsAsync();
            await Task.WhenAll(codesTask, settingsTask);

            InviteCodes.Clear();
            foreach (var c in codesTask.Result) InviteCodes.Add(c);

            if (settingsTask.Result.TryGetValue("signup.enabled", out var signupVal))
                SignupEnabled = signupVal == "true";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Public Signups Toggle =====

    public async Task<bool> SetSignupEnabledAsync(bool enabled)
    {
        try
        {
            await _adminApi.UpdateAdminSettingAsync("signup.enabled", enabled ? "true" : "false");
            SignupEnabled = enabled;
            StatusMessage = enabled ? "Public signups enabled." : "Public signups disabled.";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    // ===== Create =====

    public async Task<InviteCode?> CreateInviteCodeAsync(CreateInviteCodeRequest request)
    {
        try
        {
            var code = await _adminApi.CreateInviteCodeAsync(request);
            StatusMessage = $"Invite code \"{code.Code}\" created.";
            await LoadAsync();
            return code;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
    }

    // ===== Top up =====

    public async Task<InviteCode?> TopUpInviteCodeAsync(InviteCode code, int additionalUses)
    {
        try
        {
            if (additionalUses <= 0)
            {
                ErrorMessage = "Additional uses must be greater than 0.";
                return null;
            }

            var updated = await _adminApi.TopUpInviteCodeAsync(code.Id, new TopUpInviteCodeRequest
            {
                AdditionalUses = additionalUses
            });
            StatusMessage = $"Added {additionalUses} use{(additionalUses == 1 ? "" : "s")} to \"{updated.Code}\".";
            await LoadAsync();
            return updated;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
    }

    // ===== Toggle =====

    [RelayCommand]
    public async Task ToggleInviteCodeAsync(InviteCode code)
    {
        try
        {
            await _adminApi.UpdateInviteCodeAsync(code.Id, new UpdateInviteCodeRequest
            {
                Enabled = !code.Enabled
            });
            StatusMessage = code.Enabled ? "Invite code disabled." : "Invite code enabled.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Delete =====

    [RelayCommand]
    public async Task DeleteInviteCodeAsync(int id)
    {
        try
        {
            await _adminApi.DeleteInviteCodeAsync(id);
            StatusMessage = "Invite code deleted.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
