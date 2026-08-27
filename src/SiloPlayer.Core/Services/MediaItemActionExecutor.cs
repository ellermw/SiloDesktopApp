using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Applies optimistic card actions and restores the previous profile state
/// when the server rejects the mutation.
/// </summary>
public static class MediaItemActionExecutor
{
    public static Task<bool> ToggleWatchedAsync(
        MediaItem item,
        Func<bool, Task> persistAsync)
        => ToggleAsync(
            item,
            item.UserState?.Played == true,
            MediaItemStateUpdater.SetWatched,
            persistAsync);

    public static Task<bool> ToggleFavoriteAsync(
        MediaItem item,
        Func<bool, Task> persistAsync)
        => ToggleAsync(
            item,
            item.UserState?.IsFavorite == true,
            MediaItemStateUpdater.SetFavorite,
            persistAsync);

    private static async Task<bool> ToggleAsync(
        MediaItem item,
        bool previousValue,
        Func<MediaItem, bool, bool> apply,
        Func<bool, Task> persistAsync)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(persistAsync);

        var nextValue = !previousValue;
        apply(item, nextValue);
        try
        {
            await persistAsync(nextValue);
            return nextValue;
        }
        catch
        {
            apply(item, previousValue);
            throw;
        }
    }
}
