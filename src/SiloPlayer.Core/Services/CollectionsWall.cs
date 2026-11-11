using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services;

/// <summary>
/// The Collections page's one poster wall: what the Show pills offer, which pill is
/// current, what the wall is called and when it may say a view is empty. This is the
/// pinned WebUI page's logic (<c>Collections.tsx</c>) without its React state.
/// </summary>
public static class CollectionsWall
{
    public const string All = "all";
    public const string Mine = "mine";
    public const string Shared = "shared";
    public const string Server = "server";
    public const string LibraryPrefix = "library:";

    public const string EmptyMine = "You haven't made any collections yet.";
    public const string EmptyAll = "There are no collections yet.";

    /// <summary>One Show pill. <see cref="Count"/> is null when the page cannot know it yet.</summary>
    public sealed record Pill(string Value, string Label, int? Count, bool Separated = false);

    /// <summary>Which first reads are still on their way (none of the three has produced data yet).</summary>
    public sealed record Loading(bool PersonalLoading, bool ProfilesLoading, bool ServerLoading);

    public static string Library(int libraryId) => LibraryPrefix + libraryId;

    public static bool IsLibrary(string show) => show.StartsWith(LibraryPrefix, StringComparison.Ordinal);

    /// <summary>A lone library or an entirely empty set has a known count; non-empty libraries may overlap.</summary>
    public static int? ServerCount(IReadOnlyList<ServerCollectionsLibrary> libraries)
        => libraries.All(library => library.TotalCount == 0) ? 0 : libraries.Count == 1 ? libraries[0].TotalCount : null;

    /// <summary>
    /// The pills. All, Mine, then Shared and Server when the account has them, then one pill per
    /// library once there is more than one. Totals are per library and a collection can be in
    /// several, so the combined count is known only for a lone library or when all totals are zero.
    /// </summary>
    public static IReadOnlyList<Pill> Pills(IReadOnlyList<ServerCollectionsLibrary> libraries, int ownCount, int sharedCount)
    {
        var serverCount = ServerCount(libraries);
        var pills = new List<Pill>
        {
            new(All, "All", serverCount is { } server ? ownCount + sharedCount + server : null),
            new(Mine, "Mine", ownCount),
        };
        if (sharedCount > 0) pills.Add(new(Shared, "Shared", sharedCount));
        if (libraries.Count > 0) pills.Add(new(Server, "Server", serverCount));
        if (libraries.Count > 1)
            for (var index = 0; index < libraries.Count; index++)
                pills.Add(new(Library(libraries[index].LibraryId), libraries[index].LibraryName, libraries[index].TotalCount, index == 0));
        return pills;
    }

    /// <summary>The pills only appear when there is something besides Mine to filter between.</summary>
    public static bool ShowPills(IReadOnlyList<ServerCollectionsLibrary> libraries, int sharedCount)
        => sharedCount > 0 || libraries.Count > 0;

    /// <summary>
    /// A filter the account has nothing for falls back to All. A filter requested for the page is kept
    /// while what it needs loads, so a revisit doesn't flash All. A lone library has no pill of its own
    /// and shows as Server.
    /// </summary>
    public static string Resolve(string? requested, IReadOnlyList<ServerCollectionsLibrary> libraries, int sharedCount, Loading loading)
    {
        if (LibraryId(requested) is { } libraryId)
        {
            if (loading.ServerLoading) return Library(libraryId);
            return libraries.Any(library => library.LibraryId == libraryId) ? (libraries.Count > 1 ? Library(libraryId) : Server) : All;
        }
        return requested switch
        {
            Mine => Mine,
            Shared when sharedCount > 0 || loading.PersonalLoading || loading.ProfilesLoading => Shared,
            Server when libraries.Count > 0 || loading.ServerLoading => Server,
            _ => All,
        };
    }

    public static int? LibraryId(string? show)
        => show != null && IsLibrary(show) && int.TryParse(show.AsSpan(LibraryPrefix.Length), out var id) ? id : null;

    /// <summary>The library a resolved Show names, if it names one that is still listed.</summary>
    public static ServerCollectionsLibrary? Picked(string show, IReadOnlyList<ServerCollectionsLibrary> libraries)
        => LibraryId(show) is { } id ? libraries.FirstOrDefault(library => library.LibraryId == id) : null;

    public static bool ShowsMine(string show) => show is All or Mine;
    public static bool ShowsShared(string show, int sharedCount) => sharedCount > 0 && show is All or Shared;
    public static bool ShowsServer(string show) => show is not (Mine or Shared);

    public static string Label(string show, ServerCollectionsLibrary? picked) => show switch
    {
        Mine => "Your collections",
        Shared => "Shared with me",
        Server => "Server collections",
        _ when picked != null => picked.LibraryName + " collections",
        _ => "All collections",
    };

    /// <summary>
    /// A view with nothing in it says so rather than leaving the wall blank, but only once what it
    /// shows has loaded: a slow or failed request isn't "none".
    /// </summary>
    public static string? EmptyMessage(string show, bool personalLoaded, bool profilesLoaded, bool serverLoaded,
        int ownCount, int sharedCount, int? serverCount)
    {
        if (show == Mine && personalLoaded && ownCount == 0) return EmptyMine;
        if (show == All && personalLoaded && profilesLoaded && serverLoaded && ownCount == 0 && sharedCount == 0 && serverCount == 0) return EmptyAll;
        if ((show == Server || IsLibrary(show)) && serverLoaded && serverCount == 0) return EmptyAll;
        return null;
    }
}
