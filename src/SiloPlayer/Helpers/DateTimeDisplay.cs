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
        return DisplayDateFormatting.Date(local.DateTime, preference, AppStrings.FormatCulture, AppStrings.Current.LocalizedFormats, medium);
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
        var day = DisplayDateFormatting.MonthNameDate(value.ToLocalTime().DateTime, preference,
            AppStrings.FormatCulture, AppStrings.Current.LocalizedFormats, year: false);
        return $"{day}, {FormatTime(value)}";
    }
}
