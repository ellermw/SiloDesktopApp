using System.Globalization;

namespace SiloPlayer.Core.Services;

/// <summary>A stored calendar day is local to its display zone, not a UTC instant.</summary>
public static class CalendarDateValue
{
    public static bool TryParse(string? value, TimeZoneInfo zone, out DateTimeOffset localDay)
    {
        localDay = default;
        if (value?.Length != 10 || !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day)) return false;
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        try
        {
            localDay = new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
