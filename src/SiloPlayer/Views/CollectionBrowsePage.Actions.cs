using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Helpers;

namespace SiloPlayer.Views;

/// <summary>
/// The hero's More actions menu (pinned WebUI <c>CollectionPageActions</c>): Sync now and Delete… for the owner of a personal
/// collection, Add to my library page for everyone else. The policy, words and confirmation are shared with the Collections card
/// menu (<see cref="CollectionHeroActions"/>, <see cref="CollectionDeleteDialog"/>). Every handler re-checks that the header it was
/// built for is still the page's header, so an action started on a page the viewer has left or an account that changed does nothing.
/// </summary>
public sealed partial class CollectionBrowsePage
{
    /// <summary>The page state one header (and the menu built from it) belongs to.</summary>
    private sealed record HeroScope(NavArgs Args, int Generation, ApiRequestContext Context, CancellationTokenSource Header);

    private HeroScope? _hero;
    private bool _heroActionBusy;
    private bool _syncing;

    private bool HeroCurrent(HeroScope scope)
        => ReferenceEquals(_hero, scope) && _isNavigated && scope.Generation == _navigationGeneration && _currentArgs == scope.Args
           && !scope.Header.IsCancellationRequested && _apiClient.IsCurrentContext(scope.Context);

    private void UpdateMoreMenu(CollectionPageAccess access)
    {
        var scope = _hero;
        var plan = scope == null ? CollectionHeroMenu.Empty : CollectionHeroActions.Menu(access);
        CollectionMoreButton.Flyout = plan.HasEntries && scope != null ? BuildMoreMenu(plan, access, scope) : null;
        CollectionMoreButton.Visibility = CollectionMoreButton.Flyout == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private MenuFlyout BuildMoreMenu(CollectionHeroMenu plan, CollectionPageAccess access, HeroScope scope)
    {
        var menu = CollectionText.Direct(new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight });
        CollectionActionMenuPresentation.Configure(menu);
        MenuFlyoutItem? sync = null, delete = null;
        if (plan.ShowSync)
        {
            sync = Entry(CollectionText.Get("web.collections.copy.syncNow", "Sync now"), "refresh-cw");
            sync.Click += async (_, _) => await SyncAsync(scope);
            menu.Items.Add(sync);
        }
        if (plan.LibraryPages.Count == 1)
        {
            var page = plan.LibraryPages[0];
            var item = Entry(CollectionText.Get("web.collections.pageActions.addToMyLibraryPage", CollectionHeroActions.AddToLibraryPage(page.Name), ("library", page.Name)), "library");
            item.Click += (_, _) => AddToLibraryPage(scope, page);
            menu.Items.Add(item);
        }
        else if (plan.LibraryPages.Count > 1)
        {
            var group = new MenuFlyoutSubItem { Text = CollectionText.Get("web.collections.copy.addToMyLibraryPage", CollectionHeroActions.AddToLibraryPageGroup), Icon = WebUiIcon.Navigation("library", 16, (Brush)Application.Current.Resources["SecondaryTextBrush"]) };
            foreach (var page in plan.LibraryPages)
            {
                var item = new MenuFlyoutItem { Text = CollectionText.Get("web.collections.copy.libraryPage", CollectionHeroActions.LibraryPage(page.Name), ("library", page.Name)) };
                CollectionActionMenuPresentation.Configure(item);
                item.Click += (_, _) => AddToLibraryPage(scope, page);
                group.Items.Add(item);
            }
            menu.Items.Add(group);
        }
        if (plan.ShowDelete && access.Personal is { } own)
        {
            if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
            delete = Entry(CollectionText.Get("web.collections.actions.delete", "Delete…"), "trash-2", destructive: true);
            delete.Click += async (_, _) => await DeleteAsync(scope, own);
            menu.Items.Add(delete);
        }
        menu.Opened += (_, _) =>
        {
            if (sync != null) { sync.IsEnabled = !_heroActionBusy; sync.Text = _syncing ? CollectionText.Get("web.collections.actions.syncing", "Syncing…") : CollectionText.Get("web.collections.copy.syncNow", "Sync now"); }
            if (delete != null) delete.IsEnabled = !_heroActionBusy;
        };
        return menu;

        static MenuFlyoutItem Entry(string text, string icon, bool destructive = false)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = WebUiIcon.Navigation(icon, 16, (Brush)Application.Current.Resources[destructive ? "ErrorBrush" : "SecondaryTextBrush"]) };
            CollectionActionMenuPresentation.Configure(item, null, destructive);
            return item;
        }
    }

    private void AddToLibraryPage(HeroScope scope, CollectionPageLibrary library)
    {
        if (!HeroCurrent(scope)) return;
        // Opens Settings > Home Screen on that library page with Add row on this collection; nothing changes until that row is added.
        App.Services.GetRequiredService<NavigationService>().Navigate<CustomizeHomePage>(
            new CustomizeHomeNavigationArgs(scope.Args.CollectionId, scope.Args.Title, library.Id, ServerCollection: !scope.Args.IsUserCollection));
    }

    private async Task SyncAsync(HeroScope scope)
    {
        if (!HeroCurrent(scope) || _heroActionBusy) return;
        _heroActionBusy = _syncing = true;
        SetActionStatus(CollectionText.Get("web.collections.actions.syncing", "Syncing…"), error: false);
        UserCollectionSyncResult? result = null;
        string? failure = null;
        try { result = await _collectionsApi.SyncCollectionAsync(scope.Args.CollectionId); }
        catch (Exception ex) { failure = $"Failed to sync collection: {CollectionText.Problem(ex)}"; }
        finally { _heroActionBusy = _syncing = false; }
        // The page was left, or the account changed, while the server worked: nothing here may repaint another page.
        if (!HeroCurrent(scope)) return;
        failure ??= CollectionHeroActions.SyncFailure(result!);
        if (failure != null) { SetActionStatus(failure, error: true); return; }
        SetActionStatus(CollectionHeroActions.SyncSuccess(result!), error: false);
        // Titles may have changed: read the collection again, keeping the header on screen meanwhile.
        _ = LoadHeaderAsync(scope.Args, scope.Generation, refresh: true);
        await LoadFirstPageAsync();
    }

    private async Task DeleteAsync(HeroScope scope, PersonalCollectionFacts own)
    {
        if (!HeroCurrent(scope) || _heroActionBusy) return;
        _heroActionBusy = true;
        bool deleted;
        try
        {
            deleted = await CollectionDeleteDialog.ShowAsync(XamlRoot, own.Name, own.IsShared, async () =>
            {
                // A dialog left open over a page the viewer has since left must not delete on its behalf.
                if (!HeroCurrent(scope)) return "This collection's page is no longer open.";
                try { await _collectionsApi.DeleteCollectionAsync(scope.Args.CollectionId); return null; }
                catch (Exception ex) { return $"Failed to delete collection: {CollectionText.Problem(ex)}"; }
            });
        }
        finally { _heroActionBusy = false; }
        if (!deleted || !HeroCurrent(scope)) return;
        // Back to where collections are listed, without leaving the deleted collection in Back history.
        App.Services.GetRequiredService<NavigationService>().NavigateReplacingCurrentEntry<CollectionsPage>();
    }

    private void SetActionStatus(string? text, bool error)
    {
        CollectionActionStatus.Text = text ?? "";
        CollectionActionStatus.Foreground = (Brush)Application.Current.Resources[error ? "ErrorBrush" : "SecondaryTextBrush"];
        CollectionActionStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearHeroActions()
    {
        _hero = null;
        CollectionMoreButton.Flyout?.Hide();
        CollectionMoreButton.Flyout = null;
        CollectionMoreButton.Visibility = Visibility.Collapsed;
        SetActionStatus(null, error: false);
    }
}
