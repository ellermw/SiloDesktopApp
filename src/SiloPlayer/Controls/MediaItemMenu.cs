using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Views.Dialogs;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
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
/// LandscapeCard surfaces. The action model and ordering intentionally mirror
/// web/src/components/MediaItemMenu.tsx while server administration stays in
/// the WebUI.
/// </summary>
public static class MediaItemMenu
{
    public enum Surface
    {
        Default,
        ContinueWatching,
        NextUp,
    }

    /// <summary>
    /// The menu surface a card's row implies. Cards on Home and on a dedicated Continue Watching /
    /// Next Up page share the Home row vocabulary in <see cref="MediaItem.ItemSource"/>.
    /// </summary>
    public static Surface SurfaceFor(MediaItem item) => item.ItemSource switch
    {
        HomeDismissalPolicy.ContinueWatching => Surface.ContinueWatching,
        HomeDismissalPolicy.NextUp => Surface.NextUp,
        _ => Surface.Default,
    };

    public static MenuFlyout Build(
        MediaItem item,
        Surface surface = Surface.Default,
        bool showCollectionActions = true,
        Action? stateChanged = null,
        FrameworkElement? owner = null)
    {
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.Bottom };
        if (item.Type == "box_set") return flyout;
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var maintenanceApi = App.Services.GetRequiredService<MediaMaintenanceApi>();
        var authService = App.Services.GetRequiredService<AuthService>();
        var toast = App.Services.GetRequiredService<ToastService>();
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var menuOwner = client.CaptureIdentityContext();
        XamlRoot? DialogRoot() => owner?.XamlRoot ?? App.MainWindowInstance?.Content.XamlRoot;

        bool canCurateMetadata = AuthorizationPolicy.CanCurateMetadata(authService);
        var maintenance = ItemMaintenanceActionPolicy.Resolve(canCurateMetadata, item.Type);
        bool isWatched = item.UserState?.Played ?? false;
        bool isFavorite = item.UserState?.IsFavorite ?? false;
        bool inWatchlist = item.UserState?.InWatchlist ?? false;
        bool isLeaf = item.Type is "movie" or "episode" or "audiobook";

        // Current WebUI order: restart, watched state, collection actions,
        // manga details, permission-gated maintenance, then surface-specific dismissal.
        if (isLeaf)
        {
            var restartLibrary = owner != null ? MediaNavigationContext.LibraryId(owner) : null;
            var restartIdentity = client.CaptureIdentityContext();
            bool OwnsRestart() => client.IsCurrentIdentityContext(restartIdentity)
                && (owner == null || (owner.IsLoaded && MediaNavigationContext.LibraryId(owner) == restartLibrary
                    && (owner is not IMediaItemOwner itemOwner || OwnsContent(itemOwner.MediaItem, item))));
            var restart = BuildItem(() => item.Type == "audiobook" ? SiloPlayer.Core.Localization.AppStrings.Get("web.item.menu.listenFromBeginning", fallback: "Listen from Beginning") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.playFromBeginning", fallback: "Play from Beginning"),
                "\uE768",
                async () =>
                {
                    if (!OwnsRestart())
                    {
                        toast.Error(new InvalidOperationException("This item is no longer available from this card. Open its menu again."));
                        return;
                    }
                    try
                    {
                        await App.Services.GetRequiredService<PlayerService>()
                            .RestartAsync(item.ContentId, restartLibrary, restartIdentity);
                    }
                    catch (Exception ex) { toast.Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex)); }
                });
            void RefreshRestartAction() => restart.Visibility = item.PositionSeconds is > 0 || item.UserState?.Played == true
                ? Visibility.Visible : Visibility.Collapsed;
            RefreshRestartAction();
            flyout.Items.Add(restart);
            flyout.Opening += (_, _) => RefreshRestartAction();
        }

        if (item.UserState != null)
        {
            MenuFlyoutItem watchedAction = null!;
            watchedAction = BuildItem(() => MediaItemCardActions.GetWatchedActionLabel(item.Type, item.UserState?.Played == true),
                isWatched ? "\uE711" : "\uE73E",
                async () =>
                {
                    try
                    {
                        await MediaItemCardActions.ToggleWatchedAsync(item);
                        stateChanged?.Invoke();
                        RefreshWatchedAction();
                    }
                    catch (Exception ex) { toast.Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex)); }
                });
            void RefreshWatchedAction()
            {
                var nowWatched = item.UserState?.Played == true;
                watchedAction.Text = MediaItemCardActions.GetWatchedActionLabel(item.Type, nowWatched);
                watchedAction.Icon = new FontIcon { Glyph = nowWatched ? "\uE711" : "\uE73E" };
                AutomationProperties.SetName(watchedAction, watchedAction.Text);
            }
            flyout.Items.Add(watchedAction);
            // Retained cards can change remotely without rebuilding their menu.
            flyout.Opening += (_, _) => RefreshWatchedAction();

            if (showCollectionActions)
            {
                MenuFlyoutItem favoriteAction = null!;
                favoriteAction = BuildItem(() => item.UserState?.IsFavorite == true ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.removeFromFavorites", fallback: "Remove from Favorites") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.addToFavorites", fallback: "Add to Favorites"),
                    "\uEB52",
                    async () =>
                    {
                    try
                    {
                        await MediaItemCardActions.ToggleFavoriteAsync(item);
                        stateChanged?.Invoke();
                        SiloPlayer.Controls.MediaItemCardActions.BindText(favoriteAction, "Text", element => element.Text = item.UserState?.IsFavorite == true
                                ? SiloPlayer.Core.Localization.AppStrings.Get("web.item.menu.removeFromFavorites", fallback: "Remove from Favorites")
                                : SiloPlayer.Core.Localization.AppStrings.Get("web.item.menu.addToFavorites", fallback: "Add to Favorites"));
                            favoriteAction.Visibility = MediaItemActionPolicy.CanAddToPersonalLists(item.Type) || item.UserState?.IsFavorite == true ? Visibility.Visible : Visibility.Collapsed;
                            AutomationProperties.SetName(favoriteAction, favoriteAction.Text);
                        }
                        catch (Exception ex) { toast.Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex)); }
                    });
                if (MediaItemActionPolicy.CanAddToPersonalLists(item.Type) || isFavorite)
                    flyout.Items.Add(favoriteAction);

                MenuFlyoutItem watchlistAction = null!;
                watchlistAction = BuildItem(() => item.UserState?.InWatchlist == true ? SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.removeFromWatchlist", fallback: "Remove from Watchlist") : SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.addToWatchlist", fallback: "Add to Watchlist"),
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
                                toast.Success(SiloPlayer.Core.Localization.AppStrings.Get("web.library.watchlist.removed", fallback: "Removed from watchlist"));
                            }
                            else
                            {
                                await catalog.AddToWatchlistAsync(item.ContentId);
                                MediaItemStateUpdater.SetWatchlist(item, true);
                                stateChanged?.Invoke();
                                WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                                    MediaSurfaceChangeKind.WatchlistAdded, item.ContentId, item.SeriesId));
                                toast.Success(SiloPlayer.Core.Localization.AppStrings.Get("web.library.watchlist.added", fallback: "Added to watchlist"));
                            }
                            var nowInWatchlist = item.UserState?.InWatchlist == true;
                            SiloPlayer.Controls.MediaItemCardActions.BindText(watchlistAction, "Text", element => element.Text = nowInWatchlist
                                ? SiloPlayer.Core.Localization.AppStrings.Get("web.item.menu.removeFromWatchlist", fallback: "Remove from Watchlist")
                                : SiloPlayer.Core.Localization.AppStrings.Get("web.item.menu.addToWatchlist", fallback: "Add to Watchlist"));
                            watchlistAction.Icon = new FontIcon { Glyph = nowInWatchlist ? "\uE73E" : "\uE710" };
                            watchlistAction.Visibility = MediaItemActionPolicy.CanAddToPersonalLists(item.Type) || nowInWatchlist ? Visibility.Visible : Visibility.Collapsed;
                            AutomationProperties.SetName(watchlistAction, watchlistAction.Text);
                        }
                        catch (Exception ex) { toast.Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex)); }
                    });
                if (MediaItemActionPolicy.CanAddToPersonalLists(item.Type) || inWatchlist)
                    flyout.Items.Add(watchlistAction);
            }
        }

        if (item.Type is "movie" or "series" && item.UserState != null)
        {
            var interest = NotInterestedActions.CreateMenuItem(item, stateChanged);
            flyout.Items.Add(interest);
            flyout.Opening += async (_, _) => await NotInterestedActions.RefreshMenuItemAsync(interest, item, catalog.CaptureContext());
        }

        // Retained menus must also support a leaf gaining progress after construction.
        if (item.Type is "movie" or "episode")
        {
            var reset = BuildItem(() => "Reset Progress", "\uE777", async () =>
            {
                var context = catalog.CaptureContext();
                if (!client.IsCurrentIdentityContext(menuOwner)) return;
                await MediaItemCardActions.ResetProgressAsync(item);
                if (!catalog.IsCurrentContext(context)) return;
                stateChanged?.Invoke();
            });
            reset.Visibility = Visibility.Collapsed;
            flyout.Items.Insert(Math.Min(2, flyout.Items.Count), reset);
            flyout.Opening += async (_, _) =>
            {
                // A reconnect retires reads, not this retained menu's lifetime.
                var context = catalog.CaptureContext();
                reset.Visibility = Visibility.Collapsed;
                if (!client.IsCurrentIdentityContext(menuOwner)) { reset.IsEnabled = false; return; }
                if (!MediaItemActionPolicy.CanResetProgress(item.Type, item.PositionSeconds, true)) return;
                try
                {
                    var capabilities = await catalog.GetProgressCapabilitiesAsync();
                    if (!catalog.IsCurrentContext(context)) return;
                    reset.Visibility = MediaItemActionPolicy.CanResetProgress(item.Type, item.PositionSeconds, capabilities.ClearResumePosition)
                        ? Visibility.Visible : Visibility.Collapsed;
                }
                catch { if (catalog.IsCurrentContext(context)) reset.Visibility = Visibility.Collapsed; }
            };
        }

        if (item.Type == "manga")
        {
            flyout.Items.Add(BuildItem(() => SiloPlayer.Core.Localization.AppStrings.Get("web.detail.manga.viewDetails", fallback: "View Details"), "\uE946", async () =>
            {
                var root = DialogRoot();
                if (root == null) return;
                await new MangaFilesDialog(item.ContentId, item.Title) { XamlRoot = root }.ShowAsync();
            }));
        }

        if (maintenance.CanMatch || maintenance.CanRefreshMetadata)
        {
            if (flyout.Items.Count > 0)
                flyout.Items.Add(new MenuFlyoutSeparator());

            if (maintenance.CanRefreshMetadata)
            {
                flyout.Items.Add(BuildItem(() => SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.refreshMetadata", fallback: "Refresh Metadata"), "\uE72C", async () =>
                    await ShowRefreshMetadataDialogAsync(item, maintenanceApi, toast, DialogRoot())));
            }

            if (maintenance.CanMatch)
            {
                flyout.Items.Add(BuildItem(() => SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.matchItem", fallback: "Match Item"), "\uE721", async () =>
                    await ShowMatchItemDialogAsync(item, catalog, toast, stateChanged, DialogRoot(), owner)));
            }
        }

        if (canCurateMetadata && item.Type != "manga")
            flyout.Items.Add(BuildItem(() => SiloPlayer.Core.Localization.AppStrings.Get("web.detail.actions.editMetadata", fallback: "Edit Metadata"), "\uE70F", async () =>
            {
                var root = DialogRoot();
                if (root == null || !AuthorizationPolicy.CanCurateMetadata(authService)) return;
                var context = client.CaptureContext();
                try
                {
                    var detail = await catalog.GetItemDetailAsync(item.ContentId);
                    if (!client.IsCurrentContext(context) || (owner != null && !owner.IsLoaded)) return;
                    var dialog = new EditMetadataDialog(detail) { XamlRoot = root };
                    await dialog.ShowAsync();
                    if (!dialog.HasSaved || !client.IsCurrentContext(context)) return;
                    stateChanged?.Invoke();
                    // The editor publishes each successful mutation itself.
                }
                catch (Exception ex) { toast.Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex)); }
            }));
        if (AuthorizationPolicy.IsActingAdmin(authService))
            flyout.Items.Add(BuildItem(() => SiloPlayer.Core.Localization.AppStrings.Get("web.item.menu.viewPlayHistory", fallback: "Play History"), "\uE81C", async () =>
            {
                var root = DialogRoot();
                if (root != null && AuthorizationPolicy.IsActingAdmin(authService)) await new SiloPlayer.Views.Dialogs.PlayHistoryDialog(item.ContentId) { XamlRoot = root }.ShowAsync();
            }));

        var dismissalSeriesId = HomeDismissalPolicy.SeriesIdentity(item);
        var canDismiss = surface == Surface.ContinueWatching
            ? !string.IsNullOrWhiteSpace(item.ProgressUpdatedAt)
            : surface == Surface.NextUp && !string.IsNullOrWhiteSpace(dismissalSeriesId);
        if (canDismiss)
        {
            if (flyout.Items.Count > 0)
                flyout.Items.Add(new MenuFlyoutSeparator());

            string surfaceKey = surface == Surface.ContinueWatching ? "continue_watching" : "next_up";
            var dropsShow = item.Type is "episode" or "series";
            string label = surface == Surface.ContinueWatching
                ? item.Type == "audiobook"
                    ? "Remove from Continue Listening"
                    : item.Type == "ebook"
                        ? "Remove from Continue Reading"
                        : "Remove from Continue Watching"
                : "Remove from Next Up";
            flyout.Items.Add(BuildItem(() => label, "\uE711", async () =>
            {
                var actionId = Guid.NewGuid().ToString("N");
                using var operation = LocalLog.BeginOperation(actionId);
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                var stage = "api_request";
                var serverAccepted = false;
                try
                {
                    var homeApi = App.Services.GetRequiredService<HomeApi>();
                    var context = homeApi.CaptureContext();
                    // The card's id and stamp belong to the identity this menu was built for. Checked
                    // after the context: while that identity stays current, every request context
                    // since Build, including the one the PUT is bound to, has belonged to it.
                    if (!client.IsCurrentIdentityContext(menuOwner))
                    {
                        LocalLog.AppendLine("home_actions.txt", $"dismissal action={actionId} surface={surfaceKey} stage=owner_retired serverAccepted=false elapsedMs={elapsed.ElapsedMilliseconds}");
                        toast.Error(new InvalidOperationException("This item is no longer available from this card. Open its menu again."));
                        return;
                    }
                    object body = surfaceKey == "continue_watching"
                        ? new { progress_updated_at = item.ProgressUpdatedAt }
                        : (object)new { series_id = dismissalSeriesId! };
                    await homeApi.DismissItemAsync(context, surfaceKey, item.ContentId, body);
                    serverAccepted = true;
                    stage = "context_check";
                    if (!homeApi.IsCurrentContext(context))
                    {
                        var current = homeApi.CaptureContext();
                        if (!client.IsCurrentIdentityContext(menuOwner))
                        {
                            LocalLog.AppendLine("home_actions.txt", $"dismissal action={actionId} surface={surfaceKey} stage=context_retired serverAccepted=true elapsedMs={elapsed.ElapsedMilliseconds}");
                            return;
                        }
                        // A reconnect retired the request context, not the owner whose dismissal the
                        // server accepted: publish it under that owner's current context.
                        context = current;
                    }
                    stage = "undo_snapshot";
                    WeakReferenceMessenger.Default.Send(new HomeDismissalApplied(surfaceKey, item, context));
                    stage = "surface_update";
                    WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                        MediaSurfaceChangeKind.HomeDismissed, item.ContentId,
                        dismissalSeriesId, context: context));
                    stage = "success_toast";
                    toast.Success(dropsShow ? SiloPlayer.Core.Localization.AppStrings.Get("web.home.dismiss.removedContinueWatchingAndNextUp", fallback: "Removed from Continue Watching and Next Up") : label);
                    LocalLog.AppendLine("home_actions.txt",
                        $"dismissal action={actionId} surface={surfaceKey} stage=complete serverAccepted=true elapsedMs={elapsed.ElapsedMilliseconds}");
                }
                catch (Exception ex)
                {
                    LocalLog.AppendLine("home_actions.txt",
                        $"dismissal action={actionId} surface={surfaceKey} stage={stage} serverAccepted={serverAccepted} elapsedMs={elapsed.ElapsedMilliseconds}");
                    LocalLog.AppendException("home_actions.txt", ex, $"home.dismissal stage={stage}", actionId);
                    toast.Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex));
                }
            }));
        }

        return flyout;
    }

    private static bool OwnsContent(MediaItem? current, MediaItem captured) => current is not null
        && current.ContentId == captured.ContentId && current.Type == captured.Type;

    private static async Task ShowRefreshMetadataDialogAsync(
        MediaItem item,
        MediaMaintenanceApi maintenanceApi,
        ToastService toast,
        XamlRoot? root)
    {
        if (root == null) return;

        var dialog = new RefreshMetadataDialog(async mode =>
        {
            await maintenanceApi.RefreshMetadataAsync(item.ContentId, mode);
            WeakReferenceMessenger.Default.Send(new MediaSurfaceChanged(
                MediaSurfaceChangeKind.ItemMetadataRefreshed, item.ContentId, item.SeriesId));
            toast.Success(mode == "complete" ? "Complete refresh queued" : "Metadata refresh queued");
        })
        {
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    private static async Task ShowMatchItemDialogAsync(
        MediaItem item,
        CatalogApi catalog,
        ToastService toast,
        Action? stateChanged,
        XamlRoot? root,
        FrameworkElement? owner)
    {
        if (root == null) return;

        var client = App.Services.GetRequiredService<SiloApiClient>(); var context = client.CaptureContext();
        var auth = App.Services.GetRequiredService<AuthService>(); var session = auth.SessionGeneration; var userId = auth.CurrentUser?.Id;
        var navigation = App.Services.GetService<NavigationService>(); var frame = navigation?.Frame;
        for (DependencyObject? node = owner; node != null; node = VisualTreeHelper.GetParent(node))
            if (node is Frame sourceFrame) { frame = sourceFrame; break; }
        var surface = owner ?? frame?.Content as FrameworkElement ?? root.Content as FrameworkElement;
        if (surface?.IsLoaded != true) return;
        var itemOwner = surface as IMediaItemOwner;
        var library = MediaNavigationContext.LibraryId(surface);
        bool OwnsItem() => (itemOwner == null || OwnsContent(itemOwner.MediaItem, item))
            && MediaNavigationContext.LibraryId(surface) == library;
        var frameContent = frame?.Content;
        bool Authorized() => client.IsCurrentContext(context) && auth.SessionGeneration == session
            && auth.CurrentUser?.Id == userId && AuthorizationPolicy.CanCurateMetadata(auth);
        using var lifetime = new CancellationTokenSource();
        void Departed(object sender, RoutedEventArgs args) => lifetime.Cancel();
        void Navigated(object sender, NavigationEventArgs args) => lifetime.Cancel();
        void AuthorityChanged() { if (!Authorized()) lifetime.Cancel(); }
        void ItemChanged() { if (!OwnsItem()) lifetime.Cancel(); }
        if (itemOwner != null) itemOwner.MediaItemChanged += ItemChanged;
        surface.Unloaded += Departed; if (frame != null) frame.Navigated += Navigated;
        auth.UserChanged += AuthorityChanged; auth.LoggedOut += AuthorityChanged;
        bool Current() => !lifetime.IsCancellationRequested && OwnsItem() && surface.IsLoaded && surface.XamlRoot == root
            && (frame == null || ReferenceEquals(frame.Content, frameContent)) && Authorized();
        try
        {
            if (!Current()) return;
            var detail = await catalog.GetItemDetailAsync(item.ContentId, lifetime.Token);
            if (!Current()) return;
            var dialog = new MatchItemDialog(
                detail.ContentId,
                detail.Title,
                detail.Year > 0 ? detail.Year : null,
                detail.Type,
                libraryId: null,
                versions: detail.Versions,
                folderPaths: detail.FolderPaths,
                seriesId: detail.SeriesId)
            {
                XamlRoot = root,
            };
            dialog.BindNavigation(lifetime.Token);
            await dialog.ShowAsync();
            if (dialog.HasAppliedMatch)
            {
                if (!Current()) return;
                var moved = dialog.AppliedContentId is {} target && target != item.ContentId;
                if (moved && navigation?.Frame?.Content is ItemDetailPage current && current.ViewModel.Item?.ContentId == item.ContentId)
                    navigation.NavigateReplacingCurrentEntry<ItemDetailPage>(dialog.AppliedContentId);
                else stateChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            if (Current()) toast.Error(ex, SiloPlayer.Controls.MediaItemCardActions.Problem(ex));
        }
        finally
        {
            if (itemOwner != null) itemOwner.MediaItemChanged -= ItemChanged;
            surface.Unloaded -= Departed; if (frame != null) frame.Navigated -= Navigated; auth.UserChanged -= AuthorityChanged; auth.LoggedOut -= AuthorityChanged;
        }
    }

    private static MenuFlyoutItem BuildItem(Func<string> text, string glyph, Action onClick)
    {
        var item = new MenuFlyoutItem
        {
            Text = text(),
            Icon = new FontIcon { Glyph = glyph },
        };
        MediaItemCardActions.BindText(item, "menu-label", element =>
        {
            element.Text = text();
            AutomationProperties.SetName(element, element.Text);
            AppLocalization.ApplyDirection(element);
        });
        item.Click += (_, _) => onClick();
        return item;
    }

    private static MenuFlyoutItem BuildItem(Func<string> text, string glyph, Func<Task> onClickAsync)
    {
        var item = new MenuFlyoutItem
        {
            Text = text(),
            Icon = new FontIcon { Glyph = glyph },
        };
        MediaItemCardActions.BindText(item, "menu-label", element =>
        {
            element.Text = text();
            AutomationProperties.SetName(element, element.Text);
            AppLocalization.ApplyDirection(element);
        });
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

}
