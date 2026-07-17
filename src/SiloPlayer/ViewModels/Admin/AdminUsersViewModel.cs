using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.ViewModels.Admin;

public partial class AdminUsersViewModel : ObservableObject
{
    private readonly AdminApi _adminApi;

    public AdminUsersViewModel(AdminApi adminApi)
    {
        _adminApi = adminApi;
    }

    public ObservableCollection<AdminUser> Users { get; } = [];
    public ObservableCollection<Library> Libraries { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    public bool HasLoaded { get; private set; }

    [RelayCommand]
    private Task LoadAsync() => ReloadAsync(force: false);

    private async Task ReloadAsync(bool force)
    {
        if (HasLoaded && !force) return;
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            var usersTask = _adminApi.GetUsersAsync();
            var librariesTask = _adminApi.GetAdminLibrariesAsync();
            await Task.WhenAll(usersTask, librariesTask);

            Users.Clear();
            foreach (var u in usersTask.Result) Users.Add(u);

            Libraries.Clear();
            foreach (var l in librariesTask.Result) Libraries.Add(l);
            HasLoaded = true;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task CreateUserAsync(CreateUserRequest request)
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await _adminApi.CreateUserAsync(request);
            await ReloadAsync(force: true);
            StatusMessage = "User created successfully.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task UpdateUserAsync((int Id, UpdateUserRequest Request) args)
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await _adminApi.UpdateUserAsync(args.Id, args.Request);
            await ReloadAsync(force: true);
            StatusMessage = "User updated successfully.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task DeleteUserAsync(int userId)
    {
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;
        try
        {
            await _adminApi.DeleteUserAsync(userId);
            await ReloadAsync(force: true);
            StatusMessage = "User deleted.";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }
}
