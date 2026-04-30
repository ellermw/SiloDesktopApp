using ContinuumPlayer.Core.Models.Home;

namespace ContinuumPlayer.Core.Services;

public static class MediaItemDisplayText
{
    public static string BuildSubtitle(MediaItem item, string? sortKey)
    {
        var parts = new List<string>();
        if (item.Year > 0)
            parts.Add(item.Year.ToString());

        switch (sortKey)
        {
            case "rating_imdb":
                if (item.RatingImdb.HasValue && item.RatingImdb.Value > 0)
                    parts.Add($"\u2605 {item.RatingImdb.Value:0.0}");
                else if (item.Type == "series")
                    parts.Add("SERIES");
                break;
            default:
                if (item.Type == "series")
                    parts.Add("SERIES");
                break;
        }

        return string.Join("  ", parts);
    }
}
