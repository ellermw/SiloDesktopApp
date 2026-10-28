using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Models;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class AppUpdateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticChecksDiscoverNewReleasesWithoutManualInputAndRetryFailures(bool firstCheckFails)
    {
        using var clock = new CheckClock(); using var wire = new ChangingRelease(firstCheckFails);
        using var http = new HttpClient(wire);
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334), timeProvider: clock);
        using var stop = new CancellationTokenSource();
        var loop = service.RunAutomaticChecksAsync(stop.Token);
        try
        {
            var first = await clock.NextAsync(); Assert.Equal(TimeSpan.FromSeconds(5), first.Due); first.Fire();
            var next = await clock.NextAsync();
            Assert.Equal(firstCheckFails ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(15), next.Due);
            Assert.Null(service.State.Release); Assert.Equal(1, wire.Checks);
            next.Fire();
            var after = await clock.NextAsync();
            Assert.Equal(TimeSpan.FromMinutes(15), after.Due);
            Assert.Equal(new Version(1, 2, 335, 0), service.State.Release?.Version);
            Assert.Equal(2, wire.Checks); Assert.Null(service.State.Error);
        }
        finally { stop.Cancel(); await loop.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task RepeatedChecksAreConditionalAndKeepTheReleaseOnNotModified()
    {
        // 304 responses do not count against GitHub's 60/hour unauthenticated limit.
        using var wire = new ConditionalRelease(); using var http = new HttpClient(wire);
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));

        await service.CheckAsync();
        await service.CheckAsync();

        Assert.Equal(new[] { null, "\"release-335\"" }, wire.IfNoneMatch.ToArray());
        Assert.Equal(new Version(1, 2, 335, 0), service.State.Release?.Version);
        Assert.Null(service.State.Error);
    }

    [Theory]
    [InlineData("reset", 45, 45)]      // X-RateLimit-Reset 45 min away: wait for it
    [InlineData("retry-after", 2, 5)]  // never sooner than the normal retry interval
    [InlineData("retry-after", 2880, 360)] // never longer than six hours
    public async Task RateLimitedChecksWaitForGitHubsResetInsteadOfRetryingEveryFiveMinutes(string kind, int minutes, int expectedMinutes)
    {
        using var clock = new CheckClock(); using var wire = new RateLimitedRelease(kind, TimeSpan.FromMinutes(minutes));
        using var http = new HttpClient(wire);
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334), timeProvider: clock);
        using var stop = new CancellationTokenSource();
        var loop = service.RunAutomaticChecksAsync(stop.Token);
        try
        {
            (await clock.NextAsync()).Fire();
            var next = await clock.NextAsync();
            Assert.InRange(next.Due.TotalMinutes, expectedMinutes - 1, expectedMinutes + 0.01);
            Assert.Contains("limiting update checks", service.State.Error);
        }
        finally { stop.Cancel(); await loop.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    private sealed class ConditionalRelease : HttpMessageHandler
    {
        public List<string?> IfNoneMatch = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var etag = request.Headers.TryGetValues("If-None-Match", out var values) ? values.Single() : null;
            IfNoneMatch.Add(etag);
            if (etag == "\"release-335\"") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release()) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"release-335\"");
            return Task.FromResult(response);
        }
    }

    private sealed class RateLimitedRelease(string kind, TimeSpan wait) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"message\":\"API rate limit exceeded\"}") };
            if (kind == "reset")
            {
                response.Headers.Add("X-RateLimit-Remaining", "0");
                response.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.Add(wait).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
            return Task.FromResult(response);
        }
    }

    private sealed class ChangingRelease(bool fails) : HttpMessageHandler
    {
        public int Checks;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var first = Interlocked.Increment(ref Checks) == 1;
            return Task.FromResult(new HttpResponseMessage(first && fails ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(first ? Release(tag: "v1.2.334") : Release()) });
        }
    }

    private sealed class CheckClock : TimeProvider, IDisposable
    {
        private readonly global::System.Threading.Channels.Channel<Pending> _pending = global::System.Threading.Channels.Channel.CreateUnbounded<Pending>();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Pending(callback, state, dueTime); _pending.Writer.TryWrite(timer); return timer;
        }
        public async Task<Pending> NextAsync() => await _pending.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public void Dispose() { }
        public sealed class Pending(TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            public TimeSpan Due => due;
            private bool _disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    internal const string Digest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    internal const string AssetUrl = "https://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.335/SiloInstaller-Windows-x64.exe";

    [Fact]
    public async Task PublishedStableReleaseProvidesInAppNotesWithoutSiloCredentials()
    {
        using var wire = new Wire(Release()); using var http = new HttpClient(wire);
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334, 0));
        await service.CheckAsync();
        var update = Assert.IsType<AppRelease>(service.State.Release);
        Assert.Equal(new Version(1, 2, 335, 0), update.Version);
        Assert.Equal("## Fixed\nPlayback and navigation", update.Notes);
        Assert.Equal(Digest, update.Sha256);
        Assert.Equal("https://api.github.com/repos/ellermw/SiloDesktopApp/releases/latest", wire.Request!.RequestUri!.AbsoluteUri);
        Assert.Null(wire.Request.Headers.Authorization);
        Assert.Null(service.State.Error);
    }

    [Theory]
    [InlineData("v1.2.334")]
    [InlineData("v1.2.334.0")]
    [InlineData("v1.2.333")]
    public async Task EqualOrOlderVersionsDoNotOfferAnUpdate(string tag)
    {
        using var http = new HttpClient(new Wire(Release(tag: tag)));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334, 0));
        await service.CheckAsync();
        Assert.Null(service.State.Release); Assert.Null(service.State.Error);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DraftAndPreviewReleasesAreNotOffered(bool draft, bool preview)
    {
        using var http = new HttpClient(new Wire(Release(draft: draft, preview: preview)));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync(); Assert.Null(service.State.Release);
    }

    [Theory]
    [InlineData("https://evil.invalid/setup.exe", "sha256:" + Digest, 128)]
    [InlineData("http://github.com/ellermw/SiloDesktopApp/releases/download/v1.2.335/SiloInstaller-Windows-x64.exe", "sha256:" + Digest, 128)]
    [InlineData(AssetUrl + "?other=1", "sha256:" + Digest, 128)]
    [InlineData(AssetUrl, "", 128)]
    [InlineData(AssetUrl, "sha256:not-a-digest", 128)]
    [InlineData(AssetUrl, "sha256:" + Digest, 0)]
    [InlineData(AssetUrl, "sha256:" + Digest, 536870913)]
    public async Task UnverifiableOrUntrustedPackagesNeverBecomeAvailable(string url, string digest, long size)
    {
        using var http = new HttpClient(new Wire(Release(url: url, digest: digest, size: size)));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync(); Assert.Null(service.State.Release); Assert.NotNull(service.State.Error);
    }

    [Fact]
    public async Task OversizedMetadataIsRejectedBeforeParsing()
    {
        using var http = new HttpClient(new Wire(new string(' ', 524289)));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync(); Assert.Null(service.State.Release); Assert.NotNull(service.State.Error);
    }

    [Fact]
    public async Task HttpFailureDoesNotEscapeTheUpdateBoundary()
    {
        using var http = new HttpClient(new Wire("", HttpStatusCode.Forbidden));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync(); Assert.NotNull(service.State.Error); Assert.False(service.State.IsChecking);
    }

    [Fact]
    public void ExistingSettingsDefaultToNotificationOnly()
        => Assert.Equal(AppUpdateMode.NotifyOnly, new AppSettings().UpdateMode);

    [Fact]
    public async Task HugeChangelogsAreBoundedForResponsiveNativePresentation()
    {
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(Release())!;
        metadata["body"] = new string('x', 128000);
        using var http = new HttpClient(new Wire(metadata.ToJsonString()));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync();
        Assert.NotNull(service.State.Release);
        Assert.InRange(service.State.Release!.Notes.Length, 1, 33000);
        Assert.Contains("GitHub", service.State.Release.Notes);
    }

    internal static string Release(string tag = "v1.2.335", string url = AssetUrl, string digest = "sha256:" + Digest,
        long size = 128, bool draft = false, bool preview = false) => JsonSerializer.Serialize(new
        {
            tag_name = tag, body = "## Fixed\nPlayback and navigation", published_at = "2026-10-28T10:00:00Z",
            draft, prerelease = preview, assets = new[] { new { name = "SiloInstaller-Windows-x64.exe", browser_download_url = url, digest, size, state = "uploaded" } }
        });

    internal sealed class Wire(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
