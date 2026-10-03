using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Api;

public partial class CatalogApi
{
    private async Task<CatalogResponse> GetLargeCatalogQueryAsync(List<QueryGroup> groups, string match,
        int? libraryId, string? sort, string? order, string? genre, string? contentRating, string? yearMin,
        string? yearMax, string? q, string? type, int limit, int offset, bool includeTotal, string? snapshot,
        string? source, string? scope, string? sectionId, string? collectionId, int? queryLimit, CancellationToken ct, string? nextCursor)
    {
        var baseRules = new List<QueryRule>();
        void BaseRule(string field, string op, string? value) { if (value != null) baseRules.Add(new() { Field = field, Op = op, Value = value }); }
        BaseRule("genre", "is", genre); BaseRule("content_rating", "is", contentRating);
        BaseRule("year", "gte", yearMin); BaseRule("year", "lte", yearMax);
        // Plain GET filters combine with the structured expression using AND.
        // Distribute them through OR groups so no original group is flattened
        // or accidentally made optional in the body-only representation.
        if (baseRules.Count > 0)
        {
            if (match == "any")
                groups = groups.SelectMany(group => group.Match == "any"
                    ? group.Rules.Select(rule => new QueryGroup { Match = "all", Rules = new[] { rule }.Concat(baseRules).ToList() })
                    : new[] { new QueryGroup { Match = "all", Rules = group.Rules.Concat(baseRules).ToList() } }).ToList();
            else groups = groups.Concat(new[] { new QueryGroup { Match = "all", Rules = baseRules } }).ToList();
        }
        match = match == "any" && groups.Count > 1 ? "any" : "all";
        var body = new Dictionary<string, object?>
        {
            ["match"] = match, ["groups"] = groups, ["library_id"] = libraryId is > 0 ? libraryId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
            ["sort"] = sort?.TrimStart('-'), ["order"] = sort?.StartsWith('-') == true ? "desc" : order,
            ["q"] = q, ["type"] = type, ["source"] = source, ["scope"] = scope, ["section_id"] = sectionId,
            ["collection_id"] = collectionId, ["query_limit"] = queryLimit is > 0 ? queryLimit : null,
            ["skip_total"] = !includeTotal,
        }.Where(pair => pair.Value != null).ToDictionary(pair => pair.Key, pair => pair.Value);
        var context = client.CaptureContext(); var result = new CatalogResponse();
        limit = Math.Clamp(limit, 1, 200); offset = Math.Max(0, offset);
        while (result.Items.Count < limit)
        {
            ct.ThrowIfCancellationRequested();
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Catalog query context changed.", ct);
            var requested = Math.Min(100, limit - result.Items.Count);
            body["limit"] = requested;
            if (string.IsNullOrWhiteSpace(nextCursor)) body["seek"] = offset + result.Items.Count;
            else body.Remove("seek");
            var cursor = string.IsNullOrWhiteSpace(nextCursor) ? snapshot : nextCursor;
            if (!string.IsNullOrWhiteSpace(cursor)) body["cursor"] = cursor;
            else body.Remove("cursor");
            var page = await client.SendRequestAsync<CatalogResponse>(context, HttpMethod.Post, "/api/v2/catalog/query", body, ct);
            if (!client.IsCurrentContext(context)) throw new OperationCanceledException("Catalog query context changed.", ct);
            result.Items.AddRange(page.Items); result.Total = page.Total; result.TotalExact = page.TotalExact;
            result.Page = page.Page; result.HasMore = page.HasMore; result.EffectiveSort ??= page.EffectiveSort;
            result.Snapshot ??= page.Snapshot; snapshot ??= page.Snapshot;
            nextCursor = page.Items.Count == requested && page.HasMore && page.Snapshot == snapshot
                ? page.Page?.NextCursor : null;
            if (!page.HasMore || page.Items.Count == 0) break;
        }
        return result;
    }
}
