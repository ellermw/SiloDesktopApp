using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

public static class NotInterestedActions
{
    public static NotInterestedService CreateService(CatalogApi catalog)
    {
        var service = new NotInterestedService(catalog);
        service.Changed += change =>
        {
            void Publish()
            {
                if (!service.IsCurrent(change.Context)) return;
                App.Services.GetService<ItemDetailPrefetchCache>()?.Invalidate(change.ItemId);
                WeakReferenceMessenger.Default.Send(change);
            }
            var dispatcher = App.MainWindowInstance?.DispatcherQueue;
            if (dispatcher != null && !dispatcher.HasThreadAccess) dispatcher.TryEnqueue(Publish);
            else Publish();
        };
        return service;
    }

    public static async Task<bool> ToggleAsync(MediaItem item)
    {
        var service = App.Services.GetRequiredService<NotInterestedService>();
        var toast = App.Services.GetRequiredService<ToastService>();
        var operation = service.Capture(item, item.UserState?.NotInterested != true);
        try
        {
            await service.ApplyAsync(operation, item);
            if (!service.IsCurrent(operation.Context)) return false;
            if (!operation.Marked) toast.Success(SiloPlayer.Core.Localization.AppStrings.Get("web.common.toast.notInterested.restored", fallback: "Back in your recommendations"));
            else toast.Success(SiloPlayer.Core.Localization.AppStrings.Get("web.common.toast.notInterested.hidden"), SiloPlayer.Core.Localization.AppStrings.Get("web.common.toast.notInterested.undo"), async () =>
            {
                if (!service.IsCurrent(operation.Context)) return;
                try { await service.ApplyAsync(operation with { Marked = false }, item); }
                catch (OperationCanceledException) when (!service.IsCurrent(operation.Context)) { }
                catch (Exception ex) { if (service.IsCurrent(operation.Context)) toast.Error(ex, MediaItemCardActions.Problem(ex, SiloPlayer.Core.Localization.AppStrings.Get("web.common.toast.notInterested.undoFailed"))); }
            });
            return true;
        }
        catch (OperationCanceledException) when (!service.IsCurrent(operation.Context)) { return false; }
        catch (Exception ex) { if (service.IsCurrent(operation.Context)) toast.Error(ex, MediaItemCardActions.Problem(ex, SiloPlayer.Core.Localization.AppStrings.Get("web.common.toast.notInterested.updateFailed"))); return false; }
    }

    public static MenuFlyoutItem CreateMenuItem(MediaItem item, Action? stateChanged = null)
    {
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var client = App.Services.GetRequiredService<SiloApiClient>();
        var owner = client.CaptureIdentityContext();
        var action = new MenuFlyoutItem { Visibility = Visibility.Collapsed, Icon = new SymbolIcon(Symbol.Dislike), Tag = owner };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(action, "not-interested");
        void Update() { SiloPlayer.Controls.MediaItemCardActions.BindText(action, "Text", element => element.Text = item.UserState?.NotInterested == true ? SiloPlayer.Core.Localization.AppStrings.Get("web.item.menu.showInRecommendations", fallback: "Show in Recommendations") : SiloPlayer.Core.Localization.AppStrings.Get("web.item.menu.notInterested", fallback: "Not Interested")); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(action, action.Text); }
        Update();
        action.Click += async (_, _) =>
        {
            if (!action.IsEnabled) return;
            var context = catalog.CaptureContext();
            if (!client.IsCurrentIdentityContext(owner)) { action.IsEnabled = false; return; }
            action.IsEnabled = false;
            try { if (await ToggleAsync(item) && catalog.IsCurrentContext(context)) { stateChanged?.Invoke(); Update(); } }
            finally { action.IsEnabled = true; }
        };
        return action;
    }

    public static async Task RefreshMenuItemAsync(MenuFlyoutItem action, MediaItem item, ApiRequestContext context)
    {
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var client = App.Services.GetRequiredService<SiloApiClient>();
        if (action.Tag is ApiIdentityContext owner && !client.IsCurrentIdentityContext(owner))
        {
            action.IsEnabled = false;
            action.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var available = item.Type is "movie" or "series" && await catalog.SupportsNotInterestedAsync(context);
            if (catalog.IsCurrentContext(context))
            {
                SiloPlayer.Controls.MediaItemCardActions.BindText(action, "Text", element => element.Text = item.UserState?.NotInterested == true ? SiloPlayer.Core.Localization.AppStrings.Get("web.item.menu.showInRecommendations", fallback: "Show in Recommendations") : SiloPlayer.Core.Localization.AppStrings.Get("web.item.menu.notInterested", fallback: "Not Interested"));
                action.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        catch { if (catalog.IsCurrentContext(context)) action.Visibility = Visibility.Collapsed; }
    }
}
