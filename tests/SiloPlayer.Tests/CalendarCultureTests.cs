using System.Globalization;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CalendarCultureTests
{
    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void WeekDatesSentToTheServerAreGregorianInEveryCulture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            var date = new DateTime(2026, 10, 28);

            Assert.Equal("2026-10-26", CalendarViewModel.GetWeekStart(date));
            Assert.Equal("2026-11-01", CalendarViewModel.AddDays("2026-10-26", 6));
            Assert.Equal("2026-10-26", CalendarViewModel.GetWeekDays("2026-10-26")[0]);
            Assert.Equal("2026-11-01", CalendarViewModel.GetWeekDays("2026-10-26")[6]);
            Assert.True(CalendarViewModel.IsToday(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void WeekLabelsUseGregorianMonthNamesBesideGregorianDayNumbers(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
            culture.DateTimeFormat.Calendar = new GregorianCalendar();
            var october = new DateTime(2026, 10, 1).ToString("MMM", culture);

            Assert.Equal($"{october} 26 – {new DateTime(2026, 11, 1).ToString("MMM", culture)} 1, 2026",
                CalendarViewModel.FormatWeekRangeLabel("2026-10-26"));
            Assert.Equal(new DateTime(2026, 10, 28).ToString("ddd", culture), CalendarViewModel.FormatShortDay("2026-10-28").Label);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
