using System.Globalization;
using System.Text.Json;

namespace SiloPlayer.Core.Services;

public sealed record PlaybackHistoryDisplay(string Title, string Detail, double Seconds, bool Estimated);

public static class PlaybackHistoryPresentation
{
    public static PlaybackHistoryDisplay Format(JsonElement row)
    {
        string Text(string name) => row.TryGetProperty(name, out var value) ? value.ToString() : "";
        int? Number(string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
        var series = Text("series_title"); var media = Text("media_title");
        var title = series.Length > 0 ? series : media.Length > 0 ? media : Text("media_item_id");
        if (title.Length == 0) title = "File #" + Text("media_file_id");
        var detail = "";
        if (series.Length > 0)
        {
            var season = Number("season_number"); var episode = Number("episode_number");
            var code = season.HasValue && episode.HasValue ? $"S{season:00}E{episode:00}" : "";
            detail = string.Join(" · ", new[] { code, media != series ? media : "" }.Where(part => part.Length > 0));
        }
        if (row.TryGetProperty("played_seconds", out var measured) && measured.ValueKind == JsonValueKind.Number && measured.TryGetDouble(out var played) && double.IsFinite(played))
            return new(title, detail, Math.Max(0, played), false);
        var seconds = row.TryGetProperty("watched_seconds", out var position) && position.ValueKind == JsonValueKind.Number && position.TryGetDouble(out var watched) && double.IsFinite(watched) ? watched : 0;
        if (DateTimeOffset.TryParse(Text("started_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) &&
            DateTimeOffset.TryParse(Text("ended_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            seconds = Math.Min(seconds, Math.Max(0, (end - start).TotalSeconds));
        return new(title, detail, Math.Max(0, seconds), true);
    }
}
