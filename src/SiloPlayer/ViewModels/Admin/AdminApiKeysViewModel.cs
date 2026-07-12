using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminApiKeysViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminApiKeysViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<AdminAPIKey> ApiKeys { get; } = [];
    public ObservableCollection<AdminUser> Users { get; } = [];

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
            var keysTask = _adminApi.GetAPIKeysAsync();
            var usersTask = _adminApi.GetUsersAsync();
            await Task.WhenAll(keysTask, usersTask);

            ApiKeys.Clear();
            foreach (var k in keysTask.Result)
                ApiKeys.Add(k);

            Users.Clear();
            foreach (var u in usersTask.Result)
                Users.Add(u);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    // ===== Create Key =====

    [RelayCommand]
    public async Task<AdminAPIKey?> CreateKeyAsync(AdminCreateAPIKeyRequest request)
    {
        try
        {
            var created = await _adminApi.CreateAPIKeyAsync(request);
            StatusMessage = $"API key \"{created.Label}\" created.";
            await LoadAsync();
            return created;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
    }

    // ===== Delete Key =====

    [RelayCommand]
    public async Task DeleteKeyAsync(int id)
    {
        try
        {
            await _adminApi.DeleteAPIKeyAsync(id);
            StatusMessage = "API key deleted.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ===== Update Tier =====

    [RelayCommand]
    public async Task UpdateTierAsync((int Id, string Tier) args)
    {
        try
        {
            await _adminApi.UpdateAPIKeyTierAsync(args.Id, args.Tier);
            StatusMessage = "Tier updated.";
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
