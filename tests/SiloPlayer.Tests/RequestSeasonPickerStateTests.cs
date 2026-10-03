using SiloPlayer.Core.Models.Requests;
using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

public class RequestSeasonPickerStateTests
{
    private static dynamic Create(RequestMediaDetail item)
    {
        var type = typeof(RequestViewerPolicy).Assembly.GetType("SiloPlayer.Core.Services.RequestSeasonPickerState");
        Assert.NotNull(type);
        return Activator.CreateInstance(type!, item, new DateOnly(2026, 10, 1))!;
    }
    [Fact]
    public void UntouchedUpcomingExternalSeriesRequestsWholeSeriesWhileExplicitEmptyIsDisabled()
    {
        dynamic state = Create(new() { Availability = "missing", Request = new() { Requestable = true }, Seasons = [new() { SeasonNumber = 1, AirDate = "2027-01-01", EpisodeCount = 8 }] });
        Assert.True((bool)state.CanSubmit); Assert.Null((List<int>?)state.SubmissionSeasons);
        state.SelectAll(false); Assert.False((bool)state.CanSubmit);
        state.SelectAll(true); Assert.Equal(new[] { 1 }, (List<int>)state.SubmissionSeasons);
    }
    [Fact]
    public void LibrarySeriesWithOnlyUpcomingNeedsExplicitSelection()
    {
        dynamic state = Create(new() { Availability = "available", Request = new() { Requestable = true }, Seasons = [new() { SeasonNumber = 1, Availability = "available", AirDate = "2025-01-01", EpisodeCount = 8 }, new() { SeasonNumber = 2, AirDate = "2027-01-01", EpisodeCount = 8 }] });
        Assert.False((bool)state.CanSubmit); state.SetSeason(2, true);
        Assert.True((bool)state.CanSubmit); Assert.Equal(new[] { 2 }, (List<int>)state.SubmissionSeasons);
    }
    [Fact]
    public void AllSeasonsIncludesMissingSpecialsAndExcludesAvailableOrRequested()
    {
        dynamic state = Create(new() { Request = new() { Requestable = true }, Seasons = [new() { SeasonNumber = 0, AirDate = "2024-01-01", EpisodeCount = 1 }, new() { SeasonNumber = 1, Availability = "available" }, new() { SeasonNumber = 2, Requested = true }, new() { SeasonNumber = 3 }] });
        state.SelectAll(true); Assert.Equal(new[] { 0, 3 }, (List<int>)state.SubmissionSeasons);
        Assert.True((bool)state.AllSelected);
    }
    [Fact]
    public void UntouchedMissingAiredSeasonsLeaveSelectionToServerAndExplicitChoiceIsNamed()
    {
        dynamic state = Create(new() { Request = new() { Requestable = true }, Seasons = [new() { SeasonNumber = 2, AirDate = "2025-01-01", EpisodeCount = 8 }] });
        Assert.Null((List<int>?)state.SubmissionSeasons); Assert.Equal("Request Season 2", (string)state.SubmitLabel);
        state.SetSeason(2, false); state.SetSeason(2, true); Assert.Equal(new[] { 2 }, (List<int>)state.SubmissionSeasons);
    }
}
