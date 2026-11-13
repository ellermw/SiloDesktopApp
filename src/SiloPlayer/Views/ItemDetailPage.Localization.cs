using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Views;

public sealed partial class ItemDetailPage
{
    private static string SeriesActionLabel(MediaItemDetail item) => AppStrings.Get(
        string.IsNullOrWhiteSpace(item.PlayContentId) ? "web.detail.actions.browseSeries"
            : item.UserData?.InProgressCount > 0 ? "web.detail.actions.resume"
            : item.UserData?.WatchedCount > 0 && !item.UserData.Played ? "web.detail.actions.playNext"
            : "web.detail.actions.startFromFirstEpisode", new Dictionary<string, object?> { ["episode"] = 1 });

    private static string LegacySeriesActionLabel(IReadOnlyCollection<Season> seasons)
    {
        if (seasons.Count == 0) return AppStrings.Get("web.detail.actions.browseSeries");
        if (seasons.Any(season => season.UserData is { } progress &&
            (progress.InProgressCount > 0 || progress.WatchedCount > 0 || progress.UnplayedCount < season.EpisodeCount))) return "Play Latest";
        return AppStrings.Get("web.detail.actions.startFromFirstEpisode", new Dictionary<string, object?> { ["episode"] = 1 });
    }
}
