using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Messaging;

namespace SiloPlayer.ViewModels;

public partial class FavoritesViewModel : ObservableObject, IRecipient<MediaSurfaceChanged>
{
    private readonly CatalogApi _catalogApi;
    private bool _loadInProgress;
    private int _offset;
    private const int PageSize = 50;

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

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private bool _isLoadingMore;

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
                        _offset = Math.Max(0, _offset - 1);
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
        if (_loadInProgress) return;

        _offset = 0;
        HasMore = false;
        await LoadPageAsync(replace: true);
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || _loadInProgress) return;
        await LoadPageAsync(replace: false);
    }

    private async Task LoadPageAsync(bool replace)
    {
        _loadInProgress = true;
        IsLoading = replace && Items.Count == 0;
        IsLoadingMore = !replace;
        ErrorMessage = null;

        try
        {
            var response = await _catalogApi.GetFavoritesAsync(PageSize, _offset);
            if (replace)
                Items.Clear();

            var existing = Items.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
            foreach (var item in response.Items)
            {
                if (existing.Add(item.ContentId))
                    Items.Add(item);
            }

            // has_more is based on the raw server page, which may contain
            // entries filtered out during catalog resolution. Advance by the
            // requested page size rather than the returned display-item count.
            _offset += PageSize;
            HasMore = response.HasMore;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load favorites: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            IsLoadingMore = false;
            _loadInProgress = false;
        }
    }
}
