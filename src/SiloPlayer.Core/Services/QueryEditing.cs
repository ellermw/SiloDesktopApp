using System.Text.Json;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services;

public static class QueryEditing
{
    // A detached draft retains unknown fields and JsonElement value types.
    public static QueryDefinition Clone(QueryDefinition? query) => query == null ? new()
        : JsonSerializer.Deserialize<QueryDefinition>(JsonSerializer.Serialize(query))!;

    public static List<QueryGroup> PopulatedGroups(QueryDefinition query)
        => query.Groups.Select(group => new QueryGroup
        {
            Match = group.Match,
            AdditionalProperties = group.AdditionalProperties,
            // Only newly added empty text drafts are omitted. Stored null/unknown operators survive unrelated edits.
            Rules = group.Rules.Where(rule => rule.Value is not string text || !string.IsNullOrWhiteSpace(text)).ToList()
        }).Where(group => group.Rules.Count > 0).ToList();
}
