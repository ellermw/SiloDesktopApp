using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.ViewModels;

public partial class FavoritesViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;

    public FavoritesViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
    }

    public ObservableCollection<MediaItem> Items { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var response = await _catalogApi.GetFavoritesAsync();
            Items.Clear();
            foreach (var item in response.Items)
            {
                Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load favorites: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
