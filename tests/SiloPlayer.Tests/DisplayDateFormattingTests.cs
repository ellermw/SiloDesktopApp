using System.Globalization;
using SiloPlayer.Core.Localization;

namespace SiloPlayer.Tests;

public sealed class DisplayDateFormattingTests
{
    private static readonly DateTime Day = new(2026, 11, 12);

    [Theory]
    [InlineData("ja", "MM/DD/YYYY", true, "2026年11月12日", "11月12日")]
    [InlineData("de-DE", "MM/DD/YYYY", true, "12. Nov. 2026", "12. Nov.")]
    [InlineData("en-GB", "auto", false, "12 Nov 2026", "12 Nov")]
    [InlineData("en-US", "auto", false, "Nov 12, 2026", "Nov 12")]
    [InlineData("en-US", "DD/MM/YYYY", false, "12 Nov 2026", "12 Nov")]
    [InlineData("en-GB", "MM/DD/YYYY", false, "Nov 12, 2026", "Nov 12")]
    public void MonthNameDatesFollowTheInterfaceOrderAndEnglishRegionalPreference(
        string culture, string preference, bool localized, string medium, string shortDate)
    {
        var format = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(medium, DisplayDateFormatting.Date(Day, preference, format, localized, medium: true));
        Assert.Equal(shortDate, DisplayDateFormatting.MonthNameDate(Day, preference, format, localized, year: false));
    }

    [Theory]
    [InlineData("DD/MM/YYYY", "12/11/2026")]
    [InlineData("MM/DD/YYYY", "11/12/2026")]
    [InlineData("YYYY-MM-DD", "2026-11-12")]
    public void NumericPreferencesKeepTheirExactGregorianForm(string preference, string expected)
    {
        Assert.Equal(expected, DisplayDateFormatting.Date(Day, preference, CultureInfo.GetCultureInfo("ja"), true));
        if (preference == "YYYY-MM-DD")
            Assert.Equal(expected, DisplayDateFormatting.Date(Day, preference, CultureInfo.GetCultureInfo("de-DE"), true, medium: true));
    }
}
