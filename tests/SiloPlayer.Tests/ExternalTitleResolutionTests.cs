using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class ExternalTitleResolutionTests
{
    [Theory]
    [InlineData(404, false)]
    [InlineData(403, false)]
    [InlineData(503, true)]
    public async Task FailedLibraryCheckRetainsExternalTitleAndOnlyTemporaryFailureOffersLibraryLink(int status, bool link)
    {
        var title = new RequestMediaDetail { LibraryContentId = "library:copy", Title = "Title" };
        var cache = new ItemDetailPrefetchCache((_, _) => Task.FromException<MediaItemDetail>(new ApiException("fixture", "Fixture", status)));
        var result = await ExternalTitleResolution.ResolveAsync(title, cache, CancellationToken.None);
        Assert.Null(result.LibraryItem);
        Assert.Equal(link ? "library:copy" : null, result.LibraryLink);
    }

    [Fact]
    public async Task AccessibleLibraryItemPromotesOnlyAfterActualReadAndNoCopyDoesNotQueryCatalog()
    {
        int calls = 0;
        var cache = new ItemDetailPrefetchCache((id, _) => { calls++; return Task.FromResult(new MediaItemDetail { ContentId = id }); });
        var absent = await ExternalTitleResolution.ResolveAsync(new(), cache, CancellationToken.None);
        Assert.Null(absent.LibraryItem); Assert.Equal(0, calls);
        var present = await ExternalTitleResolution.ResolveAsync(new() { LibraryContentId = "copy" }, cache, CancellationToken.None);
        Assert.Equal("copy", present.LibraryItem!.ContentId); Assert.Equal(1, calls);
    }
}
