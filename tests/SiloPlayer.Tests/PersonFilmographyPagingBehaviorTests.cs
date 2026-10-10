using System.Net;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class PersonFilmographyPagingBehaviorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonAdvancingPageRetainsRowsAndStopsAutomaticRetry(bool repeats)
    {
        using var wire = new Wire { RepeatPage = repeats }; var vm = wire.Model();
        await vm.FilterCommand.ExecuteAsync("all");
        await vm.LoadMoreFilmographyCommand.ExecuteAsync(null);
        await vm.LoadMoreFilmographyCommand.ExecuteAsync(null);
        Assert.Equal(2, wire.Reads);
        Assert.Single(vm.Filmography);
        Assert.False(vm.FilmographyHasMore);
        Assert.NotNull(vm.ErrorMessage);
        wire.Exhausted = true;
        await vm.FilterCommand.ExecuteAsync("series");
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.FilmographyHasMore);
        vm.Cancel();
    }

    [Fact]
    public async Task LateContinuationCannotAppendUnderAnotherProfile()
    {
        using var wire = new Wire { HoldMore = true }; var vm = wire.Model();
        await vm.FilterCommand.ExecuteAsync("all");
        var pending = vm.LoadMoreFilmographyCommand.ExecuteAsync(null);
        try
        {
            await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            wire.Client!.SetProfile("other"); wire.Release.TrySetResult(); await pending;
            Assert.Single(vm.Filmography);
            Assert.Null(vm.ErrorMessage);
        }
        finally { wire.Release.TrySetResult(); await pending; vm.Cancel(); }
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal bool RepeatPage, Exhausted, HoldMore;
        internal int Reads;
        internal SiloApiClient? Client;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HttpClient? _http;
        internal PersonDetailViewModel Model()
        {
            _http = new HttpClient(this, false); Client = new(_http); Client.SetBaseUrl("https://person-paging.invalid"); Client.SetProfile("original");
            return new(new PeopleApi(Client), new CatalogApi(Client), Client) { Person = new Person { Id = "fixture", Name = "Fixture" } };
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.Host != "person-paging.invalid") throw new InvalidOperationException("External fixture request.");
            Reads++; var more = !request.RequestUri.Query.Contains("seek=0");
            if (more && HoldMore) { Started.TrySetResult(); await Release.Task.WaitAsync(ct); }
            var items = more ? HoldMore ? "[{\"content_id\":\"later\",\"title\":\"Late\"}]" : RepeatPage ? "[{\"content_id\":\"first\",\"title\":\"First\"}]" : "[]" : "[{\"content_id\":\"first\",\"title\":\"First\"}]";
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"items\":" + items + ",\"total\":" + (Exhausted ? 1 : 3) + "}") };
        }
        protected override void Dispose(bool disposing) { if (disposing) { Release.TrySetResult(); _http?.Dispose(); } base.Dispose(disposing); }
    }
}
