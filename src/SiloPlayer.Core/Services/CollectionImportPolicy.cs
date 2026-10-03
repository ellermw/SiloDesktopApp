using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services;

public static class CollectionImportPolicy
{
    public static bool CanCreate(CollectionCapabilitiesResponse capabilities, string source)
        => capabilities.Imports != false && (capabilities.ImportSources ?? ["mdblist", "tmdb"]).Contains(source, StringComparer.Ordinal);

    public static bool IsTMDBListUrl(string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value)) return false;
        if (long.TryParse(value, out var id) && id > 0) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
            uri.Host is not ("themoviedb.org" or "www.themoviedb.org")) return false;
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "list" && long.TryParse(parts[1].Split('-')[0], out id) && id > 0;
    }
}
