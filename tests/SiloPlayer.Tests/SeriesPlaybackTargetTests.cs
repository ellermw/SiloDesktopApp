using System.Text.Json;
using SiloPlayer.Core.Models.Catalog;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public sealed class SeriesPlaybackTargetTests
{
    private static MediaItemDetail Read(string json) => JsonSerializer.Deserialize<MediaItemDetail>(json,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })!;

    [Theory]
    [InlineData(1, 3, false, "Resume")]
    [InlineData(0, 3, false, "Play Next")]
    [InlineData(0, 3, true, "Start From Episode 1")]
    [InlineData(0, 0, false, "Start From Episode 1")]
    public void ServerTargetAndRollupDetermineSeriesAction(int progressing, int watched, bool played, string label)
    {
        var item = Read(JsonSerializer.Serialize(new { type = "series", play_content_id = "episode-97",
            user_data = new { in_progress_count = progressing, watched_count = watched, played } }));
        Assert.True(item.HasAuthoritativePlayTarget);
        var action = SeriesPrimaryActionResolver.ResolveDetail(item);
        Assert.Equal("episode-97", action.ContentId);
        Assert.Equal(label, action.Label);
    }

    [Fact]
    public void ExplicitlyUnavailableTargetDoesNotFallBackToAnInferredEpisode()
    {
        var unavailable = Read("{\"type\":\"series\",\"play_content_id\":null}");
        Assert.True(unavailable.HasAuthoritativePlayTarget);
        Assert.Null(SeriesPrimaryActionResolver.ResolveDetail(unavailable).ContentId);
        Assert.Equal("Browse Series", SeriesPrimaryActionResolver.ResolveDetail(unavailable).Label);
        var omitted = Read("{\"type\":\"series\"}");
        Assert.False(omitted.HasAuthoritativePlayTarget);
        Assert.Null(SeriesPrimaryActionResolver.ResolveDetail(omitted).ContentId);
    }
}
