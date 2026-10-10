using System.Net;
using System.Text;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ShufflePlaybackTests
{
    [Theory]
    [InlineData("library")]
    [InlineData("series")]
    [InlineData("season")]
    [InlineData("library_collection")]
    [InlineData("user_collection")]
    public async Task CreateUsesExactlyTheSupportedScopeKinds(string kind)
    {
        using var fixture = new Fixture();
        await fixture.Api.CreateAsync(new(kind, "scope-id"));
        var request = Assert.Single(fixture.Handler.Requests);
        Assert.Equal("POST", request.Method); Assert.Equal("?image_size=large", request.Query);
        Assert.Equal(kind, request.Body.GetProperty("scope").GetProperty("kind").GetString());
        Assert.Equal("scope-id", request.Body.GetProperty("scope").GetProperty("id").GetString());
        Assert.Equal("fixture-profile", request.Profile);
    }

    [Fact]
    public async Task GenericCollectionIsRejectedWithoutAnHttpRequest()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Api.CreateAsync(new("collection", "1")));
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public async Task ShuffleReadsAndMutationsUseEscapedIdsExactBodiesAndLargeArtwork()
    {
        using var fixture = new Fixture();
        await fixture.Api.GetAsync("shuffle space"); await fixture.Api.AdvanceAsync("shuffle space", "current-id");
        await fixture.Api.SkipAsync("shuffle space", "next-id"); await fixture.Api.DeleteAsync("shuffle space");
        Assert.All(fixture.Handler.Requests, request => Assert.StartsWith("/api/v2/shuffles/shuffle%20space", request.Path));
        Assert.All(fixture.Handler.Requests.Take(3), request => Assert.Equal("?image_size=large", request.Query));
        Assert.Equal("current-id", fixture.Handler.Requests[1].Body.GetProperty("from_content_id").GetString());
        Assert.Equal("next-id", fixture.Handler.Requests[2].Body.GetProperty("next_content_id").GetString());
        Assert.Equal("DELETE", fixture.Handler.Requests[3].Method); Assert.Empty(fixture.Handler.Requests[3].Query);
    }

    [Fact]
    public async Task SingletonNextCannotAdvanceAndStopRetiresBeforeDeleteCompletes()
    {
        using var fixture = new Fixture(); fixture.Handler.Response = _ => Task.FromResult(Reply(Pick("one", "one")));
        await fixture.Controller.StartAsync(new("library", "1"));
        Assert.Null(fixture.Controller.NextFor("one")); Assert.Null(await fixture.Controller.AdvanceAsync("one"));
        Assert.Single(fixture.Handler.Requests);
        var delete = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = _ => delete.Task;
        var stopping = fixture.Controller.StopAsync();
        Assert.False(fixture.Controller.IsActive); Assert.Null(fixture.Controller.Snapshot);
        delete.SetResult(new(HttpStatusCode.NoContent)); await stopping;
    }

    [Fact]
    public async Task ExhaustedReadDropsCachedPickWhileOtherFailuresKeepTheServerPick()
    {
        using var fixture = new Fixture(); await fixture.Controller.StartAsync(new("series", "series-id"));
        fixture.Handler.Response = _ => Task.FromResult(Problem(HttpStatusCode.BadGateway));
        await Assert.ThrowsAsync<ApiException>(() => fixture.Controller.RefreshAsync());
        Assert.Equal("b", fixture.Controller.NextFor("a")!.ContentId); Assert.False(fixture.Controller.Exhausted);
        fixture.Handler.Response = _ => Task.FromResult(Problem(HttpStatusCode.Conflict));
        await fixture.Controller.RefreshAsync();
        Assert.True(fixture.Controller.Exhausted); Assert.Null(fixture.Controller.Snapshot); Assert.Null(fixture.Controller.NextFor("a"));
    }

    [Fact]
    public async Task FailedNewScopePreservesTheRunningShuffle()
    {
        using var fixture = new Fixture(); await fixture.Controller.StartAsync(new("series", "first"));
        fixture.Handler.Response = _ => Task.FromResult(Problem(HttpStatusCode.Conflict));
        await Assert.ThrowsAsync<ApiException>(() => fixture.Controller.StartAsync(new("season", "empty")));
        Assert.True(fixture.Controller.IsActive); Assert.Equal("b", fixture.Controller.NextFor("a")!.ContentId);
    }

    [Fact]
    public async Task LateAdvanceAfterCloseCannotReturnAPlayablePick()
    {
        using var fixture = new Fixture(); await fixture.Controller.StartAsync(new("series", "series-id"));
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = _ => reply.Task;
        var advancing = fixture.Controller.AdvanceAsync("a"); fixture.Controller.Leave(); reply.SetResult(Reply(Pick("b", "c")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => advancing);
        Assert.False(fixture.Controller.IsActive); Assert.Null(fixture.Controller.Snapshot);
    }

    [Fact]
    public async Task LateStartAfterCancellationOrProfileChangeCannotAttach()
    {
        using var fixture = new Fixture();
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = _ => reply.Task;
        var starting = fixture.Controller.StartAsync(new("series", "series-id"));
        fixture.Client.SetProfile("replacement-profile"); reply.SetResult(Reply(Pick("a", "b")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting); Assert.False(fixture.Controller.IsActive);
        using var cancellation = new CancellationTokenSource();
        fixture.Handler.Response = _ => { cancellation.Cancel(); return Task.FromResult(Reply(Pick("a", "b"))); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Controller.StartAsync(new("series", "series-id"), cancellation.Token));
        Assert.False(fixture.Controller.IsActive);
    }

    [Fact]
    public async Task OlderReadCannotOverwriteTheNewerPickAnotherResponse()
    {
        using var fixture = new Fixture(); await fixture.Controller.StartAsync(new("season", "season-id"));
        var read = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = request => request.Method == "GET" ? read.Task : Task.FromResult(Reply(Pick("a", "replacement")));
        var refreshing = fixture.Controller.RefreshAsync(); await fixture.Controller.PickAnotherAsync(); read.SetResult(Reply(Pick("a", "b")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refreshing);
        Assert.Equal("replacement", fixture.Controller.NextFor("a")!.ContentId);
        Assert.Equal("b", fixture.Handler.Requests.Last().Body.GetProperty("next_content_id").GetString());
    }

    [Fact]
    public async Task PostRollRefreshCannotRetireAnAdvanceAlreadyOnTheWire()
    {
        using var fixture = new Fixture(); await fixture.Controller.StartAsync(new("season", "season-id"));
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = request =>
        {
            if (!request.Path.EndsWith("/advance")) return Task.FromResult(Reply(Pick("a", "b")));
            started.SetResult(); return reply.Task;
        };
        var advancing = fixture.Controller.AdvanceAsync("a");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(fixture.Handler.Requests, request => request.Path.EndsWith("/advance"));

        // The old video's EOF can reopen the overlay and request a passive
        // read before the advance response arrives. It cannot own the switch.
        await fixture.Controller.RefreshAsync();
        Assert.DoesNotContain(fixture.Handler.Requests, request => request.Method == "GET");
        reply.SetResult(Reply(Pick("b", "c")));
        var advanced = await advancing;
        Assert.Equal("b", advanced!.Current.ContentId);
        Assert.Equal("b", fixture.Controller.Snapshot!.Current.ContentId);
        Assert.Equal("c", fixture.Controller.NextFor("b")!.ContentId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PassiveRefreshDoesNotRetireSkipButCloseStillDoes(bool close)
    {
        using var fixture = new Fixture(); await fixture.Controller.StartAsync(new("series", "series-id"));
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = request =>
        {
            if (!request.Path.EndsWith("/skip")) return Task.FromResult(Reply(Pick("a", "b")));
            started.SetResult(); return reply.Task;
        };
        var skipping = fixture.Controller.PickAnotherAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Controller.RefreshAsync();
        Assert.DoesNotContain(fixture.Handler.Requests, request => request.Method == "GET");
        if (close) fixture.Controller.Leave();
        reply.SetResult(Reply(Pick("a", "replacement")));
        if (close)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => skipping);
            Assert.False(fixture.Controller.IsActive); Assert.Null(fixture.Controller.Snapshot);
        }
        else
        {
            await skipping;
            Assert.Equal("replacement", fixture.Controller.NextFor("a")!.ContentId);
            // The completed mutation releases passive reads for the next open.
            fixture.Handler.Response = _ => Task.FromResult(Reply(Pick("a", "fresh")));
            await fixture.Controller.RefreshAsync();
            Assert.Equal("fresh", fixture.Controller.NextFor("a")!.ContentId);
        }
    }

    [Fact]
    public async Task ReadFromBeforeAdvanceCannotReplaceItsCommittedServerState()
    {
        using var fixture = new Fixture(); await fixture.Controller.StartAsync(new("series", "series-id"));
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = request => request.Method == "GET" ? reply.Task : Task.FromResult(Reply(Pick("b", "c")));
        var refreshing = fixture.Controller.RefreshAsync();
        var advanced = await fixture.Controller.AdvanceAsync("a");
        reply.SetResult(Reply(Pick("a", "b")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refreshing);
        Assert.Equal("b", advanced!.Current.ContentId);
        Assert.Equal("c", fixture.Controller.NextFor("b")!.ContentId);
    }

    private static Shuffle Pick(string current, string next) => new()
        { Id = "shuffle-id", Scope = new() { Kind = "season", Id = "season-id", Title = "Season 6", ParentTitle = "Example" }, Current = new MediaItem { ContentId = current }, Next = new MediaItem { ContentId = next } };
    private static HttpResponseMessage Reply(object body) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Problem(HttpStatusCode code) => new(code)
        { Content = new StringContent("{\"title\":\"Fixture shuffle failure\"}", Encoding.UTF8, "application/problem+json") };

    private sealed class Fixture : IDisposable
    {
        internal readonly Handler Handler = new();
        private readonly HttpClient _http;
        internal readonly SiloApiClient Client;
        internal readonly ShufflesApi Api;
        internal readonly ShufflePlaybackController Controller;
        internal Fixture()
        {
            _http = new(Handler); Client = new(_http); Client.SetBaseUrl("https://shuffle-fixture.invalid"); Client.SetProfile("fixture-profile");
            Api = new(Client); Controller = new(Api, Client);
        }
        public void Dispose() => _http.Dispose();
    }
    private sealed record Request(string Method, string Path, string Query, JsonElement Body, string? Profile);
    private sealed class Handler : HttpMessageHandler
    {
        internal readonly List<Request> Requests = [];
        internal Func<Request, Task<HttpResponseMessage>> Response = request => Task.FromResult(request.Method == "DELETE" ? new(HttpStatusCode.NoContent) : Reply(Pick("a", "b")));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? JsonSerializer.SerializeToElement(new { }) : JsonDocument.Parse(await request.Content.ReadAsStringAsync()).RootElement.Clone();
            var value = new Request(request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body,
                request.Headers.TryGetValues("X-Profile-Id", out var profiles) ? profiles.Single() : null);
            Requests.Add(value); return await Response(value);
        }
    }
}
