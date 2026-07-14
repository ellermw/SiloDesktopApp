using System.Text.Json;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Tests;

public class FlexibleNullableDateTimeOffsetConverterTests
{
    [Theory]
    [InlineData("\"2026-07-13T16:00:00Z\"", 2026)]
    [InlineData("1752422400", 2025)]
    [InlineData("1752422400000", 2025)]
    public void DeviceTimestampsAcceptSiloTimestampShapes(string value, int expectedYear)
    {
        var profile = JsonSerializer.Deserialize<AdminDeviceProfileSummary>($$"""{"LastUpdated":{{value}}}""");
        Assert.Equal(expectedYear, profile?.LastUpdated?.Year);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"Unknown\"")]
    public void MissingOrLegacyDeviceTimestampsRemainUnknown(string value)
    {
        var profile = JsonSerializer.Deserialize<AdminDeviceProfileSummary>($$"""{"LastUpdated":{{value}}}""");
        Assert.Null(profile?.LastUpdated);
    }
}
