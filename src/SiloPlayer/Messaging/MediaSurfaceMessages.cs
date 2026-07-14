namespace SiloPlayer.Messaging;

/// <summary>
/// Reason a media surface changed. Used by subscribers to decide what to
/// refresh. Matches the webui <c>mediaSurfaceRefresh</c> event taxonomy.
/// </summary>
public enum MediaSurfaceChangeKind
{
    FavoriteAdded,
    FavoriteRemoved,
    WatchlistAdded,
    WatchlistRemoved,
    WatchedMarked,
    WatchedCleared,
    RatingChanged,
    PlaybackProgress,
    HomeDismissed,
    ItemMetadataRefreshed,
    CollectionChanged,
}

/// <summary>
/// Published when a media item's state changes anywhere in the app
/// (ItemDetailPage, FavoritesPage, WatchlistPage, player, context menus, etc.).
/// Subscribers (HomeViewModel, FavoritesViewModel, WatchlistViewModel,
/// HistoryViewModel, RecommendationsViewModel) should react by invalidating
/// caches, adjusting optimistic collections, or scheduling a refresh.
///
/// This is the F4 foundational piece referenced throughout the parity doc.
/// </summary>
public sealed class MediaSurfaceChanged
{
    public MediaSurfaceChangeKind Kind { get; }

    /// <summary>
    /// Content ID of the affected item (or series root if a season/episode
    /// changed). Subscribers that track item-level state match on this.
    /// </summary>
    public string ContentId { get; }

    /// <summary>
    /// Optional series ID when the change came from an episode/season. Lets
    /// subscribers cascade refreshes across sibling items (e.g. marking an
    /// episode watched should invalidate the season card).
    /// </summary>
    public string? SeriesId { get; }

    /// <summary>
    /// Optional numeric rating value for <see cref="MediaSurfaceChangeKind.RatingChanged"/>.
    /// Null for "rating cleared".
    /// </summary>
    public int? Rating { get; }

    public MediaSurfaceChanged(MediaSurfaceChangeKind kind, string contentId, string? seriesId = null, int? rating = null)
    {
        Kind = kind;
        ContentId = contentId;
        SeriesId = seriesId;
        Rating = rating;
    }
}

/// <summary>
/// Published by PlayerService when a playback session stops (final position
/// committed to the server). Subscribers use this to update Continue Watching
/// rows in-place so returning from the player shows the latest progress without
/// waiting for a full refetch.
/// </summary>
public sealed class PlaybackProgressUpdated
{
    public string ContentId { get; }
    public double PositionSeconds { get; }
    public double DurationSeconds { get; }
    public bool Completed { get; }
    public DateTime UpdatedAt { get; }

    public PlaybackProgressUpdated(string contentId, double position, double duration, bool completed)
    {
        ContentId = contentId;
        PositionSeconds = position;
        DurationSeconds = duration;
        Completed = completed;
        UpdatedAt = DateTime.UtcNow;
    }
}
