using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using R = SiloPlayer.Core.Services.Admin.AdminDirectConnectionsRules;
using P = SiloPlayer.Core.Services.Admin.AdminDirectConnectionsPresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// Direct connections wording, state mapping, visibility, polling order and STUN rules. The presentation cases are the official
/// <c>DirectConnectionsSection.test.tsx</c> (Silo Server c90e406db71ee468bffdfa5733cd97d1d50b4019) case by case, on synthetic data.
/// </summary>
public sealed class AdminDirectConnectionsRulesTests
{
    private static AdminDirectConnectionsKnob Congestion(string effective = "bbr", string? requested = null, string? reason = null) =>
        new("SILO_NET_TCP_CONGESTION", "bbr", effective, requested, reason, ["bbr", "off"]);
    private static AdminDirectConnectionsKnob SendBuffer(string effective = "33554432", string? requested = null, string? reason = null) =>
        new("SILO_NET_TCP_SEND_BUFFER", "33554432", effective, requested, reason, []);

    private static AdminDirectConnectionsFeature NetAdmin(string reason) => new("cap_net_admin", "off", reason, null, null, null);

    private static AdminDirectConnectionsListener Listener(AdminDirectConnectionsSendBuffer buffer, string requested = "bbr", string granted = "bbr", string state = "on", string reason = "", string? errno = null) =>
        new("api", requested, granted, state, reason, errno, buffer);

    private static AdminDirectConnectionsHostView Show(IEnumerable<AdminDirectConnectionsFeature>? features = null, IEnumerable<AdminDirectConnectionsListener>? listeners = null,
        IEnumerable<AdminDirectConnectionsKnob>? knobs = null) =>
        P.Present(new("api", "api", "API", new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero), (features ?? []).ToArray(), (listeners ?? []).ToArray(), (knobs ?? []).ToArray()), at => at.ToString("u"));

    private static AdminDirectConnectionsRowView Row(IEnumerable<AdminDirectConnectionsRowView> rows, string label) => rows.Single(row => row.Label == label);
    private static IEnumerable<string> AllText(AdminDirectConnectionsHostView view) =>
        view.Features.Concat(view.Listeners).Concat(view.Knobs).SelectMany(row => row.Details.Append(row.Value).Append(row.Label));

    // ── host tuning values ───────────────────────────────

    [Fact]
    public void AlgorithmNamesAreCapitalsAndByteSizesAreFormatted()
    {
        var view = Show(knobs: [Congestion(), SendBuffer()]);
        var algorithm = Row(view.Knobs, "TCP congestion control");
        Assert.Equal("Effective: BBR", algorithm.Value);
        Assert.Contains("Default: BBR", algorithm.Details);
        Assert.Contains("Allowed: BBR, Off", algorithm.Details);
        Assert.Equal("SILO_NET_TCP_CONGESTION", algorithm.Variable);
        var buffer = Row(view.Knobs, "TCP send buffer");
        Assert.Equal("Effective: 32.0 MB", buffer.Value);
        Assert.Contains("Default: 32.0 MB", buffer.Details);
        Assert.DoesNotContain(AllText(view), text => text.Contains("33554432"));
    }

    [Fact]
    public void OffReadsAsOffAndAnUnparsedRequestStaysAsSent()
    {
        var view = Show(knobs: [Congestion("off", "bbrr", "invalid"), SendBuffer("off", "lots", "invalid")]);
        var algorithm = Row(view.Knobs, "TCP congestion control");
        Assert.Equal("Effective: Off", algorithm.Value);
        Assert.Contains("Requested: bbrr", algorithm.Details);
        Assert.Contains("Invalid value; using off", algorithm.Details);
        var buffer = Row(view.Knobs, "TCP send buffer");
        Assert.Equal("Effective: Off", buffer.Value);
        Assert.Contains("Requested: lots", buffer.Details);
    }

    [Fact]
    public void AVariableWithNoLabelShowsByItsNameAlone()
    {
        var view = Show(knobs: [new("SILO_NET_FUTURE_KNOB", "a", "a", null, null, [])]);
        var row = Assert.Single(view.Knobs);
        Assert.Equal("", row.Label);
        Assert.Equal("SILO_NET_FUTURE_KNOB", row.Variable);
        Assert.Equal("Effective: a", row.Value);
    }

    // ── states that need no action ───────────────────────

    [Fact]
    public void ASendBufferThatNeedsNoIncreaseIsNotNeededWithoutAReasonLine()
    {
        var view = Show(listeners: [Listener(new("off", "not_needed", 0, 0, null))]);
        var details = Assert.Single(view.Listeners).Details;
        Assert.Contains("Send buffer: Not needed", details);
        Assert.DoesNotContain("No increase needed", details);
        Assert.DoesNotContain("Send buffer: Off", details);
        Assert.DoesNotContain(details, line => line.StartsWith("Requested:"));
    }

    [Fact]
    public void ADroppedNetAdminCapabilityIsNotNeededWhenNoBufferNeededIt()
    {
        var view = Show(features: [NetAdmin("net_admin_dropped"), new("socket_send_buffer", "on", null, "33554432", "33554432", null)]);
        var capability = Row(view.Features, "NET_ADMIN capability");
        Assert.Equal("Not needed", capability.Value);
        Assert.Empty(capability.Details);
        Assert.DoesNotContain(AllText(view), text => text is "NET_ADMIN capability dropped" or "NET_ADMIN capability missing");
    }

    [Fact]
    public void AMissingNetAdminCapabilityStaysOffWhenABufferNeededItAndTheReasonStaysOnTheBuffer()
    {
        var view = Show(features: [NetAdmin("no_cap_net_admin"), new("socket_send_buffer", "capped", "no_cap_net_admin", "33554432", "212992", null)]);
        var capability = Row(view.Features, "NET_ADMIN capability");
        Assert.Equal("Off", capability.Value);
        Assert.Empty(capability.Details);
        Assert.Contains("NET_ADMIN capability missing", Row(view.Features, "Socket send buffer").Details);
    }

    [Fact]
    public void AMissingNetAdminCapabilityStaysOffWhenOnlyAListenerSendBufferNeededIt()
    {
        var view = Show(features: [NetAdmin("no_cap_net_admin"), new("socket_send_buffer", "on", null, null, null, null)],
            listeners: [Listener(new("capped", "no_net_admin", 33554432, 212992, null))]);
        var capability = Row(view.Features, "NET_ADMIN capability");
        Assert.Equal("Off", capability.Value);
        Assert.NotEqual("Not needed", capability.Value);
        Assert.Empty(capability.Details);
        Assert.Single(AllText(view), "NET_ADMIN capability missing");
    }

    // ── state, reason, size and detail wording ───────────

    [Theory]
    [InlineData("on", "On")]
    [InlineData("off", "Off")]
    [InlineData("capped", "Capped")]
    [InlineData("fallback", "Fallback")]
    [InlineData("degraded", "Degraded")]
    [InlineData("unknown", "Unknown")]
    [InlineData("half_open", "half open")]
    public void StatesReadInPlainWordsAndAnUnknownStateShowsAsSent(string state, string shown) => Assert.Equal(shown, P.StateLabel(state));

    [Theory]
    [InlineData("disabled", "Disabled")]
    [InlineData("admin_off", "Disabled by administrator")]
    [InlineData("probe_failed", "Check failed")]
    [InlineData("not_available", "Not available")]
    [InlineData("not_measured", "Not measured")]
    [InlineData("no_gateway", "No router found")]
    [InlineData("mapping_varies", "Public address varies by destination")]
    [InlineData("granted_below_request", "Buffer is smaller than requested")]
    [InlineData("replica_lease_held", "replica lease held")]
    public void ReasonsReadInPlainWordsAndAnUnknownReasonShowsAsSent(string reason, string shown) => Assert.Equal(shown, P.ReasonLabel(reason));

    [Fact]
    public void FeatureRowsCarryTheirLabelStateReasonSizesAndDetail()
    {
        var view = Show(features:
        [
            new("direct", "degraded", "probe_failed", null, null, null),
            new("ipv4", "on", null, null, null, "192.0.2.10"),
            new("nat_mapping", "off", "not_available", null, null, "192.0.2.10:4000"),
            new("reachability", "unknown", "not_measured", null, null, "192.0.2.10"),
            new("port_mapping", "on", null, null, null, "upnp,pmp,xyz"),
            new("socket_receive_buffer", "capped", "limit", "33554432", null, null),
            new("gso", "on", null, null, null, null),
            new("future_feature", "weird_state", "odd_reason", null, null, null),
        ]);
        Assert.Equal(["Direct connections", "IPv4", "NAT mapping", "Reachability", "Port mapping", "Socket receive buffer", "GSO", "future_feature"], view.Features.Select(row => row.Label));
        Assert.Equal("Degraded", Row(view.Features, "Direct connections").Value);
        Assert.Equal(["Check failed"], Row(view.Features, "Direct connections").Details);
        Assert.Equal(["192.0.2.10"], Row(view.Features, "IPv4").Details);
        Assert.Equal(["Not available", "Observed public address: 192.0.2.10:4000"], Row(view.Features, "NAT mapping").Details);
        Assert.Equal(["Not measured", "Observed public address: 192.0.2.10"], Row(view.Features, "Reachability").Details);
        Assert.Equal(["UPnP, NAT-PMP, XYZ"], Row(view.Features, "Port mapping").Details);
        // A granted size that is absent reads Unknown.
        Assert.Equal(["Host buffer limit reached", "Requested: 32.0 MB; granted: Unknown"], Row(view.Features, "Socket receive buffer").Details);
        var future = Row(view.Features, "future_feature");
        Assert.Equal("weird state", future.Value);
        Assert.Equal(["odd reason"], future.Details);
    }

    [Fact]
    public void ListenersNameThemselvesAndShowAlgorithmReasonErrnoAndSendBuffer()
    {
        var view = Show(listeners:
        [
            new("api", "bbr", "bbr", "on", "", null, new("on", "", 33554432, 33554432, null)),
            new("jellyfin", "bbr", "off", "fallback", "module_missing", "ENOENT", new("capped", "no_net_admin", 33554432, 212992, "EPERM")),
            new("abs", "off", "off", "off", "disabled", null, new("off", "not_needed", 0, 0, null)),
            new("proxy", "bbr", "bbr", "on", "", null, new("on", "", 0, 0, null)),
        ]);
        Assert.Equal("Listeners", view.ListenersHeading);
        Assert.Equal(["API", "Jellyfin", "Audiobookshelf", "Proxy"], view.Listeners.Select(row => row.Label));
        var api = view.Listeners[0];
        Assert.Equal("Congestion control: On", api.Value);
        Assert.Equal(["Algorithm: BBR", "Send buffer: On", "Requested: 32.0 MB; granted: 32.0 MB"], api.Details);
        var jellyfin = view.Listeners[1];
        Assert.Equal("Congestion control: Fallback", jellyfin.Value);
        Assert.Equal(["Algorithm: requested BBR; active Off", "BBR is not available (ENOENT)", "Send buffer: Capped", "Requested: 32.0 MB; granted: 208.0 KB",
            "NET_ADMIN capability missing (EPERM)"], jellyfin.Details);
        // Off: no algorithm line; the reason shows; no requested size when none was requested.
        var abs = view.Listeners[2];
        Assert.Equal("Congestion control: Off", abs.Value);
        Assert.Equal(["Disabled", "Send buffer: Not needed"], abs.Details);
        Assert.Equal(["Algorithm: BBR", "Send buffer: On"], view.Listeners[3].Details);
    }

    [Fact]
    public void TheListenersSubsectionExistsOnlyWhenTheHostHasListeners()
    {
        Assert.Null(Show().ListenersHeading);
        Assert.Equal("Host tuning", Show().TuningHeading);
        Assert.Equal("API server", Show().Heading);
        Assert.Equal("Last checked 2026-11-01 10:00:00Z", Show().LastChecked);
    }

    [Theory]
    [InlineData("33554432", "Unknown", "32.0 MB")]
    [InlineData(null, "Unknown", "Unknown")]
    [InlineData("", "Unknown", "Unknown")]
    [InlineData("0", "Unknown", "Unknown")]
    [InlineData("not a number", "Unknown", "Unknown")]
    [InlineData("1536", "Unknown", "1.5 KB")]
    [InlineData("512", "Unknown", "512 B")]
    public void SizesUseTheFileSizeFormatAndFallBackToUnknown(string? bytes, string fallback, string expected) => Assert.Equal(expected, P.Size(bytes, fallback));

    [Fact]
    public void RequestedAndGrantedAppearWhenEitherIsPresent()
    {
        var view = Show(features: [new("socket_send_buffer", "on", null, null, "33554432", null), new("socket_receive_buffer", "on", null, null, null, null)]);
        Assert.Equal(["Requested: Unknown; granted: 32.0 MB"], Row(view.Features, "Socket send buffer").Details);
        Assert.Empty(Row(view.Features, "Socket receive buffer").Details);
    }

    // ── section visibility, polling order and the advanced disclosure ─────

    [Theory]
    [InlineData("true", 0, true)]
    [InlineData("false", 0, false)]
    [InlineData("", 0, false)]
    [InlineData(null, 0, false)]
    [InlineData("false", 1, true)]
    [InlineData("", 2, true)]
    [InlineData("TRUE", 0, false)]
    public void TheSectionExistsOnlyWhenSavedOnOrAnythingIsStaged(string? saved, int dirty, bool visible) => Assert.Equal(visible, R.SectionVisible(saved, dirty));

    [Fact]
    public void ThePollIsOlderThanTheCacheOnlyWhenARefreshWasPublishedAfterItStarted()
    {
        Assert.True(R.PollIsCurrent(0, 0));
        Assert.True(R.PollIsCurrent(3, 3));
        Assert.False(R.PollIsCurrent(0, 1));
        Assert.False(R.PollIsCurrent(2, 5));
    }

    [Fact]
    public void ApollIsDueAfterAFullInterval()
    {
        var now = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(15), R.PollInterval);
        Assert.False(R.PollDue(now - TimeSpan.FromSeconds(14.9), now));
        Assert.True(R.PollDue(now - TimeSpan.FromSeconds(15), now));
        Assert.True(R.PollDue(DateTimeOffset.MinValue, now));
    }

    [Fact]
    public void TheAdvancedDisclosureIsForcedOpenOnlyByItsOwnKeys()
    {
        Assert.False(R.AdvancedForcedOpen([R.Enabled]));
        Assert.True(R.AdvancedForcedOpen([R.Enabled, R.PortMapping]));
        Assert.True(R.AdvancedForcedOpen([R.StunServers]));
        Assert.Equal([R.Enabled, R.PortMapping, R.StunServers], R.Keys);
    }

    // ── STUN list: the server's rules ────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("stun.fixture.invalid:3478")]
    [InlineData("stun.fixture.invalid:3478,stun2.fixture.invalid:19302")]
    [InlineData(" stun.fixture.invalid:3478 , 192.0.2.1:19302 ")]
    [InlineData("STUN.Fixture.Invalid:3478")]
    [InlineData("[2001:db8::1]:3478")]
    [InlineData("[::ffff:192.0.2.1]:3478")]
    [InlineData("a:1,b:2,c:3,d:4,e:5,f:6,g:7,h:8")]
    [InlineData("a:1,a:1,a:1,a:1,a:1,a:1,a:1,a:1")]
    [InlineData("stun.fixture.invalid.:3478")]
    [InlineData("a-b.c1.invalid:65535")]
    [InlineData("[stun.fixture.invalid]:3478")]
    public void AValidStunListPasses(string value) => Assert.True(R.CheckStunServers(value).IsValid);

    [Theory]
    [InlineData("a:1,b:2,c:3,d:4,e:5,f:6,g:7,h:8,i:9", AdminStunProblem.TooMany)]
    [InlineData("a:1,a:1,a:1,a:1,a:1,a:1,a:1,a:1,a:1", AdminStunProblem.TooMany)]
    [InlineData("stun.fixture.invalid", AdminStunProblem.HostPort)]
    [InlineData("stun.fixture.invalid:3478,", AdminStunProblem.HostPort)]
    [InlineData("stun.fixture.invalid:3478,,a:1", AdminStunProblem.HostPort)]
    [InlineData("::1:3478", AdminStunProblem.HostPort)]
    [InlineData("[::1]", AdminStunProblem.HostPort)]
    [InlineData("[::1:3478", AdminStunProblem.HostPort)]
    [InlineData("a:b:3478", AdminStunProblem.HostPort)]
    [InlineData("stun.fixture.invalid:", AdminStunProblem.Port)]
    [InlineData("stun.fixture.invalid:0", AdminStunProblem.Port)]
    [InlineData("stun.fixture.invalid:65536", AdminStunProblem.Port)]
    [InlineData("stun.fixture.invalid:-1", AdminStunProblem.Port)]
    [InlineData("stun.fixture.invalid:+80", AdminStunProblem.Port)]
    [InlineData("stun.fixture.invalid:http", AdminStunProblem.Port)]
    [InlineData(":3478", AdminStunProblem.Host)]
    [InlineData("192.168.1.256:3478", AdminStunProblem.Host)]
    [InlineData("01.2.3.4:3478", AdminStunProblem.Host)]
    [InlineData("-bad.invalid:3478", AdminStunProblem.Host)]
    [InlineData("bad-.invalid:3478", AdminStunProblem.Host)]
    [InlineData("bad..invalid:3478", AdminStunProblem.Host)]
    [InlineData("host_name.invalid:3478", AdminStunProblem.Host)]
    [InlineData("stün.invalid:3478", AdminStunProblem.Host)]
    [InlineData("[fe80::1%eth0]:3478", AdminStunProblem.Host)]
    public void AnInvalidStunListIsRefusedWithTheServersRule(string value, AdminStunProblem problem)
    {
        var check = R.CheckStunServers(value);
        Assert.Equal(problem, check.Problem);
        Assert.False(check.IsValid);
        Assert.StartsWith("direct_connections.stun_servers ", check.Message);
    }

    [Fact]
    public void TheStunMessagesAreTheServersSentences()
    {
        Assert.Equal("direct_connections.stun_servers accepts at most 8 entries", R.CheckStunServers("a:1,b:1,c:1,d:1,e:1,f:1,g:1,h:1,i:1").Message);
        Assert.Equal("direct_connections.stun_servers requires host:port entries: address stun.fixture.invalid: missing port in address", R.CheckStunServers("STUN.fixture.invalid").Message);
        // An empty entry: Go's AddrError leaves the "address X:" prefix out when the address is empty, so the server's message has none.
        Assert.Equal("direct_connections.stun_servers requires host:port entries: missing port in address", R.CheckStunServers("stun.fixture.invalid:3478,,other.fixture.invalid:3478").Message);
        Assert.Equal("direct_connections.stun_servers requires host:port entries: missing port in address", R.CheckStunServers("stun.fixture.invalid:3478,").Message);
        Assert.Equal("direct_connections.stun_servers requires ports between 1 and 65535", R.CheckStunServers("a:70000").Message);
        Assert.Equal("direct_connections.stun_servers contains an invalid IP address or DNS host", R.CheckStunServers("a b:1").Message);
    }

    // ── wire validation ──────────────────────────────────

    private const string Valid = """
        {"enabled":true,"hosts":[{"host":{"id":"api","name":"API","role":"api"},"checked_at":"2026-11-22T10:00:00.000Z",
         "features":[{"feature":"direct","kind":"local","state":"on","requested":33554432,"granted":"1024"}],
         "listeners":[{"listener":"api","requested":"bbr","granted":"bbr","state":"on","reason":"","send_buffer":{"state":"on","reason":"","requested":10,"granted":20}}],
         "knobs":[{"env":"SILO_NET_TCP_CONGESTION","default":"bbr","effective":"bbr","allowed":["bbr","off"]}]}]}
        """;

    private static async Task<(AdminDirectConnectionsApi Api, AdminMonitorKit Kit, AdminMonitorAuthority Authority)> ApiAsync(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var kit = AdminMonitorKit.Create();
        kit.Http.Respond = (request, _) => Task.FromResult(respond(request));
        var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        await Task.CompletedTask;
        return (new AdminDirectConnectionsApi(kit.Client), kit, authority);
    }

    [Fact]
    public async Task ReadAndRefreshUseTheOfficialPathsAndTheStatusIsValidated()
    {
        var (api, kit, authority) = await ApiAsync(_ => AdminMonitorKit.Json(Valid));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var status = await api.GetStatusAsync(lease);
            await api.RefreshAsync(lease);
            Assert.Equal(["GET /api/v2/admin/direct-connections/status", "GET /api/v2/admin/direct-connections/status?refresh=true"], kit.Http.Requests);
            Assert.All(kit.Http.Bodies, body => Assert.Equal("", body));
            var host = Assert.Single(status.Hosts);
            Assert.Equal("api", host.Id);
            // A byte count sent as a number or a string is kept as text.
            Assert.Equal("33554432", host.Features[0].Requested);
            Assert.Equal("1024", host.Features[0].Granted);
            Assert.Equal(20, host.Listeners[0].SendBuffer.Granted);
            Assert.Equal(["bbr", "off"], host.Knobs[0].Allowed);
        }
    }

    [Theory]
    [InlineData("""{"hosts":[]}""")]
    [InlineData("""{"enabled":true}""")]
    [InlineData("""{"enabled":true,"hosts":[{"host":{"id":""},"checked_at":"2026-11-22T10:00:00Z","features":[],"knobs":[]}]}""")]
    [InlineData("""{"enabled":true,"hosts":[{"host":{"id":"api"},"features":[],"knobs":[]}]}""")]
    [InlineData("""{"enabled":true,"hosts":[{"host":{"id":"api"},"checked_at":"2026-11-22T10:00:00Z","features":[{"feature":"direct"}],"knobs":[]}]}""")]
    [InlineData("""{"enabled":true,"hosts":[{"host":{"id":"api"},"checked_at":"2026-11-22T10:00:00Z","features":[],"knobs":[{"env":"X"}]}]}""")]
    public async Task AnUnusableStatusIsAnInvalidResponseNotPartialData(string body)
    {
        var (api, kit, authority) = await ApiAsync(_ => AdminMonitorKit.Json(body));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            await Assert.ThrowsAsync<InvalidDataException>(() => api.GetStatusAsync(lease));
            await Assert.ThrowsAsync<AdminDirectConnectionsOutcomeUnknownException>(() => api.RefreshAsync(lease));
        }
    }

    [Fact]
    public async Task AListOverItsBoundIsReportedNotTrimmed()
    {
        var features = string.Join(",", Enumerable.Range(0, AdminDirectConnectionsApi.MaxFeatures + 1).Select(i => $$"""{"feature":"f{{i}}","kind":"local","state":"on"}"""));
        var (api, kit, authority) = await ApiAsync(_ => AdminMonitorKit.Json($$"""{"enabled":true,"hosts":[{"host":{"id":"api"},"checked_at":"2026-11-22T10:00:00Z","features":[{{features}}],"knobs":[]}]}"""));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var error = await Assert.ThrowsAsync<AdminNetworkAccessLimitException>(() => api.GetStatusAsync(lease));
            Assert.Equal(AdminDirectConnectionsApi.MaxFeatures, error.Limit);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "permission_denied")]
    [InlineData(HttpStatusCode.NotFound, "not_found")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "validation_failed")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited")]
    public async Task AFourHundredAnswerToRunCheckIsARefusalThatKeepsTheServersDetail(HttpStatusCode status, string type)
    {
        var (api, kit, authority) = await ApiAsync(_ => new(status)
        {
            Content = new StringContent($$"""{"type":"https://siloserver.org/docs/api/v2/problems/{{type}}","title":"No","status":{{(int)status}},"detail":"Direct connection checks are not allowed here."}""",
                System.Text.Encoding.UTF8, "application/problem+json"),
        });
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            var refused = await Assert.ThrowsAsync<ApiException>(() => api.RefreshAsync(lease));
            Assert.Equal((int)status, refused.StatusCode);
            Assert.Equal("Direct connection checks are not allowed here.", refused.ProblemDetail);
            // A plain read never keeps server text.
            var read = await Assert.ThrowsAsync<ApiException>(() => api.GetStatusAsync(lease));
            Assert.Null(read.ProblemDetail);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task ATimeoutOrServerFailureAfterSendingRunCheckIsAnUnknownOutcome(HttpStatusCode status)
    {
        var (api, kit, authority) = await ApiAsync(_ => AdminMonitorKit.Json("""{"title":"down"}""", status));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            await Assert.ThrowsAsync<AdminDirectConnectionsOutcomeUnknownException>(() => api.RefreshAsync(lease));
            Assert.Single(kit.Http.Requests);
        }
    }

    [Fact]
    public async Task ATransportFailureAfterSendingRunCheckIsAnUnknownOutcome()
    {
        var (api, kit, authority) = await ApiAsync(_ => throw new HttpRequestException("synthetic"));
        using (kit) using (authority)
        {
            using var lease = authority.Capture();
            await Assert.ThrowsAsync<AdminDirectConnectionsOutcomeUnknownException>(() => api.RefreshAsync(lease));
        }
    }

    [Fact]
    public void EveryCatalogKeyTheSectionUsesHasText()
    {
        foreach (var key in AdminDirectConnectionsText.Keys) Assert.NotEqual(key, AdminDirectConnectionsText.T(key));
        Assert.Equal("Run check", AdminDirectConnectionsText.T("web.admin.settings.network.direct.runCheck"));
        // A key the catalog does not own is answered by the shared admin catalog.
        Assert.Equal("API server", AdminDirectConnectionsText.T("web.admin.settings.network.host.apiServer"));
    }
}
