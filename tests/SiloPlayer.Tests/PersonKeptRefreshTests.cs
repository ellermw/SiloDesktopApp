using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

/// <summary>
/// A person page kept for Back rereads in place when something changed while it was away: the
/// filmography already shown stays mounted (same length, same cards where nothing changed) and a
/// title marked watched elsewhere shows as watched. It used to reload from the top on every Back.
/// </summary>
public sealed class PersonKeptRefreshTests
{
    [Fact]
    public async Task RefreshInPlaceKeepsTheShownFilmographyAndShowsAChangeMadeElsewhere()
    {
        using var wire = new Wire();
        var vm = wire.Model();
        await vm.LoadCommand.ExecuteAsync("person-7");
        await vm.LoadMoreFilmographyCommand.ExecuteAsync(null);
        Assert.Equal(120, vm.Filmography.Count);
        var unchanged = vm.Filmography[0];
        var resets = 0;
        vm.Filmography.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) resets++; };
        wire.Played.Add("film-3"); // Marked watched on the title's page.
        wire.Name = "Renamed person";
        var reads = wire.FilmographyReads.Count;

        await vm.RefreshInPlaceAsync();

        Assert.Equal(120, vm.Filmography.Count);
        Assert.Equal(0, resets);
        Assert.Same(unchanged, vm.Filmography[0]);
        Assert.True(vm.Filmography[3].UserState?.Played);
        Assert.Equal("Renamed person", vm.Person?.Name);
        Assert.Equal(new[] { 0, 60 }, wire.FilmographyReads.Skip(reads));
        Assert.False(vm.IsLoading);
        Assert.False(vm.IsLoadingFilmography);
        Assert.True(vm.FilmographyHasMore);
        vm.Cancel();
    }

    [Fact]
    public async Task AFailedRefreshKeepsWhatIsShown()
    {
        using var wire = new Wire();
        var vm = wire.Model();
        await vm.LoadCommand.ExecuteAsync("person-7");
        var shown = vm.Filmography.ToList();
        wire.Fail = true;

        await vm.RefreshInPlaceAsync();

        Assert.Equal(shown, vm.Filmography);
        Assert.Equal("Person 7", vm.Person?.Name);
        Assert.Null(vm.ErrorMessage);
        vm.Cancel();
    }

    [Fact]
    public async Task AnIdentityChangeDuringTheRefreshPublishesNothing()
    {
        using var wire = new Wire();
        var vm = wire.Model();
        await vm.LoadCommand.ExecuteAsync("person-7");
        wire.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.Played.Add("film-1");
        var refresh = vm.RefreshInPlaceAsync();
        wire.Client.SetProfile("another-profile");
        wire.Hold.SetResult();
        await refresh;

        Assert.NotEqual(true, vm.Filmography[1].UserState?.Played);
        vm.Cancel();
    }

    private sealed class Wire : HttpMessageHandler
    {
        private readonly HttpClient _http;
        internal readonly SiloApiClient Client;
        internal readonly HashSet<string> Played = [];
        internal readonly List<int> FilmographyReads = [];
        internal string Name = "Person 7";
        internal bool Fail;
        internal TaskCompletionSource? Hold;
        private const int Total = 130;

        internal Wire()
        {
            _http = new HttpClient(this, disposeHandler: false);
            Client = new SiloApiClient(_http);
            Client.SetBaseUrl("https://person-kept.invalid");
        }

        internal PersonDetailViewModel Model() => new(new PeopleApi(Client), new CatalogApi(Client), Client);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Hold is { } hold) await hold.Task;
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"detail\":\"Synthetic outage\"}") };
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v2/catalog/people/person-7")
                return Reply(new { id = "person-7", name = Name, bio = "Complete biography.", photo_url = "https://person-kept.invalid/photo.png", birth_date = "1970-01-01" });
            if (path == "/api/v2/catalog")
            {
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                    .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
                var seek = int.Parse(query["seek"]); var limit = int.Parse(query["limit"]);
                lock (FilmographyReads) FilmographyReads.Add(seek);
                var count = Math.Max(0, Math.Min(limit, Total - seek));
                return Reply(new
                {
                    items = Enumerable.Range(seek, count).Select(index => new
                    {
                        content_id = "film-" + index, title = "Film " + index, type = "movie", year = 2000 + index % 20,
                        user_state = new { played = Played.Contains("film-" + index) },
                    }),
                    total = Total, total_exact = true, page = new { has_more = seek + count < Total },
                });
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }

        private static HttpResponseMessage Reply(object body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") };

        protected override void Dispose(bool disposing)
        {
            if (disposing) _http.Dispose();
            base.Dispose(disposing);
        }
    }
}
