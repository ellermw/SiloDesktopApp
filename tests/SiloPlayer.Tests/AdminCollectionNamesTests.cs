using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
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

    // A Fact, not a Theory: an attribute argument cannot carry a lone surrogate (it is stored as UTF-8 and comes back as U+FFFD).
    [Fact]
    public void ARealReplacementCharacterNeverSavesALoneSurrogate()
    {
        Assert.Equal("a\uFFFDbc", AdminCollectionNames.Plain("a\uFFFDb\uD800c"));   // a real U+FFFD does not keep a lone high surrogate
        Assert.Equal("ab\uFFFDc", AdminCollectionNames.Plain("a\uD800b\uFFFDc"));
        Assert.Equal("a\uFFFD", AdminCollectionNames.Plain("\uDC00a\uFFFD\uD800"));  // a lone low surrogate and a trailing high one
        Assert.Equal("\uFFFD\U00010000x", AdminCollectionNames.Plain("\uFFFD\uD800\uDC00x")); // a valid pair is one character, never two lone halves
    }

    [Theory]
    [InlineData("\u0301")]                 // one combining mark
    [InlineData("\u0301\u0302\u20DD")]    // marks and an enclosing mark
    [InlineData("\uFE0F")]                 // a variation selector alone
    [InlineData(" \u0301 \u200B")]
    public void ANameMadeOnlyOfCombiningMarksShowsNothing(string name) => Assert.Equal("", AdminCollectionNames.Plain(name));

    [Theory]
    [InlineData("e\u0301", "e\u0301")]           // a mark on a letter stays
    [InlineData("\u0301abc", "\u0301abc")]       // a leading mark with text after it is still a name
    [InlineData("\u2764\uFE0F", "\u2764\uFE0F")] // an emoji with its selector
    public void MarksThatBelongToACharacterStay(string name, string expected) => Assert.Equal(expected, AdminCollectionNames.Plain(name));

    private const string England = "\U0001F3F4\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F";

    [Fact]
    public void AValidEmojiTagSequenceStaysWholeAndCountsAsOneCharacter()
    {
        Assert.Equal(England, AdminCollectionNames.Plain(England));
        Assert.Equal("Home " + England + " team", AdminCollectionNames.Plain("Home " + England + " team"));
        // One character: three of them fit a limit of three, a fourth is cut after the third, never inside a sequence.
        Assert.Equal(England + England + England + "\u2026", AdminCollectionNames.Plain(England + England + England + England, 3));
        Assert.Equal("x" + England, AdminCollectionNames.Plain("x" + England, 2));
    }

    [Theory]
    [InlineData("\U0001F3F4\U000E0067\U000E0062", "\U0001F3F4")]                       // no cancel tag: not a sequence, the tags go
    [InlineData("\U0001F3F4\U000E007F", "\U0001F3F4")]                               // a cancel tag with nothing to cancel
    [InlineData("\U0001F3F4\U000E0041\U000E007F", "\U0001F3F4")]                    // a tag that is not a letter or digit of a subdivision
    [InlineData("a\U000E0067\U000E0062\U000E007Fb", "ab")]                          // tags after anything but the waving black flag
    [InlineData("\U0001F3F4\U000E0061\U000E0062\U000E0063\U000E0064\U000E0065\U000E0066\U000E0067\U000E007F", "\U0001F3F4")] // seven tags carry too much hidden text
    public void TagCharactersThatAreNotAValidSequenceAreRemoved(string name, string expected) => Assert.Equal(expected, AdminCollectionNames.Plain(name));

    [Fact]
    public void ATagSequenceCannotMakeTheTitleLong()
    {
        var plain = AdminCollectionNames.Plain(string.Concat(Enumerable.Repeat(England, 100)), 20);
        Assert.True(plain.Length <= 20 * 16 + 1, "sequences are bounded: " + plain.Length);
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
    public void AnEmptyNameIsAPlaceholderInTheOfficialSentenceNotASentenceTheCatalogLacks()
    {
        Assert.Equal("Hide Untitled collection from Collections tabs?", AdminCollectionCopy.HideTitle("\u200B"));
        Assert.Equal("Hide Untitled collection from Collections tabs?", AdminCollectionCopy.HideTitle(null));
        Assert.Equal("Delete the Untitled shelf?", AdminCollectionCopy.DeleteShelfTitle("\u3164"));
        Assert.Equal("Delete the Untitled shelf?", AdminCollectionCopy.DeleteShelfTitle(""));
        Assert.Equal("Removes it from the row \u201CUntitled row\u201D. The row keeps its other collections.", AdminCollectionCopy.RowRemovesOnly("\u200B"));
        Assert.Equal("It leaves Untitled library \u203A Collections and Kids \u203A Collections. 1 row still shows it, but its See all won't open while it's hidden.", AdminCollectionCopy.HideDescription(["\u200B", "Kids"], 1));
        Assert.Equal("It's removed from Untitled library and Kids for everyone. This can't be undone.", AdminCollectionCopy.DeleteDescription(["\u2800", "Kids"]));
        Assert.StartsWith("Its 2 collections move to No heading on Untitled library \u203A Collections.", AdminCollectionCopy.DeleteShelfDescription(2, "\u200B"));
        Assert.Equal("Delete 2 collections in Untitled library?", AdminCollectionCopy.BulkDeleteTitle(2, null, "\u200B"));
        Assert.Equal("Delete 2 collections?", AdminCollectionCopy.BulkDeleteTitle(2, null, null));
    }

    [Fact]
    public void NamesAreJoinedLikeTheOfficialList()
    {
        Assert.Equal("", AdminCollectionNames.JoinNames([]));
        Assert.Equal("Movies", AdminCollectionNames.JoinNames(["Movies"]));
        Assert.Equal("Movies and Kids", AdminCollectionNames.JoinNames(["Movies", "Kids"]));
        Assert.Equal("Movies, Kids and 4K", AdminCollectionNames.JoinNames(["Movies", "Kids", "4K"]));
        // A name that shows nothing is its placeholder, so a count written beside the names always agrees with them.
        Assert.Equal("Movies, Untitled collection and 4K", AdminCollectionNames.JoinNames(["Movies", "\u200B", "4K\n"]));
        Assert.Equal("Untitled library and Kids", AdminCollectionNames.JoinNames(["", "Kids"], AdminCollectionNames.UntitledLibrary));
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
    public void ALongTitleKeepsTheLibrariesItIsAlsoIn()
    {
        var title = "Long " + new string('x', 295);
        var sentence = AdminCollectionCopy.AlsoDeletedElsewhere([(title, ["Kids"])], "manual");
        Assert.EndsWith("\u2026 (Kids).", sentence);                  // the title is cut on its own, the library names are not
        Assert.StartsWith("A manual collection that's also in another library goes there too: Long xxx", sentence);
        Assert.True(sentence.Length < 200, "the title part is bounded: " + sentence.Length);
        // Several libraries after a long title stay whole too, and a long library name is bounded by itself.
        Assert.EndsWith("\u2026 (Kids and 4K).", AdminCollectionCopy.AlsoDeletedElsewhere([(title, ["Kids", "4K"])], "manual"));
        Assert.Contains("(" + new string('l', 80) + "\u2026)", AdminCollectionCopy.AlsoDeletedElsewhere([("A", [new string('l', 300)])], null));
        // Entries that are already plain are not cut again, not even past the limit of one name.
        Assert.Equal(new string('p', 90) + " (Kids)", AdminCollectionCopy.SomeNames([new string('p', 90) + " (Kids)"]));
    }

    [Fact]
    public void CountsAndNamesAgreeForNamesThatShowNothing()
    {
        Assert.Equal("2 are kept because rows use them: Untitled collection and B.", AdminCollectionCopy.KeptForRows(["\u200B", "B"]));
        Assert.Equal("1 is kept because rows use it: Untitled collection.", AdminCollectionCopy.KeptForRows(["\u3164"]));
        // Seven names, one of them blank: the first four, then "3 more": the count is the entries given, and each shows.
        var titles = new[] { "A", "\u200B", "C", "D", "E", "F", "G" };
        Assert.Equal("7 are kept because rows use them: A, Untitled collection, C, D and 3 more.", AdminCollectionCopy.KeptForRows(titles));
        Assert.Equal("A collection that's also in another library goes there too: Untitled collection (Kids).", AdminCollectionCopy.AlsoDeletedElsewhere([("\u200B", ["Kids"])], null));
        Assert.Equal("Untitled collection: nothing", AdminCollectionCopy.FailureLine("\u200B", "nothing"));
    }

    [Fact]
    public void AFailureLineNamesTheCollectionAsOnePlainBoundedLine()
    {
        var line = AdminCollectionCopy.FailureLine("Bad\nname\u202E" + new string('x', 400), "It changed since you opened this.");
        Assert.StartsWith("Bad name", line);
        Assert.EndsWith("\u2026: It changed since you opened this.", line);
        Assert.DoesNotContain('\n', line);
        Assert.True(line.Length < 140, line.Length.ToString());
    }

    [Fact]
    public void ShowAndHideActOnlyOnTheCollectionsThatDiffer()
    {
        AdminCollectionEntry E(string id, string visibility) => new() { Id = id, Title = id, Visibility = visibility };
        var selected = new[] { E("a", "visible"), E("b", "hidden"), E("c", "visible") };
        Assert.Equal(["a", "c"], AdminCollectionBulk.ChangingVisibility(selected, false).Select(c => c.Id));
        Assert.Equal(["b"], AdminCollectionBulk.ChangingVisibility(selected, true).Select(c => c.Id));
        Assert.Empty(AdminCollectionBulk.ChangingVisibility([E("b", "hidden")], false));
        Assert.Empty(AdminCollectionBulk.ChangingVisibility([E("a", "visible")], true));
        Assert.Equal(["a"], AdminCollectionBulk.ChangingVisibility([E("a", "")], false).Select(c => c.Id)); // a missing value is visible, as the official isVisible reads it
    }

    [Fact]
    public void ShowAndHideSentencesCountWhatChanges()
    {
        Assert.Equal("The selected collections are already hidden.", AdminCollectionCopy.AlreadyHidden);
        Assert.Equal("The selected collections are already on Collections tabs.", AdminCollectionCopy.AlreadyShown);
        Assert.Equal("Show 1 collection on Collections tabs?", AdminCollectionCopy.ShowManyTitle(1));
        Assert.Equal("Show 2 collections on Collections tabs?", AdminCollectionCopy.ShowManyTitle(2));
        Assert.Equal("Hide 2 collections from Collections tabs?", AdminCollectionCopy.HideManyTitle(2));
        Assert.Equal("3 selected.", AdminCollectionCopy.SelectedNote(3, 3, false));
        Assert.Equal("2 of 3 selected will change; 1 already hidden.", AdminCollectionCopy.SelectedNote(2, 3, false));
        Assert.Equal("1 of 3 selected will change; 2 already show.", AdminCollectionCopy.SelectedNote(1, 3, true));
        Assert.Equal("1 of 2 selected will change; 1 already shows.", AdminCollectionCopy.SelectedNote(1, 2, true));
        Assert.Equal("Hid 2 collections from Collections tabs.", AdminCollectionCopy.BatchResult("hide", 2, 2)); // the result counts what changed, not the selection
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
        Assert.Equal("Deleted 1 collection. Kept 1 collection in use by home or library sections.", AdminCollectionCopy.BulkDeleteResult(2, 1, 1, []));
        Assert.Equal("Deleted 1 of 3 collections. A: failed", AdminCollectionCopy.BulkDeleteResult(3, 1, 0, ["A: failed"]));
        Assert.Equal("Failed to delete 2 collections. A: x B: y", AdminCollectionCopy.BulkDeleteResult(2, 0, 0, ["A: x", "B: y"]));
        // Official runBulkDelete: kept collections are in the same sentence as what was deleted and what failed.
        Assert.Equal("Deleted 1 of 3 collections. Kept 1 collection in use by home or library sections. A: x", AdminCollectionCopy.BulkDeleteResult(3, 1, 1, ["A: x"]));
        Assert.Equal("Deleted 0 of 2 collections. Kept 1 collection in use by home or library sections. A: x", AdminCollectionCopy.BulkDeleteResult(2, 0, 1, ["A: x"]));
        Assert.Equal("Deleted 0 collections. Kept 2 collections in use by home or library sections.", AdminCollectionCopy.BulkDeleteResult(2, 0, 2, []));
    }

    [Theory]
    [InlineData(1, 0, 0, 0)]
    [InlineData(2, 0, 0, 0)]
    [InlineData(2, 2, 0, 0)]
    [InlineData(2, 0, 2, 0)]
    [InlineData(2, 1, 1, 0)]
    [InlineData(3, 1, 1, 1)]
    [InlineData(2, 0, 0, 2)]
    public void ABulkDeleteResultIsNeverEmptyWhileSomethingWasConfirmed(int total, int done, int kept, int failed)
    {
        var failures = Enumerable.Range(0, failed).Select(i => $"C{i}: no").ToArray();
        var result = AdminCollectionCopy.BulkDeleteResult(total, done, kept, failures);
        Assert.NotEmpty(result);
        // A "Kept ..." sentence never stands alone as if it were the whole result.
        Assert.False(result.StartsWith("Kept ", StringComparison.Ordinal), result);
    }

    [Fact]
    public void ABulkDeleteThatNeverStartedHasItsOwnSentenceNotAResult()
    {
        Assert.Equal("A collection change is already in progress. Nothing was changed.", AdminCollectionCopy.NotStarted);
        Assert.NotEmpty(AdminCollectionCopy.NotStarted);
    }

    [Theory]
    [InlineData(404, AdminBulkDeleteOutcome.Deleted)]   // already gone: official counts it as deleted
    [InlineData(409, AdminBulkDeleteOutcome.Kept)]      // a row still shows it
    [InlineData(412, AdminBulkDeleteOutcome.Failed)]
    [InlineData(422, AdminBulkDeleteOutcome.Failed)]
    [InlineData(500, AdminBulkDeleteOutcome.Failed)]
    public void ABulkDeleteClassifiesARefusalLikeTheOfficialClient(int status, AdminBulkDeleteOutcome expected) =>
        Assert.Equal(expected, AdminCollectionBulk.ClassifyDeleteFailure(new ApiException("x", "y", status)));

    [Fact]
    public void ATransportFailureAndAChangedValidatorAreFailuresNotKeptOrDeleted()
    {
        Assert.Equal(AdminBulkDeleteOutcome.Failed, AdminCollectionBulk.ClassifyDeleteFailure(new HttpRequestException("lost reply")));
        Assert.Equal(AdminBulkDeleteOutcome.Failed, AdminCollectionBulk.ClassifyDeleteFailure(new AdminCollectionChangedException()));
        Assert.Equal(AdminBulkDeleteOutcome.Failed, AdminCollectionBulk.ClassifyDeleteFailure(new TaskCanceledException()));
    }

    [Fact]
    public void CountsUseTheCurrentCulture()
    {
        using var culture = TestCulture.Use("de-DE");
        Assert.Equal("Delete 1.200", AdminCollectionCopy.DeleteCount(1200));
    }
}
