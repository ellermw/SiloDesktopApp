using System.Text.RegularExpressions;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static class SubtitleSourceLabels
{
    public static Dictionary<SubtitleTrackInfo, string> Build(IReadOnlyList<SubtitleTrackInfo> tracks, Func<string, string> languageName)
    {
        string Label(SubtitleTrackInfo track)
        {
            var language = languageName(track.Language);
            if (string.IsNullOrWhiteSpace(language)) language = string.IsNullOrWhiteSpace(track.Language) ? "Unknown" : track.Language;
            var title = track.Label.Trim(); var parts = new List<string> { language };
            var format = title.ToLowerInvariant() is "srt" or "subrip" or "vtt" or "webvtt" or "ass" or "ssa" or "pgs" or "dvd";
            if (!format && title.Length > 0 && !title.Equals(language, StringComparison.OrdinalIgnoreCase)
                && !title.Equals(track.Language, StringComparison.OrdinalIgnoreCase)) parts.Add(title);
            if (track.Forced && !Regex.IsMatch(title, "forced", RegexOptions.IgnoreCase)) parts.Add("Forced");
            if (track.HearingImpaired && !Regex.IsMatch(title, "sdh|cc|hearing", RegexOptions.IgnoreCase)) parts.Add("SDH");
            if (!string.IsNullOrWhiteSpace(track.Source)) parts.Add(track.Source);
            return string.Join(" · ", parts);
        }
        var labels = tracks.Select(t => (Track: t, Label: Label(t))).ToList();
        var counts = labels.GroupBy(t => t.Label).ToDictionary(g => g.Key, g => g.Count());
        var used = labels.Where(t => counts[t.Label] == 1).Select(t => t.Label).ToHashSet();
        var result = new Dictionary<SubtitleTrackInfo, string>();
        foreach (var (track, label) in labels)
        {
            var text = label;
            if (counts[label] > 1)
            {
                var numbered = $"{label} · track {track.Index + 1}"; text = numbered;
                for (var n = 2; used.Contains(text); n++) text = $"{numbered} ({n})";
            }
            used.Add(text); result[track] = text;
        }
        return result;
    }
}
