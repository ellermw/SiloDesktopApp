using SiloPlayer.Core.Services;

namespace SiloPlayer.Player;

public sealed record NativeSubtitleTrack(int Id, int? StreamIndex, string? ContainerTrackId,
    string? Codec, string? Language, string? Title, bool Forced = false, bool HearingImpaired = false);

/// <summary>Matches server identity to loaded subtitle IDs; combined ordinals are never native IDs.</summary>
public static class NativeSubtitleSelection
{
    public static int Resolve(NativeSubtitleTrack requested, IReadOnlyList<NativeSubtitleTrack> loaded,
        bool trustworthyStreamIndex, bool trustworthyContainerTrackId = true)
    {
        IEnumerable<NativeSubtitleTrack> matches;
        if (trustworthyStreamIndex && requested.StreamIndex is not null)
        {
            // lavf's src-id is not necessarily a container TrackNumber. Only
            // cross-check it when the caller can attest that namespace too.
            matches = loaded.Where(t => t.StreamIndex == requested.StreamIndex &&
                (!trustworthyContainerTrackId || string.IsNullOrWhiteSpace(requested.ContainerTrackId) ||
                 string.IsNullOrWhiteSpace(t.ContainerTrackId) || t.ContainerTrackId == requested.ContainerTrackId));
        }
        else if (trustworthyContainerTrackId && !string.IsNullOrWhiteSpace(requested.ContainerTrackId))
        {
            matches = loaded.Where(t => t.ContainerTrackId == requested.ContainerTrackId);
        }
        else
        {
            // Absent or unattested absolute identity cannot select a track,
            // but must not suppress a unique full metadata match. Duplicate
            // language/codec rows are not safe.
            if (string.IsNullOrWhiteSpace(requested.Codec)) return 0;
            var metadata = loaded.Where(t => t.Id > 0 && Codec(t.Codec) == Codec(requested.Codec) &&
                LanguageIdentity(t.Language) == LanguageIdentity(requested.Language) &&
                t.Forced == requested.Forced && t.HearingImpaired == requested.HearingImpaired).ToArray();
            // A display label need not be the container title. Use a supplied
            // raw label only to disambiguate otherwise identical metadata.
            matches = metadata.Length > 1 && !string.IsNullOrWhiteSpace(requested.Title)
                ? metadata.Where(t => !string.IsNullOrWhiteSpace(t.Title) && Same(t.Title, requested.Title))
                : metadata;
        }
        var unique = matches.Where(t => t.Id > 0).Take(2).ToArray();
        return unique.Length == 1 ? unique[0].Id : 0;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string LanguageIdentity(string? value)
    {
        // Native metadata identity retains explicit BCP47 region/script subtags.
        // Reuse the catalog's ISO aliases only for the primary language.
        var tag = value?.Trim().Replace('_', '-') ?? "";
        if (tag.Length == 0 || string.Equals(tag, "und", StringComparison.OrdinalIgnoreCase)) return "";
        var separator = tag.IndexOf('-');
        return separator < 0 ? MediaLanguageCatalog.Normalize(tag)
            : MediaLanguageCatalog.Normalize(tag[..separator]) + tag[separator..].ToLowerInvariant();
    }

    private static string Codec(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "srt" => "subrip",
        "ssa" => "ass",
        "vtt" => "webvtt",
        "pgs" or "sup" => "hdmv_pgs_subtitle",
        var codec => codec ?? ""
    };
}
