using System.Text.Json;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Official 217ff25 admin devices rules: the override rows, their values, anomalies and inheritance. Synthetic values only.</summary>
public sealed class AdminUserResourceDeviceTests
{
    private static JsonElement J(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANumberControlWritesOnlyARealChangeAtTheStepsPrecision()
    {
        var speed = AdminDevicePresentation.Definition("player.playback_speed")!; var sync = AdminDevicePresentation.Definition("player.audio_sync_ms")!;
        Assert.Equal(2, AdminDevicePresentation.StepDigits(speed)); Assert.Equal(0, AdminDevicePresentation.StepDigits(sync));
        // Focus loss reads the shown text back: the same value, or one that differs only past the digits shown, is not a change.
        Assert.Null(AdminDevicePresentation.NumberChange(speed, 1.25, 1.25)); Assert.Null(AdminDevicePresentation.NumberChange(speed, 1.25, 1.2500000000004));
        Assert.Null(AdminDevicePresentation.NumberChange(speed, 1.234, 1.234)); Assert.Null(AdminDevicePresentation.NumberChange(speed, 1.25, double.NaN));
        // A spin step carries no floating-point noise into the request.
        Assert.Equal("1.3", AdminDevicePresentation.NumberChange(speed, 1.25, 1.25 + 0.05)); Assert.Equal("1.2", AdminDevicePresentation.NumberChange(speed, 1.25, 1.25 - 0.05));
        Assert.Equal("1.23", AdminDevicePresentation.NumberChange(speed, 1.234, 1.23)); Assert.Equal("1", AdminDevicePresentation.NumberChange(speed, 1.25, 1));
        Assert.Equal("4501", AdminDevicePresentation.NumberChange(sync, 4500, 4501)); Assert.Equal("-20", AdminDevicePresentation.NumberChange(sync, 0, -20.4));
    }

    [Fact]
    public void ASliderRunsOverTheManifestRangeInTheManifestStepAndReadsOutWithItsUnit()
    {
        var speed = AdminDevicePresentation.Definition("player.playback_speed")!; var sync = AdminDevicePresentation.Definition("player.audio_sync_ms")!; var prompt = AdminDevicePresentation.Definition("playback.next_up_prompt_seconds")!;
        // The range is the manifest's; a missing step is the official slider's default of one.
        Assert.Equal((-5000d, 5000d), (sync.Minimum, sync.Maximum)); Assert.Equal((0.25, 3d), (speed.Minimum, speed.Maximum)); Assert.Equal((0d, 120d), (prompt.Minimum, prompt.Maximum));
        Assert.Equal(1, AdminDevicePresentation.SliderStep(sync)); Assert.Equal(1, AdminDevicePresentation.SliderStep(prompt)); Assert.Equal(0.05, AdminDevicePresentation.SliderStep(speed));
        // The value beside the slider is the shown value at the step's precision with the manifest's unit, whole numbers through the unit's own message.
        Assert.Equal("4500 milliseconds", AdminDevicePresentation.SliderReadout(sync, 4500)); Assert.Equal("-20 milliseconds", AdminDevicePresentation.SliderReadout(sync, -20));
        Assert.Equal("1.25 x", AdminDevicePresentation.SliderReadout(speed, 1.25)); Assert.Equal("1.3 x", AdminDevicePresentation.SliderReadout(speed, 1.25 + 0.05));
        Assert.Equal("30 seconds", AdminDevicePresentation.SliderReadout(prompt, 30));
        // A drag that passes through many values is one change from the saved value to the released one; coming back to the saved value is none.
        Assert.Equal("2500", AdminDevicePresentation.NumberChange(sync, 4500, 2500)); Assert.Null(AdminDevicePresentation.NumberChange(sync, 2500, 2500));
        Assert.Equal("1.3", AdminDevicePresentation.NumberChange(speed, 1.25, 1.3000000000000003));
    }

    [Fact]
    public void AProfilesAccentIsTheOfficialHashOfItsId()
    {
        // 'p' is 112 and '7' is 55: 112 * 31 + 55 = 3527, which is 7 modulo 8, and the eighth hue is 335. An empty id hashes to 0, the first hue.
        Assert.Equal(335, AdminDevicePresentation.ProfileAccentHue("p7")); Assert.Equal(12, AdminDevicePresentation.ProfileAccentHue("")); Assert.Equal(38, AdminDevicePresentation.ProfileAccentHue("a"));
        // The hash wraps as a 32-bit integer, so a long id still picks one of the eight.
        Assert.Contains(AdminDevicePresentation.ProfileAccentHue(new string('z', 200)), new[] { 12, 38, 145, 195, 220, 265, 295, 335 });
    }

    [Fact]
    public void DefinitionsAreTheDeviceScopedManifestInOrderWithOnlyRetiredThemeWithheld()
    {
        var keys = AdminDevicePresentation.AllDefinitions.Select(x => x.Key).ToArray();
        Assert.Equal(41, keys.Length);
        Assert.Equal("playback.buffering_quality_mode", keys[0]);
        Assert.DoesNotContain("ui.theme", keys);
        Assert.Contains("ui.text_scale", keys); Assert.Contains("nav.primary_menu", keys); Assert.Contains("playback.auto_skip_intro", keys); // shown, unlike the profile's own device screen
        Assert.Equal(Array.IndexOf(keys, "player.hdr_enabled"), AdminDevicePresentation.Order("player.hdr_enabled"));
        Assert.Equal(int.MaxValue, AdminDevicePresentation.Order("unknown.key"));
    }

    [Fact]
    public void PlatformTagsHideInertSettingsButNeverAStoredOverrideOrAnUnknownPlatform()
    {
        string[] Keys(string platform, params string[] stored) => AdminDevicePresentation.ForPlatform(platform, stored).Select(x => x.Key).ToArray();
        Assert.DoesNotContain("player.audio_sync_ms", Keys("Chrome Web"));
        Assert.Contains("player.audio_sync_ms", Keys("Chrome Web", "player.audio_sync_ms"));
        Assert.Contains("player.audio_sync_ms", Keys("Toaster OS")); // an unrecognized platform offers everything
        Assert.Contains("player.audio_sync_ms", Keys("iOS"));
    }

    [Fact]
    public void ValuesAreRetypedThroughTheManifestAndBadInputIsRefusedWithoutSending()
    {
        Assert.Equal(JsonValueKind.True, AdminDevicePresentation.ValueFromString("player.hdr_enabled", "true").ValueKind);
        Assert.Equal(JsonValueKind.False, AdminDevicePresentation.ValueFromString("player.hdr_enabled", "anything else").ValueKind);
        Assert.Equal(12, AdminDevicePresentation.ValueFromString("player.audio_sync_ms", " 12.7 ").GetInt64());
        Assert.Throws<ArgumentException>(() => AdminDevicePresentation.ValueFromString("player.audio_sync_ms", "twelve"));
        Assert.Throws<ArgumentException>(() => AdminDevicePresentation.ValueFromString("player.audio_sync_ms", "NaN"));
        Assert.Equal(JsonValueKind.Null, AdminDevicePresentation.ValueFromString("playback.max_bitrate_kbps", " ").ValueKind); // a nullable value clears with empty text
        Assert.Equal("future value", AdminDevicePresentation.ValueFromString("future.key", "future value").GetString()); // unknown keys travel as themselves
        Assert.Equal("1", AdminDevicePresentation.ValueString(J("1"))); Assert.Equal("", AdminDevicePresentation.ValueString(J("null"))); Assert.Equal("{\"a\":1}", AdminDevicePresentation.ValueString(J("{\"a\":1}")));
    }

    [Fact]
    public void FleetAnomaliesFlagOutliersAndDormantOverridersWithTheirNumbers()
    {
        JsonElement Device(string id, int overrides, string updated) => J($$"""{"user_id":"1","device_id":"{{id}}","device_name":"{{id}}","device_platform":"iOS","override_count":{{overrides}},"last_updated":"{{updated}}","profiles":[]}""");
        var fleet = new[] { Device("a", 1, "2026-11-19T12:00:00Z"), Device("b", 1, "2026-11-19T12:00:00Z"), Device("c", 6, "2026-11-19T12:00:00Z"), Device("d", 2, "2026-09-01T00:00:00Z"), Device("e", 0, "2026-09-01T00:00:00Z") };
        var anomalies = AdminDevicePresentation.Anomalies(fleet, Now);
        Assert.Equal(["1/c", "1/d"], anomalies.Keys.Order().ToArray());
        Assert.True(anomalies["1/c"].Outlier); Assert.False(anomalies["1/c"].Stale);
        Assert.True(anomalies["1/d"].Stale); Assert.Equal(80, anomalies["1/d"].StaleDays);
        Assert.Contains("6 overrides", anomalies["1/c"].Summary); Assert.Contains("stale 80d", anomalies["1/d"].Summary);
        Assert.Empty(AdminDevicePresentation.Anomalies([], Now));
    }

    [Fact]
    public void OverridesRowsFlagConflictsExtremeValuesAndDormantDevices()
    {
        var conflicts = AdminDevicePresentation.Conflicts(new Dictionary<string, string> { ["player.hdr_enabled"] = "false", ["player.dv_profile7_hdr10_fallback"] = "true", ["playback.auto_skip_credits"] = "true", ["playback.auto_play_next"] = "false" });
        Assert.Equal(3, conflicts.Count); Assert.All(conflicts.Values, x => Assert.Equal("conflict", x.Kind));
        Assert.Empty(AdminDevicePresentation.Conflicts(new Dictionary<string, string> { ["player.hdr_enabled"] = "true", ["player.dv_profile7_hdr10_fallback"] = "true" }));
        var none = new Dictionary<string, AdminSettingAnomaly>();
        Assert.Equal("extreme", AdminDevicePresentation.RowAnomaly("player.audio_sync_ms", "4500", true, none, null)!.Kind);
        Assert.Null(AdminDevicePresentation.RowAnomaly("player.audio_sync_ms", "3000", true, none, null));
        Assert.Null(AdminDevicePresentation.RowAnomaly("player.audio_sync_ms", "4500", false, none, null)); // a row that is not an override is never flagged
        Assert.Equal("stale", AdminDevicePresentation.RowAnomaly("player.hdr_enabled", "true", true, none, 45)!.Kind);
        Assert.Null(AdminDevicePresentation.RowAnomaly("player.hdr_enabled", "true", true, none, 30));
        Assert.Equal("conflict", AdminDevicePresentation.RowAnomaly("player.hdr_enabled", "false", true, conflicts, 45)!.Kind);
    }

    [Fact]
    public void InheritedValueFollowsTheResolutionOrderAndSaysWhenAnotherScopeMayApply()
    {
        var definition = AdminDevicePresentation.Definition("playback.audio_language")!;
        var values = new[] { J("""{"key":"playback.audio_language","scope":"profile","profile_id":"p1","value":"fr"}"""), J("""{"key":"playback.audio_language","scope":"profile_library","profile_id":"p1","library_id":"3","value":"de"}""") };
        var inherited = AdminDevicePresentation.Inherited(definition, "p1", "Alex", values);
        Assert.Equal("Alex", inherited.ProfileName); Assert.Equal("fr", inherited.Raw); Assert.Equal("library", inherited.Varies);
        Assert.Contains("Alex", AdminDevicePresentation.InheritedSource(inherited));
        var other = AdminDevicePresentation.Inherited(definition, "p2", "Sam", values); // another profile's values never leak in
        Assert.Null(other.ProfileName); Assert.Null(other.Varies); Assert.Equal("", other.Raw);
        Assert.Equal("app default", AdminDevicePresentation.InheritedSource(other));
        var hdr = AdminDevicePresentation.Inherited(AdminDevicePresentation.Definition("player.hdr_enabled")!, "p1", "Alex", values);
        Assert.Equal("true", hdr.Raw); Assert.Equal("Enabled", hdr.Display);
    }

    [Fact]
    public void ValuesPrintAsTheOfficialRowsDo()
    {
        Assert.Equal("Enabled", AdminDevicePresentation.FormatValue("player.hdr_enabled", "true"));
        Assert.Equal("Disabled", AdminDevicePresentation.FormatValue("player.hdr_enabled", "false"));
        Assert.Equal("No limit", AdminDevicePresentation.FormatValue("playback.max_bitrate_kbps", ""));
        Assert.Equal("8 Mbps", AdminDevicePresentation.FormatValue("playback.max_bitrate_kbps", "8000"));
        Assert.Equal("future", AdminDevicePresentation.FormatValue("unknown.key", "future"));
        Assert.Contains("500", AdminDevicePresentation.FormatValue("player.audio_sync_ms", "500"));
        var options = AdminDevicePresentation.Options(AdminDevicePresentation.Definition("playback.max_bitrate_kbps")!, "7000").Select(x => x.Value).ToArray();
        Assert.Equal("", options[0]); Assert.Contains("7000", options); Assert.True(Array.IndexOf(options, "7000") > Array.IndexOf(options, "6000"));
    }
}
