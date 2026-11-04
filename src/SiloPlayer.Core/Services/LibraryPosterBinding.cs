using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public static class LibraryPosterBinding
{
    public static bool HasSameArtworkUrl(string? previous, string? next) => string.Equals(
        ImageService.NormalizeImageUrlForCache(previous ?? ""),
        ImageService.NormalizeImageUrlForCache(next ?? ""), StringComparison.Ordinal);

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
