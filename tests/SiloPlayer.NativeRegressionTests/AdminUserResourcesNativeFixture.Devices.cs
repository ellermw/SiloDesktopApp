using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services.Admin;
using SiloPlayer.Views.Admin;

internal static partial class AdminUserResourcesNativeFixture
{
    private const string ValuesPath = "/api/v2/admin/users/7/settings/values/";

    private static async Task DevicesAsync(Context c)
    {
        var s = c.Server;
        await ScenarioAsync(c, "devices: empty fleet, failed read with retry, grouped list, saved view", async () =>
        {
            s.Reset(); s.EmptyFleet = true;
            await c.OpenAsync(new AdminRouteRequest(AdminRoute.Devices), "Devices (empty)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && HasText(c.Devices, T("web.admin.devices.empty.noDevicesTitle")), "an empty fleet says so");
            s.Reset(); s.Fail("GET", "/api/v2/admin/devices", 500);
            await c.OpenAsync(new AdminRouteRequest(AdminRoute.Devices), "Devices (failed read)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceRetry"), "a failed fleet read offers a retry");
            Check(Has(c.Devices, "ResourceReload"), "page-level Reload stays reachable");
            Click(Find<Button>(c.Devices, "DeviceRetry")); await Until(() => Has(c.Devices, "DeviceGroup_u:7"), "the retry loads the fleet");
            Check(!Has(c.Devices, "Device_7_desktop"), "groups start collapsed when nothing is filtered");
            Click(Find<Button>(c.Devices, "DeviceGroup_u:7")); await Until(() => Has(c.Devices, "Device_7_desktop") && Has(c.Devices, "Device_7_phone"), "opening a group lists its devices");
            Find<ToggleButton>(c.Devices, "DeviceView_anomalies").IsChecked = true;
            await Until(() => Has(c.Devices, "Device_7_phone") && Has(c.Devices, "Device_8_tv") && !Has(c.Devices, "Device_7_desktop"), "the Anomalies view keeps only flagged devices");
            Check(HasText(c.Devices, T("web.admin.devices.list.countOfTotal", ("count", 2), ("count_text", 2), ("total", 3))), "the list says how many of the total are shown");
            Click(Find<Button>(c.Devices, "DeviceListClear")); await Until(() => !Has(c.Devices, "DeviceListClear"), "Clear all removes the filters");
            await Until(() => Has(c.Devices, "DeviceGroup_u:7"), "the unfiltered list is back");
            await CaptureAsync(c.Devices, "admin-devices-list-wide.png");
        });

        await ScenarioAsync(c, "devices: override badges and Status fit the detail pane", async () =>
        {
            s.Reset();
            string[] keys = ["player.hdr_enabled", "player.dv_profile7_hdr10_fallback", "player.audio_sync_ms"];
            var clipped = new List<string>();
            await c.OpenAsync(AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7"), "Device detail (override widths)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && keys.All(key => Has(c.Devices, "DeviceReset_" + key)),
                "all three warning rows rendered");
            Find<ToggleButton>(c.Devices, "DeviceSettingsScope_overrides").IsChecked = true;
            try
            {
                foreach (var width in new[] { 1440, 1024, 800 })
                {
                    c.Window.AppWindow.ResizeClient(new(width, 900));
                    await Until(() => Math.Abs(c.Frame.ActualWidth - width) < 1, $"device width is {width}px");
                    Find<Button>(c.Devices, "DeviceResetProfile").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .25 });
                    await Task.Delay(250);
                    c.Frame.UpdateLayout();
                    foreach (var key in keys)
                    {
                        var reset = Find<Button>(c.Devices, "DeviceReset_" + key);
                        var controls = (StackPanel)VisualTreeHelper.GetParent(reset);
                        var row = (Grid)VisualTreeHelper.GetParent(controls);
                        var copy = (StackPanel)row.Children[0];
                        var title = (Panel)copy.Children[0];
                        var badges = title.Children.OfType<Border>().ToArray();
                        // Official: an override is marked by a dot, not a pill; only the warning is still a badge.
                        var dots = title.Children.OfType<Ellipse>().ToArray();
                        Check(dots.Length == 1 && badges.Length == 1, $"{width}px {key} has an override dot and one warning badge");
                        Check(dots[0].Width == 6 && dots[0].Height == 6 && AutomationProperties.GetName(dots[0]) == T("web.admin.common.deviceOverrides.override.setLabel"), $"{width}px {key} dot is 6 px and named");
                        Check(copy.Children.OfType<TextBlock>().Any(text => text.Text == key), $"{width}px {key} shows its raw key");

                        // A StackPanel can arrange the badge at its full width outside the space the grid gave the heading.
                        var copySlot = LayoutInformation.GetLayoutSlot(copy);
                        foreach (var badge in badges)
                        {
                            var bounds = badge.TransformToVisual(row).TransformBounds(new Windows.Foundation.Rect(0, 0, badge.ActualWidth, badge.ActualHeight));
                            var arrangedWidth = Math.Max(0, Math.Min(bounds.Right, copySlot.Right) - Math.Max(bounds.Left, copySlot.Left));
                            if (arrangedWidth + 1 < badge.DesiredSize.Width || All(badge).OfType<TextBlock>().Any(text => text.IsTextTrimmed))
                                clipped.Add($"{width}px {key}: {arrangedWidth:F1} arranged of {badge.DesiredSize.Width:F1} desired");
                        }
                    }
                    var status = All(c.Devices).OfType<TextBlock>().Single(text => text.Text.StartsWith("aligned with desktop", StringComparison.Ordinal));
                    if (status.IsTextTrimmed) clipped.Add($"{width}px Status summary is trimmed");
                    await CaptureAsync(c.Frame, $"admin-device-override-layout-{width}.png");
                }
                Check(clipped.Count == 0, "override badges and Status fit at each width" + (clipped.Count > 0 ? " (" + string.Join(", ", clipped) + ")" : ""));
            }
            finally { c.Window.AppWindow.ResizeClient(new(1440, 900)); }
        });

        await ScenarioAsync(c, "devices: detail, anomalies, canonical writes, refused, lost answer, resets", async () =>
        {
            s.Reset();
            await c.OpenAsync(AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7"), "Device detail");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceProfileTab_p7") && Has(c.Devices, "DeviceSetting_player.hdr_enabled"), "detail rendered from the canonical values");
            Check(s.Calls.Any(x => x.Method == "GET" && x.Path.EndsWith("/admin/users/7/settings/values")) && !s.Calls.Any(x => x.Path.Contains("device-settings")), "overrides come from the canonical values route only");
            Check(!Find<ToggleSwitch>(c.Devices, "DeviceSetting_player.hdr_enabled").IsOn && Has(c.Devices, "DeviceReset_player.hdr_enabled"), "an override shows its value and a Reset");
            Check(HasText(c.Devices, "Conflicts with HDR disabled"), "a conflicting pair of overrides is flagged on the row");
            Check(HasText(c.Devices, AppStrings.Uppercase(T("web.admin.common.deviceOverrides.badge.extreme"))), "an extreme value is flagged, in the official small capitals");
            Check(Has(c.Devices, "DeviceSetting_ui.text_scale"), "settings the profile's own device screen hides are offered here, as the official admin does");
            await CaptureAsync(c.Devices, "admin-devices-detail-wide.png");
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(800, 900)); await Task.Delay(150);
            await CaptureAsync(c.Devices, "admin-devices-detail-compact.png");
            c.Window.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(1440, 900)); await Task.Delay(150);

            Find<ToggleSwitch>(c.Devices, "DeviceSetting_playback.auto_play_next").IsOn = true;
            await Until(() => Has(c.Devices, "DeviceReset_playback.auto_play_next"), "a write creates the override and the pane re-reads");
            var put = s.Last("PUT", ValuesPath + "playback.auto_play_next")!;
            Check(put.Query == "?scope=profile_device&profile_id=p7&device_id=desktop" && JsonNode.Parse(put.Body)!["value"]!.GetValue<bool>(), "the write names user, profile, device, key and a typed value");

            var hdr = s.Count("PUT", ValuesPath + "player.hdr_enabled"); s.Fail("PUT", ValuesPath + "player.hdr_enabled", 422, ProblemBody("That value is not allowed."));
            Find<ToggleSwitch>(c.Devices, "DeviceSetting_player.hdr_enabled").IsOn = true;
            await Until(() => HasText(c.Devices, "That value is not allowed."), "a refused write shows the server's detail");
            await Until(() => !Find<ToggleSwitch>(c.Devices, "DeviceSetting_player.hdr_enabled").IsOn, "the control returns to the saved value");
            Check(!c.Devices.Session.NeedsReview && s.Count("PUT", ValuesPath + "player.hdr_enabled") == hdr + 1, "a refusal does not lock the page and is not repeated");

            s.LoseAnswer("PUT", ValuesPath + "player.hdr_enabled"); Find<ToggleSwitch>(c.Devices, "DeviceSetting_player.hdr_enabled").IsOn = true;
            await Until(() => c.Devices.Session.NeedsReview, "a lost answer requires a review");
            Check(s.Count("PUT", ValuesPath + "player.hdr_enabled") == hdr + 2, "the lost write was sent once");
            Click(Find<Button>(c.Devices, "ResourceReload")); await Until(() => !c.Devices.Session.NeedsReview && Has(c.Devices, "DeviceSetting_player.hdr_enabled") && Find<ToggleSwitch>(c.Devices, "DeviceSetting_player.hdr_enabled").IsOn, "the reload shows the value the server holds");

            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceReset_player.audio_sync_ms"), "reset confirmation opened");
            Dialog(c.Frame)!.Hide(); await GoneAsync(c, "reset cancelled"); Check(s.Count("DELETE", ValuesPath) == 0, "a cancelled reset sends nothing");
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceReset_player.audio_sync_ms"), "reset confirmation opened again"); Click(Primary(Dialog(c.Frame)!));
            await Until(() => Dialog(c.Frame) == null && !Has(c.Devices, "DeviceReset_player.audio_sync_ms"), "a confirmed reset removes the override");

            var deletes = s.Count("DELETE", ValuesPath); await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceResetProfile"), "profile reset confirmation opened");
            Check(Dialog(c.Frame)!.Title.ToString()!.Contains("Alex", StringComparison.Ordinal), "the confirmation names the profile");
            s.DropRequest("DELETE", ValuesPath + "player.dv_profile7_hdr10_fallback"); Click(Primary(Dialog(c.Frame)!));
            await Until(() => c.Devices.Session.NeedsReview, "a transport failure mid-reset stops the sequence");
            Check(s.Count("DELETE", ValuesPath) >= deletes + 1, "earlier keys were sent once and none were repeated");
        });

        await ScenarioAsync(c, "devices: a profile reset refused partway shows what the server still holds", async () =>
        {
            s.Reset();
            await c.OpenAsync(AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7"), "Device detail (partial reset)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceResetProfile") && Has(c.Devices, "DeviceReset_player.hdr_enabled") && Has(c.Devices, "DeviceReset_player.audio_sync_ms"), "the profile's three overrides are shown");
            s.Fail("DELETE", ValuesPath + "player.dv_profile7_hdr10_fallback", 429, ProblemBody("Too many requests.", 429));
            await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceResetProfile"), "profile reset confirmation opened"); Click(Primary(Dialog(c.Frame)!));
            await Until(() => Dialog(c.Frame) == null && s.Count("DELETE", ValuesPath) == 2 && !Has(c.Devices, "DeviceReset_player.hdr_enabled") && Has(c.Devices, "DeviceReset_player.dv_profile7_hdr10_fallback") && Has(c.Devices, "DeviceReset_player.audio_sync_ms"),
                "the override deleted before the refusal is no longer shown as one, and the others remain");
            Check(!c.Devices.Session.NeedsReview && s.Count("DELETE", ValuesPath) == 2, "the reset stopped at the refusal, repeated nothing and did not lock the page");
            await Until(() => HasText(c.Devices, T("web.admin.common.users.resetAllStopped", ("count", 3), ("count_text", 3), ("completed", 1), ("reason", c.Devices.Session.Error ?? ""))), "the notice that the reset stopped stays after the re-read");
        });

        await ScenarioAsync(c, "devices: the confirmed intro reset removes both shown overrides", async () =>
        {
            s.Reset();
            s.Values.Add(AdminUserResourcesFakeServer.Obj("""{"key":"playback.auto_skip_intro","scope":"profile_device","profile_id":"p7","device_id":"desktop","value":true}"""));
            s.Values.Add(AdminUserResourcesFakeServer.Obj("""{"key":"playback.intro_skip_mode","scope":"profile_device","profile_id":"p7","device_id":"desktop","value":"both"}"""));
            await c.OpenAsync(AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7"), "Device detail (intro pair)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceReset_playback.intro_skip_mode") &&
                Has(c.Devices, "DeviceReset_playback.auto_skip_intro"), "both intro overrides shown");
            var heldOldValues = s.HoldAnswer("GET", ValuesPath.TrimEnd('/'));
            var heldRefresh = s.HoldAnswer("GET", "/api/v2/admin/devices/7/desktop");
            try
            {
                const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
                var revision = typeof(AdminDevicesPage).GetField("_detailRevision", PrivateInstance)!.GetValue(c.Devices);
                var read = typeof(AdminDevicesPage).GetMethod("ReadDeviceValuesAsync", PrivateInstance)!;
                var olderList = (Task<AdminDeviceValues?>)read.Invoke(c.Devices, [c.Devices.Session.Owner, "7", revision])!;
                await OpenDialogAsync(c, () => Find<Button>(c.Devices, "DeviceReset_playback.intro_skip_mode"), "intro reset confirmation opened");
                Click(Primary(Dialog(c.Frame)!));
                await Until(() => !Has(c.Devices, "DeviceReset_playback.intro_skip_mode") && !Has(c.Devices, "DeviceReset_playback.auto_skip_intro") &&
                    Has(c.Devices, "DeviceReset_player.audio_sync_ms"), "both intro rows leave immediately while the refresh is still held");
                heldOldValues.TrySetResult();
                Check(await olderList.WaitAsync(TimeSpan.FromSeconds(6)) == null, "the old list response is discarded before it can restore either override");
                Find<ToggleButton>(c.Devices, "DeviceSettingsScope_overrides").IsChecked = true;
                foreach (var width in new[] { 1440, 800 })
                {
                    c.Window.AppWindow.ResizeClient(new(width, 900)); await Task.Delay(250);
                    Find<Button>(c.Devices, "DeviceResetProfile").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .25 });
                    await Task.Delay(250);
                    await CaptureAsync(c.Frame, $"admin-device-intro-reset-{width}.png");
                }
                c.Window.AppWindow.ResizeClient(new(1440, 900));
            }
            finally { heldOldValues.TrySetResult(); heldRefresh.TrySetResult(); }
            Check(s.Count("DELETE", ValuesPath) == 1, "the intro pair is removed by the one selected reset");
        });

        await ScenarioAsync(c, "devices: a fractional setting shows its exact value and opening it writes nothing", async () =>
        {
            s.Reset(); s.Values.Add(AdminUserResourcesFakeServer.Obj("""{"key":"player.playback_speed","scope":"profile_device","profile_id":"p7","device_id":"desktop","value":1.25,"updated_at":"2026-11-01T00:00:00Z"}"""));
            await c.OpenAsync(AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7"), "Device detail (fractional)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceSetting_player.playback_speed"), "the playback speed override is shown");
            var speed = Find<Slider>(c.Devices, "DeviceSetting_player.playback_speed"); var speedBox = (AdminSettingSlider)speed.Parent; await Until(() => speed.IsLoaded && speedBox.Readout.Text.Length > 0, "the slider shows its value");
            Check(speed.Value == 1.25 && speedBox.Readout.Text == "1.25 x", "a fractional override shows its exact value, not a rounded one");
            Check(Math.Abs(speed.Minimum - .25) < 1e-9 && speed.Maximum == 3 && Math.Abs(speed.StepFrequency - .05) < 1e-9 && Math.Abs(speed.SmallChange - .05) < 1e-9 && Math.Abs(speed.LargeChange - .5) < 1e-9, "the speed slider runs 0.25 to 3 in steps of 0.05, ten steps for a page key");
            Check(s.Count("PUT", ValuesPath) == 0, "showing the device writes nothing");
        });

        await ScenarioAsync(c, "devices: override dot, pinned profile bar, and sliders that write once per commit", async () =>
        {
            s.Reset(); s.Values.Add(AdminUserResourcesFakeServer.Obj("""{"key":"player.playback_speed","scope":"profile_device","profile_id":"p7","device_id":"desktop","value":1.25,"updated_at":"2026-11-01T00:00:00Z"}"""));
            await c.OpenAsync(AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7"), "Device detail (sliders)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceRowsScroll") && Has(c.Devices, "DeviceSetting_player.audio_sync_ms") && Has(c.Devices, "DeviceSetting_playback.auto_play_next"), "the detail with its list is shown");
            Windows.Foundation.Point Origin(UIElement element) => element.TransformToVisual(c.Devices).TransformPoint(new Windows.Foundation.Point(0, 0));

            // The dot: one for each override, none for a setting the device does not override, and no "overridden" pill anywhere.
            var resets = All(c.Devices).OfType<Button>().Count(x => AutomationProperties.GetAutomationId(x).StartsWith("DeviceReset_", StringComparison.Ordinal));
            var dots = All(c.Devices).OfType<Ellipse>().Where(x => AutomationProperties.GetAutomationId(x) == "DeviceOverrideDot").ToArray();
            Program.Log($"INFO: override resets={resets} dots={dots.Length} opacity={dots.FirstOrDefault()?.Opacity}"); Check(resets == 4 && dots.Length == resets && dots.All(x => x.Width == 6 && x.Height == 6 && Math.Abs(x.Opacity - .8) < .001), "each override row has the 6 px dot and no other row has one");
            Check(!HasText(c.Devices, T("web.admin.common.deviceOverrides.override.setLabel")) && !HasText(c.Devices, AppStrings.Uppercase(T("web.admin.common.deviceOverrides.override.setLabel"))), "no pill says the setting is overridden");
            var plain = (Grid)VisualTreeHelper.GetParent((StackPanel)VisualTreeHelper.GetParent(Find<ToggleSwitch>(c.Devices, "DeviceSetting_playback.auto_play_next")));
            var plainTitle = (Panel)((StackPanel)plain.Children[0]).Children[0];
            var plainPill = plainTitle.Children.OfType<Border>().Single(); var plainLabel = All(plainPill).OfType<TextBlock>().Single();
            Check(!plainTitle.Children.OfType<Ellipse>().Any() && plainLabel.Text == AppStrings.Uppercase(plainLabel.Text) && plainLabel.FontSize == 9.5 && plainLabel.CharacterSpacing == 50, "a setting without an override keeps its small uppercase source label");

            // The sliders: the manifest's range and step, the value beside the thumb, a page key moving ten steps.
            var sync = Find<Slider>(c.Devices, "DeviceSetting_player.audio_sync_ms"); var syncBox = (AdminSettingSlider)sync.Parent;
            Check(sync.Minimum == -5000 && sync.Maximum == 5000 && sync.StepFrequency == 1 && sync.SmallChange == 1 && sync.LargeChange == 10 && sync.Value == 4500, "the audio sync slider runs -5000 to 5000 in steps of 1 and starts on the stored value");
            Check(syncBox.Readout.Text == "4500 milliseconds" && syncBox.Readout.FontSize == 12 && syncBox.Readout.MinWidth == 64 && syncBox.MaxWidth == 260 && !sync.IsThumbToolTipEnabled, "the value is read out beside the slider, as the official control does");

            // A drag: the thumb and the value follow, nothing is sent until the release, then exactly one write.
            var writes = s.Count("PUT", ValuesPath);
            syncBox.BeginGesture(); sync.Value = 4000; sync.Value = 3000; sync.Value = 2500;
            await Task.Delay(400);
            Check(syncBox.Readout.Text == "2500 milliseconds" && s.Count("PUT", ValuesPath) == writes, "while the thumb is held the value follows it and nothing is sent");
            syncBox.EndGesture();
            await Until(() => s.Count("PUT", ValuesPath) == writes + 1 && Find<Slider>(c.Devices, "DeviceSetting_player.audio_sync_ms") is { } done && done.Value == 2500 && !c.Devices.Session.Busy, "releasing sends one write and the page shows the saved value");
            Check(JsonNode.Parse(s.Last("PUT", ValuesPath + "player.audio_sync_ms")!.Body)!["value"]!.GetValue<long>() == 2500, "the write carries the released value as a whole number");
            await Task.Delay(300); Check(s.Count("PUT", ValuesPath) == writes + 1, "nothing more was sent after the release");

            // A key press is one commit; the keyboard stays on the slider across the page's re-read.
            sync = Find<Slider>(c.Devices, "DeviceSetting_player.audio_sync_ms"); sync.Focus(FocusState.Keyboard); await Task.Delay(100); Check(FocusManager.GetFocusedElement(c.Devices.XamlRoot) == sync, "the slider takes the keyboard focus");
            sync.Value += sync.SmallChange;
            await Until(() => s.Count("PUT", ValuesPath) == writes + 2 && Find<Slider>(c.Devices, "DeviceSetting_player.audio_sync_ms").Value == 2501 && !c.Devices.Session.Busy, "one arrow step is one write");
            await Until(() => FocusManager.GetFocusedElement(c.Devices.XamlRoot) is Slider focused && AutomationProperties.GetAutomationId(focused) == "DeviceSetting_player.audio_sync_ms", "the keyboard focus is back on the slider after the page re-read");
            sync = Find<Slider>(c.Devices, "DeviceSetting_player.audio_sync_ms"); sync.Value += sync.LargeChange;
            await Until(() => s.Count("PUT", ValuesPath) == writes + 3 && Find<Slider>(c.Devices, "DeviceSetting_player.audio_sync_ms").Value == 2511 && !c.Devices.Session.Busy, "one page step moves ten steps in one write");
            Check(JsonNode.Parse(s.Last("PUT", ValuesPath + "player.audio_sync_ms")!.Body)!["value"]!.GetValue<long>() == 2511, "the page step wrote 2511");

            // A fractional step does not carry floating-point noise into the request.
            var speed = Find<Slider>(c.Devices, "DeviceSetting_player.playback_speed"); speed.Value += speed.SmallChange;
            await Until(() => s.Count("PUT", ValuesPath) == writes + 4 && !c.Devices.Session.Busy, "a speed step is one write");
            Check(JsonNode.Parse(s.Last("PUT", ValuesPath + "player.playback_speed")!.Body)!["value"]!.GetValue<double>() == 1.3, "the speed step writes 1.3, not 1.3000000000000003");

            // A refused write puts the thumb back on what the server holds.
            s.Fail("PUT", ValuesPath + "player.audio_sync_ms", 422, ProblemBody("That value is not allowed."));
            sync = Find<Slider>(c.Devices, "DeviceSetting_player.audio_sync_ms"); sync.Value -= 100;
            await Until(() => HasText(c.Devices, "That value is not allowed.") && Find<Slider>(c.Devices, "DeviceSetting_player.audio_sync_ms").Value == 2511, "a refusal shows the server's detail and the thumb returns to the saved value");
            Check(s.Count("PUT", ValuesPath) == writes + 5 && !c.Devices.Session.NeedsReview, "the refused change was sent once and does not lock the page");

            // The profile bar is pinned: the list scrolls on its own while the tabs and the bar stay where they are.
            await Task.Delay(600); var scroll = Find<ScrollViewer>(c.Devices, "DeviceRowsScroll"); var bar = Find<Button>(c.Devices, "DeviceResetProfile"); var tab = Find<ToggleButton>(c.Devices, "DeviceProfileTab_p7"); var marker = Find<Slider>(c.Devices, "DeviceSetting_player.audio_sync_ms");
            Check(scroll.ScrollableHeight > 200 && scroll.ActualHeight <= scroll.MaxHeight + 1 && scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "the list is taller than its own scroll region");
            var barBefore = Origin(bar); var tabBefore = Origin(tab); var rowBefore = Origin(marker); var offsetBefore = scroll.VerticalOffset;
            scroll.ChangeView(null, 240, null, true); await Task.Delay(500);
            await Until(() => Math.Abs(scroll.VerticalOffset - 240) < 1 && Math.Abs(Origin(Find<Slider>(c.Devices, "DeviceSetting_player.audio_sync_ms")).Y - (rowBefore.Y + offsetBefore - 240)) < 2, "the list scrolled to 240 and its rows moved with it");
            Check(Math.Abs(Origin(bar).Y - barBefore.Y) < .5 && Math.Abs(Origin(tab).Y - tabBefore.Y) < .5, "the profile bar and the tabs stay in place while the list scrolls");
            // The scrolled place survives a write: the list is rebuilt after it, and must not jump back to the top.
            var before = s.Count("PUT", ValuesPath); var prompt = Find<Slider>(c.Devices, "DeviceSetting_playback.next_up_prompt_seconds"); Find<Button>(c.Devices, "DeviceResetProfile").Focus(FocusState.Programmatic); await Task.Delay(300);
            var placed = Find<ScrollViewer>(c.Devices, "DeviceRowsScroll").VerticalOffset; Check(placed > 100, "the list is scrolled down while the slider is used");
            prompt.Value += 1;
            await Until(() => s.Count("PUT", ValuesPath) == before + 1 && Has(c.Devices, "DeviceReset_playback.next_up_prompt_seconds") && !c.Devices.Session.Busy, "a slider on a row that was not overridden creates the override");
            await Task.Delay(300);
            Program.Log($"INFO: list offset before {placed} after {Find<ScrollViewer>(c.Devices, "DeviceRowsScroll").VerticalOffset}"); Check(Math.Abs(Find<ScrollViewer>(c.Devices, "DeviceRowsScroll").VerticalOffset - placed) < 2, "the list keeps its place after the write");

            // Captures at the three widths, the list scrolled part-way at 1024.
            foreach (var width in new[] { 1440, 1024, 800 })
            {
                c.Window.AppWindow.ResizeClient(new(width, 900));
                await Until(() => Math.Abs(c.Frame.ActualWidth - width) < 1, $"device width is {width}px");
                // 1024: the list scrolled part-way, to the speed and sync sliders; the others show the top of the list.
                var list = Find<ScrollViewer>(c.Devices, "DeviceRowsScroll"); list.ChangeView(null, 0, null, true); await Task.Delay(200);
                if (width == 1024) { Find<Slider>(c.Devices, "DeviceSetting_player.playback_speed").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .35 }); await Task.Delay(300); }
                Find<ToggleButton>(c.Devices, "DeviceProfileTab_p7").StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = .1 });
                await Task.Delay(400);
                await CaptureAsync(c.Frame, $"admin-device-detail-layout-{width}.png");
            }
            c.Window.AppWindow.ResizeClient(new(1440, 900));
        });
    }
}
