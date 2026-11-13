using System.Globalization;

namespace SiloPlayer.Core.Localization;

/// <summary>
/// Native interface text from the shared silo-translations catalog.
///
/// Callers pass explicit catalog keys; never translate API payloads, saved
/// user text, identifiers or values that are stored, sent or compared (use
/// <see cref="English"/> for those). Text is fixed in the language of the
/// moment it is produced: a retained view re-reads its text when
/// <see cref="LocaleService.Changed"/> fires (natively, through
/// <c>SiloPlayer.Services.AppLocalization.Register</c>).
/// </summary>
public static class AppStrings
{
    private static readonly AsyncLocal<AppLocale?> Override = new();

    /// <summary>The applied interface locale.</summary>
    public static AppLocale Current => Override.Value ?? LocaleService.Shared.Current;

    /// <summary>
    /// Interface text for <paramref name="key"/> in the current language, filled
    /// from <paramref name="arguments"/> (ICU subset: placeholders, cardinal
    /// plurals and selects). A language without the message uses English; a
    /// message neither has, or whose arguments are missing, returns
    /// <paramref name="fallback"/>, or the key when no fallback is given.
    /// </summary>
    public static string Get(string key, IReadOnlyDictionary<string, object?>? arguments = null, string? fallback = null)
        => Current.Get(key, arguments, fallback);

    /// <summary>
    /// Server-written English (<paramref name="serverText"/>) shown through its
    /// optional catalog key: translated only when the interface language's own
    /// catalog has the key and the arguments fill it; otherwise exactly as sent.
    /// </summary>
    public static string ServerText(string? key, IReadOnlyDictionary<string, string>? arguments, string serverText)
        => Current.ServerText(key, arguments, serverText);

    /// <summary>A catalog message in English whatever the interface language, for text that is data.</summary>
    public static string English(string key, IReadOnlyDictionary<string, object?>? arguments = null, string? fallback = null)
        => Current.English(key, arguments, fallback);

    /// <summary>Culture for displayed dates and numbers. Never use it for wire values or parsing.</summary>
    public static CultureInfo FormatCulture => Current.FormatCulture;

    /// <summary>Uppercases interface text using its language, independently of regional date and number formats.</summary>
    public static string Uppercase(string text) => text.ToUpper(CultureInfo.GetCultureInfo(Language));

    public static bool IsRightToLeft => Current.IsRightToLeft;

    /// <summary>The interface language tag (one of <see cref="MessageCatalog.ShippedLanguages"/>).</summary>
    public static string Language => Current.Language;

    /// <summary>Formats with an explicit locale for the current async flow only (tests and isolated fixtures).</summary>
    internal static IDisposable UseForCurrentFlow(AppLocale locale)
    {
        var previous = Override.Value;
        Override.Value = locale;
        return new Restore(previous);
    }

    private sealed class Restore(AppLocale? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}
