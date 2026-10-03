using System.Text.Json;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static class PlaybackMarkerUpdate
{
    public static void Apply(FileVersion version, JsonElement payload)
    {
        if (payload.TryGetProperty("marker_segments", out var segments))
            version.MarkerSegments = segments.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize<List<PlaybackMarkerSegment>>(segments.GetRawText()) ?? [] : [];
        if (payload.TryGetProperty("intro", out var intro)) version.Intro = Range(intro);
        if (payload.TryGetProperty("credits", out var credits)) version.Credits = Range(credits);
        if (payload.TryGetProperty("recap", out var recap)) version.Recap = Range(recap);
        if (payload.TryGetProperty("preview", out var preview)) version.Preview = Range(preview);
    }
    private static TimeRange? Range(JsonElement value) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty("start", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetDouble(out var start)
        && value.TryGetProperty("end", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetDouble(out var end)
        && double.IsFinite(start) && double.IsFinite(end) && start >= 0 && end > start ? new() { Start = start, End = end } : null;
}
