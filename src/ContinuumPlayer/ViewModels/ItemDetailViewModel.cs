using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;

namespace ContinuumPlayer.ViewModels;

public partial class ItemDetailViewModel : ObservableObject
{
    private readonly CatalogApi _catalogApi;

    public ItemDetailViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
    }

    [ObservableProperty]
    private MediaItemDetail? _item;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _inWatchlist;

    // Series support
    [ObservableProperty]
    private bool _isSeries;

    [ObservableProperty]
    private bool _isSeasonsLoading;

    [ObservableProperty]
    private bool _isEpisodesLoading;

    [ObservableProperty]
    private int _selectedSeasonNumber;

    public ObservableCollection<Season> Seasons { get; } = [];
    public ObservableCollection<Episode> Episodes { get; } = [];

    public string RuntimeDisplay =>
        Item?.Runtime > 0 ? $"{Item.Runtime / 60}h {Item.Runtime % 60}m" : "";

    public string GenresDisplay =>
        Item?.Genres.Count > 0 ? string.Join(", ", Item.Genres) : "";

    public string RatingDisplay =>
        Item?.RatingTmdb != null ? $"{Item.RatingTmdb:F1}" : "";

    [RelayCommand]
    private async Task LoadAsync(string contentId)
    {
        if (IsLoading) return;

        IsLoading = true;
        ErrorMessage = null;
        IsSeries = false;
        Seasons.Clear();
        Episodes.Clear();
        SelectedSeasonNumber = 0;

        try
        {
            Item = await _catalogApi.GetItemDetailAsync(contentId);
            IsFavorite = Item?.UserState?.IsFavorite ?? false;
            InWatchlist = Item?.UserState?.InWatchlist ?? false;
            IsSeries = Item?.Type == "series";
            OnPropertyChanged(nameof(RuntimeDisplay));
            OnPropertyChanged(nameof(GenresDisplay));
            OnPropertyChanged(nameof(RatingDisplay));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load item: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadSeasonsAsync()
    {
        if (Item == null || !IsSeries || IsSeasonsLoading) return;

        IsSeasonsLoading = true;
        try
        {
            var response = await _catalogApi.GetSeasonsAsync(Item.ContentId);
            Seasons.Clear();
            foreach (var season in response.Seasons)
                Seasons.Add(season);

            // Auto-select the first season
            if (Seasons.Count > 0)
                await SelectSeasonAsync(Seasons[0].SeasonNumber);
        }
        catch
        {
            // Seasons load failure is non-fatal
        }
        finally
        {
            IsSeasonsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectSeasonAsync(int seasonNumber)
    {
        if (Item == null || seasonNumber == 0) return;

        SelectedSeasonNumber = seasonNumber;
        IsEpisodesLoading = true;
        Episodes.Clear();

        try
        {
            var response = await _catalogApi.GetEpisodesAsync(Item.ContentId, seasonNumber);
            foreach (var episode in response.Episodes)
                Episodes.Add(episode);
        }
        catch
        {
            // Episode load failure is non-fatal
        }
        finally
        {
            IsEpisodesLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (Item == null) return;

        try
        {
            if (IsFavorite)
            {
                await _catalogApi.RemoveFavoriteAsync(Item.ContentId);
                IsFavorite = false;
            }
            else
            {
                await _catalogApi.AddFavoriteAsync(Item.ContentId);
                IsFavorite = true;
            }
        }
        catch
        {
            // Revert on failure
        }
    }

    [RelayCommand]
    private async Task ToggleWatchlistAsync()
    {
        if (Item == null) return;

        try
        {
            if (InWatchlist)
            {
                await _catalogApi.RemoveFromWatchlistAsync(Item.ContentId);
                InWatchlist = false;
            }
            else
            {
                await _catalogApi.AddToWatchlistAsync(Item.ContentId);
                InWatchlist = true;
            }
        }
        catch
        {
            // Revert on failure
        }
    }
}
