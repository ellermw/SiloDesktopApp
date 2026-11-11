using System.Text.Json;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services;

/// <summary>A library page of the viewer's own that a collection row can be added to.</summary>
public sealed record CollectionPageLibrary(int Id, string Name);

/// <summary>What the hero's More actions menu needs from the viewer's own personal collection.</summary>
public sealed record PersonalCollectionFacts(string Name, bool IsShared, bool IsSynced, bool ImportsAvailable);

/// <summary>What the hero's More actions menu offers; no entry at all means no menu button.</summary>
public sealed record CollectionHeroMenu(bool ShowSync, bool ShowDelete, IReadOnlyList<CollectionPageLibrary> LibraryPages)
{
    public static CollectionHeroMenu Empty { get; } = new(false, false, []);
    public bool HasEntries => ShowSync || ShowDelete || LibraryPages.Count > 0;
}

/// <summary>
/// The collection page hero's More actions menu and the words its confirmation uses, shared with the Collections
/// card menu so both surfaces say and do the same thing (pinned WebUI <c>CollectionPageActions</c> and
/// <c>web/src/lib/collections/copy.ts</c>): the owner of a personal collection gets Sync now (a synced list, when the
/// server can import) and Delete…; everyone else gets Add to my library page for the pages they may add a row to.
/// "Open in" belongs only to an administrator's server collection; Desktop has no administrator access to collections.
/// </summary>
public static class CollectionHeroActions
{
    /// <summary>Sync now applies to a collection kept in step with an outside list.</summary>
    public static bool CanSync(Collection collection) => IsSyncedType(collection.CollectionType);
    public static bool IsSyncedType(string? type) => type is "mdblist" or "tmdb" or "tmdb_list";

    public static string DeleteTitle(string name) => $"Delete \"{name}\"?";
    public static string DeleteDescription(bool shared)
        => shared ? "It's removed for you and every profile you share it with. This can't be undone." : "This can't be undone.";

    public static string AddToLibraryPageGroup => "Add to my library page";
    public static string AddToLibraryPage(string library) => $"Add to my {library} page";
    public static string LibraryPage(string library) => $"{library} page";

    /// <summary>The message to show when a finished sync reports failure; null when it did not fail.</summary>
    public static string? SyncFailure(UserCollectionSyncResult result)
        => result.Status == "failed" ? $"Collection sync failed: {result.Message}" : null;

    /// <summary>"Synced 12 titles" / "Synced 12 titles with warnings", for a sync that did not fail.</summary>
    public static string SyncSuccess(UserCollectionSyncResult result)
        => $"Synced {result.ItemsMatched} {(result.ItemsMatched == 1 ? "title" : "titles")}" + (result.Status == "warning" ? " with warnings" : "");

    public static CollectionHeroMenu Menu(CollectionPageAccess access) => access.Kind switch
    {
        CollectionPageAccessKind.Manage when access.Personal is { } own
            => new(own.IsSynced && own.ImportsAvailable, true, []),
        CollectionPageAccessKind.ReadOnly => new(false, false, access.LibraryPages),
        _ => CollectionHeroMenu.Empty,
    };

    /// <summary>
    /// The viewer's library pages a read-only collection can be added to. A personal collection offers the libraries it
    /// matches (every library when it matches none in particular); a server collection offers the library it was opened in
    /// first, then the others. <paramref name="libraryIds"/> is that matched / opened-in library set.
    /// </summary>
    public static IReadOnlyList<CollectionPageLibrary> LibraryPages(bool isUserCollection, IReadOnlyCollection<int> libraryIds, IEnumerable<Library> userLibraries)
    {
        var all = userLibraries.Select(library => new CollectionPageLibrary(library.Id, library.Name)).ToList();
        var matched = libraryIds.Count == 0 ? all : all.Where(library => libraryIds.Contains(library.Id)).ToList();
        if (isUserCollection) return matched;
        var bound = all.Where(library => matched.Any(m => m.Id == library.Id)).ToList();
        return [.. bound, .. all.Where(library => bound.All(b => b.Id != library.Id))];
    }

    /// <summary>
    /// The libraries a personal collection is restricted to: a synced list keeps them in its source configuration, a smart
    /// collection in its rules, a manual collection has none. Invalid, repeated and non-positive ids are dropped.
    /// </summary>
    public static IReadOnlyList<int> PersonalLibraryIds(Collection collection)
    {
        if (collection.CollectionType == "manual") return [];
        if (collection.SourceConfig != null && collection.SourceConfig.TryGetValue("library_ids", out var configured) && configured is JsonElement { ValueKind: JsonValueKind.Array } array)
            return Distinct(array.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out _)).Select(value => (int)Math.Truncate(value.GetDouble())));
        if (collection.SourceConfig != null && collection.SourceConfig.TryGetValue("library_ids", out var list) && list is IEnumerable<object> values)
            return Distinct(values.Select(value => value is int number ? number : value is long big ? (int)big : value is double real ? (int)Math.Truncate(real) : 0));
        return Distinct(collection.QueryDefinition?.LibraryIds ?? []);
    }

    private static IReadOnlyList<int> Distinct(IEnumerable<int> ids) => ids.Where(id => id > 0).Distinct().ToList();
}
