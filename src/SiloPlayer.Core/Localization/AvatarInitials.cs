using System.Globalization;

namespace SiloPlayer.Core.Localization;

/// <summary>
/// Initials for an avatar without an image, as the official web client draws them
/// (web/src/lib/text.ts, silo-server #4998 and #5027): whole characters as a reader
/// sees them (an emoji, a flag or a letter with its accents is never cut in half),
/// upper-cased by the interface language's rules (Turkish makes "i" into "İ").
/// </summary>
public static class AvatarInitials
{
    /// <summary>The first <paramref name="count"/> characters of <paramref name="text"/>, upper-cased for the interface language.</summary>
    public static string Leading(string? text, int count, string? languageTag = null)
    {
        if (string.IsNullOrEmpty(text) || count <= 0) return "";
        var elements = StringInfo.GetTextElementEnumerator(text);
        var taken = 0;
        var result = new System.Text.StringBuilder();
        while (taken < count && elements.MoveNext()) { result.Append(elements.GetTextElement()); taken++; }
        var culture = AppLanguageMatcher.TryCulture(languageTag ?? LocaleService.Shared.Current.Language) ?? CultureInfo.InvariantCulture;
        return result.ToString().ToUpper(culture);
    }

    /// <summary>The name's first character, upper-cased, or <paramref name="fallback"/> when it has none.</summary>
    public static string One(string? name, string fallback = "?", string? languageTag = null)
        => Leading(name?.Trim(), 1, languageTag) is { Length: > 0 } initial ? initial : fallback;

    /// <summary>The first character of each of the given words, upper-cased, or <paramref name="fallback"/> when there is none.</summary>
    public static string OfWords(IEnumerable<string?> words, string fallback = "?", string? languageTag = null)
        => string.Concat(words.Select(word => Leading(word?.Trim(), 1, languageTag))) is { Length: > 0 } initials ? initials : fallback;
}
