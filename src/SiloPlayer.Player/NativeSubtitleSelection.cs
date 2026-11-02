namespace SiloPlayer.Player;

public sealed record NativeSubtitleTrack(int Id, int? StreamIndex, string? ContainerTrackId,
    string? Codec, string? Language, string? Title, bool Forced = false, bool HearingImpaired = false);

/// <summary>Matches server identity to loaded subtitle IDs; combined ordinals are never native IDs.</summary>
public static class NativeSubtitleSelection
{
    public static int Resolve(NativeSubtitleTrack requested, IReadOnlyList<NativeSubtitleTrack> loaded,
        bool trustworthyStreamIndex)
    {
        IEnumerable<NativeSubtitleTrack> matches;
        if (!string.IsNullOrWhiteSpace(requested.ContainerTrackId))
        {
            matches = loaded.Where(t => t.ContainerTrackId == requested.ContainerTrackId &&
                (!trustworthyStreamIndex || requested.StreamIndex == null || t.StreamIndex == requested.StreamIndex));
        }
        else if (requested.StreamIndex is not null)
        {
            if (!trustworthyStreamIndex) return 0;
            matches = loaded.Where(t => t.StreamIndex == requested.StreamIndex);
        }
        else
        {
            // Older responses have no absolute identity. A unique full
            // metadata match is safe; duplicate language/codec rows are not.
            if (string.IsNullOrWhiteSpace(requested.Codec) || string.IsNullOrWhiteSpace(requested.Language)) return 0;
            matches = loaded.Where(t => Codec(t.Codec) == Codec(requested.Codec) &&
                Same(t.Language, requested.Language) && t.Forced == requested.Forced &&
                t.HearingImpaired == requested.HearingImpaired &&
                (string.IsNullOrWhiteSpace(requested.Title) || Same(t.Title, requested.Title)));
        }
        var unique = matches.Where(t => t.Id > 0).Take(2).ToArray();
        return unique.Length == 1 ? unique[0].Id : 0;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Codec(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "srt" => "subrip",
        "ssa" => "ass",
        "vtt" => "webvtt",
        "pgs" or "sup" => "hdmv_pgs_subtitle",
        var codec => codec ?? ""
    };
}
