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

    [Theory]
    [InlineData(180, 270, false, 225, 0)]   // a poster fills the frame across its width
    [InlineData(180, 270, true, 0, 338)]    // a backdrop fills it top to bottom: decoded by height
    [InlineData(180, 180, true, 0, 225)]    // square audiobook frame
    [InlineData(100, 150, false, 200, 0)]   // the small-card floor is unchanged
    [InlineData(600, 900, false, 640, 0)]
    [InlineData(600, 900, true, 0, 960)]
    public void ArtworkDecodesToFillItsFrame(double width, double height, bool backdrop, int decodeWidth, int decodeHeight)
    {
        Assert.Equal((decodeWidth, decodeHeight), LibraryPosterBinding.DecodeSize(width, height, backdrop));
    }
}
