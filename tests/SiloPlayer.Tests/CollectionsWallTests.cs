using SiloPlayer.Core.Models.Collections;
using SiloPlayer.Core.Services;
using SiloPlayer.Services;

namespace SiloPlayer.Tests;

/// <summary>The Collections page's one poster wall: pills, current filter, labels and loaded empty lines (WebUI Collections.tsx).</summary>
public sealed class CollectionsWallTests
{
    private static ServerCollectionsLibrary Library(int id, string name, int total, params string[] shown)
        => new() { LibraryId = id, LibraryName = name, TotalCount = total, Collections = shown.Select(c => new ServerCollectionSummary { Id = c, Title = c }).ToList() };
    private static readonly CollectionsWall.Loading Idle = new(false, false, false);

    [Fact]
    public void AccountWithNothingToFilterBetweenShowsNoPills()
    {
        Assert.False(CollectionsWall.ShowPills([], 0));
        Assert.True(CollectionsWall.ShowPills([], 1));
        Assert.True(CollectionsWall.ShowPills([Library(1, "Movies", 4)], 0));
    }

    [Fact]
    public void PillsCountOnlyWhatTheyCanKnow()
    {
        var one = CollectionsWall.Pills([Library(1, "Movies", 9, Enumerable.Range(1, 9).Select(id => "server-" + id).ToArray())],
            ownCount: 3, sharedCount: 2, personalIds: ["own-1", "own-2", "own-3", "shared-1", "shared-2"]);
        Assert.Equal(["all", "mine", "shared", "server"], one.Select(p => p.Value));
        Assert.Equal([3 + 2 + 9, 3, 2, 9], one.Select(p => p.Count!.Value));

        // Totals are per library and a collection can be in several: no combined count for many libraries.
        var many = CollectionsWall.Pills([Library(1, "Movies", 9), Library(2, "Shows", 5)], 3, 0);
        Assert.Equal(["all", "mine", "server", "library:1", "library:2"], many.Select(p => p.Value));
        Assert.Null(many[0].Count); Assert.Null(many.Single(p => p.Value == "server").Count);
        Assert.Equal([9, 5], many.Where(p => p.Value.StartsWith("library:")).Select(p => p.Count!.Value));
        Assert.True(many.Single(p => p.Value == "library:1").Separated); Assert.False(many.Single(p => p.Value == "library:2").Separated);

        var none = CollectionsWall.Pills([], 4, 0);
        Assert.Equal(["all", "mine"], none.Select(p => p.Value)); Assert.Equal(4, none[0].Count);
    }

    [Fact]
    public void AllCountsIncludedPersonalCollectionsOnlyOnceAcrossServerLibraries()
    {
        var libraries = new[] { Library(1, "Movies", 3, "own", "shared", "server"), Library(2, "Shows", 2, "own", "server") };
        var pills = CollectionsWall.Pills(libraries, ownCount: 1, sharedCount: 1, personalIds: ["own", "shared"]);
        Assert.Equal(3, pills.Single(pill => pill.Value == CollectionsWall.All).Count);
        Assert.Equal(1, pills.Single(pill => pill.Value == CollectionsWall.Mine).Count);
        Assert.Equal(1, pills.Single(pill => pill.Value == CollectionsWall.Shared).Count);

        var singleLibrary = CollectionsWall.Pills([libraries[0]], 1, 1, ["own", "shared"]);
        Assert.Equal(3, singleLibrary.Single(pill => pill.Value == CollectionsWall.All).Count);
        Assert.Equal(3, singleLibrary.Single(pill => pill.Value == CollectionsWall.Server).Count);
    }

    [Fact]
    public void AllOmitsTheCountWhenUnloadedServerCardsCouldOverlapPersonalCollections()
    {
        var libraries = new[] { Library(1, "Movies", 3, "own") };
        var pills = CollectionsWall.Pills(libraries, 1, 0, ["own"]);
        Assert.Null(pills.Single(pill => pill.Value == CollectionsWall.All).Count);
        Assert.Equal(3, pills.Single(pill => pill.Value == CollectionsWall.Server).Count);
        Assert.Equal(3, CollectionsWall.Pills(libraries, 0, 0, [])[0].Count);   // no personal/server overlap to resolve
        Assert.Null(CollectionsWall.Pills(libraries, 1, 0)[0].Count);   // counts alone cannot establish a distinct total
    }

    [Theory]
    [InlineData("mine", "mine")]
    [InlineData("shared", "all")]      // nobody shares: falls back to All
    [InlineData("server", "server")]
    [InlineData("nonsense", "all")]
    [InlineData(null, "all")]
    public void FilterFallsBackToAllWhenTheAccountHasNothingForIt(string? requested, string expected)
        => Assert.Equal(expected, CollectionsWall.Resolve(requested, [Library(1, "Movies", 2)], sharedCount: 0, Idle));

    [Fact]
    public void RequestedFilterIsKeptWhileWhatItNeedsLoads()
    {
        Assert.Equal("shared", CollectionsWall.Resolve("shared", [], 0, new(PersonalLoading: true, ProfilesLoading: false, ServerLoading: false)));
        Assert.Equal("shared", CollectionsWall.Resolve("shared", [], 0, new(false, ProfilesLoading: true, false)));
        Assert.Equal("server", CollectionsWall.Resolve("server", [], 0, new(false, false, ServerLoading: true)));
        Assert.Equal("library:7", CollectionsWall.Resolve("library:7", [], 0, new(false, false, ServerLoading: true)));
        Assert.Equal("all", CollectionsWall.Resolve("server", [], 0, Idle));
    }

    [Fact]
    public void LibraryFilterNeedsItsLibraryAndALoneLibraryShowsAsServer()
    {
        var two = new[] { Library(1, "Movies", 2), Library(2, "Shows", 2) };
        Assert.Equal("library:2", CollectionsWall.Resolve("library:2", two, 0, Idle));
        Assert.Equal("all", CollectionsWall.Resolve("library:9", two, 0, Idle));
        Assert.Equal("server", CollectionsWall.Resolve("library:1", [two[0]], 0, Idle));
        Assert.Equal("shared", CollectionsWall.Resolve("shared", two, 3, Idle));
    }

    [Fact]
    public void WhichSectionsEachFilterShowsFollowsTheWebUi()
    {
        Assert.True(CollectionsWall.ShowsMine("all")); Assert.True(CollectionsWall.ShowsMine("mine")); Assert.False(CollectionsWall.ShowsMine("server"));
        Assert.True(CollectionsWall.ShowsShared("all", 2)); Assert.True(CollectionsWall.ShowsShared("shared", 2)); Assert.False(CollectionsWall.ShowsShared("all", 0)); Assert.False(CollectionsWall.ShowsShared("mine", 2));
        Assert.True(CollectionsWall.ShowsServer("all")); Assert.True(CollectionsWall.ShowsServer("server")); Assert.True(CollectionsWall.ShowsServer("library:3"));
        Assert.False(CollectionsWall.ShowsServer("mine")); Assert.False(CollectionsWall.ShowsServer("shared"));
    }

    [Fact]
    public void WallIsNamedForWhatItShows()
    {
        var movies = Library(1, "Movies", 1);
        Assert.Equal("All collections", CollectionsWall.Label("all", null));
        Assert.Equal("Your collections", CollectionsWall.Label("mine", null));
        Assert.Equal("Shared with me", CollectionsWall.Label("shared", null));
        Assert.Equal("Server collections", CollectionsWall.Label("server", null));
        Assert.Equal("Movies collections", CollectionsWall.Label("library:1", movies));
        Assert.Equal(movies, CollectionsWall.Picked("library:1", [movies]));
        Assert.Null(CollectionsWall.Picked("library:2", [movies]));
        Assert.Null(CollectionsWall.Picked("server", [movies]));
    }

    [Fact]
    public void EmptyLinesAppearOnlyAfterWhatTheViewShowsHasLoaded()
    {
        Assert.Equal("You haven't made any collections yet.", CollectionsWall.EmptyMessage("mine", true, false, false, 0, 0, 0));
        // A slow or failed request is not "none".
        Assert.Null(CollectionsWall.EmptyMessage("mine", false, true, true, 0, 0, 0));
        Assert.Null(CollectionsWall.EmptyMessage("mine", true, true, true, 1, 0, 0));

        Assert.Equal("There are no collections yet.", CollectionsWall.EmptyMessage("all", true, true, true, 0, 0, 0));
        Assert.Null(CollectionsWall.EmptyMessage("all", false, true, true, 0, 0, 0));   // personal still loading
        Assert.Null(CollectionsWall.EmptyMessage("all", true, false, true, 0, 0, 0));   // profiles (who shares) unknown
        Assert.Null(CollectionsWall.EmptyMessage("all", true, true, false, 0, 0, 0));   // server unknown or failed
        Assert.Null(CollectionsWall.EmptyMessage("all", true, true, true, 0, 1, 0));    // something shared
        Assert.Null(CollectionsWall.EmptyMessage("all", true, true, true, 0, 0, 1));    // one server collection
        Assert.Equal(CollectionsWall.EmptyAll, CollectionsWall.EmptyMessage("server", true, true, true, 0, 0, 0));
        Assert.Null(CollectionsWall.EmptyMessage("shared", true, true, true, 0, 0, 0));
    }

    [Fact]
    public void OneLibraryWithNoCollectionsShowsTheLoadedEmptyLine()
    {
        var libraries = new[] { Library(1, "Movies", 0) };
        var count = CollectionsWall.ServerCount(libraries);
        Assert.Equal(0, count);
        Assert.Equal(0, CollectionsWall.Pills(libraries, 0, 0)[0].Count);
        Assert.Equal(CollectionsWall.EmptyAll, CollectionsWall.EmptyMessage("all", true, true, true, 0, 0, count));
        Assert.Equal(CollectionsWall.EmptyAll, CollectionsWall.EmptyMessage("server", false, false, true, 2, 1, count));
        Assert.Null(CollectionsWall.EmptyMessage("all", true, true, false, 0, 0, count));
        Assert.Null(CollectionsWall.EmptyMessage("server", true, true, false, 0, 0, count));

        Assert.Equal(CollectionsWall.EmptyAll, CollectionsWall.EmptyMessage("library:1", false, false, true, 0, 0, libraries[0].TotalCount));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void SeveralEmptyLibrariesShowTheLoadedEmptyLine(int libraryCount)
    {
        var libraries = Enumerable.Range(1, libraryCount).Select(id => Library(id, "Library " + id, 0)).ToArray();
        var count = CollectionsWall.ServerCount(libraries);
        Assert.Equal(0, count);   // overlap cannot make an empty union non-empty
        var pills = CollectionsWall.Pills(libraries, 0, 0);
        Assert.Equal(0, pills.Single(pill => pill.Value == "all").Count);
        Assert.Equal(0, pills.Single(pill => pill.Value == "server").Count);
        Assert.Equal(CollectionsWall.EmptyAll, CollectionsWall.EmptyMessage("all", true, true, true, 0, 0, count));
        Assert.Equal(CollectionsWall.EmptyAll, CollectionsWall.EmptyMessage("server", false, false, true, 2, 1, count));
        Assert.Null(CollectionsWall.EmptyMessage("all", true, true, false, 0, 0, count));
        Assert.Null(CollectionsWall.EmptyMessage("server", true, true, false, 0, 0, count));

        // A non-empty set still has an unknown combined count when libraries may overlap.
        var unknown = CollectionsWall.ServerCount([libraries[0], Library(99, "Shows", 1)]);
        Assert.Null(unknown);
        Assert.Null(CollectionsWall.EmptyMessage("all", true, true, true, 0, 0, unknown));
        Assert.Null(CollectionsWall.EmptyMessage("server", true, true, true, 0, 0, unknown));
    }

    [Theory]
    [InlineData("compact", 1280, 10)] [InlineData("compact", 700, 5)] [InlineData("compact", 400, 3)]
    [InlineData("standard", 1280, 8)] [InlineData("standard", 1100, 7)] [InlineData("standard", 800, 5)] [InlineData("standard", 650, 4)] [InlineData("standard", 400, 3)]
    [InlineData("large", 1400, 6)] [InlineData("large", 1100, 5)] [InlineData("large", 800, 4)] [InlineData("large", 660, 3)] [InlineData("large", 400, 2)]
    [InlineData(null, 1100, 7)]
    public void PosterWallColumnsFollowThePosterSizeSetting(string? size, double width, int columns)
        => Assert.Equal(columns, UICustomizationService.PosterColumnCount(size, width));
}
