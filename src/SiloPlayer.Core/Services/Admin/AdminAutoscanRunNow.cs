namespace SiloPlayer.Core.Services.Admin;

/// <summary>A source's contribution to the Autoscan Run now action.</summary>
public readonly record struct AdminAutoscanRunNowSource(bool Enabled, string DeliveryMode);

/// <summary>Visibility policy from the official Autoscan Sources page.</summary>
public static class AdminAutoscanRunNow
{
    public static bool IsVisible(bool? enabled, IReadOnlyList<AdminAutoscanRunNowSource>? sources)
    {
        if (enabled == false) return false;
        return sources is null || sources.Any(source => source.Enabled && source.DeliveryMode == "poll");
    }
}
