using System.Net;
using System.Net.Http.Headers;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class DirectStreamProxyTests
{
    [Fact]
    public async Task SignedNodeUrl_PreservesSignedQueryAndNeverSendsBearerToken()
    {
        var body = new byte[] { 1, 2, 3, 4 };
        var handler = new RecordingHandler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("?seek=12&token=node-token", request.RequestUri?.Query);
            return CreateResponse(body);
        });
        using var upstreamClient = new HttpClient(handler);
        using var proxy = new DirectStreamProxy(
            "https://proxy.example/stream/direct/signed-token?seek=12&token=node-token",
            () => "private-user-access-token",
            httpClient: upstreamClient);
        using var localClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var result = await localClient.GetByteArrayAsync(proxy.Start());

        Assert.Equal(body, result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task IntegratedUrl_StripsStaleQueryTokenAndUsesCurrentBearerToken()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("fresh-user-access-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("?seek=5", request.RequestUri?.Query);
            return CreateResponse(new byte[] { 5, 6, 7 });
        });
        using var upstreamClient = new HttpClient(handler);
        using var proxy = new DirectStreamProxy(
            "https://server.example/api/v1/stream/session?token=stale-token&seek=5",
            () => "fresh-user-access-token",
            httpClient: upstreamClient);
        using var localClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var result = await localClient.GetByteArrayAsync(proxy.Start());

        Assert.Equal(new byte[] { 5, 6, 7 }, result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task HeadRequest_IsForwardedWithoutAResponseBody()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            return CreateResponse(new byte[] { 8, 9, 10 });
        });
        using var upstreamClient = new HttpClient(handler);
        using var proxy = new DirectStreamProxy(
            "https://server.example/api/v1/stream/session",
            () => "access-token",
            httpClient: upstreamClient);
        using var localClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Head, proxy.Start());

        using var response = await localClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, response.Content.Headers.ContentLength);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SequentialMode_IgnoresClientRangeAndDoesNotAdvertiseByteRanges()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Null(request.Headers.Range);
            return CreateResponse(new byte[] { 11, 12, 13 });
        });
        using var upstreamClient = new HttpClient(handler);
        using var proxy = new DirectStreamProxy(
            "https://proxy.example/stream/remux/signed-token?seek=120",
            () => "private-user-access-token",
            httpClient: upstreamClient,
            supportsRanges: false);
        using var localClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var request = new HttpRequestMessage(HttpMethod.Get, proxy.Start());
        request.Headers.Range = new RangeHeaderValue(100, null);

        using var response = await localClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Headers.AcceptRanges);
        Assert.Equal(new byte[] { 11, 12, 13 }, await response.Content.ReadAsByteArrayAsync());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Dispose_CancelsAnActiveUpstreamRequestWithoutBlockingCaller()
    {
        using var handler = new BlockingHandler();
        using var upstreamClient = new HttpClient(handler);
        var proxy = new DirectStreamProxy(
            "https://server.example/api/v1/stream/session",
            () => "access-token",
            httpClient: upstreamClient);
        using var localClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        var localRequest = localClient.GetAsync(proxy.Start());
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var startedAt = DateTime.UtcNow;
        proxy.Dispose();
        var disposeElapsed = DateTime.UtcNow - startedAt;

        await handler.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<Exception>(async () => await localRequest);
        Assert.True(disposeElapsed < TimeSpan.FromSeconds(1), $"Dispose blocked for {disposeElapsed}.");
    }

    [Fact]
    public async Task LoopbackEndpoint_UsesCorrectMethodAndHeadErrorSemantics()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("Upstream should not be called."));
        using var upstreamClient = new HttpClient(handler);
        using var proxy = new DirectStreamProxy(
            "https://server.example/api/v1/stream/session",
            () => "access-token",
            httpClient: upstreamClient);
        using var localClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var localUrl = proxy.Start();

        using var post = new HttpRequestMessage(HttpMethod.Post, localUrl);
        using var postResponse = await localClient.SendAsync(post);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, postResponse.StatusCode);
        Assert.Contains("GET", postResponse.Content.Headers.Allow);
        Assert.Contains("HEAD", postResponse.Content.Headers.Allow);

        using var head = new HttpRequestMessage(HttpMethod.Head, localUrl.Replace("/stream", "/missing"));
        using var headResponse = await localClient.SendAsync(head);
        Assert.Equal(HttpStatusCode.NotFound, headResponse.StatusCode);
        Assert.Equal("Not Found".Length, headResponse.Content.Headers.ContentLength);
        Assert.Empty(await headResponse.Content.ReadAsByteArrayAsync());

        var localRoot = new Uri(localUrl).GetLeftPart(UriPartial.Authority);
        using var missingTokenResponse = await localClient.GetAsync(localRoot + "/stream");
        Assert.Equal(HttpStatusCode.NotFound, missingTokenResponse.StatusCode);
        Assert.Empty(handler.Requests);
    }

    private static HttpResponseMessage CreateResponse(byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        response.Headers.AcceptRanges.Add("bytes");
        return response;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The blocking request unexpectedly completed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }
}
