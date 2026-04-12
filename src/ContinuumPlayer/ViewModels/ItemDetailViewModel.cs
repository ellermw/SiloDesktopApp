using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Catalog;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Messaging;

namespace ContinuumPlayer.ViewModels;

public partial class ItemDetailViewModel : ObservableObject,
    IRecipient<MediaSurfaceChanged>,
    IRecipient<PlaybackProgressUpdated>
{
    private readonly CatalogApi _catalogApi;

    public ItemDetailViewModel(CatalogApi catalogApi)
    {
        _catalogApi = catalogApi;
        // F4: subscribe to media-surface changes so if the same item is
        // favorited / watchlisted / watched from another surface (context
        // menu on a poster card, etc.), this VM reflects it immediately.
        WeakReferenceMessenger.Default.Register<MediaSurfaceChanged>(this);
        WeakReferenceMessenger.Default.Register<PlaybackProgressUpdated>(this);
    }

    // F4: Helper to publish media-surface changes via the shared messenger.
    // Subscribers (HomeViewModel, FavoritesViewModel, WatchlistViewModel,
    // HistoryViewModel) react by invalidating / adjusting their own state.
    private static void Publish(MediaSurfaceChangeKind kind, string contentId, string? seriesId = null, int? rating = null)
    {
        WeakReferenceMessenger.Default.Send(
            new MediaSurfaceChanged(kind, contentId, seriesId, rating));
    }

    public void Receive(MediaSurfaceChanged message)
    {
        // Ignore our own publishes (already reflected in state) and anything
        // that doesn't match the currently displayed item.
        if (Item == null || message.ContentId != Item.ContentId) return;

        switch (message.Kind)
        {
            case MediaSurfaceChangeKind.FavoriteAdded:
                IsFavorite = true;
                break;
            case MediaSurfaceChangeKind.FavoriteRemoved:
                IsFavorite = false;
                break;
            case MediaSurfaceChangeKind.WatchlistAdded:
                InWatchlist = true;
                break;
            case MediaSurfaceChangeKind.WatchlistRemoved:
                InWatchlist = false;
                break;
            case MediaSurfaceChangeKind.WatchedMarked:
                IsWatched = true;
                break;
            case MediaSurfaceChangeKind.WatchedCleared:
                IsWatched = false;
                break;
            case MediaSurfaceChangeKind.RatingChanged:
                UserRating = message.Rating;
                break;
        }
    }

    public void Receive(PlaybackProgressUpdated message)
    {
        // Progress update for the item we're currently displaying → refresh
        // the resume position so the "Resume at X:XX" label reflects the
        // latest watch state. We don't refetch the whole detail — just patch
        // the user_data on Item in place.
        if (Item == null || message.ContentId != Item.ContentId) return;
        if (Item.UserData == null) return;
        Item.UserData.PositionSeconds = message.PositionSeconds;
        if (message.Completed) Item.UserData.Played = true;
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

    [ObservableProperty]
    private bool _isWatched;

    [ObservableProperty]
    private int? _userRating;

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
    public ObservableCollection<MediaItem> SimilarItems { get; } = [];

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
        SimilarItems.Clear();
        SelectedSeasonNumber = 0;
        UserRating = null;

        try
        {
            Item = await _catalogApi.GetItemDetailAsync(contentId);

            // Server commit 4172a16 inlines favorite/watchlist/rating on the item
            // detail response via `user_state` + `user_rating`. Prefer those over
            // the legacy three-way parallel fetch. Fall back to dedicated endpoints
            // only if the server didn't populate `user_state` (older servers).
            if (Item?.UserState != null)
            {
                IsFavorite = Item.UserState.IsFavorite;
                InWatchlist = Item.UserState.InWatchlist;
            }
            else
            {
                IsFavorite = false;
                InWatchlist = false;
                _ = CheckFavoriteWatchlistAsync(contentId);
            }

            UserRating = Item?.UserRating;

            IsWatched = Item?.UserData?.Played ?? false;
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

    private async Task CheckFavoriteWatchlistAsync(string contentId)
    {
        try
        {
            var favTask = _catalogApi.GetFavoriteItemAsync(contentId);
            var wlTask = _catalogApi.GetWatchlistItemAsync(contentId);
            await Task.WhenAll(favTask, wlTask);
            IsFavorite = await favTask;
            InWatchlist = await wlTask;
        }
        catch
        {
            // Non-fatal -- fall back to values from item detail
        }
    }

    [RelayCommand]
    private async Task LoadRatingAsync()
    {
        if (Item == null) return;
        try
        {
            UserRating = await _catalogApi.GetRatingAsync(Item.ContentId);
        }
        catch
        {
            // Rating load failure is non-fatal
        }
    }

    [RelayCommand]
    private async Task SetRatingAsync(int rating)
    {
        if (Item == null) return;

        // B13 + F4: optimistic rating swap. Clicking an already-selected star
        // clears the rating (matches webui toggle-off). Revert on failure.
        int? priorRating = UserRating;
        int? nextRating = (UserRating == rating) ? null : rating;
        UserRating = nextRating;
        try
        {
            if (nextRating == null)
                await _catalogApi.DeleteRatingAsync(Item.ContentId);
            else
                await _catalogApi.SetRatingAsync(Item.ContentId, nextRating.Value);

            Publish(MediaSurfaceChangeKind.RatingChanged, Item.ContentId, Item.SeriesId, nextRating);
        }
        catch
        {
            UserRating = priorRating;
        }
    }

    [RelayCommand]
    private async Task ToggleWatchedAsync()
    {
        if (Item == null) return;

        // B13 + F4: optimistic update — flip the flag immediately so the UI
        // responds instantly, then call the API. If it fails, revert. Publish
        // a MediaSurfaceChanged message on success so HomeViewModel's Continue
        // Watching row and HistoryViewModel refresh without a page reload.
        bool wasWatched = IsWatched;
        IsWatched = !wasWatched;
        try
        {
            if (wasWatched)
                await _catalogApi.MarkUnwatchedAsync(Item.ContentId);
            else
                await _catalogApi.MarkWatchedAsync(Item.ContentId);

            Publish(
                wasWatched ? MediaSurfaceChangeKind.WatchedCleared : MediaSurfaceChangeKind.WatchedMarked,
                Item.ContentId,
                Item.SeriesId);
        }
        catch
        {
            IsWatched = wasWatched;
        }
    }

    [RelayCommand]
    private async Task LoadSimilarAsync()
    {
        if (Item == null) return;
        try
        {
            var response = await _catalogApi.GetSimilarAsync(Item.ContentId);
            SimilarItems.Clear();

            // The API returns only IDs + scores, not full MediaItem objects.
            // Fetch each item's detail in parallel (limit to first 15).
            var tasks = response.Items.Take(15).Select(async s =>
            {
                try
                {
                    return await _catalogApi.GetItemDetailAsync(s.MediaItemId);
                }
                catch { return null; }
            });

            var details = await Task.WhenAll(tasks);

            foreach (var detail in details)
            {
                if (detail == null) continue;

                // Convert MediaItemDetail to MediaItem for PosterCard display
                var mediaItem = new MediaItem
                {
                    ContentId = detail.ContentId,
                    Type = detail.Type,
                    Title = detail.Title,
                    Year = detail.Year,
                    Genres = detail.Genres,
                    Overview = detail.Overview,
                    PosterUrl = detail.PosterUrl,
                    PosterThumbhash = detail.PosterThumbhash,
                    BackdropUrl = detail.BackdropUrl,
                    BackdropThumbhash = detail.BackdropThumbhash,
                    LogoUrl = detail.LogoUrl,
                };
                SimilarItems.Add(mediaItem);
            }
        }
        catch
        {
            // Similar items load failure is non-fatal
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

        // B13 + F4: optimistic flip, revert on failure, publish on success.
        bool wasFavorite = IsFavorite;
        IsFavorite = !wasFavorite;
        try
        {
            if (wasFavorite)
                await _catalogApi.RemoveFavoriteAsync(Item.ContentId);
            else
                await _catalogApi.AddFavoriteAsync(Item.ContentId);

            Publish(
                wasFavorite ? MediaSurfaceChangeKind.FavoriteRemoved : MediaSurfaceChangeKind.FavoriteAdded,
                Item.ContentId,
                Item.SeriesId);
        }
        catch
        {
            IsFavorite = wasFavorite;
        }
    }

    [RelayCommand]
    private async Task ToggleWatchlistAsync()
    {
        if (Item == null) return;

        // B13 + F4: optimistic flip, revert on failure, publish on success.
        bool wasInList = InWatchlist;
        InWatchlist = !wasInList;
        try
        {
            if (wasInList)
                await _catalogApi.RemoveFromWatchlistAsync(Item.ContentId);
            else
                await _catalogApi.AddToWatchlistAsync(Item.ContentId);

            Publish(
                wasInList ? MediaSurfaceChangeKind.WatchlistRemoved : MediaSurfaceChangeKind.WatchlistAdded,
                Item.ContentId,
                Item.SeriesId);
        }
        catch
        {
            InWatchlist = wasInList;
        }
    }
}
