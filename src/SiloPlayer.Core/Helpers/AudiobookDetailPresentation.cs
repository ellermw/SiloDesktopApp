using System.Globalization;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Home;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Helpers;

/// <summary>
/// Pure presentation rules of the current WebUI audiobook book page
/// (Silo-Server web/src/pages/ItemDetail/AudiobookContent.tsx,
/// pages/audiobooks/components/{ChaptersSection,RelatedRail}.tsx and
/// lib/audiobooks/{chapters,duration}.ts). Kept free of WinUI so the
/// rules are unit-tested; the detail page only lays the results out.
/// </summary>
public static class AudiobookDetailPresentation
{
    /// <summary>A book this short lists every chapter without a Show all toggle.</summary>
    public const int ShowAllUpTo = 10;
    /// <summary>Rows the collapsed list keeps before the current chapter.</summary>
    public const int RowsBeforeCurrent = 3;
    /// <summary>Rows the collapsed list shows in all.</summary>
    public const int CollapsedRows = 8;

    /// <summary>One chapter row across the whole multi-file book.</summary>
    public sealed record ChapterRow(
        int PositionIndex,
        int ChapterIndex,
        string Title,
        double AbsoluteStart,
        double DurationSeconds,
        int FileId)
    {
        /// <summary>The chapter title, else the translated "Chapter N" fallback.</summary>
        public string Label => string.IsNullOrWhiteSpace(Title)
            ? AppStrings.Get("web.audiobooks.chapter.fallbackTitle",
                new Dictionary<string, object?> { ["number"] = ChapterIndex + 1 },
                fallback: $"Chapter {ChapterIndex + 1}")
            : Title;
    }

    /// <summary>
    /// ChaptersSection.buildRows: flattens per-file chapter lists in server
    /// order with absolute starts; each file advances the offset by its length.
    /// </summary>
    public static IReadOnlyList<ChapterRow> BuildChapterRows(IEnumerable<FileVersion>? versions)
    {
        var rows = new List<ChapterRow>();
        var offset = 0d;
        var position = 1;
        foreach (var version in versions ?? [])
        {
            foreach (var chapter in version.Chapters ?? [])
            {
                var start = chapter.StartSeconds;
                var end = chapter.EndSeconds > 0 ? chapter.EndSeconds : chapter.StartSeconds;
                rows.Add(new ChapterRow(
                    position++,
                    chapter.Index,
                    chapter.Title ?? "",
                    offset + start,
                    Math.Max(0, end - start),
                    version.FileId));
            }
            offset += Math.Max(0, version.Duration);
        }
        return rows;
    }

    /// <summary>The 0-based row the position falls in; -1 without a position.</summary>
    public static int CurrentChapterIndex(IReadOnlyList<ChapterRow> rows, double? positionSeconds)
    {
        if (positionSeconds is not { } position) return -1;
        for (var index = rows.Count - 1; index >= 0; index--)
            if (position >= rows[index].AbsoluteStart) return index;
        return -1;
    }

    /// <summary>
    /// lib/audiobooks/chapters.findChapterAt: the chapter the resume label
    /// names, falling back to the first chapter before its start.
    /// </summary>
    public static ChapterRow? ResumeChapter(IReadOnlyList<ChapterRow> rows, double positionSeconds)
    {
        var index = CurrentChapterIndex(rows, positionSeconds);
        return index >= 0 ? rows[index] : rows.Count > 0 ? rows[0] : null;
    }

    public static bool IsCollapsible(int rowCount) => rowCount > ShowAllUpTo;

    /// <summary>
    /// The collapsed window around the current chapter: three rows before it,
    /// eight in all, clamped to the list; every row when short or expanded.
    /// </summary>
    public static (int Start, int Count) ChapterWindow(int rowCount, int currentIndex, bool expanded)
    {
        if (rowCount <= 0) return (0, 0);
        if (!IsCollapsible(rowCount) || expanded) return (0, rowCount);
        var start = Math.Max(0, Math.Min(currentIndex - RowsBeforeCurrent, rowCount - CollapsedRows));
        return (start, Math.Min(CollapsedRows, rowCount - start));
    }

    /// <summary>duration.formatHoursMinutes: "27 hr 14 min", "3 hr", "45 min".</summary>
    public static string FormatHoursMinutes(double totalSeconds)
    {
        if (!double.IsFinite(totalSeconds) || totalSeconds <= 0) return Minutes(0);
        var totalMinutes = (int)Math.Round(totalSeconds / 60, MidpointRounding.AwayFromZero);
        if (totalMinutes < 1) return Minutes(1);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours <= 0) return Minutes(minutes);
        if (minutes == 0)
            return AppStrings.Get("web.audiobooks.duration.hours",
                new Dictionary<string, object?> { ["hours"] = hours }, fallback: $"{hours} hr");
        return AppStrings.Get("web.audiobooks.duration.hoursMinutes",
            new Dictionary<string, object?> { ["hours"] = hours, ["minutes"] = minutes },
            fallback: $"{hours} hr {minutes} min");

        static string Minutes(int minutes) => AppStrings.Get("web.audiobooks.duration.minutes",
            new Dictionary<string, object?> { ["minutes"] = minutes }, fallback: $"{minutes} min");
    }

    /// <summary>duration.formatListeningTimeLeft: "27 hr 14 min left"; null without a duration.</summary>
    public static string? FormatTimeLeft(double positionSeconds, double durationSeconds)
    {
        if (!(durationSeconds > 0)) return null;
        var remaining = Math.Max(durationSeconds - Math.Max(0, positionSeconds), 0);
        var duration = FormatHoursMinutes(remaining);
        return AppStrings.Get("web.audiobooks.duration.left",
            new Dictionary<string, object?> { ["duration"] = duration }, fallback: $"{duration} left");
    }

    /// <summary>AudiobookContent.formatListened: "2h 05m", "4m 09s" or "12s".</summary>
    public static string FormatListened(double totalSeconds)
    {
        if (!double.IsFinite(totalSeconds) || totalSeconds < 0) return "";
        var hours = (int)Math.Floor(totalSeconds / 3600);
        var minutes = (int)Math.Floor(totalSeconds % 3600 / 60);
        var seconds = (int)Math.Floor(totalSeconds % 60);
        if (hours > 0)
            return AppStrings.Get("web.detail.duration.hoursMinutes",
                new Dictionary<string, object?> { ["hours"] = hours, ["minutes"] = minutes.ToString("00", CultureInfo.InvariantCulture) },
                fallback: $"{hours}h {minutes:00}m");
        if (minutes > 0)
            return AppStrings.Get("web.detail.mediaInfo.durationMs",
                new Dictionary<string, object?> { ["minutes"] = minutes, ["seconds"] = seconds.ToString("00", CultureInfo.InvariantCulture) },
                fallback: $"{minutes}m {seconds:00}s");
        return AppStrings.Get("web.detail.mediaInfo.durationS",
            new Dictionary<string, object?> { ["seconds"] = seconds }, fallback: $"{seconds}s");
    }

    /// <summary>The listened percentage the side column shows, 0-100.</summary>
    public static int ShownPercent(double positionSeconds, double durationSeconds)
        => durationSeconds > 0
            ? (int)Math.Min(100, Math.Round(Math.Max(0, positionSeconds) / durationSeconds * 100, MidpointRounding.AwayFromZero))
            : 0;

    /// <summary>"2h 05m listened · 42%".</summary>
    public static string ListenedProgress(double positionSeconds, double durationSeconds)
    {
        var time = FormatListened(positionSeconds);
        var percent = ShownPercent(positionSeconds, durationSeconds);
        return AppStrings.Get("web.detail.audiobook.listenedProgress",
            new Dictionary<string, object?> { ["time"] = time, ["percent"] = percent },
            fallback: $"{time} listened · {percent}%");
    }

    /// <summary>ChaptersSection.formatChapterStart: always h:mm:ss.</summary>
    public static string FormatChapterStart(double totalSeconds)
    {
        var total = Math.Max(0, totalSeconds);
        var hours = (int)Math.Floor(total / 3600);
        var minutes = (int)Math.Floor(total % 3600 / 60);
        var seconds = (int)Math.Floor(total % 60);
        return $"{hours}:{minutes:00}:{seconds:00}";
    }

    /// <summary>ChaptersSection.formatChapterDuration: "12m 04s"; empty when zero.</summary>
    public static string FormatChapterDuration(double totalSeconds)
    {
        if (!(totalSeconds > 0)) return "";
        var minutes = (int)Math.Floor(totalSeconds / 60);
        var seconds = (int)Math.Floor(totalSeconds % 60);
        return AppStrings.Get("web.audiobooks.chapters.duration",
            new Dictionary<string, object?> { ["minutes"] = minutes, ["seconds"] = seconds.ToString("00", CultureInfo.InvariantCulture) },
            fallback: $"{minutes}m {seconds:00}s");
    }

    /// <summary>"12m 04s left in this chapter" for the current row.</summary>
    public static string LeftInChapter(double seconds)
    {
        var time = FormatChapterDuration(seconds);
        return AppStrings.Get("web.audiobooks.chapters.leftInChapter",
            new Dictionary<string, object?> { ["time"] = time }, fallback: $"{time} left in this chapter");
    }

    public static string ShowAllLabel(int count)
        => AppStrings.Get("web.audiobooks.chapters.showAll",
            new Dictionary<string, object?> { ["count"] = count.ToString("N0", AppStrings.FormatCulture) },
            fallback: $"Show all {count.ToString("N0", AppStrings.FormatCulture)}");

    public static string ShowFewerLabel()
        => AppStrings.Get("web.audiobooks.chapters.showFewer", fallback: "Show fewer");

    /// <summary>
    /// AudiobookContent.relatedStatus: Finished, the time left while partway
    /// through, else its length, else its year.
    /// </summary>
    public static string? RelatedStatus(AudiobookRelatedItem entry)
    {
        if (entry.Played == true)
            return AppStrings.Get("web.audiobooks.detail.finished", fallback: "Finished");
        var duration = entry.DurationSeconds ?? 0;
        if (entry.ProgressRatio is { } ratio && duration > 0)
            return FormatTimeLeft(ratio * duration, duration);
        if (duration > 0) return FormatHoursMinutes(duration);
        return entry.Year is > 0 ? entry.Year.Value.ToString(AppStrings.FormatCulture) : null;
    }

    /// <summary>The cover progress bar fraction; none once finished.</summary>
    public static double? RelatedProgress(AudiobookRelatedItem entry)
        => entry.Played == true || entry.ProgressRatio is not > 0 ? null : Math.Clamp(entry.ProgressRatio.Value, 0, 1);

    /// <summary>"Series, book 2" above the title, else the series name.</summary>
    public static string SeriesLine(string seriesName, double? seriesIndex)
        => seriesIndex is { } index
            ? AppStrings.Get("web.audiobooks.detail.seriesBook",
                new Dictionary<string, object?> { ["series"] = seriesName, ["number"] = FormatSeriesIndex(index) },
                fallback: $"{seriesName}, book {FormatSeriesIndex(index)}")
            : seriesName;

    /// <summary>"Book 2" kicker on a series rail card.</summary>
    public static string? BookNumber(double? seriesIndex)
        => seriesIndex is { } index
            ? AppStrings.Get("web.detail.books.bookNumber",
                new Dictionary<string, object?> { ["number"] = FormatSeriesIndex(index) },
                fallback: $"Book {FormatSeriesIndex(index)}")
            : null;

    public static string FormatSeriesIndex(double index)
        => index.ToString("0.##", AppStrings.FormatCulture);

    /// <summary>
    /// RelatedRail card width: lib/uiCustomization carouselCardWidthPx, one
    /// poster-size step down for square covers (squarePosterSize).
    /// </summary>
    public static double RelatedCardWidth(string? posterSize, double viewportWidth, bool square)
    {
        var size = posterSize is "compact" or "large" ? posterSize : "standard";
        if (square) size = size == "large" ? "standard" : "compact";
        var (compact, small, large) = size switch
        {
            "compact" => (120d, 140d, 160d),
            "large" => (170d, 195d, 220d),
            _ => (140d, 160d, 185d),
        };
        return viewportWidth >= 1024 ? large : viewportWidth >= 640 ? small : compact;
    }
}
