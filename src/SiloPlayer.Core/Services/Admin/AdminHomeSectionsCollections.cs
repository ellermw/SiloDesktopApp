using SiloPlayer.Core.Api;

namespace SiloPlayer.Core.Services.Admin;

/// <summary>A library a collection is shown in, with the name the picker offers it under.</summary>
public sealed record AdminHomeCollectionLibrary(string Id, string Name);

/// <summary>
/// What the Add row collection pickers do with the collection list: the search, the Manual / Smart / Synced filter and the library filter, and how
/// a collection reads in the list (1a93371 web/src/components/homeRows/addRow/collectionSearch.ts, collectionChooser.tsx and
/// hooks/queries/homeRows/useRowCollectionOptions.ts).
/// </summary>
public static class AdminHomeSectionsCollections
{
    public const string AllKinds = "all";

    /// <summary>The libraries a collection is shown in: its own list, or its one library when the list is empty. Unnamed libraries keep their id.</summary>
    public static IReadOnlyList<string> LibraryIdsOf(AdminHomeSectionChoice option)
    {
        var ids = option.LibraryIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        return ids.Length > 0 ? ids : string.IsNullOrWhiteSpace(option.LibraryId) ? [] : [option.LibraryId];
    }

    /// <summary>The libraries that show any option, by name. A library whose name is not known cannot be offered, as in the official picker.</summary>
    public static IReadOnlyList<AdminHomeCollectionLibrary> LibrariesOf(IEnumerable<AdminHomeSectionChoice> options, IEnumerable<AdminHomeSectionChoice> libraries)
    {
        var names = libraries.GroupBy(library => library.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal);
        var found = new Dictionary<string, AdminHomeCollectionLibrary>(StringComparer.Ordinal);
        foreach (var option in options)
            foreach (var id in LibraryIdsOf(option))
                if (names.TryGetValue(id, out var name) && !string.IsNullOrEmpty(name) && !found.ContainsKey(id)) found[id] = new(id, name);
        return found.Values.OrderBy(library => library.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>
    /// The library filter in force: the pick while it still names a library that shows an option and two or more libraries remain, otherwise every
    /// library (null), so an earlier pick never keeps hiding collections.
    /// </summary>
    public static string? EffectiveLibrary(IReadOnlyList<AdminHomeCollectionLibrary> libraries, string? picked) =>
        libraries.Count > 1 && picked != null && libraries.Any(library => library.Id == picked) ? picked : null;

    /// <summary>Manual, Smart or Synced; every other stored type is a synced list.</summary>
    public static string KindOf(string collectionType) => collectionType is "manual" or "smart" ? collectionType : "synced";

    /// <summary>The options that pass the search text, the type filter ("all", "manual", "smart" or "synced") and the library filter (null for every library).</summary>
    public static IReadOnlyList<AdminHomeSectionChoice> Filter(IEnumerable<AdminHomeSectionChoice> options, string query, string kind, string? library)
    {
        var needle = query.Trim();
        return options.Where(option =>
            (kind == AllKinds || (!string.IsNullOrEmpty(option.CollectionType) && KindOf(option.CollectionType) == kind))
            && (library == null || LibraryIdsOf(option).Contains(library, StringComparer.Ordinal))
            && option.Label.Contains(needle, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    }

    /// <summary>"Movies · Smart · 12 titles": the library names tell apart collections of one name in several libraries.</summary>
    public static string Meta(AdminHomeSectionChoice option, IEnumerable<AdminHomeSectionChoice> libraries)
    {
        var names = libraries.GroupBy(library => library.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Label, StringComparer.Ordinal);
        var libraryText = string.Join(", ", LibraryIdsOf(option).Select(id => names.GetValueOrDefault(id, "")).Where(name => name.Length > 0));
        var kind = string.IsNullOrEmpty(option.CollectionType) ? null
            : AdminHomeSectionsText.T(KindOf(option.CollectionType) switch { "manual" => "web.collections.types.kindManual", "smart" => "web.collections.types.kindSmart", _ => "web.collections.types.kindSynced" });
        var count = option.ItemCount is { } items ? AdminHomeSectionsDescription.TitleCount(items) : null;
        return string.Join(" · ", new[] { libraryText, kind, count }.Where(part => !string.IsNullOrEmpty(part)));
    }

    /// <summary>What an empty list says: still loading, failed, no collections, no match for the search, none in the picked library, or none of the picked type.</summary>
    public static string EmptyText(bool loading, bool failed, int total, string query, bool libraryPicked)
    {
        if (loading) return AdminHomeSectionsText.T("web.home.addRow.collection.loading");
        if (failed) return AdminHomeSectionsText.T("web.home.addRow.collection.failed");
        if (total == 0) return AdminHomeSectionsText.T("web.home.addRow.collection.none");
        if (query.Trim().Length > 0) return AdminHomeSectionsText.T("web.home.addRow.collection.noMatch", new { query = query.Trim() });
        return AdminHomeSectionsText.T(libraryPicked ? "web.home.addRow.collection.noneInLibrary" : "web.home.addRow.collection.noneOfType");
    }
}
