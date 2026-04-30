using System.Net;
using System.Text;
using ContinuumPlayer.Core.Api;

namespace ContinuumPlayer.Tests;

public sealed class ContinuumApiClientTests
{
    [Fact]
    public async Task GetAsync_DisposesSuccessfulResponse()
    {
        TrackingResponse? response = null;
        var handler = new DelegateHandler((_, _) =>
        {
            response = JsonResponse(HttpStatusCode.OK, """{"name":"ok"}""");
            return Task.FromResult<HttpResponseMessage>(response);
        });

        var client = CreateClient(handler);

        var result = await client.GetAsync<TestDto>("/api/v1/test");

        Assert.Equal("ok", result.Name);
        Assert.NotNull(response);
        Assert.True(response.IsDisposed);
    }

    [Fact]
    public async Task ConcurrentUnauthorizedResponses_ShareOneRefresh()
    {
        var refreshCount = 0;
        var handler = new DelegateHandler((request, _) =>
        {
            var token = request.Headers.Authorization?.Parameter;
            return Task.FromResult<HttpResponseMessage>(
                token == "new-token"
                    ? JsonResponse(HttpStatusCode.OK, """{"name":"ok"}""")
                    : JsonResponse(HttpStatusCode.Unauthorized, """{"error":"unauthorized","message":"expired"}"""));
        });

        var client = CreateClient(handler);
        client.SetAccessToken("old-token");
        client.SetTokenRefresher(async _ =>
        {
            Interlocked.Increment(ref refreshCount);
            await Task.Delay(25);
            client.SetAccessToken("new-token");
            return true;
        });

        var calls = Enumerable.Range(0, 8)
            .Select(_ => client.GetAsync<TestDto>("/api/v1/test"))
            .ToArray();

        var results = await Task.WhenAll(calls);

        Assert.All(results, result => Assert.Equal("ok", result.Name));
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public async Task DictionaryBodyPreservesExplicitSnakeCaseKeys()
    {
        string? sentJson = null;
        var handler = new DelegateHandler(async (request, ct) =>
        {
            sentJson = request.Content == null
                ? null
                : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        var client = CreateClient(handler);

        await client.PostNoContentAsync("/api/v1/test", new Dictionary<string, object?>
        {
            ["audio_track_index"] = 2,
            ["is_paused"] = false,
        });

        Assert.Equal("""{"audio_track_index":2,"is_paused":false}""", sentJson);
    }

    private static ContinuumApiClient CreateClient(HttpMessageHandler handler)
    {
        var client = new ContinuumApiClient(new HttpClient(handler));
        client.SetBaseUrl("https://example.test");
        return client;
    }

    private static TrackingResponse JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new TrackingResponse(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class TestDto
    {
        public string Name { get; set; } = "";
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }

    private sealed class TrackingResponse : HttpResponseMessage
    {
        public TrackingResponse(HttpStatusCode statusCode)
            : base(statusCode)
        {
        }

        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
