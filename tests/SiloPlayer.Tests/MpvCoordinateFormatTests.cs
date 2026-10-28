using System.Globalization;
using SiloPlayer.Player;

namespace SiloPlayer.Tests;

public sealed class MpvCoordinateFormatTests
{
    [Theory]
    [InlineData("sv-SE")]
    [InlineData("nb-NO")]
    [InlineData("fi-FI")]
    [InlineData("de-DE")]
    public void NegativeDragCoordinatesUseAnAsciiMinusInEveryCulture(string culture)
    {
        // A captured seek drag past the window's left/top edge reports
        // negative client coordinates; mpv and Lua's tonumber need "-".
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("-12", MpvPlayer.FormatCoordinate(-12));
            Assert.Equal("1920", MpvPlayer.FormatCoordinate(1920));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void CurrentCultureFormattingWouldBreakTheParser()
    {
        // Documents the failure the helper avoids (ICU minus sign U+2212).
        Assert.Equal("−12", (-12).ToString(new CultureInfo("sv-SE")));
    }
}
