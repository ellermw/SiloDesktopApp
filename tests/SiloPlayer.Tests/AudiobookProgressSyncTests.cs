using System.Text.Json;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class AudiobookProgressSyncTests
{
    [Theory]
    [InlineData(120.125, 120125)]
    [InlineData(-1, 0)]
    [InlineData(8000, 7200000)]
    public void SendsGlobalBookPositionAsIntegerMilliseconds(double seconds, long expected)
    {
        var body = JsonSerializer.SerializeToElement(AudiobookProgressSync.Create("book-1", seconds, 7200));
        var item = body.GetProperty("items")[0];
        Assert.Equal(expected, item.GetProperty("position_ms").GetInt64());
        Assert.Equal(7200000, item.GetProperty("duration_ms").GetInt64());
        Assert.True(item.GetProperty("force_overwrite").GetBoolean());
        Assert.False(item.TryGetProperty("position", out _));
        Assert.False(item.TryGetProperty("duration", out _));
    }
}
