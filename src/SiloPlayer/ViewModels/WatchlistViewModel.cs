using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Messaging;

namespace SiloPlayer.ViewModels;

public partial class WatchlistViewModel : ObservableObject, IRecipient<MediaSurfaceChanged>
{
    private readonly CatalogApi _catalogApi;
    private bool _loadInProgress;
    private int _offset;
    private const int PageSize = 50;

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

    [ObservableProperty]
    private bool _hasMore;

    [ObservableProperty]
    private bool _isLoadingMore;

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
                        _offset = Math.Max(0, _offset - 1);
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
            var response = await _catalogApi.GetWatchlistAsync(PageSize, _offset);
            if (replace)
                Items.Clear();

            var existing = Items.Select(item => item.ContentId).ToHashSet(StringComparer.Ordinal);
            foreach (var item in response.Items)
            {
                if (existing.Add(item.ContentId))
                    Items.Add(item);
            }

            // The server computes has_more before hidden-series filtering, so
            // the transport offset advances by the requested raw page size.
            _offset += PageSize;
            HasMore = response.HasMore;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load watchlist: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            IsLoadingMore = false;
            _loadInProgress = false;
        }
    }
}
