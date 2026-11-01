using System.Globalization;
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
    private static TimeRange? Range(JsonElement value, TimeRange? previous)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        if (!(value.TryGetProperty("start_seconds", out var a) || value.TryGetProperty("start", out a)) ||
            !(value.TryGetProperty("end_seconds", out var b) || value.TryGetProperty("end", out b)) ||
            !TryNumber(a, out var start) || !TryNumber(b, out var end) ||
            !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start) return null;
        // Optional provenance must never prevent valid timing or later kinds from applying.
        var next = new TimeRange
        {
            Start = start, End = end,
            Source = String(value, "source"), Provider = String(value, "provider"),
            Algorithm = String(value, "algorithm"),
            Confidence = value.TryGetProperty("confidence", out var confidence) && TryNumber(confidence, out var number) ? number : null,
        };
        // Timing-only snapshots leave this kind's accepted provenance unchanged,
        // even when its timing or another kind changed. Explicit details (including
        // nulls) replace provenance, so a manual edit never inherits confidence.
        if (previous is not null &&
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

    private static string? String(JsonElement value, string name) =>
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static bool TryNumber(JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number ? value.TryGetDouble(out number) :
            value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}
