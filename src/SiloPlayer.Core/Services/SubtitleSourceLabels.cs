using System.Text.RegularExpressions;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Display labels for AI translation sources, composed like the WebUI's
/// subtitleTranslateRequest.ts sourceLabel/sourceLabels. Labels are display
/// text only; tracks are always identified by the track itself.
/// </summary>
public static class SubtitleSourceLabels
{
    private static readonly IReadOnlyDictionary<string, string> SourceKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["embedded"] = "web.subtitles.translate.source.embedded",
        ["external"] = "web.subtitles.translate.source.external",
        ["downloaded"] = "web.subtitles.translate.source.downloaded",
    };

    public static Dictionary<SubtitleTrackInfo, string> Build(IReadOnlyList<SubtitleTrackInfo> tracks, Func<string, string> languageName)
    {
        string Label(SubtitleTrackInfo track)
        {
            var language = languageName(track.Language);
            if (string.IsNullOrWhiteSpace(language))
                language = string.IsNullOrWhiteSpace(track.Language)
                    ? AppStrings.Get("web.languageName.unknown", fallback: "Unknown") : track.Language;
            var title = track.Label.Trim(); var parts = new List<string> { language };
            var format = title.ToLowerInvariant() is "srt" or "subrip" or "vtt" or "webvtt" or "ass" or "ssa" or "pgs" or "dvd";
            if (!format && title.Length > 0 && !title.Equals(language, StringComparison.OrdinalIgnoreCase)
                && !title.Equals(track.Language, StringComparison.OrdinalIgnoreCase)) parts.Add(title);
            if (track.Forced && !Regex.IsMatch(title, "forced", RegexOptions.IgnoreCase))
                parts.Add(AppStrings.Get("web.subtitles.translate.forced", fallback: "Forced"));
            if (track.HearingImpaired && !Regex.IsMatch(title, "sdh|cc|hearing", RegexOptions.IgnoreCase))
                parts.Add(AppStrings.Get("web.subtitles.translate.sdh", fallback: "SDH"));
            if (!string.IsNullOrWhiteSpace(track.Source))
                parts.Add(SourceKeys.TryGetValue(track.Source, out var key) ? AppStrings.Get(key, fallback: track.Source) : track.Source);
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
                var numbered = AppStrings.Get("web.subtitles.translate.trackNumbered",
                    new Dictionary<string, object?> { ["label"] = label, ["number"] = track.Index + 1 },
                    fallback: $"{label} · track {track.Index + 1}");
                text = numbered;
                for (var n = 2; used.Contains(text); n++)
                    text = AppStrings.Get("web.subtitles.translate.duplicate",
                        new Dictionary<string, object?> { ["label"] = numbered, ["count"] = n },
                        fallback: $"{numbered} ({n})");
            }
            used.Add(text); result[track] = text;
        }
        return result;
    }
}
