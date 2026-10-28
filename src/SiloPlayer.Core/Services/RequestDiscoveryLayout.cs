using SiloPlayer.Core.Models.Requests;

namespace SiloPlayer.Core.Services;

public static class RequestDiscoveryLayout
{
    public static string? BrandKind(string type) => type switch
    { "studios" => "studio", "networks" => "network", "genres" => "genre", _ => null };

    public static bool HasExploreAll(RequestDiscoverySection section)
        => section.TotalPages > 1 || section.TotalResults > section.Results.Count;

    public static List<RequestDiscoverRow> VisibleRows(RequestDiscoveryResponse response)
    {
        var rows = response.RowOrder ?? response.Sections.Select(section => new RequestDiscoverRow
        { Key = section.Key, Type = "tmdb_chart", Title = section.Title }).Concat(
        [ new() { Key = "studios", Type = "studios", Title = "Studios" },
          new() { Key = "networks", Type = "networks", Title = "Networks" },
          new() { Key = "genres", Type = "genres", Title = "Genres" } ]).ToList();
        var sections = response.Sections.Where(section => section.Results.Count > 0).Select(section => section.Key).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return rows.Where(row => !string.IsNullOrWhiteSpace(row.Key) && seen.Add(row.Key) &&
            (BrandKind(row.Type) != null || row.Type is "tmdb_chart" or "tmdb_discover" or "tmdb_list" or "mdblist" && sections.Contains(row.Key))).ToList();
    }
}
