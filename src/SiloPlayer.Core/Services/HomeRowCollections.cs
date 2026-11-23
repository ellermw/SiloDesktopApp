using System.Text.Json;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Core.Services;

/// <summary>One collection a row of collections lists.</summary>
public sealed record HomeRowCollectionRef(string Source, string Id)
{
    /// <summary>Unique across server and personal collections.</summary>
    public string Key => Source + ":" + Id;
}

/// <summary>A library a collection is shown in, with the name the pickers offer it under.</summary>
public sealed record HomeRowCollectionLibrary(int Id, string Name);

/// <summary>A collection the Add row pickers offer (the WebUI's CollectionOption).</summary>
public sealed class HomeRowCollectionOption
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary><c>library</c> or <c>user</c>.</summary>
    public string Source { get; init; } = "library";
    /// <summary>On a personal collection: the profile that made it; others may share theirs.</summary>
    public string? CreatorProfileId { get; init; }
    /// <summary>Every library that shows this collection; the pickers filter by these. Empty on a personal collection.</summary>
    public List<HomeRowCollectionLibrary> Libraries { get; init; } = [];
    public string LibraryName => string.Join(", ", Libraries.Select(library => library.Name).Where(name => name.Length > 0));
    public string? CollectionType { get; init; }
    public int? ItemCount { get; init; }
    public string? PosterUrl { get; init; }
    public string? PosterThumbhash { get; init; }
    public string Key => Source + ":" + Id;
}

/// <summary>
/// A row of collections (section_type <c>collection_posters</c>): its config lists the collections it shows, in order, each by one id key,
/// <c>{"collections":[{"library_collection_id":"…"},{"user_collection_id":"…"}]}</c> (official silo-server #4410,
/// web/src/lib/homeRows/collectionList.ts, and #4471 for the library filter of the pickers).
/// </summary>
public static class HomeRowCollections
{
    public const string SectionType = "collection_posters";

    /// <summary>The most collections one row lists, as the server's recipe allows.</summary>
    public const int MaxCollections = 30;

    public const string AllKinds = "all";

    /// <summary>A row of collections: its cards are collections, not titles.</summary>
    public static bool Holds(string? sectionType) => string.Equals(sectionType, SectionType, StringComparison.Ordinal);

    public static string OptionKey(string source, string id) => source + ":" + id;

    /// <summary>The collections a row's config lists, in order. Entries it cannot read are skipped.</summary>
    public static List<HomeRowCollectionRef> RefsOf(IReadOnlyDictionary<string, object>? config)
    {
        var refs = new List<HomeRowCollectionRef>();
        if (config == null || !config.TryGetValue("collections", out var value) || value == null) return refs;
        JsonElement list;
        try { list = JsonSerializer.SerializeToElement(value); }
        catch (NotSupportedException) { return refs; }
        if (list.ValueKind != JsonValueKind.Array) return refs;
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (Text(entry, "library_collection_id") is { Length: > 0 } library) refs.Add(new("library", library));
            else if (Text(entry, "user_collection_id") is { Length: > 0 } user) refs.Add(new("user", user));
        }
        return refs;
    }

    private static string? Text(JsonElement entry, string name)
        => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>The config with <paramref name="refs"/> as its list; every other key stays. The list is written in the shape the server reads.</summary>
    public static Dictionary<string, object> WithRefs(IReadOnlyDictionary<string, object>? config, IEnumerable<HomeRowCollectionRef> refs)
    {
        var result = config == null ? new Dictionary<string, object>() : new Dictionary<string, object>(config);
        result["collections"] = refs.Select(entry => new Dictionary<string, string>
            { [entry.Source == "user" ? "user_collection_id" : "library_collection_id"] = entry.Id }).ToList();
        return result;
    }

    /// <summary>Whether the server would take the list: at least one collection, at most <see cref="MaxCollections"/>.</summary>
    public static bool CanSave(IReadOnlyCollection<HomeRowCollectionRef> refs) => refs.Count is > 0 and <= MaxCollections;

    /// <summary>The list is full: only a chosen collection can still be pressed, to take it out.</summary>
    public static bool IsFull(int chosen) => chosen >= MaxCollections;

    /// <summary>Moves one entry; an index outside the list leaves it as it is.</summary>
    public static List<HomeRowCollectionRef> Move(IReadOnlyList<HomeRowCollectionRef> refs, int from, int to)
    {
        var result = refs.ToList();
        if (from < 0 || from >= result.Count || to < 0 || to >= result.Count || from == to) return result;
        var entry = result[from]; result.RemoveAt(from); result.Insert(to, entry);
        return result;
    }

    /// <summary>Adds the collection when it is not in the list and the list has room; takes it out when it is.</summary>
    public static List<HomeRowCollectionRef> Toggle(IReadOnlyList<HomeRowCollectionRef> refs, string source, string id)
    {
        var key = OptionKey(source, id);
        if (refs.Any(entry => entry.Key == key)) return refs.Where(entry => entry.Key != key).ToList();
        return IsFull(refs.Count) ? refs.ToList() : [.. refs, new HomeRowCollectionRef(source, id)];
    }

    /// <summary>The library collection ids a row of collections lists.</summary>
    public static List<string> LibraryCollectionIdsOf(IReadOnlyDictionary<string, object>? config)
        => RefsOf(config).Where(entry => entry.Source == "library").Select(entry => entry.Id).ToList();

    // ── The options the pickers offer ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// This profile's own collections and the library collections it can open, one option per collection. A collection shown in several libraries is one
    /// option that names each (web/src/hooks/queries/useAllUserCollections.ts).
    /// </summary>
    public static List<HomeRowCollectionOption> BuildOptions(
        IReadOnlyList<Library> libraries,
        IEnumerable<Collection>? userCollections,
        IReadOnlyList<IReadOnlyList<LibraryCollection>?> libraryCollectionsByLibrary)
    {
        var options = new List<HomeRowCollectionOption>();
        foreach (var collection in userCollections ?? [])
            options.Add(new()
            {
                Id = collection.Id, Title = collection.Name, Source = "user", CreatorProfileId = collection.CreatorProfileId,
                CollectionType = collection.CollectionType, ItemCount = collection.ItemCount,
                PosterUrl = collection.PosterUrl, PosterThumbhash = collection.PosterThumbhash,
            });
        var byId = new Dictionary<string, HomeRowCollectionOption>(StringComparer.Ordinal);
        for (var index = 0; index < libraries.Count && index < libraryCollectionsByLibrary.Count; index++)
        {
            var library = libraries[index];
            foreach (var collection in libraryCollectionsByLibrary[index] ?? [])
            {
                if (byId.TryGetValue(collection.Id, out var existing))
                {
                    if (existing.Libraries.All(shown => shown.Id != library.Id)) existing.Libraries.Add(new(library.Id, library.Name));
                    continue;
                }
                var option = new HomeRowCollectionOption
                {
                    Id = collection.Id, Title = collection.Title, Source = "library", Libraries = [new(library.Id, library.Name)],
                    CollectionType = collection.CollectionType, ItemCount = collection.ItemCount,
                    PosterUrl = collection.PosterUrl, PosterThumbhash = collection.PosterThumbhash,
                };
                byId[collection.Id] = option;
                options.Add(option);
            }
        }
        return options;
    }

    /// <summary>Manual, Smart or Synced; every other stored type is a synced list.</summary>
    public static string KindOf(string? collectionType) => collectionType is "manual" or "smart" ? collectionType : "synced";

    /// <summary>The plain name of a collection type, as the pickers print it.</summary>
    public static string? KindLabel(string? collectionType)
    {
        if (string.IsNullOrEmpty(collectionType)) return null;
        return KindOf(collectionType) switch
        {
            "manual" => AppStrings.Get("web.collections.types.kindManual", null, "Manual"),
            "smart" => AppStrings.Get("web.collections.types.kindSmart", null, "Smart"),
            _ => AppStrings.Get("web.collections.types.kindSynced", null, "Synced list"),
        };
    }

    /// <summary>The libraries that show any option, by name. A library whose name is not known cannot be offered, as in the WebUI.</summary>
    public static List<HomeRowCollectionLibrary> LibrariesOf(IEnumerable<HomeRowCollectionOption> options)
    {
        var found = new Dictionary<int, HomeRowCollectionLibrary>();
        foreach (var option in options)
            foreach (var library in option.Libraries)
                if (library.Name.Length > 0 && !found.ContainsKey(library.Id)) found[library.Id] = library;
        return found.Values.OrderBy(library => library.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// The library filter in force: the pick while it still names a library that shows an option and two or more libraries remain, otherwise every
    /// library (null), so an earlier pick never keeps hiding collections.
    /// </summary>
    public static int? EffectiveLibrary(IReadOnlyList<HomeRowCollectionLibrary> libraries, int? picked)
        => libraries.Count > 1 && picked is { } id && libraries.Any(library => library.Id == id) ? id : null;

    /// <summary>The options that pass the search text, the type filter ("all", "manual", "smart" or "synced") and the library filter (null for every library).</summary>
    public static List<HomeRowCollectionOption> Filter(IEnumerable<HomeRowCollectionOption> options, string? query, string kind, int? library)
    {
        var needle = (query ?? "").Trim();
        return options.Where(option =>
            (kind == AllKinds || (!string.IsNullOrEmpty(option.CollectionType) && KindOf(option.CollectionType) == kind))
            && (library == null || option.Libraries.Any(entry => entry.Id == library))
            && option.Title.Contains(needle, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    /// <summary>"Movies · Smart · 12 titles": the library tells apart collections of the same name in several libraries.</summary>
    public static string Meta(HomeRowCollectionOption option)
        => string.Join(" · ", new[]
        {
            option.LibraryName, KindLabel(option.CollectionType) ?? "",
            option.ItemCount is { } count ? AdminHomeSectionsDescription.TitleCount(count) : "",
        }.Where(part => part.Length > 0));

    /// <summary>What an empty list says: still loading, failed, no collections, no match for the search, none in the picked library, or none of the picked type.</summary>
    public static string EmptyText(bool loading, bool failed, int total, string? query, bool libraryPicked)
    {
        var search = (query ?? "").Trim();
        if (loading) return HomeRowText.T("web.home.addRow.collection.loading");
        if (failed) return HomeRowText.T("web.home.addRow.collection.failed");
        if (total == 0) return HomeRowText.T("web.home.addRow.collection.none");
        if (search.Length > 0) return HomeRowText.T("web.home.addRow.collection.noMatch", new { query = search });
        return HomeRowText.T(libraryPicked ? "web.home.addRow.collection.noneInLibrary" : "web.home.addRow.collection.noneOfType");
    }

    /// <summary>"Yours" or "by Name" on a personal collection; nothing on a server one (the WebUI's useOwnerLine).</summary>
    public static string? OwnerLine(HomeRowCollectionOption option, string? viewingProfileId, Func<string, string> ownerName)
    {
        if (option.Source != "user" || string.IsNullOrEmpty(option.CreatorProfileId)) return null;
        return option.CreatorProfileId == viewingProfileId
            ? HomeRowText.T("web.home.addRow.collections.yours")
            : HomeRowText.T("web.collections.posterCard.byOwner", new { owner = ownerName(option.CreatorProfileId) });
    }

    /// <summary>The meta line under a chosen or offered collection: its library, type and title count, then its owner line.</summary>
    public static string MetaWithOwner(HomeRowCollectionOption option, string? ownerLine)
        => string.Join(" · ", new[] { Meta(option), ownerLine ?? "" }.Where(part => part.Length > 0));

    // ── Row text ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>"3 collections": what the rows list says of a row of collections whose collections it cannot look up.</summary>
    public static string CountText(int count) => HomeRowText.T("web.home.rows.describe.collectionCount", AdminHomeSectionsText.Count(count));

    /// <summary>The row line's description of a row of collections.</summary>
    public static string Describe(IReadOnlyDictionary<string, object>? config) => CountText(RefsOf(config).Count);

    /// <summary>Whether the row's line names its title limit: a row of collections has none, its description counts its collections.</summary>
    public static bool ShowsTitleLimit(string? sectionType) => !Holds(sectionType);

    /// <summary>The editor's count line under the chosen list.</summary>
    public static string ChosenCountText(int count) => HomeRowText.T("web.home.addRow.collections.count", AdminHomeSectionsText.Count(count));

    public static string LimitText() => HomeRowText.T("web.home.addRow.collections.limit", AdminHomeSectionsText.Count(MaxCollections));

    /// <summary>
    /// A row of collections starts as "Collections", its kind's own name, not the preset's "A row of collections" (the WebUI's draftForPreset).
    /// </summary>
    public const string DefaultRowName = "Collections";

    // ── Origin ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Which tag a row of the profile surface carries: the server's own row, or one this profile added.</summary>
    public static RowOrigin OriginOf(bool isCustom) => isCustom ? RowOrigin.Yours : RowOrigin.Server;
}

/// <summary>Whether a Home row comes from the server or was added by this profile itself (the WebUI's RowOriginBadge). Never shown on the admin surface.</summary>
public enum RowOrigin { Server, Yours }
