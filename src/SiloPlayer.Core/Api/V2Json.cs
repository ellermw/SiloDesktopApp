using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SiloPlayer.Core.Api;

internal static class V2Json
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // Applied only at explicitly migrated contracts whose ID fields are strings.
    public static JsonElement Body(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, Options)!;
        ConvertIds(node);
        return JsonSerializer.SerializeToElement(node, Options);
    }

    private static void ConvertIds(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj.ToArray())
            {
                if (value == null) continue;
                if (key is "query_definition" or "display_query_definition" or "sort_config" or "provider_ids") continue;
                if (key is ("id" or "source_id" or "library_id" or "file_id" or "media_file_id" or "profile_id" or "user_id" or "default_profile_id") && value is JsonValue number && number.TryGetValue<long>(out var id))
                    obj[key] = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                else if (key is ("library_ids" or "allowed_profile_ids" or "ordered_ids") && value is JsonArray ids)
                {
                    for (int i = 0; i < ids.Count; i++)
                        if (ids[i] is JsonValue item && item.TryGetValue<long>(out var itemId)) ids[i] = itemId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else ConvertIds(value);
            }
        }
        else if (node is JsonArray array) foreach (var value in array) if (value != null) ConvertIds(value);
    }
}
