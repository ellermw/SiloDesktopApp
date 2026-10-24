using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Port of web/src/player/utils/subtitleSort.ts + subtitleMode.ts.
/// Resolves which subtitle track to auto-select at playback start, given
/// the user's subtitle mode (off/auto/always), preferred language, audio
/// language. The profile display language never stands in for unknown audio.
/// Used to show "Auto: ENG" feedback in
/// the pre-play subtitles popover before playback actually begins.
/// </summary>
public static class SubtitleAutoSelect
{
    private const string OriginalLanguageSentinel = "original";

    private static readonly Dictionary<string, int> SourcePriority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["external"] = 0,
        ["downloaded"] = 1,
        ["embedded"] = 2,
    };

    public static string NormalizeSubtitleMode(string? mode) => mode switch
    {
        "off" or "auto" or "always" => mode,
        _ => "auto",
    };

    private static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();

    private static string? NormalizeConcreteLanguage(string? value)
    {
        var n = Normalize(value);
        if (string.IsNullOrEmpty(n) || n is OriginalLanguageSentinel or "x-silo-original" or "und") return null;
        return n;
    }

    private static bool SameLanguageCode(string? a, string? b)
    {
        var left = NormalizeConcreteLanguage(a);
        var right = NormalizeConcreteLanguage(b);
        if (left == null || right == null) return false;
        if (left == right) return true;
        return MediaLanguageCatalog.Normalize(left) == MediaLanguageCatalog.Normalize(right);
    }

    public record SubtitleCandidate(
        int OriginalIndex,
        string? Language,
        string? Source,
        bool Forced,
        string? Codec = null,
        string? Label = null,
        bool HearingImpaired = false);

    private static bool IsBitmapCodec(string? codec)
        => Normalize(codec) is "pgs" or "hdmv_pgs_subtitle" or "dvdsub" or
            "dvd_subtitle" or "vobsub" or "dvbsub" or "dvb_subtitle";

    private static int TrackPriority(SubtitleCandidate track)
    {
        var source = SourcePriority.TryGetValue(track.Source ?? "embedded", out var priority)
            ? priority
            : 2;
        return source * 2 + (IsBitmapCodec(track.Codec) ? 1 : 0);
    }

    private static bool MatchesSignature(SubtitleCandidate track, SubtitleTrackSignature? signature)
    {
        if (signature == null) return false;
        return Normalize(track.Source) == Normalize(signature.Source) &&
            Normalize(track.Language) == Normalize(signature.Language) &&
            Normalize(track.Codec) == Normalize(signature.Codec) &&
            (Normalize(signature.Label) == "" || Normalize(track.Label) == Normalize(signature.Label)) &&
            track.Forced == signature.Forced &&
            track.HearingImpaired == signature.HearingImpaired;
    }

    private static int SignatureFallbackScore(SubtitleCandidate track, SubtitleTrackSignature? signature)
    {
        if (signature == null) return 0;
        var score = 0;
        if (Normalize(track.Source) == Normalize(signature.Source)) score += 4;
        if (track.Forced == signature.Forced) score += 2;
        if (track.HearingImpaired == signature.HearingImpaired) score += 2;
        if (Normalize(track.Codec) == Normalize(signature.Codec)) score += 1;
        if (Normalize(track.Label) == Normalize(signature.Label)) score += 1;
        return score;
    }

    /// <summary>
    /// Among candidates matching <paramref name="language"/>, return the one with
    /// the best source priority (external &gt; downloaded &gt; embedded). Returns
    /// -1 if nothing matches.
    /// </summary>
    public static int FindPreferredSubtitleIndex(
        IReadOnlyList<SubtitleCandidate> tracks, string language)
    {
        var bestIdx = -1;
        var bestPriority = int.MaxValue;
        foreach (var t in tracks)
        {
            if (!SameLanguageCode(t.Language, language)) continue;
            var priority = TrackPriority(t);
            if (priority < bestPriority)
            {
                bestPriority = priority;
                bestIdx = t.OriginalIndex;
            }
        }
        return bestIdx;
    }

    private static int? FindForcedSubtitleIndex(
        IReadOnlyList<SubtitleCandidate> tracks, string? language)
    {
        if (string.IsNullOrEmpty(language)) return null;
        var forcedOnly = tracks.Where(t => t.Forced).ToList();
        var match = FindPreferredSubtitleIndex(forcedOnly, language);
        return match >= 0 ? match : null;
    }

    public record Options(
        string Mode,
        IReadOnlyList<SubtitleCandidate> Tracks,
        string? PreferredLanguage,
        string? AudioLanguage,
        string? ProfileLanguage,
        bool ShowForcedSubtitles,
        SubtitleTrackSignature? PreferredTrackSignature = null);

    private static int FindPreferredSubtitleIndexWithSignature(
        IReadOnlyList<SubtitleCandidate> tracks,
        string language,
        SubtitleTrackSignature? signature)
    {
        SubtitleCandidate? best = null;
        var bestScore = -1;
        var bestPriority = int.MaxValue;
        foreach (var track in tracks)
        {
            if (!SameLanguageCode(track.Language, language)) continue;
            var score = SignatureFallbackScore(track, signature);
            var priority = TrackPriority(track);
            if (best == null || score > bestScore || (score == bestScore && priority < bestPriority))
            {
                best = track;
                bestScore = score;
                bestPriority = priority;
            }
        }
        return best?.OriginalIndex ?? -1;
    }

    /// <summary>
    /// Determine which subtitle track should auto-select on playback start.
    /// Returns the track's original backend index, or null if none should be selected.
    /// </summary>
    public static int? Resolve(Options options)
    {
        if (options.Tracks.Count == 0) return null;

        var preferredSubtitleLang = NormalizeConcreteLanguage(options.PreferredLanguage);
        var effectiveAudioLang = NormalizeConcreteLanguage(options.AudioLanguage);

        switch (NormalizeSubtitleMode(options.Mode))
        {
            case "off":
                return options.ShowForcedSubtitles
                    ? FindForcedSubtitleIndex(options.Tracks, effectiveAudioLang)
                    : null;

            case "always":
                var exact = options.Tracks.FirstOrDefault(t => MatchesSignature(t, options.PreferredTrackSignature));
                if (exact != null) return exact.OriginalIndex;
                if (string.IsNullOrEmpty(options.PreferredLanguage)) return null;
                var matchAlways = FindPreferredSubtitleIndexWithSignature(
                    options.Tracks, options.PreferredLanguage, options.PreferredTrackSignature);
                return matchAlways >= 0 ? matchAlways : null;

            case "auto":
                if (options.PreferredLanguage == "") return null;
                if (preferredSubtitleLang == null || SameLanguageCode(effectiveAudioLang, preferredSubtitleLang))
                {
                    return options.ShowForcedSubtitles
                        ? FindForcedSubtitleIndex(options.Tracks, effectiveAudioLang)
                        : null;
                }
                var matchAuto = FindPreferredSubtitleIndexWithSignature(
                    options.Tracks, preferredSubtitleLang, options.PreferredTrackSignature);
                return matchAuto >= 0 ? matchAuto : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Convenience: build <see cref="SubtitleCandidate"/> list from a version's
    /// embedded subtitle tracks. <paramref name="fallbackIndex"/> is used when
    /// the track doesn't carry its own Index (rare, but some older responses).
    /// </summary>
    public static List<SubtitleCandidate> BuildCandidates(IEnumerable<VersionSubtitleTrack>? tracks)
    {
        var result = new List<SubtitleCandidate>();
        if (tracks == null) return result;
        var i = 0;
        foreach (var t in tracks)
        {
            var source = t.External == true ? "external" : "embedded";
            result.Add(new SubtitleCandidate(
                OriginalIndex: t.Index ?? i,
                Language: t.Language,
                Source: source,
                Forced: t.Forced == true,
                Codec: t.Codec,
                Label: t.Title ?? t.EmbeddedTitle,
                HearingImpaired: t.HearingImpaired == true));
            i++;
        }
        return result;
    }

    public static List<SubtitleCandidate> BuildCandidates(IEnumerable<SubtitleTrackInfo>? tracks)
    {
        if (tracks == null) return [];
        return tracks.Select(t => new SubtitleCandidate(
            OriginalIndex: t.Index,
            Language: t.Language,
            Source: t.Source,
            Forced: t.Forced,
            Codec: t.Codec,
            Label: t.Label,
            HearingImpaired: t.HearingImpaired)).ToList();
    }
}
