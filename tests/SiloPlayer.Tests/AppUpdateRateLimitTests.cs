using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class AppUpdateRateLimitTests
{
    [Theory]
    [InlineData(403, "date", 45)]
    [InlineData(429, "none", 5)]
    [InlineData(403, "invalid-reset", 5)]
    [InlineData(403, "overflow-reset", 5)]
    public async Task HeaderVariantsUseABoundedCooldown(int status, string kind, int minutes)
    {
        using var clock = new Clock();
        using var http = new HttpClient(new ResponseWire(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status);
            if (kind == "date") response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddMinutes(45));
            if (kind.EndsWith("reset", StringComparison.Ordinal))
            {
                response.Headers.Add("X-RateLimit-Remaining", "0");
                response.Headers.TryAddWithoutValidation("X-RateLimit-Reset", kind == "overflow-reset" ? long.MaxValue.ToString() : "invalid");
            }
            return response;
        }));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334), timeProvider: clock);
        using var stop = new CancellationTokenSource(); var loop = service.RunAutomaticChecksAsync(stop.Token);
        try
        {
            (await clock.NextAsync()).Fire(); var next = await clock.NextAsync();
            Assert.Equal(TimeSpan.FromMinutes(minutes), next.Due);
            Assert.Contains("limiting update checks", service.State.Error);
        }
        finally { stop.Cancel(); await loop.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ConditionalCacheKeepsNoUpdateAndRejectsInvalidReplacementValidators()
    {
        var calls = 0;
        using var http = new HttpClient(new ResponseWire(request =>
        {
            calls++;
            Assert.Null(request.Headers.Authorization);
            if (calls is 2 or 5)
            {
                Assert.Equal("\"validated\"", request.Headers.IfNoneMatch.Single().ToString());
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }
            if (calls == 4)
            {
                var bad = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AppUpdateTests.Release(digest: "sha256:invalid")) };
                bad.Headers.ETag = new EntityTagHeaderValue("\"unvalidated\""); return bad;
            }
            var valid = new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(AppUpdateTests.Release(tag: calls == 1 ? "v1.2.334" : "v1.2.335")) };
            valid.Headers.ETag = new EntityTagHeaderValue("\"validated\""); return valid;
        }));
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334));
        await service.CheckAsync(); await service.CheckAsync();
        Assert.Null(service.State.Release); Assert.Null(service.State.Error);
        await service.CheckAsync(); var release = service.State.Release; Assert.NotNull(release);
        await service.CheckAsync(); Assert.NotNull(service.State.Error);
        await service.CheckAsync(); Assert.Same(release, service.State.Release); Assert.Null(service.State.Error);
    }

    private sealed class ResponseWire(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
    }

    [Fact]
    public async Task AutomaticTickWaitsForTheInFlightManualRateLimitResult()
    {
        using var clock = new Clock(); using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = new AppUpdateService(http, Path.GetTempPath(), new Version(1, 2, 334), timeProvider: clock);
        using var stop = new CancellationTokenSource(); var loop = service.RunAutomaticChecksAsync(stop.Token);
        try
        {
            var startup = await clock.NextAsync();
            wire.Limited = true; wire.Hold = true;
            var manual = service.CheckAsync(); await wire.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            startup.Fire();
            wire.Release.TrySetResult(); await manual;
            var next = await clock.NextAsync();
            Assert.Equal(TimeSpan.FromMinutes(45), next.Due);
            Assert.Equal(1, wire.Checks);
        }
        finally { wire.Release.TrySetResult(); stop.Cancel(); await loop.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualRateLimitProtectsAlreadyScheduledChecksEvenAfterADownload(bool download)
    {
        var directory = Path.Combine(Path.GetTempPath(), "silo-rate-limit-" + Guid.NewGuid().ToString("N"));
        using var clock = new Clock(); using var wire = new Wire(); using var http = new HttpClient(wire);
        var service = new AppUpdateService(http, directory, new Version(1, 2, 334), timeProvider: clock);
        using var stop = new CancellationTokenSource(); var loop = service.RunAutomaticChecksAsync(stop.Token);
        try
        {
            (await clock.NextAsync()).Fire();
            var regular = await clock.NextAsync(); Assert.Equal(TimeSpan.FromMinutes(15), regular.Due);
            wire.Limited = true; await service.CheckAsync(); Assert.Equal(2, wire.Checks);
            if (download)
            {
                await service.DownloadAsync(); Assert.NotNull(service.State.ReadyInstaller);
                Assert.Null(service.State.Error); // Download success must not erase GitHub's cooldown.
            }
            await service.CheckAsync(); Assert.Equal(2, wire.Checks); // Manual checks also respect it.
            regular.Fire(); var retry = await clock.NextAsync();
            Assert.Equal(2, wire.Checks); Assert.Equal(TimeSpan.FromMinutes(30), retry.Due);
            wire.Limited = false; retry.Fire();
            var after = await clock.NextAsync(); Assert.Equal(TimeSpan.FromMinutes(15), after.Due);
            Assert.Equal(3, wire.Checks); Assert.Null(service.State.Error);
            Assert.NotNull(service.State.Release);
            if (download) { Assert.NotNull(service.State.ReadyInstaller); Assert.Equal(1, wire.Downloads); }
        }
        finally { stop.Cancel(); await loop.WaitAsync(TimeSpan.FromSeconds(5)); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class Wire : HttpMessageHandler
    {
        private static readonly byte[] Package = "Synthetic update; never executed"u8.ToArray();
        public bool Limited, Hold;
        public int Checks, Downloads;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(request.Headers.Authorization);
            if (request.RequestUri!.Host == "github.com")
            { Downloads++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Package) }; }
            Assert.Equal("api.github.com", request.RequestUri.Host); Checks++;
            if (Hold) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            if (Limited)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(45)); return limited;
            }
            if (request.Headers.IfNoneMatch.Count > 0) return new HttpResponseMessage(HttpStatusCode.NotModified);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(AppUpdateTests.Release(digest: "sha256:" + Convert.ToHexString(SHA256.HashData(Package)), size: Package.Length), Encoding.UTF8, "application/json") };
            response.Headers.ETag = new EntityTagHeaderValue("\"cached-release\""); return response;
        }
    }

    private sealed class Clock : TimeProvider, IDisposable
    {
        private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
        private readonly Channel<Pending> _pending = Channel.CreateUnbounded<Pending>();
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Pending(this, callback, state, dueTime, GetUtcNow() + dueTime);
            _pending.Writer.TryWrite(timer); return timer;
        }
        public async Task<Pending> NextAsync()
        {
            while (true)
            {
                var timer = await _pending.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                if (!timer.IsDisposed) return timer;
            }
        }
        public void Dispose() { }
        public sealed class Pending(Clock clock, TimerCallback callback, object? state, TimeSpan due, DateTimeOffset at) : ITimer
        {
            public TimeSpan Due => due;
            private volatile bool _disposed;
            public bool IsDisposed => _disposed;
            public void Fire() { if (_disposed) return; Interlocked.Exchange(ref clock._ticks, Math.Max(clock.GetUtcNow().UtcTicks, at.UtcTicks)); callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
