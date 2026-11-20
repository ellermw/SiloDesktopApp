using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Regression tests for the second independent review of Manage > Playback (Nodes and Playback settings), official silo-server 217ff25.</summary>
public sealed class AdminPlaybackSecondReviewTests
{
    private static AdminPlaybackNode Node(string type = "transcode") => new() { Id = "synthetic-node", Name = "Node", Type = type, Url = "https://worker.invalid", ConfigETag = "\"one\"", Enabled = true, Healthy = true };
    private static AdminPlaybackNodeEditor Edited(string type, AdminPlaybackNode? target) => new(type, target) { Name = "Node" };

    // D1: the scaled egress field must report a range error, never throw OverflowException into the async-void click handler.
    [Theory]
    [InlineData("99999999999999999999999999")]
    [InlineData("79228162514264337593543950335")]
    [InlineData("2147483.648")]
    [InlineData("-1")]
    [InlineData("1e3")]
    public void EgressLimitOutsideTheRepresentableRangeIsAValidationError(string text)
    {
        var editor = Edited("proxy", null); editor.Url = "https://proxy.invalid"; editor.MaxBandwidthMbps = text;
        var failure = Record.Exception(() => editor.Capture(true));
        Assert.IsType<ArgumentException>(failure);
    }
    [Theory]
    [InlineData("2147483.647", 2147483647)]
    [InlineData(" 1.5 ", 1500)]
    [InlineData("0", 0)]
    public void EgressLimitAtTheEdgeOfTheRangeIsKbpsExact(string text, int kbps)
    {
        var editor = Edited("proxy", null); editor.Url = "https://proxy.invalid"; editor.MaxBandwidthMbps = text;
        Assert.Equal(kbps, editor.Capture(true)["max_bandwidth_kbps"]);
    }
    [Theory]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999")]
    public void CapacityBeyondTheServersIntegerIsAValidationError(string text)
    {
        var editor = Edited("transcode", null); editor.Url = "https://worker.invalid"; editor.MaxJobs = text;
        Assert.IsType<ArgumentException>(Record.Exception(() => editor.Capture(false)));
    }

    // D2: a node override may only name a backend in the server's hwAccelOverrideValues; VideoToolbox is cluster-wide only.
    [Fact]
    public void NodeAccelerationOverrideListMatchesTheServersEnumWithoutVideoToolbox()
    {
        Assert.Equal(new[] { "inherit", "auto", "qsv", "vaapi", "nvenc", "none" }, AdminPlaybackNodeEditor.HwAccelOverrideValues);
        var cluster = AdminPlaybackSettingsRules.Fields.Single(field => field.Key == "playback.hw_accel").Options!;
        Assert.Contains("videotoolbox", cluster);
        Assert.Equal(cluster.Where(value => value != "videotoolbox"), AdminPlaybackNodeEditor.HwAccelOverrideValues.Skip(1));
    }
    [Theory]
    [InlineData("videotoolbox", false)]
    [InlineData("qsv", true)]
    [InlineData("none", true)]
    public void NodeEditorRefusesAnOverrideTheServerWouldRejectBeforeSending(string value, bool accepted)
    {
        var editor = Edited("transcode", Node()); editor.Url = "https://worker.invalid"; editor.HwAccelOverride = value;
        if (accepted) Assert.Equal(value, editor.Capture(true)["hw_accel_override"]);
        else Assert.IsType<ArgumentException>(Record.Exception(() => editor.Capture(true)));
    }

    // D3: an untouched editor must not write blank over a stored address the form could not show.
    [Fact]
    public void UntouchedEditorLeavesAStoredAddressItCannotShowAlone()
    {
        var target = Node("proxy"); target.Url = "https://user:synthetic-secret@worker.invalid"; target.PublicUrl = "https://proxy.invalid?token=synthetic";
        var values = new AdminPlaybackNodeEditor("proxy", target) { Name = "Renamed" }.Capture(true);
        Assert.DoesNotContain("url", values.Keys); Assert.DoesNotContain("public_url", values.Keys);
        Assert.Equal("Renamed", values["name"]);
    }
    [Fact]
    public void EditingTheWithheldAddressIsAnExplicitChoice()
    {
        var target = Node("proxy"); target.Url = "worker.invalid"; target.PublicUrl = "proxy.invalid/path?x=1";
        var editor = new AdminPlaybackNodeEditor("proxy", target) { Name = "Node" }; editor.PublicUrl = "https://proxy.invalid"; editor.Url = "https://worker.invalid";
        var values = editor.Capture(true); Assert.Equal("https://worker.invalid", values["url"]); Assert.Equal("https://proxy.invalid", values["public_url"]);
        // Typing and then clearing the public address is an explicit request to clear it.
        var cleared = new AdminPlaybackNodeEditor("proxy", target) { Name = "Node" }; cleared.PublicUrl = "x"; cleared.PublicUrl = ""; cleared.Url = "https://worker.invalid";
        Assert.Null(cleared.Capture(true)["public_url"]);
    }
    [Fact]
    public void AnInvalidEditedAddressIsStillRefused()
    {
        var target = Node("proxy"); target.PublicUrl = "https://proxy.invalid?token=synthetic";
        var editor = new AdminPlaybackNodeEditor("proxy", target) { Name = "Node" }; editor.PublicUrl = "https://proxy.invalid?other=1";
        Assert.IsType<ArgumentException>(Record.Exception(() => editor.Capture(true)));
    }
    [Fact]
    public void ADisplayableStoredPublicAddressIsStillSentBackExactly()
    {
        var target = Node("proxy"); target.PublicUrl = "https://proxy.invalid";
        var values = new AdminPlaybackNodeEditor("proxy", target) { Name = "Node" }.Capture(true);
        Assert.Equal("https://proxy.invalid", values["public_url"]); Assert.Equal("https://worker.invalid", values["url"]);
    }

    // D5: a draft whose row is hidden must not keep blocking Save.
    [Theory]
    [InlineData("enable_transcode_throttle", "false", "transcode_throttle_seconds", "5")]
    [InlineData("markers.mode", "online", "markers.detection_workers", "0")]
    [InlineData("markers.mode", "off", "markers.detection_workers", "999")]
    public void RulesNameInvalidDraftsOfHiddenRows(string conditionKey, string conditionValue, string key, string draft)
    {
        var values = new Dictionary<string, string> { [conditionKey] = conditionValue, [key] = draft };
        Assert.Equal(new[] { key }, AdminPlaybackSettingsRules.HiddenInvalidKeys(values, [conditionKey, key], true, true));
        var visible = new Dictionary<string, string>(values) { [conditionKey] = conditionKey == "markers.mode" ? "both" : "true" };
        Assert.Empty(AdminPlaybackSettingsRules.HiddenInvalidKeys(visible, [conditionKey, key], true, true));
        Assert.Contains(key, AdminPlaybackSettingsRules.Validate(visible, [key]).Keys);
    }
    private sealed class SettingsHarness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority; internal readonly AdminServerSettingsStore Store; internal readonly AdminPlaybackSettingsController Controller;
        internal SettingsHarness()
        {
            Authority = new(Kit.Client, Kit.Auth); var api = new AdminServerSettingsApi(Kit.Client);
            Store = new(api, Authority, AdminPlaybackSettingsRules.Keys, []);
            Controller = new(api, Store, Authority, false);
            Kit.Http.Respond = (request, _) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                var response = AdminMonitorKit.Json(path.EndsWith("effective") ? "{\"markers.mode\":\"both\",\"playback.preview_image_width\":\"300\"}" : path.EndsWith("restart-keys") ? "{\"keys\":[],\"prefixes\":[]}" : "{\"configured\":[],\"managed_by_env\":[]}");
                response.Headers.ETag = new("\"settings-1\""); return Task.FromResult(response);
            };
        }
        public void Dispose() { Controller.Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }
    [Fact]
    public async Task SwitchingThrottlingOffDropsAnInvalidBufferAheadDraftSoSaveIsNotBlocked()
    {
        using var h = new SettingsHarness(); await h.Controller.OpenAsync();
        h.Controller.Set("transcode_throttle_seconds", "5");
        Assert.Contains("transcode_throttle_seconds", h.Controller.State.Errors.Keys);
        h.Controller.Set("enable_transcode_throttle", "false");
        Assert.Empty(h.Controller.State.Errors); Assert.DoesNotContain("transcode_throttle_seconds", h.Controller.State.DirtyKeys);
        Assert.Equal(new[] { "enable_transcode_throttle" }, h.Controller.State.DirtyKeys);
        // Switching it back on shows the saved value again, not the abandoned draft.
        h.Controller.Set("enable_transcode_throttle", "true");
        Assert.Equal("", h.Controller.State.Values["transcode_throttle_seconds"]); Assert.Empty(h.Controller.State.Errors);
    }
    [Fact]
    public async Task ChangingTheMarkerModeDropsAnInvalidWorkerCountButKeepsAValidHiddenDraft()
    {
        using var h = new SettingsHarness(); await h.Controller.OpenAsync();
        h.Controller.Set("markers.detection_workers", "0"); Assert.Contains("markers.detection_workers", h.Controller.State.Errors.Keys);
        h.Controller.Set("markers.mode", "online");
        Assert.Empty(h.Controller.State.Errors); Assert.DoesNotContain("markers.detection_workers", h.Controller.State.DirtyKeys);
        h.Controller.Set("markers.mode", "both"); h.Controller.Set("markers.detection_workers", "4"); h.Controller.Set("markers.mode", "online");
        Assert.Contains("markers.detection_workers", h.Controller.State.DirtyKeys); Assert.Equal("4", h.Controller.State.Values["markers.detection_workers"]);
    }
    [Fact]
    public async Task CapabilityHiddenPreviewRowsDoNotKeepAnInvalidDraftEither()
    {
        using var h = new SettingsHarness(); await h.Controller.OpenAsync();
        h.Controller.SetVisibilityContext(true, true);
        h.Controller.Set("playback.preview_image_width", "101"); Assert.Contains("playback.preview_image_width", h.Controller.State.Errors.Keys);
        h.Controller.SetVisibilityContext(false, true);
        Assert.Empty(h.Controller.State.Errors); Assert.Empty(h.Controller.State.DirtyKeys); Assert.Equal("300", h.Controller.State.Values["playback.preview_image_width"]);
    }

    // D7: like the official react-query page, a failed background refresh keeps the rows and keeps polling.
    private sealed class NodesHarness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create(); internal readonly AdminMonitorAuthority Authority; internal readonly AdminPlaybackNodesController Controller;
        internal bool FailList; internal Func<HttpRequestMessage, HttpResponseMessage>? Write;
        internal NodesHarness()
        {
            Authority = new(Kit.Client, Kit.Auth); Controller = new(new(Kit.Client), Authority);
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get) return Task.FromResult(Write!(request));
                if (request.RequestUri!.AbsolutePath.EndsWith("capabilities")) return Task.FromResult(AdminMonitorKit.Json("{\"allowed\":true,\"state\":\"available\"}"));
                if (FailList) throw new HttpRequestException("synthetic timeout");
                return Task.FromResult(AdminMonitorKit.Json("{\"items\":[{\"id\":\"synthetic-node\",\"name\":\"Node\",\"type\":\"proxy\",\"url\":\"https://proxy.invalid\",\"enabled\":true,\"healthy\":true,\"config_etag\":\"\\\"one\\\"\"}]}"));
            };
        }
        public void Dispose() { Controller.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }
    [Fact]
    public async Task BackgroundRefreshFailureKeepsTheRowsEditableAndPollingThenRecovers()
    {
        using var h = new NodesHarness(); await h.Controller.OpenAsync(); var window = h.Controller.State.Window; Assert.Single(window.Items);
        h.FailList = true; await h.Controller.ReloadAsync();
        Assert.False(h.Controller.State.ReviewRequired); Assert.NotNull(h.Controller.State.Error); Assert.True(h.Controller.State.Loaded); Assert.False(h.Controller.State.Loading);
        Assert.Same(window, h.Controller.State.Window);
        h.FailList = false; await h.Controller.ReloadAsync();
        Assert.Null(h.Controller.State.Error); Assert.False(h.Controller.State.ReviewRequired);
    }
    [Fact]
    public async Task FirstLoadFailureStillBlocksControlsUntilReload()
    {
        using var h = new NodesHarness(); h.FailList = true; await h.Controller.OpenAsync();
        Assert.True(h.Controller.State.ReviewRequired); Assert.False(h.Controller.State.Loaded); Assert.NotNull(h.Controller.State.Error);
        h.FailList = false; await h.Controller.ReloadAsync(); Assert.False(h.Controller.State.ReviewRequired); Assert.True(h.Controller.State.Loaded);
    }
    [Fact]
    public async Task RefreshFailureAfterAnUnknownWriteOutcomeKeepsWritesBlocked()
    {
        using var h = new NodesHarness(); h.Write = _ => throw new HttpRequestException("synthetic lost acknowledgement");
        await h.Controller.OpenAsync(); var node = Assert.Single(h.Controller.State.Window.Items);
        Assert.False(await h.Controller.UpdateAsync(node, new Dictionary<string, object?> { ["enabled"] = false })); Assert.True(h.Controller.State.ReviewRequired);
        h.FailList = true; await h.Controller.ReloadAsync();
        Assert.True(h.Controller.State.ReviewRequired);
        h.FailList = false; await h.Controller.ReloadAsync(); Assert.False(h.Controller.State.ReviewRequired);
    }

    // Reviewer's gap: the 412 path through the enabled switch is a conflict that needs a reload, never a replay or a lie about the saved value.
    [Fact]
    public async Task PreconditionFailedThroughTheEnabledSwitchBlocksReplayUntilReload()
    {
        using var h = new NodesHarness();
        h.Write = _ => AdminMonitorKit.Json("{\"type\":\"precondition_failed\",\"status\":412}", HttpStatusCode.PreconditionFailed);
        await h.Controller.OpenAsync(); var window = h.Controller.State.Window; var node = Assert.Single(window.Items);
        Assert.False(await h.Controller.UpdateAsync(node, new Dictionary<string, object?> { ["enabled"] = false }));
        Assert.True(h.Controller.State.ReviewRequired); Assert.Contains("changed on the server", h.Controller.State.Error);
        Assert.Same(window, h.Controller.State.Window); Assert.True(h.Controller.State.Window.Items[0].Enabled);
        Assert.False(await h.Controller.UpdateAsync(node, new Dictionary<string, object?> { ["enabled"] = false }));
        Assert.Single(h.Kit.Http.Requests.Where(request => request.StartsWith("PUT")));
        await h.Controller.ReloadAsync(); Assert.False(h.Controller.State.ReviewRequired);
    }
}
