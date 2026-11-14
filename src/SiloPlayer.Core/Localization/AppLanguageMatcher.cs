using System.Globalization;

namespace SiloPlayer.Core.Localization;

/// <summary>
/// Language-tag canonicalization and the shared client matching rule
/// (docs/architecture/i18n.md "Matching"): a regional tag matches its
/// language (<c>es-MX</c> → <c>es</c>, <c>pt</c>/<c>pt-PT</c> → <c>pt-BR</c>),
/// and Chinese matches by script (<c>zh</c>, <c>zh-CN</c>, <c>zh-SG</c> →
/// <c>zh-Hans</c>; <c>zh-TW</c>, <c>zh-HK</c>, <c>zh-Hant</c> → none). Tags are
/// compared after filling the likely script of each shipped language, as the
/// web compares maximized tags.
/// </summary>
public static class AppLanguageMatcher
{
    // Names the web's canonicalLanguageTag accepts for stored values.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["arabic"] = "ar",
        ["english"] = "en",
        ["spanish"] = "es",
        ["french"] = "fr",
        ["german"] = "de",
        ["portuguese"] = "pt",
        ["brazilian portuguese"] = "pt-BR",
        ["portuguese (brazil)"] = "pt-BR",
        ["chinese (traditional)"] = "zh-Hant",
        ["traditional chinese"] = "zh-Hant",
    };

    // Deprecated ISO 639 codes BCP 47 canonicalization replaces.
    private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.Ordinal)
    {
        ["iw"] = "he", ["in"] = "id", ["ji"] = "yi", ["jw"] = "jv", ["mo"] = "ro",
    };

    // CLDR likely scripts for the languages the catalogs ship (and Chinese regions).
    private static readonly Dictionary<string, string> LikelyScripts = new(StringComparer.Ordinal)
    {
        ["en"] = "Latn", ["es"] = "Latn", ["de"] = "Latn", ["fr"] = "Latn", ["pt"] = "Latn",
        ["ja"] = "Jpan", ["ar"] = "Arab", ["zh"] = "Hans",
    };

    private static readonly HashSet<string> TraditionalChineseRegions = new(StringComparer.Ordinal) { "TW", "HK", "MO" };

    /// <summary>
    /// The canonical form of a language tag, or null when it is not one (an
    /// empty value, or a primary subtag that is not 2–3 letters). Only for
    /// comparison and display; stored values are never rewritten by reading.
    /// </summary>
    public static string? Canonicalize(string? value)
    {
        if (value == null) return null;
        var trimmed = value.Trim();
        if (Aliases.TryGetValue(trimmed, out var alias)) trimmed = alias;
        if (trimmed.Length == 0) return null;
        var parts = trimmed.Replace('_', '-').Split('-');
        if (parts.Any(part => part.Length is 0 or > 8 || !part.All(char.IsAsciiLetterOrDigit))) return null;
        var language = parts[0].ToLowerInvariant();
        if (language.Length is < 2 or > 3 || !language.All(char.IsAsciiLetter)) return null;
        if (LanguageAliases.TryGetValue(language, out var replacement)) language = replacement;
        var result = new List<string> { language };
        var index = 1;
        if (index < parts.Length && parts[index].Length == 4 && parts[index].All(char.IsAsciiLetter))
            result.Add(char.ToUpperInvariant(parts[index][0]) + parts[index++][1..].ToLowerInvariant());
        if (index < parts.Length && (parts[index].Length == 2 && parts[index].All(char.IsAsciiLetter)
                                     || parts[index].Length == 3 && parts[index].All(char.IsAsciiDigit)))
            result.Add(parts[index++].ToUpperInvariant());
        for (; index < parts.Length; index++) result.Add(parts[index].ToLowerInvariant());
        return string.Join('-', result);
    }

    /// <summary>The shipped language a tag selects, or null.</summary>
    public static string? Match(string? tag, IEnumerable<string> shipped)
    {
        var wanted = Maximize(Canonicalize(tag));
        if (wanted == null) return null;
        foreach (var candidate in shipped)
        {
            var shippedTag = Maximize(Canonicalize(candidate));
            if (shippedTag is { } s && s.Language == wanted.Value.Language && s.Script == wanted.Value.Script) return candidate;
        }
        return null;
    }

    /// <summary>The shipped language a tag selects among the desktop's catalogs, or null.</summary>
    public static string? MatchShipped(string? tag) => Match(tag, MessageCatalog.ShippedLanguages.Select(language => language.Tag));

    /// <summary>The primary language subtag of a tag, or "" when it is not a tag.</summary>
    public static string LanguageOf(string? tag) => Canonicalize(tag)?.Split('-')[0] ?? "";

    private static (string Language, string Script)? Maximize(string? canonical)
    {
        if (canonical == null) return null;
        var parts = canonical.Split('-');
        var language = parts[0];
        string? script = parts.Length > 1 && parts[1].Length == 4 && char.IsAsciiLetterUpper(parts[1][0]) ? parts[1] : null;
        var region = parts.Skip(script == null ? 1 : 2).FirstOrDefault(part => part.Length == 2 && part.All(char.IsAsciiLetterUpper)
                                                                               || part.Length == 3 && part.All(char.IsAsciiDigit));
        if (script == null)
        {
            if (language == "zh") script = region != null && TraditionalChineseRegions.Contains(region) ? "Hant" : "Hans";
            else script = LikelyScripts.GetValueOrDefault(language) ?? "";
        }
        return (language, script);
    }

    /// <summary>A culture for a tag when the platform knows it, else null.</summary>
    public static CultureInfo? TryCulture(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        try { return CultureInfo.GetCultureInfo(tag); }
        catch (CultureNotFoundException) { return null; }
    }
}
