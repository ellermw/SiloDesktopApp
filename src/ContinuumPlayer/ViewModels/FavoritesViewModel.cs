using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Messaging;

namespace ContinuumPlayer.ViewModels;

public partial class FavoritesViewModel : ObservableObject, IRecipient<MediaSurfaceChanged>
{
    private readonly CatalogApi _catalogApi;

    public FavoritesViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
        // F4: Listen for favorite/unfavorite events from anywhere in the app
        // so this page reflects mutations made on ItemDetailPage or context
        // menus without needing a full reload.
        WeakReferenceMessenger.Default.Register(this);
    }

    public ObservableCollection<MediaItem> Items { get; } = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    public void Receive(MediaSurfaceChanged message)
    {
        switch (message.Kind)
        {
            case MediaSurfaceChangeKind.FavoriteRemoved:
                // Drop the item from the local list — faster and more
                // accurate than a server roundtrip.
                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    if (Items[i].ContentId == message.ContentId)
                    {
                        Items.RemoveAt(i);
                        break;
                    }
                }
                break;
            case MediaSurfaceChangeKind.FavoriteAdded:
                // Item was favorited elsewhere. If it's not already on the
                // list we need a full refresh to populate the MediaItem
                // (we don't have the full payload on the messenger event).
                if (!Items.Any(x => x.ContentId == message.ContentId))
                    _ = LoadAsync();
                break;
        }
    }

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
