using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Wire contract for Configure › Network access (official internal/apiv2/network_access.go). Synthetic data only.</summary>
public sealed class AdminNetworkAccessApiTests
{
    internal const string Capabilities = """
        {"revision":"r1","state":"available","allowed":true,"providers":[
          {"provider":"stub","display_name":"Stub Overlay","installation_id":"7"},
          {"provider":"down","display_name":"Down Overlay","installation_id":"9"}]}
        """;
    internal const string StatusOk = """
        {"provider":"stub","hosts":[{"host":{"id":"api","role":"api","name":"Living Room"},"state":"disconnected",
          "hostname":"silo.overlay.example.test","origin":"https://silo.overlay.example.test","addresses":["127.0.0.1"],
          "provider_version":"stub 0.1.0","updated_at":"2026-01-02T03:04:05.678Z"}]}
        """;
    internal const string ConnectAccepted = """
        {"provider":"stub","hosts":[{"host":{"id":"api","role":"api","name":"Living Room"},"state":"awaiting_authorization",
          "hostname":"silo.overlay.example.test","origin":"https://silo.overlay.example.test","addresses":["127.0.0.1"],
          "auth_url":"https://login.example.test/a/fixture-enrollment-secret","provider_version":"stub 0.1.0","updated_at":"2026-01-02T03:04:05.678Z"},
          {"host":{"id":"node:10","role":"proxy","name":"Edge"},"state":"connected","addresses":[]}]}
        """;

    private static (AdminMonitorKit Kit, AdminMonitorAuthority Authority, MonitorAuthorityLease Lease, AdminNetworkAccessApi Api) Create()
    {
        var kit = AdminMonitorKit.Create();
        var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        return (kit, authority, authority.Capture(), new AdminNetworkAccessApi(kit.Client));
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string type) =>
        new(status) { Content = new StringContent($$"""{"type":"https://siloserver.org/docs/api/v2/problems/{{type}}","title":"x","status":{{(int)status}},"detail":"Fixture detail."}""", Encoding.UTF8, "application/problem+json") };

    [Fact]
    public async Task CapabilitiesParsePublishedProvidersInOrder()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(Capabilities));
            var result = await api.GetCapabilitiesAsync(lease);
            Assert.Equal("GET /api/v2/network-access/capabilities", Assert.Single(kit.Http.Requests));
            Assert.Equal("available", result.State);
            Assert.True(result.Allowed);
            Assert.Equal(["stub", "down"], result.Providers.Select(p => p.Provider));
            Assert.Equal(["Stub Overlay", "Down Overlay"], result.Providers.Select(p => p.DisplayName));
        }
    }

    [Theory]
    [InlineData("""{"state":"available","allowed":true,"providers":[{"provider":"stub","display_name":"A"},{"provider":"stub","display_name":"B"}]}""")]
    [InlineData("""{"state":"available","allowed":true,"providers":[{"provider":"../admin","display_name":"A"}]}""")]
    [InlineData("""{"state":"available","allowed":true}""")]
    [InlineData("""{"state":"available","allowed":true,"providers":[{"provider":"stub"}]}""")]
    public async Task InconsistentCapabilitiesAreRejectedRatherThanTrimmed(string body)
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body));
            await Assert.ThrowsAsync<InvalidDataException>(() => api.GetCapabilitiesAsync(lease));
        }
    }

    [Fact]
    public async Task TooManyProvidersIsAnExplicitLimitState()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            var providers = string.Join(",", Enumerable.Range(0, AdminNetworkAccessApi.MaxProviders + 1).Select(i => $$"""{"provider":"p{{i}}","display_name":"P{{i}}"}"""));
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json($$"""{"state":"available","allowed":true,"providers":[{{providers}}]}"""));
            var error = await Assert.ThrowsAsync<AdminNetworkAccessLimitException>(() => api.GetCapabilitiesAsync(lease));
            Assert.Equal(AdminNetworkAccessApi.MaxProviders, error.Limit);
            Assert.Equal(AdminFailureKind.InvalidResponse, AdminFailures.Classify(error));
        }
    }

    [Fact]
    public async Task StatusReadsTheEncodedProviderAndKeepsEveryPublishedField()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(StatusOk.Replace("\"stub\"", "\"my.over-lay_1\"")));
            var status = await api.GetStatusAsync(lease, "my.over-lay_1");
            Assert.Equal("GET /api/v2/admin/network-access/my.over-lay_1/status", Assert.Single(kit.Http.Requests));
            var host = Assert.Single(status.Hosts);
            Assert.Equal(("api", "api", "Living Room"), (host.Host.Id, host.Host.Role, host.Host.Name));
            Assert.Equal("disconnected", host.State);
            Assert.Equal("https://silo.overlay.example.test", host.Origin);
            Assert.Equal("silo.overlay.example.test", host.Hostname);
            Assert.Equal(["127.0.0.1"], host.Addresses);
            Assert.Equal("stub 0.1.0", host.ProviderVersion);
            Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero), host.UpdatedAt);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("../users")]
    [InlineData("Tailscale")]
    [InlineData("a b")]
    [InlineData("stub/connect")]
    public async Task UnsafeProviderSlugIsNeverSent(string provider)
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => api.GetStatusAsync(lease, provider));
            await Assert.ThrowsAsync<ArgumentException>(() => api.ConnectAsync(lease, provider, "api"));
            Assert.Empty(kit.Http.Requests);
        }
    }

    [Theory]
    [InlineData("""{"provider":"other","hosts":[]}""")]
    [InlineData("""{"provider":"stub"}""")]
    [InlineData("""{"provider":"stub","hosts":[{"host":{"id":"api","role":"api","name":"A"},"state":"connected","addresses":[]},{"host":{"id":"api","role":"api","name":"B"},"state":"connected","addresses":[]}]}""")]
    [InlineData("""{"provider":"stub","hosts":[{"host":{"id":"","role":"api","name":"A"},"state":"connected","addresses":[]}]}""")]
    [InlineData("""{"provider":"stub","hosts":[{"host":{"id":"api","role":"api","name":"A"},"addresses":[]}]}""")]
    [InlineData("""{"provider":"stub","hosts":[{"state":"connected","addresses":[]}]}""")]
    public async Task MismatchedOrInconsistentStatusIsRejected(string body)
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body));
            await Assert.ThrowsAsync<InvalidDataException>(() => api.GetStatusAsync(lease, "stub"));
        }
    }

    [Fact]
    public async Task TooManyHostsIsAnExplicitLimitState()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            var hosts = string.Join(",", Enumerable.Range(0, AdminNetworkAccessApi.MaxHosts + 1).Select(i => $$"""{"host":{"id":"node:{{i}}","role":"proxy","name":"N"},"state":"connected","addresses":[]}"""));
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json($$"""{"provider":"stub","hosts":[{{hosts}}]}"""));
            var error = await Assert.ThrowsAsync<AdminNetworkAccessLimitException>(() => api.GetStatusAsync(lease, "stub"));
            Assert.Equal(AdminNetworkAccessApi.MaxHosts, error.Limit);
        }
    }

    [Fact]
    public async Task OversizedResponseIsRejectedBeforeParsing()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            var padding = new string(' ', AdminNetworkAccessApi.MaxResponseBytes + 1);
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"provider\":\"stub\",\"hosts\":[]" + padding + "}"));
            await Assert.ThrowsAsync<InvalidDataException>(() => api.GetStatusAsync(lease, "stub"));
        }
    }

    [Fact]
    public async Task UnpublishedStateIsKeptForDisplayAndEnrollmentUrlNeverReachesText()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(ConnectAccepted.Replace("\"connected\"", "\"warming\"")));
            var status = await api.GetStatusAsync(lease, "stub");
            Assert.Equal("warming", status.Hosts[1].State);
            var waiting = status.Hosts[0];
            Assert.Equal("https://login.example.test/a/fixture-enrollment-secret", waiting.AuthUrl);
            Assert.DoesNotContain("fixture-enrollment-secret", waiting.ToString());
            Assert.DoesNotContain("fixture-enrollment-secret", status.ToString());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CommandPostsExactlyTheCapturedHostOnce(bool connect)
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(ConnectAccepted, HttpStatusCode.Accepted));
            var result = connect ? await api.ConnectAsync(lease, "stub", "api") : await api.DisconnectAsync(lease, "stub", "api");
            Assert.Equal($"POST /api/v2/admin/network-access/stub/{(connect ? "connect" : "disconnect")}", Assert.Single(kit.Http.Requests));
            Assert.Equal("""{"hosts":["api"]}""", Assert.Single(kit.Http.Bodies));
            Assert.Equal(2, result.Hosts.Count);
        }
    }

    [Fact]
    public async Task CommandRejectsAnEmptyHostWithoutSending()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => api.ConnectAsync(lease, "stub", ""));
            await Assert.ThrowsAsync<ArgumentException>(() => api.DisconnectAsync(lease, "stub", "  "));
            Assert.Empty(kit.Http.Requests);
        }
    }

    [Fact]
    public async Task UnauthorizedCommandIsNotReplayedThroughTokenRefresh()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(Problem(HttpStatusCode.Unauthorized, "authentication_required"));
            var error = await Assert.ThrowsAsync<ApiException>(() => api.ConnectAsync(lease, "stub", "api"));
            Assert.Equal(AdminFailureKind.Unauthorized, AdminFailures.Classify(error, lease));
            Assert.Single(kit.Http.Requests);
            Assert.DoesNotContain("Fixture detail", error.Message);
        }
    }

    [Fact]
    public async Task TransportFailureAfterSendIsAnUnknownOutcomeAndNotRetried()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection reset"));
            await Assert.ThrowsAsync<AdminNetworkAccessOutcomeUnknownException>(() => api.DisconnectAsync(lease, "stub", "api"));
            Assert.Single(kit.Http.Requests);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task AmbiguousServerFailureIsAnUnknownOutcome(HttpStatusCode status)
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(Problem(status, "internal"));
            await Assert.ThrowsAsync<AdminNetworkAccessOutcomeUnknownException>(() => api.ConnectAsync(lease, "stub", "api"));
            Assert.Single(kit.Http.Requests);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"provider":"other","hosts":[]}""")]
    [InlineData("""{"provider":"stub","hosts":[{"host":{"id":"node:10","role":"proxy","name":"Edge"},"state":"connected","addresses":[]}]}""")]
    public async Task AcknowledgementThatCannotConfirmTheHostIsAnUnknownOutcome(string body)
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json(body, HttpStatusCode.Accepted));
            await Assert.ThrowsAsync<AdminNetworkAccessOutcomeUnknownException>(() => api.ConnectAsync(lease, "stub", "api"));
        }
    }

    [Fact]
    public async Task DefiniteRejectionKeepsItsClassification()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority) using (lease)
        {
            kit.Http.Respond = (_, _) => Task.FromResult(Problem(HttpStatusCode.UnprocessableEntity, "validation_failed"));
            var error = await Assert.ThrowsAsync<ApiException>(() => api.ConnectAsync(lease, "stub", "node:404"));
            Assert.Equal(AdminFailureKind.Validation, AdminFailures.Classify(error, lease));
            kit.Http.Respond = (_, _) => Task.FromResult(Problem(HttpStatusCode.NotFound, "not_found"));
            error = await Assert.ThrowsAsync<ApiException>(() => api.GetStatusAsync(lease, "stub"));
            Assert.Equal(AdminFailureKind.NotFound, AdminFailures.Classify(error, lease));
        }
    }

    [Fact]
    public async Task RetiredLeaseNeverSends()
    {
        var (kit, authority, lease, api) = Create();
        using (kit) using (authority)
        {
            lease.Dispose();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.ConnectAsync(lease, "stub", "api"));
            Assert.Empty(kit.Http.Requests);
        }
    }
}
