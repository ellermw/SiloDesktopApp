using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Controls;

/// <summary>Owned collection-menu seam, reusable by the PR128 card/hero plumbing.</summary>
internal static class CollectionAllowanceMenu
{
    internal static void Attach(MenuFlyout menu, FrameworkElement host, Collection collection, Func<bool> navigationCurrent)
    {
        var creator = collection.CreatorProfileId;
        var owner = AllowanceDialogOwner.Capture(() => host.IsLoaded && collection.CreatorProfileId == creator && navigationCurrent());
        if (!owner.IsPrimary || collection.CreatorProfileId != owner.Owner.ProfileId) return;
        var action = new MenuFlyoutItem { Text = AllowFor(), Visibility = Visibility.Collapsed };
        var retrying = false;
        menu.Items.Add(action);
        var busy = false;
        async Task LoadAsync()
        {
            if (busy || !owner.IsCurrent) return;
            busy = true;
            try
            {
                var available = await owner.LoadAsync();
                if (!owner.IsCurrent) return;
                retrying = false;
                action.Text = AllowFor();
                SiloPlayer.Services.AppLocalization.ApplyDirection(action);
                action.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (OperationCanceledException) { action.Visibility = Visibility.Collapsed; }
            catch { if (owner.IsCurrent) { retrying = true; action.Text = "Retry title allowances"; action.Visibility = Visibility.Visible; } }
            finally { busy = false; }
        }
        menu.Opening += async (_, _) => { action.Visibility = Visibility.Collapsed; await LoadAsync(); };
        action.Click += async (_, _) =>
        {
            if (!owner.IsCurrent || busy) return;
            if (retrying) { await LoadAsync(); return; }
            if (!owner.Available) return;
            await new AllowCollectionDialog(owner, collection) { XamlRoot = host.XamlRoot }.ShowAsync();
        };
    }

    private static string AllowFor() => SiloPlayer.Helpers.CollectionText.Get("web.collections.actions.allowFor", "Allow for…");
}
