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
