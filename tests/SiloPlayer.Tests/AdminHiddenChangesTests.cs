using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using static SiloPlayer.Tests.HiddenChangesWire;
using R = SiloPlayer.Core.Services.Admin.AdminPlaybackSettingsRules;

namespace SiloPlayer.Tests;

/// <summary>
/// "A change the admin cannot see is never sent" (official server #4696) for the native Storage, Playback and General (Redis) pages: a row a mode or switch hides
/// stays in view while it holds an edit, and a Redis switch-off with no saved URL drops what was typed. Synthetic values only.
/// </summary>
public sealed class AdminHiddenChangesTests
{
    // ---------------------------------------------------------------- Storage: link-mode rows

    private sealed class FakeDraft(string urlAuth, params string[] dirty) : IAdminStorageDraft
    {
        public string Get(string key) => key == AdminStorageRules.UrlAuth ? urlAuth : "";
        public string GetPersisted(string key) => "";
        public bool IsDirty(string key) => dirty.Contains(key);
    }

    [Theory]
    [InlineData("", 0, new string[0])]
    [InlineData("presigned", 0, new string[0])]
    [InlineData("public", 1, new[] { "s3.public_read_endpoint" })]
    [InlineData("cloudflare_token", 4, new[] { "s3.public_read_endpoint", "s3.public_token_secret", "s3.public_token_param", "s3.public_token_ttl" })]
    public void AnUntouchedAdvancedTierShowsOnlyTheRowsTheLinkModeUses(string mode, int count, string[] shown)
    {
        var draft = new FakeDraft(mode);
        Assert.Equal(count, AdminStorageRules.PublicAdvancedCount(draft));
        Assert.Equal(shown.Order(), AdminStorageRules.PublicS3AdvancedKeys.Where(key => AdminStorageRules.ShowsAdvanced(draft, key)).Order());
        Assert.DoesNotContain(AdminStorageRules.PublicS3AdvancedKeys, key => AdminStorageRules.KeptForEdit(draft, key));
    }

    [Fact]
    public void ARowTheLinkModeDoesNotUseStaysAndIsMarkedWhileItHoldsAnEdit()
    {
        var draft = new FakeDraft("presigned", "s3.public_token_secret", "s3.public_token_ttl");
        Assert.Equal(2, AdminStorageRules.PublicAdvancedCount(draft));
        Assert.True(AdminStorageRules.ShowsAdvanced(draft, "s3.public_token_secret"));
        Assert.True(AdminStorageRules.KeptForEdit(draft, "s3.public_token_secret"));
        Assert.True(AdminStorageRules.KeptForEdit(draft, "s3.public_token_ttl"));
        Assert.False(AdminStorageRules.ShowsAdvanced(draft, "s3.public_token_param"));
        Assert.False(AdminStorageRules.ShowsAdvanced(draft, "s3.public_read_endpoint"));
        // Under a mode that uses the row nothing is "kept": it is simply shown.
        var cloudflare = new FakeDraft("cloudflare_token", "s3.public_token_secret");
        Assert.True(AdminStorageRules.ShowsAdvanced(cloudflare, "s3.public_token_secret"));
        Assert.False(AdminStorageRules.KeptForEdit(cloudflare, "s3.public_token_secret"));
    }

    [Fact]
    public void NoDirtyAdvancedRowIsHiddenInAnyLinkModeOrCombination()
    {
        var keys = AdminStorageRules.PublicS3AdvancedKeys;
        foreach (var mode in new[] { "", "presigned", "public", "cloudflare_token", "unrecognised" })
            for (var mask = 0; mask < 1 << keys.Count; mask++)
            {
                var dirty = keys.Where((_, index) => (mask & 1 << index) != 0).ToArray();
                var draft = new FakeDraft(mode, dirty);
                foreach (var key in dirty) Assert.True(AdminStorageRules.ShowsAdvanced(draft, key), $"{mode}/{key}");
            }
    }

    // ---------------------------------------------------------------- Playback rules and controller

    [Theory]
    [InlineData("enable_transcode_throttle", "false", "transcode_throttle_seconds")]
    [InlineData("allow_4k_transcode", "false", "playback.prefer_lower_version_transcode")]
    [InlineData("markers.mode", "off", "markers.detection_workers")]
    [InlineData("markers.mode", "online", "markers.detection_workers")]
    [InlineData("markers.mode", "off", "markers.online_storage")]
    [InlineData("markers.mode", "local", "markers.online_storage")]
    public void ARowASwitchOrModeHidesStaysWhileItHoldsAnEdit(string condition, string value, string key)
    {
        var values = new Dictionary<string, string> { [condition] = value };
        Assert.False(R.Visible(key, values, true, true));
        Assert.False(R.IsShown(key, values, true, true, [condition]));
        Assert.True(R.IsShown(key, values, true, true, [condition, key]));
    }

    [Fact]
    public void CapabilityHiddenRowsStayHiddenWhateverTheyHold()
    {
        Assert.False(R.IsShown("playback.preview_image_width", new Dictionary<string, string>(), false, true, ["playback.preview_image_width"]));
        Assert.False(R.IsShown("markers.detect_intros", new Dictionary<string, string>(), true, false, ["markers.detect_intros"]));
        Assert.Equal(["markers.detect_intros", "playback.trickplay_workers"],
            R.HiddenDirtyKeys(new Dictionary<string, string>(), ["markers.detect_intros", "playback.trickplay_workers", "markers.mode"], false, false).Order());
        Assert.Empty(R.HiddenDirtyKeys(new Dictionary<string, string>(), ["enable_transcode_throttle", "transcode_throttle_seconds"], true, true));
    }

    [Fact]
    public void LazyPlaybackStagedByOnDemandStorageHasNoRowButADraftUnderModeOffDoes()
    {
        var onDemand = new Dictionary<string, string> { ["markers.mode"] = "online", ["markers.online_storage"] = "on_demand" };
        Assert.False(R.IsShown("markers.lazy_playback", onDemand, true, true, ["markers.online_storage", "markers.lazy_playback"]));
        var off = new Dictionary<string, string> { ["markers.mode"] = "off", ["markers.online_storage"] = "on_demand" };
        Assert.True(R.IsShown("markers.lazy_playback", off, true, true, ["markers.lazy_playback"]));
    }

    private sealed class PlaybackHarness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminPlaybackSettingsController Controller;
        internal string Effective;
        internal string Sensitive = """{"configured":[],"managed_by_env":[]}""";
        internal PlaybackHarness(string effective = """{"markers.mode":"both","playback.preview_image_width":"300","playback.hw_accel":"vaapi","playback.hw_device":"/dev/dri/renderD128"}""")
        {
            Effective = effective;
            Authority = new(Kit.Client, Kit.Auth); var api = new AdminServerSettingsApi(Kit.Client);
            Store = new(api, Authority, R.Keys, []); Controller = new(api, Store, Authority, false);
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get) return Task.FromResult(SavedAck());
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("sensitive-status") && Sensitive == "fail") return Task.FromResult(AdminMonitorKit.Json("{}", System.Net.HttpStatusCode.ServiceUnavailable));
                var response = AdminMonitorKit.Json(path.EndsWith("effective") ? Effective : path.EndsWith("restart-keys") ? """{"keys":[],"prefixes":[]}""" : Sensitive);
                response.Headers.ETag = new("\"settings-1\""); return Task.FromResult(response);
            };
        }
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task BufferAheadEditedThenThrottlingSwitchedOffIsKeptAndSentTogether()
    {
        using var h = new PlaybackHarness(); await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(true, true);
        h.Controller.Set("transcode_throttle_seconds", "400");
        h.Controller.Set("enable_transcode_throttle", "false");
        Assert.Equal(["enable_transcode_throttle", "transcode_throttle_seconds"], h.Controller.State.DirtyKeys.Order());
        Assert.True(R.IsShown("transcode_throttle_seconds", h.Controller.State.Values, true, true, h.Controller.State.DirtyKeys));
        await h.Controller.SaveAsync();
        Assert.Equal(["enable_transcode_throttle", "transcode_throttle_seconds"], SentKeys(h.Kit));
        // Once saved the row has no edit and hides.
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.False(R.IsShown("transcode_throttle_seconds", h.Controller.State.Values, true, true, h.Controller.State.DirtyKeys));
    }

    [Fact]
    public async Task AnInvalidBufferAheadDraftStaysVisibleWithItsErrorAndBlocksSaveUntilCorrected()
    {
        using var h = new PlaybackHarness(); await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(true, true);
        h.Controller.Set("transcode_throttle_seconds", "5");
        h.Controller.Set("enable_transcode_throttle", "false");
        Assert.Contains("transcode_throttle_seconds", h.Controller.State.DirtyKeys);
        Assert.Contains("transcode_throttle_seconds", h.Controller.State.Errors.Keys);
        Assert.True(R.IsShown("transcode_throttle_seconds", h.Controller.State.Values, true, true, h.Controller.State.DirtyKeys));
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
        h.Controller.Set("transcode_throttle_seconds", "120");
        Assert.Empty(h.Controller.State.Errors);
        await h.Controller.SaveAsync();
        Assert.Equal(["enable_transcode_throttle", "transcode_throttle_seconds"], SentKeys(h.Kit));
    }

    [Fact]
    public async Task TheLowerVersionPreferenceAndAMarkerWorkerCountFollowTheSameRule()
    {
        using var h = new PlaybackHarness(); await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(true, true);
        h.Controller.Set("playback.prefer_lower_version_transcode", "false");
        h.Controller.Set("allow_4k_transcode", "false");
        h.Controller.Set("markers.detection_workers", "4");
        h.Controller.Set("markers.mode", "online");
        foreach (var key in new[] { "playback.prefer_lower_version_transcode", "markers.detection_workers" })
            Assert.True(R.IsShown(key, h.Controller.State.Values, true, true, h.Controller.State.DirtyKeys), key);
        await h.Controller.SaveAsync();
        Assert.Equal(["allow_4k_transcode", "markers.detection_workers", "markers.mode", "playback.prefer_lower_version_transcode"], SentKeys(h.Kit));
    }

    [Fact]
    public async Task OnDemandStorageStagesLazyPlaybackWithItAsTheOfficialPageDoes()
    {
        using var h = new PlaybackHarness("""{"markers.mode":"both","markers.online_storage":"stored","markers.lazy_playback":"false"}"""); await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(true, true);
        h.Controller.Set("markers.online_storage", "on_demand");
        Assert.Equal(["markers.lazy_playback", "markers.online_storage"], h.Controller.State.DirtyKeys.Order());
        // The paired value has no row of its own under this choice; it is neither reset nor shown.
        Assert.False(R.IsShown("markers.lazy_playback", h.Controller.State.Values, true, true, h.Controller.State.DirtyKeys));
        await h.Controller.SaveAsync();
        Assert.Equal(["markers.lazy_playback", "markers.online_storage"], SentKeys(h.Kit));
    }

    [Fact]
    public async Task ADraftForARowTheServerCapabilitiesRemoveIsDroppedNotSent()
    {
        using var h = new PlaybackHarness(); await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(true, true);
        h.Controller.Set("markers.detect_intros", "false");
        h.Controller.Set("playback.trickplay_workers", "4");
        h.Controller.Set("playback.watched_threshold", "80");
        h.Controller.SetVisibilityContext(false, false);
        Assert.Equal(["playback.watched_threshold"], h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(["playback.watched_threshold"], SentKeys(h.Kit));
    }

    // A capabilities read that FAILS is unknown, not false: official builds its keys from the last good data, so an edit is never dropped because a refetch failed.
    [Fact]
    public async Task AFailedCapabilitiesReadAfterASuccessfulOneKeepsEveryDraftAndSendsThemOnceTheCapabilityIsKnownAgain()
    {
        using var h = new PlaybackHarness(); await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(true, true);
        h.Controller.Set("playback.preview_image_width", "640");
        h.Controller.Set("playback.trickplay_workers", "4");
        h.Controller.Set("markers.detect_intros", "false");
        h.Controller.Set("markers.detect_credits", "false");
        string[] drafts = ["markers.detect_credits", "markers.detect_intros", "playback.preview_image_width", "playback.trickplay_workers"];
        // Both capability reads failed (the admin's Refresh, or the refresh after the acceleration changed).
        h.Controller.SetVisibilityContext(null, null);
        Assert.Equal(drafts, h.Controller.State.DirtyKeys.Order());
        Assert.Equal("640", h.Controller.State.Values["playback.preview_image_width"]);
        // Only one of them failed: the other's answer applies, the failed one keeps what it was.
        h.Controller.SetVisibilityContext(null, true);
        Assert.Equal(drafts, h.Controller.State.DirtyKeys.Order());
        // Known again (true): Save sends what the admin typed, once.
        h.Controller.SetVisibilityContext(true, true);
        await h.Controller.SaveAsync();
        Assert.Equal(drafts, SentKeys(h.Kit));
        Assert.Equal(1, Writes(h.Kit));
    }

    [Fact]
    public async Task AFailedCapabilityReadDoesNotUndoWhatAnEarlierReadSaidIsMissing()
    {
        using var h = new PlaybackHarness(); await h.Controller.OpenAsync();
        // The marker read said no detection kinds; the library read failed. The marker rows stay unavailable, and no trickplay draft is dropped.
        h.Controller.SetVisibilityContext(null, false);
        h.Controller.Set("playback.trickplay_workers", "4");
        h.Controller.Set("markers.detect_intros", "false");
        Assert.Equal(["playback.trickplay_workers"], h.Controller.State.DirtyKeys);
        h.Controller.SetVisibilityContext(null, null);
        h.Controller.Set("markers.detect_intros", "false");
        Assert.Equal(["playback.trickplay_workers"], h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task AFirstCapabilitiesReadThatFailsDropsNoDraft()
    {
        using var h = new PlaybackHarness(); await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(null, null);
        h.Controller.Set("playback.preview_image_width", "640");
        h.Controller.Set("markers.detect_intros", "false");
        Assert.Equal(["markers.detect_intros", "playback.preview_image_width"], h.Controller.State.DirtyKeys.Order());
        h.Controller.SetVisibilityContext(null, null);
        Assert.Equal(["markers.detect_intros", "playback.preview_image_width"], h.Controller.State.DirtyKeys.Order());
    }

    [Fact]
    public async Task ASuccessfulReadThatSaysFalseAfterAFailedOneDropsTheDraftThen()
    {
        using var h = new PlaybackHarness(); await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(true, true);
        h.Controller.Set("playback.preview_image_width", "640");
        h.Controller.Set("markers.detect_intros", "false");
        h.Controller.Set("playback.watched_threshold", "80");
        h.Controller.SetVisibilityContext(null, null);
        Assert.Equal(3, h.Controller.State.DirtyKeys.Count);
        h.Controller.SetVisibilityContext(false, false);
        Assert.Equal(["playback.watched_threshold"], h.Controller.State.DirtyKeys);
        Assert.Equal("300", h.Controller.State.Values["playback.preview_image_width"]);
        await h.Controller.SaveAsync();
        Assert.Equal(["playback.watched_threshold"], SentKeys(h.Kit));
    }

    [Fact]
    public void TheSupportRecordKeepsTheLastKnownCapabilityWhenItsReadFailedAndTakesTheNewAnswerOtherwise()
    {
        var good = new AdminPlaybackSupport(true, true, null, null, null, []);
        var bothFailed = new AdminPlaybackSupport(false, false, null, null, 7, []) { TrickplayRead = false, DetectionKindsRead = false };
        var merged = bothFailed.KeepingLastKnown(good);
        Assert.True(merged.Trickplay); Assert.True(merged.DetectionKindSettings); Assert.True(merged.TrickplayRead); Assert.True(merged.DetectionKindsRead);
        Assert.Equal(7, merged.PreviewsToRemake);
        // The first read failed: still unknown, and reported so (the page renders it hidden, the draft rule drops nothing).
        var first = bothFailed.KeepingLastKnown(null);
        Assert.False(first.TrickplayRead); Assert.False(first.DetectionKindsRead);
        Assert.False(bothFailed.KeepingLastKnown(first).TrickplayRead);
        // A read that succeeded says what it says, also when it is false.
        var removed = new AdminPlaybackSupport(false, false, null, null, null, []).KeepingLastKnown(good);
        Assert.False(removed.Trickplay); Assert.False(removed.DetectionKindSettings); Assert.True(removed.TrickplayRead);
        var mixed = new AdminPlaybackSupport(false, true, null, null, null, []) { TrickplayRead = false }.KeepingLastKnown(good);
        Assert.True(mixed.Trickplay); Assert.True(mixed.DetectionKindSettings);
    }

    [Fact]
    public async Task TheSupportReadReportsWhichCapabilityReadFailed()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var failLibrary = true; var failMarkers = false;
        kit.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (failLibrary && path.EndsWith("/libraries/capabilities")) throw new HttpRequestException("synthetic timeout");
            if (failMarkers && path.EndsWith("/markers/capabilities")) throw new HttpRequestException("synthetic timeout");
            if (path.EndsWith("/libraries/capabilities")) return Task.FromResult(AdminMonitorKit.Json("""{"trickplay":false}"""));
            if (path.EndsWith("/markers/capabilities")) return Task.FromResult(AdminMonitorKit.Json("""{"allowed":false,"state":"unavailable","detection_kind_settings":false}"""));
            return Task.FromResult(AdminMonitorKit.Json("""{"items":[]}"""));
        };
        var api = new AdminPlaybackAdministrationApi(kit.Client);
        var libraryFailed = await api.SupportAsync(owner, default, detectHardware: false);
        Assert.False(libraryFailed.TrickplayRead); Assert.True(libraryFailed.DetectionKindsRead); Assert.False(libraryFailed.Trickplay);
        failLibrary = false; failMarkers = true;
        var markersFailed = await api.SupportAsync(owner, default, detectHardware: false);
        Assert.True(markersFailed.TrickplayRead); Assert.False(markersFailed.DetectionKindsRead);
        failMarkers = false;
        var allRead = await api.SupportAsync(owner, default, detectHardware: false);
        // A read that succeeded and says "no" is a definite false.
        Assert.True(allRead.TrickplayRead); Assert.True(allRead.DetectionKindsRead); Assert.False(allRead.Trickplay); Assert.False(allRead.DetectionKindSettings);
    }

    // Finding 9: after a failed environment status re-read the last answered managed list stays, and a managed row that is hidden or kept in view is never sent.
    [Fact]
    public async Task AfterAFailedStatusReReadTheLastManagedListStaysAndAManagedKeptRowIsNeverSent()
    {
        using var h = new PlaybackHarness(); await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(true, true);
        h.Controller.Set("transcode_throttle_seconds", "400");
        h.Controller.Set("enable_transcode_throttle", "false");
        Assert.Equal(["enable_transcode_throttle", "transcode_throttle_seconds"], h.Controller.State.DirtyKeys.Order());
        h.Sensitive = """{"configured":[],"managed_by_env":["transcode_throttle_seconds"],"managed_by_env_vars":{"transcode_throttle_seconds":"SILO_SYNTHETIC"}}""";
        await h.Controller.ReloadAsync(preserveDraft: true);
        // The environment now sets the kept row: its draft goes with it.
        Assert.Equal(["enable_transcode_throttle"], h.Controller.State.DirtyKeys);
        Assert.True(h.Controller.State.EnvManaged.IsManaged("transcode_throttle_seconds"));
        h.Sensitive = "fail";
        await h.Controller.ReloadAsync(preserveDraft: true);
        Assert.True(h.Controller.State.Loaded);
        Assert.True(h.Controller.State.EnvManaged.IsManaged("transcode_throttle_seconds"));
        Assert.Equal(["transcode_throttle_seconds"], h.Controller.State.ManagedByEnv);
        h.Controller.Set("transcode_throttle_seconds", "500");
        Assert.Equal(["enable_transcode_throttle"], h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(["enable_transcode_throttle"], SentKeys(h.Kit));
    }

    [Fact]
    public async Task AManagedHiddenRowIsNeitherStagedNorSentAndRaisesNoError()
    {
        using var h = new PlaybackHarness { Sensitive = """{"configured":[],"managed_by_env":["transcode_throttle_seconds"],"managed_by_env_vars":{"transcode_throttle_seconds":"SILO_SYNTHETIC"}}""" };
        await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(true, true);
        h.Controller.Set("transcode_throttle_seconds", "5");
        h.Controller.Set("enable_transcode_throttle", "false");
        Assert.Equal(["enable_transcode_throttle"], h.Controller.State.DirtyKeys);
        Assert.Empty(h.Controller.State.Errors);
        await h.Controller.SaveAsync();
        Assert.Equal(["enable_transcode_throttle"], SentKeys(h.Kit));
    }

    // ---------------------------------------------------------------- Playback: the hardware device picker

    private const string A = "/dev/dri/renderD128", B = "/dev/dri/renderD129", C = "/dev/dri/renderD130";

    [Fact]
    public void SavedDevicesKeepTheirRowsAndAreNotDuplicated()
    {
        Assert.Equal([A, B, C], R.DeviceRows([A, B], C, $"{B},{C}"));
        Assert.Equal([A, B, C], R.DeviceRows([], $"{A}, {B}", C));
        Assert.Equal([A, B, C], R.DeviceOrder($"{B},{A}", [A, B, C]).Order());
        Assert.Equal([B, A, C], R.DeviceOrder($"{B},{A}", [A, B, C]));
    }

    [Fact]
    public void UnderNvencOnlyTheStagedDeviceCanGoBackOffAndNoRenderPathIsAdded()
    {
        // Saved A, staged A+B, then NVENC: the picker stays because the list holds an edit.
        Assert.True(R.ShowsDevicePicker(3, applies: false, dirty: true));
        Assert.False(R.ShowsDevicePicker(3, applies: false, dirty: false));
        Assert.False(R.ShowsDevicePicker(0, applies: true, dirty: true));
        Assert.False(R.DevicePickerApplies("none", false));
        Assert.False(R.DevicePickerApplies("nvenc", true));
        Assert.False(R.DevicePickerApplies("auto", true));
        Assert.True(R.DevicePickerApplies("auto", false));
        var staged = $"{A},{B}";
        Assert.True(R.DeviceSwitchLocked(false, false, C, staged, A));
        Assert.False(R.DeviceSwitchLocked(false, false, B, staged, A));
        Assert.False(R.DeviceSwitchLocked(false, false, A, staged, A));
        Assert.False(R.DeviceSwitchLocked(false, true, C, staged, A));
        Assert.True(R.DeviceSwitchLocked(true, true, C, staged, A));
        Assert.True(R.DeviceKeptForEdit(false, B, staged, A, orderOnly: false));
        Assert.False(R.DeviceKeptForEdit(false, A, staged, A, orderOnly: false));
        Assert.False(R.DeviceKeptForEdit(true, B, staged, A, orderOnly: false));
        // Switching the staged device back off lands on the saved devices: the saved text itself is staged, which undoes the edit.
        Assert.Equal(A, R.NextDeviceDraft(staged, A, B, R.DeviceOrder(A, [A, B, C])));
    }

    [Fact]
    public void AStagedListThatDiffersOnlyInOrderCanGoBackToTheSavedList()
    {
        var saved = $"{B},{A}";
        var order = R.DeviceOrder(saved, [A, B, C]);
        Assert.True(R.OrderOnlyDraft(true, [A, B, C], $"{A},{B}", saved));
        Assert.False(R.OrderOnlyDraft(false, [A, B, C], $"{A},{B}", saved));
        Assert.True(R.DeviceKeptForEdit(false, A, $"{A},{B}", saved, orderOnly: true));
        Assert.False(R.DeviceKeptForEdit(false, C, $"{A},{B}", saved, orderOnly: true));
        // A off, then on again, restores the saved order, which is the saved text.
        var off = R.NextDeviceDraft($"{A},{B}", saved, A, order);
        Assert.Equal(B, off);
        Assert.Equal(saved, R.NextDeviceDraft(off, saved, A, order));
    }

    [Fact]
    public async Task StagingTheSavedDeviceTextUndoesTheEditHoweverItIsSpaced()
    {
        using var h = new PlaybackHarness($$"""{"playback.hw_accel":"vaapi","playback.hw_device":"{{A}}, {{B}}"}"""); await h.Controller.OpenAsync();
        var saved = h.Controller.State.PersistedValues["playback.hw_device"];
        var order = R.DeviceOrder(saved, [A, B, C]);
        h.Controller.Set("playback.hw_device", R.NextDeviceDraft(saved, saved, C, order));
        Assert.Contains("playback.hw_device", h.Controller.State.DirtyKeys);
        h.Controller.Set("playback.hw_device", R.NextDeviceDraft(h.Controller.State.Values["playback.hw_device"], saved, C, order));
        Assert.DoesNotContain("playback.hw_device", h.Controller.State.DirtyKeys);
        Assert.Equal(saved, h.Controller.State.Values["playback.hw_device"]);
    }

    [Fact]
    public async Task AManagedDeviceListIsNeverStagedOrSent()
    {
        using var h = new PlaybackHarness { Sensitive = """{"configured":[],"managed_by_env":["playback.hw_device"],"managed_by_env_vars":{"playback.hw_device":"SILO_HW_DEVICE"}}""" };
        await h.Controller.OpenAsync();
        h.Controller.Set("playback.hw_device", $"{A},{B}");
        Assert.Empty(h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }

    // ---------------------------------------------------------------- General: Redis

    private sealed class GeneralHarness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority;
        internal readonly AdminServerSettingsStore Store;
        internal readonly AdminGeneralSettingsController Controller;
        internal string Sensitive;
        internal GeneralHarness(string sensitive)
        {
            Sensitive = sensitive;
            Authority = new(Kit.Client, Kit.Auth); var api = new AdminServerSettingsApi(Kit.Client);
            Store = new(api, Authority, AdminGeneralSettingsController.Keys, AdminGeneralSettingsController.Secrets); Controller = new(api, Store, Authority);
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get) return Task.FromResult(SavedAck());
                var path = request.RequestUri!.AbsolutePath;
                var response = AdminMonitorKit.Json(path.EndsWith("effective") ? """{"branding.server_name":"Silo","redis.db":"2"}""" : path.EndsWith("restart-keys") ? """{"keys":[],"prefixes":[]}""" : Sensitive);
                response.Headers.ETag = new("\"one\""); return Task.FromResult(response);
            };
        }
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private const string RedisNotConfigured = """{"configured":[],"managed_by_env":[]}""";
    private const string RedisConfigured = """{"configured":["redis.url"],"managed_by_env":[]}""";

    [Fact]
    public async Task SwitchingRedisOffWithNoSavedUrlDropsWhatWasTypedAndSendsNothing()
    {
        using var h = new GeneralHarness(RedisNotConfigured); await h.Controller.OpenAsync();
        h.Controller.StageRedisUrl("redis://synthetic.invalid:6379");
        h.Controller.Set("redis.db", "5");
        Assert.Contains("redis.url", h.Controller.State.DirtyKeys);
        h.Controller.DisableRedis();
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Equal("", h.Controller.State.Values["redis.url"]);
        Assert.Equal("2", h.Controller.State.Values["redis.db"]);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }

    [Fact]
    public async Task SwitchingRedisOffWithNoSavedUrlKeepsAnUnrelatedEditAndSendsOnlyThat()
    {
        using var h = new GeneralHarness(RedisNotConfigured); await h.Controller.OpenAsync();
        h.Controller.Set("branding.server_name", "Synthetic");
        h.Controller.StageRedisUrl("redis://synthetic.invalid:6379");
        h.Controller.DisableRedis();
        Assert.Equal(["branding.server_name"], h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        Assert.Equal(["branding.server_name"], SentKeys(h.Kit));
        Assert.DoesNotContain("synthetic.invalid", SentBody(h.Kit), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SwitchingRedisOffWithASavedUrlStagesTheEmptyUrlAndNeverSendsATypedReplacement()
    {
        using var h = new GeneralHarness(RedisConfigured); await h.Controller.OpenAsync();
        h.Controller.StageRedisUrl("redis://typed-synthetic.invalid:6379");
        h.Controller.DisableRedis();
        Assert.Equal("", h.Controller.State.Values["redis.url"]);
        Assert.Contains("redis.url", h.Controller.State.DirtyKeys);
        await h.Controller.SaveAsync();
        using var body = JsonDocument.Parse(SentBody(h.Kit));
        Assert.Equal("", body.RootElement.GetProperty("values").GetProperty("redis.url").GetString());
        Assert.DoesNotContain("typed-synthetic", SentBody(h.Kit), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManagedRedisIsNotSwitchedOffAndRaisesNoError()
    {
        using var h = new GeneralHarness("""{"configured":["redis.url"],"managed_by_env":["redis.url","redis.db"],"managed_by_env_vars":{"redis.url":"REDIS_URL","redis.db":"REDIS_URL"}}""");
        await h.Controller.OpenAsync();
        h.Controller.DisableRedis();
        h.Controller.StageRedisUrl("redis://typed-synthetic.invalid");
        Assert.Empty(h.Controller.State.DirtyKeys);
        Assert.Null(h.Controller.State.Error);
        await h.Controller.SaveAsync();
        Assert.Equal(0, Writes(h.Kit));
    }
}
