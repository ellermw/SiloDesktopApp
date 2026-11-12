using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Localization;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

/// <summary>
/// Displayed dates and times: the explicit date/time preferences win; "auto" and
/// month names follow the interface's display culture (<see cref="AppStrings.FormatCulture"/>).
/// </summary>
public static class DateTimeDisplay
{
    public static string FormatDate(DateTimeOffset value, bool medium = false)
    {
        var local = value.ToLocalTime();
        var preference = App.Services.GetRequiredService<SettingsService>().Load().UiDateFormat;
        if (medium) return preference switch
        {
            "DD/MM/YYYY" => local.ToString("d MMM yyyy", AppStrings.FormatCulture),
            "MM/DD/YYYY" => local.ToString("MMM d, yyyy", AppStrings.FormatCulture),
            "YYYY-MM-DD" => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            // Match the WebUI's medium date contract: abbreviated month, numeric day, and year.
            _ => local.ToString("MMM d, yyyy", AppStrings.FormatCulture)
        };
        return preference switch
        {
            "DD/MM/YYYY" => local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            "MM/DD/YYYY" => local.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
            "YYYY-MM-DD" => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => local.ToString("d", AppStrings.FormatCulture)
        };
    }

    public static string FormatTime(DateTimeOffset value, bool seconds = false)
    {
        var local = value.ToLocalTime();
        var preference = App.Services.GetRequiredService<SettingsService>().Load().UiTimeFormat;
        return preference switch
        {
            "12h" => local.ToString(seconds ? "h:mm:ss tt" : "h:mm tt", CultureInfo.InvariantCulture),
            "24h" => local.ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture),
            _ => local.ToString(seconds ? "T" : "t", AppStrings.FormatCulture)
        };
    }

    public static string FormatDateTime(DateTimeOffset value, bool seconds = false)
        => $"{FormatDate(value)} {FormatTime(value, seconds)}";

    public static string FormatShortDateTime(DateTimeOffset value)
    {
        var preference = App.Services.GetRequiredService<SettingsService>().Load().UiDateFormat;
        // A pinned English regional order yields to the interface language when dates are localized (as the web does).
        var culture = preference switch
        {
            _ when AppStrings.Current.LocalizedFormats => AppStrings.FormatCulture,
            "DD/MM/YYYY" => CultureInfo.GetCultureInfo("en-GB"),
            "MM/DD/YYYY" => CultureInfo.GetCultureInfo("en-US"),
            _ => AppStrings.FormatCulture,
        };
        var day = value.ToLocalTime().ToString(preference == "DD/MM/YYYY" ? "d MMM" : "MMM d", culture);
        return $"{day}, {FormatTime(value)}";
    }
}
