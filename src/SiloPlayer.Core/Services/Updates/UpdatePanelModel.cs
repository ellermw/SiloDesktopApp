using System.Globalization;

namespace SiloPlayer.Core.Services.Updates;

public enum UpdatePanelPhase { Working, Ready, Deferred, Finishing, Failed }

/// <summary>
/// Presentation state of the small update panel docked in Silo's window. It only
/// decides what the panel shows; it cannot touch packages, pipes or elevation.
/// </summary>
public sealed class UpdatePanelModel
{
    public const string ReadyTitle = "Update ready";
    public const string ReadyText = "Restart Silo to finish updating.";
    public const string DeferredText = "The update will finish automatically the next time you start Silo.";
    public const string ExpandGlyph = "", CollapseGlyph = ""; // ChevronDown, ChevronUp

    public UpdatePanelPhase Phase { get; private set; } = UpdatePanelPhase.Working;
    public bool DetailsExpanded { get; private set; }
    /// <summary>Details belong to the progress view only; other states keep the panel minimal.</summary>
    public bool ShowsDetails => DetailsExpanded && Phase == UpdatePanelPhase.Working;
    public bool ShowsDetailsToggle => Phase == UpdatePanelPhase.Working;
    public bool ShowsProgress => Phase is UpdatePanelPhase.Working or UpdatePanelPhase.Finishing;
    public bool ShowsChoice => Phase == UpdatePanelPhase.Ready;
    public string DetailsGlyph => ShowsDetails ? CollapseGlyph : ExpandGlyph;
    public string DetailsLabel => ShowsDetails ? "Hide details" : "Details";

    /// <summary>Flips the details section; returns whether it is now shown.</summary>
    public bool ToggleDetails()
    {
        if (Phase != UpdatePanelPhase.Working) return false;
        DetailsExpanded = !DetailsExpanded;
        return DetailsExpanded;
    }

    /// <summary>Everything is downloaded: the panel asks Restart now or Later.</summary>
    public void Ready() { if (Phase == UpdatePanelPhase.Working) Phase = UpdatePanelPhase.Ready; }
    /// <summary>Restart now continues installation in the same panel.</summary>
    public void Restart() { if (Phase == UpdatePanelPhase.Ready) Phase = UpdatePanelPhase.Working; }
    public void Defer() { if (Phase == UpdatePanelPhase.Ready) Phase = UpdatePanelPhase.Deferred; }
    public void Finish() { if (Phase is UpdatePanelPhase.Working or UpdatePanelPhase.Ready) Phase = UpdatePanelPhase.Finishing; }
    public void Fail() => Phase = UpdatePanelPhase.Failed;

    /// <summary>Whole percent for the right end of the progress bar; null while progress is unknown.</summary>
    public static string? Percent(long completed, long total) => total <= 0 ? null
        : ((int)Math.Floor(100d * Math.Clamp(completed, 0, total) / total)).ToString(CultureInfo.InvariantCulture) + "%";
}

public readonly record struct PanelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>Where the update panel sits and when it shows. Native physical pixels throughout.</summary>
public static class UpdatePanelPlacement
{
    public const int PanelWidth = 340, Margin = 16;

    /// <summary>
    /// Docks to the owner's client bottom-right. The bottom edge stays fixed, so a taller
    /// panel (details open) grows upward. The result stays inside the monitor work area.
    /// </summary>
    public static (int X, int Y) DockBottomRight(PanelRect client, int width, int height, int margin, PanelRect work)
    {
        var x = client.Right - margin - width;
        var y = client.Bottom - margin - height;
        return (Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width)),
            Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height)));
    }

    /// <summary>A Silo video surface covering most of the window or its monitor is playback; the panel stays out of its way.</summary>
    public static bool IsPlaybackSurface(PanelRect surface, PanelRect ownerClient, PanelRect monitor)
    {
        static long Area(PanelRect r) => Math.Max(0, r.Width) * (long)Math.Max(0, r.Height);
        return Area(surface) * 2 >= Area(ownerClient) || Area(surface) * 2 >= Area(monitor);
    }

    /// <summary>
    /// The panel stays visible with Silo regardless of focus. Native z-order tracking keeps it above
    /// Silo while normal foreground applications can cover it. Playback and minimization hide it.
    /// Once Silo closes to restart, the final status stays visible.
    /// </summary>
    public static bool ShouldShow(bool ownerAlive, bool ownerMinimized, bool playback, bool? foregroundIsSilo, bool visibleNow)
    {
        if (!ownerAlive) return true;
        if (ownerMinimized || playback) return false;
        return true;
    }
}
