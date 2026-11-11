using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class DeviceLoginBindingTests
{
    [Fact]
    public async Task SetupReusesRaw32ByteSecretForEveryStartAndPollAndRotatesWithOwner()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl(Wire.Server);
        client.SetAccessToken("fixture-old-access");
        var api = new AuthApi(client);
        using var setup = new DeviceLoginSetup(api, Wire.Server);
        await setup.StartAsync("Fixture desktop", "Windows");
        await setup.PollAsync("fixture-device"); await setup.PollAsync("fixture-device");
        await setup.StartAsync("Retry desktop", "Windows");
        Assert.Equal(1, wire.Capabilities);
        Assert.True(wire.Hashes.Count == 2 && wire.Hashes[0] == wire.Hashes[1]);
        Assert.True(wire.Secrets.Count == 2 && wire.Secrets[0] == wire.Secrets[1]);
        Assert.True(wire.Secrets[0] is { Length: 43 } && wire.Hashes[0] is { Length: 43 });
        var bytes = Convert.FromBase64String(wire.Secrets[0]!.Replace('-', '+').Replace('_', '/') + "=");
        Assert.Equal(32, bytes.Length);
        Assert.True(Encode(SHA256.HashData(bytes)) == wire.Hashes[0]);
        Assert.False(wire.Authenticated);
        using var replacement = new DeviceLoginSetup(api, Wire.Server);
        await replacement.StartAsync("Replacement", "Windows"); await replacement.PollAsync("replacement-device");
        Assert.True(wire.Hashes[2] != wire.Hashes[0] && wire.Secrets[2] != wire.Secrets[0]);
        setup.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => setup.PollAsync("fixture-device"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => setup.StartAsync("Retired", "Windows"));
    }

    [Theory]
    [InlineData("negative")]
    [InlineData("absent")]
    [InlineData("404")]
    [InlineData("405")]
    public async Task OlderServersOmitBothBindingFields(string mode)
    {
        using var wire = new Wire { Mode = mode }; using var http = new HttpClient(wire);
        var api = new AuthApi(new SiloApiClient(http));
        using var setup = new DeviceLoginSetup(api, Wire.Server);
        await setup.StartAsync("Fixture", "Windows"); await setup.PollAsync("fixture-device");
        Assert.Null(Assert.Single(wire.Hashes)); Assert.Null(Assert.Single(wire.Secrets));
    }

    [Theory]
    [InlineData("500")]
    [InlineData("transport")]
    [InlineData("malformed")]
    public async Task FailedNegotiationCannotStartOrPollUnbound(string mode)
    {
        using var wire = new Wire { Mode = mode }; using var http = new HttpClient(wire);
        using var setup = new DeviceLoginSetup(new AuthApi(new SiloApiClient(http)), Wire.Server);
        await Assert.ThrowsAnyAsync<Exception>(() => setup.StartAsync("Fixture", "Windows"));
        await Assert.ThrowsAnyAsync<Exception>(() => setup.PollAsync("fixture-device"));
        Assert.Empty(wire.Hashes); Assert.Empty(wire.Secrets);
    }

    [Fact]
    public async Task BoundSecretNeverEscapesInApiExceptionIncludingEchoedProblemFields()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var api = new AuthApi(new SiloApiClient(http)); using var setup = new DeviceLoginSetup(api, Wire.Server);
        await setup.StartAsync("Fixture", "Windows"); wire.Refuse = true;
        var error = await Assert.ThrowsAsync<ApiException>(() => setup.PollAsync("fixture-device"));
        Assert.Equal("device_binding_mismatch", error.ErrorCode); Assert.Equal(403, error.StatusCode);
        Assert.True(!error.ToString().Contains(wire.Secrets[0]!, StringComparison.Ordinal));
        Assert.True(error.ErrorLocation is null || !error.ErrorLocation.Contains(wire.Secrets[0]!, StringComparison.Ordinal));
        wire.EchoCode = true;
        error = await Assert.ThrowsAsync<ApiException>(() => setup.PollAsync("fixture-device"));
        Assert.True(!error.ToString().Contains(wire.Secrets[0]!, StringComparison.Ordinal) && !error.ErrorCode.Contains(wire.Secrets[0]!, StringComparison.Ordinal));
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task BoundRequestRejectsMissingOrWrongSecretWithoutPreventingTheOwningPoll()
    {
        using var wire = new Wire { EnforceBinding = true }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl(Wire.Server); var api = new AuthApi(client);
        using var setup = new DeviceLoginSetup(api, Wire.Server);
        await setup.StartAsync("Fixture", "Windows");
        var missing = await Assert.ThrowsAsync<ApiException>(() => api.DevicePollAsync("fixture-device"));
        var wrong = await Assert.ThrowsAsync<ApiException>(() => api.DevicePollAsync(Wire.Server, "fixture-device", new string('A', 43), CancellationToken.None));
        Assert.Equal("device_binding_mismatch", missing.ErrorCode); Assert.Equal("device_binding_mismatch", wrong.ErrorCode);
        Assert.Equal(0, wire.AcceptedPolls);
        Assert.Equal("pending", (await setup.PollAsync("fixture-device")).Status);
        Assert.Equal(1, wire.AcceptedPolls);
    }

    [Fact]
    public async Task PlainHttpServersRetainBoundPhoneSignIn()
    {
        using var wire = new Wire { EnforceBinding = true }; using var http = new HttpClient(wire);
        using var setup = new DeviceLoginSetup(new AuthApi(new SiloApiClient(http)), "http://device-contract.invalid");
        await setup.StartAsync("Fixture", "Windows");
        Assert.Equal("pending", (await setup.PollAsync("fixture-device")).Status);
        Assert.Equal("pending", (await setup.PollAsync("fixture-device")).Status);
        Assert.Equal(1, wire.Capabilities);
        Assert.True(Assert.Single(wire.Hashes) is { Length: 43 });
        Assert.Equal(2, wire.AcceptedPolls);
        Assert.True(wire.Secrets[0] is { Length: 43 } && wire.Secrets[0] == wire.Secrets[1]);
        Assert.False(wire.Authenticated);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task TransientPollFailurePreservesRetryAfterAndSetupSecret(int status)
    {
        using var wire = new Wire { EnforceBinding = true, PollFailureStatus = (HttpStatusCode)status }; using var http = new HttpClient(wire);
        using var setup = new DeviceLoginSetup(new AuthApi(new SiloApiClient(http)), Wire.Server);
        await setup.StartAsync("Fixture", "Windows");
        var error = await Assert.ThrowsAsync<ApiException>(() => setup.PollAsync("fixture-device"));
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(2d, error.RetryAfterSeconds);
        Assert.Equal("pending", (await setup.PollAsync("fixture-device")).Status);
        Assert.Equal(1, wire.Capabilities);
        Assert.Single(wire.Hashes);
        Assert.Equal(2, wire.Secrets.Count);
        Assert.True(wire.Secrets[0] is { Length: 43 } && wire.Secrets[0] == wire.Secrets[1]);
        Assert.Equal(1, wire.AcceptedPolls);
    }

    [Fact]
    public async Task EchoedTransportExceptionDoesNotPersistTheSecret()
    {
        var directory = Path.Combine(TestRepository.Root, ".codex-tmp", "device-binding", "transport-log-" + Guid.NewGuid().ToString("N"));
        using var operation = LocalLog.BeginOperation("device-binding-transport-fixture", directory);
        using var wire = new Wire { TransportEcho = true }; using var http = new HttpClient(wire);
        using var setup = new DeviceLoginSetup(new AuthApi(new SiloApiClient(http)), Wire.Server);
        await setup.StartAsync("Fixture", "Windows");
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => setup.PollAsync("fixture-device"));
        Assert.DoesNotContain("fixture-transport-sensitive-value", error.ToString());
        var log = File.ReadAllText(Path.Combine(directory, "api_diagnostics.txt"));
        Assert.DoesNotContain("fixture-transport-sensitive-value", log);
    }

    [Fact]
    public async Task LookupReadsBindingAndManualDecisionsSendOnlyCode()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl(Wire.Server); var api = new AuthApi(client);
        var found = await api.DeviceLookupAsync(null, "1234 5678");
        Assert.Equal(new string('A', 43), found.Binding);
        Assert.Equal("?code=1234%205678", wire.Query);
        await api.DeviceApproveAsync(null, "12345678"); await api.DeviceDenyAsync(null, "12345678");
        await api.DeviceApproveHandoffAsync(null, "12345678");
        Assert.Equal(3, wire.Decisions);
    }

    [Fact]
    public async Task LegacyDirectStartPollRemainExplicitlyUnbound()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl(Wire.Server); var api = new AuthApi(client);
        await api.DeviceStartAsync("Fixture", "Windows"); await api.DevicePollAsync("fixture-device");
        Assert.Equal(0, wire.Capabilities); Assert.Null(Assert.Single(wire.Hashes)); Assert.Null(Assert.Single(wire.Secrets));
    }

    [Fact]
    public async Task RetiredNegotiationCannotSendStartAndExplicitServerDoesNotFollowActiveServer()
    {
        using var wire = new Wire { CapabilityGate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://other-fixture.invalid");
        using var setup = new DeviceLoginSetup(new AuthApi(client), Wire.Server);
        var start = setup.StartAsync("Fixture", "Windows"); setup.Dispose();
        wire.CapabilityGate.SetResult(Wire.Reply(new { session_binding = true }));
        await Assert.ThrowsAnyAsync<Exception>(() => start); Assert.Empty(wire.Hashes);
        Assert.True(wire.Hosts.All(h => h == new Uri(Wire.Server).Host));
    }

    [Fact]
    public async Task ChangedSetupOwnerDuringNegotiationCannotSendStart()
    {
        using var wire = new Wire { CapabilityGate = new(TaskCreationOptions.RunContinuationsAsynchronously) }; using var http = new HttpClient(wire);
        var current = true;
        using var setup = new DeviceLoginSetup(new AuthApi(new SiloApiClient(http)), Wire.Server, () => current);
        var start = setup.StartAsync("Fixture", "Windows"); current = false;
        wire.CapabilityGate.SetResult(Wire.Reply(new { session_binding = true }));
        await Assert.ThrowsAsync<OperationCanceledException>(() => start); Assert.Empty(wire.Hashes);
    }

    private static string Encode(byte[] raw) => Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed class Wire : HttpMessageHandler
    {
        internal const string Server = "https://device-contract.invalid";
        internal string Mode = "positive", Query = "";
        internal int Capabilities, Decisions, AcceptedPolls;
        internal bool Authenticated, Refuse, EchoCode, EnforceBinding, TransportEcho;
        internal HttpStatusCode? PollFailureStatus;
        internal List<string?> Hashes = [], Secrets = [];
        internal List<string> Hosts = [];
        internal TaskCompletionSource<HttpResponseMessage>? CapabilityGate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Hosts.Add(request.RequestUri!.Host);
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("capability"))
            {
                ++Capabilities; if (CapabilityGate is { } gate) return await gate.Task;
                return Mode switch
                {
                    "negative" => Reply(new { session_binding = false }), "absent" => Reply(new { cancel = true }),
                    "404" => new(HttpStatusCode.NotFound) { Content = new StringContent("{}") }, "405" => new(HttpStatusCode.MethodNotAllowed) { Content = new StringContent("{}") },
                    "500" => new(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") },
                    "transport" => throw new HttpRequestException("Synthetic offline"), "malformed" => Reply(new { session_binding = "true" }),
                    _ => Reply(new { session_binding = true })
                };
            }
            if (path == "/api/v2/auth/device") { Query = request.RequestUri.Query; return Reply(new { status = "pending", binding = new string('A', 43) }); }
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = json.RootElement;
            if (path.EndsWith("/start"))
            {
                Authenticated |= request.Headers.Authorization is not null;
                Hashes.Add(body.TryGetProperty("binding", out var hash) ? hash.GetString() : null);
                Assert.Equal(Hashes[^1] is null ? 2 : 3, body.EnumerateObject().Count());
                return Reply(new { device_code = "fixture-device", interval = 1 });
            }
            if (path.EndsWith("/poll"))
            {
                Authenticated |= request.Headers.Authorization is not null;
                Secrets.Add(body.TryGetProperty("binding_secret", out var secret) ? secret.GetString() : null);
                Assert.Equal(Secrets[^1] is null ? 1 : 2, body.EnumerateObject().Count());
                if (TransportEcho) throw new HttpRequestException("fixture-transport-sensitive-value");
                if (EnforceBinding && (Secrets[^1] is not { } provided || Encode(SHA256.HashData(Convert.FromBase64String(provided.Replace('-', '+').Replace('_', '/') + "="))) != Hashes[0]))
                    return Reply(new { type = "https://siloserver.org/docs/api/v2/problems/device_binding_mismatch" }, HttpStatusCode.Forbidden);
                if (Refuse) return Reply(new { type = "https://siloserver.org/docs/api/v2/problems/device_binding_mismatch", code = EchoCode ? Secrets[^1] : "device_binding_mismatch", detail = Secrets[^1], errors = new[] { new { location = Secrets[^1] } } }, HttpStatusCode.Forbidden);
                if (PollFailureStatus is { } status)
                {
                    PollFailureStatus = null;
                    var failure = Reply(new { code = "slow_down" }, status);
                    failure.Headers.RetryAfter = new(TimeSpan.FromSeconds(2));
                    return failure;
                }
                ++AcceptedPolls;
                return Reply(new { status = "pending" });
            }
            Assert.Equal("12345678", body.GetProperty("code").GetString()); Assert.Single(body.EnumerateObject()); ++Decisions;
            return new(HttpStatusCode.NoContent);
        }
        internal static HttpResponseMessage Reply(object value, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
    }
}
