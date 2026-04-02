using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Admin;

namespace ContinuumPlayer.ViewModels.Admin;

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

    // ===== Load =====

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var response = await _adminApi.GetInviteCodesAsync();
            InviteCodes.Clear();
            foreach (var c in response.InviteCodes) InviteCodes.Add(c);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
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
