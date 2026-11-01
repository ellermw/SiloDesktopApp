using System.Text.Json;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static class PlaybackMarkerUpdate
{
    public static void Apply(FileVersion version, JsonElement payload)
    {
        // The production notifier broadcasts timing-only snapshots. Keep the
        // watch response's provenance when that entire inventory is unchanged.
        if (IsTimingOnlyNoOp(version, payload)) return;
        if (payload.TryGetProperty("marker_segments", out var segments))
            version.MarkerSegments = segments.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize<List<PlaybackMarkerSegment>>(segments.GetRawText()) ?? [] : [];
        if (payload.TryGetProperty("intro", out var intro)) version.Intro = Range(intro);
        if (payload.TryGetProperty("credits", out var credits)) version.Credits = Range(credits);
        if (payload.TryGetProperty("recap", out var recap)) version.Recap = Range(recap);
        if (payload.TryGetProperty("preview", out var preview)) version.Preview = Range(preview);
    }
    private static bool IsTimingOnlyNoOp(FileVersion version, JsonElement payload)
    {
        foreach (var (kind, previous) in new[] { ("intro", version.Intro), ("credits", version.Credits), ("recap", version.Recap), ("preview", version.Preview) })
        {
            if (!payload.TryGetProperty(kind, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Object && new[] { "source", "provider", "confidence", "algorithm" }.Any(key => value.TryGetProperty(key, out _))) return false;
            var next = Range(value);
            if (next?.Start != previous?.Start || next?.End != previous?.End) return false;
        }
        if (!payload.TryGetProperty("marker_segments", out var raw)) return true;
        var segments = raw.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<PlaybackMarkerSegment>>(raw.GetRawText()) ?? [] : [];
        return segments.Select(segment => (segment.Kind, segment.StartSeconds, segment.EndSeconds))
            .SequenceEqual((version.MarkerSegments ?? []).Select(segment => (segment.Kind, segment.StartSeconds, segment.EndSeconds)));
    }
    private static readonly JsonSerializerOptions RangeJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static TimeRange? Range(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        if (!(value.TryGetProperty("start_seconds", out var a) || value.TryGetProperty("start", out a)) ||
            !(value.TryGetProperty("end_seconds", out var b) || value.TryGetProperty("end", out b)) ||
            a.ValueKind != JsonValueKind.Number || !a.TryGetDouble(out var start) ||
            b.ValueKind != JsonValueKind.Number || !b.TryGetDouble(out var end) ||
            !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start) return null;
        return JsonSerializer.Deserialize<TimeRange>(value.GetRawText(), RangeJson);
    }
}
