using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Services;

namespace ContinuumPlayer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;
    private readonly AuthService _authService;

    public MainViewModel(CatalogApi catalogApi, AuthService authService)
    {
        _catalogApi = catalogApi;
        _authService = authService;
    }

    public ObservableCollection<Library> Libraries { get; } = [];

    [ObservableProperty]
    private bool _isLoadingLibraries;

    [ObservableProperty]
    private string? _currentProfileName;

    // ===== Impersonation =====

    [ObservableProperty]
    private bool _isImpersonating;

    [ObservableProperty]
    private string _impersonatedUsername = "";

    [RelayCommand]
    private async Task LoadLibrariesAsync()
    {
        if (IsLoadingLibraries) return;

        IsLoadingLibraries = true;
        try
        {
            var libraries = await _catalogApi.GetLibrariesAsync();
            Libraries.Clear();
            foreach (var lib in libraries)
            {
                Libraries.Add(lib);
            }
        }
        catch
        {
            // Non-fatal, sidebar just won't show libraries
        }
        finally
        {
            IsLoadingLibraries = false;
        }
    }
}
