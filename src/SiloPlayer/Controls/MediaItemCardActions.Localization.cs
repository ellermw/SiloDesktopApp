using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using SiloPlayer.Services;

namespace SiloPlayer.Controls;

public static partial class MediaItemCardActions
{
    private static readonly ConditionalWeakTable<FrameworkElement, Dictionary<string, IDisposable>> TextRegistrations = new();

    internal static string Problem(Exception exception, string? fallback = null) => exception is SiloPlayer.Core.Api.ApiException problem
        ? SiloPlayer.Core.Localization.ApiProblemText.Display(problem, fallback) : fallback ?? exception.Message;

    internal static T Localize<T>(T element, string? text = null, string? content = null, string? header = null,
        string? placeholder = null, string? title = null, string? primaryButton = null,
        string? secondaryButton = null, string? closeButton = null, string? toolTip = null, string? name = null, string? help = null, bool uppercase = false) where T : FrameworkElement
    {
        if (uppercase) Localized.SetUppercase(element, true);
        if (text != null) Localized.SetText(element, text);
        if (toolTip != null) Localized.SetToolTip(element, toolTip);
        if (name != null) Localized.SetName(element, name);
        if (help != null) Localized.SetHelp(element, help);
        if (content != null) Localized.SetContent(element, content);
        if (header != null) Localized.SetHeader(element, header);
        if (placeholder != null) Localized.SetPlaceholder(element, placeholder);
        if (title != null) Localized.SetTitle(element, title);
        if (primaryButton != null) Localized.SetPrimaryButton(element, primaryButton);
        if (secondaryButton != null) Localized.SetSecondaryButton(element, secondaryButton);
        if (closeButton != null) Localized.SetCloseButton(element, closeButton);
        if (element is Microsoft.UI.Xaml.Controls.ContentDialog or Microsoft.UI.Xaml.Controls.MenuFlyoutItem
            or Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem) Localized.SetDirection(element, true);
        return element;
    }

    internal static T BindText<T>(T element, string slot, Action<T> refresh) where T : FrameworkElement
    {
        var registrations = TextRegistrations.GetOrCreateValue(element);
        if (registrations.Remove(slot, out var previous)) previous.Dispose();
        void Apply()
        {
            refresh(element);
            if (element is Microsoft.UI.Xaml.Controls.MenuFlyoutItem item)
            {
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, item.Text);
                AppLocalization.ApplyDirection(item);
            }
        }
        Apply();
        registrations[slot] = AppLocalization.Register(element, Apply);
        return element;
    }
}
