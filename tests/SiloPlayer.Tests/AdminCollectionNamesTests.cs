using System.Globalization;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>
/// A name written into a confirmation or dialog title is one plain, bounded line, and the confirmations use the official sentences.
/// These tests compute their answers from inputs; none reads source text.
/// </summary>
public sealed class AdminCollectionNamesTests
{
    [Theory]
    [InlineData("A\nB", "A B")]
    [InlineData("A\r\nB", "A B")]
    [InlineData("A\u2028B\u2029C", "A B C")]
    [InlineData("A\tB\u0085C", "A B C")]
    [InlineData("  spaced   out  ", "spaced out")]
    [InlineData("A\u00A0\u2003B", "A B")]
    public void ControlsLineSeparatorsAndSpacesBecomeOneSpace(string name, string expected) => Assert.Equal(expected, AdminCollectionNames.Plain(name));

    [Theory]
    [InlineData("a\u202Eb", "ab")]        // right-to-left override
    [InlineData("a\u202Ab\u202C", "ab")]  // embedding and pop
    [InlineData("a\u2066b\u2069", "ab")]  // isolates
    [InlineData("a\u200Fb\u200E", "ab")]  // marks
    [InlineData("a\u061Cb", "ab")]        // Arabic letter mark
    public void DirectionCharactersAreRemoved(string name, string expected) => Assert.Equal(expected, AdminCollectionNames.Plain(name));

    [Theory]
    [InlineData("\u3164")]   // Hangul filler
    [InlineData("\u115F\u1160")]
    [InlineData("\uFFA0")]
    [InlineData("\u2800")]   // braille blank
    [InlineData("\u200B\u2060\uFEFF\u00AD")]
    [InlineData("\u034F\u17B4\u17B5\u180E")]
    [InlineData("\U000E0041\U000E0042")] // tag characters
    [InlineData("   \n\t ")]
    [InlineData("")]
    public void InvisibleNamesShowNothing(string name) => Assert.Equal("", AdminCollectionNames.Plain(name));

    [Fact]
    public void FillersBetweenVisibleCharactersAreRemovedWithoutJoiningNothingElse() => Assert.Equal("AB", AdminCollectionNames.Plain("A\u3164\u200B\u2800B"));

    [Theory]
    [InlineData("\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645")]           // Persian: the non-joiner keeps the word's shape
    [InlineData("\U0001F469\u200D\U0001F4BB")]                                 // woman technologist: ZWJ sequence
    [InlineData("\u0915\u094D\u200D\u0937")]                                   // Indic conjunct with an explicit joiner
    public void JoinersThatJoinCharactersAreKept(string name) => Assert.Equal(name, AdminCollectionNames.Plain(name));

    [Theory]
    [InlineData("\u200Dabc\u200D", "abc")]       // nothing to join at either end
    [InlineData("a \u200D b", "a b")]            // next to a space
    [InlineData("a\u200D\u200Cb", "ab")]         // joiners next to each other join nothing
    [InlineData("a\u200D\u200B\u200Db", "ab")]   // the filler between them goes first, then the two joiners meet
    [InlineData("a\n\u200Cb", "a b")]            // next to a line break
    public void JoinersThatJoinNothingAreRemoved(string name, string expected) => Assert.Equal(expected, AdminCollectionNames.Plain(name));

    [Fact]
    public void LoneSurrogatesAreRemovedButRealReplacementCharactersStay()
    {
        Assert.Equal("ab", AdminCollectionNames.Plain("a\uD800b"));
        Assert.Equal("a\uFFFDb", AdminCollectionNames.Plain("a\uFFFDb"));
    }

    [Fact]
    public void LongNamesAreCutAtTheLimitWithAnEllipsisAndNeverInsideACharacter()
    {
        Assert.Equal(new string('a', 10) + "\u2026", AdminCollectionNames.Plain(new string('a', 11), 10));
        Assert.Equal(new string('a', 10), AdminCollectionNames.Plain(new string('a', 10), 10));
        // Surrogate pairs and ZWJ sequences count as one character each and are never split.
        var family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        var cut = AdminCollectionNames.Plain(string.Concat(Enumerable.Repeat(family, 5)), 2);
        Assert.Equal(family + family + "\u2026", cut);
        var emoji = AdminCollectionNames.Plain(new string('x', 3) + "\U0001F600\U0001F600", 4);
        Assert.Equal("xxx\U0001F600\u2026", emoji);
        // Text that was cut at a space does not keep it before the ellipsis.
        Assert.Equal("ab\u2026", AdminCollectionNames.Plain("ab cd", 3));
    }

    [Fact]
    public void AStackOfCombiningMarksCannotMakeTheTitleLong()
    {
        var zalgo = "a" + string.Concat(Enumerable.Repeat("\u0301", 5000));
        var plain = AdminCollectionNames.Plain(zalgo, 20);
        Assert.True(plain.Length <= 20 * 16 + 1, "a single character with thousands of marks is bounded: " + plain.Length);
    }

    [Fact]
    public void TitledUsesTheFallbackWhenTheNameShowsNothing()
    {
        Assert.Equal("Delete collection?", AdminCollectionCopy.DeleteTitle("\u200B \u3164"));
        Assert.Equal("Delete collection?", AdminCollectionCopy.DeleteTitle(null));
        Assert.Equal("Delete Movies Night?", AdminCollectionCopy.DeleteTitle("Movies\nNight"));
        Assert.Equal("Delete the Kids shelf?", AdminCollectionCopy.DeleteShelfTitle("Kids"));
        Assert.Equal("Hide Marvel from Collections tabs?", AdminCollectionCopy.HideTitle("Marvel\u202E"));
    }

    [Fact]
    public void NamesAreJoinedLikeTheOfficialList()
    {
        Assert.Equal("", AdminCollectionNames.JoinNames([]));
        Assert.Equal("Movies", AdminCollectionNames.JoinNames(["Movies"]));
        Assert.Equal("Movies and Kids", AdminCollectionNames.JoinNames(["Movies", "Kids"]));
        Assert.Equal("Movies, Kids and 4K", AdminCollectionNames.JoinNames(["Movies", "Kids", "4K"]));
        Assert.Equal("Movies and 4K", AdminCollectionNames.JoinNames(["Movies", "\u200B", "4K\n"]));
    }
}

public sealed class AdminCollectionCopyTests
{
    [Fact]
    public void DeleteDescriptionNamesEveryLibraryItLeaves()
    {
        Assert.Equal("It's removed for everyone. This can't be undone.", AdminCollectionCopy.DeleteDescription([]));
        Assert.Equal("It's removed from Movies for everyone. This can't be undone.", AdminCollectionCopy.DeleteDescription(["Movies"]));
        Assert.Equal("It's removed from Movies and Kids for everyone. This can't be undone.", AdminCollectionCopy.DeleteDescription(["Movies", "Kids"]));
        Assert.Equal("It's removed from Movies for everyone. 2 rows show it:", AdminCollectionCopy.DeleteWithRowsDescription(["Movies"], 2));
        Assert.Equal("It's removed for everyone. 1 row shows it:", AdminCollectionCopy.DeleteWithRowsDescription([], 1));
    }

    [Theory]
    [InlineData(0, false, "Delete")]
    [InlineData(1, false, "Delete it and its 1 row")]
    [InlineData(3, false, "Delete it and its 3 rows")]
    [InlineData(1, true, "Delete it and change 1 row")]
    [InlineData(2, true, "Delete it and change 2 rows")]
    public void TheDeleteButtonSaysWhatHappensToTheRows(int rows, bool stays, string expected) => Assert.Equal(expected, AdminCollectionCopy.DeleteButton(rows, stays));

    [Fact]
    public void ARowThatListsOtherCollectionsStays()
    {
        Assert.True(AdminCollectionCopy.RowStays(JsonNode.Parse("""{"id":"s","collection_count":2}""")!.AsObject()));
        Assert.False(AdminCollectionCopy.RowStays(JsonNode.Parse("""{"id":"s","collection_count":1}""")!.AsObject()));
        Assert.False(AdminCollectionCopy.RowStays(JsonNode.Parse("""{"id":"s"}""")!.AsObject()));
        Assert.False(AdminCollectionCopy.RowStays(JsonNode.Parse("""{"id":"s","collection_count":"2"}""")!.AsObject()));
        Assert.Equal("Removes it from the row \u201CHome picks\u201D. The row keeps its other collections.", AdminCollectionCopy.RowRemovesOnly("Home\npicks"));
    }

    [Fact]
    public void HideSentencesNameTheTabsAndRows()
    {
        Assert.Equal("1 row still shows it, but its See all won't open while it's hidden.", AdminCollectionCopy.HideDescription([], 1));
        Assert.Equal("It leaves Movies \u203A Collections and Kids \u203A Collections. 3 rows still show it, but their See all won't open while it's hidden.", AdminCollectionCopy.HideDescription(["Movies", "Kids"], 3));
        Assert.Equal("Hide 1 collection from Collections tabs?", AdminCollectionCopy.HideManyTitle(1));
        Assert.Equal("Hide 4 collections from Collections tabs?", AdminCollectionCopy.HideManyTitle(4));
        Assert.Equal("1 row still shows one of them, but its See all won't open while it's hidden.", AdminCollectionCopy.HideManyDescription(1));
        Assert.Equal("2 rows still show them, but those rows' See all won't open while they're hidden.", AdminCollectionCopy.HideManyDescription(2));
    }

    [Theory]
    [InlineData(1, "manual", null, "Delete 1 manual collection?")]
    [InlineData(7, "synced", "Movies", "Delete 7 synced lists in Movies?")]
    [InlineData(2, "smart", "this view", "Delete 2 smart collections in this view?")]
    [InlineData(3, null, null, "Delete 3 collections?")]
    public void BulkDeleteTitleCountsOnlyWhatIsDeleted(int count, string? kind, string? where, string expected) => Assert.Equal(expected, AdminCollectionCopy.BulkDeleteTitle(count, kind, where));

    [Fact]
    public void BulkDeleteNamesWhatAlsoLeavesOtherLibrariesAndWhatIsKept()
    {
        Assert.Equal("It's removed for everyone. Its titles stay in your libraries.", AdminCollectionCopy.BulkDeleteDescription(1));
        Assert.Equal("They're removed for everyone. Their titles stay in your libraries.", AdminCollectionCopy.BulkDeleteDescription(2));
        Assert.Equal("A synced list that's also in another library goes there too: Ghibli (Kids).",
            AdminCollectionCopy.AlsoDeletedElsewhere([("Ghibli", ["Kids"])], "synced"));
        Assert.Equal("Collections that are also in other libraries go there too: A (Kids) and B (Kids and 4K).",
            AdminCollectionCopy.AlsoDeletedElsewhere([("A", ["Kids"]), ("B", ["Kids", "4K"])], null));
        Assert.Equal("1 is kept because rows use it: Picks.", AdminCollectionCopy.KeptForRows(["Picks"]));
        Assert.Equal("2 are kept because rows use them: Picks and Mix.", AdminCollectionCopy.KeptForRows(["Picks", "Mix"]));
        Assert.Equal("A, B, C, D and 3 more", AdminCollectionCopy.SomeNames(["A", "B", "C", "D", "E", "F", "G"]));
        Assert.Equal("A, B, C, D and E", AdminCollectionCopy.SomeNames(["A", "B", "C", "D", "E"]));
        Assert.Equal("Delete 3", AdminCollectionCopy.DeleteCount(3));
    }

    [Fact]
    public void ShelfDeleteSaysWhereItsCollectionsGo()
    {
        Assert.StartsWith("It has no collections. Only Movies changes;", AdminCollectionCopy.DeleteShelfDescription(0, "Movies"));
        Assert.StartsWith("Its 1 collection moves to No heading on Movies \u203A Collections. It isn't deleted. Only Movies changes;", AdminCollectionCopy.DeleteShelfDescription(1, "Movies"));
        Assert.StartsWith("Its 4 collections move to No heading on Movies \u203A Collections. They aren't deleted.", AdminCollectionCopy.DeleteShelfDescription(4, "Movies\n"));
        Assert.EndsWith("You can make the shelf again later.", AdminCollectionCopy.DeleteShelfDescription(4, "Movies"));
    }

    [Theory]
    [InlineData("show", 3, 3, "Showed 3 collections on Collections tabs.")]
    [InlineData("show", 1, 1, "Showed 1 collection on Collections tabs.")]
    [InlineData("hide", 2, 2, "Hid 2 collections from Collections tabs.")]
    [InlineData("hide", 1, 3, "Hid 1 of 3 collections.")]
    [InlineData("sync", 2, 2, "Synced 2 lists.")]
    [InlineData("sync", 1, 1, "Synced 1 list.")]
    [InlineData("sync", 0, 2, "Couldn't sync 2 lists.")]
    [InlineData("show", 0, 1, "Couldn't show 1 collection.")]
    public void BatchResultsUseTheOfficialCountSentences(string action, int done, int total, string expected) => Assert.Equal(expected, AdminCollectionCopy.BatchResult(action, done, total));

    [Fact]
    public void BulkDeleteResultReportsDeletedKeptAndFailed()
    {
        Assert.Equal("Deleted 2 collections.", AdminCollectionCopy.BulkDeleteResult(2, 2, 0, []));
        Assert.Equal("Deleted 1 collection. Kept 1 collection in use by home or library sections.", AdminCollectionCopy.BulkDeleteResult(1, 1, 1, []));
        Assert.Equal("Deleted 1 of 3 collections. A: failed", AdminCollectionCopy.BulkDeleteResult(3, 1, 0, ["A: failed"]));
        Assert.Equal("Failed to delete 2 collections. A: x B: y", AdminCollectionCopy.BulkDeleteResult(2, 0, 0, ["A: x", "B: y"]));
        Assert.Equal("", AdminCollectionCopy.BulkDeleteResult(2, 0, 0, []));
    }

    [Fact]
    public void CountsUseTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Delete 1.200", AdminCollectionCopy.DeleteCount(1200));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
