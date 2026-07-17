using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Applies profile-scoped state changes to an already-loaded browse item.
/// Keeping this logic in one place ensures context menus, virtualized library
/// windows, and catalog surfaces agree without refetching an entire page.
/// </summary>
public static class MediaItemStateUpdater
{
    public static bool SetFavorite(MediaItem item, bool value)
    {
        var state = EnsureUserState(item);
        if (state.IsFavorite == value) return false;
        state.IsFavorite = value;
        return true;
    }

    public static bool SetWatchlist(MediaItem item, bool value)
    {
        var state = EnsureUserState(item);
        if (state.InWatchlist == value) return false;
        state.InWatchlist = value;
        return true;
    }

    public static bool SetWatched(MediaItem item, bool value)
    {
        var state = EnsureUserState(item);
        if (state.Played == value) return false;
        state.Played = value;
        return true;
    }

    public static bool SetPlaybackProgress(
        MediaItem item,
        double positionSeconds,
        double durationSeconds,
        bool completed,
        DateTime updatedAt)
    {
        var normalizedPosition = Math.Max(0, positionSeconds);
        var normalizedDuration = durationSeconds > 0
            ? durationSeconds
            : item.DurationSeconds.GetValueOrDefault();
        var changed = item.PositionSeconds != normalizedPosition
            || (normalizedDuration > 0 && item.DurationSeconds != normalizedDuration)
            || item.UserState?.Played != completed;

        item.PositionSeconds = normalizedPosition;
        if (normalizedDuration > 0)
            item.DurationSeconds = normalizedDuration;
        item.ProgressUpdatedAt = updatedAt.ToUniversalTime().ToString("o");
        EnsureUserState(item).Played = completed;

        if (item.SortMetrics != null && normalizedDuration > 0)
            item.SortMetrics.ProgressRatio = Math.Clamp(normalizedPosition / normalizedDuration, 0, 1);

        return changed;
    }

    private static UserState EnsureUserState(MediaItem item)
        => item.UserState ??= new UserState();
}
