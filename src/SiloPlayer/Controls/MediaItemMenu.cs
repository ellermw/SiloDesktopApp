using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.Views;
using CommunityToolkit.Mvvm.Messaging;

namespace SiloPlayer.Controls;

/// <summary>
/// F10: Builds a context menu (<see cref="MenuFlyout"/>) for a media item
/// card. Mirrors the webui <c>MediaItemMenu</c> used on PosterCard,
/// LandscapeCard, and ContinueWatchingCard. Exposed as a static helper so it
/// can be attached to any card's <c>ContextFlyout</c> without each control
/// re-implementing the action list.
///
/// Actions published via <see cref="WeakReferenceMessenger"/> so the F4
/// subscribers (Home / Favorites / Watchlist / History) react in-place.
///
/// Admin-only entries (Refresh Metadata, View Play History) render only
/// when the current user's role is "admin".
/// </summary>
public static class MediaItemMenu
{
    public enum Surface
    {
        /// <summary>Generic card on home sections (recently added, popular, etc.).</summary>
        Default,
        /// <summary>Continue Watching row — adds a Dismiss entry.</summary>
        ContinueWatching,
        /// <summary>Next Up row — adds a Dismiss entry.</summary>
        NextUp,
    }

    /// <summary>
    /// Build a fresh <see cref="MenuFlyout"/> for the given item. A new
    /// flyout is created per call because <see cref="FlyoutBase"/> instances
    /// can only have one host at a time.
    /// </summary>
    public static MenuFlyout Build(MediaItem item, Surface surface = Surface.Default)
    {
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var adminApi = TryGetAdminApi();
        var authService = App.Services.GetRequiredService<AuthService>();
        var toast = App.Services.GetRequiredService<ToastService>();

        bool canCurateMetadata = AuthorizationPolicy.CanCurateMetadata(authService);
        bool isWatched = item.UserState?.Played ?? false;
        bool isFavorite = item.UserState?.IsFavorite ?? false;
        bool inWatchlist = item.UserState?.InWatchlist ?? false;

        // ─── Mark Watched / Unwatched ────────────────────────────────
        flyout.Items.Add(BuildItem(
            isWatched ? "Mark Unwatched" : "Mark Watched",
            isWatched ? "\uE711" : "\uE73E",
            async () =>
            {
                try
                {
                    if (isWatched)
                    {
                        await catalog.MarkUnwatchedAsync(item.ContentId);
                        MediaItemStateUpdater.SetWatched(item, false);
                        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                            MediaSurfaceChangeKind.WatchedCleared, item.ContentId, item.SeriesId));
                        toast.Success($"Marked unwatched: {item.Title}");
                    }
                    else
                    {
                        await catalog.MarkWatchedAsync(item.ContentId);
                        MediaItemStateUpdater.SetWatched(item, true);
                        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                            MediaSurfaceChangeKind.WatchedMarked, item.ContentId, item.SeriesId));
                        toast.Success($"Marked watched: {item.Title}");
                    }
                }
                catch (Exception ex) { toast.Error(ex.Message); }
            }));

        // ─── Favorite toggle ─────────────────────────────────────────
        flyout.Items.Add(BuildItem(
            isFavorite ? "Remove from Favorites" : "Add to Favorites",
            "\uEB52", // heart filled
            async () =>
            {
                try
                {
                    if (isFavorite)
                    {
                        await catalog.RemoveFavoriteAsync(item.ContentId);
                        MediaItemStateUpdater.SetFavorite(item, false);
                        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                            MediaSurfaceChangeKind.FavoriteRemoved, item.ContentId, item.SeriesId));
                        toast.Success("Removed from favorites");
                    }
                    else
                    {
                        await catalog.AddFavoriteAsync(item.ContentId);
                        MediaItemStateUpdater.SetFavorite(item, true);
                        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                            MediaSurfaceChangeKind.FavoriteAdded, item.ContentId, item.SeriesId));
                        toast.Success("Added to favorites");
                    }
                }
                catch (Exception ex) { toast.Error(ex.Message); }
            }));

        // ─── Watchlist toggle ────────────────────────────────────────
        flyout.Items.Add(BuildItem(
            inWatchlist ? "Remove from Watchlist" : "Add to Watchlist",
            inWatchlist ? "\uE73E" : "\uE710", // check / plus
            async () =>
            {
                try
                {
                    if (inWatchlist)
                    {
                        await catalog.RemoveFromWatchlistAsync(item.ContentId);
                        MediaItemStateUpdater.SetWatchlist(item, false);
                        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                            MediaSurfaceChangeKind.WatchlistRemoved, item.ContentId, item.SeriesId));
                        toast.Success("Removed from watchlist");
                    }
                    else
                    {
                        await catalog.AddToWatchlistAsync(item.ContentId);
                        MediaItemStateUpdater.SetWatchlist(item, true);
                        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                            MediaSurfaceChangeKind.WatchlistAdded, item.ContentId, item.SeriesId));
                        toast.Success("Added to watchlist");
                    }
                }
                catch (Exception ex) { toast.Error(ex.Message); }
            }));

        // ─── Dismiss from Continue Watching / Next Up ────────────────
        var canDismiss = surface == Surface.ContinueWatching
            ? !string.IsNullOrWhiteSpace(item.ProgressUpdatedAt)
            : surface == Surface.NextUp && !string.IsNullOrWhiteSpace(item.SeriesId);
        if (canDismiss)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            string surfaceKey = surface == Surface.ContinueWatching ? "continue_watching" : "next_up";
            string label = surface == Surface.ContinueWatching
                ? item.Type == "audiobook"
                    ? "Remove from Continue Listening"
                    : item.Type == "ebook"
                        ? "Remove from Continue Reading"
                        : "Remove from Continue Watching"
                : "Remove from Next Up";
            flyout.Items.Add(BuildItem(label, "\uE711", async () =>
            {
                try
                {
                    var homeApi = App.Services.GetRequiredService<HomeApi>();
                    object body = surfaceKey == "continue_watching"
                        ? new { progress_updated_at = item.ProgressUpdatedAt }
                        : (object)new { series_id = item.SeriesId! };
                    await homeApi.DismissItemAsync(surfaceKey, item.ContentId, body);
                    WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                        MediaSurfaceChangeKind.HomeDismissed, item.ContentId, item.SeriesId));
                    toast.Info("Dismissed");
                }
                catch (Exception ex) { toast.Error(ex.Message); }
            }));
        }

        // ─── Admin-only actions ──────────────────────────────────────
        if (canCurateMetadata && adminApi != null)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(BuildItem("Refresh Metadata", "\uE72C", async () =>
            {
                try
                {
                    await adminApi.RefreshItemMetadataAsync(item.ContentId);
                    WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                        MediaSurfaceChangeKind.ItemMetadataRefreshed, item.ContentId, item.SeriesId));
                    toast.Success("Metadata refresh queued");
                }
                catch (Exception ex) { toast.Error(ex.Message); }
            }));
        }

        // ─── Open details ────────────────────────────────────────────
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildItem("View Details", "\uE946", () =>
        {
            var nav = App.Services.GetRequiredService<NavigationService>();
            var target = !string.IsNullOrEmpty(item.SeriesId) ? item.SeriesId : item.ContentId;
            nav.Navigate<ItemDetailPage>(target);
        }));

        return flyout;
    }

    private static MenuFlyoutItem BuildItem(string text, string glyph, Action onClick)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph },
        };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static MenuFlyoutItem BuildItem(string text, string glyph, Func<Task> onClickAsync)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph },
        };
        item.Click += async (_, _) => await onClickAsync();
        return item;
    }

    /// <summary>AdminApi is only registered when the current user is admin;
    /// return null when the service lookup fails so non-admin builds don't
    /// crash.</summary>
    private static AdminApi? TryGetAdminApi()
    {
        try { return App.Services.GetRequiredService<AdminApi>(); }
        catch { return null; }
    }
}
