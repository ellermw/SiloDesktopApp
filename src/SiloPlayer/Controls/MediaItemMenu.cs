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
        bool canCurateMetadata = AuthorizationPolicy.CanCurateMetadata(authService);
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
                MediaItemCardActions.GetWatchedActionLabel(item.Type, isWatched),
                isWatched ? "\uE711" : "\uE73E",
                async () =>
                {
                    try
                    {
                        await MediaItemCardActions.ToggleWatchedAsync(item);
                        stateChanged?.Invoke();
                        var nowWatched = item.UserState?.Played == true;
                        watchedAction.Text = MediaItemCardActions.GetWatchedActionLabel(item.Type, nowWatched);
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
                        await MediaItemCardActions.ToggleFavoriteAsync(item);
                        stateChanged?.Invoke();
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

        if ((isActingAdmin || canCurateMetadata) && adminApi != null)
        {
            if (flyout.Items.Count > 0)
                flyout.Items.Add(new MenuFlyoutSeparator());

            if (isActingAdmin)
            {
                flyout.Items.Add(BuildItem("View Play History", "\uE81C", () =>
                {
                    App.MainWindowInstance?.RestoreMainPane();
                    App.Services.GetRequiredService<NavigationService>()
                        .Navigate<Views.Admin.AdminShellPage>(new Views.Admin.AdminShellNavigation(
                            typeof(Views.Admin.AdminPlaybackHistoryPage),
                            new Views.Admin.AdminPlaybackHistoryFilter(MediaItemId: item.ContentId)));
                }));
            }

            if (canCurateMetadata)
            {
                flyout.Items.Add(BuildItem("Refresh Metadata", "\uE72C", async () =>
                    await ShowRefreshMetadataDialogAsync(item, adminApi, toast)));

                if (item.Type is "movie" or "series")
                {
                    flyout.Items.Add(BuildItem("Edit Metadata", "\uE70F", async () =>
                        await ShowEditMetadataDialogAsync(item, catalog, toast, stateChanged)));
                    flyout.Items.Add(BuildItem("Match Item", "\uE721", async () =>
                        await ShowMatchItemDialogAsync(item, catalog, toast, stateChanged)));
                }
            }
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

    private static async Task ShowEditMetadataDialogAsync(
        MediaItem item,
        CatalogApi catalog,
        ToastService toast,
        Action? stateChanged)
    {
        var root = App.MainWindowInstance?.Content.XamlRoot;
        if (root == null) return;

        try
        {
            var detail = await catalog.GetItemDetailAsync(item.ContentId);
            var dialog = new EditMetadataDialog(detail) { XamlRoot = root };
            await dialog.ShowAsync();
            if (!dialog.HasAppliedChanges) return;

            stateChanged?.Invoke();
            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                MediaSurfaceChangeKind.ItemMetadataRefreshed,
                item.ContentId,
                item.SeriesId));
        }
        catch (Exception ex)
        {
            toast.Error(ex.Message);
        }
    }

    private static async Task ShowMatchItemDialogAsync(
        MediaItem item,
        CatalogApi catalog,
        ToastService toast,
        Action? stateChanged)
    {
        var root = App.MainWindowInstance?.Content.XamlRoot;
        if (root == null) return;

        try
        {
            var detail = await catalog.GetItemDetailAsync(item.ContentId);
            var dialog = new MatchItemDialog(
                detail.ContentId,
                detail.Title,
                detail.Year > 0 ? detail.Year : null,
                detail.Type,
                libraryId: null,
                versions: detail.Versions,
                folderPaths: detail.FolderPaths)
            {
                XamlRoot = root,
            };
            await dialog.ShowAsync();
            if (dialog.HasAppliedMatch)
            {
                stateChanged?.Invoke();
                WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                    MediaSurfaceChangeKind.ItemMetadataRefreshed,
                    item.ContentId,
                    item.SeriesId));
            }
        }
        catch (Exception ex)
        {
            toast.Error(ex.Message);
        }
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
