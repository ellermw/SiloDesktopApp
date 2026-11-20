using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

        await ScenarioAsync(c, "devices: detail, anomalies, canonical writes, refused, lost answer, resets", async () =>
        {
            s.Reset();
            await c.OpenAsync(AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7"), "Device detail");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceProfileTab_p7") && Has(c.Devices, "DeviceSetting_player.hdr_enabled"), "detail rendered from the canonical values");
            Check(s.Calls.Any(x => x.Method == "GET" && x.Path.EndsWith("/admin/users/7/settings/values")) && !s.Calls.Any(x => x.Path.Contains("device-settings")), "overrides come from the canonical values route only");
            Check(!Find<ToggleSwitch>(c.Devices, "DeviceSetting_player.hdr_enabled").IsOn && Has(c.Devices, "DeviceReset_player.hdr_enabled"), "an override shows its value and a Reset");
            Check(HasText(c.Devices, "Conflicts with HDR disabled"), "a conflicting pair of overrides is flagged on the row");
            Check(HasText(c.Devices, T("web.admin.common.deviceOverrides.badge.extreme")), "an extreme value is flagged");
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

            Click(Find<Button>(c.Devices, "DeviceReset_player.audio_sync_ms")); await Until(() => Dialog(c.Frame) != null, "reset confirmation opened");
            Dialog(c.Frame)!.Hide(); await GoneAsync(c, "reset cancelled"); Check(s.Count("DELETE", ValuesPath) == 0, "a cancelled reset sends nothing");
            Click(Find<Button>(c.Devices, "DeviceReset_player.audio_sync_ms")); await Until(() => Dialog(c.Frame) != null, "reset confirmation opened again"); Click(Primary(Dialog(c.Frame)!));
            await Until(() => Dialog(c.Frame) == null && !Has(c.Devices, "DeviceReset_player.audio_sync_ms"), "a confirmed reset removes the override");

            var deletes = s.Count("DELETE", ValuesPath); Click(Find<Button>(c.Devices, "DeviceResetProfile")); await Until(() => Dialog(c.Frame) != null, "profile reset confirmation opened");
            Check(Dialog(c.Frame)!.Title.ToString()!.Contains("Alex", StringComparison.Ordinal), "the confirmation names the profile");
            s.DropRequest("DELETE", ValuesPath + "player.dv_profile7_hdr10_fallback"); Click(Primary(Dialog(c.Frame)!));
            await Until(() => c.Devices.Session.NeedsReview, "a transport failure mid-reset stops the sequence");
            Check(s.Count("DELETE", ValuesPath) >= deletes + 1, "earlier keys were sent once and none were repeated");
        });

        await ScenarioAsync(c, "devices: a fractional setting shows its exact value and opening it writes nothing", async () =>
        {
            s.Reset(); s.Values.Add(AdminUserResourcesFakeServer.Obj("""{"key":"player.playback_speed","scope":"profile_device","profile_id":"p7","device_id":"desktop","value":1.25,"updated_at":"2026-11-01T00:00:00Z"}"""));
            await c.OpenAsync(AdminRouteRequest.Device("7", "desktop").WithQuery("profile", "p7"), "Device detail (fractional)");
            await Until(() => All(c.Frame).OfType<AdminDevicesPage>().Any() && Has(c.Devices, "DeviceSetting_player.playback_speed"), "the playback speed override is shown");
            var speed = Find<NumberBox>(c.Devices, "DeviceSetting_player.playback_speed"); await Until(() => speed.IsLoaded && speed.Text.Length > 0, "the number box shows its text");
            Check(speed.Value == 1.25 && speed.Text.Replace(',', '.') == "1.25", "a fractional override shows its exact value, not a rounded one");
            Check(s.Count("PUT", ValuesPath) == 0, "showing the device writes nothing");
        });
    }
}
