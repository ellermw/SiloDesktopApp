using SiloPlayer.Core.Helpers;
using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Tests;

public sealed class MangaChapterPresentationTests
{
    [Fact]
    public void LooseChapterPrecedesLaterVolumeAndVolumeAliasesShareOneSection()
    {
        MangaChapter[] chapters =
        [
            new() { ContentId = "two", ChapterIndex = 2, Volume = "v01" },
            new() { ContentId = "one", ChapterIndex = 1 },
            new() { ContentId = "four", ChapterIndex = 4, Volume = "1" },
            new() { ContentId = "three", ChapterIndex = 3, Volume = "01" },
        ];
        var entries = MangaChapterPresentation.Build(chapters);
        Assert.Equal(2, entries.Count);
        Assert.Null(entries[0].Volume);
        Assert.Equal("1", entries[1].Volume);
        Assert.Equal(new[] { "one", "two", "three", "four" }, MangaChapterPresentation.Flatten(chapters).Select(chapter => chapter.ContentId));
    }

    [Fact]
    public void UnindexedChaptersRetainInputOrderAfterIndexedEntries()
    {
        MangaChapter[] chapters =
        [
            new() { ContentId = "special-z", Title = "Z" },
            new() { ContentId = "indexed", ChapterIndex = 1 },
            new() { ContentId = "special-a", Title = "A" },
        ];
        Assert.Equal(new[] { "indexed", "special-z", "special-a" }, MangaChapterPresentation.Flatten(chapters).Select(chapter => chapter.ContentId));
    }
}
