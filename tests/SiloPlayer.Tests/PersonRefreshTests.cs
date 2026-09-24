using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class PersonRefreshTests
{
    [Theory]
    [InlineData("\"person-a\"", "person-a")]
    [InlineData("7", "7")]
    public async Task RefreshReceiptPreservesOpaqueAndLegacyNumericPersonIds(string jsonId, string expected)
    {
        using var http = new HttpClient(new Handler(_ => Json("{\"status\":\"queued\",\"person_id\":" + jsonId + "}")));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://server.test");
        Assert.Equal(expected, (await new PeopleApi(client).RefreshPersonAsync(expected)).PersonId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenPersonReceivesDelayedMetadataWithoutResettingFilmography(bool initiallyComplete)
    {
        int reads = 0, refreshes = 0, catalogReads = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref refreshes);
                return Json("{\"status\":\"queued\",\"person_id\":\"person-a\"}");
            }
            if (request.RequestUri!.AbsolutePath == "/api/v2/catalog")
            {
                Interlocked.Increment(ref catalogReads);
                return Json("{\"items\":[{\"content_id\":\"film\",\"title\":\"Film\"}],\"total\":1}");
            }
            return Json(Interlocked.Increment(ref reads) == 1
                ? initiallyComplete
                    ? "{\"id\":\"person-a\",\"name\":\"Person\",\"bio\":\"Original\",\"birth_date\":\"1980-01-01\",\"photo_url\":\"https://images.test/old.jpg\"}"
                    : "{\"id\":\"person-a\",\"name\":\"Person\"}"
                : "{\"id\":\"person-a\",\"name\":\"Person\",\"bio\":\"Arrived later\",\"birth_date\":\"1980-01-01\",\"photo_url\":\"https://images.test/new.jpg\"}");
        }));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://server.test");
        var vm = new PersonDetailViewModel(new PeopleApi(client), new CatalogApi(client), client);
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Person) && vm.Person?.Bio == "Arrived later") updated.TrySetResult();
        };
        try
        {
            await vm.LoadCommand.ExecuteAsync("person-a");
            var film = Assert.Single(vm.Filmography);
            await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("https://images.test/new.jpg", vm.Person!.PhotoUrl);
            Assert.Same(film, Assert.Single(vm.Filmography));
            Assert.Equal(initiallyComplete ? 0 : 1, refreshes);
            Assert.Equal(1, catalogReads);
            Assert.False(vm.IsLoading);
        }
        finally { vm.Cancel(); }
    }

    internal static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    [Theory]
    [InlineData("server")]
    [InlineData("profile")]
    [InlineData("cancel")]
    public async Task LatePersonRefreshCannotEscapeItsPageContext(string change)
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new AsyncHandler(_ =>
        {
            requested.TrySetResult();
            return response.Task;
        }));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://server.test");
        using var cts = new CancellationTokenSource();
        await using var observer = new PeopleApi(client).ObservePersonRefreshAsync(new Person
        {
            Id = "person-a", Bio = "Initial", BirthDate = "1980-01-01", PhotoUrl = "https://images.test/old.jpg"
        }, cts.Token).GetAsyncEnumerator();
        var pending = observer.MoveNextAsync().AsTask();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (change == "server") client.SetBaseUrl("https://other-server.test");
        else if (change == "profile") client.SetProfile("another-profile");
        else cts.Cancel();
        response.SetResult(Json("{\"id\":\"person-a\",\"bio\":\"Stale\"}"));
        if (change == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        else Assert.False(await pending);
    }

    [Fact]
    public async Task FailedRefreshReadKeepsPollingWithoutRepeatingRefreshRequest()
    {
        int posts = 0, reads = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++;
                return new HttpResponseMessage(HttpStatusCode.Forbidden)
                { Content = new StringContent("{\"message\":\"Denied\"}") };
            }
            return ++reads == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent("{\"message\":\"Temporarily unavailable\"}") }
                : Json("{\"id\":\"person-a\",\"bio\":\"Recovered\"}");
        }));
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://server.test");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var observer = new PeopleApi(client).ObservePersonRefreshAsync(new Person { Id = "person-a" }, cts.Token).GetAsyncEnumerator();
        Assert.True(await observer.MoveNextAsync());
        Assert.Equal("Recovered", observer.Current.Bio);
        Assert.Equal(1, posts);
        Assert.Equal(2, reads);
    }

    internal sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }

    internal sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
