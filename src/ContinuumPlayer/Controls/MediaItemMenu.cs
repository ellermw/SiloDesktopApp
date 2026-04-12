using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls.Primitives;
using ContinuumPlayer.Core.Api;
using ContinuumPlayer.Core.Models.Home;
using ContinuumPlayer.Core.Services;
using ContinuumPlayer.Helpers;
using ContinuumPlayer.Messaging;
using ContinuumPlayer.Services;
using ContinuumPlayer.Views;
using CommunityToolkit.Mvvm.Messaging;

namespace ContinuumPlayer.Controls;

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

        bool isAdmin = authService.CurrentUser?.Role == "admin";
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
                        if (item.UserState != null) item.UserState.Played = false;
                        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                            MediaSurfaceChangeKind.WatchedCleared, item.ContentId, item.SeriesId));
                        toast.Success($"Marked unwatched: {item.Title}");
                    }
                    else
                    {
                        await catalog.MarkWatchedAsync(item.ContentId);
                        if (item.UserState != null) item.UserState.Played = true;
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
                        if (item.UserState != null) item.UserState.IsFavorite = false;
                        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                            MediaSurfaceChangeKind.FavoriteRemoved, item.ContentId, item.SeriesId));
                        toast.Success("Removed from favorites");
                    }
                    else
                    {
                        await catalog.AddFavoriteAsync(item.ContentId);
                        if (item.UserState != null) item.UserState.IsFavorite = true;
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
                        if (item.UserState != null) item.UserState.InWatchlist = false;
                        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                            MediaSurfaceChangeKind.WatchlistRemoved, item.ContentId, item.SeriesId));
                        toast.Success("Removed from watchlist");
                    }
                    else
                    {
                        await catalog.AddToWatchlistAsync(item.ContentId);
                        if (item.UserState != null) item.UserState.InWatchlist = true;
                        WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                            MediaSurfaceChangeKind.WatchlistAdded, item.ContentId, item.SeriesId));
                        toast.Success("Added to watchlist");
                    }
                }
                catch (Exception ex) { toast.Error(ex.Message); }
            }));

        // ─── Dismiss from Continue Watching / Next Up ────────────────
        if (surface == Surface.ContinueWatching || surface == Surface.NextUp)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            string surfaceKey = surface == Surface.ContinueWatching ? "continue_watching" : "next_up";
            string label = surface == Surface.ContinueWatching
                ? "Dismiss from Continue Watching"
                : "Dismiss from Next Up";
            flyout.Items.Add(BuildItem(label, "\uE711", async () =>
            {
                try
                {
                    var homeApi = App.Services.GetRequiredService<HomeApi>();
                    object body = surfaceKey == "continue_watching"
                        ? new { progress_updated_at = DateTime.UtcNow.ToString("o") }
                        : (object)new { series_id = item.SeriesId ?? item.ContentId };
                    await homeApi.DismissItemAsync(surfaceKey, item.ContentId, body);
                    WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                        MediaSurfaceChangeKind.PlaybackProgress, item.ContentId, item.SeriesId));
                    toast.Info("Dismissed");
                }
                catch (Exception ex) { toast.Error(ex.Message); }
            }));
        }

        // ─── Admin-only actions ──────────────────────────────────────
        if (isAdmin && adminApi != null)
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
