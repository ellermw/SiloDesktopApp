using System.Globalization;

namespace SiloPlayer.Core.Localization;

/// <summary>
/// CLDR cardinal plural categories for the shipped catalog languages, using the
/// CLDR operands n (absolute value), i (integer digits) and v (visible fraction
/// digit count). A language without rules here, or a number those rules do not
/// name, is "other", as in CLDR's root.
/// </summary>
public static class PluralRules
{
    public static string Category(string language, decimal value)
    {
        var n = Math.Abs(value);
        var i = decimal.Truncate(n);
        var v = VisibleFractionDigits(n);
        var integer = v == 0;
        var baseLanguage = language.Split('-')[0].ToLowerInvariant();
        switch (baseLanguage)
        {
            case "en":
            case "de":
                return i == 1 && integer ? "one" : "other";
            case "es":
                if (n == 1) return "one";
                return IsMillionMany(i, integer) ? "many" : "other";
            case "fr":
                if (i is 0 or 1) return "one";
                return IsMillionMany(i, integer) ? "many" : "other";
            case "pt":
                // CLDR "pt" (Brazilian): one is i = 0..1; pt-PT uses different rules, but the
                // shipped catalog is pt-BR.
                if (i is 0 or 1) return "one";
                return IsMillionMany(i, integer) ? "many" : "other";
            case "ar":
                if (!integer) return "other";
                if (n == 0) return "zero";
                if (n == 1) return "one";
                if (n == 2) return "two";
                var hundreds = n % 100;
                if (hundreds is >= 3 and <= 10) return "few";
                if (hundreds is >= 11 and <= 99) return "many";
                return "other";
            default:
                // ja, zh and any language without rules.
                return "other";
        }
    }

    // CLDR "many" for es/fr/pt: e = 0 and i != 0 and i % 1000000 = 0 and v = 0
    // (the compact-exponent branch never applies to plain numbers).
    private static bool IsMillionMany(decimal i, bool integer) => integer && i != 0 && i % 1_000_000 == 0;

    private static int VisibleFractionDigits(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        var point = text.IndexOf('.');
        return point < 0 ? 0 : text.Length - point - 1;
    }
}
