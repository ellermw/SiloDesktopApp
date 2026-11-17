namespace SiloPlayer.Helpers;

/// <summary>
/// Windows "Animation effects" (Settings › Accessibility › Visual effects), the
/// native counterpart of the WebUI's <c>prefers-reduced-motion</c> (official #4311).
/// The value is read once and kept current by <c>AnimationsEnabledChanged</c>, so
/// scroll and reveal paths read a cached field instead of querying WinRT each time.
/// Before Windows 10 2004 (no change event) it falls back to reading on demand.
/// </summary>
internal static class SystemMotion
{
    private static readonly Windows.UI.ViewManagement.UISettings? s_uiSettings = CreateUISettings();
    private static volatile bool s_animationsEnabled = ReadAnimationsEnabled();
    private static readonly bool s_followsChanges = FollowChanges();

    /// <summary>False when the user turned Windows animation effects off: jump instead of animating.</summary>
    internal static bool AnimationsEnabled => s_followsChanges ? s_animationsEnabled : ReadAnimationsEnabled();

    private static Windows.UI.ViewManagement.UISettings? CreateUISettings()
    {
        try { return new Windows.UI.ViewManagement.UISettings(); } catch { return null; }
    }

    private static bool ReadAnimationsEnabled()
    {
        try { return s_uiSettings?.AnimationsEnabled ?? true; } catch { return true; }
    }

    private static bool FollowChanges()
    {
        var settings = s_uiSettings;
        if (settings is null) return false;
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return false;
        try
        {
            // Raised off the UI thread; only a bool is written. The static field keeps
            // the UISettings instance (and so this subscription) alive for the process.
            settings.AnimationsEnabledChanged += (_, _) => s_animationsEnabled = ReadAnimationsEnabled();
            // Close the window between the initial read and the subscription.
            s_animationsEnabled = ReadAnimationsEnabled();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
