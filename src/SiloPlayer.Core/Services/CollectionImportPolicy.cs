using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services;

public static class CollectionImportPolicy
{
    public static bool CanCreateTemplate(CollectionCapabilitiesResponse capabilities, CollectionTemplate template)
        => !template.RequiresProfile && template.Source is "mdblist" or "tmdb" or "tmdb_list" && CanCreate(capabilities, template.Source);
    public static bool CanCreate(CollectionCapabilitiesResponse capabilities, string source)
        => capabilities.Imports != false && (capabilities.ImportSources ?? []).Contains(source, StringComparer.Ordinal);

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
    public static string CleanMDBListLink(string? input)
        => (input ?? "").Trim().Split('#')[0].Split('?')[0].TrimEnd('/');

    public static bool IsMDBListUrl(string? input)
    {
        if (!Uri.TryCreate(CleanMDBListLink(input), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
            uri.UserInfo.Length > 0 || uri.Port is not (80 or 443) ||
            uri.Host.TrimEnd('.').ToLowerInvariant() is not ("mdblist.com" or "www.mdblist.com")) return false;
        return uri.AbsolutePath.StartsWith("/lists/", StringComparison.Ordinal) && uri.AbsolutePath.Length > 7 && uri.AbsolutePath[7] != '/';
    }
}
