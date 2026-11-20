using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

public sealed class AdminPlaybackNodesTests
{
    [Theory]
    [InlineData("playback.transcoding")]
    [InlineData("playback.preview_images")]
    [InlineData("nodes.routing")]
    public void PlaybackDisclosuresRememberAnExplicitChoiceWithoutReadingPlayerSettings(string section)
    {
        var preferences = new LoggingDisclosurePreferences(); Assert.False(preferences.Read(section)); preferences.Write(section, true); Assert.True(preferences.Read(section)); preferences.Write(section, false); Assert.False(preferences.Read(section));
    }
    private static AdminPlaybackNode Node(string type = "transcode") => new() { Id = "synthetic-node", Name = "Node", Type = type, Url = "https://worker.invalid", ConfigETag = "\"one\"", Enabled = true, Healthy = true, HwAccelOverride = "qsv", HwDeviceOverride = "/dev/dri/renderD128" };
    private static AdminPlaybackNodeEditor Editor(string type, bool existing) => new(type, existing ? Node(type) : null) { Name = "Node", Url = "https://worker.invalid" };

    [Fact]
    public void ProxyEditClearsPublicAddressAndDoesNotSendHiddenAccelerationOrSourceAccess()
    {
        var values = Editor("proxy", true).Capture(true);
        Assert.True(values.ContainsKey("public_url")); Assert.Null(values["public_url"]);
        Assert.DoesNotContain("hw_accel_override", values.Keys); Assert.DoesNotContain("hw_device_override", values.Keys); Assert.DoesNotContain("source_access", values.Keys);
        Assert.DoesNotContain("type", values.Keys);
    }
    [Fact]
    public void NewTranscodeSendsNeitherOverridesNorUnsupportedSourceAccess()
    {
        var editor = Editor("transcode", false); editor.SourceAccess = "core";
        var values = editor.Capture(false);
        Assert.Equal("transcode", values["type"]); Assert.DoesNotContain("source_access", values.Keys);
        Assert.DoesNotContain("hw_accel_override", values.Keys); Assert.DoesNotContain("hw_device_override", values.Keys);
        Assert.DoesNotContain("public_url", values.Keys);
    }
    [Fact]
    public void ExplicitInheritUsesNullAndFractionalMbpsConvertsToKbps()
    {
        var editor = Editor("transcode", true); editor.HwAccelOverride = "inherit"; editor.HwDeviceOverride = ""; editor.MaxBandwidthMbps = "12.345";
        var values = editor.Capture(true);
        Assert.Null(values["hw_accel_override"]); Assert.Null(values["hw_device_override"]); Assert.Equal(12345, values["max_bandwidth_kbps"]);
        Assert.Equal("local", values["source_access"]); Assert.Equal(0, values["max_jobs"]);
    }
    [Theory]
    [InlineData("https://name:password@worker.invalid")]
    [InlineData("https://worker.invalid?token=synthetic")]
    [InlineData("file:///etc/passwd")]
    [InlineData("relative/node")]
    public void NodeAddressCannotEmbedCredentialsOrQueryData(string url)
    {
        var editor = Editor("proxy", false); editor.Url = url; Assert.Throws<ArgumentException>(() => editor.Capture(true));
    }
    [Fact]
    public void EditorDoesNotRevealLegacyCredentialBearingAddresses()
    {
        var target = Node("proxy"); target.Url = "https://user:synthetic-secret@worker.invalid"; target.PublicUrl = "https://proxy.invalid?token=synthetic";
        var editor = new AdminPlaybackNodeEditor("proxy", target); Assert.Empty(editor.Url); Assert.Empty(editor.PublicUrl);
    }
    [Fact]
    public void DeviceTogglePreservesUnknownPinnedDevicesAndDetectionOrder()
    {
        Assert.Equal("/dev/dri/renderD128,/dev/dri/missing", AdminPlaybackSettingsRules.ToggleDevice("/dev/dri/missing", "/dev/dri/renderD128", ["/dev/dri/renderD128", "/dev/dri/renderD129"]));
        Assert.Equal("/dev/dri/missing", AdminPlaybackSettingsRules.ToggleDevice("/dev/dri/renderD128,/dev/dri/missing", "/dev/dri/renderD128", ["/dev/dri/renderD128"]));
    }
    [Fact]
    public void OfficialPathDefaultsAndThresholdDependencyMatchServer()
    {
        Assert.Equal("/tmp/silo-transcode", AdminPlaybackSettingsRules.Get(new Dictionary<string, string>(), "playback.transcode_dir"));
        var errors = AdminPlaybackSettingsRules.Validate(new Dictionary<string, string> { ["playback.watched_threshold"] = "40", ["playback.min_resume_threshold"] = "50" }, ["playback.watched_threshold"]);
        Assert.Contains("playback.min_resume_threshold", errors.Keys);
    }
    [Theory]
    [InlineData(161)] [InlineData(641)] [InlineData(100)]
    public void InvalidPreviewWidthIsRejected(int width) => Assert.Contains("playback.preview_image_width", AdminPlaybackSettingsRules.Validate(new Dictionary<string, string> { ["playback.preview_image_width"] = width.ToString() }, ["playback.preview_image_width"]).Keys);

    [Fact]
    public async Task WrongNodeWriteAcknowledgementIsRejected()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) =>
        {
            var response = AdminMonitorKit.Json("{\"id\":\"other\",\"config_etag\":\"\\\"two\\\"\"}"); response.Headers.ETag = new("\"two\""); return Task.FromResult(response);
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminPlaybackAdministrationApi(kit.Client).UpdateNodeAsync(owner, Node(), new Dictionary<string, object?> { ["enabled"] = false }, default));
        Assert.Single(kit.Http.Requests);
    }
    [Fact]
    public async Task RepeatedNodeCursorIsRejectedBeforeWalkingAnUnboundedCollection()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[],\"page\":{\"next_cursor\":\"same\"}}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new AdminPlaybackAdministrationApi(kit.Client).ListNodesAsync(owner, default)); Assert.Equal(2, kit.Http.Requests.Count);
    }
    [Fact]
    public async Task MalformedOptionalHardwareDoesNotBecomeVerifiedOrCrashThePage()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.AbsolutePath.EndsWith("/hw-accel") ? "{\"resolved\":null,\"render_device_details\":[null]}" : "{\"items\":[]}"));
        var support = await new AdminPlaybackAdministrationApi(kit.Client).SupportAsync(owner, default); Assert.Null(support.Hardware);
    }
    [Fact]
    public async Task MarkerTaskRefreshReadsOnlyTheBoundedTaskCollection()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[{\"key\":\"sync_markers\",\"state\":\"running\"},{\"key\":\"other_task\",\"state\":\"idle\"}]}"));
        var tasks = await new AdminPlaybackAdministrationApi(kit.Client).ListMarkerTasksAsync(owner, default);
        Assert.Equal("sync_markers", Assert.Single(tasks).Key); Assert.Equal(new[] { "GET /api/v2/admin/tasks" }, kit.Http.Requests);
    }
    [Fact]
    public async Task LateKeyResponseCannotRevealIntoRetiredAuthority()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        kit.Http.Respond = (_, _) => { started.SetResult(); return response.Task; };
        var revealed = false; var operation = new AdminPlaybackAdministrationApi(kit.Client).IssueKeyAsync(owner, Node(), _ => revealed = true, default);
        await started.Task; kit.SetRole("user"); response.SetResult(AdminMonitorKit.Json("{\"node_key\":\"synthetic-once\"}"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation); Assert.False(revealed); Assert.Single(kit.Http.Requests);
    }
    [Fact]
    public async Task UnauthorizedNonIdempotentWriteNeverRefreshesOrReplays()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var refreshes = 0; kit.Client.SetTokenRefresher(_ => { refreshes++; return Task.FromResult(true); });
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"type\":\"authentication_required\",\"status\":401}", HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ApiException>(() => new AdminPlaybackAdministrationApi(kit.Client).IssueKeyAsync(owner, Node(), _ => throw new InvalidOperationException(), default));
        Assert.Equal(0, refreshes); Assert.Single(kit.Http.Requests);
    }
    [Fact]
    public async Task UnknownNodeWriteBlocksEveryReplayUntilReload()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method != HttpMethod.Get) throw new HttpRequestException("synthetic lost acknowledgement");
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("capabilities") ? AdminMonitorKit.Json("{\"allowed\":true,\"state\":\"available\",\"drain\":true,\"node_keys\":true,\"source_access\":true}") : AdminMonitorKit.Json("{\"items\":[{\"id\":\"synthetic-node\",\"name\":\"Node\",\"type\":\"transcode\",\"url\":\"https://worker.invalid\",\"config_etag\":\"\\\"one\\\"\"}]}"));
        };
        using var controller = new AdminPlaybackNodesController(new(kit.Client), authority); await controller.OpenAsync();
        var node = Assert.Single(controller.State.Window.Items);
        Assert.False(await controller.UpdateAsync(node, new Dictionary<string, object?> { ["enabled"] = false }));
        Assert.True(controller.State.ReviewRequired);
        Assert.False(await controller.UpdateAsync(node, new Dictionary<string, object?> { ["enabled"] = false }));
        Assert.Single(kit.Http.Requests.Where(request => request.StartsWith("PUT")));
        await controller.ReloadAsync(); Assert.False(controller.State.ReviewRequired);
        controller.Suspend(); Assert.Empty(controller.State.Window.Items);
    }

    /// <summary>The production pairing: the shared client with its own timeout, and the client without one that Backups use.</summary>
    private sealed class TwoTransports : IDisposable
    {
        internal readonly AdminMonitorHttp Shared = new(), Unbounded = new();
        internal readonly SiloApiClient Client; internal readonly AuthService Auth;
        private readonly HttpClient _shared, _unbounded;
        internal TwoTransports(TimeSpan sharedTimeout)
        {
            _shared = new HttpClient(Shared) { Timeout = sharedTimeout }; _unbounded = new HttpClient(Unbounded) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            Client = new SiloApiClient(_shared, _unbounded); Client.SetBaseUrl("https://silo.test");
            Auth = new AuthService(Client, new AuthApi(Client)); Auth.SetTokens("synthetic-access", "synthetic-refresh", 86400);
            Auth.SetCurrentUser(new SiloPlayer.Core.Models.Auth.UserInfo { Id = "1", Username = "synthetic", Role = "admin" });
            Auth.SelectProfile("p1", null, new SiloPlayer.Core.Models.Auth.Profile { Id = "p1", Name = "Primary", IsPrimary = true });
        }
        public void Dispose() { Auth.Dispose(); _shared.Dispose(); _unbounded.Dispose(); }
    }
    // Re-probe answers only after probing. The shared client's own timeout (100 seconds in production) must not end it before its 180-second deadline.
    [Fact]
    public async Task ReprobeOutlastsTheSharedClientTimeout()
    {
        using var kit = new TwoTransports(TimeSpan.FromMilliseconds(100)); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> slow = async (_, ct) =>
        {
            await Task.Delay(600, ct); return AdminMonitorKit.Json("{\"node_id\":\"synthetic-node\",\"status\":\"ok\",\"capabilities_refreshed\":true}");
        };
        kit.Shared.Respond = slow; kit.Unbounded.Respond = slow;
        var result = await new AdminPlaybackAdministrationApi(kit.Client).CommandAsync(owner, Node(), "reprobe", default);
        Assert.Equal("ok", result.Status); Assert.Empty(kit.Shared.Requests);
        Assert.Equal("POST /api/v2/admin/nodes/synthetic-node/reprobe", Assert.Single(kit.Unbounded.Requests));
    }
    [Fact]
    public async Task OnlyReprobeLeavesTheSharedClient()
    {
        using var kit = new TwoTransports(TimeSpan.FromSeconds(100)); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Shared.Respond = (request, _) => Task.FromResult(AdminMonitorKit.Json(request.RequestUri!.AbsolutePath.EndsWith("/check")
            ? "{\"healthy\":true,\"active_jobs\":0,\"egress_kbps\":0,\"health_persisted\":true}"
            : "{\"node_id\":\"synthetic-node\",\"drain_requested\":true,\"drain_state\":\"draining\",\"active_jobs\":0}"));
        var api = new AdminPlaybackAdministrationApi(kit.Client);
        await api.CommandAsync(owner, Node(), "check", default); await api.CommandAsync(owner, Node(), "drain", default);
        Assert.Equal(2, kit.Shared.Requests.Count); Assert.Empty(kit.Unbounded.Requests);
    }
}
