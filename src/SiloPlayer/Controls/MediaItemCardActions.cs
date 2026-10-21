using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Messaging;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

/// <summary>
/// Executes the shared quick-action and overflow-menu mutations used by media
/// cards. Profile state changes optimistically and rolls back on failure.
/// </summary>
public static class MediaItemCardActions
{
    public static async Task ToggleWatchedAsync(MediaItem item)
    {
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var toast = App.Services.GetRequiredService<ToastService>();
        var context = catalog.CaptureContext();
        try
        {
            var isWatched = await MediaItemActionExecutor.ToggleWatchedAsync(
                item,
                async nextValue =>
                {
                    if (nextValue) await catalog.MarkWatchedAsync(item.ContentId);
                    else await catalog.MarkUnwatchedAsync(item.ContentId);
                    if (!catalog.IsCurrentContext(context)) throw new OperationCanceledException();
                });

            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                isWatched
                    ? MediaSurfaceChangeKind.WatchedMarked
                    : MediaSurfaceChangeKind.WatchedCleared,
                item.ContentId,
                item.SeriesId, context: context));
            toast.Success(GetWatchedToastMessage(item.Type, isWatched));
        }
        catch (OperationCanceledException) when (!catalog.IsCurrentContext(context)) { }
        catch (Exception ex)
        {
            toast.Error(ex);
        }
    }

    public static async Task ResetProgressAsync(MediaItem item)
    {
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var toast = App.Services.GetRequiredService<ToastService>();
        var context = catalog.CaptureContext();
        try
        {
            await MediaItemActionExecutor.ResetProgressAsync(item, async () =>
            {
                await catalog.ClearResumePositionAsync(item.ContentId);
                if (!catalog.IsCurrentContext(context)) throw new OperationCanceledException();
            });
            App.Services.GetService<ItemDetailPrefetchCache>()?.Invalidate(item.ContentId);
            WeakReferenceMessenger.Default.Send(new PlaybackProgressUpdated(item.ContentId, 0,
                item.DurationSeconds ?? 0, item.UserState?.Played == true));
            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.PlaybackProgress, item.ContentId, item.SeriesId));
            toast.Success("Progress reset");
        }
        catch (OperationCanceledException) when (!catalog.IsCurrentContext(context)) { }
        catch (Exception ex) { toast.Error(ex); }
    }

    public static async Task ToggleFavoriteAsync(MediaItem item)
    {
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var toast = App.Services.GetRequiredService<ToastService>();
        try
        {
            var isFavorite = await MediaItemActionExecutor.ToggleFavoriteAsync(
                item,
                nextValue => nextValue
                    ? catalog.AddFavoriteAsync(item.ContentId)
                    : catalog.RemoveFavoriteAsync(item.ContentId));

            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                isFavorite
                    ? MediaSurfaceChangeKind.FavoriteAdded
                    : MediaSurfaceChangeKind.FavoriteRemoved,
                item.ContentId,
                item.SeriesId));
            toast.Success(isFavorite ? "Added to favorites" : "Removed from favorites");
        }
        catch (Exception ex)
        {
            toast.Error(ex);
        }
    }

    public static string GetWatchedActionLabel(string type, bool played) => type switch
    {
        "series" => played ? "Mark Series Unwatched" : "Mark Series Watched",
        "season" => played ? "Mark Season Unwatched" : "Mark Season Watched",
        "audiobook" => played ? "Mark Unlistened" : "Mark Listened",
        "ebook" or "manga" => played ? "Mark Unread" : "Mark Read",
        _ => played ? "Mark Unwatched" : "Mark Watched",
    };

    private static string GetWatchedToastMessage(string type, bool played) => type switch
    {
        "audiobook" => played ? "Marked as listened" : "Marked as unlistened",
        "ebook" or "manga" => played ? "Marked as read" : "Marked as unread",
        _ => played ? "Marked as watched" : "Marked as unwatched",
    };
}
