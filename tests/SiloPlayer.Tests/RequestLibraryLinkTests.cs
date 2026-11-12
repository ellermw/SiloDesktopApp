using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Official LibraryItemLink (a0ae0c34): a request's library_content_id is a global server match, not proof the
/// viewer can open it. A click reads the item first, stays with a notice on an authoritative not-found, navigates
/// on success or any other failure, and drops a check the viewer has moved on from.
/// </summary>
public sealed class RequestLibraryLinkTests
{
    [Fact]
    public async Task ReadableCopyOpensAndWarmsTheItemPageRead()
    {
        var calls = 0;
        var cache = new ItemDetailPrefetchCache((id, _) => { calls++; return Task.FromResult(new MediaItemDetail { ContentId = id }); });
        Assert.Equal(RequestLibraryAccess.Open, await RequestLibraryLinks.CheckAsync("movie-603", cache, () => true, CancellationToken.None));
        await cache.GetAsync("movie-603", CancellationToken.None);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(404, RequestLibraryAccess.Unavailable)]
    [InlineData(403, RequestLibraryAccess.Unavailable)]
    [InlineData(500, RequestLibraryAccess.Open)]
    [InlineData(503, RequestLibraryAccess.Open)]
    public async Task OnlyAuthoritativeNotFoundOrForbiddenStaysOnThePage(int status, RequestLibraryAccess expected)
    {
        var cache = new ItemDetailPrefetchCache((_, _) => Task.FromException<MediaItemDetail>(new ApiException("fixture", "Fixture", status)));
        Assert.Equal(expected, await RequestLibraryLinks.CheckAsync("movie-603", cache, () => true, CancellationToken.None));
    }

    [Fact]
    public async Task TransportFailureSaysNothingAboutAccessSoItNavigates()
    {
        var cache = new ItemDetailPrefetchCache((_, _) => Task.FromException<MediaItemDetail>(new HttpRequestException("offline")));
        Assert.Equal(RequestLibraryAccess.Open, await RequestLibraryLinks.CheckAsync("movie-603", cache, () => true, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChecksTheViewerHasMovedOnFromAreRetiredWhateverTheyFind(bool fails)
    {
        var gate = new TaskCompletionSource<MediaItemDetail>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ItemDetailPrefetchCache((_, _) => gate.Task);
        var current = true;
        var check = RequestLibraryLinks.CheckAsync("movie-603", cache, () => current, CancellationToken.None);
        current = false;
        if (fails) gate.SetException(new ApiException("fixture", "Fixture", 404)); else gate.SetResult(new MediaItemDetail { ContentId = "movie-603" });
        Assert.Equal(RequestLibraryAccess.Retired, await check);
    }

    [Fact]
    public async Task CancelledNavigationOwnerRetiresThePendingCheck()
    {
        var cache = new ItemDetailPrefetchCache((_, _) => new TaskCompletionSource<MediaItemDetail>().Task);
        using var owner = new CancellationTokenSource();
        var check = RequestLibraryLinks.CheckAsync("movie-603", cache, () => true, owner.Token);
        owner.Cancel();
        Assert.Equal(RequestLibraryAccess.Retired, await check);
    }

    [Fact]
    public async Task NoLibraryCopyNeverQueriesTheCatalog()
    {
        var calls = 0;
        var cache = new ItemDetailPrefetchCache((id, _) => { calls++; return Task.FromResult(new MediaItemDetail { ContentId = id }); });
        Assert.Equal(RequestLibraryAccess.Unavailable, await RequestLibraryLinks.CheckAsync(null, cache, () => true, CancellationToken.None));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ExplicitRequestLinksRouteThroughTheAccessCheckInsteadOfNavigatingDirectly()
    {
        var requests = ReadRepoFile("src", "SiloPlayer", "Views", "RequestsPage.xaml.cs");
        var detail = ReadRepoFile("src", "SiloPlayer", "Views", "RequestDetailPage.xaml.cs");
        Assert.Contains("OpenLibraryAsync(id)", requests);
        Assert.Contains("RequestLibraryLinks.CheckAsync(", requests);
        Assert.DoesNotContain("library.Click += (_, _) => App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(id)", requests);
        Assert.Contains("RequestLibraryLinks.CheckAsync(", detail);
        Assert.DoesNotContain("OpenLibrary_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string id }) App.Services.GetRequiredService<NavigationService>().Navigate<ItemDetailPage>(id); }", detail);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
