namespace SiloPlayer.Core.Services.Admin;

/// <summary>
/// The sentences of the Collections page's delete and hide confirmations, in the words of the official WebUI catalog
/// (<c>web.collections.copy.*</c>, <c>web.collections.deleteDialog.*</c>, <c>web.collections.shelfDialogs.*</c> and
/// <c>web.admin.collections.*</c> in <c>en.json</c> at Silo Server 02a9d1d3e). Names written into a sentence are always one plain line.
/// </summary>
public static class AdminCollectionCopy
{
    public const string DeleteFooter = "Titles stay in your libraries. This can't be undone.";
    public const string KeepRowsHint = "To keep the rows, open each one and point it at another collection, then come back.";
    public const string CollectionsInUse = "Rows still use them. Remove the rows first.";
    public const string StarterPackBlocksDelete = "A starter pack is being added. Delete once it finishes.";
    public const string Irreversible = "This can't be undone.";
    public const string DeleteLabel = "Delete";
    public const string DeleteShelfLabel = "Delete shelf";
    public const string HideLabel = "Hide";
    public const string ThisView = "this view";

    private static string Count(int count) => count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
    private static string Plural(int count, string one, string other) => count == 1 ? one : other;

    public static string DeleteTitle(string? title) => AdminCollectionNames.Titled(title, name => $"Delete {name}?", "Delete collection?");

    /// <summary>The plain confirmation: it names every library it leaves.</summary>
    public static string DeleteDescription(IEnumerable<string?> libraryNames)
    {
        var libraries = AdminCollectionNames.JoinNames(libraryNames);
        return libraries.Length == 0 ? "It's removed for everyone. This can't be undone." : $"It's removed from {libraries} for everyone. This can't be undone.";
    }

    /// <summary>Deleting a collection that rows show: the rows go first.</summary>
    public static string DeleteWithRowsDescription(IEnumerable<string?> libraryNames, int rowCount)
    {
        var libraries = AdminCollectionNames.JoinNames(libraryNames);
        var removed = libraries.Length == 0 ? "It's removed for everyone." : $"It's removed from {libraries} for everyone.";
        return $"{removed} {Plural(rowCount, $"{Count(rowCount)} row shows it:", $"{Count(rowCount)} rows show it:")}";
    }

    /// <summary>The confirm button: plain Delete, or Delete it and its rows (or change them, when a row lists other collections and stays).</summary>
    public static string DeleteButton(int rowCount, bool someRowsStay)
    {
        if (rowCount <= 0) return DeleteLabel;
        return someRowsStay
            ? Plural(rowCount, $"Delete it and change {Count(rowCount)} row", $"Delete it and change {Count(rowCount)} rows")
            : Plural(rowCount, $"Delete it and its {Count(rowCount)} row", $"Delete it and its {Count(rowCount)} rows");
    }

    /// <summary>A row that lists other collections too stays and loses only this one (official <c>keepsRowWithoutCollection</c>).</summary>
    public static bool RowStays(System.Text.Json.Nodes.JsonObject row) =>
        row["collection_count"] is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<int>(out var count) && count > 1;

    public static string RowRemovesOnly(string? rowTitle) => $"Removes it from the row \u201C{AdminCollectionNames.Plain(rowTitle)}\u201D. The row keeps its other collections.";

    public static string HideTitle(string? name) => AdminCollectionNames.Titled(name, plain => $"Hide {plain} from Collections tabs?", "Hide collection from Collections tabs?");

    public static string HideDescription(IEnumerable<string?> libraryNames, int rowCount)
    {
        var rows = Plural(rowCount, $"{Count(rowCount)} row still shows it, but its See all won't open while it's hidden.",
            $"{Count(rowCount)} rows still show it, but their See all won't open while it's hidden.");
        var tabs = AdminCollectionNames.JoinNames(libraryNames.Select(l => AdminCollectionNames.Plain(l) is { Length: > 0 } plain ? plain + " \u203A Collections" : null));
        return tabs.Length == 0 ? rows : $"It leaves {tabs}. {rows}";
    }

    public static string HideManyTitle(int count) => Plural(count, $"Hide {Count(count)} collection from Collections tabs?", $"Hide {Count(count)} collections from Collections tabs?");

    public static string HideManyDescription(int rowCount) => Plural(rowCount,
        $"{Count(rowCount)} row still shows one of them, but its See all won't open while it's hidden.",
        $"{Count(rowCount)} rows still show them, but those rows' See all won't open while they're hidden.");

    private static string KindWords(string? kind, bool plural) => kind switch
    {
        "manual" => plural ? "manual collections" : "manual collection",
        "smart" => plural ? "smart collections" : "smart collection",
        "synced" => plural ? "synced lists" : "synced list",
        _ => plural ? "collections" : "collection",
    };

    /// <summary>"Delete 7 synced lists in Movies?": <paramref name="kind"/> is the one type they share (manual, smart, synced) or null for a mix; <paramref name="where"/> a library name or "this view".</summary>
    public static string BulkDeleteTitle(int count, string? kind, string? where)
    {
        var words = $"Delete {Count(count)} {KindWords(kind, count != 1)}";
        var place = AdminCollectionNames.Plain(where);
        return place.Length == 0 ? words + "?" : $"{words} in {place}?";
    }

    public static string BulkDeleteDescription(int count) => count == 1
        ? "It's removed for everyone. Its titles stay in your libraries."
        : "They're removed for everyone. Their titles stay in your libraries.";

    /// <summary>Up to five names, then how many more: the first four and "N more".</summary>
    public static string SomeNames(IReadOnlyList<string> names)
    {
        if (names.Count <= 5) return AdminCollectionNames.JoinNames(names);
        return AdminCollectionNames.JoinNames(names.Take(4).Append($"{Count(names.Count - 4)} more"));
    }

    /// <summary>Collections being deleted that are also in other libraries, with those libraries: "Studio Ghibli (Kids)".</summary>
    public static string AlsoDeletedElsewhere(IReadOnlyList<(string Title, IReadOnlyList<string> Libraries)> entries, string? kind)
    {
        var names = SomeNames(entries.Select(e => $"{AdminCollectionNames.Plain(e.Title)} ({AdminCollectionNames.JoinNames(e.Libraries)})").ToArray());
        var count = entries.Count;
        var article = kind switch { "manual" => "manual collection", "smart" => "smart collection", "synced" => "synced list", _ => "collection" };
        var plural = KindWords(kind, true);
        return count == 1
            ? $"A {article} that's also in another library goes there too: {names}."
            : $"{char.ToUpperInvariant(plural[0])}{plural[1..]} that are also in other libraries go there too: {names}.";
    }

    /// <summary>The collections a delete leaves alone because rows use them.</summary>
    public static string KeptForRows(IReadOnlyList<string> titles)
    {
        var names = SomeNames(titles.Select(t => AdminCollectionNames.Plain(t)).ToArray());
        return titles.Count == 1 ? $"{Count(titles.Count)} is kept because rows use it: {names}." : $"{Count(titles.Count)} are kept because rows use them: {names}.";
    }

    public static string DeleteCount(int count) => $"Delete {Count(count)}";

    public static string DeleteShelfTitle(string? name) => AdminCollectionNames.Titled(name, plain => $"Delete the {plain} shelf?", "Delete shelf?");

    /// <summary>Deleting a shelf never deletes its collections and touches one library only.</summary>
    public static string DeleteShelfDescription(int collectionCount, string? libraryName)
    {
        var library = AdminCollectionNames.Plain(libraryName);
        var members = collectionCount == 0 ? "It has no collections."
            : collectionCount == 1 ? $"Its 1 collection moves to No heading on {library} \u203A Collections. It isn't deleted."
            : $"Its {Count(collectionCount)} collections move to No heading on {library} \u203A Collections. They aren't deleted.";
        return $"{members} Only {library} changes; shelves in other libraries stay as they are. You can make the shelf again later.";
    }

    /// <summary>The select-mode result of Show on tabs, Hide from tabs or Sync lists: all done, some done, or none (official <c>batchAll</c>, <c>batchSome</c>, <c>batchNone</c>).</summary>
    public static string BatchResult(string action, int done, int total)
    {
        var noun = action == "sync" ? ("list", "lists") : ("collection", "collections");
        string Counted(int n) => $"{Count(n)} {(n == 1 ? noun.Item1 : noun.Item2)}";
        if (total == 0) return "";
        if (done == total) return action switch { "sync" => $"Synced {Counted(done)}.", "show" => $"Showed {Counted(done)} on Collections tabs.", _ => $"Hid {Counted(done)} from Collections tabs." };
        if (done > 0) return action switch { "sync" => $"Synced {Count(done)} of {Counted(total)}.", "show" => $"Showed {Count(done)} of {Counted(total)}.", _ => $"Hid {Count(done)} of {Counted(total)}." };
        return action switch { "sync" => $"Couldn't sync {Counted(total)}.", "show" => $"Couldn't show {Counted(total)}.", _ => $"Couldn't hide {Counted(total)}." };
    }

    /// <summary>The select-mode delete result, in the official count sentences.</summary>
    public static string BulkDeleteResult(int total, int done, int kept, IReadOnlyList<string> failures)
    {
        var parts = new List<string>();
        if (done == total && failures.Count == 0) parts.Add(Plural(done, $"Deleted {Count(done)} collection", $"Deleted {Count(done)} collections"));
        else if (done > 0) parts.Add(Plural(total, $"Deleted {Count(done)} of {Count(total)} collection", $"Deleted {Count(done)} of {Count(total)} collections"));
        else if (failures.Count > 0) parts.Add(Plural(failures.Count, $"Failed to delete {Count(failures.Count)} collection", $"Failed to delete {Count(failures.Count)} collections"));
        if (kept > 0) parts.Add(Plural(kept, $"Kept {Count(kept)} collection in use by home or library sections", $"Kept {Count(kept)} collections in use by home or library sections"));
        return string.Join(". ", parts) + (parts.Count > 0 ? "." : "") + (failures.Count > 0 ? " " + string.Join(" ", failures.Take(5)) : "");
    }
}
