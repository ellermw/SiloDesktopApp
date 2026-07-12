using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Port of web/src/pages/ItemDetail/components/versionRankingUtils.ts.
/// Ranks file versions by resolution / HDR / audio codec quality and picks a
/// default accounting for last-watched file and quality preference cap.
/// </summary>
public static class VersionRanking
{
    private static readonly Dictionary<string, int> ResolutionRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["4k"] = 4, ["2160p"] = 4, ["1440p"] = 3, ["1080p"] = 2, ["720p"] = 1, ["480p"] = 0,
    };

    // Matched via .Contains(); order the entries so longer keys win over shorter substrings.
    private static readonly (string Key, int Score)[] AudioRank =
    [
        ("atmos", 6), ("truehd", 5), ("dts-hd", 4), ("dts:x", 4),
        ("dts", 3), ("flac", 2), ("eac3", 1), ("e-ac-3", 1), ("aac", 0),
    ];

    public static string MapAudioLabel(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec)) return "";
        var lower = codec.ToLowerInvariant();
        if (lower.Contains("atmos")) return "Atmos";
        if (lower.Contains("truehd")) return "TrueHD";
        if (lower.Contains("dts-hd") || lower.Contains("dts:x")) return "DTS-HD";
        if (lower.Contains("dts")) return "DTS";
        if (lower.Contains("eac3") || lower.Contains("e-ac-3")) return "EAC3";
        if (lower.Contains("aac")) return "AAC";
        if (lower.Contains("flac")) return "FLAC";
        return codec.ToUpperInvariant();
    }

    public static int ResolutionScore(string? res)
    {
        if (string.IsNullOrWhiteSpace(res)) return -1;
        return ResolutionRank.TryGetValue(res, out var v) ? v : -1;
    }

    public static int AudioScore(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec)) return -1;
        var lower = codec.ToLowerInvariant();
        foreach (var (key, score) in AudioRank)
            if (lower.Contains(key)) return score;
        return -1;
    }

    public record BestAttributes(string Resolution, bool Hdr, string AudioLabel);

    /// <summary>
    /// Find the best resolution + HDR + audio-codec combo across a set of versions.
    /// Respects a quality-preference cap (e.g. "1080p" → ignore 4K versions).
    /// </summary>
    public static BestAttributes? PickBestAttributes(
        IReadOnlyList<FileVersion> versions,
        string? qualityPreference)
    {
        if (versions.Count == 0) return null;

        IReadOnlyList<FileVersion> candidates = versions;
        if (!string.IsNullOrEmpty(qualityPreference) &&
            !qualityPreference.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            if (ResolutionRank.TryGetValue(qualityPreference, out var maxRank))
            {
                var filtered = versions.Where(v => ResolutionScore(v.Resolution) <= maxRank).ToList();
                if (filtered.Count > 0) candidates = filtered;
            }
        }

        var bestRes = "";
        var bestResScore = -1;
        var hdr = false;
        var bestAudioCodec = "";
        var bestAudioScore = -1;

        foreach (var v in candidates)
        {
            var rs = ResolutionScore(v.Resolution);
            if (rs > bestResScore) { bestResScore = rs; bestRes = v.Resolution; }
            if (v.Hdr) hdr = true;

            var aScore = AudioScore(v.CodecAudio);
            if (aScore > bestAudioScore) { bestAudioScore = aScore; bestAudioCodec = v.CodecAudio; }

            if (v.AudioTracks != null)
            {
                foreach (var t in v.AudioTracks)
                {
                    if (string.IsNullOrEmpty(t.Codec)) continue;
                    var ts = AudioScore(t.Codec);
                    if (ts > bestAudioScore) { bestAudioScore = ts; bestAudioCodec = t.Codec; }
                }
            }
        }

        return new BestAttributes(bestRes, hdr, MapAudioLabel(bestAudioCodec));
    }

    /// <summary>
    /// Pick the default version: prefer last-watched file, else best resolution + HDR
    /// + audio match (respecting quality preference), fallback to highest resolution.
    /// </summary>
    public static FileVersion? SelectDefaultVersion(
        IReadOnlyList<FileVersion> versions,
        WatchUserData? userData,
        string? qualityPreference)
    {
        if (versions.Count == 0) return null;

        // 1. Last-watched file wins (so "Play" picks the version the user left off in).
        if (userData?.LastFileId != null)
        {
            var match = versions.FirstOrDefault(v => v.FileId == userData.LastFileId.Value);
            if (match != null) return match;
        }

        if (versions.Count == 1) return versions[0];

        // 2. Pick version matching best (resolution, HDR, audio) combo.
        var best = PickBestAttributes(versions, qualityPreference);
        if (best != null)
        {
            var match = versions.FirstOrDefault(v =>
                string.Equals(v.Resolution, best.Resolution, StringComparison.OrdinalIgnoreCase) &&
                v.Hdr == best.Hdr &&
                (best.AudioLabel.Length == 0 ||
                 MapAudioLabel(v.CodecAudio).Equals(best.AudioLabel, StringComparison.OrdinalIgnoreCase)));
            if (match != null) return match;
        }

        // 3. Fallback: highest resolution.
        return versions.OrderByDescending(v => ResolutionScore(v.Resolution)).First();
    }
}
