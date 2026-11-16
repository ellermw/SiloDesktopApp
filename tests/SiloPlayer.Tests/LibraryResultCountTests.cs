using SiloPlayer.Core.Localization;
using SiloPlayer.Helpers;

namespace SiloPlayer.Tests;

/// <summary>
/// Library browse item count (official Silo Server #4316, 321223364): the count
/// beside the Filters button names what the library holds, uses only the exact
/// total a browse reported, waits while it is read and hides when unknown,
/// empty, failed or when the catalog toolbar is not the visible one.
/// </summary>
public sealed class LibraryResultCountTests
{
    private static readonly AppLocale English = new LocaleService().Create(AppLocaleResolution.Resolve("en", [], "en-US"));

    [Theory]
    // A series library counts series, or episodes when the type picker asks for them.
    [InlineData("series", null, null, LibraryCountNoun.Series)]
    [InlineData("series", "series", "series", LibraryCountNoun.Series)]
    [InlineData("series", "episode", "episode", LibraryCountNoun.Episodes)]
    [InlineData("tv", "episode", null, LibraryCountNoun.Episodes)]
    [InlineData(" Series ", "Episode", null, LibraryCountNoun.Episodes)]
    // Movie libraries report the kind as movie or movies.
    [InlineData("movie", null, null, LibraryCountNoun.Movies)]
    [InlineData("movies", null, null, LibraryCountNoun.Movies)]
    // Book libraries count books whatever the picker says.
    [InlineData("audiobook", null, null, LibraryCountNoun.Books)]
    [InlineData("audiobooks", "books", null, LibraryCountNoun.Books)]
    [InlineData("ebook", null, null, LibraryCountNoun.Books)]
    [InlineData("ebooks", null, null, LibraryCountNoun.Books)]
    // Any other library follows the selected media scope, else generic titles.
    [InlineData("mixed", null, null, LibraryCountNoun.Titles)]
    [InlineData("mixed", "", "", LibraryCountNoun.Titles)]
    [InlineData("mixed", "video", "video", LibraryCountNoun.Titles)]
    [InlineData("mixed", "movie", "movie", LibraryCountNoun.Movies)]
    [InlineData("mixed", "series", "series", LibraryCountNoun.Series)]
    [InlineData("mixed", "episode", "episode", LibraryCountNoun.Episodes)]
    [InlineData("mixed", "audiobook", "audiobook", LibraryCountNoun.Books)]
    [InlineData("mixed", "ebook", "ebook", LibraryCountNoun.Books)]
    [InlineData("manga", null, null, LibraryCountNoun.Titles)]
    [InlineData(null, null, null, LibraryCountNoun.Titles)]
    public void NounFollowsTheLibraryKindThenTheSelectedScope(string? library, string? browse, string? scope, LibraryCountNoun expected)
        => Assert.Equal(expected, LibraryResultCount.Noun(library, browse, scope));

    [Theory]
    [InlineData(LibraryCountNoun.Movies, 1, "1 movie")]
    [InlineData(LibraryCountNoun.Movies, 5321, "5,321 movies")]
    [InlineData(LibraryCountNoun.Series, 1, "1 series")]
    [InlineData(LibraryCountNoun.Series, 42, "42 series")]
    [InlineData(LibraryCountNoun.Episodes, 1, "1 episode")]
    [InlineData(LibraryCountNoun.Episodes, 12, "12 episodes")]
    [InlineData(LibraryCountNoun.Books, 1, "1 book")]
    [InlineData(LibraryCountNoun.Books, 640, "640 books")]
    [InlineData(LibraryCountNoun.Titles, 1, "1 title")]
    [InlineData(LibraryCountNoun.Titles, 1234567, "1,234,567 titles")]
    public void LabelPluralizesAndGroupsTheExactCount(LibraryCountNoun noun, int count, string expected)
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal(expected, LibraryResultCount.Label(noun, count));
    }

    [Fact]
    public void LabelFromLibraryStateCombinesNounAndCount()
    {
        using var _ = AppStrings.UseForCurrentFlow(English);
        Assert.Equal("12 episodes", LibraryResultCount.Label("series", "episode", "episode", 12));
        Assert.Equal("5,321 movies", LibraryResultCount.Label("movies", null, null, 5321));
        Assert.Equal("640 books", LibraryResultCount.Label("audiobooks", "books", null, 640));
    }

    [Theory]
    // Shown only once the exact total is above zero, whatever the estimate says.
    [InlineData(true, 1234, 1234, false, false, LibraryResultCountMode.Label)]
    [InlineData(true, 800, 1200, true, false, LibraryResultCountMode.Label)]
    // Waiting: the pager has an estimate or the first window is still loading.
    [InlineData(true, 0, 1200, false, false, LibraryResultCountMode.Loading)]
    [InlineData(true, 0, 0, true, false, LibraryResultCountMode.Loading)]
    // Empty result: nothing to count.
    [InlineData(true, 0, 0, false, false, LibraryResultCountMode.Hidden)]
    // A failed (or estimate-only) count read hides, even over an older number.
    [InlineData(true, 0, 1200, false, true, LibraryResultCountMode.Hidden)]
    [InlineData(true, 800, 800, false, true, LibraryResultCountMode.Hidden)]
    // Audiobook series/author/narrator groups use their own count, not this one.
    [InlineData(false, 1234, 1234, false, false, LibraryResultCountMode.Hidden)]
    [InlineData(false, 0, 1200, true, false, LibraryResultCountMode.Hidden)]
    public void ModeShowsWaitsOrHidesTheCount(bool toolbarShown, int exact, int estimated, bool loading, bool failed, LibraryResultCountMode expected)
        => Assert.Equal(expected, LibraryResultCount.Mode(toolbarShown, exact, estimated, loading, failed));

    [Fact]
    public void LibraryPageDrivesTheCountFromTheExactTotalWithoutForcingItCollapsed()
    {
        var xaml = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml");
        var code = ReadRepoFile("src", "SiloPlayer", "Views", "LibraryPage.xaml.cs");
        var update = Between(code, "private void UpdateCountDisplay()", "private static List<FilterOption> BuildFilterOptions");

        // The two forced-collapse sites are gone; visibility follows the mode.
        Assert.DoesNotContain("ResultCountText.Visibility = Visibility.Collapsed;\n        await", code);
        Assert.Equal(1, CountOf(code, "ResultCountText.Visibility = "));
        Assert.Contains("LibraryResultCount.Mode(", update);
        Assert.Contains("ViewModel.DisplayTotalCount", update);
        Assert.Contains("ViewModel.ExactCountFailed", update);
        Assert.Contains("LibraryResultCount.Label(", update);
        // The estimated extent is never the number shown, and the count issues no read.
        Assert.DoesNotContain("ViewModel.TotalCount,", Between(update, "LibraryResultCount.Label(", ";"));
        Assert.DoesNotContain("await", update);
        Assert.DoesNotContain("Api", update);
        // Placement and accessibility: a live region next to Filters and a labelled progress ring.
        Assert.Contains("x:Name=\"ResultCountProgress\"", xaml);
        Assert.Contains("web.catalog.filters.loadingCount", xaml);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml);
        // Audiobook group axes route through the toolbar helper, which hides the count.
        Assert.Contains("UpdateCatalogToolbarVisibility(showBooks: true);", code);
        Assert.Contains("_catalogToolbarShown = showBooks;", code);
        // The view model's changes reach the page, and the retained page keeps no slide animation.
        Assert.Contains("nameof(ViewModel.ExactCountFailed)", code);
        Assert.DoesNotContain("Transition", Between(xaml, "x:Name=\"ResultCountText\"", "<StackPanel Grid.Row=\"1\" Grid.Column=\"4\""));
    }

    [Fact]
    public void CountLabelsFallBackToUpstreamEnglishUntilTheCatalogsShipThem()
    {
        var helper = ReadRepoFile("src", "SiloPlayer", "Helpers", "LibraryResultCount.cs");
        foreach (var key in new[]
        {
            "web.library.browse.countMovies", "web.library.browse.countSeries",
            "web.library.browse.countEpisodes", "web.library.browse.countBooks", "web.catalog.page.titleCount",
        })
            Assert.Contains(key, helper);
        Assert.Contains("fallback:", helper);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "Missing source marker: " + start);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, "Missing source marker: " + end);
        return text[from..to];
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path).Replace("\r\n", "\n");
            directory = directory.Parent;
        }

        throw new FileNotFoundException(Path.Combine(parts));
    }
}
