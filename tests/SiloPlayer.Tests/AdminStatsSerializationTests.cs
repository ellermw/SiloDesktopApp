using System.Text.Json;
using SiloPlayer.Core.Models.Admin;

namespace SiloPlayer.Tests;

public class AdminStatsSerializationTests
{
    [Fact]
    public void WatchProviderNumericSuffixFieldsMatchServerContract()
    {
        const string json = """
        {
          "watch_provider_activity": {
            "sync_runs_24h": 79,
            "sync_errors_24h": 7,
            "imported_watched_24h": 297,
            "imported_progress_24h": 22,
            "exported_watched_24h": 2399,
            "scrobbles_24h": 18
          }
        }
        """;

        var stats = JsonSerializer.Deserialize<AdminStats>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });

        Assert.NotNull(stats);
        Assert.Equal(79, stats.WatchProviderActivity.SyncRuns24h);
        Assert.Equal(7, stats.WatchProviderActivity.SyncErrors24h);
        Assert.Equal(297, stats.WatchProviderActivity.ImportedWatched24h);
        Assert.Equal(22, stats.WatchProviderActivity.ImportedProgress24h);
        Assert.Equal(2399, stats.WatchProviderActivity.ExportedWatched24h);
        Assert.Equal(18, stats.WatchProviderActivity.Scrobbles24h);
    }
}
