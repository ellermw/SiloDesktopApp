using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

/// <summary>
/// Port of web/src/player/utils/subtitleSort.ts + subtitleMode.ts.
/// Resolves which subtitle track to auto-select at playback start, given
/// the user's subtitle mode (off/auto/always), preferred language, audio
/// language, and profile language. Used to show "Auto: ENG" feedback in
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

    // Subset of ISO639-1 → ISO639-2/B mapping for cross-format matching.
    // (Most embedded subtitle tracks use 3-letter codes; user prefs use 2-letter.)
    private static readonly Dictionary<string, string> Iso639To3 = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "eng", ["es"] = "spa", ["fr"] = "fre", ["de"] = "ger", ["it"] = "ita",
        ["pt"] = "por", ["nl"] = "dut", ["pl"] = "pol", ["sv"] = "swe", ["no"] = "nor",
        ["da"] = "dan", ["fi"] = "fin", ["ru"] = "rus", ["uk"] = "ukr", ["cs"] = "cze",
        ["sk"] = "slo", ["hu"] = "hun", ["ro"] = "rum", ["bg"] = "bul", ["hr"] = "hrv",
        ["sl"] = "slv", ["sr"] = "srp", ["tr"] = "tur", ["el"] = "gre", ["he"] = "heb",
        ["ar"] = "ara", ["zh"] = "chi", ["ja"] = "jpn", ["ko"] = "kor", ["vi"] = "vie",
        ["th"] = "tha", ["id"] = "ind", ["ms"] = "may", ["hi"] = "hin",
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
        if (string.IsNullOrEmpty(n) || n == OriginalLanguageSentinel) return null;
        return n;
    }

    private static bool SameLanguageCode(string? a, string? b)
    {
        var left = NormalizeConcreteLanguage(a);
        var right = NormalizeConcreteLanguage(b);
        if (left == null || right == null) return false;
        if (left == right) return true;
        var leftMapped = Iso639To3.TryGetValue(left, out var l3) ? l3 : left;
        var rightMapped = Iso639To3.TryGetValue(right, out var r3) ? r3 : right;
        return leftMapped == rightMapped;
    }

    public record SubtitleCandidate(
        int OriginalIndex,
        string? Language,
        string? Source,
        bool Forced);

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
            var priority = SourcePriority.TryGetValue(t.Source ?? "embedded", out var p) ? p : 2;
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
        bool ShowForcedSubtitles);

    /// <summary>
    /// Determine which subtitle track should auto-select on playback start.
    /// Returns the track's original backend index, or null if none should be selected.
    /// </summary>
    public static int? Resolve(Options options)
    {
        if (options.Tracks.Count == 0) return null;

        var preferredSubtitleLang = NormalizeConcreteLanguage(options.PreferredLanguage);
        var rawProfile = Normalize(options.ProfileLanguage);
        var effectiveProfileLang = NormalizeConcreteLanguage(options.ProfileLanguage)
            ?? (rawProfile == OriginalLanguageSentinel ? preferredSubtitleLang
                : rawProfile == "" ? "en"
                : null);
        var effectiveAudioLang = NormalizeConcreteLanguage(options.AudioLanguage) ?? effectiveProfileLang;

        switch (NormalizeSubtitleMode(options.Mode))
        {
            case "off":
                return options.ShowForcedSubtitles
                    ? FindForcedSubtitleIndex(options.Tracks, effectiveAudioLang)
                    : null;

            case "always":
                if (string.IsNullOrEmpty(options.PreferredLanguage)) return null;
                var matchAlways = FindPreferredSubtitleIndex(options.Tracks, options.PreferredLanguage);
                return matchAlways >= 0 ? matchAlways : null;

            case "auto":
                if (options.PreferredLanguage == "") return null;
                if (effectiveProfileLang != null &&
                    SameLanguageCode(effectiveAudioLang, effectiveProfileLang))
                {
                    return options.ShowForcedSubtitles
                        ? FindForcedSubtitleIndex(options.Tracks, effectiveAudioLang)
                        : null;
                }
                var lang = preferredSubtitleLang ?? effectiveProfileLang;
                if (lang == null)
                {
                    return options.ShowForcedSubtitles
                        ? FindForcedSubtitleIndex(options.Tracks, effectiveAudioLang)
                        : null;
                }
                var matchAuto = FindPreferredSubtitleIndex(options.Tracks, lang);
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
                Forced: t.Forced == true));
            i++;
        }
        return result;
    }
}
