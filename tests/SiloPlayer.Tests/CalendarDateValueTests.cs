using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class CalendarDateValueTests
{
    [Theory]
    [InlineData(-12)]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(14)]
    public void StoredDayStaysOnItsCalendarDayInEveryDisplayZone(int offsetHours)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("fixture", TimeSpan.FromHours(offsetHours), "fixture", "fixture");
        Assert.True(CalendarDateValue.TryParse("2026-10-18", zone, out var value));
        Assert.Equal(new DateOnly(2026, 10, 18), DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, zone).DateTime));
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("2026-02-29")]
    [InlineData("2026-10-18T00:00:00Z")]
    [InlineData("10/18/2026")]
    [InlineData(null)]
    public void InvalidDaysAndActualTimestampsDoNotEnterCalendarDatePath(string? value)
        => Assert.False(CalendarDateValue.TryParse(value, TimeZoneInfo.Utc, out _));

    [Fact]
    public void LeapDayIsPreserved()
    {
        Assert.True(CalendarDateValue.TryParse("2028-02-29", TimeZoneInfo.Utc, out var value));
        Assert.Equal(29, value.Day);
    }
}
