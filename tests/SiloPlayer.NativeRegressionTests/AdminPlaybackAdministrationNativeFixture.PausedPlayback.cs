using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SiloPlayer.Core.Services.Admin;

/// <summary>
/// Admin > Playback "Paused playback" (official silo-server #5180, PausedPlaybackGroup.tsx at c59798116): the group follows
/// Watch behavior, holds one minutes field from 0 to 1440 with the official label, unit and help, and saves through the
/// page's own form. Captured at 1440 and 800 wide.
/// </summary>
internal static partial class AdminPlaybackAdministrationNativeFixture
{
    private const string PausedKey = "playback.paused_session_timeout_minutes";
    private const string PausedHelp = "0 to 1440. Ends a video paused this long in a Silo app, freeing its stream and any transcode; playing it again continues from the same spot. Watch Together, casting and audiobooks are not affected. 0 turns this off.";

    private static async Task PausedPlaybackAsync(Window window, Frame outer, FrameworkElement playback, Server server)
    {
        var panel = Field(playback, "_panel")!;
        var fields = (StackPanel)Field(panel, "_fields")!;
        // The group type is internal to the app; its dictionary is read untyped.
        var groups = (System.Collections.IDictionary)Field(panel, "_groups")!;
        UIElement Group(string key) => (UIElement)groups[key]!;
        int At(string group) => fields.Children.IndexOf(Group(group));
        Check(At("pausedPlayback") == At("watch") + 1 && At("transcoding") == At("pausedPlayback") + 1, "Paused playback sits between Watch behavior and Transcoding (official PlaybackSettings.tsx order)");
        Check(All(Group("pausedPlayback")).OfType<TextBlock>().Any(text => text.Text == "Paused playback"), "the group carries the official title");
        var row = Find<FrameworkElement>(playback, "PlaybackRow_" + PausedKey);
        var box = Find<TextBox>(playback, "Playback_" + PausedKey);
        string[] Texts() => All(row).OfType<TextBlock>().Where(text => Shown(text)).Select(text => text.Text).ToArray();
        Check(Shown(row) && box.Text == "15" && box.IsEnabled, "the paused timeout shows the saved 15 minutes and is editable");
        Check(Texts().Contains("End paused playback after") && Texts().Contains("minutes") && Texts().Contains(PausedHelp), "the row shows the official label, unit and help");
        Check(AutomationProperties.GetName(box) == "End paused playback after" && AutomationProperties.GetHelpText(box) == PausedHelp, "the field is named and described for assistive technology");
        row.StartBringIntoView(); await Task.Delay(400);
        await Capture(outer, "playback-paused-group-1440.png");

        await ResizeTo(window, outer, 800);
        row.StartBringIntoView(); await Task.Delay(400);
        Check(Shown(row) && Texts().Contains("End paused playback after") && box.ActualWidth > 0, "at 800 wide the row keeps its label and field");
        await Capture(outer, "playback-paused-group-800.png");
        await ResizeTo(window, outer, 1440);

        var controller = (AdminPlaybackSettingsController)Field(playback, "_controller")!;
        box.Text = "1441";
        await Until(() => controller.State.Errors.ContainsKey(PausedKey) && Shown(Find<Button>(outer, "PlaybackSave")) && !Find<Button>(outer, "PlaybackSave").IsEnabled, "1441 minutes is refused and Save is blocked");
        await Until(() => Texts().Contains("Enter a whole number from 0 to 1440."), "the range error shows under the field");
        row.StartBringIntoView(); await Task.Delay(300);
        await Capture(outer, "playback-paused-group-error-1440.png");
        box.Text = "0";
        await Until(() => controller.State.DirtyKeys.Contains(PausedKey) && controller.State.Errors.Count == 0 && Find<Button>(outer, "PlaybackSave").IsEnabled, "0 turns the timeout off and can be saved");
        var before = server.Writes;
        Invoke(Find<Button>(outer, "PlaybackSave"));
        await Until(() => server.Writes == before + 1 && !Shown(Find<Button>(outer, "PlaybackSave")), "the paused timeout is saved once");
        Check(server.LastBody != null && server.LastBody.Contains("\"" + PausedKey + "\":\"0\"", StringComparison.Ordinal) && !server.LastBody.Contains("playback.watched_threshold", StringComparison.Ordinal),
            "only the paused timeout is written");
        await Until(() => box.Text == "0" && !controller.State.DirtyKeys.Contains(PausedKey), "the saved value shows without an unsaved mark");
    }

    private static async Task ResizeTo(Window window, Frame outer, int width)
    {
        var scale = outer.XamlRoot?.RasterizationScale ?? 1;
        window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Round(width * scale), (int)Math.Round(1000 * scale)));
        await Until(() => Math.Abs(outer.ActualWidth - width) < 2, $"the window is {width} wide");
        await Task.Delay(300);
    }
}
