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
public static partial class MediaItemCardActions
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
            toast.Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex));
        }
    }

    public static async Task ResetProgressAsync(MediaItem item)
    {
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var toast = App.Services.GetRequiredService<ToastService>();
        var context = catalog.CaptureContext();
        var identity = catalog.CaptureIdentityContext();
        try
        {
            await MediaItemActionExecutor.ResetProgressAsync(item, async () =>
            {
                await catalog.ClearResumePositionAsync(item.ContentId);
                if (!catalog.IsCurrentContext(context)) throw new OperationCanceledException();
            });
            App.Services.GetService<ItemDetailPrefetchCache>()?.Invalidate(item.ContentId);
            WeakReferenceMessenger.Default.Send(new PlaybackProgressUpdated(item.ContentId, 0,
                item.DurationSeconds ?? 0, item.UserState?.Played == true) { Identity = identity });
            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(MediaSurfaceChangeKind.PlaybackProgress, item.ContentId, item.SeriesId));
            toast.Success("Progress reset");
        }
        catch (OperationCanceledException) when (!catalog.IsCurrentContext(context)) { }
        catch (Exception ex) { toast.Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex)); }
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
            toast.Success(isFavorite ? SiloPlayer.Core.Localization.AppStrings.Get("web.common.toast.favorites.added", fallback: "Added to favorites") : SiloPlayer.Core.Localization.AppStrings.Get("web.common.toast.favorites.removed", fallback: "Removed from favorites"));
        }
        catch (Exception ex)
        {
            toast.Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex));
        }
    }

    public static string GetWatchedActionLabel(string type, bool played) => type switch
    {
        "series" => played ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markSeriesUnwatched", fallback: "Mark Series Unwatched") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markSeriesWatched", fallback: "Mark Series Watched"),
        "season" => played ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markSeasonUnwatched", fallback: "Mark Season Unwatched") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markSeasonWatched", fallback: "Mark Season Watched"),
        "audiobook" => played ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markUnlistened", fallback: "Mark Unlistened") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markListened", fallback: "Mark Listened"),
        "ebook" or "manga" => played ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markUnread", fallback: "Mark Unread") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markRead", fallback: "Mark Read"),
        _ => played ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markUnwatched", fallback: "Mark Unwatched") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markWatched", fallback: "Mark Watched"),
    };

    private static string GetWatchedToastMessage(string type, bool played) => type switch
    {
        "audiobook" => played ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markedListened", fallback: "Marked as listened") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markedUnlistened", fallback: "Marked as unlistened"),
        "ebook" or "manga" => played ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markedRead", fallback: "Marked as read") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markedUnread", fallback: "Marked as unread"),
        _ => played ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markedWatched", fallback: "Marked as watched") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.markedUnwatched", fallback: "Marked as unwatched"),
    };
}
