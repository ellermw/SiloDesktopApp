using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Services.PlaybackMenus;

namespace SiloPlayer.Services;

public partial class PlayerService
{
    // Last value spoken for each Playback Info row, keyed by its section/label id.
    private Dictionary<string, string>? _playbackInfoSpoken;
    // When a live counter row was last spoken (Environment.TickCount64).
    private long _playbackInfoLiveSpokenAt;
    private const long PlaybackInfoLiveIntervalMs = 15_000;
    // Rows that move on almost every refresh while the panel is open.
    private static readonly HashSet<string> PlaybackInfoLiveRows = new(StringComparer.Ordinal)
    {
        "VIDEO INFO\nDisplay area", "VIDEO INFO\nDropped frames",
        "PLAYBACK STREAM INFO\nVideo bitrate (measured)", "PLAYBACK STREAM INFO\nAudio bitrate (measured)",
    };
    // The mpv instance whose OSC last reported an open Playback Info panel, so
    // a panel from a previous player is never reported as shown.
    private object? _playbackInfoShownOn;

    private bool IsPlaybackInfoShown => _mpv != null && ReferenceEquals(_playbackInfoShownOn, _mpv);

    /// <summary>
    /// Playback Info is drawn by the OSC as an ASS overlay, which has no UI
    /// Automation peers. The OSC hands over its displayed rows when the panel
    /// opens and again whenever they change. Opening announces every row's
    /// label and value through the main window; later refreshes announce every
    /// row whose displayed value differs from the one last spoken, so the
    /// spoken text follows the panel. Live counters that change on nearly
    /// every refresh catch up at most once per interval instead of every second.
    /// </summary>
    private void AnnouncePlaybackInfoRows(string reason, string json)
    {
        if (reason == "close") { _playbackInfoShownOn = null; _playbackInfoSpoken = null; return; }
        if (reason is "open" or "update") _playbackInfoShownOn = _mpv;
        if (_closing || _mpv == null || reason is not ("open" or "update") ||
            App.MainWindowInstance?.Content is not UIElement root) return;
        var rows = PlaybackStatsSnapshot.Parse(json);
        if (rows == null || rows.Count == 0) return;
        string text;
        var processing = AutomationNotificationProcessing.ImportantMostRecent;
        if (reason == "open" || _playbackInfoSpoken == null)
        {
            _playbackInfoSpoken = rows.ToDictionary(row => row.Id, row => row.Value, StringComparer.Ordinal);
            _playbackInfoLiveSpokenAt = Environment.TickCount64;
            text = HudText("web.player.info.heading", "Playback info") + ". " + string.Join(" ", rows.GroupBy(row => row.Section).Select(section =>
                section.Key + ": " + string.Join("; ", section.Select(row => row.Label + ", " + row.Value)) + "."));
        }
        else
        {
            var spoken = _playbackInfoSpoken;
            var now = Environment.TickCount64;
            var liveDue = now - _playbackInfoLiveSpokenAt >= PlaybackInfoLiveIntervalMs;
            // A row that disappears and later returns is new again.
            var present = rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in spoken.Keys.Where(id => !present.Contains(id)).ToList()) spoken.Remove(id);
            var changed = new List<PlaybackStatsRow>();
            foreach (var row in rows)
            {
                var known = spoken.TryGetValue(row.Id, out var previous);
                if (known && previous == row.Value) continue;
                // Nothing to say for a value that became unknown, but remember
                // it so the value that replaces it is spoken.
                if (IsPlaybackInfoPlaceholder(row.Value)) { spoken[row.Id] = row.Value; continue; }
                // A live counter's first real value is spoken at once; later
                // moves keep the last spoken value until the interval elapses.
                if (known && !IsPlaybackInfoPlaceholder(previous!) && PlaybackInfoLiveRows.Contains(row.Id) && !liveDue) continue;
                spoken[row.Id] = row.Value;
                changed.Add(row);
            }
            if (changed.Count == 0) return;
            if (changed.Any(row => PlaybackInfoLiveRows.Contains(row.Id))) _playbackInfoLiveSpokenAt = now;
            text = "Playback info updated. " + string.Join("; ", changed.Select(row => row.Label + ", " + row.Value)) + ".";
            // Queue behind the opening read instead of cutting it off.
            processing = AutomationNotificationProcessing.ImportantAll;
        }
        try
        {
            var peer = PlaybackInfoAnnouncer(root);
            if (peer == null) { LogToFile("state_trace.txt", "Playback info announcement: no automation peer"); return; }
            peer.RaiseNotificationEvent(AutomationNotificationKind.Other, processing, text,
                reason == "open" ? "SiloPlaybackInfo" : "SiloPlaybackInfoUpdate");
        }
        catch (Exception ex) { LogToFile("state_trace.txt", $"Playback info announcement: {ex.GetType().Name}"); }
    }

    private static bool IsPlaybackInfoPlaceholder(string value) => value is "" or "—" or "Unavailable";

    /// <summary>
    /// The main window's root is a Grid, and panels have no automation peer,
    /// so a notification raised on it is silently dropped. Use the first
    /// visible element in the tree that does produce a peer.
    /// </summary>
    private static AutomationPeer? PlaybackInfoAnnouncer(UIElement root)
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var node = pending.Dequeue();
            if (node is UIElement element)
            {
                if (element.Visibility != Visibility.Visible) continue;
                if ((FrameworkElementAutomationPeer.FromElement(element) ??
                     FrameworkElementAutomationPeer.CreatePeerForElement(element)) is { } peer) return peer;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) pending.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }
}
