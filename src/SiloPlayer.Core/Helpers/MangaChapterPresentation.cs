using System.Text.RegularExpressions;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Helpers;

public static class MangaChapterPresentation
{
    public sealed record Group(string? Volume, IReadOnlyList<MangaChapter> Chapters);

    private static readonly Regex VolumeToken = new("^v?([0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<Group> Build(IEnumerable<MangaChapter> chapters)
    {
        var entries = new List<Group>();
        var volumes = new Dictionary<string, List<MangaChapter>>(StringComparer.Ordinal);
        foreach (var chapter in chapters)
        {
            var token = chapter.Volume?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                entries.Add(new(null, new[] { chapter }));
                continue;
            }
            var key = CanonicalVolume(token);
            if (!volumes.TryGetValue(key, out var bucket)) volumes[key] = bucket = [];
            bucket.Add(chapter);
        }
        foreach (var (volume, bucket) in volumes)
            entries.Add(new(volume, bucket.OrderBy(Index).ToArray()));
        return entries.OrderBy(entry => entry.Chapters.Min(Index)).ToArray();
    }

    public static List<MangaChapter> Flatten(IEnumerable<MangaChapter> chapters)
        => Build(chapters).SelectMany(entry => entry.Chapters).ToList();

    public static string VolumeLabel(string volume)
        => VolumeToken.IsMatch(volume)
            ? AppStrings.Get("web.common.format.manga.volume", new Dictionary<string, object?> { ["number"] = CanonicalVolume(volume) })
            : volume;

    public static string ChapterLabel(MangaChapter chapter)
        => chapter.ChapterIndex is { } index
            ? AppStrings.Get("web.common.format.manga.chapter", new Dictionary<string, object?> { ["number"] = index.ToString("0.##", AppStrings.FormatCulture) })
            : chapter.Title?.Trim() is { Length: > 0 } title ? title : AppStrings.Get("web.common.format.manga.chapterFallback");

    private static double Index(MangaChapter chapter)
        => chapter.ChapterIndex is { } index && double.IsFinite(index) ? index : double.PositiveInfinity;

    private static string CanonicalVolume(string token)
    {
        var match = VolumeToken.Match(token);
        if (!match.Success) return token;
        var number = match.Groups[1].Value.TrimStart('0');
        return number.Length == 0 ? "0" : number;
    }
}
