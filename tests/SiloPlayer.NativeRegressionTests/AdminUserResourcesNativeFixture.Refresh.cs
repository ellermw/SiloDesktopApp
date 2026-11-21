using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;
using Windows.UI;

/// <summary>
/// The parity refresh against official a84b2551b2: the Devices segmented controls and their announcement, the subtitle appearance override editor
/// as an autosaved draft (a refused save, an unknown outcome, edits while a save is in flight, closing, reset), long names in lists and dialogs,
/// plain bounded names in confirmations, and the official danger fill on destructive confirmations. Synthetic values only.
/// </summary>
internal static partial class AdminUserResourcesNativeFixture
{
    private const string SubtitleKey = "playback.subtitle_appearance";
    private static string Chr(int code) => char.ConvertFromUtf32(code);
    /// <summary>A name with a line break, a line separator and direction controls inside it: none of them may reach a confirmation.</summary>
    private static string Evil => "Evil" + Chr(0x202E) + "\n" + Chr(0x2066) + "name" + Chr(0x2028) + "tail";
    private static bool Plain(string text) => !text.Any(ch => char.IsControl(ch) || ch is (char)0x2028 or (char)0x2029 or (char)0x200E or (char)0x200F or (char)0x061C or (>= (char)0x202A and <= (char)0x202E) or (>= (char)0x2066 and <= (char)0x2069));
    private static string Repeated(string word, int count) => string.Join(" ", Enumerable.Repeat(word, count));
    private static ToggleState State(ToggleButton button) => ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)).ToggleState;
    private static double Luminance(Color color)
    {
        static double Part(byte value) { var x = value / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Part(color.R) + 0.7152 * Part(color.G) + 0.0722 * Part(color.B);
    }
    private static double Contrast(Color a, Color b) { var x = Luminance(a); var y = Luminance(b); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
    private static IEnumerable<TextBlock> Texts(DependencyObject root) => All(root).OfType<TextBlock>();
    private static TextBlock TextStarting(DependencyObject root, string start) => Texts(root).First(x => x.Text.StartsWith(start, StringComparison.Ordinal));

    /// <summary>The confirm button of a destructive dialog: the official solid danger fill, a readable label, 90% of the fill on hover.</summary>
    private static void CheckDestructive(ContentDialog dialog, string label)
    {
        var primary = Primary(dialog);
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast) { Check(true, label + ": a Windows contrast theme is on, so the system colours apply"); return; }
        var fill = ((SolidColorBrush)primary.Background).Color; var text = ((SolidColorBrush)primary.Foreground).Color;
        Check(fill.R == 0xDC && fill.G == 0x26 && fill.B == 0x26 && fill.A == 0xFF, label + ": the confirm button uses the official danger fill");
        var ratio = Contrast(fill, text);
        Check(ratio >= 4.5, label + ": its label can be read on that fill (" + ratio.ToString("0.00") + ":1)");
        VisualStateManager.GoToState(primary, "PointerOver", false);
        var root = All(primary).OfType<Border>().FirstOrDefault(x => x.Name == "Root");
        Program.Log("INFO: " + label + " hover: root=" + (root == null ? "missing" : root.Background?.GetType().Name + " opacity " + root.Background?.Opacity + " color " + (root.Background as SolidColorBrush)?.Color) + "; borders=" + string.Join(",", All(primary).OfType<Border>().Select(x => x.Name)));
        Check(root?.Background is SolidColorBrush hover && Math.Abs(hover.Opacity - 0.9) < 0.001 && hover.Color == fill, label + ": it darkens to 90% of the fill on hover");
        VisualStateManager.GoToState(primary, "Normal", false);
    }

    // The subtitle appearance dialog's pills mark the chosen option with a white fill.
    private static string ActivePill(ContentDialog dialog, string host) =>
        (string)((Panel)dialog.FindName(host)).Children.OfType<Button>().Single(x => x.Background is SolidColorBrush { Color: { A: 255, R: 255, G: 255, B: 255 } }).Tag;
    private static void ChoosePill(ContentDialog dialog, string host, string value) => Click(((Panel)dialog.FindName(host)).Children.OfType<Button>().Single(x => (string)x.Tag == value));
    private static string DialogStatus(ContentDialog dialog) => ((TextBlock)dialog.FindName("SaveStatusText")).Text;
    private static JsonNode PutBody(AdminUserResourcesFakeServer server, string path) => JsonNode.Parse(server.Last("PUT", path)!.Body)!["value"]!;
    private static JsonNode ParsedValue(JsonNode value) => value is JsonValue text && text.TryGetValue<string>(out var json) ? JsonNode.Parse(json)! : value;

    private static async Task RefreshAsync(Context c)
    {
        var s = c.Server; const string subtitles = ValuesPath + SubtitleKey;
        var devicePath = AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7");

        await ScenarioAsync(c, "refresh: the theme's status colours and the danger fill can be read", async () =>
        {
            await Task.Yield();
            var surface = ((SolidColorBrush)Application.Current.Resources["SurfaceBrush"]).Color;
            foreach (var key in new[] { "ErrorBrush", "WarningBrush", "SuccessBrush", "SecondaryTextBrush", "PrimaryTextBrush" })
            {
                var ratio = Contrast(((SolidColorBrush)Application.Current.Resources[key]).Color, surface);
                Check(ratio >= 4.5, key + " text on the page surface reads at " + ratio.ToString("0.00") + ":1");
            }
            // A primary button on hover is the accent at 90% over the page; its label must stay readable (official #4750).
            var accent = (Color)Application.Current.Resources["AccentColor"]; var page = (Color)Application.Current.Resources["AppBackgroundColor"]; var label = (Color)Application.Current.Resources["AccentForegroundColor"];
            var hover = Color.FromArgb(255, (byte)Math.Round(accent.R * 0.9 + page.R * 0.1), (byte)Math.Round(accent.G * 0.9 + page.G * 0.1), (byte)Math.Round(accent.B * 0.9 + page.B * 0.1));
            var hoverRatio = Contrast(hover, label);
            Check(hoverRatio >= 4.5, "a primary button label on hover reads at " + hoverRatio.ToString("0.00") + ":1");
        });

        await ScenarioAsync(c, "devices: segmented controls show and announce the selected option", async () =>
        {
            s.Reset();
            await c.OpenAsync(new AdminRouteRequest(AdminRoute.Devices), "Devices (segmented controls)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceGroupBy_user") && Has(c.Devices, "DeviceScope_all"), "the segmented controls are shown");
            foreach (var (prefix, selected, other, name) in new[] { ("DeviceGroupBy", "user", "platform", T("web.admin.devices.groupBy.label")), ("DeviceScope", "all", "overrides", "") })
            {
                var chosen = Find<ToggleButton>(c.Devices, prefix + "_" + selected); var unchosen = Find<ToggleButton>(c.Devices, prefix + "_" + other); var row = (StackPanel)chosen.Parent;
                Check(State(chosen) == ToggleState.On && State(unchosen) == ToggleState.Off, prefix + ": the selected option is announced as on and the other as off");
                var group = FrameworkElementAutomationPeer.CreatePeerForElement(row);
                Check(group != null && group.GetAutomationControlType() == AutomationControlType.Group && group.GetName().Length > 0 && (name.Length == 0 || group.GetName() == name), prefix + ": the options sit in a named group");
                Check(new ToggleButtonAutomationPeer(chosen).GetName().Length > 0, prefix + ": each option has an accessible name");
                ((IToggleProvider)new ToggleButtonAutomationPeer(unchosen).GetPattern(PatternInterface.Toggle)).Toggle();
                await Until(() => State(Find<ToggleButton>(c.Devices, prefix + "_" + other)) == ToggleState.On && State(Find<ToggleButton>(c.Devices, prefix + "_" + selected)) == ToggleState.Off, prefix + ": choosing another option moves the selected state");
                Check(((StackPanel)Find<ToggleButton>(c.Devices, prefix + "_" + other).Parent).Children.OfType<ToggleButton>().Count(x => x.IsChecked == true) == 1, prefix + ": exactly one option is pressed");
            }
            await Until(() => Has(c.Devices, "DeviceGroup_p:desktop"), "grouping by platform regroups the list");
        });

        await ScenarioAsync(c, "devices: the subtitle appearance override saves as an autosaved draft", async () =>
        {
            s.Reset();
            await c.OpenAsync(devicePath, "Device detail (subtitle appearance)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceEdit_" + SubtitleKey), "the subtitle appearance row offers its editor");
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey), "the subtitle editor opened");
            var dialog = Dialog(c.Frame)!; var body = (Control)dialog.FindName("AppearanceBody");
            await Until(() => body.IsEnabled, "the editor is ready");
            Check(((TextBlock)dialog.FindName("EyebrowText")).Text == AppStrings.Uppercase("Alex · Study PC"), "the eyebrow names the profile and the device");
            Check(DialogStatus(dialog) == T("web.admin.common.subtitleAppearanceDialog.createsOverride"), "before a first save the status says a change creates an override");
            var first = ActivePill(dialog, "SizePillsHost");

            ChoosePill(dialog, "SizePillsHost", "small");
            await Until(() => s.Count("PUT", subtitles) == 1 && DialogStatus(dialog) == T("web.admin.common.subtitleAppearanceDialog.savedAutomatically"), "a change saves at once and the status says it is saved");
            var saved = s.Last("PUT", subtitles)!; var value = ParsedValue(PutBody(s, subtitles));
            Check(saved.Query == "?scope=profile_device&profile_id=p7&device_id=desktop" && value["fontSize"]!.GetValue<string>() == "small" && value.AsObject().Count == 10, "the write names user, profile, device and key and carries the whole appearance value");
            Check(((FrameworkElement)dialog.FindName("ResetButton")).Visibility == Visibility.Visible && ((TextBlock)dialog.FindName("ResetLabelText")).Text == T("web.admin.common.subtitleAppearanceDialog.resetOverride"), "once an override exists the dialog offers to reset it");

            // A refused save: the edit leaves the view, the server's reason is shown, nothing is repeated, the next save is built without it.
            s.Fail("PUT", subtitles, 422, ProblemBody("That value is not allowed."));
            ChoosePill(dialog, "SizePillsHost", "xlarge");
            await Until(() => DialogStatus(dialog).Contains("That value is not allowed."), "a refused change shows the server's reason in the editor");
            Check(ActivePill(dialog, "SizePillsHost") == "small", "the refused change drops back out of the view");
            Check(s.Count("PUT", subtitles) == 2 && !c.Devices.Session.NeedsReview && body.IsEnabled, "a refusal is not repeated, does not lock the page and keeps the editor usable");
            Click(((Panel)dialog.FindName("FontColorSwatchHost")).Children.OfType<Button>().Single(x => (string)x.Tag == "#facc15"));
            await Until(() => s.Count("PUT", subtitles) == 3, "the next change is saved");
            value = ParsedValue(PutBody(s, subtitles));
            Check(value["fontSize"]!.GetValue<string>() == "small" && value["fontColor"]!.GetValue<string>() == "#facc15", "the refused edit is not in the next save");
            await Until(() => DialogStatus(dialog) == T("web.admin.common.subtitleAppearanceDialog.savedAutomatically"), "the failure message clears once a save is accepted");

            // Edits made while a save is in flight wait, merge into one save, and go out built from what the server acknowledged.
            var gate = s.HoldAnswer("PUT", subtitles);
            ChoosePill(dialog, "PositionPillsHost", "top"); await Until(() => s.Count("PUT", subtitles) == 4, "the first change of a burst is sent");
            ChoosePill(dialog, "SizePillsHost", "large"); ChoosePill(dialog, "BgStylePillsHost", "shadow");
            await Task.Delay(400);
            Check(s.Count("PUT", subtitles) == 4, "only one save is in flight");
            Check(ActivePill(dialog, "PositionPillsHost") == "top" && ActivePill(dialog, "SizePillsHost") == "large" && ActivePill(dialog, "BgStylePillsHost") == "shadow", "the editor shows every edit at once");
            gate.SetResult();
            await Until(() => s.Count("PUT", subtitles) == 5, "the edits made meanwhile go out as one save");
            value = ParsedValue(PutBody(s, subtitles));
            Check(value["position"]!.GetValue<string>() == "top" && value["fontSize"]!.GetValue<string>() == "large" && value["backgroundStyle"]!.GetValue<string>() == "shadow" && value["fontColor"]!.GetValue<string>() == "#facc15", "that save carries the acknowledged value and all of them");
            await Task.Delay(500); Check(s.Count("PUT", subtitles) == 5, "nothing else was sent");

            // An unknown outcome is never replayed: the edit leaves the view, the editor locks, the page asks for a review.
            s.LoseAnswer("PUT", subtitles); ChoosePill(dialog, "SizePillsHost", "xxlarge");
            await Until(() => c.Devices.Session.NeedsReview, "a lost answer requires a review");
            await Until(() => DialogStatus(dialog).Contains("may have reached the server") && !body.IsEnabled, "the editor says the outcome is unknown and locks");
            Check(ActivePill(dialog, "SizePillsHost") == "large", "the unknown edit leaves the view");
            await Task.Delay(500); Check(s.Count("PUT", subtitles) == 6, "the lost write was sent once and never replayed");
            dialog.Hide(); await GoneAsync(c, "the editor closed");
            Click(Find<Button>(c.Devices, "ResourceReload")); await Until(() => !c.Devices.Session.NeedsReview && Has(c.Devices, "DeviceReset_" + SubtitleKey), "the reload shows the override the server holds");
        });

        await ScenarioAsync(c, "devices: closing the subtitle editor drops unsent edits and waits for the save in flight", async () =>
        {
            s.Reset();
            await c.OpenAsync(devicePath, "Device detail (subtitle appearance close)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceEdit_" + SubtitleKey), "the editor is offered");
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey), "the subtitle editor opened");
            var dialog = Dialog(c.Frame)!; var body = (Control)dialog.FindName("AppearanceBody"); await Until(() => body.IsEnabled, "the editor is ready");
            var gate = s.HoldAnswer("PUT", subtitles);
            ChoosePill(dialog, "SizePillsHost", "small"); await Until(() => s.Count("PUT", subtitles) == 1, "the first change is in flight");
            ChoosePill(dialog, "PositionPillsHost", "top");
            dialog.Hide(); await GoneAsync(c, "the editor closed while a save was in flight");
            Check(s.Count("PUT", subtitles) == 1 && !Has(c.Devices, "DeviceReset_" + SubtitleKey), "the page does not read the server before the save in flight has answered");
            gate.SetResult();
            await Until(() => Has(c.Devices, "DeviceReset_" + SubtitleKey), "once it answered the page shows the override");
            await Task.Delay(500);
            Check(s.Count("PUT", subtitles) == 1 && ParsedValue(PutBody(s, subtitles))["position"]!.GetValue<string>() != "top", "the edit not sent yet was dropped with the editor");

            // Reset: edits not sent yet are dropped before the clear, and the clear waits for the save in flight.
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey), "the editor opened on the override");
            dialog = Dialog(c.Frame)!; body = (Control)dialog.FindName("AppearanceBody"); await Until(() => body.IsEnabled, "the editor is ready again");
            gate = s.HoldAnswer("PUT", subtitles);
            ChoosePill(dialog, "SizePillsHost", "large"); await Until(() => s.Count("PUT", subtitles) == 2, "a change is in flight");
            ChoosePill(dialog, "PositionPillsHost", "top");
            Click((Button)dialog.FindName("ResetButton")); await Task.Delay(400);
            Check(s.Count("DELETE", ValuesPath) == 0, "the reset waits for the save in flight");
            gate.SetResult();
            await Until(() => s.Count("DELETE", ValuesPath + SubtitleKey) == 1 && Dialog(c.Frame) == null, "the override is reset once and the editor closes");
            Check(s.Count("PUT", subtitles) == 2, "the edit queued behind the save was never written back");
            await Until(() => Has(c.Devices, "DeviceEdit_" + SubtitleKey) && !Has(c.Devices, "DeviceReset_" + SubtitleKey), "the page shows the row as inherited again");
        });

        await ScenarioAsync(c, "devices: long names are cut, initials are whole characters, confirmations are plain text", async () =>
        {
            s.Reset();
            var longName = Repeated("Study", 40); var profileName = "😀" + Evil + " " + Repeated("Party", 30);
            var desktop = s.Devices.OfType<JsonObject>().First(x => x["device_id"]!.GetValue<string>() == "desktop");
            desktop["device_name"] = longName;
            foreach (var row in s.Devices.OfType<JsonObject>().Where(x => x["user_id"]!.GetValue<string>() == "7")) { row["username"] = Repeated("member", 30); foreach (var p in row["profiles"]!.AsArray().OfType<JsonObject>().Where(p => p["profile_id"]!.GetValue<string>() == "p7")) p["profile_name"] = profileName; }
            await c.OpenAsync(devicePath, "Device detail (long names)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceProfileTab_p7") && Has(c.Devices, "DeviceGroup_u:7"), "the page shows the long names");
            await Until(() => Has(c.Devices, "Device_7_desktop") && Has(c.Devices, "Device_7_phone"), "the group of the open device lists its devices");
            var longRow = Find<Button>(c.Devices, "Device_7_desktop"); var shortRow = Find<Button>(c.Devices, "Device_7_phone"); var list = Find<Button>(c.Devices, "DeviceGroup_u:7");
            var rowName = TextStarting(longRow, "Study");
            Check(rowName.TextWrapping == TextWrapping.NoWrap && rowName.TextTrimming == TextTrimming.CharacterEllipsis && rowName.TextReadingOrder == TextReadingOrder.DetectFromContent, "a device name is one line cut with an ellipsis, its direction read from the text");
            Check(Math.Abs(longRow.ActualHeight - shortRow.ActualHeight) < 3 && longRow.ActualWidth <= shortRow.ActualWidth + 1, "a long device name does not make its row taller or wider");
            var groupLabel = Texts(list).First();
            Check(groupLabel.TextTrimming == TextTrimming.CharacterEllipsis && groupLabel.TextWrapping == TextWrapping.NoWrap && list.ActualHeight < 60, "a long group label is cut on one line");
            var tab = Find<ToggleButton>(c.Devices, "DeviceProfileTab_p7"); var tabTexts = Texts(tab).ToArray();
            Check(tabTexts[0].Text == AdminNameText.Initial(profileName) && tabTexts[0].Text == "😀", "the tab's initial is the whole first character, not half of a surrogate pair");
            var tabName = tabTexts.First(x => x.Text.StartsWith("😀", StringComparison.Ordinal) && x != tabTexts[0]);
            Check(tabName.TextTrimming == TextTrimming.CharacterEllipsis && tabName.ActualWidth <= 120.5 && tabName.TextReadingOrder == TextReadingOrder.DetectFromContent, "a long profile name is cut at 120 px keeping its start");
            var title = Texts(c.Devices).First(x => x.FontSize == 20 && x.Text.StartsWith("Study", StringComparison.Ordinal));
            var titleRight = title.TransformToVisual(c.Devices).TransformPoint(new Windows.Foundation.Point(title.ActualWidth, 0)).X;
            Check(title.TextWrapping == TextWrapping.Wrap && title.ActualHeight > 40 && titleRight <= c.Devices.ActualWidth + 1, "the detail title wraps instead of running out of the page");

            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceResetProfile"), "the profile reset confirmation opened");
            var dialog = Dialog(c.Frame)!; var heading = dialog.Title.ToString()!;
            Check(Plain(heading) && heading.Contains("Evil", StringComparison.Ordinal) && heading.Contains('…') && heading.Length < 130, "the title names the profile as bounded plain text");
            CheckDestructive(dialog, "reset all overrides");
            dialog.Hide(); await GoneAsync(c, "the confirmation cancelled");
            Check(s.Count("DELETE", ValuesPath) == 0, "cancelling sent nothing");
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceReset_player.audio_sync_ms"), "the single reset confirmation opened");
            CheckDestructive(Dialog(c.Frame)!, "reset one override"); Dialog(c.Frame)!.Hide(); await GoneAsync(c, "the single confirmation cancelled");
        });

        await ScenarioAsync(c, "groups: long names in cards and lists, plain confirmations, the danger fill", async () =>
        {
            s.Reset();
            var longName = Repeated("Household", 30) + Evil;
            s.Group("3")["name"] = longName; s.Group("3")["description"] = Repeated("A description that goes on and on", 20);
            s.Group("2")["name"] = "Home" + Chr(0x202E) + "\nbase";
            s.User("9")["username"] = Repeated("guest", 40); s.User("9")["email"] = "guest-one@example.invalid";
            s.User("10")["email"] = "אלכס@example.invalid";
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(150);
            await c.OpenAsync(new AdminRouteRequest(AdminRoute.AccessGroups), "Access Groups (long names)");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupCard_3") && Has(c.Groups, "GroupCard_2"), "the cards are shown");
            var card = Find<Button>(c.Groups, "GroupCard_3"); var plainCard = Find<Button>(c.Groups, "GroupCard_2");
            var name = TextStarting(card, "Household"); var description = TextStarting(card, "A description");
            Program.Log("INFO: card name wrap=" + name.TextWrapping + " trimming=" + name.TextTrimming + " height=" + name.ActualHeight + " maxLines=" + name.MaxLines + " page width=" + c.Groups.ActualWidth);
            Check(name.TextWrapping == TextWrapping.NoWrap && name.TextTrimming == TextTrimming.CharacterEllipsis && name.ActualHeight < 32, "wide: a long group name is one cut line");
            Check(description.MaxLines == 2 && description.TextTrimming == TextTrimming.CharacterEllipsis && description.TextReadingOrder == TextReadingOrder.DetectFromContent && description.ActualHeight <= 2 * 16 + 2, "a long description is cut after two lines, its direction read from the text");
            Check(card.ActualWidth > 0 && Math.Abs(card.ActualWidth - plainCard.ActualWidth) < 2, "the long name does not widen its card");
            await CaptureAsync(c.Groups, "admin-access-groups-long-names-wide.png");
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900)); await Task.Delay(300);
            await Until(() => TextStarting(Find<Button>(c.Groups, "GroupCard_3"), "Household").MaxLines == 2, "compact: the name is re-laid out for two lines");
            name = TextStarting(Find<Button>(c.Groups, "GroupCard_3"), "Household");
            Check(name.TextWrapping == TextWrapping.Wrap && name.ActualHeight <= 2 * 24 + 2, "compact: a long group name takes at most two lines");
            await CaptureAsync(c.Groups, "admin-access-groups-long-names-compact.png");
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(200);

            await c.OpenAsync(AdminRouteRequest.AccessGroup("3"), "Group editor (long names)");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupMember_9") && Has(c.Groups, "GroupMember_10"), "the members are listed");
            var memberTexts = Texts(Find<CheckBox>(c.Groups, "GroupMember_9").Parent).Where(x => x.Text.Length > 0).ToArray();
            Check(memberTexts[0].TextTrimming == TextTrimming.CharacterEllipsis && memberTexts[0].TextWrapping == TextWrapping.NoWrap && memberTexts[0].TextReadingOrder == TextReadingOrder.DetectFromContent && memberTexts[0].ActualHeight < 28, "a long username is one cut line with its direction read from the text");
            Check(memberTexts[1].FlowDirection == FlowDirection.LeftToRight && memberTexts[1].TextTrimming == TextTrimming.CharacterEllipsis, "a Latin email is laid out left to right and cut at its end");
            var hebrew = Texts(Find<CheckBox>(c.Groups, "GroupMember_10").Parent).Where(x => x.Text.Length > 0).ToArray()[1];
            Check(hebrew.ReadLocalValue(FrameworkElement.FlowDirectionProperty) == DependencyProperty.UnsetValue, "an email that starts in a right-to-left script keeps the page's direction");

            await OpenDialogAsync(c, () => Find<Button>(c.Groups, "GroupAddMembers"), "the add users dialog opened");
            var picker = Dialog(c.Frame)!; await Until(() => Has(picker, "GroupAddUser_7"), "the eligible accounts are listed");
            var rows = All(picker).Where(x => x.GetType().Name == "SharedNamesRow").Cast<Panel>().ToArray();
            Check(rows.Length >= 2 && rows.All(row => row.Children.Cast<FrameworkElement>().Sum(x => x.ActualWidth) <= row.ActualWidth + 1), "each row's two names fit the row");
            Check(rows.Any(row => row.Children.Cast<FrameworkElement>().All(x => x.ActualWidth > 0)), "both names of a row are shown");
            picker.Hide(); await GoneAsync(c, "the add users dialog cancelled");

            await c.OpenAsync(AdminRouteRequest.AccessGroup("3"), "Group editor (delete)");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupDelete") && Find<Button>(c.Groups, "GroupDelete").IsEnabled, "the editor is ready");
            await OpenDialogAsync(c, () => Find<Button>(c.Groups, "GroupDelete"), "the delete confirmation opened");
            var confirm = Dialog(c.Frame)!; var heading = confirm.Title.ToString()!;
            Check(Plain(heading) && heading.Contains("Household", StringComparison.Ordinal) && heading.Contains('…'), "the title names the group as bounded plain text");
            Check(Texts(confirm).All(x => Plain(x.Text)) && HasText(confirm, "Home base"), "the default group the members move to is named as plain text too");
            CheckDestructive(confirm, "delete group");
            confirm.Hide(); await GoneAsync(c, "the delete confirmation cancelled"); Check(s.Count("DELETE", GroupsPath) == 0, "cancelling sent nothing");
        });

        await ScenarioAsync(c, "refresh: captures of each page at four widths", async () =>
        {
            s.Reset();
            var states = new (string Name, AdminRouteRequest Route, Func<bool> Ready, Func<FrameworkElement> Page, Func<Task>? Setup)[]
            {
                ("groups-list", new AdminRouteRequest(AdminRoute.AccessGroups), () => Has(c.Groups, "GroupCard_3"), () => c.Groups, null),
                ("groups-editor", AdminRouteRequest.AccessGroup("3"), () => Has(c.Groups, "GroupMember_9"), () => c.Groups, null),
                ("devices-list", new AdminRouteRequest(AdminRoute.Devices), () => Has(c.Devices, "DeviceGroup_u:7"), () => c.Devices,
                    async () => { Click(Find<Button>(c.Devices, "DeviceGroup_u:7")); await Until(() => Has(c.Devices, "Device_7_desktop"), "the capture group is open"); }),
                ("devices-detail", devicePath, () => Has(c.Devices, "DeviceProfileTab_p7") && Has(c.Devices, "DeviceSetting_player.hdr_enabled"), () => c.Devices, null),
                ("history-import", SourceRoute(), () => Has(c.History, "ImportRemove_9") && Has(c.History, "ImportExpand_run1"), () => c.History, null),
            };
            foreach (var (name, route, ready, page, setup) in states)
            {
                c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(250);
                await c.OpenAsync(route, "capture page " + name);
                await Until(ready, "the " + name + " page is ready to capture");
                if (setup != null) await setup();
                foreach (var width in new[] { 1440, 1024, 800, 560 })
                {
                    c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, 900)); await Task.Delay(400);
                    // The window paints the page background; the page alone is transparent, which would leave its light text unreadable in the picture.
                    if (page() is Page shown) shown.Background = (Brush)Application.Current.Resources["AppBackgroundBrush"];
                    await CaptureAsync(page(), "refresh-" + name + "-" + width + ".png");
                }
            }
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(250);
        });

        await ScenarioAsync(c, "history import: plain names in confirmations, an address left to right, the danger fill", async () =>
        {
            s.Reset();
            s.Source("3")["name"] = "Fixture " + Evil + " " + Repeated("source", 30);
            s.Mappings.OfType<JsonObject>().First()["external_user_name"] = "Viewer " + Evil;
            await c.OpenAsync(SourceRoute(), "History Import (long names)");
            await Until(() => All(c.Frame).OfType<AdminHistoryImportPage>().Any() && Has(c.History, "ImportDeleteSource") && Find<Button>(c.History, "ImportDeleteSource").IsEnabled && Has(c.History, "ImportRemove_9"), "page ready");
            await OpenDialogAsync(c, () => Find<Button>(c.History, "ImportDeleteSource"), "the delete confirmation opened");
            var dialog = Dialog(c.Frame)!;
            Check(Texts(dialog).All(x => Plain(x.Text)) && HasText(dialog, "Fixture Evil"), "the server's name is bounded plain text in the confirmation");
            CheckDestructive(dialog, "delete source"); dialog.Hide(); await GoneAsync(c, "the delete confirmation cancelled");
            await OpenDialogAsync(c, () => Find<Button>(c.History, "ImportRemove_9"), "the mapping removal confirmation opened");
            dialog = Dialog(c.Frame)!;
            Check(Texts(dialog).All(x => Plain(x.Text)) && HasText(dialog, "Viewer Evil"), "the mapped name is plain text in the confirmation");
            CheckDestructive(dialog, "remove mapping"); dialog.Hide(); await GoneAsync(c, "the removal cancelled");
            Check(s.Count("DELETE", "/history-import-sources/3") == 0 && s.Count("DELETE", ImportPath + "/mappings") == 0, "cancelling sent nothing");
            await OpenDialogAsync(c, () => Find<Button>(c.History, "ImportEditSource"), "the edit dialog opened");
            Check(Find<TextBox>(Dialog(c.Frame)!, "ImportSourceUrl").FlowDirection == FlowDirection.LeftToRight, "the server address is edited left to right");
            Dialog(c.Frame)!.Hide(); await GoneAsync(c, "the edit dialog cancelled");
        });
    }
}
