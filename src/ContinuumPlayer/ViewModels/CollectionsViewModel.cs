using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Collections;

namespace ContinuumPlayer.ViewModels;

public partial class CollectionsViewModel : ObservableObject
{
    private readonly CollectionsApi _collectionsApi;

    public CollectionsViewModel(CollectionsApi collectionsApi)
    {
        _collectionsApi = collectionsApi;
    }

    public ObservableCollection<Collection> Collections { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task LoadCollectionsAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var response = await _collectionsApi.GetCollectionsAsync();
            Collections.Clear();
            foreach (var c in response.Collections)
                Collections.Add(c);
            IsEmpty = Collections.Count == 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load collections: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync(string id)
    {
        try
        {
            await _collectionsApi.DeleteCollectionAsync(id);
            // Remove from local list
            var item = Collections.FirstOrDefault(c => c.Id == id);
            if (item != null)
                Collections.Remove(item);
            IsEmpty = Collections.Count == 0;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to delete collection: {ex.Message}";
        }
    }
}
