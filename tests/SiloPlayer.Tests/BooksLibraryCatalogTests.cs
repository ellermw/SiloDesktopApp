using System.Net;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class BooksLibraryCatalogTests
{
    [Theory]
    [InlineData("audiobooks", "audiobook")]
    [InlineData("ebooks", "ebook")]
    [InlineData("manga", "manga")]
    public async Task LibraryLoadKeepsBookMetadataAndDetailTarget(string libraryType, string itemType)
    {
        using var wire = new Wire(itemType);
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://books-library.invalid");
        var viewModel = new LibraryViewModel(new CatalogApi(client))
        {
            Library = new Library { Id = 17, Type = libraryType },
        };
        await viewModel.LoadCommand.ExecuteAsync(null);
        var item = Assert.IsType<SiloPlayer.Core.Models.Home.MediaItem>(viewModel.GetWindowItem(0));
        Assert.Equal(itemType, item.Type);
        Assert.Equal("book-detail", item.ContentId);
        Assert.Equal(new[] { "Avery Writer", "Morgan Writer" }, item.Authors);
        Assert.Equal(3600d, item.PositionSeconds);
        Assert.Equal(14400d, item.DurationSeconds);
        Assert.Equal(12, item.MangaVolumeCount);
        Assert.Equal(3, item.MangaChapterCount);
        Assert.All(wire.Requests, uri => Assert.Contains("library_id=17", uri.Query));
        await viewModel.EnsureRangeLoadedAsync(0, 0);
        Assert.Same(item, viewModel.GetWindowItem(0));
    }

    [Theory]
    [InlineData("audiobooks", "audiobook", "narrator")]
    [InlineData("ebooks", "ebook", "author")]
    [InlineData("manga", "manga", "author")]
    public async Task BookFiltersAndSortReachLibraryCatalog(string libraryType, string itemType, string sort)
    {
        using var wire = new Wire(itemType);
        using var http = new HttpClient(wire);
        var client = new SiloApiClient(http);
        client.SetBaseUrl("https://books-library.invalid");
        var viewModel = new LibraryViewModel(new CatalogApi(client))
        {
            Library = new Library { Id = 17, Type = libraryType },
            SelectedSort = sort,
            SelectedAuthor = "Avery Writer",
            SelectedSeries = libraryType == "manga" ? null : "The Journey",
            SelectedWatchStatus = "in_progress",
        };
        await viewModel.ApplyFilterCommand.ExecuteAsync(null);
        var query = wire.Requests.First().Query;
        Assert.Contains("sort=" + sort, query);
        var decoded = Uri.UnescapeDataString(query);
        Assert.Contains("\"field\":\"author\"", decoded);
        Assert.Contains("\"field\":\"in_progress\"", decoded);
        Assert.DoesNotContain("\"field\":\"narrator\"", decoded);
        Assert.Null(viewModel.ErrorMessage);
    }

    private sealed class Wire(string itemType) : HttpMessageHandler
    {
        internal List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/api/v2/catalog")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            Requests.Add(request.RequestUri!);
            var body = JsonSerializer.Serialize(new
            {
                items = new[] { new { content_id = "book-detail", type = itemType, title = "A journey", status = "matched",
                    authors = new[] { "Avery Writer", "Morgan Writer" }, position_seconds = 3600, duration_seconds = 14400,
                    manga_volume_count = 12, manga_chapter_count = 3 } },
                total = 1,
                page = new { has_more = false },
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
