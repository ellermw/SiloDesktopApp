using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services;

/// <summary>What a collection page offers the viewer: nothing yet or when it can't be known, changing it, or only adding it to Home.</summary>
public enum CollectionPageAccessKind { None, Manage, ReadOnly }

/// <summary>
/// <see cref="OwnerName"/> is another profile's name on their shared personal collection (the page's
/// "by Name · Read-only" byline); null for a server collection.
/// </summary>
public sealed record CollectionPageAccess(CollectionPageAccessKind Kind, string? OwnerName = null)
{
    public static CollectionPageAccess None { get; } = new(CollectionPageAccessKind.None);
    /// <summary>The viewer's own personal collection, as the More actions menu needs it (Manage only).</summary>
    public PersonalCollectionFacts? Personal { get; init; }
    /// <summary>The library pages of the viewer's own that a read-only collection can be added to (ReadOnly only).</summary>
    public IReadOnlyList<CollectionPageLibrary> LibraryPages { get; init; } = [];
}

/// <summary>
/// The description, backdrop and access a collection page shows besides its name. An unreadable source
/// is the same as having none: the page keeps its plain header.
/// </summary>
public sealed record CollectionPageHeader(string? Description, string? BackdropUrl, string? BackdropThumbhash, CollectionPageAccess Access)
{
    public static CollectionPageHeader Empty { get; } = new(null, null, null, CollectionPageAccess.None);
    public bool HasBackdrop => !string.IsNullOrWhiteSpace(BackdropUrl) || !string.IsNullOrWhiteSpace(BackdropThumbhash);
}

/// <summary>
/// Reads a collection page's header from the sources the pinned WebUI uses (<c>useCollectionPageHeader</c>,
/// <c>useCollectionPageAccess</c>): a server collection from its library's Collections tab, a personal one
/// from its own snapshot, which also says who owns it.
/// </summary>
public static class CollectionPageHeaderLoader
{
    /// <summary>
    /// The libraries whose Collections tab may list server collection <paramref name="id"/>: the one the page
    /// was opened in; else the first the server-collections list shows it under; else, when it is past a
    /// library's teaser cap there, every library whose teaser was cut short. Null while the list is unknown.
    /// </summary>
    public static IReadOnlyList<int>? ServerLibraryIds(string id, int? libraryId, IReadOnlyList<ServerCollectionsLibrary>? libraries)
    {
        if (libraryId is > 0) return [libraryId.Value];
        if (libraries == null) return null;
        var listing = libraries.FirstOrDefault(library => library.Collections.Any(collection => collection.Id == id));
        if (listing != null) return [listing.LibraryId];
        return libraries.Where(library => library.TotalCount > library.Collections.Count).Select(library => library.LibraryId).ToArray();
    }

    public static async Task<CollectionPageHeader> LoadAsync(CollectionsApi collections, CatalogApi catalog, AuthApi? auth,
        string id, bool isUserCollection, int? libraryId, string? profileId, CancellationToken ct)
        => isUserCollection
            ? await LoadPersonalAsync(collections, catalog, auth, id, profileId, ct)
            : await LoadServerAsync(collections, catalog, id, libraryId, ct);

    /// <summary>The viewer's visible libraries; unreadable means none, so no library page is offered.</summary>
    private static async Task<IReadOnlyList<Library>> LoadLibrariesAsync(CatalogApi catalog, CancellationToken ct)
    {
        try { return await catalog.GetLibrariesAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return []; }
    }

    private static async Task<CollectionPageHeader> LoadPersonalAsync(CollectionsApi collections, CatalogApi catalog, AuthApi? auth, string id, string? profileId, CancellationToken ct)
    {
        Collection collection;
        try { collection = await collections.GetCollectionAsync(id, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return CollectionPageHeader.Empty; }
        var description = Clean(collection.Description);
        if (string.IsNullOrEmpty(profileId)) return new(description, null, null, CollectionPageAccess.None);
        if (PersonalCollectionOwnership.IsOwn(collection, profileId))
        {
            // Sync now needs a synced list and a server that can import; an unreadable capability reads as unable.
            var synced = CollectionHeroActions.CanSync(collection);
            var imports = false;
            if (synced)
            {
                try { imports = (await collections.GetCollectionCapabilitiesAsync(ct)).Imports == true; }
                catch (Exception ex) when (ex is not OperationCanceledException) { }
            }
            return new(description, null, null, new(CollectionPageAccessKind.Manage) { Personal = new(collection.Name, collection.IsShared, synced, imports) });
        }
        var librariesTask = LoadLibrariesAsync(catalog, ct);
        // Wait for the names, so the byline never shows a placeholder first. Without them the page still reads, just unsigned.
        string? owner = null;
        if (auth != null)
        {
            try
            {
                var profiles = await auth.GetProfilesAsync(ct);
                owner = profiles.Profiles.FirstOrDefault(profile => profile.Id == collection.CreatorProfileId)?.Name ?? "Another profile";
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        var pages = CollectionHeroActions.LibraryPages(true, CollectionHeroActions.PersonalLibraryIds(collection), await librariesTask);
        return new(description, null, null, new(CollectionPageAccessKind.ReadOnly, owner) { LibraryPages = pages });
    }

    private static async Task<CollectionPageHeader> LoadServerAsync(CollectionsApi collections, CatalogApi catalog, string id, int? libraryId, CancellationToken ct)
    {
        var librariesTask = LoadLibrariesAsync(catalog, ct);
        IReadOnlyList<ServerCollectionsLibrary>? listed = null;
        if (libraryId is not > 0)
        {
            // A first load that failed reads as no library, so the page stops waiting.
            try { listed = (await collections.GetServerCollectionsAsync(ct)).Libraries; }
            catch (Exception ex) when (ex is not OperationCanceledException) { listed = []; }
        }
        // Desktop has no administrator editor, so a server collection offers only Add to my Home.
        // Add to my library page lists the library the collection was opened in first, then the viewer's others.
        var pages = CollectionHeroActions.LibraryPages(false, libraryId is > 0 ? [libraryId.Value] : [], await librariesTask);
        var access = new CollectionPageAccess(CollectionPageAccessKind.ReadOnly) { LibraryPages = pages };
        foreach (var candidate in ServerLibraryIds(id, libraryId, listed) ?? [])
        {
            try
            {
                var tab = await catalog.GetLibraryCollectionsAsync(candidate, ct);
                var found = tab.Collections.FirstOrDefault(collection => collection.Id == id);
                if (found != null)
                    return new(Clean(found.Description), Clean(found.BackdropUrl), Clean(found.BackdropThumbhash), access);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
        return new(null, null, null, access);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
