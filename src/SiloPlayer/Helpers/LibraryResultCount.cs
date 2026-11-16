using SiloPlayer.Core.Localization;

namespace SiloPlayer.Helpers;

/// <summary>What a library browse lists, for the count beside its Filters button.</summary>
public enum LibraryCountNoun
{
    Movies,
    Series,
    Episodes,
    Books,
    Titles,
}

/// <summary>How the count beside the Filters button is shown.</summary>
public enum LibraryResultCountMode
{
    /// <summary>Nothing: no exact count is known and none is on its way.</summary>
    Hidden,

    /// <summary>A small progress indicator while the exact count is still being read.</summary>
    Loading,

    /// <summary>The count and its noun, for example "1,234 movies".</summary>
    Label,
}

/// <summary>
/// The library browse item count (official Silo Server #4316): how many titles
/// the current filters match, named for what the library holds. The number is
/// only ever the exact total a browse response reported; the pager's estimated
/// extent is never shown as a count.
/// </summary>
public static class LibraryResultCount
{
    /// <summary>
    /// What the browse lists: the library's kind, narrowed by the type picker.
    /// A series library counts series or, when the picker asks for episodes,
    /// episodes. Movie libraries count movies and book libraries count books.
    /// Any other library (mixed, manga) follows the selected media scope and
    /// otherwise counts generic titles.
    /// </summary>
    public static LibraryCountNoun Noun(string? libraryType, string? browseType, string? mediaScope)
    {
        var library = Normalize(libraryType);
        if (library is "series" or "tv")
            return Normalize(browseType) == "episode" ? LibraryCountNoun.Episodes : LibraryCountNoun.Series;
        if (library is "movie" or "movies") return LibraryCountNoun.Movies;
        if (library is "audiobook" or "audiobooks" or "ebook" or "ebooks") return LibraryCountNoun.Books;
        return Normalize(mediaScope) switch
        {
            "movie" => LibraryCountNoun.Movies,
            "series" => LibraryCountNoun.Series,
            "episode" => LibraryCountNoun.Episodes,
            "audiobook" or "ebook" => LibraryCountNoun.Books,
            _ => LibraryCountNoun.Titles,
        };
    }

    /// <summary>
    /// The localized count text. The <c>web.library.browse.count*</c> messages
    /// arrive with the localization catalogs; until then (and in a language
    /// without them) the English fallback below matches the upstream English
    /// message, so the label reads the same before and after they ship.
    /// </summary>
    public static string Label(LibraryCountNoun noun, int count)
    {
        var shown = count.ToString("N0", AppStrings.FormatCulture);
        var (key, one, other) = noun switch
        {
            LibraryCountNoun.Movies => ("web.library.browse.countMovies", "movie", "movies"),
            LibraryCountNoun.Series => ("web.library.browse.countSeries", "series", "series"),
            LibraryCountNoun.Episodes => ("web.library.browse.countEpisodes", "episode", "episodes"),
            LibraryCountNoun.Books => ("web.library.browse.countBooks", "book", "books"),
            _ => ("web.catalog.page.titleCount", "title", "titles"),
        };
        return AppStrings.Get(
            key,
            new Dictionary<string, object?> { ["count"] = count, ["shown"] = shown },
            fallback: $"{shown} {(count == 1 ? one : other)}");
    }

    /// <summary>Convenience for <see cref="Label(LibraryCountNoun, int)"/> from the page's library state.</summary>
    public static string Label(string? libraryType, string? browseType, string? mediaScope, int count)
        => Label(Noun(libraryType, browseType, mediaScope), count);

    /// <summary>
    /// Whether the count shows, waits or hides. It shows only once the catalog
    /// has reported an exact total above zero. While that total is still being
    /// read (a first window or an exact-count read is loading) it
    /// waits, and it hides when the latest read failed (even over an older
    /// number), when the result is empty, and wherever the catalog toolbar
    /// itself is not shown (audiobook series/author/narrator groups).
    /// </summary>
    /// <param name="catalogToolbarShown">The sort/order/Filters toolbar is the visible one.</param>
    /// <param name="exactTotal">The exact total the browse reported; 0 when not known (or empty).</param>
    /// <param name="isExactCountLoading">An exact-count read is in flight for the current query.</param>
    /// <param name="isLoading">A catalog window is being read.</param>
    /// <param name="countFailed">The exact-count read failed for the current query.</param>
    public static LibraryResultCountMode Mode(
        bool catalogToolbarShown, int exactTotal, bool isExactCountLoading, bool isLoading, bool countFailed)
    {
        if (!catalogToolbarShown) return LibraryResultCountMode.Hidden;
        // A failed refresh drops the count rather than keep a stale number.
        if (countFailed) return LibraryResultCountMode.Hidden;
        if (exactTotal > 0) return LibraryResultCountMode.Label;
        return isExactCountLoading || isLoading ? LibraryResultCountMode.Loading : LibraryResultCountMode.Hidden;
    }

    private static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();
}
