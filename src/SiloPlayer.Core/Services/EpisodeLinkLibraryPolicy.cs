using SiloPlayer.Core.Models.Catalog;

namespace SiloPlayer.Core.Services;

/// <summary>
/// The library an episode card opens its detail page with, mirroring the WebUI's
/// <c>episodeLinkLibraryId</c> (<c>web/src/pages/ItemDetail/itemDetailLayout.ts</c>). An episode
/// whose list entry has no file keeps no library: with the administrator setting
/// <c>catalog.scope_versions_to_library</c> on, a library-scoped list holds only that library's
/// files, and the episode would then open with no version to play. Series, season and Play links
/// still keep the library.
/// </summary>
public static class EpisodeLinkLibraryPolicy
{
    public static int? LibraryFor(Episode episode, int? libraryId)
        => (episode.Files?.Count ?? 0) > 0 ? libraryId : null;
}
