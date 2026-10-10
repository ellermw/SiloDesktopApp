using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class SimilarCardsBehaviorTests
{
    [Fact]
    public async Task SimilarCardsRetainCurrentCardDataWithoutFetchingEveryDetail()
    {
        using var wire = new Wire(); using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://similar-cards.invalid");
        var catalog = new CatalogApi(client);
        var vm = new ItemDetailViewModel(catalog, new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException("Unexpected detail read")));
        try
        {
            vm.Item = new MediaItemDetail { ContentId = "source", Type = "movie" };
            await vm.LoadSimilarCommand.ExecuteAsync(null);
            Assert.False(vm.SimilarLoadFailed);
            Assert.Equal(12, vm.SimilarItems.Count);
            Assert.Equal(1, wire.Requests);
            Assert.Equal("play-0", vm.SimilarItems[0].PlayContentId);
            Assert.Equal("Poster 0", vm.SimilarItems[0].Title);
            Assert.Equal("https://similar-cards.invalid/poster-0", vm.SimilarItems[0].PosterUrl);
        }
        finally { CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(vm); }
    }

    [Fact]
    public async Task SimilarCardsFromAnOldProfileCannotReplaceTheCurrentPage()
    {
        using var wire = new Wire { Hold = true }; using var http = new HttpClient(wire);
        var client = new SiloApiClient(http); client.SetBaseUrl("https://similar-cards.invalid");
        client.SetProfile("first");
        var vm = new ItemDetailViewModel(new CatalogApi(client), new ItemDetailPrefetchCache((_, _) => throw new InvalidOperationException()));
        try
        {
            vm.Item = new MediaItemDetail { ContentId = "source", Type = "series" };
            var pending = vm.LoadSimilarCommand.ExecuteAsync(null);
            await wire.Entered.Task;
            client.SetProfile("second");
            vm.SimilarItems.Add(new() { ContentId = "current-profile-card" });
            wire.Release.TrySetResult();
            await pending;
            Assert.Equal("current-profile-card", Assert.Single(vm.SimilarItems).ContentId);
            Assert.False(vm.SimilarLoadFailed);
        }
        finally { CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(vm); }
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int Requests;
        public bool Hold;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Entered.TrySetResult();
            if (Hold) await Release.Task;
            var similar = request.RequestUri!.AbsolutePath.StartsWith("/api/v2/recommendations/similar/");
            var json = similar ? JsonSerializer.Serialize(new { items = Enumerable.Range(0, 15).Select(i => new { content_id = "card-" + i, play_content_id = "play-" + i, title = "Poster " + i, poster_url = "https://similar-cards.invalid/poster-" + i, type = "movie" }) }) : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
