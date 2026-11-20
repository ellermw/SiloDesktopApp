using System.Text.Json.Nodes;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public sealed partial class AdminCollectionsApi
{
    public async Task<IReadOnlyList<(string Id, string Name)>> PeopleAsync(MonitorAuthorityLease lease, string text, CancellationToken ct)
    {
        if (text.Trim().Length < 2) return [];
        var body = (await ReadAsync(lease, "/api/v2/catalog/people?limit=20&q=" + Uri.EscapeDataString(text.Trim()), ct: ct).ConfigureAwait(false)).Value;
        return (body["items"] as JsonArray)?.OfType<JsonObject>().Take(20).Select(p => (p["id"]!.ToString(), p["name"]!.ToString())).ToArray() ?? [];
    }
    public async Task<IReadOnlyList<string>> FacetsAsync(MonitorAuthorityLease lease, string field, string text, string? library, CancellationToken ct)
    {
        var path = "/api/v2/catalog/filters/search?source=query&facet=" + Uri.EscapeDataString(field) + "&q=" + Uri.EscapeDataString(text.Trim()) + "&limit=50";
        if (library != null) path += "&library_ids=" + Id(library);
        var body = (await ReadAsync(lease, path, ct: ct).ConfigureAwait(false)).Value;
        return (body["values"] as JsonArray)?.OfType<JsonObject>().Take(50).Select(v => v["value"]!.ToString()).ToArray()
            ?? (body["matches"] as JsonArray)?.Take(50).Select(v => v!.ToString()).ToArray() ?? [];
    }
    public async Task<IReadOnlyList<string>> LanguagesAsync(MonitorAuthorityLease lease, string field, string? library, CancellationToken ct)
    {
        var body = (await ReadAsync(lease, "/api/v2/catalog/filters?source=query" + (library == null ? "" : "&library_ids=" + Id(library)), ct: ct).ConfigureAwait(false)).Value;
        var key = field switch { "original_language" => "original_languages", "audio_language" => "audio_languages", _ => "subtitle_languages" };
        return ((body["technical"] as JsonObject)?[key] as JsonArray ?? body[key] as JsonArray)?.Take(500).Select(v => v!.ToString()).ToArray() ?? [];
    }
    public async Task<QueryEditorCapabilities> QueryCapabilitiesAsync(MonitorAuthorityLease lease, CancellationToken ct)
    {
        var body = (await ReadAsync(lease, "/api/v2/catalog/search/capabilities", ct: ct).ConfigureAwait(false)).Value;
        var shown = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var ratings = (await ReadAsync(lease, "/api/v2/capabilities/ratings", ct: ct).ConfigureAwait(false)).Value;
            if (ratings["state"]?.ToString() == "available")
                foreach (var source in (ratings["sources"] as JsonArray ?? []).OfType<JsonObject>().Take(100))
                    if (source["source"]?.ToString() is { Length: > 0 } key) shown.Add(key);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { /* Saved rules survive; new unavailable rating choices stay hidden. */ }
        return new(body["extended_query_rules"]?.GetValue<bool>() == true, shown, body["show_title_rules"]?.GetValue<bool>() == true);
    }
    /// <summary>Public MDBList lists matching a search. Only asked when the capabilities report mdblist_search; at most 50 results are kept.</summary>
    public async Task<AdminMdbListSearch> MdbListSearchAsync(MonitorAuthorityLease lease, string query, CancellationToken ct)
    {
        var text = query.Trim();
        if (text.Length == 0 || text.Length > 256) throw new ArgumentException("Enter a search for MDBList.");
        var body = (await ReadAsync(lease, "/api/v2/collections/import/mdblist/search?q=" + Uri.EscapeDataString(text), ct: ct).ConfigureAwait(false)).Value;
        return ParseMdbListSearch(body);
    }
    /// <summary>The discovery envelope: { configured, items: [{ id, user_id, user_name, name, slug, description, media_type, items, likes, url }], page? }.</summary>
    public static AdminMdbListSearch ParseMdbListSearch(JsonObject body)
    {
        if (body["configured"] is not JsonValue configured || !configured.TryGetValue<bool>(out var isConfigured) || body["items"] is not JsonArray items)
            throw new InvalidDataException("The MDBList search answer was not recognized.");
        var lists = new List<AdminMdbListEntry>();
        foreach (var node in items.Take(50))
        {
            if (node is not JsonObject list || AdminCollectionsApi.CanonicalId(list["id"]) is not { } id || list["name"] is not JsonValue name || !name.TryGetValue<string>(out var title))
                throw new InvalidDataException("The MDBList search answer was not recognized.");
            lists.Add(new(id, title, list["user_name"]?.ToString() ?? "", list["description"]?.ToString() ?? "", list["media_type"]?.ToString() ?? "",
                list["items"] is JsonValue count && count.TryGetValue<int>(out var total) ? total : 0,
                list["likes"] is JsonValue likes && likes.TryGetValue<int>(out var liked) ? liked : 0, list["url"]?.ToString() ?? ""));
        }
        return new(isConfigured, lists);
    }
    /// <summary>The list a search hit follows: its own JSON feed, as the official picker stores it. Empty when MDBList reported no page.</summary>
    public static string MdbListFeed(AdminMdbListEntry entry) =>
        Uri.TryCreate(entry.Url, UriKind.Absolute, out var page) && page.Scheme == Uri.UriSchemeHttps && page.UserInfo.Length == 0 && page.Host.EndsWith("mdblist.com", StringComparison.OrdinalIgnoreCase)
            ? page.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/json" : "";
}
