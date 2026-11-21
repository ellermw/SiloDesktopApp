using SiloPlayer.Core.Api;
using SiloPlayer.Core.Models.Admin;
using SiloPlayer.Core.Services;
using SiloPlayer.Core.Services.Admin;
using P = SiloPlayer.Core.Services.Admin.AdminActivityPresentation;

namespace SiloPlayer.Tests;

/// <summary>
/// The Monitor parity refresh against official e130f262d412cc7ffc4f0e040bdaba02c132515e: the live-row naming change (#4561), the Activity episode filter,
/// and the review finding on Concurrent streams' Stacked view (offered only on the advertised <c>streams_by_method</c> capability).
/// </summary>
public sealed class AdminMonitorParityRefreshTests
{
    private static DashboardTimeseries Series(params DashboardTimeseriesPoint[] points) => new() { ResolutionSeconds = 60, Points = [.. points] };

    private static DashboardTimeseriesPoint Point(bool split) =>
        new() { T = DateTimeOffset.Parse("2026-11-20T10:00:00Z"), Streams = 3, StreamsByMethod = split ? new() { ["direct"] = 3 } : null };

    // ---- review finding on #207 (P2): "Gate Stacked on the advertised capability" ----

    [Fact]
    public void AnEmptyWindowShowsNoMethodSplit()
    {
        // HasMethodSplit once applied All to an empty point list and answered true, so an older server with no samples in the window offered Stacked.
        Assert.False(DashboardChartMath.HasMethodSplit(Series()));
        Assert.False(DashboardChartMath.HasMethodSplit(new DashboardTimeseries { Points = null }));
        Assert.True(DashboardChartMath.HasMethodSplit(Series(Point(true))));
        Assert.False(DashboardChartMath.HasMethodSplit(Series(Point(true), Point(false))));
    }

    [Fact]
    public void StackedIsOfferedOnlyWhenTheServerAdvertisesTheSplit()
    {
        var older = new DashboardCapabilities { StreamsByMethod = false };
        var current = new DashboardCapabilities { StreamsByMethod = true };

        // The capability read is still in flight, or failed: the option is not offered, whatever the timeseries says.
        Assert.False(DashboardChartMath.CanStackConcurrent(null, null));
        Assert.False(DashboardChartMath.CanStackConcurrent(null, Series(Point(true))));

        // An older server: not offered while the timeseries is loading, with no samples, or with samples (which carry no split).
        Assert.False(DashboardChartMath.CanStackConcurrent(older, null));
        Assert.False(DashboardChartMath.CanStackConcurrent(older, Series()));
        Assert.False(DashboardChartMath.CanStackConcurrent(older, Series(Point(false))));

        // A current server: offered while loading and for an empty window (the capability is the evidence), withdrawn for a window an older replica answered.
        Assert.True(DashboardChartMath.CanStackConcurrent(current, null));
        Assert.True(DashboardChartMath.CanStackConcurrent(current, Series()));
        Assert.True(DashboardChartMath.CanStackConcurrent(current, Series(Point(true))));
        Assert.False(DashboardChartMath.CanStackConcurrent(current, Series(Point(true), Point(false))));
    }

    // ---- #4561: a live row names an episode as history does ----

    private static async Task<AdminActivitySession> ReadRow(string extra)
    {
        using var kit = AdminMonitorKit.Create();
        using var authority = new AdminMonitorAuthority(kit.Client, kit.Auth);
        var row = "{\"session_id\":\"s1\",\"user_id\":\"7\",\"username\":\"viewer\",\"profile_id\":\"p1\",\"media_file_id\":\"40\",\"requested_media_file_id\":\"40\"," +
                  "\"play_method\":\"direct\",\"reporting_node\":\"local\",\"started_at\":\"2026-11-20T10:00:00Z\",\"updated_at\":\"2026-11-20T10:00:00Z\"," +
                  "\"position_seconds\":12,\"is_paused\":false,\"has_playback_control\":true" + extra + "}";
        kit.Http.Respond = (_, _) => Task.FromResult(AdminMonitorKit.Json("{\"items\":[" + row + "],\"page\":{\"has_more\":false}}"));
        using var lease = authority.Capture();
        return Assert.Single(await new AdminMonitorApi(kit.Client).ListActivitySessionsAsync(lease));
    }

    [Fact]
    public async Task ACurrentEpisodeRowIsNamedByItsOwnTitleWithTheShowAsTheSeries()
    {
        var row = await ReadRow(",\"media_type\":\"episode\",\"media_title\":\"Pilot\",\"series_title\":\"Example Show\",\"media_item_id\":\"ep-9\",\"season_number\":1,\"episode_number\":3");
        Assert.Equal("Pilot", P.GetPlaybackSessionTitle(row));
        Assert.Equal("S1E3 · Example Show", P.GetPlaybackSessionSubtitle(row));
        Assert.Equal("ep-9", row.MediaItemId);
        Assert.Equal("Example Show", row.SeriesTitle);
        var tile = ServerActivityDashboardFeed.MapSession(row);
        Assert.Equal("ep-9", tile.MediaItemId);
        Assert.Equal("Example Show", tile.SeriesTitle);
        Assert.Equal("Pilot", DashboardPresentation.SessionTitle(tile, "nowPlaying"));
    }

    [Fact]
    public async Task AnEpisodeWithoutATitleFallsBackToItsCodeAndASeriesLevelRowToTheSeriesSubtitle()
    {
        var untitled = await ReadRow(",\"media_type\":\"episode\",\"media_title\":\"\",\"series_title\":\"Example Show\",\"season_number\":2,\"episode_number\":1");
        Assert.Equal("S2E1", P.GetPlaybackSessionTitle(untitled));
        // A file linked to a show with no matching episode row is reported as the show itself.
        var show = await ReadRow(",\"media_type\":\"series\",\"media_title\":\"Example Show\"");
        Assert.Equal("Example Show", P.GetPlaybackSessionTitle(show));
        Assert.Equal(P.GetPlaybackSessionSubtitle(show), P.GetPlaybackSessionSubtitle(await ReadRow(",\"media_type\":\"episode\",\"media_title\":\"x\"")));
        Assert.False(string.IsNullOrEmpty(P.GetPlaybackSessionSubtitle(show)));
    }

    [Fact]
    public async Task AServerThatStillSendsTheOldNamesIsReadIntoTheCurrentShape()
    {
        // Pre-e130f262d: the show as media_title, media_type "series", the episode in episode_name, content_id. The official client no longer reads these;
        // this native client may meet an older server, so they fold into the current fields and everything downstream sees one shape.
        var row = await ReadRow(",\"media_type\":\"series\",\"media_title\":\"Example Show\",\"series_name\":\"Example Show\",\"episode_name\":\"Pilot\",\"content_id\":\"c-1\",\"season_number\":1,\"episode_number\":3");
        Assert.Equal("episode", row.MediaType);
        Assert.Equal("Pilot", row.MediaTitle);
        Assert.Equal("Example Show", row.SeriesTitle);
        Assert.Equal("c-1", row.MediaItemId);
        Assert.Equal("Pilot", P.GetPlaybackSessionTitle(row));
        Assert.Equal("S1E3 · Example Show", P.GetPlaybackSessionSubtitle(row));

        // Old movie rows only rename content_id.
        var movie = await ReadRow(",\"media_type\":\"movie\",\"media_title\":\"Alien\",\"content_id\":\"m-1\"");
        Assert.Equal("m-1", movie.MediaItemId);
        Assert.Equal("Alien", movie.MediaTitle);
        Assert.Null(movie.SeriesTitle);

        // A current row never loses its fields to the old ones.
        var current = await ReadRow(",\"media_type\":\"episode\",\"media_title\":\"Pilot\",\"series_title\":\"Example Show\",\"series_name\":\"Stale\",\"episode_name\":\"Stale\",\"media_item_id\":\"ep-9\",\"content_id\":\"stale\",\"season_number\":1,\"episode_number\":3");
        Assert.Equal("Pilot", current.MediaTitle);
        Assert.Equal("Example Show", current.SeriesTitle);
        Assert.Equal("ep-9", current.MediaItemId);
    }

    // ---- #4561: the Activity type filter selects episodes and keeps show-level rows ----

    [Fact]
    public void TheSeriesChipSelectsEpisodesAndShowLevelRows()
    {
        var sessions = new[]
        {
            AdminActivityPresentationTests.Session(x => { x.SessionId = "movie"; x.MediaType = "movie"; x.MediaTitle = "Alien"; }),
            AdminActivityPresentationTests.Session(x => { x.SessionId = "episode"; x.MediaType = "episode"; x.MediaTitle = "Pilot"; x.SeriesTitle = "Show"; x.SeasonNumber = 1; x.EpisodeNumber = 1; }),
            AdminActivityPresentationTests.Session(x => { x.SessionId = "show"; x.MediaType = "series"; x.MediaTitle = "Show"; }),
        };
        var state = new AdminActivityListState();
        state.ToggleType("episode");
        Assert.Equal(["episode", "show"], state.Apply(sessions).Select(s => s.SessionId).Order());
        state.ToggleType("episode");
        state.ToggleType("movie");
        Assert.Equal("movie", Assert.Single(state.Apply(sessions)).SessionId);
        // The show's own title is searchable beside the episode's.
        state.ClearFilters();
        state.Search = "show";
        Assert.Equal(["episode", "show"], state.Apply(sessions).Select(s => s.SessionId).Order());
    }
}
