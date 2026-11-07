using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class LibraryPosterBindingTests
{
    [Theory]
    [InlineData("https://example.test/poster?width=300&X-Amz-Signature=old", "https://example.test/poster?X-Amz-Signature=new&width=300", true)]
    [InlineData("https://example.test/backdrop?X-Goog-Signature=old", "https://example.test/backdrop?X-Goog-Signature=new", true)]
    [InlineData("https://example.test/poster?width=300", "https://example.test/poster?width=600", false)]
    [InlineData("https://example.test/poster", "https://example.test/other-poster", false)]
    [InlineData("https://example.test/poster", null, false)]
    [InlineData(null, null, true)]
    public void ArtworkIdentityIgnoresOnlyVolatileUrlParts(string? previous, string? next, bool same)
        => Assert.Equal(same, LibraryPosterBinding.HasSameArtworkUrl(previous, next));

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
