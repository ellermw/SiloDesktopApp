using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;
using SiloPlayer.Messaging;
using SiloPlayer.Services;
using SiloPlayer.Views;

namespace SiloPlayer.Controls;

/// <summary>
/// Builds the card action menu used by the current WebUI's PosterCard and
/// LandscapeCard surfaces. The action model, ordering, and administrator
/// gate intentionally mirror web/src/components/MediaItemMenu.tsx.
/// </summary>
public static class MediaItemMenu
{
    public enum Surface
    {
        Default,
        ContinueWatching,
        NextUp,
    }

    public static MenuFlyout Build(
        MediaItem item,
        Surface surface = Surface.Default,
        bool showCollectionActions = true,
        Action? stateChanged = null)
    {
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var adminApi = TryGetAdminApi();
        var authService = App.Services.GetRequiredService<AuthService>();
        var toast = App.Services.GetRequiredService<ToastService>();

        bool isActingAdmin = AuthorizationPolicy.IsActingAdmin(authService);
        bool isWatched = item.UserState?.Played ?? false;
        bool isFavorite = item.UserState?.IsFavorite ?? false;
        bool inWatchlist = item.UserState?.InWatchlist ?? false;
        bool isLeaf = item.Type is "movie" or "episode" or "audiobook";
        bool hasPartialProgress = item.PositionSeconds is > 0 && !isWatched;

        // Current WebUI order: restart, watched state, collection actions,
        // manga details, admin actions, then surface-specific dismissal.
        if (isLeaf && (hasPartialProgress || isWatched))
        {
            flyout.Items.Add(BuildItem(
                item.Type == "audiobook" ? "Listen from Beginning" : "Play from Beginning",
                "\uE768",
                async () =>
                {
                    try
                    {
                        await App.Services.GetRequiredService<PlayerService>()
                            .PlayAsync(item.ContentId, fromStart: true);
                    }
                    catch (Exception ex) { toast.Error(ex.Message); }
                }));
        }

        if (item.UserState != null)
        {
            MenuFlyoutItem watchedAction = null!;
            watchedAction = BuildItem(
                GetWatchedActionLabel(item.Type, isWatched),
                isWatched ? "\uE711" : "\uE73E",
                async () =>
                {
                    try
                    {
                        var currentlyWatched = item.UserState?.Played == true;
                        if (currentlyWatched)
                        {
                            await catalog.MarkUnwatchedAsync(item.ContentId);
                            MediaItemStateUpdater.SetWatched(item, false);
                            stateChanged?.Invoke();
                            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                                MediaSurfaceChangeKind.WatchedCleared, item.ContentId, item.SeriesId));
                            toast.Success(GetWatchedToastMessage(item.Type, false));
                        }
                        else
                        {
                            await catalog.MarkWatchedAsync(item.ContentId);
                            MediaItemStateUpdater.SetWatched(item, true);
                            stateChanged?.Invoke();
                            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                                MediaSurfaceChangeKind.WatchedMarked, item.ContentId, item.SeriesId));
                            toast.Success(GetWatchedToastMessage(item.Type, true));
                        }
                        var nowWatched = item.UserState?.Played == true;
                        watchedAction.Text = GetWatchedActionLabel(item.Type, nowWatched);
                        watchedAction.Icon = new FontIcon { Glyph = nowWatched ? "\uE711" : "\uE73E" };
                        AutomationProperties.SetName(watchedAction, watchedAction.Text);
                    }
                    catch (Exception ex) { toast.Error(ex.Message); }
                });
            flyout.Items.Add(watchedAction);

            if (showCollectionActions)
            {
                MenuFlyoutItem favoriteAction = null!;
                favoriteAction = BuildItem(
                    isFavorite ? "Remove from Favorites" : "Add to Favorites",
                    "\uEB52",
                    async () =>
                    {
                        try
                        {
                            var currentlyFavorite = item.UserState?.IsFavorite == true;
                            if (currentlyFavorite)
                            {
                                await catalog.RemoveFavoriteAsync(item.ContentId);
                                MediaItemStateUpdater.SetFavorite(item, false);
                                stateChanged?.Invoke();
                                WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                                    MediaSurfaceChangeKind.FavoriteRemoved, item.ContentId, item.SeriesId));
                                toast.Success("Removed from favorites");
                            }
                            else
                            {
                                await catalog.AddFavoriteAsync(item.ContentId);
                                MediaItemStateUpdater.SetFavorite(item, true);
                                stateChanged?.Invoke();
                                WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                                    MediaSurfaceChangeKind.FavoriteAdded, item.ContentId, item.SeriesId));
                                toast.Success("Added to favorites");
                            }
                            favoriteAction.Text = item.UserState?.IsFavorite == true
                                ? "Remove from Favorites"
                                : "Add to Favorites";
                            AutomationProperties.SetName(favoriteAction, favoriteAction.Text);
                        }
                        catch (Exception ex) { toast.Error(ex.Message); }
                    });
                flyout.Items.Add(favoriteAction);

                MenuFlyoutItem watchlistAction = null!;
                watchlistAction = BuildItem(
                    inWatchlist ? "Remove from Watchlist" : "Add to Watchlist",
                    inWatchlist ? "\uE73E" : "\uE710",
                    async () =>
                    {
                        try
                        {
                            var currentlyInWatchlist = item.UserState?.InWatchlist == true;
                            if (currentlyInWatchlist)
                            {
                                await catalog.RemoveFromWatchlistAsync(item.ContentId);
                                MediaItemStateUpdater.SetWatchlist(item, false);
                                stateChanged?.Invoke();
                                WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                                    MediaSurfaceChangeKind.WatchlistRemoved, item.ContentId, item.SeriesId));
                                toast.Success("Removed from watchlist");
                            }
                            else
                            {
                                await catalog.AddToWatchlistAsync(item.ContentId);
                                MediaItemStateUpdater.SetWatchlist(item, true);
                                stateChanged?.Invoke();
                                WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                                    MediaSurfaceChangeKind.WatchlistAdded, item.ContentId, item.SeriesId));
                                toast.Success("Added to watchlist");
                            }
                            var nowInWatchlist = item.UserState?.InWatchlist == true;
                            watchlistAction.Text = nowInWatchlist
                                ? "Remove from Watchlist"
                                : "Add to Watchlist";
                            watchlistAction.Icon = new FontIcon { Glyph = nowInWatchlist ? "\uE73E" : "\uE710" };
                            AutomationProperties.SetName(watchlistAction, watchlistAction.Text);
                        }
                        catch (Exception ex) { toast.Error(ex.Message); }
                    });
                flyout.Items.Add(watchlistAction);
            }
        }

        if (item.Type == "manga")
        {
            flyout.Items.Add(BuildItem("View Details", "\uE946", async () =>
            {
                var root = App.MainWindowInstance?.Content.XamlRoot;
                if (root == null) return;
                await new MangaFilesDialog(item.ContentId, item.Title) { XamlRoot = root }.ShowAsync();
            }));
        }

        if (isActingAdmin && adminApi != null)
        {
            if (flyout.Items.Count > 0)
                flyout.Items.Add(new MenuFlyoutSeparator());

            flyout.Items.Add(BuildItem("View Play History", "\uE81C", () =>
            {
                App.MainWindowInstance?.RestoreMainPane();
                App.Services.GetRequiredService<NavigationService>()
                    .Navigate<Views.Admin.AdminShellPage>(new Views.Admin.AdminShellNavigation(
                        typeof(Views.Admin.AdminPlaybackHistoryPage),
                        new Views.Admin.AdminPlaybackHistoryFilter(MediaItemId: item.ContentId)));
            }));

            flyout.Items.Add(BuildItem("Refresh Metadata", "\uE72C", async () =>
                await ShowRefreshMetadataDialogAsync(item, adminApi, toast)));
        }

        var canDismiss = surface == Surface.ContinueWatching
            ? !string.IsNullOrWhiteSpace(item.ProgressUpdatedAt)
            : surface == Surface.NextUp && !string.IsNullOrWhiteSpace(item.SeriesId);
        if (canDismiss)
        {
            if (flyout.Items.Count > 0)
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

        return flyout;
    }

    private static string GetWatchedActionLabel(string type, bool played) => type switch
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

    private static async Task ShowRefreshMetadataDialogAsync(
        MediaItem item,
        AdminApi adminApi,
        ToastService toast)
    {
        var root = App.MainWindowInstance?.Content.XamlRoot;
        if (root == null) return;

        var dialog = new RefreshMetadataDialog(async mode =>
        {
            await adminApi.RefreshItemMetadataAsync(item.ContentId, mode);
            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                MediaSurfaceChangeKind.ItemMetadataRefreshed, item.ContentId, item.SeriesId));
            toast.Success(mode == "complete" ? "Complete refresh queued" : "Metadata refresh queued");
        })
        {
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    private static MenuFlyoutItem BuildItem(string text, string glyph, Action onClick)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph },
        };
        AutomationProperties.SetName(item, text);
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
        AutomationProperties.SetName(item, text);
        item.Click += async (_, _) =>
        {
            if (!item.IsEnabled) return;
            item.IsEnabled = false;
            try
            {
                await onClickAsync();
            }
            finally
            {
                item.IsEnabled = true;
            }
        };
        return item;
    }

    private static AdminApi? TryGetAdminApi()
    {
        try { return App.Services.GetRequiredService<AdminApi>(); }
        catch { return null; }
    }
}
