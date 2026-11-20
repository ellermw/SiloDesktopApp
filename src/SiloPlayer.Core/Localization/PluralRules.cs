namespace SiloPlayer.Core.Localization;

/// <summary>
/// CLDR cardinal plural categories for the shipped catalog languages, using the
/// CLDR operands n (absolute value), i (integer digits), v (visible fraction
/// digit count) and f/t (fraction digits). Insignificant trailing zeros are
/// removed, as for the WebUI's JavaScript numbers. A language without rules here,
/// or a number those rules do not name, is "other", as in CLDR's root.
/// </summary>
public static class PluralRules
{
    public static string Category(string language, decimal value)
    {
        var n = Math.Abs(value);
        var i = decimal.Truncate(n);
        var integer = n == i;
        var baseLanguage = language.Split('-')[0].ToLowerInvariant();
        switch (baseLanguage)
        {
            case "en":
            case "de":
            case "nl":
            case "sv":
            case "nb":
            case "fi":
            case "tr":
                return i == 1 && integer ? "one" : "other";
            case "it":
                if (i == 1 && integer) return "one";
                return IsMillionMany(i, integer) ? "many" : "other";
            case "da":
                return n == 1 || (!integer && i is 0 or 1) ? "one" : "other";
            case "pl":
                if (!integer) return "other";
                if (i == 1) return "one";
                if (i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14)) return "few";
                return "many";
            case "ru":
            case "uk":
                if (!integer) return "other";
                if (i % 10 == 1 && i % 100 != 11) return "one";
                if (i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14)) return "few";
                return "many";
            case "cs":
                if (!integer) return "many";
                if (i == 1) return "one";
                return i is >= 2 and <= 4 ? "few" : "other";
            case "hr":
                var digits = integer ? i : FractionDigits(n - i);
                if (digits % 10 == 1 && digits % 100 != 11) return "one";
                return digits % 10 is >= 2 and <= 4 && digits % 100 is not (>= 12 and <= 14) ? "few" : "other";
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
                // ja, ko, zh and any language without rules.
                return "other";
        }
    }

    // CLDR "many" for es/fr/it/pt: e = 0 and i != 0 and i % 1000000 = 0 and v = 0
    // (the compact-exponent branch never applies to plain numbers).
    private static bool IsMillionMany(decimal i, bool integer) => integer && i != 0 && i % 1_000_000 == 0;

    private static decimal FractionDigits(decimal fraction)
    {
        while (fraction != decimal.Truncate(fraction)) fraction *= 10;
        return fraction;
    }
}
