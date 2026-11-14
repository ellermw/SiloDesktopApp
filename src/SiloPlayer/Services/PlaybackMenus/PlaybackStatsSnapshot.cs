using System.Text.Json;

namespace SiloPlayer.Services.PlaybackMenus;

internal sealed record PlaybackStatsRow(string Id, string Section, string Label, string Value);

/// <summary>Bounded display data from the active OSC's existing visible-only diagnostic collector.</summary>
internal static class PlaybackStatsSnapshot
{
    internal static bool IsLiveCounter(string id) => id is
        "video.display-area" or "video.droppedFrames" or
        "stream.video-bitrate-measured" or "stream.audio-bitrate-measured" or
        // Legacy English OSC payloads did not carry explicit row ids.
        "VIDEO INFO\nDisplay area" or "VIDEO INFO\nDropped frames" or
        "PLAYBACK STREAM INFO\nVideo bitrate (measured)" or "PLAYBACK STREAM INFO\nAudio bitrate (measured)";

    private static bool IsPlaceholder(string value) => value is "" or "—" or "Unavailable";

    // Keep the last spoken values, not the last sampled values: when the live
    // interval elapses the announcement must catch up to the current counters.
    internal static List<PlaybackStatsRow> ChangedRows(IReadOnlyList<PlaybackStatsRow> rows,
        Dictionary<string, string> spoken, bool liveDue)
    {
        var present = rows.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in spoken.Keys.Where(id => !present.Contains(id)).ToList()) spoken.Remove(id);
        var changed = new List<PlaybackStatsRow>();
        foreach (var row in rows)
        {
            var known = spoken.TryGetValue(row.Id, out var previous);
            if (known && previous == row.Value) continue;
            if (IsPlaceholder(row.Value)) { spoken[row.Id] = row.Value; continue; }
            // A live counter's first real value is spoken immediately.
            if (known && !IsPlaceholder(previous!) && IsLiveCounter(row.Id) && !liveDue) continue;
            spoken[row.Id] = row.Value;
            changed.Add(row);
        }
        return changed;
    }

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
                    // Older OSC payloads have no explicit identity. New payloads
                    // must never derive identity from localized display text.
                    var id = header + "\n" + label;
                    if (row.TryGetProperty("id", out _) &&
                        (!Text(row, "id", 256, out id) || string.IsNullOrEmpty(id))) return null;
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
