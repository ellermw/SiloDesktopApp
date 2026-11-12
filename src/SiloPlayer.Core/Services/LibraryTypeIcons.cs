namespace SiloPlayer.Core.Services;

/// <summary>
/// The sidebar icon a library type uses. Mirrors the WebUI's admin library-type map
/// (<c>web/src/components/admin/libraries/libraryTypes.ts</c>), including the other spellings
/// <c>internal/librarykind</c> accepts, because the server stores a library's type as free text.
/// </summary>
public static class LibraryTypeIcons
{
    public const string FallbackIcon = "library";

    /// <summary>The canonical type for a stored spelling, or null when the type is not one the server names.</summary>
    public static string? CanonicalType(string? type)
    {
        var kind = (type ?? "").Trim().ToLowerInvariant();
        return kind switch
        {
            "movies" or "movie" => "movies",
            "series" or "tv" or "show" or "tvshows" => "series",
            "mixed" => "mixed",
            "audiobooks" or "audiobook" => "audiobooks",
            "ebooks" or "ebook" => "ebooks",
            "manga" => "manga",
            "podcasts" or "podcast" => "podcasts",
            _ => null,
        };
    }

    /// <summary>The Lucide asset name under <c>Assets/Icons</c>; the generic library icon for an unknown type.</summary>
    public static string IconName(string? type) => CanonicalType(type) switch
    {
        "movies" => "film",
        "series" => "tv",
        "mixed" => "layers",
        "audiobooks" => "book-headphones",
        "ebooks" => "book-open",
        "manga" => "book-marked",
        "podcasts" => "podcast",
        _ => FallbackIcon,
    };
}
