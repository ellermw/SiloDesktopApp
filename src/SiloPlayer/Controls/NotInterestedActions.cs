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
            if (!operation.Marked) toast.Success("Back in your recommendations");
            else toast.Success("We won't recommend this", "Undo", async () =>
            {
                if (!service.IsCurrent(operation.Context)) return;
                try { await service.ApplyAsync(operation with { Marked = false }, item); }
                catch (OperationCanceledException) when (!service.IsCurrent(operation.Context)) { }
                catch (Exception ex) { if (service.IsCurrent(operation.Context)) toast.Error(ex, "Failed to undo Not interested"); }
            });
            return true;
        }
        catch (OperationCanceledException) when (!service.IsCurrent(operation.Context)) { return false; }
        catch (Exception ex) { if (service.IsCurrent(operation.Context)) toast.Error(ex, "Failed to update Not interested"); return false; }
    }

    public static MenuFlyoutItem CreateMenuItem(MediaItem item, Action? stateChanged = null)
    {
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        var context = catalog.CaptureContext();
        var action = new MenuFlyoutItem { Visibility = Visibility.Collapsed, Icon = new SymbolIcon(Symbol.Dislike) };
        void Update() { action.Text = item.UserState?.NotInterested == true ? "Show in Recommendations" : "Not Interested"; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(action, action.Text); }
        Update();
        action.Click += async (_, _) =>
        {
            if (!catalog.IsCurrentContext(context) || !action.IsEnabled) return;
            action.IsEnabled = false;
            try { if (await ToggleAsync(item)) { stateChanged?.Invoke(); Update(); } }
            finally { action.IsEnabled = true; }
        };
        return action;
    }

    public static async Task RefreshMenuItemAsync(MenuFlyoutItem action, MediaItem item, ApiRequestContext context)
    {
        var catalog = App.Services.GetRequiredService<CatalogApi>();
        try
        {
            var available = item.Type is "movie" or "series" && await catalog.SupportsNotInterestedAsync(context);
            if (catalog.IsCurrentContext(context))
            {
                action.Text = item.UserState?.NotInterested == true ? "Show in Recommendations" : "Not Interested";
                action.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        catch { action.Visibility = Visibility.Collapsed; }
    }
}
