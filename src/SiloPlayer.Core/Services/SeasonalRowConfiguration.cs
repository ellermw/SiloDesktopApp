using System.Globalization;
using System.Text.Json;
using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Home;

namespace SiloPlayer.Core.Services;

/// <summary>Editor validation only; the server selects the active holiday and its titles.</summary>
public static class SeasonalRowConfiguration
{
    public static T Read<T>(Dictionary<string, object> config, string key, T fallback)
    {
        if (!config.TryGetValue(key, out var value) || value is null) return fallback;
        try { return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, V2Json.Options), V2Json.Options) ?? fallback; }
        catch (JsonException) { return fallback; }
    }

    public static void Set<T>(Dictionary<string, object> config, string key, T value)
        => config[key] = JsonSerializer.SerializeToElement(value, V2Json.Options);

    public static bool IsMonthDay(string? value) => value is { Length: 5 } && value[2] == '-'
        && DateOnly.TryParseExact("2000-" + value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static bool IsPublicListUrl(string? value)
    {
        var text = value?.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        var authorityStart = text!.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = text.IndexOfAny(['/', '?', '#'], authorityStart);
        var authority = authorityEnd < 0 ? text[authorityStart..] : text[authorityStart..authorityEnd];
        return !authority.Contains('@') && uri.UserInfo.Length == 0
            && uri.Port is 80 or 443
            && uri.Host.TrimEnd('.').ToLowerInvariant() is "mdblist.com" or "www.mdblist.com"
            && uri.AbsolutePath.StartsWith("/lists/", StringComparison.Ordinal);
    }

    public static string? Validate(Dictionary<string, object> config, int maxCustomHolidays = 12)
    {
        var holidays = Read(config, "custom_holidays", new List<SeasonalCustomHoliday>());
        if (holidays.Count > maxCustomHolidays) return $"Choose at most {maxCustomHolidays} custom holidays.";
        foreach (var holiday in holidays)
        {
            if (holiday is null) return "Complete each custom holiday.";
            if (string.IsNullOrWhiteSpace(holiday.Name)) return "Give each custom holiday a name.";
            if (!IsMonthDay(holiday.Start) || !IsMonthDay(holiday.End)) return "Enter each holiday's start and end as MM-DD, such as 07-04.";
            if (!IsPublicListUrl(holiday.ListUrl)) return "Use a public MDBList list link (https://mdblist.com/lists/…).";
        }
        foreach (var dates in Read(config, "theme_dates", new Dictionary<string, SeasonalDates>()).Values)
            if (dates is null || !IsMonthDay(dates.Start) || !IsMonthDay(dates.End)) return "Enter both dates as MM-DD, or clear them to use the usual dates.";
        if (holidays.Count == 0 && config.ContainsKey("enabled_themes")
            && Read(config, "enabled_themes", Array.Empty<string>()).Length == 0)
            return "Pick at least one holiday or add your own.";
        return null;
    }
}
