using SiloPlayer.Core.Helpers;

namespace SiloPlayer.Tests;

public sealed class TimeAgoTests
{
    [Fact]
    public void OffsetlessServerTimestampIsInterpretedAsUtc()
    {
        var utc = DateTime.UtcNow.AddMinutes(-5);
        var offsetless = utc.ToString("yyyy-MM-dd'T'HH:mm:ss");

        Assert.Equal("5m ago", TimeAgo.FormatShort(offsetless));
    }

    [Fact]
    public void ExplicitOffsetStillRepresentsTheSameInstant()
    {
        var timestamp = DateTimeOffset.UtcNow.AddHours(-3).ToOffset(TimeSpan.FromHours(-5));

        Assert.Equal("3h ago", TimeAgo.FormatShort(timestamp.ToString("O")));
    }
}
