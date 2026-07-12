using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Helpers;

public static class DateTimeDisplay
{
    public static string FormatDate(DateTimeOffset value, bool medium = false)
    {
        var local = value.ToLocalTime();
        var preference = App.Services.GetRequiredService<SettingsService>().Load().UiDateFormat;
        if (medium) return preference switch
        {
            "DD/MM/YYYY" => local.ToString("d MMM yyyy", CultureInfo.CurrentCulture),
            "MM/DD/YYYY" => local.ToString("MMM d, yyyy", CultureInfo.CurrentCulture),
            "YYYY-MM-DD" => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => local.ToString("D", CultureInfo.CurrentCulture)
        };
        return preference switch
        {
            "DD/MM/YYYY" => local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            "MM/DD/YYYY" => local.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
            "YYYY-MM-DD" => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => local.ToString("d", CultureInfo.CurrentCulture)
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
            _ => local.ToString(seconds ? "T" : "t", CultureInfo.CurrentCulture)
        };
    }

    public static string FormatDateTime(DateTimeOffset value, bool seconds = false)
        => $"{FormatDate(value)} {FormatTime(value, seconds)}";
}
