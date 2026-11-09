using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    /// <summary>
    /// Playback Info is drawn by the OSC as an ASS overlay, which has no UI
    /// Automation peers. The OSC hands over its displayed rows once per opening
    /// and they are announced through the main window, so screen readers hear
    /// every row's label and value while the visual panel stays top-left.
    /// </summary>
    private void AnnouncePlaybackInfoRows(string json)
    {
        if (_closing || _mpv == null || App.MainWindowInstance?.Content is not UIElement root) return;
        var rows = PlaybackStatsSnapshot.Parse(json);
        if (rows == null || rows.Count == 0) return;
        var text = "Playback info. " + string.Join(" ", rows.GroupBy(row => row.Section).Select(section =>
            section.Key + ": " + string.Join("; ", section.Select(row => row.Label + ", " + row.Value)) + "."));
        try
        {
            var peer = FrameworkElementAutomationPeer.FromElement(root) ?? FrameworkElementAutomationPeer.CreatePeerForElement(root);
            peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent,
                text, "SiloPlaybackInfo");
        }
        catch (Exception ex) { LogToFile("state_trace.txt", $"Playback info announcement: {ex.GetType().Name}"); }
    }
}
