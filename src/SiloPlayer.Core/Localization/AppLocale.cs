using System.Globalization;

namespace SiloPlayer.Core.Localization;

/// <summary>
/// The inputs and outcome of choosing the interface language
/// (docs/architecture/i18n.md "Precedence and catalog gating"):
/// 1. the profile's effective <c>ui.app_language</c> when it matches a shipped language;
/// 2. the first device language that matches one;
/// 3. English.
/// </summary>
public sealed record AppLocaleResolution(
    string Language,
    string? StoredTag,
    string? MatchedStoredLanguage,
    string FormatCultureName,
    bool LocalizedFormats,
    bool IsRightToLeft)
{
    /// <summary>A stored value this client does not ship: kept as stored, shown by name, and resolved as if null.</summary>
    public bool StoredTagUnsupported => StoredTag != null && MatchedStoredLanguage == null;

    public static AppLocaleResolution Resolve(string? storedTag, IEnumerable<string> deviceLanguages, string deviceFormatCulture)
        => Resolve(storedTag, deviceLanguages, deviceFormatCulture, MessageCatalog.ShippedLanguages);

    public static AppLocaleResolution Resolve(string? storedTag, IEnumerable<string> deviceLanguages, string deviceFormatCulture,
        IReadOnlyList<AppLanguageInfo> shipped)
    {
        var tags = shipped.Select(language => language.Tag).ToArray();
        var stored = string.IsNullOrWhiteSpace(storedTag) ? null : storedTag;
        var storedCanonical = AppLanguageMatcher.Canonicalize(stored);
        var chosen = AppLanguageMatcher.Match(storedCanonical, tags);
        var fromDevice = deviceLanguages.Select(tag => AppLanguageMatcher.Match(tag, tags)).FirstOrDefault(match => match != null);
        var language = chosen ?? fromDevice ?? "en";
        var runtimeLanguage = AppLanguageMatcher.Match(deviceFormatCulture, tags);
        // Formats keep the device's own locale unless the person chose a language
        // the device's locale does not speak: German chosen on an English device
        // gets German dates; English chosen on an en-GB device keeps British dates.
        var formatFromStored = chosen != null && runtimeLanguage != chosen && AppLanguageMatcher.TryCulture(storedCanonical) != null;
        var formatCulture = formatFromStored ? storedCanonical! : deviceFormatCulture;
        var localizedFormats = language != "en" && (formatFromStored || runtimeLanguage == language);
        var rtl = shipped.FirstOrDefault(item => item.Tag == language)?.IsRightToLeft ?? false;
        return new AppLocaleResolution(language, stored, chosen, formatCulture, localizedFormats, rtl);
    }
}

/// <summary>
/// One immutable interface locale: its catalog (with English beneath it),
/// direction and display culture. A new locale replaces the whole snapshot,
/// so a caller formatting text never sees a half-applied language.
/// </summary>
public sealed class AppLocale
{
    private readonly MessageCatalog? _catalog;
    private readonly MessageCatalog? _english;
    private readonly MessageFormatContext _context;
    private readonly MessageFormatContext _englishContext;

    internal AppLocale(AppLocaleResolution resolution, MessageCatalog? catalog, MessageCatalog? english, long generation)
    {
        Resolution = resolution;
        _catalog = catalog;
        _english = english;
        Generation = generation;
        Language = catalog?.Language ?? "en";
        IsRightToLeft = catalog != null && resolution.IsRightToLeft;
        // The device's own culture object keeps the person's Windows format overrides
        // (custom date patterns); a cached culture by name would drop them.
        var device = CultureInfo.CurrentCulture;
        FormatCulture = string.Equals(device.Name, resolution.FormatCultureName, StringComparison.OrdinalIgnoreCase)
            ? device
            : AppLanguageMatcher.TryCulture(resolution.FormatCultureName) ?? CultureInfo.InvariantCulture;
        LocalizedFormats = catalog != null && resolution.LocalizedFormats;
        _context = new(Language, NumberFormatFor(Language), FormatCulture, IsRightToLeft);
        _englishContext = new("en", NumberFormatFor("en"), FormatCulture, IsRightToLeft);
    }

    /// <summary>The shipped language the interface uses ("en" when its catalog could not load).</summary>
    public string Language { get; }

    public bool IsRightToLeft { get; }

    /// <summary>Culture for dates and numbers shown in the interface (never for wire values).</summary>
    public CultureInfo FormatCulture { get; }

    /// <summary>
    /// Whether dates come out in a non-English interface language, so a format
    /// that pins an English regional locale should follow <see cref="FormatCulture"/>.
    /// </summary>
    public bool LocalizedFormats { get; }

    public AppLocaleResolution Resolution { get; }

    /// <summary>Increases with every applied locale; retained views compare it to skip redundant refreshes.</summary>
    public long Generation { get; }

    public bool IsEnglish => Language == "en";

    /// <summary>
    /// Interface text for <paramref name="key"/>. Uses the language's message,
    /// then English's, filled from <paramref name="arguments"/>; when neither
    /// has the key or the arguments it needs, returns <paramref name="fallback"/>
    /// (or the key itself so a missing message is visible, never a raw pattern).
    /// </summary>
    public string Get(string key, IReadOnlyDictionary<string, object?>? arguments = null, string? fallback = null)
    {
        if (_catalog != null && _catalog.TryGet(key, out var own) && own.TryFormat(_context, arguments, out var text)) return text!;
        if (_english != null && !ReferenceEquals(_english, _catalog) && _english.TryGet(key, out var english)
            && english.TryFormat(_englishContext, arguments, out text)) return text!;
        return fallback ?? key;
    }

    /// <summary>Whether the interface language's own catalog has <paramref name="key"/> (no English fallback).</summary>
    public bool HasTranslation(string key) => _catalog != null && !IsEnglish && _catalog.Contains(key);

    /// <summary>
    /// Text the server sent in English, translated when the interface language's
    /// own catalog has <paramref name="key"/> (a <c>title_key</c>, an
    /// <c>error.*</c> key) and <paramref name="arguments"/> fill every
    /// placeholder it uses. Otherwise, and always in English, the server's text
    /// exactly as sent.
    /// </summary>
    public string ServerText(string? key, IReadOnlyDictionary<string, string>? arguments, string serverText)
    {
        if (string.IsNullOrEmpty(key) || IsEnglish || _catalog == null || !_catalog.TryGet(key, out var pattern)) return serverText;
        foreach (var name in pattern.PrintedArguments.Concat(pattern.SelectorArguments))
            if (arguments == null || !arguments.ContainsKey(name)) return serverText;
        var values = arguments?.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
        return pattern.TryFormat(_context, values, out var text) ? text! : serverText;
    }

    /// <summary>English text, whatever the interface language, for text that is data (stored, sent or compared).</summary>
    public string English(string key, IReadOnlyDictionary<string, object?>? arguments = null, string? fallback = null)
    {
        var englishContext = new MessageFormatContext("en", NumberFormatFor("en"), CultureInfo.InvariantCulture, false);
        return _english != null && _english.TryGet(key, out var pattern) && pattern.TryFormat(englishContext, arguments, out var text)
            ? text!
            : fallback ?? key;
    }

    // Arabic shows Latin digits here, as the web's default (ar-u-nu-latn) does;
    // its grouping then follows ICU's Latin symbols rather than Arabic separators.
    internal static NumberFormatInfo NumberFormatFor(string language)
        => language == "ar"
            ? CultureInfo.InvariantCulture.NumberFormat
            : (AppLanguageMatcher.TryCulture(language) ?? CultureInfo.InvariantCulture).NumberFormat;
}
