using System.Net;
using System.Reflection;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// Baseline-compilable RED checkpoint: missing production types fail assertions, rather than preventing
/// the coordinator from running the tests against Beta 248aafd6. Reflection is confined to this fixture.
/// All transport data is synthetic. Contracts: official silo-server 217ff25.
/// </summary>
public sealed class AdminPlaybackAdministrationTests
{
    private static readonly Assembly Core = typeof(AdminSettingsForm).Assembly;
    private static Type Type(string name) { var type = Core.GetType(name); Assert.NotNull(type); return type!; }
    private static Type Service(string name) => Type("SiloPlayer.Core.Services.Admin." + name);
    private static object? Rule(string name, params object?[] args) => Service("AdminPlaybackSettingsRules").GetMethod(name)!.Invoke(null, args);
    private static dynamic Wire(string name, string json) => JsonSerializer.Deserialize(json, Type("SiloPlayer.Core.Models.Admin." + name), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;
    private static dynamic Api(AdminMonitorKit kit) => Activator.CreateInstance(Type("SiloPlayer.Core.Api.AdminPlaybackAdministrationApi"), kit.Client)!;
    private const string Node = """{"id":"node/one","name":"Synthetic node","type":"transcode","url":"https://worker.invalid","enabled":true,"healthy":true,"active_jobs":2,"group":"rack-a","max_jobs":4,"max_bandwidth_kbps":0,"egress_kbps":1000,"last_health_check":"2026-11-17T12:00:00Z","created_at":"2026-11-17T00:00:00Z","drain_requested":false,"drain_state":"none","key_issued":true,"config_etag":"\"node-1\"","capabilities":{"transport_features":["trickplay_extract_v2"]},"capabilities_hash":"hash","advertised_capabilities_hash":"hash"}""";
    private static HttpResponseMessage Response(string body, string? etag = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = AdminMonitorKit.Json(body, status);
        if (etag != null) response.Headers.ETag = new(etag);
        return response;
    }
    [Fact]
    public void BothPagesAreRegisteredAndTheirXbfResourcesAreApproved()
    {
        var registration = File.ReadAllText(Path.Combine(TestRepository.Root, "src", "SiloPlayer", "Services", "AdminConfigureServices.cs"));
        Assert.Contains("AdminPageRegistration(AdminRoute.Nodes, typeof(AdminNodesPage))", registration);
        Assert.Contains("AdminPageRegistration(AdminRoute.PlaybackSettings, typeof(AdminPlaybackSettingsPage))", registration);
        var installer = File.ReadAllText(Path.Combine(TestRepository.Root, "installer", "build.ps1"));
        Assert.Contains("Views/Admin/AdminNodesPage.xbf", installer); Assert.Contains("Views/Admin/AdminPlaybackSettingsPage.xbf", installer);
    }

    [Fact]
    public void PlaybackOwnsExactlyTheOfficialThirtyThreeKeysAndRoutingOwnsFiveSeparateKeys()
    {
        var rules = Service("AdminPlaybackSettingsRules");
        var keys = (IReadOnlyList<string>)rules.GetProperty("Keys")!.GetValue(null)!;
        Assert.Equal(33, keys.Count);
        Assert.Equal(33, keys.Distinct().Count());
        Assert.DoesNotContain(keys, key => key.StartsWith("download.") || key.StartsWith("playback.routing."));
        Assert.DoesNotContain("playback.chapter_thumbnail_node_capacity", keys);
        Assert.Contains("playback.copy_keyframe_playlist", keys);
        Assert.Contains("markers.detect_credits", keys);
        Assert.Equal(5, ((IReadOnlyList<string>)rules.GetProperty("RoutingKeys")!.GetValue(null)!).Count);
    }

    [Theory]
    [InlineData("playback.prefer_lower_version_transcode", "allow_4k_transcode", "true", true)]
    [InlineData("playback.prefer_lower_version_transcode", "allow_4k_transcode", "false", false)]
    [InlineData("transcode_throttle_seconds", "enable_transcode_throttle", "true", true)]
    [InlineData("transcode_throttle_seconds", "enable_transcode_throttle", "false", false)]
    [InlineData("markers.online_storage", "markers.mode", "online", true)]
    [InlineData("markers.online_storage", "markers.mode", "local", false)]
    [InlineData("markers.detection_workers", "markers.mode", "off", false)]
    [InlineData("markers.detection_workers", "markers.mode", "both", true)]
    public void ConditionalControlsFollowEffectiveDraft(string key, string dependency, string value, bool visible) =>
        Assert.Equal(visible, (bool)Rule("Visible", key, new Dictionary<string, string> { [dependency] = value }, true, true)!);

    [Theory]
    [InlineData("playback.preview_image_width")]
    [InlineData("playback.trickplay_workers")]
    [InlineData("playback.trickplay_execution")]
    public void UnsupportedTrickplayIsHidden(string key) =>
        Assert.False((bool)Rule("Visible", key, new Dictionary<string, string>(), false, true)!);

    [Fact]
    public void DetectionKindAndOnDemandLazyControlsFollowCapabilitiesAndMode()
    {
        var values = new Dictionary<string, string> { ["markers.mode"] = "both", ["markers.online_storage"] = "on_demand" };
        Assert.False((bool)Rule("Visible", "markers.detect_intros", values, true, false)!);
        Assert.False((bool)Rule("Visible", "markers.lazy_playback", values, true, true)!);
    }

    [Theory]
    [InlineData("standard", "prefer_proxy", "prefer_transcode", "prefer_proxy")]
    [InlineData("gpu", "prefer_api", "prefer_api", "prefer_proxy")]
    [InlineData("central", "api_only", "prefer_worker", "api_only")]
    public void PresetsStageTheOfficialFiveValues(string preset, string direct, string remux, string videoEgress)
    {
        var values = (IReadOnlyDictionary<string, string>)Rule("Preset", preset)!;
        Assert.Equal(5, values.Count);
        Assert.Equal(direct, values["playback.routing.direct_play_egress"]);
        Assert.Equal(remux, values["playback.routing.remux_execution"]);
        Assert.Equal(videoEgress, values["playback.routing.video_transcode_egress"]);
    }

    [Theory]
    [InlineData("playback.hw_accel", "cuda")]
    [InlineData("markers.mode", "all")]
    [InlineData("playback.chapter_thumbnail_hdr_policy", "hardware")]
    [InlineData("playback.watched_threshold", "101")]
    [InlineData("playback.trickplay_workers", "1.5")]
    public void InvalidTypedSettingsAreRejected(string key, string value)
    {
        var errors = (IReadOnlyDictionary<string, string>)Rule("Validate", new Dictionary<string, string> { [key] = value }, new[] { key })!;
        Assert.Contains(key, errors.Keys);
    }

    [Fact]
    public async Task NodeUpdateUsesCapturedStrongConfigTagAndAcknowledgesTheSameNode()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        string? tag = null;
        kit.Http.Respond = (request, _) => { tag = request.Headers.IfMatch.Single().ToString(); return Task.FromResult(Response(Node.Replace("node-1", "node-2"), "\"node-2\"")); };
        dynamic api = Api(kit);
        await (Task)api.UpdateNodeAsync(lease, Wire("AdminPlaybackNode", Node), new Dictionary<string, object?> { ["enabled"] = false }, CancellationToken.None);
        Assert.Equal("\"node-1\"", tag);
        Assert.Single(kit.Http.Requests);
        Assert.Contains("node%2Fone", kit.Http.Requests.Single(), StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(kit.Http.Bodies.Single());
        Assert.Equal(JsonValueKind.False, body.RootElement.GetProperty("enabled").ValueKind);
        Assert.Single(body.RootElement.EnumerateObject());
    }

    [Theory]
    [InlineData("W/\"node-1\"")]
    [InlineData("*")]
    [InlineData("")]
    public async Task MissingOrWeakConfigTagNeverSendsDelete(string tag)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        dynamic api = Api(kit);
        // Use the parsed model to avoid constructing invalid JSON for a quoted ETag.
        dynamic node = Wire("AdminPlaybackNode", Node); node.ConfigETag = tag;
        await Assert.ThrowsAnyAsync<InvalidDataException>(async () => await (Task)api.DeleteNodeAsync(lease, node, CancellationToken.None));
        Assert.Empty(kit.Http.Requests);
    }

    [Fact]
    public async Task DeleteAcceptsAnEmpty204AndDoesNotReplay()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        dynamic api = Api(kit); await (Task)api.DeleteNodeAsync(lease, Wire("AdminPlaybackNode", Node), CancellationToken.None);
        Assert.Single(kit.Http.Requests);
    }

    [Theory]
    [InlineData("check", "{\"healthy\":false,\"active_jobs\":0,\"egress_kbps\":0,\"health_persisted\":false}")]
    [InlineData("reprobe", "{\"node_id\":\"node/one\",\"node_name\":\"Synthetic node\",\"status\":\"error\",\"capabilities_refreshed\":false}")]
    [InlineData("drain", "{\"node_id\":\"node/one\",\"drain_requested\":true,\"drain_state\":\"none\",\"active_jobs\":2}")]
    [InlineData("resume", "{\"node_id\":\"node/one\",\"drain_requested\":false,\"drain_state\":\"draining\",\"active_jobs\":2}")]
    public async Task CommandsValidateAcknowledgementsWithoutTreatingAcceptedWorkAsCompleted(string command, string json)
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        kit.Http.Respond = (_, _) => Task.FromResult(Response(json)); dynamic api = Api(kit);
        var result = await api.CommandAsync(lease, Wire("AdminPlaybackNode", Node), command, CancellationToken.None);
        Assert.NotNull((object)result); Assert.Single(kit.Http.Requests);
        Assert.EndsWith("/" + command, kit.Http.Requests.Single());
    }

    [Fact]
    public async Task ListStopsAtItsPageBudgetAndDisclosesContinuation()
    {
        using var kit = AdminMonitorKit.Create(); using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth); using var lease = authority.Capture();
        var calls = 0; kit.Http.Respond = (_, _) => { var item = Node.Replace("node/one", "n" + ++calls); return Task.FromResult(Response("{\"items\":[" + item + "],\"page\":{\"next_cursor\":\"c" + calls + "\"}}")); };
        dynamic api = Api(kit); dynamic window = await api.ListNodesAsync(lease, CancellationToken.None);
        Assert.True((bool)window.HasMore); Assert.InRange(calls, 1, 8);
        Assert.Equal(calls, ((System.Collections.ICollection)window.Items).Count);
    }

    private sealed class SettingsHarness : IDisposable
    {
        internal readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        internal readonly AdminMonitorAuthority Authority; internal readonly AdminServerSettingsStore Store;
        internal readonly dynamic Controller;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>>? Write;
        internal SettingsHarness()
        {
            Authority = new(Kit.Client, Kit.Auth); var api = new AdminServerSettingsApi(Kit.Client);
            var keys = (IReadOnlyList<string>)Service("AdminPlaybackSettingsRules").GetProperty("Keys")!.GetValue(null)!;
            Store = new(api, Authority, keys, []);
            Controller = Activator.CreateInstance(Service("AdminPlaybackSettingsController"), api, Store, Authority, false)!;
            Kit.Http.Respond = (request, _) =>
            {
                if (request.Method != HttpMethod.Get) return Write?.Invoke(request) ?? Task.FromResult(Response("{}", status: HttpStatusCode.InternalServerError));
                var body = request.RequestUri!.AbsolutePath.EndsWith("effective") ? "{\"markers.mode\":\"both\",\"markers.online_storage\":\"stored\",\"markers.lazy_playback\":\"false\",\"playback.preview_image_width\":\"300\",\"playback.trickplay_interval_seconds\":\"10\"}" : request.RequestUri.AbsolutePath.EndsWith("restart-keys") ? "{\"keys\":[],\"prefixes\":[]}" : "{\"configured\":[],\"managed_by_env\":[]}";
                return Task.FromResult(Response(body, "\"settings-1\""));
            };
        }
        public void Dispose() { ((IDisposable)Controller).Dispose(); Store.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    [Fact]
    public async Task OnDemandOnlineStorageStagesLazyPlaybackAndSavesOnlyDirtyStringValues()
    {
        using var h = new SettingsHarness(); await (Task)h.Controller.OpenAsync();
        h.Controller.Set("markers.online_storage", "on_demand");
        var values = (IReadOnlyDictionary<string, string>)h.Controller.State.Values;
        Assert.Equal("true", values["markers.lazy_playback"]);
        h.Write = request => { Assert.Equal("\"settings-1\"", request.Headers.IfMatch.Single().ToString()); return Task.FromResult(Response("{\"values\":{\"markers.online_storage\":\"on_demand\",\"markers.lazy_playback\":\"true\"},\"restart_required\":false}", "\"settings-2\"")); };
        await (Task)h.Controller.SaveAsync();
        var writes = h.Kit.Http.Requests.Where(x => x.StartsWith("PUT")).ToArray(); Assert.Single(writes);
        Assert.Empty((IReadOnlyCollection<string>)h.Controller.State.DirtyKeys);
        using var body = JsonDocument.Parse(h.Kit.Http.Bodies.Last()); Assert.Equal(2, body.RootElement.GetProperty("values").EnumerateObject().Count());
    }

    [Fact]
    public async Task ConflictBlocksReplayUntilExplicitReviewAndReload()
    {
        using var h = new SettingsHarness(); await (Task)h.Controller.OpenAsync(); h.Controller.Set("markers.mode", "off");
        h.Write = _ => Task.FromResult(Response("{\"type\":\"precondition_failed\",\"status\":412}", "\"settings-2\"", HttpStatusCode.PreconditionFailed));
        await (Task)h.Controller.SaveAsync(); await (Task)h.Controller.SaveAsync();
        Assert.True((bool)h.Controller.State.Conflict); Assert.Single(h.Kit.Http.Requests.Where(x => x.StartsWith("PUT")));
        await (Task)h.Controller.ReloadAsync(true); Assert.False((bool)h.Controller.State.Conflict);
        Assert.Contains("markers.mode", (IReadOnlyCollection<string>)h.Controller.State.DirtyKeys);
    }

    [Fact]
    public async Task LostWriteIsNotRetriedAndRetirementErasesTheDraft()
    {
        using var h = new SettingsHarness(); await (Task)h.Controller.OpenAsync(); h.Controller.Set("markers.mode", "off");
        h.Write = _ => throw new HttpRequestException("synthetic lost answer");
        await (Task)h.Controller.SaveAsync(); await (Task)h.Controller.SaveAsync();
        Assert.True((bool)h.Controller.State.OutcomeUnknown); Assert.Single(h.Kit.Http.Requests.Where(x => x.StartsWith("PUT")));
        h.Controller.Suspend(); Assert.Empty((IReadOnlyDictionary<string, string>)h.Controller.State.Values);
    }
}
