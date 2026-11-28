using System.Globalization;
using SiloPlayer.ViewModels;

namespace SiloPlayer.Tests;

public sealed class CalendarCultureTests
{
    // Official calendarWeek.test.ts (#5230): a phone-width week strip shows the short weekday names when every one of the seven fits in four
    // characters, otherwise the shortest names, for the whole week.
    [Theory]
    [InlineData("en-US", false)]
    [InlineData("pl-PL", true)]
    public void APhoneWidthWeekStripUsesTheShortestNamesOnlyWhenAShortNameIsTooLong(string cultureName, bool compacted)
    {
        using var scope = TestCulture.Use(cultureName);
        using var locale = SiloPlayer.Core.Localization.AppStrings.UseForCurrentFlow(new SiloPlayer.Core.Localization.LocaleService().Create(
            SiloPlayer.Core.Localization.AppLocaleResolution.Resolve(null, [cultureName], cultureName)));
        var culture = SiloPlayer.Core.Localization.AppStrings.FormatCulture;
        foreach (var date in Enumerable.Range(23, 7).Select(day => $"2026-11-{day}"))
        {
            var (label, compact, day) = CalendarViewModel.FormatShortDay(date);
            var parsed = new DateTime(2026, 11, day);
            Assert.Equal(parsed.ToString("ddd", culture), label);
            Assert.Equal(compacted ? culture.DateTimeFormat.GetShortestDayName(parsed.DayOfWeek) : label, compact);
            if (compacted) Assert.True(compact.Length < label.Length || compact.Length <= 2, $"{culture.Name}: '{compact}' for '{label}'");
        }
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void WeekDatesSentToTheServerAreGregorianInEveryCulture(string cultureName)
    {
        using var scope = TestCulture.Use(cultureName);
        var date = new DateTime(2026, 10, 28);

        Assert.Equal("2026-10-26", CalendarViewModel.GetWeekStart(date));
        Assert.Equal("2026-11-01", CalendarViewModel.AddDays("2026-10-26", 6));
        Assert.Equal("2026-10-26", CalendarViewModel.GetWeekDays("2026-10-26")[0]);
        Assert.Equal("2026-11-01", CalendarViewModel.GetWeekDays("2026-10-26")[6]);
        Assert.True(CalendarViewModel.IsToday(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    public void WeekLabelsUseGregorianMonthNamesBesideGregorianDayNumbers(string cultureName)
    {
        using var scope = TestCulture.Use(cultureName);
        var culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        var october = new DateTime(2026, 10, 1).ToString("MMM", culture);
        using var locale = SiloPlayer.Core.Localization.AppStrings.UseForCurrentFlow(new SiloPlayer.Core.Localization.LocaleService().Create(
            SiloPlayer.Core.Localization.AppLocaleResolution.Resolve(null, [cultureName], cultureName)));
        var range = CalendarViewModel.FormatWeekRangeLabel("2026-10-26");
        Assert.Contains(october, range);
        Assert.Contains(new DateTime(2026, 11, 1).ToString("MMM", culture), range);
        Assert.Contains("2026", range);
        Assert.Equal(new DateTime(2026, 10, 28).ToString("ddd", culture), CalendarViewModel.FormatShortDay("2026-10-28").Label);
    }
}
