namespace SiloPlayer.Core.Services;

/// <summary>
/// The canonical ISO 639-1 language choices exposed by the current Silo WebUI.
/// Keep this list aligned with web/src/player/utils/languageNames.ts.
/// </summary>
public static class MediaLanguageCatalog
{
    public sealed record Language(string Code, string Label);

    public static IReadOnlyList<Language> All { get; } =
    [
        new("ar", "Arabic"),
        new("bn", "Bengali"),
        new("bg", "Bulgarian"),
        new("zh", "Chinese"),
        new("hr", "Croatian"),
        new("cs", "Czech"),
        new("da", "Danish"),
        new("nl", "Dutch"),
        new("en", "English"),
        new("fi", "Finnish"),
        new("fr", "French"),
        new("de", "German"),
        new("el", "Greek"),
        new("he", "Hebrew"),
        new("hi", "Hindi"),
        new("hu", "Hungarian"),
        new("id", "Indonesian"),
        new("it", "Italian"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("ms", "Malay"),
        new("no", "Norwegian"),
        new("fa", "Persian"),
        new("pl", "Polish"),
        new("pt", "Portuguese"),
        new("ro", "Romanian"),
        new("ru", "Russian"),
        new("sk", "Slovak"),
        new("sl", "Slovenian"),
        new("es", "Spanish"),
        new("sv", "Swedish"),
        new("ta", "Tamil"),
        new("te", "Telugu"),
        new("th", "Thai"),
        new("tr", "Turkish"),
        new("uk", "Ukrainian"),
        new("vi", "Vietnamese"),
    ];

    public static string Label(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "Unknown";

        var normalized = Normalize(code);
        return All.FirstOrDefault(language => language.Code == normalized)?.Label
            ?? char.ToUpperInvariant(code[0]) + code[1..];
    }

    public static string Normalize(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant() ?? "";
        var separator = normalized.IndexOfAny(['-', '_']);
        if (separator >= 0)
            normalized = normalized[..separator];

        return ThreeLetterAliases.TryGetValue(normalized, out var twoLetter)
            ? twoLetter
            : normalized;
    }

    private static readonly IReadOnlyDictionary<string, string> ThreeLetterAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["eng"] = "en", ["spa"] = "es", ["fre"] = "fr", ["fra"] = "fr",
            ["ger"] = "de", ["deu"] = "de", ["ita"] = "it", ["por"] = "pt",
            ["dut"] = "nl", ["nld"] = "nl", ["pol"] = "pl", ["rus"] = "ru",
            ["chi"] = "zh", ["zho"] = "zh", ["jpn"] = "ja", ["kor"] = "ko",
            ["ara"] = "ar", ["tur"] = "tr", ["swe"] = "sv", ["dan"] = "da",
            ["nor"] = "no", ["fin"] = "fi", ["hun"] = "hu", ["cze"] = "cs",
            ["ces"] = "cs", ["rum"] = "ro", ["ron"] = "ro", ["heb"] = "he",
            ["tha"] = "th", ["vie"] = "vi", ["gre"] = "el", ["ell"] = "el",
            ["bul"] = "bg", ["hrv"] = "hr", ["slo"] = "sk", ["slk"] = "sk",
            ["slv"] = "sl", ["ukr"] = "uk", ["ind"] = "id", ["may"] = "ms",
            ["msa"] = "ms", ["hin"] = "hi", ["tam"] = "ta", ["tel"] = "te",
            ["ben"] = "bn", ["per"] = "fa", ["fas"] = "fa",
        };
}
