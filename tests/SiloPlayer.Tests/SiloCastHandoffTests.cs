using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.SiloCast;

namespace SiloPlayer.Tests;

public sealed class SiloCastHandoffTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-12T12:00:00Z");
    private const string Start = """{"device_code":"secret-device-code","user_code":"4821-7730","match_code":"warm pony","expires_at":"2026-10-12T12:15:00Z","expires_in":900,"interval":5,"client_purpose":"remote_playback","temporary":true}""";
    private const string Approved = """{"status":"approved","temporary":true,"profile_id":"phone-profile","profile_token":"proof","session_expires_at":"2026-10-12T14:00:00Z","tokens":{"access_token":"phone-access","refresh_token":"phone-refresh","expires_in":3600,"user":{"id":"7","username":"phone","role":"user"}}}""";

    [Fact]
    public async Task ApprovalCreatesIndependentProfileAuthorityAndDisposalClearsTokens()
    {
        JsonObject? challenge = null;
        var handler = new ScriptedHandler();
        var localApi = new SiloApiClient(new HttpClient(new ScriptedHandler()));
        localApi.SetBaseUrl("https://desktop.test");
        using var localAuth = new AuthService(localApi, new AuthApi(localApi));
        localAuth.SetTokens("desktop-access", "desktop-refresh", 86400);
        localAuth.SelectProfile("desktop-profile", "desktop-proof");
        var identity = await Handoff(handler).PrepareAsync(Offer(), "PC", "pc-id", value => { challenge = value; return Task.CompletedTask; }, default);

        Assert.Equal("phone-access", identity.ApiClient.AccessToken);
        Assert.Equal("phone-refresh", identity.Auth.RefreshToken);
        Assert.Equal("phone-profile", identity.ApiClient.ProfileId);
        Assert.Equal("proof", identity.ApiClient.ProfileToken);
        Assert.Equal("phone-profile", identity.ProfileId);
        Assert.Equal("Phone profile", identity.ProfileName);
        Assert.Equal("request-1", identity.RequestId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T14:00:00Z"), identity.ExpiresAt);
        Assert.Equal("4821-7730", challenge?["userCode"]?.GetValue<string>());
        Assert.Equal("request-1", challenge?["requestId"]?.GetValue<string>());
        Assert.Null(challenge?["deviceCode"]);
        Assert.Equal("remote_playback", handler.StartBody?["client_purpose"]?.GetValue<string>());
        Assert.True(handler.StartBody?["temporary"]?.GetValue<bool>());
        Assert.Equal("windows", handler.StartBody?["device_platform"]?.GetValue<string>());
        Assert.Equal("pc-id", handler.DeviceId);
        Assert.Equal("pc-id", handler.PollDeviceId);
        Assert.All(handler.BootstrapAuthorization, Assert.Null);
        Assert.Equal("desktop-access", localApi.AccessToken);
        Assert.Equal("desktop-refresh", localAuth.RefreshToken);
        Assert.Equal("desktop-profile", localApi.ProfileId);
        identity.Dispose();
        Assert.Null(identity.ApiClient.AccessToken);
        Assert.Null(identity.Auth.RefreshToken);
        Assert.Null(identity.ApiClient.ProfileToken);
    }

    [Theory]
    [InlineData("serverId", "wrong")]
    [InlineData("serverURL", "file:///C:/test")]
    [InlineData("serverURL", "https://user:password@example.test")]
    [InlineData("serverURL", "https://example.test?token=secret")]
    [InlineData("requestId", "")]
    [InlineData("profileId", " ")]
    public async Task InvalidOfferCannotStartApproval(string field, string value)
    {
        var handler = new ScriptedHandler();
        var offer = Offer();
        offer[field] = value;
        await Assert.ThrowsAsync<SiloCastHandoffException>(() => Handoff(handler).PrepareAsync(offer, "PC", "id", _ => Task.CompletedTask, default));
        Assert.Empty(handler.Paths);
    }

    [Theory]
    [InlineData("profile_id", "other")]
    [InlineData("profile_token", "")]
    [InlineData("session_expires_at", "2026-10-12T11:59:59Z")]
    [InlineData("session_expires_at", "nonsense")]
    public async Task ApprovalMustProveExactProfileAndFutureExpiry(string field, string value)
    {
        var approved = JsonNode.Parse(Approved)!.AsObject();
        approved[field] = value;
        var handler = new ScriptedHandler { PollResponse = approved.ToJsonString() };
        await Assert.ThrowsAsync<SiloCastHandoffException>(() => Handoff(handler).PrepareAsync(Offer(), "PC", "id", _ => Task.CompletedTask, default));
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("expired")]
    [InlineData("consumed")]
    [InlineData("canceled")]
    [InlineData("unknown")]
    public async Task TerminalPollNeverCreatesAuthority(string status)
    {
        var handler = new ScriptedHandler { PollResponse = new JsonObject { ["status"] = status }.ToJsonString() };
        await Assert.ThrowsAsync<SiloCastHandoffException>(() => Handoff(handler).PrepareAsync(Offer(), "PC", "id", _ => Task.CompletedTask, default));
    }

    [Fact]
    public async Task CancellationWinsEvenWhenApprovalResponseArrives()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new ScriptedHandler { OnPoll = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handoff(handler).PrepareAsync(Offer(), "PC", "id", _ => Task.CompletedTask, cancellation.Token));
    }

    [Fact]
    public async Task PendingPollUsesServerWaitAndExpiresWithoutSpinning()
    {
        var now = Now;
        var waits = new List<TimeSpan>();
        var handler = new ScriptedHandler { PollResponse = """{"status":"pending","poll_after":7}""" };
        var handoff = new SiloCastHandoff(() => handler, () => now, (delay, _) => { waits.Add(delay); now += delay; return Task.CompletedTask; });
        await Assert.ThrowsAsync<SiloCastHandoffException>(() => handoff.PrepareAsync(Offer(), "PC", "id", _ => Task.CompletedTask, default));
        Assert.Equal(TimeSpan.FromSeconds(7), waits[0]);
        Assert.Equal(TimeSpan.FromSeconds(900), waits.Aggregate(TimeSpan.Zero, (sum, wait) => sum + wait));
    }

    [Fact]
    public async Task AlternativeEndpointMustAnswerMatchingDeploymentIdentity()
    {
        var handler = new ScriptedHandler { UnreachableHost = "example.test" };
        var offer = Offer();
        offer["serverIdentity"] = "deployment-1";
        offer["serverEndpoints"] = new JsonArray(new JsonObject { ["url"] = "https://reachable.test", ["kind"] = "public" });
        using var identity = await Handoff(handler).PrepareAsync(offer, "PC", "id", _ => Task.CompletedTask, default);
        Assert.Equal("https://reachable.test", identity.ApiClient.BaseUrl);
        Assert.Equal("aHR0cHM6Ly9leGFtcGxlLnRlc3Q", identity.ServerId);
        Assert.All(handler.BootstrapAuthorization, Assert.Null);
    }

    [Fact]
    public async Task IdentityMismatchStopsBeforeDeviceStart()
    {
        var handler = new ScriptedHandler { Identity = "other-deployment" };
        var offer = Offer();
        offer["serverIdentity"] = "deployment-1";
        var error = await Assert.ThrowsAsync<SiloCastHandoffException>(() => Handoff(handler).PrepareAsync(offer, "PC", "id", _ => Task.CompletedTask, default));
        Assert.Equal("identity_mismatch", error.Code);
        Assert.DoesNotContain("/api/v2/auth/device/start", handler.Paths);
    }

    [Fact]
    public async Task RedirectDoesNotFollowToAnAlternativeOrigin()
    {
        var handler = new ScriptedHandler { RedirectCapability = true };
        await Assert.ThrowsAsync<SiloCastHandoffException>(() => Handoff(handler).PrepareAsync(Offer(), "PC", "id", _ => Task.CompletedTask, default));
        Assert.Single(handler.Paths);
    }

    [Theory]
    [InlineData("access_token", "")]
    [InlineData("refresh_token", " ")]
    public async Task EmptyApprovalCredentialsAreRejected(string field, string value)
    {
        var approved = JsonNode.Parse(Approved)!.AsObject();
        approved["tokens"]![field] = value;
        var handler = new ScriptedHandler { PollResponse = approved.ToJsonString() };
        var error = await Assert.ThrowsAsync<SiloCastHandoffException>(() => Handoff(handler).PrepareAsync(Offer(), "PC", "id", _ => Task.CompletedTask, default));
        Assert.Equal("invalid_response", error.Code);
    }

    [Fact]
    public async Task NonTemporaryApprovalIsRejected()
    {
        var approved = JsonNode.Parse(Approved)!.AsObject();
        approved["temporary"] = false;
        var handler = new ScriptedHandler { PollResponse = approved.ToJsonString() };
        var error = await Assert.ThrowsAsync<SiloCastHandoffException>(() => Handoff(handler).PrepareAsync(Offer(), "PC", "id", _ => Task.CompletedTask, default));
        Assert.Equal("invalid_response", error.Code);
    }

    [Fact]
    public async Task CancellationWithdrawsPendingRequestWithoutCredentials()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new ScriptedHandler();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Handoff(handler).PrepareAsync(Offer(), "PC", "id", _ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token));
        Assert.Contains("/api/v2/auth/device/cancel", handler.Paths);
        Assert.DoesNotContain("/api/v2/auth/device/poll", handler.Paths);
        Assert.All(handler.BootstrapAuthorization, Assert.Null);
    }

    [Fact]
    public async Task NoDeploymentIdentityUsesOnlyOfferedUrl()
    {
        var handler = new ScriptedHandler { UnreachableHost = "example.test" };
        var offer = Offer();
        offer["serverEndpoints"] = new JsonArray(new JsonObject { ["url"] = "https://reachable.test" });
        await Assert.ThrowsAsync<SiloCastHandoffException>(() => Handoff(handler).PrepareAsync(offer, "PC", "id", _ => Task.CompletedTask, default));
        Assert.Equal(new[] { "example.test" }, handler.Hosts.Distinct());
    }

    [Fact]
    public async Task AbsoluteExpiryPreventsAuthenticatedRequestsAndRefresh()
    {
        var now = Now;
        var handler = new ScriptedHandler();
        using var identity = await new SiloCastHandoff(() => handler, () => now).PrepareAsync(Offer(), "PC", "id", _ => Task.CompletedTask, default);
        var requestsBefore = handler.Paths.Count;
        now = identity.ExpiresAt;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => identity.ApiClient.GetAsync<JsonObject>("/api/v2/watch/movie"));
        Assert.False(await identity.Auth.TryRefreshAsync());
        Assert.Equal(requestsBefore, handler.Paths.Count);
    }

    [Theory]
    [InlineData("allowed", false)]
    [InlineData("remote_playback_handoff", false)]
    public async Task RefusedCapabilityCannotStartDeviceRequest(string field, bool value)
    {
        var capability = JsonNode.Parse("""{"state":"available","remote_playback_handoff":true,"protocol_versions":[2],"cancel":true}""")!.AsObject();
        capability[field] = value;
        var handler = new ScriptedHandler { CapabilityResponse = capability.ToJsonString() };
        var error = await Assert.ThrowsAsync<SiloCastHandoffException>(() => Handoff(handler).PrepareAsync(Offer(), "PC", "id", _ => Task.CompletedTask, default));
        Assert.Equal("unsupported_server", error.Code);
        Assert.DoesNotContain("/api/v2/auth/device/start", handler.Paths);
    }

    [Fact]
    public async Task StartRequiresCreatedStatusBeforePublishingChallenge()
    {
        var handler = new ScriptedHandler { StartStatus = HttpStatusCode.OK };
        var challengeSent = false;
        await Assert.ThrowsAsync<SiloCastHandoffException>(() => Handoff(handler).PrepareAsync(Offer(), "PC", "id", _ => { challengeSent = true; return Task.CompletedTask; }, default));
        Assert.False(challengeSent);
    }

    [Fact]
    public async Task TransientPollFailureWaitsBeforeCollectingFreshApproval()
    {
        var handler = new ScriptedHandler { PollFailuresRemaining = 1 };
        var waits = new List<TimeSpan>();
        using var identity = await new SiloCastHandoff(() => handler, () => Now, (wait, _) => { waits.Add(wait); return Task.CompletedTask; })
            .PrepareAsync(Offer(), "PC", "id", _ => Task.CompletedTask, default);
        Assert.Equal(new[] { TimeSpan.FromSeconds(5) }, waits);
        Assert.Equal("phone-access", identity.ApiClient.AccessToken);
    }

    private static SiloCastHandoff Handoff(ScriptedHandler handler) => new(() => handler, () => Now);
    private static JsonObject Offer() => JsonNode.Parse("""{"requestId":"request-1","serverId":"aHR0cHM6Ly9leGFtcGxlLnRlc3Q","serverURL":" https://example.test/ ","serverName":"Server","profileId":"phone-profile","profileName":"Phone profile"}""")!.AsObject();

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public string PollResponse { get; set; } = Approved;
        public string CapabilityResponse { get; set; } = """{"state":"available","remote_playback_handoff":true,"protocol_versions":[2],"cancel":true}""";
        public HttpStatusCode StartStatus { get; set; } = HttpStatusCode.Created;
        public int PollFailuresRemaining { get; set; }
        public string Identity { get; set; } = "deployment-1";
        public string? UnreachableHost { get; set; }
        public bool RedirectCapability { get; set; }
        public Action? OnPoll { get; set; }
        public JsonObject? StartBody { get; private set; }
        public string? DeviceId { get; private set; }
        public string? PollDeviceId { get; private set; }
        public List<string> Paths { get; } = [];
        public List<string> Hosts { get; } = [];
        public List<string?> BootstrapAuthorization { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            Hosts.Add(request.RequestUri.Host);
            BootstrapAuthorization.Add(request.Headers.Authorization?.ToString());
            if (request.RequestUri.Host == UnreachableHost) throw new HttpRequestException("unreachable");
            if (path == "/api/v2/system/identity") return Json(new JsonObject { ["server_id"] = Identity }.ToJsonString());
            if (path == "/api/v2/auth/device/capability")
            {
                if (RedirectCapability) return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://other.test") } };
                return Json(CapabilityResponse);
            }
            if (path == "/api/v2/auth/device/start")
            {
                StartBody = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                DeviceId = request.Headers.GetValues("X-Silo-Device-Id").Single();
                return Json(Start, StartStatus);
            }
            if (path == "/api/v2/auth/device/poll")
            {
                PollDeviceId = request.Headers.TryGetValues("X-Silo-Device-Id", out var values) ? values.Single() : null;
                if (PollFailuresRemaining-- > 0) return Json("{}", HttpStatusCode.ServiceUnavailable);
                OnPoll?.Invoke(); return Json(PollResponse);
            }
            if (path == "/api/v2/auth/device/cancel") return Json("""{"status":"canceled"}""");
            throw new InvalidOperationException("Unexpected test request " + path);
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
