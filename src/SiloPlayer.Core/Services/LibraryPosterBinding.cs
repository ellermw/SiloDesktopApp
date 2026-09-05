using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

public static class LibraryPosterBinding
{
    public static bool RequiresReload(MediaItem? previous, MediaItem next)
    {
        if (previous == null || previous.ContentId != next.ContentId) return true;
        var oldPoster = !string.IsNullOrWhiteSpace(previous.PosterUrl);
        var newPoster = !string.IsNullOrWhiteSpace(next.PosterUrl);
        return oldPoster != newPoster || !string.Equals(
            ImageService.NormalizeImageUrlForCache((oldPoster ? previous.PosterUrl : previous.BackdropUrl) ?? ""),
            ImageService.NormalizeImageUrlForCache((newPoster ? next.PosterUrl : next.BackdropUrl) ?? ""), StringComparison.Ordinal);
    }
}
