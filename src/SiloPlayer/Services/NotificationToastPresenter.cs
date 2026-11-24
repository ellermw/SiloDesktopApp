using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;
using System.Text.Json;

namespace SiloPlayer.Services;

/// <summary>Shows the official WebUI's toast for a notification that has just arrived.</summary>
public static class NotificationToastPresenter
{
    /// <summary>Whether the notification has a toast; shows it when it does. Text is read in the language on screen now.</summary>
    public static bool Show(ToastService toasts, JsonElement notification)
    {
        if (NotificationToastRules.For(notification) is not { } content) return false;
        toasts.Info(content.Render(part => part.Key == null ? part.Text : AppStrings.Get(part.Key, part.Arguments, part.Text)));
        return true;
    }
}
