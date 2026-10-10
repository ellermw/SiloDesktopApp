using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>Caption and duration contracts from the WebUI's NowListeningHero.</summary>
public static class NowListeningPresentation
{
    public readonly record struct Chapter(double Start, string Label);

    public static List<Chapter> BuildChapters(IEnumerable<FileVersion> files)
    {
        var result = new List<Chapter>();
        var offset = 0d;
        foreach (var file in files)
        {
            foreach (var chapter in file.Chapters ?? [])
                result.Add(new(offset + chapter.StartSeconds,
                    string.IsNullOrEmpty(chapter.Title) ? $"Chapter {chapter.Index + 1}" : chapter.Title));
            offset += file.Duration;
        }
        return result;
    }

    public static double ResolveDuration(double? detailDuration, double fileDuration, double? deckDuration)
        => detailDuration is > 0 ? detailDuration.Value : fileDuration > 0 ? fileDuration : deckDuration ?? 0;

    public static string ChapterLine(IReadOnlyList<Chapter> chapters, double position, double duration)
    {
        if (chapters.Count == 0) return duration > 0 ? FormatDuration(duration) : "";
        var index = 0;
        for (var i = chapters.Count - 1; i >= 0; i--)
        {
            if (position < chapters[i].Start) continue;
            index = i;
            break;
        }
        return FormatChapter(index + 1, chapters.Count, chapters[index].Label);
    }

    public static string FormatChapter(int index, int count, string? label)
        => $"Chapter {index} of {count}" +
           (!string.IsNullOrEmpty(label) && label != $"Chapter {index}" ? $" · {label}" : "");

    public static string? TimeLeft(double position, double duration)
        => duration > 0 ? $"{FormatDuration(Math.Max(duration - position, 0))} left" : null;

    public static string FormatDuration(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return "0 min";
        var totalMinutes = Math.Max(1, (long)Math.Floor(seconds / 60 + .5));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0) return $"{minutes} min";
        return minutes == 0 ? $"{hours} hr" : $"{hours} hr {minutes} min";
    }
}
