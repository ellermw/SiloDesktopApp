using SiloPlayer.Core.Services.Admin;

namespace SiloPlayer.Tests;

/// <summary>Upstream AdminActivity.test.tsx filter/sort behaviour.</summary>
public sealed class AdminActivityListStateTests
{
    private static readonly IReadOnlyList<SiloPlayer.Core.Models.Admin.AdminActivitySession> Sessions =
    [
        AdminActivityPresentationTests.Session(x => { x.SessionId = "a"; x.Username = "zoe"; x.MediaTitle = "Alien"; x.StartedAt = "2026-11-14T10:00:00Z"; x.PlayMethod = "direct"; x.VideoDecision = "direct"; x.AudioDecision = "direct"; x.ClientIp = "10.0.0.5"; }),
        AdminActivityPresentationTests.Session(x => { x.SessionId = "b"; x.Username = "amy"; x.MediaTitle = "Brazil"; x.StartedAt = "2026-11-14T11:00:00Z"; x.MediaType = "series"; x.SeriesName = "Show"; x.SeasonNumber = 1; x.EpisodeNumber = 2; x.EpisodeName = "Pilot"; x.RoutingExecution = "transcode"; x.RoutingExecutionNodeName = "gpu-1"; x.RoutingWorkload = "video_transcode"; }),
        AdminActivityPresentationTests.Session(x => { x.SessionId = "c"; x.Username = "max"; x.MediaTitle = "Cars"; x.StartedAt = "2026-11-14T09:00:00Z"; x.ClientLabelFull = "Silo iOS 2.1 (build 5)"; }),
    ];

    [Fact]
    public void DefaultSortIsTimeDescendingAndNewFieldsStartAscending()
    {
        var state = new AdminActivityListState();
        Assert.Equal(["b", "a", "c"], state.Apply(Sessions).Select(s => s.SessionId));
        state.ToggleSort(ActivitySortField.Username);
        Assert.Equal(["b", "c", "a"], state.Apply(Sessions).Select(s => s.SessionId));
        state.ToggleSort(ActivitySortField.Username);
        Assert.Equal(["a", "c", "b"], state.Apply(Sessions).Select(s => s.SessionId));
        state.ToggleSort(ActivitySortField.Started);
        Assert.False(state.SortAscending);
        state.ToggleSort(ActivitySortField.Method);
        Assert.Equal("a", state.Apply(Sessions)[0].SessionId);
    }

    [Fact]
    public void SearchMatchesFullClientLabelIpAndRouteNames()
    {
        var state = new AdminActivityListState { Search = "build 5" };
        Assert.Equal("c", Assert.Single(state.Apply(Sessions)).SessionId);
        state.Search = "10.0.0";
        Assert.Equal("a", Assert.Single(state.Apply(Sessions)).SessionId);
        state.Search = "GPU-1";
        Assert.Equal("b", Assert.Single(state.Apply(Sessions)).SessionId);
        state.Search = "pilot";
        Assert.Equal("b", Assert.Single(state.Apply(Sessions)).SessionId);
        Assert.True(state.ShowsFilterStatus);
    }

    [Fact]
    public void MethodNodeAndTypeFiltersToggleAndClearKeepsSearch()
    {
        var state = new AdminActivityListState { Search = "" };
        state.ToggleMethod("direct");
        Assert.Equal("a", Assert.Single(state.Apply(Sessions)).SessionId);
        state.ToggleMethod("direct");
        Assert.Null(state.MethodFilter);
        state.ToggleNode("transcode:gpu-1");
        Assert.Equal("b", Assert.Single(state.Apply(Sessions)).SessionId);
        state.ToggleType("series");
        Assert.Equal(2, state.ActiveFilterCount);
        state.Search = "x";
        state.ClearFilters();
        Assert.Equal(0, state.ActiveFilterCount);
        Assert.Equal("x", state.Search);
    }

    [Fact]
    public void SummaryCountsAndSubtitle()
    {
        var methods = AdminActivityListState.MethodCounts(Sessions);
        Assert.Equal(["direct", "transcode"], methods.Select(m => m.Meta.Key));
        Assert.Equal([1, 2], methods.Select(m => m.Count));
        var nodes = AdminActivityListState.RouteNodeCounts(Sessions);
        Assert.Equal(2, nodes.Count);
        Assert.Equal("3 active streams across 2 nodes", AdminActivityListState.Subtitle(Sessions));
        Assert.Equal("No active streams", AdminActivityListState.Subtitle([]));
        Assert.Equal("3 live", AdminActivityListState.LiveCount(3));
        Assert.Equal("Showing 1 of 3 streams", AdminActivityListState.ShowingText(1, 3));
    }
}
