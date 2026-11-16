using System.Collections.Concurrent;
using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Auth;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Behaviour of the page-owned Network access state (official networkAccess.ts hooks). Synthetic data only.</summary>
public sealed class AdminNetworkAccessControllerTests
{
    private sealed class Fixture : IDisposable
    {
        public readonly AdminMonitorKit Kit = AdminMonitorKit.Create();
        public readonly AdminMonitorAuthority Authority;
        public readonly DashboardManualTime Time = new();
        public readonly AdminNetworkAccessController Controller;
        public readonly ConcurrentDictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> Routes = new();
        public int InFlight, MaxInFlight;
        public bool Defer;

        public Fixture(string capabilities = AdminNetworkAccessApiTests.Capabilities)
        {
            Authority = new AdminMonitorAuthority(Kit.Client, Kit.Auth);
            Routes["GET /api/v2/network-access/capabilities"] = (_, _) => Json(capabilities);
            Routes["GET /api/v2/admin/network-access/stub/status"] = (_, _) => Json(AdminNetworkAccessApiTests.StatusOk);
            Routes["GET /api/v2/admin/network-access/down/status"] = (_, _) => Json(Unavailable);
            Kit.Http.Respond = async (request, ct) =>
            {
                var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
                var status = key.EndsWith("/status", StringComparison.Ordinal);
                if (status) { var now = Interlocked.Increment(ref InFlight); lock (Routes) MaxInFlight = Math.Max(MaxInFlight, now); }
                try { return Routes.TryGetValue(key, out var route) ? await route(request, ct) : AdminMonitorKit.Json("{}", HttpStatusCode.NotFound); }
                finally { if (status) Interlocked.Decrement(ref InFlight); }
            };
            Controller = new AdminNetworkAccessController(new AdminNetworkAccessApi(Kit.Client), Authority, Time, () => Defer);
        }

        public static Task<HttpResponseMessage> Json(string body, HttpStatusCode status = HttpStatusCode.OK) => Task.FromResult(AdminMonitorKit.Json(body, status));
        public int Count(string request) => Kit.Http.Requests.Count(r => r == request);
        public void Dispose() { Controller.Dispose(); Authority.Dispose(); Kit.Dispose(); }
    }

    private const string Unavailable = """
        {"provider":"down","hosts":[{"host":{"id":"api","role":"api","name":"Living Room"},"state":"unavailable","addresses":[],
          "error":"plugin process is backoff: plugin process exited"}]}
        """;
    private const string StubStatus = "GET /api/v2/admin/network-access/stub/status";
    private const string DownStatus = "GET /api/v2/admin/network-access/down/status";
    private const string CapabilitiesRead = "GET /api/v2/network-access/capabilities";

    private static async Task Until(Func<bool> condition, string label)
    {
        bool Met() { try { return condition(); } catch (InvalidOperationException) { return false; } }
        for (var i = 0; i < 400 && !Met(); i++) await Task.Delay(5);
        Assert.True(Met(), "Timed out: " + label);
    }

    private static AdminNetworkAccessProviderState Provider(AdminNetworkAccessController controller, string id) =>
        controller.State.Providers.Single(p => p.Summary.Provider == id);

    private static async Task<Fixture> OpenedAsync(bool active = true)
    {
        var fixture = new Fixture();
        fixture.Controller.SetActive(active);
        Assert.True(fixture.Controller.Open());
        await Until(() => fixture.Controller.State.Providers.Count == 2 && fixture.Controller.State.Providers.All(p => p.Status != null), "initial load");
        return fixture;
    }

    [Fact]
    public async Task OpenReadsCapabilitiesThenEveryProviderStatus()
    {
        using var fixture = await OpenedAsync();
        var state = fixture.Controller.State;
        Assert.False(state.CapabilitiesLoading);
        Assert.Equal(["stub", "down"], state.Providers.Select(p => p.Summary.Provider));
        Assert.Equal("disconnected", Provider(fixture.Controller, "stub").Status!.Hosts.Single().State);
        Assert.Equal("unavailable", Provider(fixture.Controller, "down").Status!.Hosts.Single().State);
        Assert.Equal(1, fixture.Count(CapabilitiesRead));
        Assert.Equal(1, fixture.Count(StubStatus));
        Assert.Equal(1, fixture.Count(DownStatus));
    }

    [Fact]
    public async Task NoProvidersIsAnEmptyLoadedState()
    {
        using var fixture = new Fixture("""{"state":"not_configured","allowed":false,"providers":[]}""");
        fixture.Controller.SetActive(true);
        fixture.Controller.Open();
        await Until(() => fixture.Controller.State.CapabilitiesLoaded, "empty capabilities");
        Assert.Empty(fixture.Controller.State.Providers);
        Assert.Null(fixture.Controller.State.CapabilitiesFailure);
        fixture.Time.Advance(TimeSpan.FromMinutes(2));
        await Task.Delay(30);
        Assert.Equal(1, fixture.Kit.Http.Requests.Count);
    }

    [Fact]
    public async Task OneProviderFailureLeavesTheOthersIntact()
    {
        var fixture = new Fixture();
        using (fixture)
        {
            fixture.Routes[DownStatus] = (_, _) => Fixture.Json("""{"type":"https://siloserver.org/docs/api/v2/problems/not_found","title":"x","status":404}""", HttpStatusCode.NotFound);
            fixture.Controller.SetActive(true);
            fixture.Controller.Open();
            await Until(() => Provider(fixture.Controller, "down").Failure != null && Provider(fixture.Controller, "stub").Status != null, "isolated failure");
            Assert.Equal(AdminFailureKind.NotFound, Provider(fixture.Controller, "down").Failure);
            Assert.Null(Provider(fixture.Controller, "stub").Failure);
            Assert.Single(Provider(fixture.Controller, "stub").Status!.Hosts);
        }
    }

    [Fact]
    public async Task CapabilitiesFailureIsReportedAndRetriedOnlyOnRequest()
    {
        using var fixture = new Fixture();
        fixture.Routes[CapabilitiesRead] = (_, _) => Fixture.Json("{}", HttpStatusCode.ServiceUnavailable);
        fixture.Controller.SetActive(true);
        fixture.Controller.Open();
        await Until(() => fixture.Controller.State.CapabilitiesFailure != null, "capabilities failure");
        Assert.Equal(AdminFailureKind.Server, fixture.Controller.State.CapabilitiesFailure);
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(30);
        Assert.Equal(1, fixture.Count(CapabilitiesRead));
        fixture.Routes[CapabilitiesRead] = (_, _) => Fixture.Json(AdminNetworkAccessApiTests.Capabilities);
        await fixture.Controller.RetryCapabilitiesAsync();
        await Until(() => fixture.Controller.State.Providers.Count == 2 && fixture.Controller.State.Providers.All(p => p.Status != null), "retry");
        Assert.Null(fixture.Controller.State.CapabilitiesFailure);
    }

    [Fact]
    public async Task AtMostTwoStatusReadsRunConcurrently()
    {
        var providers = string.Join(",", Enumerable.Range(0, 5).Select(i => $$"""{"provider":"p{{i}}","display_name":"P{{i}}"}"""));
        using var fixture = new Fixture($$"""{"state":"available","allowed":true,"providers":[{{providers}}]}""");
        var gates = Enumerable.Range(0, 5).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            fixture.Routes[$"GET /api/v2/admin/network-access/p{i}/status"] = async (_, ct) =>
            {
                await gates[index].Task.WaitAsync(ct);
                return AdminMonitorKit.Json($$"""{"provider":"p{{index}}","hosts":[]}""");
            };
        }
        fixture.Controller.SetActive(true);
        fixture.Controller.Open();
        await Until(() => fixture.InFlight == 2, "two reads started");
        await Task.Delay(40);
        Assert.Equal(2, fixture.InFlight);
        foreach (var gate in gates) gate.SetResult();
        await Until(() => fixture.Controller.State.Providers.All(p => p.Status != null), "all statuses");
        Assert.Equal(2, fixture.MaxInFlight);
        Assert.Equal(5, fixture.Kit.Http.Requests.Count(r => r.EndsWith("/status", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ActivePagePollsEveryFifteenSecondsAndHiddenPageNeverDoes()
    {
        using var fixture = await OpenedAsync();
        Assert.True(fixture.Controller.IsPolling);
        fixture.Time.Advance(TimeSpan.FromSeconds(14));
        await Task.Delay(30);
        Assert.Equal(1, fixture.Count(StubStatus));
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await Until(() => fixture.Count(StubStatus) == 2 && fixture.Count(DownStatus) == 2, "first poll");
        Assert.Equal(1, fixture.Count(CapabilitiesRead));

        fixture.Controller.SetActive(false);
        Assert.False(fixture.Controller.IsPolling);
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(40);
        Assert.Equal(2, fixture.Count(StubStatus));
        Assert.Equal(1, fixture.Count(CapabilitiesRead));

        // Genuine activation refreshes only what is stale: statuses (15 s) and capabilities (30 s).
        fixture.Controller.SetActive(true);
        await Until(() => fixture.Count(StubStatus) == 3 && fixture.Count(CapabilitiesRead) == 2, "stale activation refresh");
        fixture.Controller.SetActive(false);
        fixture.Controller.SetActive(true);
        await Task.Delay(40);
        Assert.Equal(3, fixture.Count(StubStatus));
        Assert.Equal(2, fixture.Count(CapabilitiesRead));
    }

    [Fact]
    public async Task InitialLoadWhileInactiveDoesNotStartPolling()
    {
        using var fixture = await OpenedAsync(active: false);
        Assert.False(fixture.Controller.IsPolling);
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(30);
        Assert.Equal(1, fixture.Count(StubStatus));
    }

    [Fact]
    public async Task ForegroundPlaybackOrNavigationDefersAPollTick()
    {
        using var fixture = await OpenedAsync();
        fixture.Defer = true;
        fixture.Time.Advance(TimeSpan.FromSeconds(15));
        await Task.Delay(40);
        Assert.Equal(1, fixture.Count(StubStatus));
        fixture.Defer = false;
        fixture.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => fixture.Count(StubStatus) == 2, "resumed poll");
    }

    [Fact]
    public async Task SlowPollIsCoalescedRatherThanStacked()
    {
        using var fixture = await OpenedAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes[StubStatus] = async (_, ct) => { await gate.Task.WaitAsync(ct); return AdminMonitorKit.Json(AdminNetworkAccessApiTests.StatusOk); };
        fixture.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => fixture.Count(StubStatus) == 2, "slow poll started");
        fixture.Time.Advance(TimeSpan.FromSeconds(45));
        await Task.Delay(40);
        Assert.Equal(2, fixture.Count(StubStatus));
        gate.SetResult();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CommandSendsOneCapturedHostMarksOnlyItBusyAndAppliesTheAcknowledgement(bool connect)
    {
        using var fixture = await OpenedAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var kind = connect ? "connect" : "disconnect";
        fixture.Routes[$"POST /api/v2/admin/network-access/stub/{kind}"] = async (_, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return AdminMonitorKit.Json(AdminNetworkAccessApiTests.ConnectAccepted, HttpStatusCode.Accepted);
        };
        var task = connect ? fixture.Controller.ConnectAsync("stub", "api") : fixture.Controller.DisconnectAsync("stub", "api");
        await Until(() => Provider(fixture.Controller, "stub").BusyHosts.Contains("api"), "busy");
        Assert.Empty(Provider(fixture.Controller, "down").BusyHosts);
        var duplicate = connect ? fixture.Controller.ConnectAsync("stub", "api") : fixture.Controller.DisconnectAsync("stub", "api");
        await duplicate;
        gate.SetResult();
        await task;
        Assert.Equal(1, fixture.Count($"POST /api/v2/admin/network-access/stub/{kind}"));
        Assert.Contains("""{"hosts":["api"]}""", fixture.Kit.Http.Bodies);
        await Until(() => fixture.Count(StubStatus) == 2, "post-command refresh");
        var stub = Provider(fixture.Controller, "stub");
        Assert.Empty(stub.BusyHosts);
        Assert.NotNull(stub.Status);
    }

    [Fact]
    public async Task AcknowledgedResultIsShownBeforeTheFollowUpRead()
    {
        using var fixture = await OpenedAsync();
        var refresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes["POST /api/v2/admin/network-access/stub/connect"] = (_, _) => Fixture.Json(AdminNetworkAccessApiTests.ConnectAccepted, HttpStatusCode.Accepted);
        fixture.Routes[StubStatus] = async (_, ct) => { await refresh.Task.WaitAsync(ct); return AdminMonitorKit.Json(AdminNetworkAccessApiTests.ConnectAccepted); };
        await fixture.Controller.ConnectAsync("stub", "api");
        var host = Provider(fixture.Controller, "stub").Status!.Hosts.Single(h => h.Host.Id == "api");
        Assert.Equal("awaiting_authorization", host.State);
        Assert.True(fixture.Controller.TryGetEnrollmentUri("stub", "api", out var uri));
        Assert.Equal("login.example.test", uri.Host);
        refresh.SetResult();
    }

    [Fact]
    public async Task OlderInFlightReadCannotOverwriteAnAcknowledgedCommand()
    {
        using var fixture = await OpenedAsync();
        var stale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes[StubStatus] = async (_, _) => { await stale.Task; return AdminMonitorKit.Json(AdminNetworkAccessApiTests.StatusOk); };
        fixture.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => fixture.Count(StubStatus) == 2, "stale poll in flight");
        var fresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes[StubStatus] = async (_, ct) => { await fresh.Task.WaitAsync(ct); return AdminMonitorKit.Json(AdminNetworkAccessApiTests.ConnectAccepted); };
        fixture.Routes["POST /api/v2/admin/network-access/stub/connect"] = (_, _) => Fixture.Json(AdminNetworkAccessApiTests.ConnectAccepted, HttpStatusCode.Accepted);
        await fixture.Controller.ConnectAsync("stub", "api");
        stale.SetResult();
        await Task.Delay(40);
        Assert.Equal("awaiting_authorization", Provider(fixture.Controller, "stub").Status!.Hosts.Single(h => h.Host.Id == "api").State);
        fresh.SetResult();
    }

    [Fact]
    public async Task UnknownCommandOutcomeReconcilesByReadingStatusAndNeverResends()
    {
        using var fixture = await OpenedAsync();
        AdminNetworkAccessCommandFeedback? feedback = null;
        fixture.Controller.CommandFailed += value => feedback = value;
        fixture.Routes["POST /api/v2/admin/network-access/stub/connect"] = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("reset"));
        await fixture.Controller.ConnectAsync("stub", "api");
        await Until(() => fixture.Count(StubStatus) == 2, "reconciliation read");
        Assert.Equal(1, fixture.Count("POST /api/v2/admin/network-access/stub/connect"));
        Assert.Equal(AdminNetworkAccessCommandOutcome.Unknown, feedback?.Outcome);
        Assert.Empty(Provider(fixture.Controller, "stub").BusyHosts);
    }

    [Fact]
    public async Task UnauthorizedCommandIsNotRetriedAndKeepsTheLastKnownState()
    {
        using var fixture = await OpenedAsync();
        AdminNetworkAccessCommandFeedback? feedback = null;
        fixture.Controller.CommandFailed += value => feedback = value;
        fixture.Routes["POST /api/v2/admin/network-access/stub/disconnect"] = (_, _) =>
            Fixture.Json("""{"type":"https://siloserver.org/docs/api/v2/problems/authentication_required","title":"x","status":401}""", HttpStatusCode.Unauthorized);
        await fixture.Controller.DisconnectAsync("stub", "api");
        Assert.Equal(1, fixture.Count("POST /api/v2/admin/network-access/stub/disconnect"));
        Assert.Equal(AdminNetworkAccessCommandOutcome.Rejected, feedback?.Outcome);
        Assert.Equal(AdminFailureKind.Unauthorized, feedback?.Failure);
        Assert.Equal(AdminNetworkAccessCommandKind.Disconnect, feedback?.Kind);
        Assert.Equal("disconnected", Provider(fixture.Controller, "stub").Status!.Hosts.Single().State);
        await Task.Delay(30);
        Assert.Equal(1, fixture.Count(StubStatus));
    }

    [Fact]
    public async Task UnknownHostRejectionRefreshesTheStaleHostList()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes["POST /api/v2/admin/network-access/stub/connect"] = (_, _) =>
            Fixture.Json("""{"type":"https://siloserver.org/docs/api/v2/problems/validation_failed","title":"x","status":422}""", HttpStatusCode.UnprocessableEntity);
        await fixture.Controller.ConnectAsync("stub", "api");
        await Until(() => fixture.Count(StubStatus) == 2, "stale host refresh");
        Assert.Equal(1, fixture.Count("POST /api/v2/admin/network-access/stub/connect"));
    }

    [Fact]
    public async Task CommandsForAHostNotOnTheCurrentListAreIgnored()
    {
        using var fixture = await OpenedAsync();
        await fixture.Controller.ConnectAsync("stub", "node:99");
        await fixture.Controller.ConnectAsync("missing", "api");
        Assert.DoesNotContain(fixture.Kit.Http.Requests, r => r.StartsWith("POST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetirementDuringACommandDropsItsResultAndClearsEnrollmentData()
    {
        using var fixture = await OpenedAsync();
        fixture.Routes[StubStatus] = (_, _) => Fixture.Json(AdminNetworkAccessApiTests.ConnectAccepted);
        fixture.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => fixture.Controller.TryGetEnrollmentUri("stub", "api", out _), "enrollment visible");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes["POST /api/v2/admin/network-access/stub/disconnect"] = async (_, _) =>
        {
            await gate.Task;
            return AdminMonitorKit.Json(AdminNetworkAccessApiTests.StatusOk, HttpStatusCode.Accepted);
        };
        var retired = 0;
        AdminNetworkAccessCommandFeedback? feedback = null;
        fixture.Controller.AuthorityRetired += () => retired++;
        fixture.Controller.CommandFailed += value => feedback = value;
        var command = fixture.Controller.DisconnectAsync("stub", "api");
        await Until(() => Provider(fixture.Controller, "stub").BusyHosts.Count == 1, "command pending");
        fixture.Kit.Auth.SetCurrentUser(new UserInfo { Id = "1", Username = "synthetic", Role = "user" });
        await Until(() => retired == 1, "retired");
        Assert.Empty(fixture.Controller.State.Providers);
        Assert.False(fixture.Controller.TryGetEnrollmentUri("stub", "api", out _));
        gate.SetResult();
        await command;
        Assert.Empty(fixture.Controller.State.Providers);
        Assert.Null(feedback);
        Assert.False(fixture.Controller.IsPolling);
    }

    [Fact]
    public async Task SuspendCancelsPendingReadsAndStopsPolling()
    {
        using var fixture = await OpenedAsync();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Routes[StubStatus] = async (_, ct) =>
        {
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return AdminMonitorKit.Json("{}");
        };
        fixture.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => fixture.Count(StubStatus) == 2, "pending poll");
        fixture.Controller.Suspend();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(fixture.Controller.IsPolling);
        Assert.Empty(fixture.Controller.State.Providers);
        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(30);
        Assert.Equal(2, fixture.Count(StubStatus));
        fixture.Controller.Resume();
        Assert.True(fixture.Controller.Open());
        await Until(() => fixture.Count(CapabilitiesRead) == 2, "reopened");
    }

    [Fact]
    public async Task EnrollmentLinkRequiresACurrentWaitingHostWithASafeWebUrl()
    {
        using var fixture = await OpenedAsync();
        Assert.False(fixture.Controller.TryGetEnrollmentUri("stub", "api", out _));
        fixture.Routes[StubStatus] = (_, _) => Fixture.Json(AdminNetworkAccessApiTests.ConnectAccepted.Replace("https://login.example.test/a/fixture-enrollment-secret", "javascript:alert(1)"));
        fixture.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => Provider(fixture.Controller, "stub").Status!.Hosts.Count == 2, "unsafe url status");
        Assert.False(fixture.Controller.TryGetEnrollmentUri("stub", "api", out _));
        fixture.Routes[StubStatus] = (_, _) => Fixture.Json(AdminNetworkAccessApiTests.ConnectAccepted.Replace("\"awaiting_authorization\"", "\"connected\""));
        fixture.Time.Advance(TimeSpan.FromSeconds(15));
        await Until(() => fixture.Count(StubStatus) == 3 && Provider(fixture.Controller, "stub").Status!.Hosts[0].State == "connected", "connected status");
        Assert.False(fixture.Controller.TryGetEnrollmentUri("stub", "api", out _));
        Assert.True(fixture.Controller.TryGetOriginUri("stub", "api", out var origin));
        Assert.Equal("https://silo.overlay.example.test/", origin.ToString());
    }

    [Fact]
    public async Task OverLimitHostListIsAnExplicitProviderState()
    {
        using var fixture = new Fixture();
        var hosts = string.Join(",", Enumerable.Range(0, AdminNetworkAccessApi.MaxHosts + 1).Select(i => $$"""{"host":{"id":"node:{{i}}","role":"proxy","name":"N"},"state":"connected","addresses":[]}"""));
        fixture.Routes[StubStatus] = (_, _) => Fixture.Json($$"""{"provider":"stub","hosts":[{{hosts}}]}""");
        fixture.Controller.SetActive(true);
        fixture.Controller.Open();
        await Until(() => Provider(fixture.Controller, "stub").Failure != null, "limit");
        Assert.Equal(AdminNetworkAccessApi.MaxHosts, Provider(fixture.Controller, "stub").Limit);
        Assert.NotNull(Provider(fixture.Controller, "down").Status);
    }
}
