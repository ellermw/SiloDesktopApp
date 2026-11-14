using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Session drain, sequenced commands, terminate, IP lookup and FFmpeg rows (upstream adminSessions/adminPlaybackCommands tests).</summary>
public sealed class AdminMonitorActivityApiTests
{
    private static string SessionJson(string id) =>
        $$"""{"session_id":"{{id}}","user_id":"7","username":"u","profile_id":"p","media_file_id":"1","requested_media_file_id":"1","media_title":"T","media_type":"movie","play_method":"direct","reporting_node":"","file_duration":null,"started_at":"2026-11-14T10:00:00Z","updated_at":"2026-11-14T10:00:00Z","position_seconds":0,"is_paused":false,"has_playback_control":true,"stream_location":"local","audio_track_index":0,"transcode_audio":false,"stream_bitrate_kbps":null,"target_bitrate_kbps":null,"source_bitrate_kbps":null,"source_audio_channels":null}""";

    private static string Page(bool hasMore, string? next, params string[] rows) =>
        "{\"items\":[" + string.Join(",", rows) + "],\"page\":{\"has_more\":" + (hasMore ? "true" : "false") +
        (next == null ? "" : ",\"next_cursor\":\"" + next + "\"") + "}}";

    private static (AdminMonitorKit Kit, AdminMonitorAuthority Authority, AdminMonitorApi Api) Create()
    {
        var kit = AdminMonitorKit.Create();
        return (kit, new AdminMonitorAuthority(kit.Client, kit.Auth), new AdminMonitorApi(kit.Client));
    }

    [Fact]
    public async Task SessionDrainDedupesAcceptsEmptyContinuationsAndUsesPageSize100()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (request, _) =>
        {
            var query = request.RequestUri!.Query;
            return Task.FromResult(AdminMonitorKit.Json(query.Contains("cursor=c2")
                ? Page(false, null, SessionJson("b"), SessionJson("c"))
                : query.Contains("cursor=c1")
                    ? """{"items":[],"page":{"has_more":true,"next_cursor":"c2"}}"""
                      : Page(true, "c1", SessionJson("a"), SessionJson("b"))));
        };
        using var lease = authority.Capture();
        var sessions = await api.ListActivitySessionsAsync(lease);
        Assert.Equal(["a", "b", "c"], sessions.Select(s => s.SessionId));
        Assert.All(kit.Http.Requests, r => Assert.Contains("limit=100", r));
    }

    [Theory]
    [InlineData("""{"items":[]}""")]
    [InlineData("""{"items":[],"page":{"has_more":true}}""")]
    public async Task SessionDrainRejectsMissingPageOrContinuation(string body)
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body));
        using var lease = authority.Capture();
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListActivitySessionsAsync(lease));
    }

    [Fact]
    public async Task SessionDrainRejectsRepeatedCursorAndNonIntegerIds()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":true,"next_cursor":"same"}}"""));
        using var lease = authority.Capture();
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListActivitySessionsAsync(lease));
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(Page(false, null, SessionJson("x").Replace("\"user_id\":\"7\"", "\"user_id\":\"7.5\""))));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.ListActivitySessionsAsync(lease));
    }

    [Fact]
    public async Task ProfileSwitchDuringALaterPageDiscardsTheDrain()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        var calls = 0;
        kit.Http.Respond = (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                kit.Auth.SelectProfile("child", null, new() { Id = "child", IsPrimary = false });
                return Task.FromResult(AdminMonitorKit.Json(Page(true, "c1", SessionJson("a"))));
            }
            return Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}"""));
        };
        using var lease = authority.Capture();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ListActivitySessionsAsync(lease));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CommandSendsOneIdentityAndValidatesTheReceipt()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = async (request, _) =>
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement;
            var id = body.GetProperty("command_id").GetString();
            return AdminMonitorKit.Json($$"""{"command_id":"{{id}}","sequence":{{body.GetProperty("sequence").GetInt64()}},"outcome":"applied","delivery":"dispatched"}""", HttpStatusCode.Accepted);
        };
        using var lease = authority.Capture();
        var receipt = await api.SendActivityCommandAsync(lease, "s/1", "pause");
        Assert.Equal("applied", receipt.Outcome);
        Assert.Equal("POST /api/v2/admin/sessions/s%2F1/pause", Assert.Single(kit.Http.Requests));
        var sent = JsonDocument.Parse(Assert.Single(kit.Http.Bodies)).RootElement;
        Assert.True(Guid.TryParse(sent.GetProperty("command_id").GetString(), out _));
        Assert.True(sent.GetProperty("sequence").GetInt64() > 0);
        Assert.False(sent.TryGetProperty("message", out _));
        var outcome = AdminActivityCommandText.FromReceipt("pause", receipt);
        Assert.Equal(("Pause command sent", (bool?)true), (outcome.Message, outcome.OptimisticPaused));
    }

    [Fact]
    public async Task UnknownOutcomeRepeatsTheSameIdentityOnlyForTheSameIntent()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        var identities = new List<(string Id, long Sequence, string Path)>();
        var fail = true;
        kit.Http.Respond = async (request, _) =>
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement;
            var id = body.GetProperty("command_id").GetString()!;
            identities.Add((id, body.GetProperty("sequence").GetInt64(), request.RequestUri!.AbsolutePath));
            if (fail) return AdminMonitorKit.Json("""{"type":"https://siloserver.org/docs/api/v2/problems/internal","title":"x","status":503,"detail":"lane unavailable","instance":"urn:silo:request:1"}""", HttpStatusCode.ServiceUnavailable);
            return AdminMonitorKit.Json($$"""{"command_id":"{{id}}","sequence":1,"outcome":"replayed","delivery":"dispatched"}""");
        };
        using var lease = authority.Capture();
        await Assert.ThrowsAsync<AdminActivityCommandException>(() => api.SendActivityCommandAsync(lease, "s", "stop"));
        Assert.True(api.ActivityCommandLedger.HasUnsettled("s"));
        fail = false;
        var replayed = await api.SendActivityCommandAsync(lease, "s", "stop");
        Assert.Equal(identities[0].Id, identities[1].Id);
        Assert.Equal(identities[0].Sequence, identities[1].Sequence);
        Assert.Contains("earlier stop request was already accepted", AdminActivityCommandText.FromReceipt("stop", replayed).Message);

        // A different command supersedes: fresh identity, higher sequence.
        fail = true;
        await Assert.ThrowsAsync<AdminActivityCommandException>(() => api.SendActivityCommandAsync(lease, "s", "stop"));
        fail = false;
        await api.SendActivityCommandAsync(lease, "s", "pause");
        Assert.NotEqual(identities[2].Id, identities[3].Id);
        Assert.True(identities[3].Sequence > identities[2].Sequence);
    }

    [Fact]
    public async Task StaleRefusalRaisesTheAllocationFloor()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        var sequences = new List<long>();
        kit.Http.Respond = async (request, _) =>
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement;
            sequences.Add(body.GetProperty("sequence").GetInt64());
            var response = AdminMonitorKit.Json("""{"type":"https://siloserver.org/docs/api/v2/problems/conflict","title":"Conflict","status":409,"detail":"stale command","instance":"urn:silo:request:1"}""", HttpStatusCode.Conflict);
            response.Headers.Add("X-Silo-Latest-Sequence", "9000000000000");
            return response;
        };
        using var lease = authority.Capture();
        var error = await Assert.ThrowsAsync<AdminActivityCommandException>(() => api.SendActivityCommandAsync(lease, "s", "resume"));
        Assert.Equal("The resume command was not applied: stale command", AdminActivityCommandText.DescribeFailure("resume", error));
        Assert.False(api.ActivityCommandLedger.HasUnsettled("s"));
        Assert.True(api.ActivityCommandLedger.Allocate("s").Sequence > 9_000_000_000_000);
    }

    [Fact]
    public async Task RetiredLeaseRefusesBeforeSendingAndRoleLossDuringSendIsStale()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        var lease = authority.Capture();
        kit.SetRole("user");
        await Assert.ThrowsAsync<MonitorAuthorityRetiredException>(() => api.SendActivityCommandAsync(lease, "s", "pause"));
        Assert.Empty(kit.Http.Requests);

        kit.SetRole("admin");
        var second = authority.Capture();
        kit.Http.Respond = (request, _) =>
        {
            kit.SetRole("user");
            return Task.FromResult(AdminMonitorKit.Json("""{"session_id":"s","authority_revoked":true,"already_revoked":false,"durable_state":"stopped","client_notified":false,"delivery":"unavailable"}"""));
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.TerminateActivitySessionAsync(second, "s"));
    }

    [Fact]
    public void TerminateAndFailureCopyKeepRevocationAndNotificationDistinct()
    {
        var text = AdminActivityCommandText.FromTerminate(new() { SessionId = "s", AuthorityRevoked = true, ClientNotified = false });
        Assert.Equal("Playback authority revoked; the player could not be reached and will stop when it next contacts the server.", text.Message);
        Assert.Equal("Playback authority was already revoked; the player was told to stop.",
            AdminActivityCommandText.FromTerminate(new() { AlreadyRevoked = true, ClientNotified = true }).Message);
        Assert.Equal("Stop could not reach the player directly. Silo will end the session shortly instead.",
            AdminActivityCommandText.FromReceipt("stop", new() { Outcome = "applied", Delivery = "fallback_scheduled" }).Message);
        Assert.Null(AdminActivityCommandText.FromReceipt("pause", new() { Outcome = "applied", Delivery = "fallback_scheduled" }).OptimisticPaused);
        Assert.Equal("This playback session has already ended.",
            AdminActivityCommandText.DescribeFailure("stop", new ApiException("not_found", "gone", 404)));
    }

    [Fact]
    public async Task MessageRequiresTextAndSendsIt()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = async (request, _) =>
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement;
            return AdminMonitorKit.Json($$"""{"command_id":"{{body.GetProperty("command_id").GetString()}}","sequence":1,"outcome":"applied","delivery":"dispatched"}""");
        };
        using var lease = authority.Capture();
        await Assert.ThrowsAsync<ArgumentException>(() => api.SendActivityCommandAsync(lease, "s", "message", "   "));
        await api.SendActivityCommandAsync(lease, "s", "message", "  Restart soon ");
        Assert.Equal("Restart soon", JsonDocument.Parse(Assert.Single(kit.Http.Bodies)).RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task IpLookupPagesRetainGoodRowsOnLaterFailureAndRestart()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (request, _) => Task.FromResult(request.RequestUri!.Query.Contains("cursor=n1")
            ? AdminMonitorKit.Json("{}", HttpStatusCode.InternalServerError)
            : AdminMonitorKit.Json("""{"items":[{"user_id":"3","username":"sam","first_seen":"2026-11-01T00:00:00Z","last_seen":"2026-11-02T00:00:00Z","request_count":12}],"page":{"has_more":true,"next_cursor":"n1"}}"""));
        using var lease = authority.Capture();
        var lookup = new AdminActivityIpLookup(api);
        await lookup.StartAsync(lease, " 203.0.113.50 ");
        Assert.Equal(AdminIpLookupView.Rows, lookup.View);
        Assert.True(lookup.HasNextPage);
        Assert.Contains("ip=203.0.113.50&days=30&limit=50", kit.Http.Requests.First());
        await lookup.LoadMoreAsync(lease);
        Assert.True(lookup.HasError);
        Assert.Single(lookup.Rows);
        Assert.Equal(AdminIpLookupView.Rows, lookup.View);
        await lookup.RestartAsync(lease);
        Assert.False(lookup.HasError);
        Assert.Single(lookup.Rows);
    }

    [Fact]
    public async Task IpLookupFirstPageErrorAndEmptyStates()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false,"next_cursor":"bad"}}"""));
        using var lease = authority.Capture();
        var lookup = new AdminActivityIpLookup(api);
        await lookup.StartAsync(lease, "10.0.0.1");
        Assert.Equal(AdminIpLookupView.LoadError, lookup.View);
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[],"page":{"has_more":false}}"""));
        await lookup.RestartAsync(lease);
        Assert.Equal(AdminIpLookupView.Empty, lookup.View);
        Assert.Equal("No users found for 10.0.0.1 in the last 30 days.", AdminText.T("web.admin.activity.ipLookup.empty", new { ip = lookup.ActiveIp }));
    }

    [Fact]
    public async Task FfmpegConsoleReadsTwelveRowsForTheSession()
    {
        var (kit, authority, api) = Create();
        using var _k = kit; using var _a = authority;
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("""{"items":[{"id":"5","timestamp":"2026-11-14T10:00:00Z","level":"info","component":"ffmpeg","message":"ffmpeg stderr","attrs":{"ffmpeg_line":"frame=1"}}],"page":{"has_more":false}}"""));
        using var lease = authority.Capture();
        var rows = await api.ListActivityFfmpegRowsAsync(lease, "sess 1");
        Assert.Equal("GET /api/v2/admin/logs/app?playback_session_id=sess%201&component=ffmpeg&limit=12", Assert.Single(kit.Http.Requests));
        Assert.Equal("frame=1", AdminActivityPresentation.FfmpegRowText(Assert.Single(rows)));
    }
}
