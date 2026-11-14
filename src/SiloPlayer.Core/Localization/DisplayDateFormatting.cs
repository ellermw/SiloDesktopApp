using System.Globalization;
using System.Text.RegularExpressions;

namespace SiloPlayer.Core.Localization;

/// <summary>Presentation-only calendar dates; explicit numeric preferences remain invariant.</summary>
public static class DisplayDateFormatting
{
    public static string Date(DateTime value, string? preference, CultureInfo culture, bool localized, bool medium = false)
    {
        if (preference == "YYYY-MM-DD") return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (medium) return MonthNameDate(value, preference, culture, localized);
        return preference switch
        {
            "DD/MM/YYYY" => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            "MM/DD/YYYY" => value.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
            _ => value.ToString("d", culture),
        };
    }

    public static string MonthNameDate(DateTime value, string? preference, CultureInfo culture, bool localized, bool year = true)
    {
        if (!localized)
            culture = preference switch
            {
                "DD/MM/YYYY" => CultureInfo.GetCultureInfo("en-GB"),
                "MM/DD/YYYY" => CultureInfo.GetCultureInfo("en-US"),
                _ => culture,
            };
        var pattern = culture.TwoLetterISOLanguageName switch
        {
            "ja" or "zh" => year ? "yyyy年M月d日" : "M月d日",
            "de" => year ? "d. MMM yyyy" : "d. MMM",
            "es" or "fr" or "ar" => year ? "d MMM yyyy" : "d MMM",
            "pt" => year ? "d 'de' MMM 'de' yyyy" : "d 'de' MMM",
            "en" => culture.DateTimeFormat.MonthDayPattern.TrimStart().StartsWith('d')
                ? (year ? "d MMM yyyy" : "d MMM") : (year ? "MMM d, yyyy" : "MMM d"),
            // Other device locales retain their own field order and punctuation.
            _ => year
                ? Regex.Replace(culture.DateTimeFormat.LongDatePattern, @"dddd[\s,]*|[\s,]*dddd", "").Replace("MMMM", "MMM")
                : culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM"),
        };
        return value.ToString(pattern, culture);
    }
}
