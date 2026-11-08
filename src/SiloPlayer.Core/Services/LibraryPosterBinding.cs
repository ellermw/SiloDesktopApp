using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public static class LibraryPosterBinding
{
    public static bool HasSameArtworkUrl(string? previous, string? next) => string.Equals(
        ImageService.NormalizeImageUrlForCache(previous ?? ""),
        ImageService.NormalizeImageUrlForCache(next ?? ""), StringComparison.Ordinal);

    /// <summary>
    /// The decode size of a card's artwork, filling its frame (UniformToFill) with 25 % headroom for
    /// the hover zoom and small resizes. A poster fills the frame across its width. A backdrop shown
    /// for an item without a poster is wider than the frame, so it fills it top to bottom and is
    /// decoded by height; decoding it by width scaled it up about twice and blurred it.
    /// Exactly one of the two is set; the other is 0.
    /// </summary>
    public static (int Width, int Height) DecodeSize(double frameWidth, double frameHeight, bool backdrop) => backdrop
        ? (0, Math.Clamp((int)Math.Ceiling(frameHeight * 1.25), 200, 960))
        : (Math.Clamp((int)Math.Ceiling(frameWidth * 1.25), 200, 640), 0);

    public static bool RequiresReload(MediaItem? previous, MediaItem next)
    {
        if (previous == null || previous.ContentId != next.ContentId) return true;
        var oldPoster = !string.IsNullOrWhiteSpace(previous.PosterUrl);
        var newPoster = !string.IsNullOrWhiteSpace(next.PosterUrl);
        return oldPoster != newPoster || !HasSameArtworkUrl(
            oldPoster ? previous.PosterUrl : previous.BackdropUrl,
            newPoster ? next.PosterUrl : next.BackdropUrl);
    }
}
