using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Messaging;

namespace ContinuumPlayer.ViewModels;

public partial class WatchlistViewModel : ObservableObject, IRecipient<MediaSurfaceChanged>
{
    private readonly CatalogApi _catalogApi;

    public WatchlistViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
        // F4: react to watchlist add/remove events from anywhere in the app.
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
            case MediaSurfaceChangeKind.WatchlistRemoved:
                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    if (Items[i].ContentId == message.ContentId)
                    {
                        Items.RemoveAt(i);
                        break;
                    }
                }
                break;
            case MediaSurfaceChangeKind.WatchlistAdded:
                // Item added elsewhere — need a refresh since the messenger
                // event doesn't carry the full MediaItem payload.
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
            var response = await _catalogApi.GetWatchlistAsync();
            Items.Clear();
            foreach (var item in response.Items)
            {
                Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load watchlist: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
