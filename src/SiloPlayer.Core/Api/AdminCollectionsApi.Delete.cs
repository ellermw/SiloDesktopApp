using System.Text.Json.Nodes;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Core.Api;

public sealed partial class AdminCollectionsApi
{
    public async Task<IReadOnlyList<JsonObject>> RowsAsync(MonitorAuthorityLease lease, string id)
    {
        var body = (await ReadAsync(lease, Root + "/" + Id(id) + "/sections").ConfigureAwait(false)).Value;
        var rows = (body["items"] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [];
        if (rows.Length > 1000 || body["page"]?["has_more"]?.GetValue<bool>() == true ||
            rows.Any(r => string.IsNullOrEmpty(r["id"]?.GetValue<string>())) || rows.Select(r => r["id"]!.GetValue<string>()).Distinct().Count() != rows.Length)
            throw new InvalidDataException("Rows could not be completely read. No rows were changed.");
        return rows;
    }

    /// <summary>Read the collection validator before the confirmation. Never silently refresh consent on submit.</summary>
    public async Task<AdminCollectionDeletePlan> PrepareDeleteAsync(MonitorAuthorityLease lease, string id, bool sectionReferences)
    {
        var snapshot = await GetAsync(lease, id).ConfigureAwait(false);
        return new(snapshot, sectionReferences ? await RowsAsync(lease, id).ConfigureAwait(false) : []);
    }

    public async Task DeleteAsync(MonitorAuthorityLease lease, AdminCollectionDeletePlan plan, bool removeRows)
    {
        var id = plan.Collection.Value["id"]!.GetValue<string>();
        Check(lease, plan.Collection, Root + "/" + Id(id));
        if (plan.Rows.Count > 0 && !removeRows) throw new InvalidOperationException("The collection is used by a section.");
        var before = await GetAsync(lease, id).ConfigureAwait(false);
        if (before.ETag != plan.Collection.ETag) throw new AdminCollectionChangedException();
        var wrote = false;
        foreach (var row in plan.Rows)
        {
            var rowId = row["id"]!.GetValue<string>();
            try
            {
                var current = await ReadAsync(lease, "/api/v2/admin/sections/" + Id(rowId), true).ConfigureAwait(false);
                var config = current.Value["config"] as JsonObject ?? new JsonObject();
                if (current.Value["section_type"]?.GetValue<string>() == "collection_posters")
                {
                    var refs = config["collections"] as JsonArray;
                    if (refs == null || !refs.OfType<JsonObject>().Any(r => r["library_collection_id"]?.GetValue<string>() == id)) continue;
                    var remaining = refs.Where(r => r is not JsonObject o || o["library_collection_id"]?.GetValue<string>() != id).Select(r => r?.DeepClone()).ToArray();
                    if (remaining.Length > 0)
                    {
                        var updated = (JsonObject)config.DeepClone();
                        updated["collections"] = new JsonArray(remaining);
                        await WriteAsync(lease, HttpMethod.Patch, current.Path, new() { ["config"] = updated }, current).ConfigureAwait(false);
                        wrote = true;
                        continue;
                    }
                }
                else if (config["library_collection_id"]?.GetValue<string>() != id) continue;
                await WriteAsync(lease, HttpMethod.Delete, current.Path, observed: current).ConfigureAwait(false);
                wrote = true;
            }
            catch (ApiException error) when (error.StatusCode == 404) { /* Already removed; the collection DELETE still checks references. */ }
            // Any other failure stops immediately. A subsequent attempt must explicitly review a fresh plan.
        }
        var observed = plan.Collection;
        if (wrote)
        {
            var after = await GetAsync(lease, id).ConfigureAwait(false);
            if (!SameDefinition(before.Value, after.Value)) throw new AdminCollectionChangedException();
            observed = after; // Only this run's row changes can advance the revision without new consent.
        }
        await WriteAsync(lease, HttpMethod.Delete, observed.Path, observed: observed).ConfigureAwait(false);
    }

    private static bool SameDefinition(JsonObject first, JsonObject second)
    {
        var a = (JsonObject)first.DeepClone(); var b = (JsonObject)second.DeepClone();
        foreach (var key in new[] { "poster_url", "backdrop_url" }) { a.Remove(key); b.Remove(key); }
        return JsonNode.DeepEquals(a, b);
    }
}
