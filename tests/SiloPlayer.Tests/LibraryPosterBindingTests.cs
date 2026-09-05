using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class LibraryPosterBindingTests
{
    [Fact]
    public void RenewedSignatureDoesNotClearTheSamePoster()
    {
        var old = new MediaItem { ContentId = "movie", PosterUrl = "https://example.test/poster?width=300&X-Amz-Signature=old" };
        var updated = new MediaItem { ContentId = "movie", PosterUrl = "https://example.test/poster?X-Amz-Signature=new&width=300" };
        Assert.False(LibraryPosterBinding.RequiresReload(old, updated));
        updated.PosterUrl = "https://example.test/poster?width=600&X-Amz-Signature=new";
        Assert.True(LibraryPosterBinding.RequiresReload(old, updated));
    }

    [Fact]
    public void MetadataRebindDoesNotRestartUnchangedArtwork()
    {
        var old = new MediaItem { ContentId = "movie", Title = "Old", PosterUrl = "https://example.test/poster" };
        var updated = new MediaItem { ContentId = "movie", Title = "New", PosterUrl = old.PosterUrl };
        Assert.False(LibraryPosterBinding.RequiresReload(old, updated));
    }

    [Theory]
    [InlineData("other", "https://example.test/poster", null)]
    [InlineData("movie", "https://example.test/new-poster", null)]
    [InlineData("movie", null, "https://example.test/poster")]
    public void RecycledOrChangedArtworkMustReload(string id, string? poster, string? backdrop)
    {
        var old = new MediaItem { ContentId = "movie", PosterUrl = "https://example.test/poster" };
        var updated = new MediaItem { ContentId = id, PosterUrl = poster, BackdropUrl = backdrop };
        Assert.True(LibraryPosterBinding.RequiresReload(old, updated));
    }
}
