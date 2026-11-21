using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Official 8ae4f4f5a: environment-managed (#4499) and deployment-managed (#4654) nodes, the server's reasons, plain text and the card presentation. Synthetic values only.</summary>
public sealed class AdminNodesParityRefreshTests
{
    private static AdminPlaybackNode Node(string type = "transcode", string? managed = null) => new()
    {
        Id = "synthetic-node", Name = "Node", Type = type, Url = "https://worker.invalid", ConfigETag = "\"one\"", Enabled = true, Healthy = true, KeyIssued = type == "transcode",
        EnvManaged = managed == null ? null : managed.Split(',', StringSplitOptions.RemoveEmptyEntries),
    };
    private static string Row(string extra = "") => "{\"id\":\"synthetic-node\",\"name\":\"Node\",\"type\":\"transcode\",\"url\":\"https://worker.invalid\",\"config_etag\":\"\\\"one\\\"\"" + extra + "}";

    // ---- contract ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ManagedFieldsAreReadWhenPresentAndMeanNotManagedWhenAbsent()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + Row(",\"deployment_managed\":true,\"env_managed\":[\"name\",\"max_jobs\"]") + "," + Row(",\"id\":\"older\"").Replace("\"id\":\"synthetic-node\",", "") + "]}"));
        var window = await new AdminPlaybackAdministrationApi(kit.Client).ListNodesAsync(owner, default);
        var managed = window.Items[0]; var older = window.Items[1];
        Assert.True(managed.IsDeploymentManaged); Assert.True(managed.IsEnvManaged("name")); Assert.True(managed.IsEnvManaged("max_jobs")); Assert.False(managed.IsEnvManaged("url"));
        Assert.False(older.IsDeploymentManaged); Assert.False(older.IsEnvManaged("name")); Assert.Null(older.DeploymentManaged); Assert.Null(older.EnvManaged);
    }
    [Fact]
    public async Task EmptyOrNullEnvironmentDeclarationIsNotManaged()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + Row(",\"deployment_managed\":false,\"env_managed\":[]") + "]}"));
        var node = Assert.Single((await new AdminPlaybackAdministrationApi(kit.Client).ListNodesAsync(owner, default)).Items);
        Assert.False(node.IsDeploymentManaged); Assert.False(node.IsEnvManaged("name"));
    }
    [Theory]
    [InlineData(",\"deployment_managed\":\"yes\"")]
    [InlineData(",\"deployment_managed\":1")]
    [InlineData(",\"env_managed\":\"name\"")]
    [InlineData(",\"env_managed\":[1]")]
    [InlineData(",\"env_managed\":[null]")]
    [InlineData(",\"env_managed\":[\"\"]")]
    [InlineData(",\"env_managed\":[\"has\\ncontrol\"]")]
    [InlineData(",\"env_managed\":[\"a\",\"b\",\"c\",\"d\",\"e\",\"f\",\"g\",\"h\",\"i\",\"j\",\"k\",\"l\",\"m\",\"n\",\"o\",\"p\",\"q\"]")]
    public async Task MalformedManagedFieldsRefuseTheWholePage(string extra)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + Row(extra) + "]}"));
        await Assert.ThrowsAnyAsync<Exception>(() => new AdminPlaybackAdministrationApi(kit.Client).ListNodesAsync(owner, default));
    }
    [Fact]
    public async Task CapabilitiesCarryTheNewFlagsAndDefaultToFalseOnAnOlderServer()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"allowed\":true,\"state\":\"available\",\"node_keys\":true,\"env_declarations\":true,\"deployment_nodes\":true}"));
        var current = await new AdminPlaybackAdministrationApi(kit.Client).CapabilitiesAsync(owner, default); Assert.True(current.EnvDeclarations && current.DeploymentNodes);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"allowed\":true,\"state\":\"available\",\"node_keys\":true}"));
        var older = await new AdminPlaybackAdministrationApi(kit.Client).CapabilitiesAsync(owner, default); Assert.False(older.EnvDeclarations || older.DeploymentNodes); Assert.True(older.NodeKeys);
    }
    [Fact]
    public async Task ProbeReasonAndPciAddressAreReadUnderTheOfficialNames()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + Row(",\"capabilities\":{\"resolved\":\"qsv\",\"detected_backends\":[{\"backend\":\"qsv\",\"verified\":false,\"reason\":\"driver missing\"}],\"render_device_details\":[{\"path\":\"/dev/dri/renderD128\",\"pci_address\":\"0000:03:00.0\",\"description\":\"Synthetic GPU\"}]}") + "]}"));
        var node = Assert.Single((await new AdminPlaybackAdministrationApi(kit.Client).ListNodesAsync(owner, default)).Items);
        Assert.Equal("driver missing", node.Capabilities!.DetectedBackends![0].Reason); Assert.Equal("0000:03:00.0", node.Capabilities.RenderDeviceDetails![0].PciAddress);
    }

    // ---- editor: a managed field is never part of a write ---------------------------------------------------------------

    private static readonly string[] TranscodeFields = ["name", "url", "group", "max_jobs", "max_bandwidth_kbps", "hw_accel_override", "hw_device_override", "source_access"];
    [Theory]
    [InlineData("name")] [InlineData("url")] [InlineData("group")] [InlineData("max_jobs")] [InlineData("max_bandwidth_kbps")] [InlineData("hw_accel_override")] [InlineData("hw_device_override")] [InlineData("source_access")]
    public void ALockedTranscodeFieldIsNeverSentButTheRestAre(string field)
    {
        var editor = new AdminPlaybackNodeEditor("transcode", Node("transcode", field)) { Name = "Changed", Group = "other", MaxJobs = "9", HwAccelOverride = "qsv", HwDeviceOverride = "/dev/dri/renderD129" };
        editor.Url = "https://changed.invalid"; editor.SourceAccess = "core";
        var body = editor.Capture(true);
        Assert.DoesNotContain(field, body.Keys);
        foreach (var other in TranscodeFields.Where(name => name != field)) Assert.Contains(other, body.Keys);
        Assert.True(editor.IsLocked(field)); Assert.False(editor.IsLocked(TranscodeFields.First(name => name != field)));
    }
    [Theory]
    [InlineData("public_url")] [InlineData("group")] [InlineData("max_jobs")] [InlineData("max_bandwidth_kbps")]
    public void ALockedProxyFieldIsNeverSent(string field)
    {
        var target = Node("proxy", field); target.PublicUrl = "https://public.invalid";
        var editor = new AdminPlaybackNodeEditor("proxy", target) { PublicUrl = "https://other.invalid", Group = "x", MaxJobs = "3", MaxBandwidthMbps = "5" };
        Assert.DoesNotContain(field, editor.Capture(true).Keys);
    }
    [Fact]
    public void EveryFieldLockedLeavesNothingToSend()
    {
        var editor = new AdminPlaybackNodeEditor("transcode", Node("transcode", string.Join(',', TranscodeFields)));
        Assert.Empty(editor.Capture(true));
    }
    [Fact]
    public void AStoredValueTheFormWouldRefuseDoesNotBlockSavingWhenTheNodeOwnsIt()
    {
        var target = Node("transcode", "url,name,max_jobs"); target.Url = "https://user:secret@worker.invalid"; target.Name = "";
        var editor = new AdminPlaybackNodeEditor("transcode", target) { MaxJobs = "-1" };
        var body = editor.Capture(true); Assert.DoesNotContain("url", body.Keys); Assert.DoesNotContain("name", body.Keys); Assert.DoesNotContain("max_jobs", body.Keys);
        Assert.Contains("group", body.Keys);
    }
    [Fact]
    public void AnUnmanagedNodeStillSendsTheWholeForm()
    {
        var body = new AdminPlaybackNodeEditor("transcode", Node("transcode")) { Name = "Node", Url = "https://worker.invalid" }.Capture(true);
        Assert.All(TranscodeFields, field => Assert.Contains(field, body.Keys));
    }

    // ---- API: nothing the page does not offer is sent ------------------------------------------------------------------

    [Fact]
    public async Task UpdateNeverSendsAFieldTheNodeDeclares()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        await Assert.ThrowsAsync<AdminPlaybackNodeActionNotOfferedException>(() => new AdminPlaybackAdministrationApi(kit.Client).UpdateNodeAsync(owner, Node("transcode", "group"), new Dictionary<string, object?> { ["group"] = "x" }, default));
        Assert.Empty(kit.Http.Requests);
        // The enabled switch is not a declarable field, so it stays available on a managed node.
        kit.Http.Respond = (_, _) => { var response = AdminMonitorKit.Json(Row()); response.Headers.ETag = new("\"one\""); return Task.FromResult(response); };
        await new AdminPlaybackAdministrationApi(kit.Client).UpdateNodeAsync(owner, Node("transcode", "name,group"), new Dictionary<string, object?> { ["enabled"] = false }, default); Assert.Single(kit.Http.Requests);
    }
    [Fact]
    public async Task DeleteAndKeyAreNeverSentForADeploymentManagedNode()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var owner = authority.Capture();
        var node = Node(); node.DeploymentManaged = true; var api = new AdminPlaybackAdministrationApi(kit.Client);
        await Assert.ThrowsAsync<AdminPlaybackNodeActionNotOfferedException>(() => api.DeleteNodeAsync(owner, node, default));
        await Assert.ThrowsAsync<AdminPlaybackNodeActionNotOfferedException>(() => api.IssueKeyAsync(owner, node, _ => throw new InvalidOperationException(), default));
        Assert.Empty(kit.Http.Requests);
    }

    // ---- controller -------------------------------------------------------------------------------------------------------

    private static Task<HttpResponseMessage> Reads(HttpRequestMessage request, string row) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("capabilities")
        ? AdminMonitorKit.Json("{\"allowed\":true,\"state\":\"available\",\"node_keys\":true,\"drain\":true}") : AdminMonitorKit.Json("{\"items\":[" + row + "]}"));

    [Fact]
    public async Task DeletingADeploymentManagedNodeShowsTheOfficialNoteAndSendsNothing()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Reads(request, Row(",\"deployment_managed\":true"));
        using var controller = new AdminPlaybackNodesController(new(kit.Client), authority); await controller.OpenAsync();
        var node = Assert.Single(controller.State.Window.Items); var before = kit.Http.Requests.Count;
        Assert.False(await controller.DeleteAsync(node));
        Assert.Equal(before, kit.Http.Requests.Count); Assert.Equal("Set by the server\u2019s deployment configuration. Remove it there and restart the API servers to delete it here.", controller.State.Feedback);
        Assert.False(controller.State.Busy); Assert.False(controller.State.ReviewRequired); Assert.Null(controller.State.Error);
        Assert.False(await controller.IssueKeyAsync(node, _ => throw new InvalidOperationException())); Assert.Equal(before, kit.Http.Requests.Count);
    }
    [Fact]
    public async Task EditingADeclaredFieldIsNotSentEvenIfACallerTriesIt()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => Reads(request, Row(",\"env_managed\":[\"group\"]"));
        using var controller = new AdminPlaybackNodesController(new(kit.Client), authority); await controller.OpenAsync();
        var node = Assert.Single(controller.State.Window.Items); var before = kit.Http.Requests.Count;
        Assert.False(await controller.UpdateAsync(node, new Dictionary<string, object?> { ["group"] = "x" }));
        Assert.Equal(before, kit.Http.Requests.Count); Assert.False(controller.State.ReviewRequired);
    }
    [Fact]
    public async Task ADefiniteRefusalShowsTheServersReasonKeepsWritesOpenAndIsNeverReplayed()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var deletes = 0;
        kit.Http.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Delete) { deletes++; return Task.FromResult(AdminMonitorKit.Json("{\"type\":\"https://silo.invalid/problems/node_deployment_managed\",\"status\":409,\"title\":\"Managed\",\"detail\":\"This node is set by the deployment.\\u202e hidden\\n\"}", HttpStatusCode.Conflict)); }
            return Reads(request, Row());
        };
        using var controller = new AdminPlaybackNodesController(new(kit.Client), authority); await controller.OpenAsync();
        Assert.False(await controller.DeleteAsync(Assert.Single(controller.State.Window.Items)));
        Assert.Equal(1, deletes); Assert.Equal("This node is set by the deployment. hidden", controller.State.Error); Assert.False(controller.State.ReviewRequired); Assert.False(controller.State.Busy);
    }
    [Fact]
    public async Task AManagedFieldRefusalNamesTheNodesEnvironment()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => request.Method == HttpMethod.Put
            ? Task.FromResult(AdminMonitorKit.Json("{\"type\":\"https://silo.invalid/problems/validation_failed\",\"status\":422,\"title\":\"Invalid\",\"detail\":\"Set by the node\\u2019s environment. Change it there and restart the node.\",\"errors\":[{\"location\":\"body.group\",\"code\":\"managed_by_environment\",\"detail\":\"Set by the node\\u2019s environment. Change it there and restart the node.\"}]}", HttpStatusCode.UnprocessableEntity))
            : Reads(request, Row());
        using var controller = new AdminPlaybackNodesController(new(kit.Client), authority); await controller.OpenAsync();
        Assert.False(await controller.UpdateAsync(Assert.Single(controller.State.Window.Items), new Dictionary<string, object?> { ["group"] = "x" }));
        Assert.Equal("Set by the node\u2019s environment. Change it there and restart the node.", controller.State.Error); Assert.False(controller.State.ReviewRequired);
    }
    [Fact]
    public async Task ARefusalWithoutAReasonFallsBackToTheGenericLine()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        kit.Http.Respond = (request, _) => request.Method == HttpMethod.Put ? Task.FromResult(AdminMonitorKit.Json("{\"status\":422}", HttpStatusCode.UnprocessableEntity)) : Reads(request, Row());
        using var controller = new AdminPlaybackNodesController(new(kit.Client), authority); await controller.OpenAsync();
        Assert.False(await controller.UpdateAsync(Assert.Single(controller.State.Window.Items), new Dictionary<string, object?> { ["enabled"] = false }));
        Assert.Contains("refused", controller.State.Error);
    }
    [Fact]
    public async Task ALostKeyAnswerIsUnknownAndBlocksRepeatsUntilTheNodesAreReadAgain()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var keys = 0;
        kit.Http.Respond = (request, _) => { if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/key")) { keys++; throw new HttpRequestException("lost"); } return Reads(request, Row()); };
        using var controller = new AdminPlaybackNodesController(new(kit.Client), authority); await controller.OpenAsync(); var node = Assert.Single(controller.State.Window.Items);
        Assert.False(await controller.IssueKeyAsync(node, _ => throw new InvalidOperationException())); Assert.True(controller.State.ReviewRequired);
        Assert.False(await controller.IssueKeyAsync(node, _ => throw new InvalidOperationException())); Assert.Equal(1, keys);
        await controller.ReloadAsync(); Assert.False(controller.State.ReviewRequired);
    }

    // ---- plain text and copy ---------------------------------------------------------------------------------------------

    [Fact]
    public void PlainTextNeutralizesControlSeparatorAndDirectionCharactersAndBoundsLength()
    {
        Assert.Equal("a b c d e", AdminPlaybackNodeText.Plain("a\nb\r\nc\u2028d\u2029e"));
        Assert.Equal("ab", AdminPlaybackNodeText.Plain("\u202Ea\u2066b\u2069\u200F"));
        Assert.Equal("x y", AdminPlaybackNodeText.Plain("  x \t\u0000 y  "));
        var bounded = AdminPlaybackNodeText.Plain(new string('n', 500), 40); Assert.Equal(41, bounded.Length); Assert.EndsWith("\u2026", bounded);
        Assert.Equal("", AdminPlaybackNodeText.Plain(null));
        Assert.Equal("\uD83D\uDE00\uD83D\uDE00", AdminPlaybackNodeText.Plain("\uD83D\uDE00\uD83D\uDE00"));
    }
    [Fact]
    public void KeySettingsAreExactlyTheVisibleValuesWithNoInvisibleCharacters()
    {
        var settings = AdminPlaybackNodeText.KeySettings(" https://core.invalid:8080/// ", "snk1_synthetic");
        Assert.Equal("SILO_CORE_URL=https://core.invalid:8080\nSILO_NODE_KEY=snk1_synthetic", settings);
        Assert.All(settings!, character => Assert.False(AdminPlaybackNodeText.IsDirectionCharacter(character) || char.IsControl(character) && character != '\n'));
    }
    [Theory]
    [InlineData("\u2068https://core.invalid\u2069", "snk1_x")]
    [InlineData("https://core.invalid", "snk1_\u200Ex")]
    [InlineData("https://user:pw@core.invalid", "snk1_x")]
    [InlineData("https://core.invalid?token=x", "snk1_x")]
    [InlineData("", "snk1_x")]
    [InlineData("https://core.invalid", "")]
    [InlineData("https://core.invalid/\nSILO_NODE_KEY=evil", "snk1_x")]
    public void NothingIsCopiedWhenAValueCouldCarryAnInvisibleCharacterOrCredential(string core, string key) => Assert.Null(AdminPlaybackNodeText.KeySettings(core, key));
    [Fact]
    public void ReasonIsBoundedPlainAndRedacted()
    {
        var error = new ApiException("x", "m", 422) { ProblemDetail = new string('d', 900) + "\u202E" };
        var text = AdminPlaybackNodeText.Reason(error, "fallback"); Assert.True(text.Length <= 301); Assert.DoesNotContain('\u202E', text);
        Assert.Equal("fallback", AdminPlaybackNodeText.Reason(new ApiException("x", "m", 422), "fallback"));
        Assert.Equal("field detail", AdminPlaybackNodeText.Reason(new ApiException("x", "m", 422) { ProblemDetail = "summary", FieldErrors = [new("body.group", "managed_by_environment", "field detail")] }, "fallback"));
    }

    // ---- presentation ----------------------------------------------------------------------------------------------------

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-21T12:00:00Z");
    [Fact]
    public void ReprobeAndAccelerationFollowTheOfficialRules()
    {
        Assert.True(AdminPlaybackNodePresentation.ReportsAcceleration(Node("transcode"))); Assert.False(AdminPlaybackNodePresentation.ReportsAcceleration(Node("proxy")));
        var node = Node(); node.CapabilitiesHash = "sha256:a";
        var unchanged = AdminPlaybackNodePresentation.ReprobeOutcome(node, new() { Status = "ok", NodeName = "Node", CapabilityHash = "sha256:a", CapabilitiesRefreshed = true, Resolved = "qsv" });
        var changed = AdminPlaybackNodePresentation.ReprobeOutcome(node, new() { Status = "ok", CapabilityHash = "sha256:b", CapabilitiesRefreshed = true });
        var failed = AdminPlaybackNodePresentation.ReprobeOutcome(node, new() { Status = "error", Error = "no\nprobe" });
        Assert.True(unchanged.Ok && changed.Ok); Assert.NotEqual(unchanged.Message, changed.Message); Assert.False(failed.Ok); Assert.Contains("no probe", failed.Message);
    }
    [Fact]
    public void DrainStateAndGroupDegradationMatchTheOfficialPage()
    {
        var draining = Node(); draining.DrainRequested = true; draining.DrainState = "draining"; draining.ActiveJobs = 3;
        Assert.Equal("draining", AdminPlaybackNodePresentation.Drain(draining)!.Phase); Assert.Equal(DashboardNodeState.Draining, AdminPlaybackNodePresentation.State(draining));
        draining.DrainState = "drained"; Assert.Equal("drained", AdminPlaybackNodePresentation.Drain(draining)!.Phase);
        draining.Enabled = false; Assert.Equal(DashboardNodeState.Disabled, AdminPlaybackNodePresentation.State(draining));
        Assert.Null(AdminPlaybackNodePresentation.Drain(Node("proxy")));
        var ungrouped = Node(); var sick = Node(); sick.Id = "b"; sick.Group = "rack"; sick.Healthy = false; var well = Node("proxy"); well.Id = "c"; well.Group = "rack"; var off = Node(); off.Id = "d"; off.Group = "z"; off.Enabled = false; off.Healthy = false;
        var groups = AdminPlaybackNodePresentation.Groups([ungrouped, sick, well, off]);
        Assert.Equal(["rack", "z", ""], groups.Select(group => group.Value)); Assert.True(groups[0].Degraded); Assert.False(groups[1].Degraded); Assert.False(groups[2].Degraded);
    }
    [Fact]
    public void AccelerationStalenessAndOverrideFollowTheOfficialRules()
    {
        var node = Node(); node.CapabilitiesRefreshedAt = "2026-11-21T11:59:00Z"; node.LastHealthCheck = "2026-11-21T11:59:30Z"; node.CapabilitiesHash = "h"; node.AdvertisedCapabilitiesHash = "h";
        Assert.Null(AdminPlaybackNodePresentation.Staleness(node, Now));
        node.AdvertisedCapabilitiesHash = ""; Assert.Equal("unreported", AdminPlaybackNodePresentation.Staleness(node, Now));
        node.AdvertisedCapabilitiesHash = "other"; Assert.Equal("contradicted", AdminPlaybackNodePresentation.Staleness(node, Now));
        node.AdvertisedCapabilitiesHash = null; node.LastHealthCheck = "2026-11-21T11:00:00Z"; Assert.Equal("unconfirmed", AdminPlaybackNodePresentation.Staleness(node, Now));
        node.Healthy = false; Assert.Null(AdminPlaybackNodePresentation.Staleness(node, Now));
        Assert.Null(AdminPlaybackNodePresentation.Override(Node()));
        var pinned = Node(); pinned.HwAccelOverride = "none"; pinned.HwDeviceOverride = "/dev/dri/renderD128";
        Assert.Contains("software", AdminPlaybackNodePresentation.Override(pinned)!.Label); Assert.Contains("/dev/dri/renderD128", AdminPlaybackNodePresentation.Override(pinned)!.Label);
    }
    [Fact]
    public void BackendBadgeAndDevicePickerFollowTheReportedInventory()
    {
        var node = Node(); node.Capabilities = new() { Resolved = "qsv", DetectedBackends = [new() { Backend = "qsv", Verified = false, Reason = "driver\nmissing" }, new() { Backend = "vaapi", Verified = false, Reason = "bad" }],
            RenderDeviceDetails = [new() { Path = "/dev/dri/renderD128", Description = "Synthetic GPU" }, new() { Path = "/dev/dri/renderD129", Description = "Synthetic GPU" }] };
        var gpu = AdminPlaybackNodePresentation.Gpu(node, Now);
        Assert.Equal(AdminPlaybackBackendState.Failed, gpu.Backend!.State); Assert.Contains("driver missing", gpu.Backend.Title); Assert.Equal("VAAPI", Assert.Single(gpu.Failures).Label); Assert.Contains("2", gpu.DeviceSummary);
        var rows = AdminPlaybackNodePresentation.DeviceRows(node, "/dev/dri/renderD129,/dev/dri/gone");
        Assert.Equal(3, rows.Count); Assert.True(rows[1].Selected); Assert.False(rows[0].Selected); Assert.False(rows[2].Reported); Assert.True(rows[2].Selected);
        Assert.False(AdminPlaybackNodePresentation.HasDeviceInventory(Node()));
        Assert.True(AdminPlaybackNodePresentation.UsesCudaDevices(node, "nvenc", null)); Assert.False(AdminPlaybackNodePresentation.UsesCudaDevices(node, "qsv", "nvenc"));
        Assert.True(AdminPlaybackNodePresentation.UsesCudaDevices(node, "inherit", "nvenc")); Assert.True(AdminPlaybackNodePresentation.DeviceSyntaxChanges(node, "qsv", "nvenc", null));
    }
    [Fact]
    public void ASampleThatIsStaleOrFromAnUnhealthyNodeIsNotShownAsLive()
    {
        var node = Node(); node.LastHealthCheck = Now.AddSeconds(-5).ToString("O");
        node.LastStats = new() { SampledAt = Now.AddSeconds(-6).ToString("O"), System = new() { CpuPct = 40, MemUsedMb = 100, MemTotalMb = 200, NetRxBps = 2_000_000, NetTxBps = 500 } };
        var live = AdminPlaybackNodePresentation.Host(node, Now); Assert.True(live.Reported); Assert.Contains(live.Rows, row => row.Label == "Network" && row.Value.Contains('\u2193'));
        node.LastHealthCheck = Now.AddMinutes(-5).ToString("O"); Assert.False(AdminPlaybackNodePresentation.Host(node, Now).Reported);
        node.LastHealthCheck = Now.AddSeconds(-5).ToString("O"); node.Healthy = false; Assert.False(AdminPlaybackNodePresentation.Host(node, Now).Reported);
    }
}
