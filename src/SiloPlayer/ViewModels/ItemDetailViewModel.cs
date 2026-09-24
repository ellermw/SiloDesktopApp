using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;

namespace SiloPlayer.ViewModels;

public partial class ItemDetailViewModel : ObservableObject,
    IRecipient<MediaSurfaceChanged>,
    IRecipient<PlaybackProgressUpdated>
{
    private readonly CatalogApi _catalogApi;
    private readonly ItemDetailPrefetchCache _detailPrefetchCache;
    private CancellationTokenSource? _loadCts;
    private long _similarLoadGeneration;
    private long _seasonsLoadGeneration;
    private long _episodesLoadGeneration;
    private long _watchedStateGeneration;

    public ItemDetailViewModel(
        CatalogApi catalogApi,
        ItemDetailPrefetchCache detailPrefetchCache)
    {
        _catalogApi = catalogApi;
        _detailPrefetchCache = detailPrefetchCache;
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
        _detailPrefetchCache.Invalidate(message.ContentId);
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
        _detailPrefetchCache.Invalidate(message.ContentId);
        // Progress update for the item we're currently displaying → refresh
        // the resume position so the "Resume at X:XX" label reflects the
        // latest watch state. We don't refetch the whole detail — just patch
        // the user_data on Item in place.
        if (Item == null || message.ContentId != Item.ContentId) return;
        Item.UserData ??= new ItemDetailUserData();
        Item.UserData.PositionSeconds = message.PositionSeconds;
        Item.UserData.DurationSeconds = message.DurationSeconds;
        if (message.Completed)
        {
            Item.UserData.Played = true;
            IsWatched = true;
        }
    }

    /// <summary>
    /// Read the displayed item's state after final progress saves, including
    /// episodes retired by autoplay and aggregate season/series completion.
    /// Keep the existing page mounted and reject results after navigation.
    /// </summary>
    public async Task RefreshWatchedStateAsync(
        Task pendingProgressSave, CancellationToken cancellationToken)
    {
        var item = Item;
        if (item?.Type is not ("movie" or "episode" or "series" or "season")) return;
        var generation = Volatile.Read(ref _watchedStateGeneration);
        try
        {
            await pendingProgressSave.WaitAsync(cancellationToken);
            if (!ReferenceEquals(Item, item) || generation != Volatile.Read(ref _watchedStateGeneration)) return;
            _detailPrefetchCache.Invalidate(item.ContentId);
            var refreshed = await _detailPrefetchCache.GetAsync(item.ContentId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(Item, item) || generation != Volatile.Read(ref _watchedStateGeneration)) return;
            item.UserData = refreshed.UserData;
            IsWatched = item.UserData?.Played ?? false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Watched-state refresh failed: {ex.Message}");
        }
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

    partial void OnIsWatchedChanged(bool value) => Interlocked.Increment(ref _watchedStateGeneration);

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
    private bool _seasonsLoadFailed;

    [ObservableProperty]
    private bool _episodesLoadFailed;

    [ObservableProperty]
    private bool _similarLoadFailed;

    [ObservableProperty]
    private int _selectedSeasonNumber = -1;

    public ObservableCollection<Season> Seasons { get; } = [];
    public ObservableCollection<Episode> Episodes { get; } = [];
    public ObservableCollection<MediaItem> SimilarItems { get; } = [];

    public string RuntimeDisplay => Item?.Runtime > 0
        ? Item.Runtime >= 60
            ? Item.Runtime % 60 == 0
                ? $"{Item.Runtime / 60}h"
                : $"{Item.Runtime / 60}h {Item.Runtime % 60}m"
            : $"{Item.Runtime}m"
        : "";

    public string GenresDisplay =>
        Item?.Genres.Count > 0 ? string.Join(", ", Item.Genres) : "";

    public string RatingDisplay =>
        Item?.RatingTmdb != null ? $"{Item.RatingTmdb:F1}" : "";

    public void CancelPendingLoads()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadAsync(string contentId)
    {
        CancelPendingLoads();
        var loadCts = new CancellationTokenSource();
        _loadCts = loadCts;
        var ct = loadCts.Token;

        IsLoading = true;
        ErrorMessage = null;
        Item = null;
        Interlocked.Increment(ref _similarLoadGeneration);
        Interlocked.Increment(ref _seasonsLoadGeneration);
        Interlocked.Increment(ref _episodesLoadGeneration);
        IsSeries = false;
        IsSeasonsLoading = false;
        IsEpisodesLoading = false;
        Seasons.Clear();
        Episodes.Clear();
        SimilarItems.Clear();
        SeasonsLoadFailed = false;
        EpisodesLoadFailed = false;
        SimilarLoadFailed = false;
        SelectedSeasonNumber = -1;
        UserRating = null;

        try
        {
            var item = await _detailPrefetchCache.GetAsync(contentId, ct);
            ct.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_loadCts, loadCts)) return;
            Item = item;

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
                await CheckFavoriteWatchlistAsync(contentId, loadCts);
            }

            UserRating = Item?.UserRating;

            IsWatched = Item?.UserData?.Played ?? false;
            IsSeries = Item?.Type == "series";
            OnPropertyChanged(nameof(RuntimeDisplay));
            OnPropertyChanged(nameof(GenresDisplay));
            OnPropertyChanged(nameof(RatingDisplay));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Item detail load failed for {contentId}: {ex}");
            ErrorMessage = "Silo could not load this item. Check the connection and try again.";
        }
        finally
        {
            if (ReferenceEquals(_loadCts, loadCts))
            {
                IsLoading = false;
                _loadCts.Dispose();
                _loadCts = null;
            }
        }
    }

    /// <summary>
    /// Invalidates prefetched detail after a server-side metadata or state
    /// mutation, then performs a definitive reload.
    /// </summary>
    public Task ReloadAsync(string contentId)
    {
        _detailPrefetchCache.Invalidate(contentId);
        return LoadAsync(contentId);
    }

    private async Task CheckFavoriteWatchlistAsync(string contentId, CancellationTokenSource owner)
    {
        try
        {
            var ct = owner.Token;
            var favTask = _catalogApi.GetFavoriteItemAsync(contentId, ct);
            var wlTask = _catalogApi.GetWatchlistItemAsync(contentId, ct);
            await Task.WhenAll(favTask, wlTask);
            if (!ReferenceEquals(_loadCts, owner) || Item?.ContentId != contentId) return;
            IsFavorite = await favTask;
            InWatchlist = await wlTask;
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
        }
        catch
        {
            // Non-fatal -- fall back to values from item detail
        }
    }

    [RelayCommand]
    private async Task LoadRatingAsync()
    {
        var contentId = Item?.ContentId;
        if (string.IsNullOrWhiteSpace(contentId)) return;

        try
        {
            var rating = await _catalogApi.GetRatingAsync(contentId);
            if (Item?.ContentId != contentId) return;
            UserRating = rating;
        }
        catch
        {
            // Rating load failure is non-fatal
        }
    }

    [RelayCommand]
    private async Task SetRatingAsync(int rating)
    {
        var contentId = Item?.ContentId;
        var seriesId = Item?.SeriesId;
        if (string.IsNullOrWhiteSpace(contentId)) return;

        // B13 + F4: optimistic rating swap. Clicking an already-selected star
        // clears the rating (matches webui toggle-off). Revert on failure.
        int? priorRating = UserRating;
        int? nextRating = (UserRating == rating) ? null : rating;
        UserRating = nextRating;
        try
        {
            if (nextRating == null)
                await _catalogApi.DeleteRatingAsync(contentId);
            else
                await _catalogApi.SetRatingAsync(contentId, nextRating.Value);

            if (Item?.ContentId != contentId) return;
            Publish(MediaSurfaceChangeKind.RatingChanged, contentId, seriesId, nextRating);
        }
        catch
        {
            if (Item?.ContentId == contentId)
                UserRating = priorRating;
        }
    }

    [RelayCommand]
    private async Task ToggleWatchedAsync()
    {
        var contentId = Item?.ContentId;
        var seriesId = Item?.SeriesId;
        if (string.IsNullOrWhiteSpace(contentId)) return;

        // B13 + F4: optimistic update — flip the flag immediately so the UI
        // responds instantly, then call the API. If it fails, revert. Publish
        // a MediaSurfaceChanged message on success so HomeViewModel's Continue
        // Watching row and HistoryViewModel refresh without a page reload.
        bool wasWatched = IsWatched;
        IsWatched = !wasWatched;
        try
        {
            if (wasWatched)
                await _catalogApi.MarkUnwatchedAsync(contentId);
            else
                await _catalogApi.MarkWatchedAsync(contentId);

            if (Item?.ContentId != contentId) return;
            Publish(
                wasWatched ? MediaSurfaceChangeKind.WatchedCleared : MediaSurfaceChangeKind.WatchedMarked,
                contentId,
                seriesId);
        }
        catch
        {
            if (Item?.ContentId == contentId)
                IsWatched = wasWatched;
        }
    }

    [RelayCommand]
    private async Task LoadSimilarAsync()
    {
        if (Item == null) return;
        var contentId = Item.ContentId;
        var generation = Interlocked.Increment(ref _similarLoadGeneration);
        SimilarLoadFailed = false;
        try
        {
            var response = await _catalogApi.GetSimilarAsync(contentId);
            if (generation != Volatile.Read(ref _similarLoadGeneration)
                || Item?.ContentId != contentId) return;

            // The API returns only IDs + scores, not full MediaItem objects.
            // The current WebUI recommendation grid caps this surface at 12.
            var tasks = response.Items.Take(12).Select(async s =>
            {
                try
                {
                    return await _catalogApi.GetItemDetailAsync(s.MediaItemId);
                }
                catch { return null; }
            });

            var details = await Task.WhenAll(tasks);

            if (generation != Volatile.Read(ref _similarLoadGeneration)
                || Item?.ContentId != contentId) return;

            SimilarItems.Clear();

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
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _similarLoadGeneration)
                || Item?.ContentId != contentId) return;
            SimilarLoadFailed = true;
            System.Diagnostics.Debug.WriteLine($"Similar-items load failed for {contentId}: {ex}");
        }
    }

    [RelayCommand]
    private async Task LoadSeasonsAsync()
    {
        if (Item == null || !IsSeries || IsSeasonsLoading) return;

        var contentId = Item.ContentId;
        var generation = Interlocked.Increment(ref _seasonsLoadGeneration);

        IsSeasonsLoading = true;
        SeasonsLoadFailed = false;
        try
        {
            var response = await _catalogApi.GetSeasonsAsync(contentId);
            if (generation != Volatile.Read(ref _seasonsLoadGeneration)
                || Item?.ContentId != contentId) return;
            Seasons.Clear();
            foreach (var season in response.Seasons)
                Seasons.Add(season);

            // Auto-select the first season
            if (Seasons.Count > 0)
                await SelectSeasonAsync(Seasons[0].SeasonNumber);
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _seasonsLoadGeneration)
                || Item?.ContentId != contentId) return;
            SeasonsLoadFailed = true;
            System.Diagnostics.Debug.WriteLine($"Seasons load failed for {contentId}: {ex}");
        }
        finally
        {
            if (generation == Volatile.Read(ref _seasonsLoadGeneration)
                && Item?.ContentId == contentId)
                IsSeasonsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SelectSeasonAsync(int seasonNumber)
    {
        if (Item == null || seasonNumber < 0) return;

        var contentId = Item.ContentId;
        var generation = Interlocked.Increment(ref _episodesLoadGeneration);

        SelectedSeasonNumber = seasonNumber;
        IsEpisodesLoading = true;
        EpisodesLoadFailed = false;
        Episodes.Clear();

        try
        {
            var response = await _catalogApi.GetEpisodesAsync(contentId, seasonNumber);
            if (generation != Volatile.Read(ref _episodesLoadGeneration)
                || Item?.ContentId != contentId) return;
            foreach (var episode in response.Episodes)
                Episodes.Add(episode);
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _episodesLoadGeneration)
                || Item?.ContentId != contentId) return;
            EpisodesLoadFailed = true;
            System.Diagnostics.Debug.WriteLine($"Episode load failed for season {seasonNumber}: {ex}");
        }
        finally
        {
            if (generation == Volatile.Read(ref _episodesLoadGeneration)
                && Item?.ContentId == contentId)
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
