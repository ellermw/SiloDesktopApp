using SiloPlayer.Core.Localization;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>Official English for the Home sections page keys the generated interface catalog does not carry
/// (1a93371 web/src/locales/en.json rendered to ICU; rich-text tags keep their inner text). Keys the
/// catalog has are read from it so translations apply, except the few whose English changed after the catalog
/// was generated: <see cref="Newer"/> gives those their current English while the interface language is English.</summary>
public static class AdminHomeSectionsText
{
    internal static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["web.admin.common.sections.deleteFailed"] = "{count, plural, one {Failed to delete {count_text} section} other {Failed to delete {count_text} sections}}",
        ["web.admin.common.sections.deleted"] = "{count, plural, one {Deleted {count_text} section} other {Deleted {count_text} sections}}",
        ["web.admin.common.sections.partiallyDeleted"] = "{count, plural, one {Deleted {deleted} of {count_text} section} other {Deleted {deleted} of {count_text} sections}}",
        ["web.admin.homeRows.delete.failed"] = "Could not delete this row",
        ["web.admin.homeRows.delete.progress"] = "Deleting {done} of {total}…",
        ["web.admin.homeRows.more.restore"] = "Restore defaults…",
        ["web.admin.homeRows.more.restoreHelp"] = "Put back the rows Silo starts with on this page.",
        ["web.admin.homeRows.more.select"] = "Select rows",
        ["web.admin.homeRows.more.selectHelp"] = "Turn several rows on or off, or delete them together.",
        ["web.admin.homeRows.page.loadRowsFailed"] = "Could not load the current rows",
        ["web.admin.homeRows.page.loadingRows"] = "Loading the current rows…",
        ["web.admin.homeRows.page.subtitle"] = "The sections everyone sees on Home and on library pages. Profiles can still hide, rename or reorder them.",
        ["web.admin.homeRows.page.title"] = "Sections",
        ["web.admin.homeRows.restore.done"] = "Restored the default rows on {page}.",
        ["web.admin.homeRows.restore.doneWithLibraryPages"] = "{count, plural, one {Restored the default rows on {page} and # library page.} other {Restored the default rows on {page} and # library pages.}}",
        ["web.admin.homeRows.restore.doneWithOtherLibraryPages"] = "{count, plural, one {Restored the default rows on {page} and # other library page.} other {Restored the default rows on {page} and # other library pages.}}",
        ["web.admin.homeRows.restore.failed"] = "Could not restore the default rows",
        ["web.admin.homeRows.restore.libraryFallback"] = "Library {id}",
        ["web.admin.homeRows.restore.libraryPagesKept"] = "{count, plural, one {# library page kept its rows.} other {# library pages kept their rows.}}",
        ["web.admin.homeRows.restore.libraryPagesUnknown"] = "{count, plural, one {# library page may have been restored.} other {# library pages may have been restored.}}",
        ["web.admin.homeRows.restore.otherLibraryPagesKept"] = "{count, plural, one {# other library page kept its rows.} other {# other library pages kept their rows.}}",
        ["web.admin.homeRows.restore.otherLibraryPagesUnknown"] = "{count, plural, one {# other library page may have been restored.} other {# other library pages may have been restored.}}",
        ["web.admin.homeRows.restore.pageFailed"] = "{label}: {message}",
        ["web.admin.homeRows.restore.pageUnknown"] = "{label}: it may have been restored, but Silo couldn't confirm it. Reload it to check.",
        ["web.admin.homeRows.row.cantCopyToLibraries"] = "This row can't be added to other libraries.",
        ["web.admin.homeRows.row.moveFailed"] = "Could not move this row",
        ["web.admin.homeRows.row.saveFailed"] = "Could not save this row",
        ["web.admin.homeRows.rowMenu.addToOtherLibraries"] = "Add to other libraries…",
        ["web.admin.homeRows.rowMenu.delete"] = "Delete row…",
        ["web.admin.homeRows.rowMenu.edit"] = "Edit row…",
        ["web.admin.homeRows.rowMenu.stopUsingAsHero"] = "Stop using as hero banner",
        ["web.admin.homeRows.rowMenu.useAsHero"] = "Use as hero banner",
        ["web.admin.homeRows.select.alreadyOff"] = "The selected rows are already off.",
        ["web.admin.homeRows.select.alreadyOn"] = "The selected rows are already on.",
        ["web.admin.homeRows.select.changeFailed"] = "Could not change these rows",
        ["web.admin.homeRows.select.rowChanged"] = "{title} changed since you opened this page.",
        ["web.admin.homeRows.select.rowFailed"] = "{title}: {message}",
        ["web.admin.homeRows.select.rowLabel"] = "Select {title}",
        ["web.admin.homeRows.select.turnOffFailed"] = "{count, plural, one {Could not turn off # row.} other {Could not turn off # rows.}}",
        ["web.admin.homeRows.select.turnOnFailed"] = "{count, plural, one {Could not turn on # row.} other {Could not turn on # rows.}}",
        ["web.admin.homeRows.select.turnedOff"] = "{count, plural, one {Turned off # row.} other {Turned off # rows.}}",
        ["web.admin.homeRows.select.turnedOffPartial"] = "{count, plural, one {Turned off {done_text} of # row.} other {Turned off {done_text} of # rows.}}",
        ["web.admin.homeRows.select.turnedOn"] = "{count, plural, one {Turned on # row.} other {Turned on # rows.}}",
        ["web.admin.homeRows.select.turnedOnPartial"] = "{count, plural, one {Turned on {done_text} of # row.} other {Turned on {done_text} of # rows.}}",
        ["web.home.addRow.collection.library"] = "Library",
        ["web.home.addRow.collection.noneInLibrary"] = "No collections of this type in this library.",
        ["web.home.addRow.collections.add"] = "Add",
        ["web.home.addRow.collections.addLabel"] = "Add collections",
        ["web.home.addRow.collections.added"] = "Added",
        ["web.home.addRow.collections.chosenLabel"] = "Collections in this row",
        ["web.home.addRow.collections.count"] = "{count, plural, one {{count_text} collection, shown in this order} other {{count_text} collections, shown in this order}}",
        ["web.home.addRow.collections.drag.canceled"] = "Moving {collection} was canceled.",
        ["web.home.addRow.collections.drag.dropped"] = "{collection} dropped.",
        ["web.home.addRow.collections.drag.droppedAt"] = "{collection} dropped at position {position} of {total}.",
        ["web.home.addRow.collections.drag.instructions"] = "To move a collection, focus its handle and press Space or Enter. Use the up and down arrow keys to move it, Space or Enter to drop it, or Escape to cancel.",
        ["web.home.addRow.collections.drag.over"] = "{collection} is now at position {position} of {total}.",
        ["web.home.addRow.collections.drag.pickedUp"] = "Picked up {collection}, position {position} of {total}.",
        ["web.home.addRow.collections.dragHandle"] = "Reorder {collection}",
        ["web.home.addRow.collections.empty"] = "Add at least one collection below.",
        ["web.home.addRow.collections.limit"] = "{count, plural, one {A row holds up to {count_text} collection.} other {A row holds up to {count_text} collections.}}",
        ["web.home.addRow.collections.moveDown"] = "Move {collection} down",
        ["web.home.addRow.collections.moveUp"] = "Move {collection} up",
        ["web.home.addRow.collections.remove"] = "Remove {collection}",
        ["web.home.addRow.collections.unavailable"] = "No longer available",
        ["web.home.addRow.collections.yours"] = "Yours",
        ["web.home.rows.describe.collectionCount"] = "{count, plural, one {{count_text} collection} other {{count_text} collections}}",
        ["web.home.rows.describe.collectionList"] = "{count, plural, one {{count_text} collection: {names}} other {{count_text} collections: {names}}}",
        ["web.home.rows.describe.collectionListMore"] = "{count, plural, one {{count_text} more} other {{count_text} more}}",
        ["web.home.rows.describe.collectionsGone"] = "{count, plural, one {{count_text} collection no longer available} other {{count_text} collections no longer available}}",
        ["web.home.rows.draft.addCollection"] = "Add a collection to see the row.",
        ["web.home.rows.kindSentences.collectionPosters"] = "Show several collections as posters. Each one opens its collection.",
        ["web.home.rows.kinds.collectionPosters"] = "A row of collections",
    };

    /// <summary>
    /// Text of this page that has no official sentence: states and menu entries only the native page has. English only, never translated,
    /// and listed in the parity record. Keys start with <c>native.homeSections.</c>.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> NativeOnly = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["native.homeSections.adminRequired"] = "Administrator access is required.",
        ["native.homeSections.librariesFailed"] = "Library pages could not be loaded. Home is available; reload to try again.",
        ["native.homeSections.moveUp"] = "Move up",
        ["native.homeSections.moveDown"] = "Move down",
        ["native.homeSections.preview"] = "Preview",
        ["native.homeSections.removeName"] = "Remove {name}",
        ["native.homeSections.titleFallback"] = "Title ({id})",
        ["native.homeSections.savedRuleReadOnlyThisServer"] = "Saved rule (read only). This server or row uses options outside the available controls. Remove it to replace it.",
        ["native.homeSections.extendedRuleOptionsAreUnavailableExisting"] = "Extended rule options are unavailable. Existing rules are preserved.",
        ["native.homeSections.aSavedRuleGroupCannotBe"] = "A saved rule group cannot be edited here. It will be preserved.",
        ["native.homeSections.noRulesYetAddARule"] = "No rules yet. Add a rule to choose what this row shows.",
        ["native.homeSections.dateYYYYMMDDOrRelative"] = "Date (YYYY-MM-DD) or relative period (30d)",
        ["native.homeSections.fromToSeparatedByAComma"] = "From, to (separated by a comma)",
        ["native.homeSections.savedRuleReadOnly"] = "Saved rule (read only)",
        ["native.homeSections.latestEpisodeAired"] = "Latest episode aired",
        ["native.homeSections.latestEpisodeAdded"] = "Latest episode added",
        ["native.homeSections.howGroupsCombine"] = "How groups combine",
        ["native.homeSections.allOfTheseRules"] = "All of these rules",
        ["native.homeSections.anyOfTheseRules"] = "Any of these rules",
        ["native.homeSections.addAnotherGroup"] = "Add another group",
        ["native.homeSections.moviesAndShows"] = "Movies and shows",
        ["native.homeSections.allGroupsAnd"] = "All groups (and)",
        ["native.homeSections.savedCondition"] = "Saved condition",
        ["native.homeSections.anyGroupOr"] = "Any group (or)",
        ["native.homeSections.recentlyAdded"] = "Recently added",
        ["native.homeSections.contentRating"] = "Content rating",
        ["native.homeSections.searchValues"] = "Search values",
        ["native.homeSections.removeGroup"] = "Remove group",
        ["native.homeSections.releaseDate"] = "Release date",
        ["native.homeSections.removeRule"] = "Remove rule",
        ["native.homeSections.dateViewed"] = "Date viewed",
        ["native.homeSections.iMDbRating"] = "IMDb rating",
        ["native.homeSections.tMDBRating"] = "TMDB rating",
        ["native.homeSections.everything"] = "Everything",
        ["native.homeSections.audiobooks"] = "Audiobooks",
        ["native.homeSections.resolution"] = "Resolution",
        ["native.homeSections.descending"] = "Descending",
        ["native.homeSections.condition"] = "Condition",
        ["native.homeSections.direction"] = "Direction",
        ["native.homeSections.ascending"] = "Ascending",
        ["native.homeSections.episodes"] = "Episodes",
        ["native.homeSections.duration"] = "Duration",
        ["native.homeSections.progress"] = "Progress",
        ["native.homeSections.narrator"] = "Narrator",
        ["native.homeSections.bitrate"] = "Bitrate",
        ["native.homeSections.movies"] = "Movies",
        ["native.homeSections.ebooks"] = "Ebooks",
        ["native.homeSections.author"] = "Author",
        ["native.homeSections.series"] = "Series",
        ["native.homeSections.shows"] = "Shows",
        ["native.homeSections.manga"] = "Manga",
        ["native.homeSections.match"] = "Match",
        ["native.homeSections.field"] = "Field",
        ["native.homeSections.value"] = "Value",
        ["native.homeSections.title"] = "Title",
        ["native.homeSections.plays"] = "Plays",
        ["native.homeSections.show"] = "Show",
        ["native.homeSections.year"] = "Year",
        ["native.homeSections.showingUpTo50TitlesRefine"] = "Showing up to 50 titles. Refine your search or choose Next.",
        ["native.homeSections.enterATitleToSearch"] = "Enter a title to search.",
        ["native.homeSections.loadingChoices"] = "Loading choices…",
        ["native.homeSections.noTitlesFound"] = "No titles found.",
        ["native.homeSections.noMatches"] = "No matches.",
        ["native.homeSections.previous"] = "Previous",
        ["native.homeSections.search"] = "Search",
        ["native.homeSections.remove"] = "Remove",
        ["native.homeSections.next"] = "Next",
        ["native.homeSections.theDraftChangedAfterThisPreview"] = "The draft changed after this preview. Choose Live preview to refresh it.",
        ["native.homeSections.enterAWholeNumberOf1"] = "Enter a whole number of 1 or more.",
        ["native.homeSections.inSeason"] = "In season",
        ["native.homeSections.defaultsReview"] = "Next Up defaults need to be reloaded and reviewed before another change.",
        ["native.homeSections.defaultsChanged"] = "Defaults changed elsewhere. Reload and review.",
        ["native.homeSections.defaultsUnknown"] = "The save outcome is unknown. Reload defaults and review. The request was not replayed.",
        ["native.homeSections.pickerCapped"] = "Showing the first {count}. Search to narrow the list.",
        ["native.homeSections.collectionId"] = "Collection ID",
        ["native.homeSections.findCollection"] = "Find collection by ID",
        ["native.homeSections.collectionNotFound"] = "Collection not available. Check its ID and try again.",
        ["native.homeSections.choicesFailed"] = "Couldn't load choices. Retry Search.",
        ["native.homeSections.choicesPage"] = "{count} choices · page {page}",
        ["native.homeSections.collectionsTruncated"] = "Only the first {count} collections are listed, so a search can miss some. To add one that is not listed, use “{find}”.",
    };

    /// <summary>
    /// Official English that changed after the generated catalog was built (1a93371 renamed rule rows "Smart row"). Other languages keep the
    /// catalog's translation of the earlier sentence until the catalog is regenerated; English reads the current sentence.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Newer = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["web.home.rows.groups.collections"] = "Collections & smart rows",
        ["web.home.rows.groups.collectionsBlurb"] = "Your collections, or smart rows built from rules",
        ["web.home.rows.kindSentences.customFilter"] = "Like a smart collection, but only a row. Pick titles with rules like genre and decade.",
        ["web.home.rows.kinds.customFilter"] = "Smart row",
    };

    /// <summary>The longest name a confirmation shows; longer names end in an ellipsis.</summary>
    public const int NameLimit = 120;

    /// <summary>
    /// A name from the server as one plain line, for a title or a confirmation: control characters (line breaks and tabs among them), the line and paragraph
    /// separators U+2028 and U+2029 and the bidirectional controls (U+200E, U+200F, U+061C, U+202A to U+202E, U+2066 to U+2069) each become a space, the ends are trimmed
    /// and the length is bounded, so a name can neither fake a second line nor reorder the text around it.
    /// </summary>
    public static string OneLine(string? name, int limit = NameLimit)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var line = new System.Text.StringBuilder(Math.Min(name.Length, limit + 1));
        foreach (var character in name)
        {
            var blank = char.IsControl(character) || character is '\u2028' or '\u2029' or '\u200E' or '\u200F' or '\u061C' || character is >= '\u202A' and <= '\u202E' || character is >= '\u2066' and <= '\u2069';
            line.Append(blank ? ' ' : character);
        }
        var text = line.ToString().Trim();
        if (limit < 1 || text.Length <= limit) return text;
        // Cut between whole characters: a high surrogate left at the end would be half a character.
        var cut = char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit;
        return text[..cut].TrimEnd() + "…";
    }

    /// <summary>True when the key has text: from the pinned table or the interface catalog.</summary>
    public static bool Has(string key) => English.ContainsKey(key) || NativeOnly.ContainsKey(key) || MessageCatalog.English.Contains(key);

    /// <summary>The page text for <paramref name="key"/>; the interface catalog wins, the pinned English fills the gaps.</summary>
    public static string T(string key, object? values = null)
    {
        var map = Values(values);
        if (NativeOnly.TryGetValue(key, out var nativeText)) return AdminText.Format(nativeText, map);
        if (Newer.TryGetValue(key, out var newer) && AppStrings.Language == "en") return AdminText.Format(newer, map);
        var pinned = English.TryGetValue(key, out var text) ? AdminText.Format(text, map) : key;
        return AppStrings.Get(key, map, pinned);
    }

    /// <summary>What a stopped batch says about the rows it never reached. Empty when it reached them all.</summary>
    public static string NotAttempted(int count) => count <= 0 ? "" : count == 1 ? "1 more row was not attempted and is unchanged."
        : AdminText.FormatNumber(count) + " more rows were not attempted and are unchanged.";

    /// <summary>A count with its locale-formatted text, for messages that read both.</summary>
    public static Dictionary<string, object?> Count(long count, params (string Name, object? Value)[] more)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = count, ["count_text"] = AdminText.FormatNumber(count) };
        foreach (var (name, value) in more) result[name] = value;
        return result;
    }

    private static IReadOnlyDictionary<string, object?> Values(object? values)
    {
        if (values is null) return new Dictionary<string, object?>();
        if (values is IReadOnlyDictionary<string, object?> ready) return ready;
        if (values is IDictionary<string, object?> dictionary) return new Dictionary<string, object?>(dictionary);
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in values.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            result[property.Name] = property.GetValue(values);
        return result;
    }
}
