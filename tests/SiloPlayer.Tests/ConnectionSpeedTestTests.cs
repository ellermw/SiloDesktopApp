using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ConnectionSpeedTestTests
{
    private static PlaybackCapabilityV3 Capability => new()
    {
        Features = ["connection_speed_v1", "buffering_quality_v1"],
        BandwidthProbeUrl = "/api/v2/playback/bandwidth-probe?media_path=true",
    };

    [Fact]
    public async Task MediaProxyRedirectFollowsSignedUrlWithoutSessionHeaders()
    {
        using var wire = new ProbeWire();
        using var http = new HttpClient(wire) { DefaultRequestVersion = HttpVersion.Version20 };
        var client = Client(http);
        await client.MeasurePlaybackConnectionAsync(client.CaptureIdentityContext(), Capability, true, "ethernet", default);
        Assert.Equal(2, wire.Requests.Count);
        Assert.True(wire.Requests[0].Authorized);
        Assert.False(wire.Requests[0].ProfileGrant);
        Assert.Contains("media_path=true&size=1000000000", wire.Requests[0].Path);
        Assert.Equal("media.fixture.invalid", wire.Requests[1].Host);
        Assert.False(wire.Requests[1].Authorized);
        Assert.False(wire.Requests[1].AnyIdentity);
        Assert.All(wire.Requests, r => { Assert.Equal(HttpVersion.Version20, r.Version); Assert.True(r.NoStore); Assert.True(r.Binary); });
    }

    [Theory]
    [InlineData("http://media.fixture.invalid/stream/bandwidth-probe/fixture", false)]
    [InlineData("https://user:password@media.fixture.invalid/stream/bandwidth-probe/fixture", false)]
    [InlineData("file:///probe", false)]
    [InlineData("https://media.fixture.invalid/loop", true)]
    public async Task RedirectsRejectDowngradesCredentialsAndUnboundedChains(string location, bool loop)
    {
        using var wire = new ProbeWire { Redirect = location, Loop = loop };
        using var http = new HttpClient(wire);
        var client = Client(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.MeasurePlaybackConnectionAsync(client.CaptureIdentityContext(), Capability, true, "wifi", default));
        Assert.InRange(wire.Requests.Count, 1, 6);
    }

    [Fact]
    public async Task RateLimitOnMediaProxyRetainsRetryAfter()
    {
        using var wire = new ProbeWire { MediaStatus = HttpStatusCode.TooManyRequests };
        using var http = new HttpClient(wire);
        var client = Client(http);
        var error = await Assert.ThrowsAsync<ApiException>(() => client.MeasurePlaybackConnectionAsync(client.CaptureIdentityContext(), Capability, true, "wifi", default));
        Assert.Equal(429, error.StatusCode);
        Assert.Equal(7, error.RetryAfterSeconds);
        Assert.Equal("Try again in 7 seconds", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedResultSurvivesReadFailureButRejectedSaveIsDistinct(bool rejectSave)
    {
        using var wire = new ServiceWire { RejectSave = rejectSave };
        using var http = new HttpClient(wire); var client = Client(http);
        var service = PlaybackConnectionService.For(new PlaybackApi(client));
        if (rejectSave) await Assert.ThrowsAsync<ConnectionProbeNotSavedException>(() => service.TestAsync("fixture", false, "ethernet"));
        else Assert.True((await service.TestAsync("fixture", false, "ethernet")).Kbps >= 100);
        Assert.Equal(1, wire.Reports);
        Assert.Null(service.State);
    }

    [Fact]
    public async Task RefreshOnSameLinkDoesNotCancelManualTestAndPlaybackDoes()
    {
        using var wire = new ServiceWire(); using var http = new HttpClient(wire); var client = Client(http);
        var service = PlaybackConnectionService.For(new PlaybackApi(client));
        var test = service.TestAsync("fixture", false, "ethernet");
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.RefreshAsync("fixture", false, "ethernet", true);
        Assert.True((await test).Kbps >= 100);
        var blocked = service.TestAsync("fixture", false, "ethernet");
        service.SetMediaActive(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        Assert.Equal(1, wire.Reports);
    }

    private sealed class ServiceWire : HttpMessageHandler
    {
        internal bool RejectSave;
        internal int Reports;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/capabilities")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("""{"features":["connection_speed_v1","buffering_quality_v1"],"bandwidth_probe_url":"/probe"}""") });
            if (path.EndsWith("/results")) { Reports++; return Task.FromResult(new HttpResponseMessage(RejectSave ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent)); }
            if (path.EndsWith("/connection")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            Started.TrySetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new PacedStream()) });
        }
    }
    private sealed class PacedStream : Stream
    {
        private int _read;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_read++ == 0) return Math.Min(64000, buffer.Length);
            await Task.Delay(550, ct);
            return _read == 2 ? Math.Min(64000, buffer.Length) : 0;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private static SiloApiClient Client(HttpClient http)
    {
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://api.fixture.invalid"); client.SetAccessToken("fixture-access"); client.SetProfile("fixture", "fixture-pin");
        client.SetDeviceMetadata("fixture-device", "fixture", "windows");
        return client;
    }

    [Fact]
    public async Task ProbeRefreshesExpiredAccountAccessBeforeFollowingMediaRedirect()
    {
        using var wire = new ProbeWire { UnauthorizedOnce = true }; using var http = new HttpClient(wire);
        var client = Client(http); var refreshed = 0;
        client.SetTokenRefresher(_ => { refreshed++; client.SetAccessToken("fixture-renewed"); return Task.FromResult(true); });
        await client.MeasurePlaybackConnectionAsync(client.CaptureIdentityContext(), Capability, true, "wifi", default);
        Assert.Equal(1, refreshed); Assert.Equal(3, wire.Requests.Count);
        Assert.All(wire.Requests, r => Assert.False(r.ProfileGrant));
        Assert.False(wire.Requests[^1].AnyIdentity);
    }

    [Fact]
    public async Task IdentityChangeStopsBeforeFollowingSignedMediaUrl()
    {
        using var wire = new ProbeWire(); using var http = new HttpClient(wire); var client = Client(http);
        wire.BeforeResponse = () => client.SetProfile("replacement");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.MeasurePlaybackConnectionAsync(client.CaptureIdentityContext(), Capability, true, "wifi", default));
        Assert.Single(wire.Requests);
    }

    private sealed class ProbeWire : HttpMessageHandler
    {
        public string Redirect = "https://media.fixture.invalid/stream/bandwidth-probe/fixture?size=1000000000";
        public bool Loop;
        public bool UnauthorizedOnce;
        public Action? BeforeResponse;
        public HttpStatusCode MediaStatus = HttpStatusCode.OK;
        public List<(string Host, string Path, bool Authorized, bool ProfileGrant, bool AnyIdentity, Version Version, bool NoStore, bool Binary)> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.Host, request.RequestUri.PathAndQuery,
                request.Headers.Contains("X-Profile-Id"), request.Headers.Contains("X-Profile-Token"),
                request.Headers.Authorization != null || request.Headers.Any(h => h.Key.StartsWith("X-Profile") || h.Key.StartsWith("X-Silo-Device")),
                request.Version, request.Headers.CacheControl?.NoStore == true, request.Headers.Accept.Any(h => h.MediaType == "application/octet-stream")));
            BeforeResponse?.Invoke();
            if (UnauthorizedOnce && Requests.Count == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            var redirect = Requests.Count == (UnauthorizedOnce ? 2 : 1) || Loop;
            var response = new HttpResponseMessage(redirect ? HttpStatusCode.TemporaryRedirect : MediaStatus) { Content = new ByteArrayContent([]) };
            if (redirect) response.Headers.Location = new Uri(Redirect);
            if (MediaStatus == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return Task.FromResult(response);
        }
    }
}
