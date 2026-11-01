using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static class PlaybackMarkerRanges
{
    public static TimeRange? Active(IEnumerable<PlaybackMarkerSegment> segments, string kind, double position, TimeRange? provenance = null)
    {
        var range = segments.FirstOrDefault(segment => segment is not null && segment.Kind == kind &&
            double.IsFinite(segment.StartSeconds) && double.IsFinite(segment.EndSeconds) &&
            segment.StartSeconds >= 0 && segment.EndSeconds > segment.StartSeconds &&
            position >= segment.StartSeconds && position < segment.EndSeconds);
        return range == null ? null : new TimeRange { Start = range.StartSeconds, End = range.EndSeconds,
            Source = provenance?.Source, Provider = provenance?.Provider,
            Confidence = provenance?.Confidence, Algorithm = provenance?.Algorithm };
    }
}
