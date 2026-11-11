using System.Text.Json;
using SiloPlayer.Core.Models.Collections;

namespace SiloPlayer.Core.Services;

public static class QueryEditing
{
    public static void SetMediaScope(QueryDefinition query, string? scope)
    {
        if (scope == "ebook" && query.MediaScope != "ebook")
        {
            foreach (var group in query.Groups) group.Rules.RemoveAll(rule => rule.Field == "narrator");
            query.Groups.RemoveAll(group => group.Rules.Count == 0);
        }
        query.MediaScope = scope == "all" ? null : scope;
    }

    // A detached draft retains unknown fields and JsonElement value types.
    public static QueryDefinition Clone(QueryDefinition? query) => query == null ? new()
        : JsonSerializer.Deserialize<QueryDefinition>(JsonSerializer.Serialize(query))!;

    /// <summary>
    /// Mirrors the official server's <c>QueryDefinition.HasCompleteRule</c>: some rule has a
    /// non-blank field, a non-blank operator and a filled value. Without one a Smart
    /// collection matches every title, so the API answers 422 and the editors neither
    /// preview nor save it.
    /// </summary>
    public static bool HasCompleteRule(QueryDefinition? query)
        => query?.Groups?.Any(group => group?.Rules?.Any(IsCompleteRule) == true) == true;

    public static bool IsCompleteRule(QueryRule? rule)
        => rule != null
            && !string.IsNullOrWhiteSpace(rule.Field)
            && !string.IsNullOrWhiteSpace(rule.Op)
            && IsFilledValue(rule.Value);

    /// <summary>
    /// A missing value, blank text, an empty array or an array with an unfilled entry filters
    /// nothing yet. Numbers and booleans, zero and false included, are values.
    /// </summary>
    public static bool IsFilledValue(object? value)
    {
        switch (value)
        {
            case null:
                return false;
            case string text:
                return !string.IsNullOrWhiteSpace(text);
            case JsonElement element:
                return element.ValueKind switch
                {
                    JsonValueKind.Undefined or JsonValueKind.Null => false,
                    JsonValueKind.String => !string.IsNullOrWhiteSpace(element.GetString()),
                    JsonValueKind.Array => element.GetArrayLength() > 0
                        && element.EnumerateArray().All(entry => IsFilledValue(entry)),
                    _ => true,
                };
            case System.Collections.IEnumerable sequence and not System.Collections.IDictionary:
            {
                var any = false;
                foreach (var entry in sequence)
                {
                    any = true;
                    if (!IsFilledValue(entry)) return false;
                }
                return any;
            }
            default:
                return true;
        }
    }

    public static List<QueryGroup> PopulatedGroups(QueryDefinition query)
        => query.Groups.Select(group => new QueryGroup
        {
            Match = group.Match,
            AdditionalProperties = group.AdditionalProperties,
            // Only newly added empty text drafts are omitted. Stored null/unknown operators survive unrelated edits.
            Rules = group.Rules.Where(rule => rule.Value is not string text || !string.IsNullOrWhiteSpace(text)).ToList()
        }).Where(group => group.Rules.Count > 0).ToList();
}
