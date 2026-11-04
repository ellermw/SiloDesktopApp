using System.Text.Json;

namespace SiloPlayer.Services.PlaybackMenus;

internal sealed record PlaybackStatsRow(string Id, string Section, string Label, string Value);

/// <summary>Bounded display data from the active OSC's existing visible-only diagnostic collector.</summary>
internal static class PlaybackStatsSnapshot
{
    internal static IReadOnlyList<PlaybackStatsRow>? Parse(string json)
    {
        if (json.Length > 65536) return null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > 8) return null;
            var rows = new List<PlaybackStatsRow>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var section in document.RootElement.EnumerateArray())
            {
                if (!Text(section, "header", 128, out var header) ||
                    !section.TryGetProperty("rows", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 128) return null;
                foreach (var row in items.EnumerateArray())
                {
                    if (!Text(row, "label", 256, out var label) || !Text(row, "value", 2048, out var value)) return null;
                    var id = header + "\n" + label;
                    if (!ids.Add(id)) return null;
                    rows.Add(new(id, header, label, value));
                }
            }
            return rows;
        }
        catch (JsonException) { return null; }
    }

    private static bool Text(JsonElement item, string property, int maximum, out string value)
    {
        value = "";
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(property, out var text) || text.ValueKind != JsonValueKind.String) return false;
        value = text.GetString() ?? "";
        return value.Length <= maximum;
    }
}
