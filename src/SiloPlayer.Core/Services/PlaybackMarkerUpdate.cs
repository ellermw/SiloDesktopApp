using System.Text.Json;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static class PlaybackMarkerUpdate
{
    public static void Apply(FileVersion version, JsonElement payload)
    {
        if (payload.TryGetProperty("marker_segments", out var segments) && segments.ValueKind == JsonValueKind.Array)
            version.MarkerSegments = JsonSerializer.Deserialize<List<PlaybackMarkerSegment>>(segments.GetRawText()) ?? [];
        if (payload.TryGetProperty("intro", out var intro)) version.Intro = Range(intro, version.Intro);
        if (payload.TryGetProperty("credits", out var credits)) version.Credits = Range(credits, version.Credits);
        if (payload.TryGetProperty("recap", out var recap)) version.Recap = Range(recap, version.Recap);
        if (payload.TryGetProperty("preview", out var preview)) version.Preview = Range(preview, version.Preview);
    }
    private static readonly JsonSerializerOptions RangeJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static TimeRange? Range(JsonElement value, TimeRange? previous)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        if (!(value.TryGetProperty("start_seconds", out var a) || value.TryGetProperty("start", out a)) ||
            !(value.TryGetProperty("end_seconds", out var b) || value.TryGetProperty("end", out b)) ||
            a.ValueKind != JsonValueKind.Number || !a.TryGetDouble(out var start) ||
            b.ValueKind != JsonValueKind.Number || !b.TryGetDouble(out var end) ||
            !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start) return null;
        var next = JsonSerializer.Deserialize<TimeRange>(value.GetRawText(), RangeJson);
        // Timing-only snapshots leave this kind's accepted provenance unchanged,
        // even when its timing or another kind changed. Explicit details (including
        // nulls) replace provenance, so a manual edit never inherits confidence.
        if (next is not null && previous is not null &&
            !new[] { "source", "provider", "confidence", "algorithm" }.Any(key => value.TryGetProperty(key, out _)))
        {
            if (next.Start == previous.Start && next.End == previous.End) return previous;
            next.Source = previous.Source;
            next.Provider = previous.Provider;
            next.Confidence = previous.Confidence;
            next.Algorithm = previous.Algorithm;
        }
        return next;
    }
}
