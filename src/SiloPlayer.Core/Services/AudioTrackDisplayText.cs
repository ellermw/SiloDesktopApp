using System.Text.RegularExpressions;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Models.Playback;

namespace SiloPlayer.Core.Services;

public static partial class AudioTrackDisplayText
{
    // FFprobe can supply its codec's long name as the stream title. Match the
    // player WebUI's titleOnlyNamesAudioFormat vocabulary (mediaFormat.ts).
    private static readonly HashSet<string> FormatWords = new((
        "atsc a/52 a/52a a/52b dca dts dts-hd ma coherent acoustics truehd atmos " +
        "ac3 ac-3 eac3 e-ac3 eac-3 e-ac-3 aac advanced audio coding codec interactive " +
        "flac free lossless alac apple opus vorbis mp2 mp3 mpeg layer pcm signed unsigned " +
        "floating point little-endian big-endian dolby digital plus surround stereo mono " +
        "1.0 2.0 2.1 5.0 5.1 7.0 7.1").Split(' '), StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"[\s()·,]+")]
    private static partial Regex TitleSeparators();

    [GeneratedRegex(@"\A[0-9]{1,3}(?:-bit)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FormatNumber();

    public static string? MenuStreamTitle(AudioTrackInfo track)
    {
        var title = (string.IsNullOrWhiteSpace(track.Title) ? track.EmbeddedTitle : track.Title)?.Trim();
        if (string.IsNullOrEmpty(title)) return null;
        return !string.IsNullOrWhiteSpace(track.Language) && TitleOnlyNamesFormat(title) ? null : title;
    }

    public static bool TitleOnlyNamesFormat(string title)
    {
        var namesFormat = false;
        foreach (var word in TitleSeparators().Split(title))
        {
            if (word.Length == 0) continue;
            if (FormatWords.Contains(word)) namesFormat = true;
            else if (!FormatNumber().IsMatch(word)) return false;
        }
        return namesFormat;
    }

    public static string MenuTitle(AudioTrackInfo track, string languageName, int trackNumber) =>
        MenuStreamTitle(track) ?? (languageName.Length > 0 ? languageName :
            AppStrings.Get("web.player.audioMenu.trackFallback", new Dictionary<string, object?> { ["number"] = trackNumber },
                fallback: $"Track {trackNumber}"));

    /// <summary>Source audio-track bitrates arrive from Silo in kilobits per second; shown in the display culture.</summary>
    public static string FormatBitrate(int? kbps) => kbps is > 0
        ? AppStrings.Get("web.common.format.bitrate.kbps",
            new Dictionary<string, object?> { ["rate"] = kbps.Value.ToString("0.#", AppStrings.FormatCulture) },
            fallback: kbps.Value.ToString("0.#", AppStrings.FormatCulture) + " kbps")
        : "";

    /// <summary>
    /// A media language code's name in the interface language (the catalog's
    /// <c>language.*</c> names), or <paramref name="english"/> when the catalog
    /// has none. Codes stay data; only the displayed name changes.
    /// </summary>
    public static string LanguageName(string? code, string english)
    {
        if (string.IsNullOrWhiteSpace(code)) return english;
        var normalized = MediaLanguageCatalog.Normalize(code);
        return normalized.Length is 2 or 3 && normalized.All(char.IsAsciiLetterLower)
            ? AppStrings.Get("language." + normalized, fallback: english)
            : english;
    }
}
