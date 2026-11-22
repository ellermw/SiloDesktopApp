using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SiloPlayer.Controls;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

/// <summary>
/// Review fixes for the account-resource pages: an edit made during the merged save never undoes a queued one, a retired or locked editor stops
/// accepting edits and says so, a dialog requested the moment another one closes opens on the first click and never beside another, the selected look
/// of a segmented option in a contrast theme, and plain text for every third-party name the pages show. Synthetic values only.
/// </summary>
internal static partial class AdminUserResourcesNativeFixture
{
    private static IEnumerable<AdminUserResourcesFakeServer.Call> SubtitleWrites(AdminUserResourcesFakeServer s) =>
        s.Calls.Where(x => x.Method == "PUT" && x.Path.Contains(ValuesPath + SubtitleKey, StringComparison.Ordinal));

    private static JsonObject WrittenValue(AdminUserResourcesFakeServer.Call call) => ParsedValue(JsonNode.Parse(call.Body)!["value"]!).AsObject();

    /// <summary>The fields of two written appearance values that differ, so a request can be read as "the previous one plus exactly these".</summary>
    private static string[] Differences(JsonObject before, JsonObject after) =>
        after.Select(x => x.Key).Union(before.Select(x => x.Key)).Where(key => before[key]?.ToJsonString() != after[key]?.ToJsonString()).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static string SlotState(object host)
    {
        var slot = host.GetType().GetField("_slot", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(host);
        return slot == null ? "(no slot)" : "occupied=" + slot.GetType().GetProperty("Occupied")!.GetValue(slot) + " showing=" + slot.GetType().GetProperty("Showing")!.GetValue(slot);
    }

    /// <summary>The slot's state at this moment: a dialog is on the screen (showing), or one is closing or finishing (occupied and not showing), or none.</summary>
    private static (bool Occupied, bool Showing) SlotNow(object host)
    {
        var slot = host.GetType().GetField("_slot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(host)!;
        return ((bool)slot.GetType().GetProperty("Occupied")!.GetValue(slot)!, (bool)slot.GetType().GetProperty("Showing")!.GetValue(slot)!);
    }
    private static int OpenDialogCount(Context c) => VisualTreeHelper.GetOpenPopupsForXamlRoot(c.Frame.XamlRoot).SelectMany(p => All(p.Child)).OfType<ContentDialog>().Count();

    private static bool HasInvisible(string text) => text.Any(ch => ch is (char)0x200B or (>= (char)0x2060 and <= (char)0x2064) or (char)0xFEFF);

    private static async Task ReviewFixAsync(Context c)
    {
        var s = c.Server; const string subtitles = ValuesPath + SubtitleKey;
        var devicePath = AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7");

        await ScenarioAsync(c, "devices: an edit made during the merged save never undoes a queued edit", async () =>
        {
            s.Reset();
            await c.OpenAsync(devicePath, "Device detail (merged save)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceEdit_" + SubtitleKey), "the subtitle appearance row offers its editor");
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey), "the subtitle editor opened");
            var dialog = Dialog(c.Frame)!; var body = (Control)dialog.FindName("AppearanceBody"); await Until(() => body.IsEnabled, "the editor is ready");
            var slider = (Slider)dialog.FindName("TextOpacitySlider"); var baseline = (int)slider.Value; var dragged = baseline == 55 ? 56 : 55;
            var first = s.HoldAnswer("PUT", subtitles); var second = s.HoldAnswer("PUT", subtitles);

            ChoosePill(dialog, "SizePillsHost", "small"); await Until(() => SubtitleWrites(s).Count() == 1, "the first edit is sent and its answer held");
            ChoosePill(dialog, "PositionPillsHost", "top");
            Click(((Panel)dialog.FindName("FontColorSwatchHost")).Children.OfType<Button>().Single(x => (string)x.Tag == "#facc15"));
            await Task.Delay(300);
            Check(SubtitleWrites(s).Count() == 1, "the two later edits wait behind the save in flight");
            Check(ActivePill(dialog, "SizePillsHost") == "small" && ActivePill(dialog, "PositionPillsHost") == "top", "the editor shows every edit at once");

            first.SetResult();
            await Until(() => SubtitleWrites(s).Count() == 2, "the two queued edits go out as one save");
            await Task.Delay(250);
            Check(ActivePill(dialog, "SizePillsHost") == "small" && ActivePill(dialog, "PositionPillsHost") == "top",
                "while that merged save is in flight the controls still show the queued edits");
            Check(DialogStatus(dialog) == T("web.admin.common.subtitleAppearanceDialog.saving"), "and the status says it is saving");

            slider.Value = dragged;                                                                    // a slider drag during the merged save
            await Task.Delay(300);
            Check(SubtitleWrites(s).Count() == 2, "the drag waits behind the merged save");
            second.SetResult();
            await Until(() => SubtitleWrites(s).Count() == 3, "the drag goes out when the merged save was answered");
            await Until(() => DialogStatus(dialog) == T("web.admin.common.subtitleAppearanceDialog.savedAutomatically"), "every save was accepted");

            var writes = SubtitleWrites(s).Select(WrittenValue).ToArray();
            Check(writes[0]["fontSize"]!.GetValue<string>() == "small", "request 1 carries the first edit");
            Check(Differences(writes[0], writes[1]).SequenceEqual(new[] { "fontColor", "position" }) && writes[1]["fontSize"]!.GetValue<string>() == "small", "request 2 is request 1 plus the two queued edits");
            Check(Differences(writes[1], writes[2]).SequenceEqual(new[] { "textOpacity" }) && writes[2]["textOpacity"]!.GetValue<int>() == dragged, "request 3 is request 2 plus the drag and nothing else: no queued edit went back to an old value");
            Check(writes[2]["fontSize"]!.GetValue<string>() == "small" && writes[2]["position"]!.GetValue<string>() == "top" && writes[2]["fontColor"]!.GetValue<string>() == "#facc15", "request 3 still holds every earlier edit");
            Program.Log("INFO: merged-save requests differ by " + string.Join("|", Differences(writes[0], writes[1])) + " then " + string.Join("|", Differences(writes[1], writes[2])) + " (initial text opacity " + baseline + ")");
            Check(ActivePill(dialog, "SizePillsHost") == "small" && ActivePill(dialog, "PositionPillsHost") == "top" && (int)slider.Value == dragged, "the final controls show every edit");
            Check(((TextBlock)dialog.FindName("SaveStatusText")).Text.Length > 0 && body.IsEnabled && !c.Devices.Session.NeedsReview, "the editor is usable and nothing needs a review");
        });

        await ScenarioAsync(c, "devices: an editor whose draft was retired stops accepting edits and says so", async () =>
        {
            s.Reset();
            await c.OpenAsync(devicePath, "Device detail (retired draft)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceEdit_" + SubtitleKey), "the editor is offered");
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey), "the subtitle editor opened");
            var dialog = Dialog(c.Frame)!; var body = (Control)dialog.FindName("AppearanceBody"); await Until(() => body.IsEnabled, "the editor is ready");
            // The device's detail is replaced under the open editor without the admin session retiring (a newer read of the same device).
            var revision = typeof(AdminDevicesPage).GetField("_detailRevision", BindingFlags.NonPublic | BindingFlags.Instance)!;
            revision.SetValue(c.Devices, (long)revision.GetValue(c.Devices)! + 1);
            var before = SubtitleWrites(s).Count();
            ChoosePill(dialog, "SizePillsHost", "small");
            await Until(() => !body.IsEnabled, "the editor is disabled once its edit could not be saved");
            Check(DialogStatus(dialog) == T("admin.native.userResources.subtitleEditor.retired") && !((Control)dialog.FindName("ResetButton")).IsEnabled, "it says the device changed and must be opened again, and does not say \"saved automatically\"");
            Check(SubtitleWrites(s).Count() == before, "nothing was written");
            Check(ActivePill(dialog, "SizePillsHost") != "small", "the controls no longer show an edit that was not saved");
            Check(((TextBlock)dialog.FindName("SaveStatusText")).Text != T("web.admin.common.subtitleAppearanceDialog.savedAutomatically"), "the status never claims the change was saved");
        });

        await ScenarioAsync(c, "devices: a reset with an unknown outcome locks the editor and the page", async () =>
        {
            s.Reset();
            await c.OpenAsync(devicePath, "Device detail (reset unknown)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceEdit_" + SubtitleKey), "the editor is offered");
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey), "the subtitle editor opened");
            var dialog = Dialog(c.Frame)!; var body = (Control)dialog.FindName("AppearanceBody"); await Until(() => body.IsEnabled, "the editor is ready");
            ChoosePill(dialog, "SizePillsHost", "small");
            await Until(() => SubtitleWrites(s).Count() == 1 && DialogStatus(dialog) == T("web.admin.common.subtitleAppearanceDialog.savedAutomatically"), "an override exists");
            var reset = (Button)dialog.FindName("ResetButton"); await Until(() => reset.IsEnabled && reset.Visibility == Visibility.Visible, "the editor offers Reset override");
            s.LoseAnswer("DELETE", ValuesPath + SubtitleKey);
            Click(reset);
            await Until(() => c.Devices.Session.NeedsReview, "the lost answer requires a review");
            await Until(() => !body.IsEnabled && !reset.IsEnabled, "the editor locks as for an unknown save");
            Check(DialogStatus(dialog).Contains("may have reached the server") && Dialog(c.Frame) != null, "it says the change may have reached the server and stays open for the admin to read it");
            await Task.Delay(500);
            Check(s.Count("DELETE", ValuesPath + SubtitleKey) == 1 && SubtitleWrites(s).Count() == 1, "the clear was sent once and nothing else was sent after it");
            dialog.Hide(); await GoneAsync(c, "the editor closed");
            Click(Find<Button>(c.Devices, "ResourceReload")); await Until(() => !c.Devices.Session.NeedsReview, "the reload clears the review");
        });

        await ScenarioAsync(c, "dialogs: a button pressed the moment a dialog closes opens its dialog on the first click, and never beside another", async () =>
        {
            s.Reset();
            await c.OpenAsync(devicePath, "Device detail (dialog hand-over)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceResetProfile") && Has(c.Devices, "DeviceReset_player.audio_sync_ms"), "the reset buttons are shown");
            var host = typeof(AdminDevicesPage).GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(c.Devices)!;
            var confirm = host.GetType().GetMethod("ConfirmAsync", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!;
            // Rounds are repeated (a bounded number of times) until at least one second press was made while the slot was occupied and no longer showing:
            // that is the window the wait exists for, and a press that merely follows a finished dialog proves nothing about it.
            int windowHits = 0, presses = 0, round;
            for (round = 0; round < 6 || (windowHits == 0 && round < 40); round++)
            {
                var id = round % 2 == 0 ? "DeviceResetProfile" : "DeviceReset_player.audio_sync_ms";
                Click(Find<Button>(c.Devices, id)); presses++;
                await Until(() => Dialog(c.Frame) != null, "round " + round + ": the confirmation opened on the first click", 4000);
                // A second request while a dialog is showing is refused at once, never opened beside it.
                var refused = (Task<bool>)confirm.Invoke(host, ["Second", "Never shown", "OK", false, null])!;
                Check(await refused.WaitAsync(TimeSpan.FromSeconds(3)) == false && VisualTreeHelper.GetOpenPopupsForXamlRoot(c.Frame.XamlRoot).SelectMany(p => All(p.Child)).OfType<ContentDialog>().Count() == 1,
                    "round " + round + ": a second dialog is not opened beside the first");
                Dialog(c.Frame)!.Hide();
                // The very moment the popup has left the screen, press the next button: no wait for the dialog's owner to finish.
                for (var spins = 0; Dialog(c.Frame) != null && spins < 20000; spins++) await Task.Yield();
                var next = Find<Button>(c.Devices, round % 2 == 0 ? "DeviceReset_player.audio_sync_ms" : "DeviceResetProfile");
                var before = SlotNow(host);                                  // the slot's state immediately before the press
                if (before.Occupied && !before.Showing) windowHits++;
                Click(next); presses++;
                try { await Until(() => Dialog(c.Frame) != null, "round " + round + ": a button pressed as the previous dialog closed still opens its dialog", 4000); }
                catch { Program.Log("DIAG: slot after the dropped press: " + SlotState(host)); throw; }
                Check(s.Count("DELETE", ValuesPath) == 0, "round " + round + ": cancelling sent nothing");
                Dialog(c.Frame)!.Hide(); await GoneAsync(c, "round " + round + ": closed");
            }
            Check(windowHits >= 1, "at least one round pressed while the slot was occupied and not showing (the closing gap)");
            Program.Log("INFO: dialog hand-over: " + presses + " buttons pressed once each, " + presses + " dialogs opened; " + windowHits + " of " + round + " rounds pressed with the slot occupied and not showing");
        });

        await ScenarioAsync(c, "devices: Edit subtitles and a reset pressed while a dialog is closing open one dialog, never two, and nothing is lost silently", async () =>
        {
            s.Reset();
            await c.OpenAsync(devicePath, "Device detail (two presses)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceReset_player.audio_sync_ms") && Has(c.Devices, "DeviceEdit_" + SubtitleKey), "both buttons are shown");
            var host = typeof(AdminDevicesPage).GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(c.Devices)!;
            int hits = 0, attempts = 0, editorWon = 0, resetWon = 0, mostAtOnce = 0, secondOpened = 0;
            for (; attempts < 40 && (attempts < 3 || hits == 0); attempts++)
            {
                Click(Find<Button>(c.Devices, "DeviceReset_player.audio_sync_ms"));
                await Until(() => Dialog(c.Frame) != null, "attempt " + attempts + ": a confirmation is open", 4000);
                await Task.Delay(250); Dialog(c.Frame)!.Hide();
                for (var spins = 0; Dialog(c.Frame) != null && spins < 20000; spins++) await Task.Yield();
                var before = SlotNow(host);
                if (before.Occupied && !before.Showing) hits++;
                // Both presses in the same moment, the second without waiting for the first to show anything.
                Click(Find<Button>(c.Devices, "DeviceReset_player.audio_sync_ms")); Click(Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey));
                for (var waited = 0; waited < 1500; waited += 25) { mostAtOnce = Math.Max(mostAtOnce, OpenDialogCount(c)); await Task.Delay(25); }
                var first = Dialog(c.Frame);
                Check(first != null && OpenDialogCount(c) == 1, "attempt " + attempts + ": exactly one dialog opened for the two presses");
                if (first is SubtitleAppearanceDialog) editorWon++; else resetWon++;
                Check(c.Devices.Session.Error is null or "" && !HasText(c.Devices, "could not be opened"), "attempt " + attempts + ": no failure was reported for either press");
                first!.Hide(); await GoneAsync(c, "attempt " + attempts + ": the dialog closed");
                // The other request was refused or opens afterwards; it never was an exception.
                await Task.Delay(1200);
                if (Dialog(c.Frame) is { } later) { secondOpened++; Check(OpenDialogCount(c) == 1, "attempt " + attempts + ": the later dialog is alone"); later.Hide(); await GoneAsync(c, "attempt " + attempts + ": the later dialog closed"); }
                await Until(() => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey).IsEnabled, "attempt " + attempts + ": the Edit button is usable again once nothing is pending");
                Check(s.Count("DELETE", ValuesPath) == 0 && c.Devices.Session.Error is null or "", "attempt " + attempts + ": nothing was deleted and no error shows");
            }
            Check(hits >= 1, "at least one attempt pressed while the slot was occupied and not showing");
            Check(mostAtOnce <= 1, "never more than one dialog was open at once");
            Program.Log("INFO: two presses: " + attempts + " attempts, " + hits + " with the slot occupied and not showing, the editor won " + editorWon + ", the reset confirmation won " + resetWon + ", the other opened afterwards " + secondOpened + ", most dialogs open at once " + mostAtOnce);
        });

        await ScenarioAsync(c, "devices: closing the editor during a save and pressing Edit again waits for the save and the read, then opens on the saved value", async () =>
        {
            s.Reset();
            await c.OpenAsync(devicePath, "Device detail (editor reopened during a save)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceEdit_" + SubtitleKey), "the editor is offered");
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey), "the subtitle editor opened");
            var dialog = Dialog(c.Frame)!; var body = (Control)dialog.FindName("AppearanceBody"); await Until(() => body.IsEnabled, "the editor is ready");
            var held = s.HoldAnswer("PUT", subtitles);
            ChoosePill(dialog, "SizePillsHost", "small"); await Until(() => SubtitleWrites(s).Count() == 1, "the save is sent and its answer held");
            dialog.Hide(); await Until(() => Dialog(c.Frame) == null, "the editor closed while its save is still in flight");
            var host = typeof(AdminDevicesPage).GetField("_host", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(c.Devices)!;
            var edit = Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey);
            Check(!edit.IsEnabled && ToolTipService.GetToolTip(edit) is string tip && tip == T("admin.native.userResources.subtitleEditor.finishing"), "the Edit button is disabled and says the previous editor is still finishing");
            Check(SlotNow(host) is { Occupied: true, Showing: false }, "the slot is still held, though nothing is on the screen");
            // A press that reaches the page anyway (assistive technology, a stale handler) is refused: no second editor.
            var press = (Task)typeof(AdminDevicesPage).GetMethod("EditSubtitlesAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(c.Devices, ["p7"])!;
            await press.WaitAsync(TimeSpan.FromSeconds(3)); await Task.Delay(300);
            Check(Dialog(c.Frame) == null && SubtitleWrites(s).Count() == 1, "a second editor did not open and nothing else was written while the save is pending");
            held.SetResult();
            await Until(() => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey).IsEnabled, "the Edit button is usable once the save has landed and the page has read again");
            Check(SlotNow(host) is { Occupied: false }, "the slot was released only then");
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey), "the editor opened again");
            var again = Dialog(c.Frame)!; var againBody = (Control)again.FindName("AppearanceBody"); await Until(() => againBody.IsEnabled, "the new editor is ready");
            Check(ActivePill(again, "SizePillsHost") == "small", "the new editor starts from the value the server now holds");
            ChoosePill(again, "PositionPillsHost", "top");
            await Until(() => SubtitleWrites(s).Count() == 2, "the first edit of the new editor is sent");
            await Until(() => DialogStatus(again) == T("web.admin.common.subtitleAppearanceDialog.savedAutomatically"), "and accepted");
            Check(DialogStatus(again) != T("admin.native.userResources.subtitleEditor.writeBusy") && DialogStatus(again) != T("admin.native.userResources.subtitleEditor.retired") && againBody.IsEnabled, "it was neither refused as busy nor retired");
            var writes = SubtitleWrites(s).Select(WrittenValue).ToArray();
            Check(writes[1]["fontSize"]!.GetValue<string>() == "small" && writes[1]["position"]!.GetValue<string>() == "top", "the second request holds the first editor's value and the new edit");
            again.Hide(); await GoneAsync(c, "the new editor closed");
            await Until(() => Find<Button>(c.Devices, "DeviceEdit_" + SubtitleKey).IsEnabled, "the Edit button is usable again");
        });

        await ScenarioAsync(c, "segmented options keep the platform highlight in a Windows contrast theme", async () =>
        {
            await Task.Yield();
            var ui = typeof(AdminDevicesPage).Assembly.GetType("SiloPlayer.Views.Admin.AdminUserResourceUi")!;
            var selected = ui.GetMethod("OfficialSelected", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!;
            var contrastButton = new ToggleButton(); var normalButton = new ToggleButton(); var thickness = contrastButton.BorderThickness;
            selected.Invoke(null, [contrastButton, true]); selected.Invoke(null, [normalButton, false]);
            Check(contrastButton.Resources.Count == 0 && contrastButton.BorderThickness == thickness, "in a contrast theme the option's checked fill, text and border are left to the platform's highlight pair");
            Check(normalButton.Resources.Count == AdminSelectionLook.KeysReplaced(false).Count && normalButton.BorderThickness == new Thickness(0), "outside one the official hover fill, foreground text and no border are applied");
            Check(AdminSelectionLook.KeysReplaced(false).All(key => normalButton.Resources.ContainsKey(key)) && AdminSelectionLook.KeysReplaced(true).Count == 0, "the keys replaced are exactly the ones the shared look lists");
            Program.Log("INFO: contrast branch exercised through the contrast seam of OfficialSelected; a real Windows contrast theme was not switched on");
        });

        await ScenarioAsync(c, "groups: member names and group names in notices and accessible names are plain text", async () =>
        {
            s.Reset();
            var hostile = "Guest" + Chr(0x200B) + Chr(0x202E) + "Nine" + Chr(0xFEFF) + Chr(0x2060);
            var group = "Fam" + Chr(0x200B) + Chr(0x2066) + "ily" + Chr(0x2062);
            s.User("9")["username"] = hostile; s.Group("2")["name"] = group;
            await c.OpenAsync(AdminRouteRequest.AccessGroup("3"), "Group editor (plain notices)");
            await Until(() => All(c.Frame).OfType<AdminAccessGroupsPage>().Any() && Has(c.Groups, "GroupMember_9"), "the members are listed");
            var named = AutomationProperties.GetName(Find<CheckBox>(c.Groups, "GroupMember_9"));
            Check(Plain(named) && !HasInvisible(named) && named.Contains("Guest Nine", StringComparison.Ordinal), "the accessible name of a member row is plain text");
            Find<CheckBox>(c.Groups, "GroupMember_9").IsChecked = true; SelectTag(Find<ComboBox>(c.Groups, "GroupMoveTarget"), "2");
            await OpenDialogAsync(c, () => Find<Button>(c.Groups, "GroupMoveMembers"), "move confirmation opened");
            var dialog = Dialog(c.Frame)!;
            Check(Texts(dialog).All(x => Plain(x.Text) && !HasInvisible(x.Text)) && !HasInvisible(dialog.Title.ToString()!), "the move confirmation shows only plain text, change lines included");
            s.Fail("PUT", "/api/v2/admin/users/9", 409, ProblemBody("This account is locked.", 409)); Click(Primary(dialog));
            await Until(() => HasText(c.Groups, "This account is locked."), "the refused move is listed by account");
            var named9 = Texts(c.Groups).Where(x => x.Text.Contains("Guest", StringComparison.Ordinal) && x.Text.Contains("locked", StringComparison.Ordinal)).ToArray();
            Program.Log("INFO: failure notice texts naming the account: " + string.Join(" || ", Texts(c.Groups).Where(x => x.Text.Contains("locked", StringComparison.Ordinal)).Select(x => x.Text.Replace("\n", " "))));
            Check(named9.Length > 0 && named9.All(x => Plain(x.Text) && !HasInvisible(x.Text) && x.Text.Contains("Guest Nine", StringComparison.Ordinal)), "the account's name in the failure notice is plain text");
            await OpenDialogAsync(c, () => Find<Button>(c.Groups, "GroupMoveMembers"), "move confirmation opened again");
            Click(Primary(Dialog(c.Frame)!));
            await Until(() => Dialog(c.Frame) == null && HasText(c.Groups, "Moved 1 user to Fam ily"), "the notice of a successful move names the group as plain text");
            Check(Texts(c.Groups).Where(x => x.Text.StartsWith("Moved ", StringComparison.Ordinal)).All(x => Plain(x.Text) && !HasInvisible(x.Text)), "the moved notice holds no control, direction or invisible character");
        });
    }
}
