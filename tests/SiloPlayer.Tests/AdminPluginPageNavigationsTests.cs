using SiloPlayer.Core.Services;

namespace SiloPlayer.Tests;

/// <summary>
/// Which navigation decides the state of an Admin plugin page, driven as the web view reports it: "s1" is
/// NavigationStarting of navigation 1 and "c1" its NavigationCompleted. Navigation ids are synthetic.
/// </summary>
public sealed class AdminPluginPageNavigationsTests
{
    private static readonly AdminPluginPageNavigations.Result Page = new(true, 200);
    private static readonly AdminPluginPageNavigations.Result Fragment = new(true, 0);
    private static readonly AdminPluginPageNavigations.Result Cancelled = new(false, 0);
    private static readonly AdminPluginPageNavigations.Result Missing = new(false, 404);

    [Theory]
    [InlineData("s1 s2 c1 c2")]
    [InlineData("s1 s2 c2 c1")]
    public void AFragmentSetWhileOpeningLeavesTheDecisionToTheOpeningNavigation(string events) =>
        Assert.Equal(Page, Assert.Single(Drive(events, Page, Fragment)));

    // The opening navigation 1 and two fragments the plugin sets at startup, in every order the three can complete
    // in, with the second fragment set before or after the opening navigation or the first fragment completes.
    [Theory]
    [InlineData("s1 s2 s3 c1 c2 c3")]
    [InlineData("s1 s2 s3 c1 c3 c2")]
    [InlineData("s1 s2 s3 c2 c1 c3")]
    [InlineData("s1 s2 s3 c2 c3 c1")]
    [InlineData("s1 s2 s3 c3 c1 c2")]
    [InlineData("s1 s2 s3 c3 c2 c1")]
    [InlineData("s1 s2 c1 s3 c2 c3")]
    [InlineData("s1 s2 c1 s3 c3 c2")]
    [InlineData("s1 s2 c2 s3 c1 c3")]
    [InlineData("s1 s2 c2 s3 c3 c1")]
    public void TwoFragmentsSetWhileOpeningLeaveTheDecisionToTheOpeningNavigation(string events)
    {
        Assert.Equal(Page, Assert.Single(Drive(events, Page, Fragment, Fragment)));
        Assert.Equal(Missing, Assert.Single(Drive(events, Missing, Fragment, Fragment)));
    }

    [Theory]
    [InlineData("s1 s2 s3 s4 c4 c3 c2 c1")]
    [InlineData("s1 s2 s3 s4 c1 c2 c3 c4")]
    [InlineData("s1 s2 c1 s3 c3 s4 c2 c4")]
    public void AnyNumberOfFragmentsSetWhileOpeningLeavesTheDecisionToTheOpeningNavigation(string events) =>
        Assert.Equal(Page, Assert.Single(Drive(events, Page, Fragment, Fragment, Fragment)));

    // The opening document 1 goes on to another document 2 of the plugin, which sets its fragment (3): document 2
    // decides, not the navigation it replaced.
    [Theory]
    [InlineData("s1 s2 c1 s3 c2 c3")]
    [InlineData("s1 s2 c1 s3 c3 c2")]
    [InlineData("s1 s2 s3 c1 c2 c3")]
    [InlineData("s1 s2 s3 c2 c1 c3")]
    [InlineData("s1 s2 s3 c3 c1 c2")]
    [InlineData("s1 s2 s3 c3 c2 c1")]
    public void ANewerDocumentDecidesInsteadOfTheNavigationItReplaced(string events)
    {
        Assert.Equal(Page, Assert.Single(Drive(events, Cancelled, Page, Fragment)));
        Assert.Equal(Missing, Assert.Single(Drive(events, Page, Missing, Fragment)));
    }

    [Fact]
    public void ARedirectIsTheSameNavigation()
    {
        Assert.Equal(Page, Assert.Single(Drive("s1 s1 c1", Page)));
        // Reported again after a newer navigation started, it keeps its place and still decides.
        Assert.Equal(Page, Assert.Single(Drive("s1 s2 s1 c2 c1", Page, Fragment)));
    }

    [Fact]
    public void ANavigationThatNeverStartedDecidesNothing()
    {
        var navigations = new AdminPluginPageNavigations();
        navigations.Started(1, opening: true);
        Assert.Null(navigations.Completed(9, Cancelled, opening: true));
        Assert.Null(navigations.Completed(9, Page, opening: true));
        Assert.True(navigations.Pending == 1);
        Assert.True(navigations.Completed(1, Cancelled, opening: true) == Cancelled);
        Assert.Null(navigations.Pending);
    }

    [Fact]
    public void OnceThePageIsShownTheNewestNavigationDecidesWithItsOwnResult()
    {
        // The page is shown at c1. A fragment navigation then reports itself, and a navigation a newer one took over
        // from reports nothing.
        Assert.Equal(new[] { Page, Fragment, Missing }, Drive("s1 c1 s2 c2 s3 s4 c3 c4", Page, Fragment, Page, Missing));
    }

    [Fact]
    public void ClearedNavigationsDecideNothing()
    {
        var navigations = new AdminPluginPageNavigations();
        navigations.Started(1, opening: true);
        navigations.Started(2, opening: true);
        Assert.Null(navigations.Completed(1, Page, opening: true));
        navigations.Clear();
        Assert.Null(navigations.Pending);
        Assert.Null(navigations.Completed(2, Fragment, opening: true));
        // Nothing of the earlier load is applied to the next one.
        navigations.Started(3, opening: true);
        Assert.True(navigations.Completed(3, Fragment, opening: true) == Fragment);
    }

    /// <summary>Feeds the events as the page does: it is opening until a decision shows it. Navigation n completes with results[n - 1].</summary>
    private static List<AdminPluginPageNavigations.Result> Drive(string events, params AdminPluginPageNavigations.Result[] results)
    {
        var navigations = new AdminPluginPageNavigations();
        var decided = new List<AdminPluginPageNavigations.Result>();
        var shown = false;
        foreach (var step in events.Split(' '))
        {
            var id = (ulong)(step[1] - '0');
            if (step[0] == 's') navigations.Started(id, opening: !shown);
            else if (navigations.Completed(id, results[(int)id - 1], opening: !shown) is { } result)
            {
                decided.Add(result);
                shown |= result.IsSuccess && result.HttpStatusCode is >= 200 and < 400;
            }
        }
        return decided;
    }
}
