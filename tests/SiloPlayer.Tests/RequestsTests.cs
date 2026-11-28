using System.Net;
using SiloPlayer.Core.Api;

namespace SiloPlayer.Tests;

public sealed class RequestsTests
{
    [Fact]
    public async Task ConcurrentCallersShareStatusEvenWhenOneCancels()
    {
        using var wire = new StatusWire { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://status.fixture.invalid");
        var api = new RequestsApi(client);
        using var cancelled = new CancellationTokenSource();
        var first = api.GetStatusAsync(cancelled.Token);
        await wire.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = api.GetStatusAsync();
        cancelled.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first); }
        finally { wire.Hold!.TrySetResult(); }
        Assert.True((await second).RequestsEnabled);
        Assert.True((await api.GetStatusAsync()).RequestsEnabled);
        Assert.Equal(1, wire.Reads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InFlightStatusWaitersGetFreshStatusAfterInvalidation(bool catchUp, bool newerCaller)
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var wire = new StatusWire { Hold = hold };
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://status.fixture.invalid");
        var api = new RequestsApi(client);
        var publications = 0;
        api.StatusChanged += () => Interlocked.Increment(ref publications);
        var timeout = TimeSpan.FromSeconds(5);
        var first = api.GetStatusAsync();
        try
        {
            await wire.Arrived.Task.WaitAsync(timeout);
            var second = api.GetStatusAsync();
            if (catchUp)
                Assert.True(client.TryInvalidateAccessContext(client.CaptureContext(), out _, transportCatchUp: true));
            else
                api.InvalidateStatus();
            wire.Enabled = false;
            wire.Hold = null;
            var newer = newerCaller ? api.GetStatusAsync() : null;
            hold.TrySetResult();

            Assert.False((await first.WaitAsync(timeout)).RequestsEnabled);
            Assert.False((await second.WaitAsync(timeout)).RequestsEnabled);
            if (newer != null) Assert.False((await newer.WaitAsync(timeout)).RequestsEnabled);
            Assert.False((await api.GetStatusAsync()).RequestsEnabled);
            Assert.Equal(1, publications);
            Assert.Equal(2, wire.Reads);
        }
        finally { hold.TrySetResult(); }
    }

    [Fact]
    public async Task FreshStatusIsDroppedOnProfileAndAccessChanges()
    {
        using var wire = new StatusWire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://status.fixture.invalid");
        client.SetProfile("one");
        var api = new RequestsApi(client);
        Assert.True((await api.GetStatusAsync()).RequestsEnabled);
        Assert.True((await api.GetStatusAsync()).RequestsEnabled);
        wire.Enabled = false;
        client.SetProfile("two");
        Assert.False((await api.GetStatusAsync()).RequestsEnabled);
        Assert.False((await api.GetStatusAsync()).RequestsEnabled);
        wire.Enabled = true;
        client.InvalidateAccessContext();
        Assert.True((await api.GetStatusAsync()).RequestsEnabled);
        Assert.Equal(3, wire.Reads);
    }

    [Fact]
    public async Task RequestWriteInvalidatesTheFreshStatus()
    {
        using var wire = new StatusWire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://status.fixture.invalid");
        var api = new RequestsApi(client);
        Assert.True((await api.GetStatusAsync()).Allowed);
        await api.GetStatusAsync();
        Assert.Equal(1, wire.Reads);
        await api.CreateAsync(new());
        Assert.False((await api.GetStatusAsync()).Allowed);
        Assert.Equal(2, wire.Reads);
    }

    private sealed class StatusWire : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource? Hold;
        internal int Reads;
        internal bool Enabled = true;
        private bool _allowed = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v2/requests/status")
            {
                Interlocked.Increment(ref Reads);
                var status = System.Text.Json.JsonSerializer.Serialize(new { requests_enabled = Enabled, allowed = _allowed });
                var hold = Hold;
                Arrived.TrySetResult();
                if (hold != null) await hold.Task.WaitAsync(cancellationToken);
                return new(HttpStatusCode.OK) { Content = new StringContent(status) };
            }
            _allowed = false;
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    [Fact]
    public async Task CachedStatusExpiresAtThirtySecondsAndIsIsolatedFromCallerChanges()
    {
        using var wire = new StatusWire();
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://status.fixture.invalid");
        var clock = new StatusClock();
        var api = new RequestsApi(client, clock: clock);
        var status = await api.GetStatusAsync();
        status.RequestsEnabled = false;
        wire.Enabled = false;
        clock.Elapsed = TimeSpan.FromSeconds(29);
        Assert.True((await api.GetStatusAsync()).RequestsEnabled);
        Assert.Equal(1, wire.Reads);
        clock.Elapsed = TimeSpan.FromSeconds(30);
        Assert.False((await api.GetStatusAsync()).RequestsEnabled);
        Assert.Equal(2, wire.Reads);
    }

    private sealed class StatusClock : TimeProvider
    {
        internal TimeSpan Elapsed;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Elapsed.Ticks;
    }

    [Fact]
    public async Task SearchPreservesMediaTypeQueryAndPage()
    {
        var handler = new CaptureHandler("{\"page\":3,\"total_pages\":8,\"total_results\":150,\"results\":[]}");
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var result = await new RequestsApi(client).SearchAsync("series", "star wars", 3);

        Assert.Equal(3, result.Page);
        Assert.Equal(150, result.TotalResults);
        Assert.Equal("/api/v2/requests/search?q=star%20wars&media_type=series&page=3", handler.LastUri?.PathAndQuery);
    }

    [Fact]
    public async Task BrowseUsesCurrentKindSlugSortAndMediaContract()
    {
        var handler = new CaptureHandler("{\"kind\":\"genre\",\"slug\":\"science-fiction\",\"display_name\":\"Science Fiction\",\"media_type\":\"movie\",\"sort\":\"vote_average\",\"page\":2,\"total_pages\":4,\"results\":[]}");
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var result = await new RequestsApi(client).BrowseDiscoverAsync("genre", "science-fiction", "movie", "vote_average", 2);

        Assert.Equal("Science Fiction", result.DisplayName);
        Assert.Equal("/api/v2/requests/discover/browse/genre/science-fiction?sort=vote_average&page=2&media_type=movie", handler.LastUri?.PathAndQuery);
    }

    [Fact]
    public async Task DetailDeserializesRequestAndLibraryState()
    {
        var handler = new CaptureHandler("""
        {"media_type":"movie","tmdb_id":42,"title":"Example","availability":"available","library_content_id":"content-42","request":{"status":"completed","requestable":false,"reason":"already_available","request_id":"req-1"}}
        """);
        var client = new SiloApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");

        var result = await new RequestsApi(client).GetDetailAsync("movie", 42);

        Assert.Equal("content-42", result.LibraryContentId);
        Assert.Equal("completed", result.Request.Status);
        Assert.False(result.Request.Requestable);
    }

    private sealed class CaptureHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        }
    }
}
