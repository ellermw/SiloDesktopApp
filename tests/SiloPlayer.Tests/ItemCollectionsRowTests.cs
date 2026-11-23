using System.Net;
using System.Text;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

/// <summary>The "Also in" row of a movie or series page: the collections that hold the title (silo-server #4528).</summary>
public sealed class ItemCollectionsRowTests
{
    private const string TwoCollections = """
        {"items":[
          {"id":"c1","source":"library","title":"Marvel","poster_url":"https://img.invalid/c1.jpg","poster_is_collage":true,"item_count":23,"library_id":"4"},
          {"id":"c2","source":"user","title":"Weekend","poster_url":"","poster_is_collage":false,"item_count":3,"creator_profile_id":"owner-b"}],
         "preview_items":[]}
        """;
    private const string OneCollection = """
        {"items":[{"id":"c1","source":"library","title":"Marvel","poster_url":"","poster_is_collage":false,"item_count":9,"library_id":"4"}],
         "preview_items":[{"content_id":"movie:thor","type":"movie","title":"Thor"},{"content_id":"movie:avengers","type":"movie","title":"The Avengers"}]}
        """;
    private const string None = "{\"items\":[],\"preview_items\":[]}";

    [Fact]
    public async Task TheRequestIsTheTitleCollectionsReadWithTheLibraryOnlyWhenThePageHasOne()
    {
        var wire = new Wire { Reply = TwoCollections }; using var http = new HttpClient(wire);
        var api = new CatalogApi(Client(http));
        await api.GetItemCollectionsAsync("movie:heat-1995", 12);
        await api.GetItemCollectionsAsync("series:dark");
        Assert.Equal([("GET", "/api/v2/catalog/items/movie%3Aheat-1995/collections?library_id=12"), ("GET", "/api/v2/catalog/items/series%3Adark/collections")],
            wire.Requests.Select(request => (request.Method, request.Target)));
    }

    [Fact]
    public async Task TheReplyCarriesTheCardsAndPreviewTitlesInTheOfficialShape()
    {
        var wire = new Wire { Reply = TwoCollections }; using var http = new HttpClient(wire);
        var answer = await new CatalogApi(Client(http)).GetItemCollectionsAsync("film", 4);
        Assert.Equal(["library:c1", "user:c2"], answer.Items.Select(card => card.Key));
        var server = answer.Items[0]; var personal = answer.Items[1];
        Assert.Equal(("Marvel", 23, 4, false), (server.Title, server.ItemCount, server.Library, server.IsPersonal));
        Assert.True(server.PosterIsCollage);
        Assert.True(personal.IsPersonal); Assert.Null(personal.Library); Assert.Equal("owner-b", personal.CreatorProfileId);
        Assert.Empty(answer.PreviewItems);
        Assert.Equal(ItemCollectionsRowMode.AlsoIn, answer.Mode);
    }

    [Theory]
    [InlineData(None, ItemCollectionsRowMode.None)]
    [InlineData(TwoCollections, ItemCollectionsRowMode.AlsoIn)]
    [InlineData(OneCollection, ItemCollectionsRowMode.Preview)]
    public async Task OneCollectionWithTitlesToPreviewShowsThemTheOthersShowACardEach(string reply, ItemCollectionsRowMode mode)
    {
        var wire = new Wire { Reply = reply }; using var http = new HttpClient(wire);
        Assert.Equal(mode, (await new CatalogApi(Client(http)).GetItemCollectionsAsync("film")).Mode);
    }

    [Fact]
    public async Task OneCollectionWithNothingToPreviewFallsBackToTheCardRow()
    {
        var wire = new Wire { Reply = OneCollection.Replace("\"preview_items\":[{\"content_id\":\"movie:thor\",\"type\":\"movie\",\"title\":\"Thor\"},{\"content_id\":\"movie:avengers\",\"type\":\"movie\",\"title\":\"The Avengers\"}]", "\"preview_items\":[]") };
        using var http = new HttpClient(wire);
        var answer = await new CatalogApi(Client(http)).GetItemCollectionsAsync("film");
        Assert.Single(answer.Items); Assert.Empty(answer.PreviewItems);
        Assert.Equal(ItemCollectionsRowMode.AlsoIn, answer.Mode);
    }

    [Fact]
    public async Task OnlyAnotherProfilesSharedCollectionNamesItsOwner()
    {
        var wire = new Wire { Reply = TwoCollections }; using var http = new HttpClient(wire);
        var answer = await new CatalogApi(Client(http)).GetItemCollectionsAsync("film");
        var names = new Dictionary<string, string> { ["owner-b"] = "Sam" };
        Assert.Null(answer.Items[0].OwnerName("me", names));                 // a server collection
        Assert.Equal("Sam", answer.Items[1].OwnerName("me", names));         // another profile's
        Assert.Equal("Another profile", answer.Items[1].OwnerName("me", new Dictionary<string, string>())); // no longer listed
        Assert.Null(answer.Items[1].OwnerName("owner-b", names));            // the viewer's own
        Assert.True(answer.Items[1].NeedsOwnerName("me")); Assert.False(answer.Items[1].NeedsOwnerName("owner-b")); Assert.False(answer.Items[0].NeedsOwnerName("me"));
    }

    [Fact]
    public async Task AReplyThatArrivesForAnotherProfileIsDropped()
    {
        var wire = new Wire { Reply = TwoCollections, Hold = true }; using var http = new HttpClient(wire); var client = Client(http);
        var read = new CatalogApi(client).GetItemCollectionsAsync("film", 4);
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        client.SetProfile("beta");
        wire.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task TheRowShowsForAMovieWithCollectionsAndReadsWithThePageLibrary()
    {
        var wire = new Wire { Reply = TwoCollections }; using var http = new HttpClient(wire);
        var vm = Vm(http, "movie", "film", 12);
        await vm.RefreshItemCollectionsAsync();
        Assert.Equal(ItemCollectionsRowMode.AlsoIn, vm.AlsoIn?.Mode);
        Assert.Equal(2, vm.AlsoIn!.Items.Count);
        Assert.Equal("/api/v2/catalog/items/film/collections?library_id=12", Assert.Single(wire.Requests).Target);
    }

    [Fact]
    public async Task ASeriesWithoutALibraryReadsWithoutOne()
    {
        var wire = new Wire { Reply = TwoCollections }; using var http = new HttpClient(wire);
        var vm = Vm(http, "series", "show", null);
        await vm.RefreshItemCollectionsAsync();
        Assert.NotNull(vm.AlsoIn);
        Assert.Equal("/api/v2/catalog/items/show/collections", Assert.Single(wire.Requests).Target);
    }

    [Theory]
    [InlineData("episode")] [InlineData("season")] [InlineData("audiobook")] [InlineData("ebook")] [InlineData("manga")]
    public async Task OnlyMoviesAndSeriesAskForCollections(string type)
    {
        var wire = new Wire { Reply = TwoCollections }; using var http = new HttpClient(wire);
        var vm = Vm(http, type, "x", 12);
        await vm.RefreshItemCollectionsAsync();
        Assert.Null(vm.AlsoIn); Assert.Empty(wire.Requests);
    }

    [Fact]
    public async Task NoCollectionsMeansNoRow()
    {
        var wire = new Wire { Reply = None }; using var http = new HttpClient(wire);
        var vm = Vm(http, "movie", "film", 12);
        await vm.RefreshItemCollectionsAsync();
        Assert.Null(vm.AlsoIn); Assert.Single(wire.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)] [InlineData(HttpStatusCode.InternalServerError)] [InlineData(HttpStatusCode.ServiceUnavailable)] [InlineData(HttpStatusCode.Forbidden)]
    public async Task AFailedFirstReadShowsNoRowAndNoError(HttpStatusCode status)
    {
        var wire = new Wire { Reply = "{\"type\":\"about:blank\",\"title\":\"x\",\"status\":" + (int)status + "}", Status = status }; using var http = new HttpClient(wire);
        var vm = Vm(http, "movie", "film", 12);
        await vm.RefreshItemCollectionsAsync();
        Assert.Null(vm.AlsoIn); Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task AFailedRefetchKeepsTheLastAnswerAndAnEmptyOneRemovesIt()
    {
        var wire = new Wire { Reply = TwoCollections }; using var http = new HttpClient(wire);
        var vm = Vm(http, "movie", "film", 12);
        await vm.RefreshItemCollectionsAsync();
        var shown = vm.AlsoIn; Assert.NotNull(shown);
        wire.Status = HttpStatusCode.InternalServerError; wire.Reply = "{\"type\":\"about:blank\",\"title\":\"x\",\"status\":500}";
        await vm.RefreshItemCollectionsAsync();
        Assert.Same(shown, vm.AlsoIn);
        wire.Status = HttpStatusCode.OK; wire.Reply = None;
        await vm.RefreshItemCollectionsAsync();
        Assert.Null(vm.AlsoIn);
    }

    [Fact]
    public async Task TheReadRunsBesideThePageAndItsReplyIsDroppedWhenTheViewerMovesToAnotherTitle()
    {
        var wire = new Wire { Reply = TwoCollections, Hold = true }; using var http = new HttpClient(wire);
        var vm = Vm(http, "movie", "film", 12);
        var read = vm.RefreshItemCollectionsAsync();
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(read.IsCompleted); Assert.Null(vm.AlsoIn);
        vm.Item = new MediaItemDetail { ContentId = "other", Type = "movie" };
        wire.Release.TrySetResult();
        await read.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(vm.AlsoIn);
    }

    [Fact]
    public async Task AReplyForAnotherProfileOrAccountIsDropped()
    {
        var wire = new Wire { Reply = TwoCollections, Hold = true }; using var http = new HttpClient(wire); var client = Client(http);
        var vm = Vm(http, "movie", "film", 12, client);
        var read = vm.RefreshItemCollectionsAsync();
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        client.SetProfile("beta");
        wire.Release.TrySetResult();
        await read.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(vm.AlsoIn);
    }

    [Fact]
    public async Task LeavingThePageCancelsTheReadAndDropsTheAnswer()
    {
        var wire = new Wire { Reply = TwoCollections, Hold = true }; using var http = new HttpClient(wire);
        var vm = Vm(http, "movie", "film", 12);
        var read = vm.RefreshItemCollectionsAsync();
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.CancelPendingLoads();
        await wire.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        wire.Release.TrySetResult();
        await read.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(vm.AlsoIn);
        // A shown row goes with the page too.
        wire.Hold = false;
        var shown = Vm(http, "movie", "film", 12);
        await shown.RefreshItemCollectionsAsync(); Assert.NotNull(shown.AlsoIn);
        shown.CancelPendingLoads(); Assert.Null(shown.AlsoIn);
    }

    [Fact]
    public async Task ANewerReadSupersedesAnOlderOne()
    {
        var wire = new Wire { Reply = TwoCollections, Hold = true }; using var http = new HttpClient(wire);
        var vm = Vm(http, "movie", "film", 12);
        var first = vm.RefreshItemCollectionsAsync();
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        wire.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        wire.Reply = OneCollection;
        var second = vm.RefreshItemCollectionsAsync();
        await wire.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        wire.Release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(ItemCollectionsRowMode.Preview, vm.AlsoIn?.Mode);
    }

    [Fact]
    public void TheRowSitsWhereTheWebUiPutsItAndTheViewModelFeedsIt()
    {
        var xaml = Source("src", "SiloPlayer", "Views", "ItemDetailPage.xaml");
        Assert.True(xaml.IndexOf("x:Name=\"BoxSetSection\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"AlsoInSection\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"AlsoInSection\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"SimilarSection\"", StringComparison.Ordinal));
        var page = Source("src", "SiloPlayer", "Views", "ItemDetailPage.xaml.cs");
        // Movie: ... box set, Also in, More Like This. Series: ... cast and crew, Also in, More Like This.
        Assert.Contains("CastSection, CrewSection, BoxSetSection, AlsoInSection, SimilarSection,", page);
        Assert.Matches("CastSection, CrewSection, AlsoInSection, SimilarSection,\\s+\\],\\s+\"episode\"", page);
        Assert.Contains("nameof(ViewModel.AlsoIn)", page);
        var model = Source("src", "SiloPlayer", "ViewModels", "ItemDetailViewModel.cs");
        Assert.Contains("_ = RefreshItemCollectionsAsync();", model);
        Assert.Contains("RetireItemCollections();", model);
    }

    private static string Source(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "SiloPlayer.sln"))) dir = Directory.GetParent(dir)?.FullName ?? "";
        return File.ReadAllText(Path.Combine([dir, .. parts]));
    }

    private static ItemDetailViewModel Vm(HttpClient http, string type, string id, int? library, SiloApiClient? client = null)
    {
        var vm = new ItemDetailViewModel(new CatalogApi(client ?? Client(http)), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        typeof(ItemDetailViewModel).GetProperty(nameof(vm.LibraryId))!.SetValue(vm, library);
        vm.Item = new MediaItemDetail { ContentId = id, Type = type };
        return vm;
    }

    private static SiloApiClient Client(HttpClient http) { var client = new SiloApiClient(http); client.SetBaseUrl("https://collections.invalid"); client.SetProfile("alpha"); return client; }

    private sealed class Wire : HttpMessageHandler
    {
        internal sealed record Seen(string Method, string Target);
        internal readonly List<Seen> Requests = [];
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal string Reply = None;
        internal bool Hold;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(request.Method.Method, request.RequestUri!.PathAndQuery));
            if (Hold)
            {
                Started.TrySetResult();
                using var registration = ct.Register(() => Cancelled.TrySetResult());
                var released = Release.Task;
                var gate = await Task.WhenAny(released, Task.Delay(Timeout.Infinite, ct));
                if (gate != released) ct.ThrowIfCancellationRequested();
            }
            return new HttpResponseMessage(Status) { Content = new StringContent(Reply, Encoding.UTF8, Status == HttpStatusCode.OK ? "application/json" : "application/problem+json") };
        }
    }
}
